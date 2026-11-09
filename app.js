/* Shared offline UI: browser demonstration or the validated native desktop bridge. */
(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const { DEFAULT_SETTINGS, validateSettings, calculate } = window.SalaryModel;
  const desktop = window.NiumaDesktop || { isDesktop: false };
  const isDesktop = desktop.isDesktop;
  let nativeCommittedState = isDesktop ? cloneState(desktop.initialState) : null;
  let nativeConfigured = Boolean(isDesktop && nativeCommittedState.settings && validateSettings(nativeCommittedState.settings).valid);
  let nativeSnapshot = null;
  let pendingSettings = null;
  let pendingReset = false;
  let pendingBreakSave = false;
  let formDirty = false;
  function cloneState(value) { return JSON.parse(JSON.stringify(value || {})); }
  const storageKey = 'niuma-comfort.prototype.settings.v1';
  const privacyKey = 'niuma-comfort.prototype.privacy.v1';
  const sessionsKey = 'niuma-comfort.prototype.breaks.v1';
  const clone = value => JSON.parse(JSON.stringify(value));
  let settings = nativeConfigured ? clone(nativeCommittedState.settings) : clone(DEFAULT_SETTINGS);
  if (!isDesktop) try {
    const saved = JSON.parse(localStorage.getItem(storageKey));
    if (saved && validateSettings(saved).valid) settings = saved;
  } catch { /* Storage can be unavailable in private browsers and file previews. */ }

  let scene = isDesktop ? 'real' : 'working';
  let sceneEpoch = Date.now();
  let sceneDate = new Date(2026, 10, 9, 16, 38, 0);
  let page = 'today';
  let privacy = isDesktop ? nativeCommittedState.privacy === true : false;
  if (!isDesktop) try { privacy = localStorage.getItem(privacyKey) === 'true'; } catch { /* Session fallback. */ }
  let mood = null;
  let quoteIndex = 0;
  let duration = 300;
  let activeBreak = null;
  let breakSessions = isDesktop && Array.isArray(nativeCommittedState.breakSessions) ? clone(nativeCommittedState.breakSessions).slice(0, 100) : [];
  let lastSummaryKey = '';
  if (!isDesktop) try {
    const stored = JSON.parse(localStorage.getItem(sessionsKey));
    if (Array.isArray(stored)) breakSessions = stored.filter(item => item && Number.isFinite(item.seconds) && item.seconds >= 0 && Number.isFinite(item.amount) && item.amount >= 0 && typeof item.date === 'string' && typeof item.at === 'string' && ['demo', 'real'].includes(item.source)).slice(0, 100);
  } catch { /* No valid local history. */ }
  let toastTimeout;
  let mascotClicks = 0;
  let selectedDays = new Set(settings.workDays);
  const week = ['星期日', '星期一', '星期二', '星期三', '星期四', '星期五', '星期六'];
  const shortWeek = ['日', '一', '二', '三', '四', '五', '六'];
  const moodNames = { angry: '有点烦', tired: '累瘫了', sleep: '想摆烂', good: '还不错' };
  // Original adaptations of the supplied report's themes; see docs/report-copy-adaptation.md.
  const quotes = {
    default: [
      '工作只是你的人生支线，别让它抢了主角的戏。',
      '手机会记得充电，你的电量也算数。',
      '这几分钟不必增值，歇着就值。',
      '待办可以有很多条，但你今天只有一个。',
      '下班后的那顿饭、那阵风，也值得认真期待。'
    ],
    angry: [
      '需求又更新了，你不用顺便把内耗也更新。',
      '这事先放一放，我陪你吐槽两句。',
      'KPI 写在表格里，不用刻在脑门上。',
      '需求可以再改，你先喝口水。',
      '这口气先别咽，跟我一起吐槽：又改？'
    ],
    tired: [
      '今天先开省电模式，能量条不是靠自责涨的。',
      '手机都充上电了，也给自己留个充电口。',
      '给下班后的自己，也留一点电。',
      '电量不足就充电，谁家手机靠自责续航啊。',
      '休息不需要先把所有事都做完。'
    ],
    sleep: [
      '发呆五分钟，不用顺便考个证。',
      '今天摸会儿鱼，鱼也没要求你写复盘。',
      '这几分钟不必增值，歇着就值。',
      '偶尔当一颗土豆，也是一种稳定发挥。',
      '允许自己缓一缓，主角也有中场休息。'
    ],
    good: [
      '今天攒到一点开心，晚上记得花掉。',
      '今天状态不错，给下班后的自己留点余量。',
      '有好心情就先收下，不用等周末再开心。',
      '这份小开心，记得带着一起下班。',
      '今天的你，值得下班后加个鸡腿。'
    ]
  };

  function clockNow() {
    return scene === 'real' ? new Date() : new Date(sceneDate.getTime() + Date.now() - sceneEpoch);
  }
  const pad = n => String(n).padStart(2, '0');
  const dayKey = d => `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
  const timeText = d => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
  const money = value => privacy ? '¥****' : `¥${value.toFixed(2)}`;
  function durationText(seconds) {
    const total = Math.max(0, Math.ceil(seconds / 60));
    return `${Math.floor(total / 60)}时${pad(total % 60)}分`;
  }
  function showToast(message) {
    clearTimeout(toastTimeout);
    $('toast').querySelector('span').textContent = message;
    $('toast').hidden = false;
    toastTimeout = setTimeout(() => { $('toast').hidden = true; }, 4200);
  }
  function navigate(target) {
    page = target;
    document.querySelectorAll('.page').forEach(section => { section.hidden = section.id !== `page-${target}`; });
    document.querySelectorAll('[data-page]').forEach(button => {
      const selected = button.dataset.page === target;
      button.classList.toggle('active', selected);
      if (selected) button.setAttribute('aria-current', 'page'); else button.removeAttribute('aria-current');
    });
    document.querySelector('.main-content').scrollTop = 0;
    const name = { today: '今天这班', break: '带薪喘口气', feel: '情绪急救站', settings: '我的打工配置' }[target];
    document.title = `牛马不放器 · ${name}`;
    if (target === 'settings') populateForm();
  }
  function collapse() {
    if (isDesktop) { desktop.send('hide'); return; }
    $('appWindow').hidden = true;
    $('collapsedMessage').hidden = false;
    document.body.classList.remove('panel-open');
    $('taskbarWidget').setAttribute('aria-expanded', 'false');
    $('taskbarWidget').setAttribute('aria-label', '展开牛马不放器');
    $('taskbarWidget').focus();
  }
  function expand() {
    $('appWindow').hidden = false;
    $('collapsedMessage').hidden = true;
    document.body.classList.add('panel-open');
    $('taskbarWidget').setAttribute('aria-expanded', 'true');
    $('taskbarWidget').setAttribute('aria-label', '收起牛马不放器');
  }

  function renderClock() {
    const now = clockNow();
    const data = isDesktop && nativeConfigured && nativeSnapshot ? nativeSnapshot : calculate(settings, now);
    const [whole, cents] = data.earned.toFixed(2).split('.');
    $('earnedWhole').textContent = privacy ? '****' : whole;
    $('earnedCents').textContent = privacy ? '' : `.${cents}`;
    $('hourlyValue').textContent = money(data.hourlySalary);
    $('earningRate').textContent = privacy ? '收益金额已隐藏' : data.status === 'working'
      ? `每分钟 +¥${(settings.monthlySalary / settings.paidDays / data.paidSeconds * 60).toFixed(2)}`
      : { lunch: '午休中 · 暂停增长', before: '到点再开始计薪', after: '已收工 · 停止增长', rest: '休息日 · 暂不计薪' }[data.status];
    $('todayDate').textContent = `${pad(now.getMonth() + 1)} 月 ${pad(now.getDate())} 日 · ${week[now.getDay()]}`;
    $('systemClock').replaceChildren(document.createTextNode(timeText(now)), document.createElement('br'));
    const small = document.createElement('small');
    small.textContent = `${now.getFullYear()}/${pad(now.getMonth() + 1)}/${pad(now.getDate())}`;
    $('systemClock').append(small);
    const pct = Math.round(data.progress * 1000) / 10;
    $('progressPercent').textContent = data.status === 'rest' ? '自由支配' : `${pct.toFixed(1)}%`;
    $('progressFill').style.width = `${pct}%`;
    $('progressBuddy').style.left = `${Math.max(1, Math.min(99, pct))}%`;
    $('startLabel').textContent = data.status === 'rest' ? '今天不打卡' : `${settings.startTime} 上班`;
    $('endLabel').textContent = data.status === 'rest' ? '放心休息' : `${settings.endTime} 下班`;
    const labels = {
      working: ['正在营业', '班可以上，气不能受', '距离下班还有', `${settings.endTime}，把时间还给自己。`, '你认真生活的底气，正在 +1'],
      lunch: ['午休充电中', '吃饱了，才有力气吐槽', '距离下班还有', '收益暂停，饭要好好吃。', '午休不计薪，快乐要照常供应。'],
      before: ['还没到点', '先吃早餐，再接需求', '距离下班还有', `今天 ${settings.startTime} 开工，先不着急。`, '还没上班，先把自己照顾好。'],
      after: ['今日已打烊', '今天辛苦了，该你生活了', '今日已下班', '工作到这里，生活接着来。', '今日收益已封顶，现在轮到你。'],
      rest: ['今天休息', '周末不营业，快乐不打烊', '今天不用打卡', '不用倒数，今天就是你的。', '休息日不计薪，也值得好好过。']
    };
    const copy = labels[data.status];
    $('workStatus').lastChild.textContent = copy[0];
    $('todayHeading').replaceChildren(document.createTextNode(copy[1]));
    const period = document.createElement('span'); period.textContent = '。'; $('todayHeading').append(period);
    $('countdownLabel').textContent = copy[2];
    $('countdownCaption').textContent = copy[3];
    $('earningCaption').textContent = copy[4];
    $('earningLabel').textContent = data.status === 'after' ? '今日收工 · 估算' : '今日已赚 · 估算';
    $('progressTitle').textContent = data.status === 'rest' ? '今天不计算打工进度' : '今日打工进度';
    $('progressMessage').textContent = { working: pct > 50 ? '已经走过一大半了' : '慢慢来，下一分钟也算数', lunch: '午休暂停计薪，安心吃饭', before: '还没开始，先好好吃早餐', after: '今日份辛苦，到此为止', rest: '时间全部归你' }[data.status];
    if (data.status === 'after' || data.status === 'rest') {
      const text = document.createElement('b'); text.textContent = data.status === 'after' ? '下班万岁' : '自由时间'; text.style.fontSize = '25px';
      $('countdownValue').replaceChildren(text);
    } else {
      const minutes = Math.ceil(data.remainingWallSeconds / 60);
      const nodes = [pad(Math.floor(minutes / 60)), '时', pad(minutes % 60), '分'].map((text, i) => {
        const node = document.createElement(i % 2 ? 'small' : 'b'); node.textContent = text; return node;
      });
      $('countdownValue').replaceChildren(...nodes);
    }
    $('widgetAmount').textContent = money(data.earned);
    $('widgetSubtitle').textContent = activeBreak ? `正在喘口气 · ${breakElapsedText()}`
      : data.status === 'after' ? '今日收工 · 下班万岁' : data.status === 'rest' ? '休息日 · 快乐归你'
      : `今日已赚 · ${durationText(data.remainingWallSeconds)}下班`;
    document.body.classList.remove('status-working', 'status-lunch', 'status-before', 'status-after', 'status-rest');
    document.body.classList.add(`status-${data.status}`);
    if (isDesktop && !nativeConfigured) renderUnconfigured();
    renderBreak();
    if (lastSummaryKey !== `${dayKey(now)}:${scene === 'real' ? 'real' : 'demo'}`) renderSummary();
  }

  function renderUnconfigured() {
    $('appWindow').classList.add('unconfigured');
    $('earnedWhole').textContent = '待设置';
    $('earnedCents').textContent = '';
    $('earningLabel').textContent = '填好薪资后开始估算';
    $('hourlyValue').textContent = '待设置';
    $('earningRate').textContent = '不会自动读取你的工资';
    $('earningCaption').textContent = '先把你的上班安排告诉我。';
    $('workStatus').lastChild.textContent = '请先配置';
    $('todayHeading').textContent = '先设置好，再一起等下班。';
    $('countdownLabel').textContent = '下班时间待设置';
    const placeholder = document.createElement('b'); placeholder.textContent = '待设置';
    $('countdownValue').replaceChildren(placeholder);
    $('countdownCaption').textContent = '按你的排班来，不替你猜。';
    $('progressTitle').textContent = '配置完成后开始显示进度';
    $('progressPercent').textContent = '待设置';
    $('progressFill').style.width = '0%'; $('progressBuddy').style.left = '1%';
    $('startLabel').textContent = '上班时间待设置'; $('endLabel').textContent = '下班时间待设置';
    $('progressMessage').textContent = '';
    $('widgetAmount').textContent = '请先设置薪资'; $('widgetSubtitle').textContent = '牛马不放器';
  }

  function setScene(next) {
    if (isDesktop) next = 'real';
    if (activeBreak) finishBreak(true);
    scene = next;
    const moments = sceneMoments();
    if (moments[next]) sceneDate = moments[next];
    sceneEpoch = Date.now();
    $('sceneLabel').textContent = { working: '演示 · 上班中', lunch: '演示 · 午休中', after: '演示 · 已下班', rest: '演示 · 休息日', real: '本机 · 实时时间' }[next];
    closeSceneMenu();
    renderClock();
    renderSummary();
  }
  function sceneMoments() {
    const minute = value => { const [h, m] = value.split(':').map(Number); return h * 60 + m; };
    const start = minute(settings.startTime), end = minute(settings.endTime);
    const ls = settings.lunchEnabled ? minute(settings.lunchStart) : end;
    const le = settings.lunchEnabled ? minute(settings.lunchEnd) : end;
    const offset = [0, 1, 2, 3, 4, 5, 6].find(d => settings.workDays.includes((d + 1) % 7));
    const restOffset = [6, 5, 4, 3, 2, 1, 0].find(d => !settings.workDays.includes((d + 1) % 7));
    const at = (day, minutes) => new Date(2026, 10, 9 + day, 0, minutes, 0);
    let workMinute = start + (end - start - (le - ls)) * 0.8292;
    if (workMinute >= ls) workMinute += le - ls;
    const moments = { working: at(offset, Math.floor(workMinute)), lunch: at(offset, Math.floor((ls + le) / 2)), after: at(offset, end), rest: at(restOffset ?? 6, 10 * 60) };
    // Keep the familiar 09:00–18:00 default demonstration at 16:38 and 18:05.
    if (end + 5 < 1440) moments.after = at(offset, end + 5);
    const setMenu = (name, text, disabled) => { const button = document.querySelector(`[data-scene="${name}"]`); button.querySelector('span').textContent = text; button.disabled = disabled; };
    setMenu('working', timeText(moments.working), false);
    setMenu('lunch', settings.lunchEnabled ? timeText(moments.lunch) : '已关闭', !settings.lunchEnabled);
    setMenu('after', timeText(moments.after), false);
    setMenu('rest', restOffset === undefined ? '每天均工作' : week[moments.rest.getDay()], restOffset === undefined);
    return moments;
  }
  function closeSceneMenu() { $('sceneMenu').hidden = true; $('sceneTrigger').setAttribute('aria-expanded', 'false'); }

  function chooseMood(next, openFeel = false) {
    mood = next; quoteIndex = 0;
    document.querySelectorAll('[data-mood]').forEach(button => {
      const selected = button.dataset.mood === next;
      button.classList.toggle('selected', selected); button.setAttribute('aria-pressed', String(selected));
    });
    updateQuote();
    if (openFeel) navigate('feel');
  }
  function updateQuote() {
    const pool = quotes[mood || 'default'];
    const text = pool[quoteIndex % pool.length];
    $('comfortText').textContent = text;
    $('comfortFrom').textContent = `${mood ? `已接住你的「${moodNames[mood]}」` : '今日精神补给'} / ${pad(quoteIndex % pool.length + 1)}`;
    $('emotionQuote').textContent = text;
    $('emotionTag').textContent = mood ? `收到，你现在「${moodNames[mood]}」` : '今日份情绪接住了';
  }
  function nextQuote() { quoteIndex++; updateQuote(); }

  function breakElapsedSeconds() { return activeBreak ? Math.max(0, (Date.now() - activeBreak.startedReal) / 1000) : 0; }
  function breakElapsedText() {
    const sec = Math.floor(breakElapsedSeconds()); return `${pad(Math.floor(sec / 60))}:${pad(sec % 60)}`;
  }
  // Intersect the rest interval with configured paid hours. This also handles midnight correctly.
  function paidDuring(startDate, endDate, config) {
    if (!config) return 0;
    let seconds = 0;
    const cursor = new Date(startDate.getFullYear(), startDate.getMonth(), startDate.getDate());
    const at = (day, value) => { const [h, m] = value.split(':').map(Number); return new Date(day.getFullYear(), day.getMonth(), day.getDate(), h, m).getTime(); };
    const overlap = (a, b) => Math.max(0, Math.min(b, endDate.getTime()) - Math.max(a, startDate.getTime())) / 1000;
    while (cursor.getTime() <= endDate.getTime()) {
      if (config.workDays.includes(cursor.getDay())) {
        seconds += overlap(at(cursor, config.startTime), at(cursor, config.endTime));
        if (config.lunchEnabled) seconds -= overlap(at(cursor, config.lunchStart), at(cursor, config.lunchEnd));
      }
      cursor.setDate(cursor.getDate() + 1);
    }
    const paidSeconds = calculate(config, startDate).paidSeconds;
    return seconds * config.monthlySalary / config.paidDays / paidSeconds;
  }
  function startOrPauseBreak() {
    if (activeBreak) { finishBreak(false); return; }
    activeBreak = { startedReal: Date.now(), startedClock: clockNow(), settings: isDesktop && !nativeConfigured ? null : clone(settings), notified: false, target: duration };
    document.body.classList.add('break-active'); document.body.classList.remove('break-complete');
    $('timerLabel').textContent = '喘口气中 · 已休息';
    $('startBreak').querySelector('span').textContent = '我歇好了';
    $('startBreak').querySelector('use').setAttribute('href', '#i-check');
    $('durationOptions').querySelectorAll('button').forEach(button => { button.disabled = true; });
    $('timerTip').textContent = '不强制结束，不扣分。休息的节奏由你决定。';
    showToast('摸鱼搭子已就位。先把肩膀放下来，歇会儿。'); renderBreak();
  }
  function finishBreak(silent = false) {
    if (!activeBreak) return;
    const elapsed = breakElapsedSeconds();
    const endedClock = new Date(activeBreak.startedClock.getTime() + elapsed * 1000);
    breakSessions.unshift({ seconds: elapsed, amount: paidDuring(activeBreak.startedClock, endedClock, activeBreak.settings), date: dayKey(activeBreak.startedClock), at: timeText(activeBreak.startedClock), source: scene === 'real' ? 'real' : 'demo' });
    breakSessions = breakSessions.slice(0, 100);
    saveBreakHistory();
    activeBreak = null;
    document.body.classList.remove('break-active', 'break-complete');
    $('timerLabel').textContent = '本次喘气时间';
    $('startBreak').querySelector('span').textContent = '再喘口气';
    $('startBreak').querySelector('use').setAttribute('href', '#i-play');
    $('durationOptions').querySelectorAll('button').forEach(button => { button.disabled = false; });
    $('timerTip').textContent = '到点轻提示，由你决定什么时候回来。';
    if (!silent) showToast('欢迎回来。歇一会儿，也算是在认真照顾自己。');
    renderSummary(); renderClock();
  }
  function renderBreak() {
    const seconds = breakElapsedSeconds();
    $('breakTime').textContent = activeBreak ? breakElapsedText() : '00:00';
    if (activeBreak) {
      const end = new Date(activeBreak.startedClock.getTime() + seconds * 1000);
      const amount = paidDuring(activeBreak.startedClock, end, activeBreak.settings);
      $('breakIncome').textContent = activeBreak.settings ? `这会儿的计薪估算 ${money(amount)} · 已含在今日收益里`
        : '这几分钟留给自己；设置薪资后才估算金额。';
      if (seconds >= activeBreak.target && !activeBreak.notified) {
        activeBreak.notified = true; document.body.classList.add('break-complete');
        $('timerTip').textContent = `${Math.round(activeBreak.target / 60)} 分钟到了，可以回来，也可以再缓一会儿。`;
        showToast('小休息到点啦。你决定什么时候回来。');
      }
    } else $('breakIncome').textContent = '这会儿不必有产出，歇着也算数。';
  }
  function renderSummary() {
    lastSummaryKey = `${dayKey(clockNow())}:${scene === 'real' ? 'real' : 'demo'}`;
    const today = breakSessions.filter(session => session.date === dayKey(clockNow()) && session.source === (scene === 'real' ? 'real' : 'demo'));
    const total = today.reduce((n, session) => n + session.seconds, 0);
    $('totalBreakTime').textContent = total < 60 ? `${Math.floor(total)} 秒` : `${(total / 60).toFixed(1)} 分钟`;
    $('totalBreakCount').textContent = today.length ? `今天主动照顾了自己 ${today.length} 次。很好，继续保持这个习惯。` : '还没开始也没关系，随时可以歇会儿。';
    $('breakHistory').replaceChildren();
    today.slice(0, 8).forEach(session => {
      const row = document.createElement('div'); row.className = 'history-row';
      const at = document.createElement('span'); at.textContent = `${session.at} · 喘口气`;
      const time = document.createElement('span'); time.textContent = `${Math.floor(session.seconds / 60)} 分 ${Math.floor(session.seconds % 60)} 秒`;
      const earned = document.createElement('b'); earned.textContent = `计薪估算 ${money(session.amount)}`;
      row.append(at, time, earned); $('breakHistory').append(row);
    });
  }

  function saveBreakHistory() {
    if (isDesktop) { pendingBreakSave = true; desktop.send('saveBreakSessions', { sessions: breakSessions }); return; }
    try { localStorage.setItem(sessionsKey, JSON.stringify(breakSessions)); }
    catch { showToast('此浏览器不能持久保存，休息记录仅在本次会话中保留。'); }
  }

  function populateForm() {
    const form = $('settingsForm');
    for (const name of ['monthlySalary', 'paidDays', 'startTime', 'endTime', 'lunchStart', 'lunchEnd']) form.elements[name].value = settings[name];
    form.elements.lunchEnabled.checked = settings.lunchEnabled;
    if (isDesktop && !nativeConfigured) form.elements.monthlySalary.value = '';
    if (isDesktop) $('startWithWindows').checked = nativeCommittedState.startWithWindows === true;
    selectedDays = new Set(settings.workDays);
    clearErrors(); renderDayButtons(); updateLunchFields();
    formDirty = false;
  }
  function clearErrors() {
    document.querySelectorAll('[data-error]').forEach(field => { field.textContent = ''; });
    $('settingsForm').querySelectorAll('input').forEach(field => { field.removeAttribute('aria-invalid'); field.removeAttribute('aria-describedby'); });
    $('settingsSaved').textContent = '';
  }
  function renderDayButtons() {
    $('workDays').replaceChildren();
    [1, 2, 3, 4, 5, 6, 0].forEach(day => {
      const button = document.createElement('button'); button.type = 'button'; button.textContent = shortWeek[day];
      button.setAttribute('aria-label', week[day]); button.setAttribute('aria-pressed', String(selectedDays.has(day)));
      button.addEventListener('click', () => {
        formDirty = true;
        if (selectedDays.has(day)) selectedDays.delete(day); else selectedDays.add(day);
        button.setAttribute('aria-pressed', String(selectedDays.has(day)));
      });
      $('workDays').append(button);
    });
  }
  function updateLunchFields() {
    const enabled = $('lunchEnabled').checked;
    $('settingsForm').elements.lunchStart.disabled = !enabled;
    $('settingsForm').elements.lunchEnd.disabled = !enabled;
  }
  function saveSettings(event) {
    event.preventDefault(); clearErrors();
    const form = $('settingsForm');
    const next = {
      monthlySalary: Number(form.elements.monthlySalary.value), paidDays: Number(form.elements.paidDays.value),
      startTime: form.elements.startTime.value, endTime: form.elements.endTime.value,
      lunchEnabled: form.elements.lunchEnabled.checked, lunchStart: form.elements.lunchStart.value,
      lunchEnd: form.elements.lunchEnd.value, workDays: [...selectedDays].sort()
    };
    const result = validateSettings(next);
    if (!result.valid) {
      Object.entries(result.errors).forEach(([name, message]) => {
        const field = document.querySelector(`[data-error="${name}"]`);
        if (field) { field.textContent = message; field.id = `error-${name}`; }
        const input = form.elements[name]; if (input) { input.setAttribute('aria-invalid', 'true'); input.setAttribute('aria-describedby', `error-${name}`); }
      });
      form.querySelector('[aria-invalid="true"]')?.focus();
      showToast('有几处设置需要调整，字段旁边已经写好了。'); return;
    }
    if (activeBreak) finishBreak(true);
    if (isDesktop) {
      pendingSettings = clone(next);
      $('saveSettingsButton').disabled = true;
      $('resetSettings').disabled = true;
      $('settingsSaved').textContent = '正在保存…';
      desktop.send('saveSettings', next);
      return;
    }
    settings = next;
    let saved = true;
    try { localStorage.setItem(storageKey, JSON.stringify(settings)); } catch { saved = false; }
    $('settingsSaved').textContent = saved ? '已保存 ✓' : '本次会话已生效';
    showToast(saved ? '已保存。辛苦钱和下班时间，都安排明白了。' : '设置已生效；此浏览器不能持久保存，关闭后会恢复。');
    if (scene !== 'real') {
      if ((scene === 'rest' && settings.workDays.length === 7) || (scene === 'lunch' && !settings.lunchEnabled)) scene = 'working';
      setScene(scene);
    } else renderClock();
  }

  function openDialog(kind) {
    const content = kind === 'formula' ? `<h2>这份辛苦钱，怎么算？</h2><p>这是一笔按时间估算的收入，不代表真实到账。</p><div class="dialog-detail">参考日薪 = 月薪 ÷ 月计薪天数<br>参考时薪 = 参考日薪 ÷ 当日净计薪小时数<br>今日已赚 = 参考日薪 × 已过计薪时间 ÷ 当日净计薪时间</div><p>开启午休不计薪后，午休期间收益暂停。下班后封顶，休息日归零。“距下班”按实际时钟倒数；打工进度按计薪时间计算。</p><p class="dialog-foot">当前原型暂不包含税费、社保、节假日调休、请假、加班或跨夜班。示例月薪为虚构演示数据。</p>`
      : `<h2>给打工人的一点精神补给</h2><p>核心场景：工作烦、想放弃时，抬眼看到收益在涨，下班在靠近，点一下能有人替你吐槽。</p><h3>这版原型的三个取舍</h3><ul><li>平时安静做小挂件，需要时展开；金额随时能隐藏。</li><li>情绪按钮即时回应，休息主动开始，由自己结束。</li><li>沿用原有 Windows 计薪逻辑，先把这一个安慰瞬间做好。</li></ul><h3>调研参考 · 2026-11-09</h3><p><a href="https://github.com/cass-2003/Floatly" target="_blank" rel="noopener noreferrer">Floatly</a> / 紧凑桌面形态<br><a href="https://github.com/huaixu016/DeskBuddy" target="_blank" rel="noopener noreferrer">DeskBuddy</a> / 收益和陪伴结合<br><a href="https://github.com/hovancik/stretchly" target="_blank" rel="noopener noreferrer">Stretchly</a> / 可跳过、由用户掌控的休息</p><p class="dialog-foot">公开资料调研，未进行真人访谈。界面、图形和文案为本原型原创。当前为本地浏览器演示，原生任务栏接入、系统托盘和开机启动待 Windows 开发阶段实现。</p><p><a href="docs/research.md" target="_blank">查看完整调研记录 ↗</a></p>`;
    $('dialogEyebrow').textContent = kind === 'formula' ? 'SALARY ESTIMATE' : 'DESIGN NOTES';
    $('dialogContent').innerHTML = isDesktop && kind !== 'formula'
      ? '<h2>牛马不放器</h2><p>给打工人的一点精神补给。工资按你的排班估算，下班时间按本机时钟倒数；烦的时候，也能来这里吐槽两句、揉一张纸。</p><h3>在任务栏里，安静陪你</h3><p>设置和已结束的休息记录只保存到本地。不用登录，不上传薪资；烦恼纸上的文字完成或关闭后就清空。</p><p>点击任务栏里的收益入口打开面板，再点一次收起。任务栏暂时无法显示入口时，可从系统托盘恢复面板。随 Windows 启动由你主动开启；彻底退出请使用托盘菜单。</p><h3>关于估算</h3><p>收益不是实际到账。午休不计薪时暂停增长，下班封顶，休息日归零。当前不含税费、请假、调休、加班或跨夜班。</p>'
      : content;
    if (isDesktop && kind === 'formula') {
      const foot = $('dialogContent').querySelector('.dialog-foot');
      if (foot) foot.textContent = '当前暂不包含税费、社保、节假日调休、请假、加班或跨夜班。配置和估算只在本机使用。';
    }
    $('infoDialog').showModal();
  }

  function sameSettings(left, right) {
    if (!left || !right) return left === right;
    if (!Array.isArray(left.workDays) || !Array.isArray(right.workDays)) return false;
    return ['monthlySalary', 'paidDays', 'startTime', 'endTime', 'lunchEnabled', 'lunchStart', 'lunchEnd']
      .every(key => left[key] === right[key]) && [...left.workDays].sort().join(',') === [...right.workDays].sort().join(',');
  }
  function applyNativeState(next) {
    if (!next || typeof next !== 'object') return;
    const validSettings = next.settings && validateSettings(next.settings).valid ? next.settings : null;
    const changedSettings = !sameSettings(nativeCommittedState.settings || null, validSettings);
    const confirmedSave = pendingSettings && sameSettings(pendingSettings, validSettings);
    const confirmedReset = pendingReset && !validSettings;
    nativeCommittedState = clone(next);
    nativeCommittedState.settings = validSettings;
    nativeConfigured = Boolean(validSettings);
    settings = validSettings ? clone(validSettings) : clone(DEFAULT_SETTINGS);
    privacy = next.privacy === true;
    if (Array.isArray(next.breakSessions)) breakSessions = clone(next.breakSessions).slice(0, 100);
    pendingBreakSave = false;
    $('appWindow').classList.toggle('unconfigured', !nativeConfigured);
    if (changedSettings) nativeSnapshot = null;
    $('privacyButton').disabled = false;
    $('startWithWindows').disabled = false;
    $('startWithWindows').checked = next.startWithWindows === true;
    if (confirmedSave || confirmedReset || (changedSettings && !formDirty)) populateForm();
    if (confirmedSave) {
      pendingSettings = null;
      formDirty = false;
      $('saveSettingsButton').disabled = false;
      $('resetSettings').disabled = false;
      $('settingsSaved').textContent = '已保存在本机 ✓';
      showToast('已保存在本机。按你的安排，慢慢等下班。');
    }
    if (confirmedReset) {
      pendingReset = false;
      $('resetSettings').disabled = false;
      showToast('薪资配置已清除，需要时再填。');
    }
    updatePrivacyUI(); renderClock(); renderSummary();
  }
  if (isDesktop) {
    document.addEventListener('niuma:state', event => applyNativeState(event.detail));
    document.addEventListener('niuma:snapshot', event => {
      const message = event.detail;
      if (typeof message?.configured === 'boolean') nativeConfigured = message.configured;
      const snapshot = message?.snapshot;
      const statuses = ['working', 'lunch', 'before', 'after', 'rest'];
      if (snapshot && statuses.includes(snapshot.status) && ['earned', 'dailySalary', 'hourlySalary', 'paidSeconds',
        'elapsedPaidSeconds', 'remainingPaidSeconds', 'remainingWallSeconds', 'progress'].every(key => Number.isFinite(snapshot[key]))) {
        nativeSnapshot = snapshot;
      }
      renderClock();
    });
    document.addEventListener('niuma:error', event => {
      pendingSettings = null; pendingReset = false;
      $('saveSettingsButton').disabled = false; $('resetSettings').disabled = false;
      $('privacyButton').disabled = false; $('startWithWindows').disabled = false;
      $('startWithWindows').checked = nativeCommittedState.startWithWindows === true;
      $('settingsSaved').textContent = '未保存';
      if (pendingBreakSave) {
        breakSessions = Array.isArray(nativeCommittedState.breakSessions) ? clone(nativeCommittedState.breakSessions) : [];
        pendingBreakSave = false; renderSummary();
      }
      showToast(typeof event.detail === 'string' ? event.detail : '这次操作没有保存，请重试。');
    });
    document.addEventListener('click', event => {
      const anchor = event.target.closest('a[href]');
      if (!anchor || anchor.getAttribute('href').startsWith('#')) return;
      event.preventDefault();
      let target;
      try { target = new URL(anchor.href); } catch { return; }
      if (target.protocol === 'https:') desktop.send('openExternal', { url: target.href });
      else showToast('这里的内容已随软件离线提供。');
    });
    $('startWithWindows').addEventListener('change', () => {
      const requested = $('startWithWindows').checked;
      $('startWithWindows').checked = nativeCommittedState.startWithWindows === true;
      $('startWithWindows').disabled = true;
      desktop.send('setStartup', { value: requested });
    });
    $('openDataFolder').addEventListener('click', () => desktop.send('openDataFolder'));
    $('nativeVersion').textContent = `牛马不放器 ${desktop.version} · 离线陪伴`;
    document.querySelector('.window-title').lastChild.textContent = `牛马不放器 · ${desktop.version}`;
    $('storageDescription').textContent = '不用登录，薪资与休息记录只保存在这台电脑。烦恼纸上的文字不会保存或上传。';
    $('resetSettings').textContent = '清除薪资配置';
    const salaryFields = new Set(['monthlySalary', 'paidDays', 'startTime', 'endTime', 'lunchEnabled', 'lunchStart', 'lunchEnd']);
    const markSalaryDraft = event => { if (salaryFields.has(event.target.name)) formDirty = true; };
    $('settingsForm').addEventListener('input', markSalaryDraft);
    $('settingsForm').addEventListener('change', markSalaryDraft);
  }

  document.querySelectorAll('[data-page]').forEach(button => button.addEventListener('click', () => navigate(button.dataset.page)));
  document.querySelectorAll('[data-scene]').forEach(button => button.addEventListener('click', () => setScene(button.dataset.scene)));
  document.querySelectorAll('[data-mood]').forEach(button => { button.setAttribute('aria-pressed', 'false'); button.addEventListener('click', () => chooseMood(button.dataset.mood, false)); });
  $('sceneTrigger').addEventListener('click', () => { const open = $('sceneMenu').hidden; $('sceneMenu').hidden = !open; $('sceneTrigger').setAttribute('aria-expanded', String(open)); });
  document.addEventListener('click', event => { if (!event.target.closest('.scene-picker')) closeSceneMenu(); });
  $('privacyButton').addEventListener('click', () => {
    if (isDesktop) { $('privacyButton').disabled = true; desktop.send('setPrivacy', { value: !privacy }); return; }
    privacy = !privacy;
    try { localStorage.setItem(privacyKey, String(privacy)); } catch { /* Session fallback. */ }
    updatePrivacyUI();
    renderClock(); renderSummary();
  });
  function updatePrivacyUI() {
    document.body.classList.toggle('privacy-on', privacy);
    $('privacyButton').setAttribute('aria-label', privacy ? '显示金额' : '隐藏金额');
    $('privacyButton').title = privacy ? '显示金额' : '隐藏金额';
    $('privacyButton').querySelector('use').setAttribute('href', privacy ? '#i-eye-off' : '#i-eye');
  }
  ['minimizeButton', 'closeButton', 'collapseLink'].forEach(id => $(id).addEventListener('click', collapse));
  $('taskbarWidget').addEventListener('click', () => { if ($('appWindow').hidden) expand(); else collapse(); });
  $('reopenButton').addEventListener('click', expand);
  $('quickBreak').addEventListener('click', () => navigate('break'));
  $('goRest').addEventListener('click', () => navigate('break'));
  $('newQuote').addEventListener('click', nextQuote);
  $('feelNewQuote').addEventListener('click', nextQuote);
  $('startBreak').addEventListener('click', startOrPauseBreak);
  $('clearBreaks').addEventListener('click', () => {
    const key = dayKey(clockNow()); const source = scene === 'real' ? 'real' : 'demo';
    breakSessions = breakSessions.filter(session => session.date !== key || session.source !== source);
    saveBreakHistory(); renderSummary(); showToast('已清除今天的休息记录，轻装上阵。');
  });
  $('finishBreak').addEventListener('click', () => finishBreak());
  document.querySelectorAll('[data-duration]').forEach(button => button.addEventListener('click', () => {
    duration = Number(button.dataset.duration);
    document.querySelectorAll('[data-duration]').forEach(item => { item.classList.toggle('selected', item === button); item.setAttribute('aria-pressed', String(item === button)); });
    $('timerTip').textContent = `${duration / 60} 分钟后轻提示，由你决定什么时候回来。`;
  }));
  document.querySelectorAll('[data-rest]').forEach(button => button.addEventListener('click', () => {
    const messages = { water: '去接杯水吧，顺便离开屏幕一小会儿。', shoulder: '吸口气，把肩膀轻轻放下来。没必要一直绷着。', window: '看会儿窗外，云今天也没交周报。' };
    showToast(messages[button.dataset.rest]);
  }));
  $('lunchEnabled').addEventListener('change', updateLunchFields);
  $('settingsForm').addEventListener('submit', saveSettings);
  $('resetSettings').addEventListener('click', () => {
    if (activeBreak) finishBreak(true);
    if (isDesktop) {
      pendingReset = true; $('resetSettings').disabled = true;
      desktop.send('resetSettings'); return;
    }
    settings = clone(DEFAULT_SETTINGS);
    try { localStorage.removeItem(storageKey); } catch { /* Current session remains usable. */ }
    populateForm(); setScene('working'); showToast('已恢复虚构的演示设置。');
  });
  $('formulaOpen').addEventListener('click', () => openDialog('formula'));
  $('researchOpen').addEventListener('click', () => openDialog('research'));
  $('dialogClose').addEventListener('click', () => $('infoDialog').close());
  $('infoDialog').addEventListener('click', event => { if (event.target === $('infoDialog')) { const r = $('infoDialog').getBoundingClientRect(); if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) $('infoDialog').close(); } });
  $('mascotButton').addEventListener('click', () => {
    mascotClicks++;
    const whispers = ['摸鱼搭子已就位。', '手机充电，你也充电。', '这会儿不生成待办。', '班味先放一边。', '再摸一下，快乐就 +1。'];
    $('mascotWhisper').textContent = whispers[(mascotClicks - 1) % whispers.length];
    $('mascotButton').classList.remove('mascot-bounce'); void $('mascotButton').offsetWidth; $('mascotButton').classList.add('mascot-bounce');
    if (mascotClicks === 5) showToast('隐藏成就：摸了五下小牛马。今天也要对自己温柔一点。');
  });
  $('crumpleButton').addEventListener('click', () => window.PaperPlay.open(document.activeElement));
  document.addEventListener('paper:rest', () => {
    navigate('break');
    if (!activeBreak) {
      duration = 300;
      document.querySelectorAll('[data-duration]').forEach(button => {
        const selected = Number(button.dataset.duration) === duration;
        button.classList.toggle('selected', selected);
        button.setAttribute('aria-pressed', String(selected));
      });
      startOrPauseBreak();
    }
    $('startBreak').focus({ preventScroll: true });
  });
  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    if ($('paperOverlay').open) return;
    if ($('infoDialog').open) return;
    if (!$('sceneMenu').hidden) { closeSceneMenu(); $('sceneTrigger').focus(); return; }
    if (!$('appWindow').hidden) collapse();
  });
  document.querySelector('.prototype-brand').addEventListener('click', event => { event.preventDefault(); expand(); navigate('today'); });
  populateForm(); updatePrivacyUI(); setScene(isDesktop ? 'real' : 'working');
  if (isDesktop) {
    if (!nativeConfigured) navigate('settings');
    desktop.send('ready');
  }
  setInterval(renderClock, 1000);
  // Re-render after a browser tab sleeps rather than letting a relative counter drift.
  document.addEventListener('visibilitychange', () => { if (!document.hidden) { renderClock(); renderSummary(); } });
})();

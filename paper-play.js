/* A small, local-only paper ritual. Written thoughts are never saved or sent. */
(() => {
  'use strict';

  const $ = id => document.getElementById(id);
  const dialog = $('paperOverlay');
  const toy = $('paperToy');
  const bin = $('paperBin');
  const thought = $('paperThought');
  const timers = new Set();
  const workStages = new Set(['crumple', 'ready', 'throwing', 'landed']);
  // Original copy adapted from self-care and shared-humor themes, not copied post text.
  const responses = [
    ['给下班后的自己，留一点电。', '需求可能还在，但这一刻可以不带着它。接下来几分钟，先留给自己。'],
    ['咚。今天的烦，先下线。', '不用连你自己也一起耗尽。喝口水，松松肩膀，再决定下一步。'],
    ['这张纸，不用交周报。', '纸篓也不催进度。想再丢一张，或者安静发会儿呆，都行。'],
    ['这一张，就到这里。', '今天少带一份内耗下班。还想吐槽就再来一张，不想说了就去歇会儿。']
  ];

  let stage = 'write';
  let squeeze = 0;
  let completed = 0;
  let trigger = null;
  let generation = 0;
  let drag = null;
  let suppressClick = false;
  let lastResponse = -1;

  function later(callback, delay) {
    const current = generation;
    const handle = setTimeout(() => {
      timers.delete(handle);
      if (current === generation && dialog.open) callback();
    }, delay);
    timers.add(handle);
    return handle;
  }

  function releasePointer(pointerId) {
    try { if (toy.hasPointerCapture(pointerId)) toy.releasePointerCapture(pointerId); }
    catch { /* A cancelled pointer can already have released its capture. */ }
  }

  function clearMotion() {
    generation++;
    timers.forEach(clearTimeout);
    timers.clear();
    if (drag) {
      const pointerId = drag.pointerId;
      drag = null;
      releasePointer(pointerId);
    }
    suppressClick = false;
    toy.classList.remove('throwing', 'dragging', 'returning', 'squeezing');
    bin.classList.remove('is-over', 'caught');
    ['--paper-drag-x', '--paper-drag-y', '--paper-throw-x', '--paper-throw-y'].forEach(name => toy.style.setProperty(name, '0px'));
  }

  function transition(next) {
    stage = next;
    dialog.dataset.stage = next;
    $('paperWrite').hidden = next !== 'write';
    $('paperPlay').hidden = !workStages.has(next);
    $('paperResult').hidden = next !== 'done';
    $('paperThrow').hidden = next !== 'ready';
    $('paperThrow').disabled = next !== 'ready';
    toy.disabled = next !== 'crumple' && next !== 'ready';
    const step = next === 'write' ? '01 / 写下来'
      : next === 'crumple' ? '02 / 揉一揉'
        : next === 'done' ? '给自己留一点空地' : '03 / 丢掉它';
    $('paperStep').textContent = step;
    $('paperCount').textContent = completed ? `这次已丢 ${completed} 张` : '不计分，不留存';
    if (next === 'write' || next === 'crumple' || next === 'done') {
      dialog.querySelector('.paper-flow-content').scrollTop = 0;
    }
  }

  function updateCharacters() {
    $('paperChars').textContent = `${thought.value.length} / 120`;
  }

  function focusWrite() {
    // Opening a quiet diversion should not force a phone keyboard onto the screen.
    const target = window.matchMedia('(pointer: coarse)').matches ? $('paperStart') : thought;
    target.focus({ preventScroll: true });
  }

  function resetSheet() {
    clearMotion();
    squeeze = 0;
    dialog.dataset.squeeze = '0';
    thought.value = '';
    $('paperPreview').textContent = '';
    updateCharacters();
    $('paperSqueezeMeter').querySelectorAll('i').forEach(dot => {
      dot.classList.remove('complete');
      dot.setAttribute('aria-hidden', 'true');
    });
    $('paperTitle').textContent = '把眼前的烦，先放到纸上。';
    $('paperSubtitle').textContent = '写一句、选一句，或者什么都不写。都可以。';
    $('paperHint').textContent = '写下的字只在这张纸上，关掉就清空。';
    toy.setAttribute('aria-label', '揉一揉这张纸');
    transition('write');
  }

  function open(origin) {
    trigger = origin instanceof HTMLElement ? origin : document.activeElement;
    completed = 0;
    resetSheet();
    dialog.hidden = false;
    if (!dialog.open) dialog.showModal();
    focusWrite();
  }

  function close() {
    clearMotion();
    thought.value = '';
    $('paperPreview').textContent = '';
    updateCharacters();
    dialog.hidden = true;
    if (dialog.open) dialog.close();
    if (trigger && trigger.isConnected && typeof trigger.focus === 'function') trigger.focus({ preventScroll: true });
  }

  function squeezePaper() {
    if (stage !== 'crumple') return;
    squeeze = Math.min(3, squeeze + 1);
    dialog.dataset.squeeze = String(squeeze);
    $('paperSqueezeMeter').querySelectorAll('i').forEach((dot, index) => dot.classList.toggle('complete', index < squeeze));
    toy.classList.remove('squeezing');
    void toy.offsetWidth;
    toy.classList.add('squeezing');
    later(() => toy.classList.remove('squeezing'), 220);

    if (squeeze === 3) {
      transition('ready');
      $('paperTitle').textContent = '揉好了。给它找个去处。';
      $('paperSubtitle').textContent = '拖进下面的纸篓，或者点一下「帮我丢进去」。';
      $('paperHint').textContent = '抓住纸团，往纸篓里一丢。';
      toy.setAttribute('aria-label', '可拖动的纸团；也可以按 Tab 选择帮我丢进去');
    } else {
      $('paperTitle').textContent = squeeze === 1 ? '先揉一下，班味就轻一点。' : '再揉一下，谁还不是纸老虎。';
      $('paperSubtitle').textContent = '点点这张纸，让它慢慢皱起来。';
      $('paperHint').textContent = squeeze === 1 ? '再揉两下就成团了。' : '最后一下，揉成一团就好。';
      toy.setAttribute('aria-label', `再揉一下纸，已完成 ${squeeze} 次，共 3 次`);
    }
  }

  function startPaper() {
    if (stage !== 'write') return;
    $('paperPreview').textContent = thought.value.trim() || '今天这些烦心事，先到这里。';
    transition('crumple');
    squeezePaper();
    toy.focus({ preventScroll: true });
  }

  function finishThrow() {
    completed++;
    const index = lastResponse < 0 ? Math.floor(Math.random() * responses.length)
      : (lastResponse + 1 + Math.floor(Math.random() * (responses.length - 1))) % responses.length;
    lastResponse = index;
    $('paperResultTag').textContent = '叮，给自己腾出一点空间';
    $('paperResultTitle').textContent = responses[index][0];
    $('paperResultCopy').textContent = responses[index][1];
    $('paperTitle').textContent = '丢完这一张，先歇一会儿。';
    $('paperSubtitle').textContent = '不用马上振作。下一步，选个对自己轻一点的。';
    transition('done');
    // The thought leaves the interface along with its paper.
    thought.value = '';
    $('paperPreview').textContent = '';
    updateCharacters();
    $('paperAgain').focus({ preventScroll: true });
  }

  function throwPaper() {
    if (stage !== 'ready') return;
    const box = toy.getBoundingClientRect();
    const target = bin.getBoundingClientRect();
    const dx = parseFloat(toy.style.getPropertyValue('--paper-drag-x')) || 0;
    const dy = parseFloat(toy.style.getPropertyValue('--paper-drag-y')) || 0;
    // Endpoints are offsets from the undragged origin. CSS animates from the current drag offset.
    toy.style.setProperty('--paper-throw-x', `${target.left + target.width / 2 - (box.left + box.width / 2 - dx)}px`);
    toy.style.setProperty('--paper-throw-y', `${target.top + target.height * .35 - (box.top + box.height / 2 - dy)}px`);
    bin.classList.remove('is-over');
    toy.classList.remove('dragging', 'returning', 'squeezing');
    transition('throwing');
    toy.classList.add('throwing');
    $('paperHint').textContent = '走你——';
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    later(() => {
      transition('landed');
      bin.classList.add('caught');
      $('paperHint').textContent = '咚，接住了。';
      later(finishThrow, reduced ? 180 : 650);
    }, reduced ? 120 : 760);
  }

  function isOverBin(x, y) {
    const target = bin.getBoundingClientRect();
    return x >= target.left - 20 && x <= target.right + 20 && y >= target.top - 20 && y <= target.bottom + 20;
  }

  function resetDragPosition() {
    toy.classList.remove('dragging');
    bin.classList.remove('is-over');
    toy.classList.add('returning');
    toy.style.setProperty('--paper-drag-x', '0px');
    toy.style.setProperty('--paper-drag-y', '0px');
    later(() => toy.classList.remove('returning'), 300);
  }

  function cancelDrag() {
    if (!drag) return;
    const pointerId = drag.pointerId;
    drag = null;
    releasePointer(pointerId);
    resetDragPosition();
  }

  thought.addEventListener('input', updateCharacters);
  document.querySelectorAll('[data-paper-prompt]').forEach(button => button.addEventListener('click', () => {
    if (stage !== 'write') return;
    thought.value = (button.dataset.paperPrompt || button.textContent.trim()).slice(0, 120);
    updateCharacters();
    thought.focus({ preventScroll: true });
  }));

  $('paperStart').addEventListener('click', startPaper);
  toy.style.touchAction = 'none';
  toy.draggable = false;
  toy.setAttribute('aria-describedby', 'paperHint');
  toy.addEventListener('click', event => {
    if (suppressClick && event.detail !== 0) { suppressClick = false; return; }
    if (stage === 'crumple') squeezePaper();
    else if (stage === 'ready') {
      toy.classList.remove('squeezing');
      void toy.offsetWidth;
      toy.classList.add('squeezing');
      later(() => toy.classList.remove('squeezing'), 220);
      $('paperHint').textContent = '已经够皱啦。往下面一丢，或者让我帮你。';
    }
  });
  toy.addEventListener('pointerdown', event => {
    if (stage !== 'ready' || drag || !event.isPrimary || event.button !== 0) return;
    toy.classList.remove('returning');
    const box = toy.getBoundingClientRect();
    drag = { pointerId: event.pointerId, startX: event.clientX, startY: event.clientY, box, dx: 0, dy: 0, moved: false };
    try { toy.setPointerCapture(event.pointerId); } catch { drag = null; }
  });
  toy.addEventListener('pointermove', event => {
    if (!drag || drag.pointerId !== event.pointerId || stage !== 'ready') return;
    const rawX = event.clientX - drag.startX;
    const rawY = event.clientY - drag.startY;
    if (!drag.moved && Math.hypot(rawX, rawY) < 7) return;
    event.preventDefault();
    drag.moved = true;
    const arena = $('paperStageArena').getBoundingClientRect();
    drag.dx = Math.max(arena.left - drag.box.left + 3, Math.min(rawX, arena.right - drag.box.right - 3));
    drag.dy = Math.max(arena.top - drag.box.top + 3, Math.min(rawY, arena.bottom - drag.box.bottom - 3));
    toy.classList.add('dragging');
    toy.style.setProperty('--paper-drag-x', `${drag.dx}px`);
    toy.style.setProperty('--paper-drag-y', `${drag.dy}px`);
    bin.classList.toggle('is-over', isOverBin(drag.box.left + drag.box.width / 2 + drag.dx, drag.box.top + drag.box.height / 2 + drag.dy));
  });
  toy.addEventListener('pointerup', event => {
    if (!drag || drag.pointerId !== event.pointerId) return;
    const released = drag;
    drag = null;
    releasePointer(event.pointerId);
    if (!released.moved) return;
    suppressClick = true;
    later(() => { suppressClick = false; }, 450);
    const centerX = released.box.left + released.box.width / 2 + released.dx;
    const centerY = released.box.top + released.box.height / 2 + released.dy;
    if (isOverBin(centerX, centerY)) throwPaper();
    else {
      resetDragPosition();
      $('paperHint').textContent = '差一点，纸篓又不会跑，再试一次。';
    }
  });
  toy.addEventListener('pointercancel', cancelDrag);
  toy.addEventListener('lostpointercapture', cancelDrag);
  window.addEventListener('resize', () => { if (drag) cancelDrag(); });
  $('paperThrow').addEventListener('click', throwPaper);
  $('paperAgain').addEventListener('click', () => {
    if (stage !== 'done') return;
    resetSheet();
    focusWrite();
  });
  $('paperRest').addEventListener('click', () => {
    if (stage !== 'done') return;
    close();
    document.dispatchEvent(new CustomEvent('paper:rest'));
  });
  $('paperDone').addEventListener('click', close);
  $('paperClose').addEventListener('click', close);
  dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
  dialog.addEventListener('close', () => { if (!dialog.open && !dialog.hidden) close(); });

  window.PaperPlay = Object.freeze({ open, close });
})();

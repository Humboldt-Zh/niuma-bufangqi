/* Original, dependency-free salary estimate model. Times use the device's local timezone. */
(function (root) {
  'use strict';

  const DEFAULT_SETTINGS = Object.freeze({
    monthlySalary: 12000,
    paidDays: 22,
    startTime: '09:00',
    endTime: '18:00',
    lunchEnabled: true,
    lunchStart: '12:00',
    lunchEnd: '13:00',
    workDays: Object.freeze([1, 2, 3, 4, 5])
  });

  function timeSeconds(value) {
    if (typeof value !== 'string' || !/^([01]\d|2[0-3]):[0-5]\d$/.test(value)) return NaN;
    const [hours, minutes] = value.split(':').map(Number);
    return hours * 3600 + minutes * 60;
  }

  function positiveNumber(value) {
    return typeof value === 'number' && Number.isFinite(value) && value > 0;
  }

  function validateSettings(settings) {
    const errors = {};
    if (!settings || typeof settings !== 'object' || Array.isArray(settings)) {
      return { valid: false, errors: { settings: '请填写薪资和工作安排' } };
    }
    if (!positiveNumber(settings.monthlySalary) || settings.monthlySalary > 10000000) {
      errors.monthlySalary = '月薪需为大于 0 且不超过 10,000,000 元的有效数字';
    }
    if (!positiveNumber(settings.paidDays) || settings.paidDays > 31) {
      errors.paidDays = '每月计薪日需为大于 0 且不超过 31 天的有效数字';
    }

    const start = timeSeconds(settings.startTime);
    const end = timeSeconds(settings.endTime);
    if (!Number.isFinite(start)) errors.startTime = '请使用 HH:mm 格式填写上班时间';
    if (!Number.isFinite(end)) errors.endTime = '请使用 HH:mm 格式填写下班时间';
    if (Number.isFinite(start) && Number.isFinite(end) && end <= start) {
      errors.endTime = '下班时间需晚于上班时间，暂不支持跨夜班';
    }
    if (typeof settings.lunchEnabled !== 'boolean') errors.lunchEnabled = '请选择是否扣除午休';
    if (settings.lunchEnabled === true) {
      const lunchStart = timeSeconds(settings.lunchStart);
      const lunchEnd = timeSeconds(settings.lunchEnd);
      if (!Number.isFinite(lunchStart)) errors.lunchStart = '请使用 HH:mm 格式填写午休开始时间';
      if (!Number.isFinite(lunchEnd)) errors.lunchEnd = '请使用 HH:mm 格式填写午休结束时间';
      if (Number.isFinite(lunchStart) && Number.isFinite(lunchEnd)) {
        if (lunchEnd <= lunchStart) errors.lunchEnd = '午休结束时间需晚于开始时间';
        if (Number.isFinite(start) && lunchStart < start) errors.lunchStart = '午休需在工作时段内';
        if (Number.isFinite(end) && lunchEnd > end) errors.lunchEnd = '午休需在工作时段内';
        if (Number.isFinite(start) && Number.isFinite(end) && end > start && lunchEnd > lunchStart &&
            lunchEnd - lunchStart >= end - start) {
          errors.lunchEnd = '午休扣除后需保留有效计薪时间';
        }
      }
    }
    if (!Array.isArray(settings.workDays) || settings.workDays.length === 0 ||
        !settings.workDays.every(day => Number.isInteger(day) && day >= 0 && day <= 6)) {
      errors.workDays = '请至少选择一个有效工作日';
    }
    return { valid: Object.keys(errors).length === 0, errors };
  }

  function money(value) {
    return Math.round((value + Number.EPSILON) * 100) / 100;
  }

  function calculate(settings, date = new Date()) {
    const validation = validateSettings(settings);
    if (!validation.valid) {
      const error = new RangeError('薪资设置无效');
      error.errors = validation.errors;
      throw error;
    }
    if (!(date instanceof Date) || !Number.isFinite(date.getTime())) {
      throw new TypeError('请提供有效的 Date');
    }

    const start = timeSeconds(settings.startTime);
    const end = timeSeconds(settings.endTime);
    const lunchStart = settings.lunchEnabled ? timeSeconds(settings.lunchStart) : end;
    const lunchEnd = settings.lunchEnabled ? timeSeconds(settings.lunchEnd) : end;
    const paidSeconds = end - start - (lunchEnd - lunchStart);
    const dailyRate = settings.monthlySalary / settings.paidDays;
    const now = date.getHours() * 3600 + date.getMinutes() * 60 + date.getSeconds() + date.getMilliseconds() / 1000;
    const isWorkday = settings.workDays.includes(date.getDay());

    let status = 'rest';
    let elapsedPaidSeconds = 0;
    if (isWorkday) {
      status = now < start ? 'before' : now >= end ? 'after'
        : settings.lunchEnabled && now >= lunchStart && now < lunchEnd ? 'lunch' : 'working';
      const cappedNow = Math.min(end, Math.max(start, now));
      const elapsedLunch = settings.lunchEnabled ? Math.max(0, Math.min(cappedNow, lunchEnd) - lunchStart) : 0;
      elapsedPaidSeconds = Math.min(paidSeconds, Math.max(0, cappedNow - start - elapsedLunch));
    }
    const progress = elapsedPaidSeconds / paidSeconds;

    return {
      status,
      earned: money(dailyRate * progress),
      dailySalary: money(dailyRate),
      hourlySalary: money(dailyRate / paidSeconds * 3600),
      paidSeconds,
      elapsedPaidSeconds,
      remainingPaidSeconds: isWorkday ? paidSeconds - elapsedPaidSeconds : 0,
      remainingWallSeconds: isWorkday ? Math.max(0, end - now) : 0,
      progress
    };
  }

  const api = Object.freeze({ DEFAULT_SETTINGS, validateSettings, calculate });
  root.SalaryModel = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(globalThis);

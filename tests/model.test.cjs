const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { DEFAULT_SETTINGS, validateSettings, calculate } = require('../model.js');

const settings = overrides => ({ ...DEFAULT_SETTINGS, workDays: [...DEFAULT_SETTINGS.workDays], ...overrides });
// 2026-11-09 is Monday. Local construction keeps tests independent of machine timezone.
const monday = (hours, minutes = 0, seconds = 0) => new Date(2026, 10, 9, hours, minutes, seconds);

test('valid defaults and browser/global exposure', () => {
  assert.deepEqual(validateSettings(settings()), { valid: true, errors: {} });
  assert.equal(globalThis.SalaryModel.calculate, calculate);
  const browser = vm.createContext({});
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../model.js'), 'utf8'), browser);
  assert.equal(typeof browser.SalaryModel.calculate, 'function');
  assert.equal(browser.SalaryModel.DEFAULT_SETTINGS.monthlySalary, 12000);
});

test('before 09:00 no earned salary and all paid time remains', () => {
  const result = calculate(settings(), monday(8, 30));
  assert.equal(result.status, 'before');
  assert.equal(result.earned, 0);
  assert.equal(result.progress, 0);
  assert.equal(result.paidSeconds, 8 * 3600);
  assert.equal(result.remainingPaidSeconds, 8 * 3600);
  assert.equal(result.remainingWallSeconds, 9.5 * 3600);
});

test('working salary uses net scheduled time and distinguishes wall time', () => {
  const result = calculate(settings(), monday(10));
  assert.equal(result.status, 'working');
  assert.equal(result.elapsedPaidSeconds, 3600);
  assert.equal(result.progress, 1 / 8);
  assert.equal(result.earned, 68.18);
  assert.equal(result.dailySalary, 545.45);
  assert.equal(result.hourlySalary, 68.18);
  assert.equal(result.remainingPaidSeconds, 7 * 3600);
  assert.equal(result.remainingWallSeconds, 8 * 3600);
});

test('lunch freezes salary, then resumes at exactly 13:00', () => {
  const atStart = calculate(settings(), monday(12));
  const during = calculate(settings(), monday(12, 45));
  const atEnd = calculate(settings(), monday(13));
  assert.equal(atStart.status, 'lunch');
  assert.equal(during.status, 'lunch');
  assert.equal(atEnd.status, 'working');
  assert.equal(atStart.earned, 204.55);
  assert.equal(during.earned, atStart.earned);
  assert.equal(atEnd.earned, atStart.earned);
  assert.equal(during.elapsedPaidSeconds, 3 * 3600);
  assert.equal(during.remainingPaidSeconds, 5 * 3600);
  assert.equal(during.remainingWallSeconds, 5.25 * 3600);
  assert.equal(calculate(settings(), monday(14)).earned, 272.73);
});

test('salary caps at end of day, with no overtime accumulation', () => {
  const atEnd = calculate(settings(), monday(18));
  const late = calculate(settings(), monday(22, 15));
  assert.equal(atEnd.status, 'after');
  assert.equal(late.status, 'after');
  assert.equal(late.earned, late.dailySalary);
  assert.equal(late.earned, atEnd.earned);
  assert.equal(late.progress, 1);
  assert.equal(late.elapsedPaidSeconds, 8 * 3600);
  assert.equal(late.remainingPaidSeconds, 0);
  assert.equal(late.remainingWallSeconds, 0);
});

test('weekend is a rest day unless selected as a workday', () => {
  const saturday = new Date(2026, 10, 14, 14);
  const result = calculate(settings(), saturday);
  assert.equal(result.status, 'rest');
  assert.equal(result.earned, 0);
  assert.equal(result.progress, 0);
  assert.equal(result.elapsedPaidSeconds, 0);
  assert.equal(result.remainingPaidSeconds, 0);
  assert.equal(result.remainingWallSeconds, 0);
  assert.equal(calculate(settings({ workDays: [6] }), saturday).status, 'working');
});

test('disabled lunch is paid and lunch fields are ignored', () => {
  const config = settings({ lunchEnabled: false, lunchStart: '', lunchEnd: '' });
  assert.equal(validateSettings(config).valid, true);
  const result = calculate(config, monday(12, 30));
  assert.equal(result.status, 'working');
  assert.equal(result.paidSeconds, 9 * 3600);
  assert.equal(result.elapsedPaidSeconds, 3.5 * 3600);
  assert.equal(result.earned, 212.12);
  assert.equal(result.hourlySalary, 60.61);
});

test('same-time and overnight shifts are rejected', () => {
  for (const endTime of ['09:00', '08:00']) {
    const config = settings({ endTime });
    assert.equal(validateSettings(config).valid, false);
    assert.ok(validateSettings(config).errors.endTime);
    assert.throws(() => calculate(config, monday(10)), RangeError);
  }
});

test('invalid pay and no workdays produce field-specific errors', () => {
  for (const monthlySalary of [0, -1, NaN, Infinity, '12000']) {
    assert.ok(validateSettings(settings({ monthlySalary })).errors.monthlySalary);
  }
  for (const paidDays of [0, -1, NaN, Infinity]) {
    assert.ok(validateSettings(settings({ paidDays })).errors.paidDays);
  }
  for (const workDays of [[], [7], [-1], ['1'], null]) {
    assert.ok(validateSettings(settings({ workDays })).errors.workDays);
  }
  assert.equal(validateSettings(settings({ paidDays: 21.75 })).valid, true);
});

test('salary and paid-day upper bounds match the settings form', () => {
  assert.equal(validateSettings(settings({ monthlySalary: 10000000, paidDays: 31 })).valid, true);
  const salaryError = validateSettings(settings({ monthlySalary: 10000000.01 }));
  assert.equal(salaryError.valid, false);
  assert.equal(salaryError.errors.monthlySalary, '月薪需为大于 0 且不超过 10,000,000 元的有效数字');
  const daysError = validateSettings(settings({ paidDays: 31.01 }));
  assert.equal(daysError.valid, false);
  assert.equal(daysError.errors.paidDays, '每月计薪日需为大于 0 且不超过 31 天的有效数字');
  assert.equal(validateSettings(settings({ paidDays: 21.75 })).valid, true);
});

test('bad times, out-of-shift lunch and zero net time are rejected', () => {
  assert.ok(validateSettings(settings({ startTime: '9:00' })).errors.startTime);
  assert.ok(validateSettings(settings({ endTime: '24:00' })).errors.endTime);
  assert.ok(validateSettings(settings({ lunchStart: '08:00' })).errors.lunchStart);
  assert.ok(validateSettings(settings({ lunchEnd: '19:00' })).errors.lunchEnd);
  assert.ok(validateSettings(settings({ lunchStart: '13:00', lunchEnd: '12:00' })).errors.lunchEnd);
  assert.ok(validateSettings(settings({ lunchStart: '09:00', lunchEnd: '18:00' })).errors.lunchEnd);
  assert.equal(validateSettings(null).valid, false);
  assert.throws(() => calculate(settings(), new Date('invalid')), TypeError);
});

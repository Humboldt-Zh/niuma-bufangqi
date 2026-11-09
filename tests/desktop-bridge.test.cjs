'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const bridgeSource = fs.readFileSync(path.join(__dirname, '..', 'desktop-bridge.js'), 'utf8');

// Only the browser APIs at the bridge boundary are substituted; the real bridge runs unchanged.
function loadBridge({ bootstrap = true, transport = 'complete', failSend = false } = {}) {
  const sent = [];
  const dispatched = [];
  const rootClasses = new Set();
  const bodyClasses = new Set();
  const state = { settings: null, privacy: false, startWithWindows: false, breakSessions: [] };
  let messageListener;
  const webview = transport === 'absent' ? undefined : {};
  if (webview && transport !== 'without-post') {
    webview.postMessage = message => {
      if (failSend) throw new Error('synthetic-transport-detail');
      sent.push(message);
    };
  }
  if (webview && transport !== 'without-events') {
    webview.addEventListener = (name, listener) => {
      assert.equal(name, 'message');
      messageListener = listener;
    };
  }
  const window = { chrome: { webview } };
  if (bootstrap) window.__NIUMA_BOOTSTRAP__ = { state, version: '0.1.0-test' };
  const document = {
    documentElement: { classList: { add: value => rootClasses.add(value) } },
    body: { classList: { add: value => bodyClasses.add(value) } },
    dispatchEvent: event => dispatched.push(event)
  };
  class CustomEvent {
    constructor(type, { detail }) { this.type = type; this.detail = detail; }
  }
  vm.runInNewContext(bridgeSource, { window, document, CustomEvent }, { filename: 'desktop-bridge.js' });
  return {
    api: window.NiumaDesktop,
    state,
    sent,
    dispatched,
    rootClasses,
    bodyClasses,
    receive(message) {
      assert.equal(typeof messageListener, 'function', 'the native transport must have a receiver');
      messageListener({ data: message });
    }
  };
}

test('a WebView transport without host bootstrap remains a browser demonstration', () => {
  const bridge = loadBridge({ bootstrap: false });
  assert.equal(bridge.api.isDesktop, false);
  assert.equal(bridge.api.initialState, null);
  assert.equal(bridge.rootClasses.size, 0);
  assert.equal(bridge.bodyClasses.size, 0);
  assert.equal(bridge.api.send('ready'), false);
  assert.equal(bridge.sent.length, 0);
});

test('bootstrap alone cannot activate a missing or incomplete native transport', () => {
  for (const transport of ['absent', 'without-post', 'without-events']) {
    const bridge = loadBridge({ transport });
    assert.equal(bridge.api.isDesktop, false, transport);
    assert.equal(bridge.api.send('ready'), false, transport);
    assert.equal(bridge.rootClasses.size, 0, transport);
    assert.equal(bridge.bodyClasses.size, 0, transport);
  }
});

test('host bootstrap and a complete transport activate desktop styling and initial state', () => {
  const bridge = loadBridge();
  assert.equal(bridge.api.isDesktop, true);
  assert.equal(bridge.api.initialState, bridge.state);
  assert.equal(bridge.api.version, '0.1.0-test');
  assert.equal(bridge.rootClasses.has('desktop-host'), true);
  assert.equal(bridge.bodyClasses.has('desktop-host'), true);
  assert.equal(Object.isFrozen(bridge.api), true);
});

test('allowed commands use the versioned public envelope and preserve their payload', () => {
  const bridge = loadBridge();
  const commands = ['ready', 'saveSettings', 'setPrivacy', 'saveBreakSessions', 'setStartup',
    'hide', 'openExternal', 'openDataFolder', 'resetSettings'];
  for (const type of commands) {
    const payload = { marker: type };
    assert.equal(bridge.api.send(type, payload), true);
    const message = bridge.sent.at(-1);
    assert.equal(message.version, 1);
    assert.equal(message.type, type);
    assert.equal(message.payload, payload);
    assert.deepEqual(Object.keys(message).sort(), ['payload', 'type', 'version']);
  }
  assert.equal(bridge.api.send('ready'), true);
  assert.deepEqual(JSON.parse(JSON.stringify(bridge.sent.at(-1))), { version: 1, type: 'ready', payload: {} });
});

test('unknown commands and non-object or array payloads never cross the native boundary', () => {
  const bridge = loadBridge();
  assert.equal(bridge.api.send('runArbitraryCommand', {}), false);
  for (const payload of [null, [], ['value'], 'value', 12, true]) {
    assert.equal(bridge.api.send('saveSettings', payload), false);
  }
  assert.equal(bridge.sent.length, 0);
});

test('host state, snapshots and friendly errors become the documented UI events', () => {
  const bridge = loadBridge();
  const state = { settings: null, privacy: true };
  const snapshot = { type: 'snapshot', configured: false, snapshot: null };
  bridge.receive({ type: 'state', state });
  bridge.receive(snapshot);
  bridge.receive({ type: 'error', message: '设置没有保存，请重试。' });
  bridge.receive({ type: 'error', message: null });
  assert.deepEqual(bridge.dispatched.map(event => event.type),
    ['niuma:state', 'niuma:snapshot', 'niuma:error', 'niuma:error']);
  assert.equal(bridge.dispatched[0].detail, state);
  assert.equal(bridge.dispatched[1].detail, snapshot);
  assert.equal(bridge.dispatched[2].detail, '设置没有保存，请重试。');
  assert.equal(typeof bridge.dispatched[3].detail, 'string');
  assert.match(bridge.dispatched[3].detail, /没有保存/);
});

test('irrelevant or malformed incoming messages do not dispatch UI state changes', () => {
  const bridge = loadBridge();
  for (const message of [null, undefined, 'state', 123, [], {}, { type: 'unknown' },
    { type: 'state' }, { type: 'state', state: null }, { type: 'state', state: 'invalid' }]) {
    bridge.receive(message);
  }
  assert.equal(bridge.dispatched.length, 0);
  assert.equal(bridge.sent.length, 0);
});

test('a failed transport returns false and dispatches a friendly unsaved error', () => {
  const bridge = loadBridge({ failSend: true });
  assert.equal(bridge.api.send('saveSettings', { monthlySalary: 10000 }), false);
  assert.equal(bridge.sent.length, 0);
  assert.equal(bridge.dispatched.length, 1);
  assert.equal(bridge.dispatched[0].type, 'niuma:error');
  assert.match(bridge.dispatched[0].detail, /没有保存/);
  assert.doesNotMatch(bridge.dispatched[0].detail, /synthetic-transport-detail/);
});

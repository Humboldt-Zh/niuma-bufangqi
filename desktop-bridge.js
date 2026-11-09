/* The desktop bridge only activates for the packaged, host-bootstrapped UI. */
(() => {
  'use strict';
  const bootstrap = window.__NIUMA_BOOTSTRAP__;
  const webview = window.chrome?.webview;
  const isDesktop = Boolean(bootstrap && typeof bootstrap === 'object' && bootstrap.state && webview
    && typeof webview.postMessage === 'function' && typeof webview.addEventListener === 'function');
  const commands = new Set(['ready', 'saveSettings', 'setPrivacy', 'saveBreakSessions', 'setStartup',
    'hide', 'openExternal', 'openDataFolder', 'resetSettings']);
  const initialState = isDesktop ? bootstrap.state : null;
  if (isDesktop) {
    document.documentElement.classList.add('desktop-host');
    document.body.classList.add('desktop-host');
    webview.addEventListener('message', event => {
      const message = event.data;
      if (!message || typeof message !== 'object') return;
      if (message.type === 'snapshot') document.dispatchEvent(new CustomEvent('niuma:snapshot', { detail: message }));
      else if (message.type === 'state' && message.state && typeof message.state === 'object') {
        document.dispatchEvent(new CustomEvent('niuma:state', { detail: message.state }));
      } else if (message.type === 'error') {
        document.dispatchEvent(new CustomEvent('niuma:error', { detail: typeof message.message === 'string'
          ? message.message : '这次操作没有保存，请稍后重试。' }));
      }
    });
  }
  window.NiumaDesktop = Object.freeze({
    isDesktop,
    initialState,
    version: isDesktop && typeof bootstrap.version === 'string' ? bootstrap.version : '0.1.4',
    send(type, payload = {}) {
      if (!isDesktop || !commands.has(type) || !payload || typeof payload !== 'object' || Array.isArray(payload)) return false;
      try { webview.postMessage({ version: 1, type, payload }); return true; }
      catch {
        document.dispatchEvent(new CustomEvent('niuma:error', { detail: '本地连接暂时不可用，这次操作没有保存。' }));
        return false;
      }
    }
  });
})();

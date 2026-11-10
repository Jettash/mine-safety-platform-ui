'use strict';
const form = document.querySelector('#auth-form'), message = document.querySelector('#auth-message');
const setup = document.body.dataset.auth === 'setup'; let setupToken = '';
async function initialize() {
  try { const response = await fetch('/api/setup-info', { cache: 'no-store' }); if (!response.ok) throw new Error('无法读取服务状态。');
    const info = await response.json();
    if (setup && info.initialized) { location.replace('/login'); return; }
    if (!setup && !info.initialized) { location.replace('/setup'); return; }
    if (setup && !info.canSetup) { form.querySelectorAll('input,button').forEach(input => input.disabled = true); throw new Error('请在服务器本机完成首次设置。'); }
    setupToken = info.setupToken || '';
    if (!setup && new URLSearchParams(location.search).has('initialized')) message.textContent = '账号已建立，请登录。';
  } catch (error) { message.textContent = error.message; }
}
form.addEventListener('submit', async event => {
  event.preventDefault(); const button = form.querySelector('button'); button.disabled = true; message.textContent = '';
  try {
    let body;
    if (setup) {
      const admin = document.querySelector('#admin-password').value, viewer = document.querySelector('#viewer-password').value;
      if (admin !== document.querySelector('#admin-confirm').value || viewer !== document.querySelector('#viewer-confirm').value)
        throw new Error('两次输入的密码不一致，请检查。');
      body = { adminPassword: admin, viewerPassword: viewer };
    } else body = { username: document.querySelector('#username').value.trim(), password: document.querySelector('#password').value };
    const response = await fetch(setup ? '/api/setup' : '/api/login', { method: 'POST', credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', ...(setup ? { 'X-Setup-Token': setupToken } : {}) }, body: JSON.stringify(body) });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(result.error || '请求失败，请重试。');
    location.replace(setup ? '/login?initialized=1' : '/');
  } catch (error) { message.textContent = error.message; }
  finally { button.disabled = false; }
});
initialize();

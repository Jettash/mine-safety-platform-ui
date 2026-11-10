'use strict';
const $ = selector => document.querySelector(selector);
const labels = { valid: '有效', invalid: '无效' };
let snapshot = null, dirty = false, querySerial = 0, pageSerial = 0, detailSerial = 0, exportSerial = 0;
let homeBusy = false, serverOffset = 0, toastTimer;
function toast(message) {
  $('#toast').textContent = message; $('#toast').style.display = 'block';
  clearTimeout(toastTimer); toastTimer = setTimeout(() => $('#toast').style.display = 'none', 4000);
}
async function api(url, options = {}) {
  const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', ...options,
    headers: { ...(options.body ? { 'Content-Type': 'application/json' } : {}), ...options.headers } });
  if (response.status === 401) { location.replace('/login'); throw new Error('请重新登录。'); }
  if (!response.ok) {
    const data = await response.json().catch(() => ({}));
    const error = new Error(data.error || '请求失败，请稍后重试。'); error.code = data.code;
    if (response.status === 410) { dirty = true; $('#export-open').disabled = true; $('#query-message').textContent = '本次查询已过期，请重新点击“查询”。'; }
    throw error;
  }
  return response;
}
async function json(url, options) { return (await api(url, options)).json(); }
function chinaParts() {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit',
    day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' })
    .formatToParts(new Date(Date.now() + serverOffset));
  const get = type => parts.find(part => part.type === type).value;
  return { day: `${get('year')}-${get('month')}-${get('day')}`, clock: `${get('hour')}:${get('minute')}:${get('second')}` };
}
function updateClock() { const now = chinaParts(); $('#clock').textContent = `${now.day} ${now.clock}`; }
function addCells(tr, values) { values.forEach(value => { const td = document.createElement('td'); td.textContent = value; tr.append(td); }); }
function vehicleText(item) { return [item.vehicleId, item.vehicleName].filter(Boolean).join(' / ') || '未匹配车辆'; }
function tag(status) { const span = document.createElement('span'); span.className = `pill ${labels[status] ? status : 'invalid'}`; span.textContent = labels[status] || '未知'; return span; }
function volume(value) { return value === null || value === undefined ? '—' : Number(value).toFixed(2); }
function empty(body, count, message) {
  const tr = document.createElement('tr'), td = document.createElement('td'); tr.className = 'empty-row';
  td.colSpan = count; td.textContent = message; tr.append(td); body.append(tr);
}
async function refreshHome() {
  if (homeBusy) return; homeBusy = true;
  try {
    const data = await json('/api/home');
    serverOffset = new Date(data.updatedAt.replace(' ', 'T') + '+08:00').getTime() - Date.now(); updateClock();
    ['total', 'valid', 'invalid'].forEach(key => $('#home-' + key).textContent = data.summary[key]);
    $('#home-volume').textContent = volume(data.summary.volume);
    $('#home-day').textContent = `今日数据 · ${data.day}（北京时间）`;
    $('#home-updated').classList.remove('error'); $('#home-updated').textContent = `更新于 ${data.updatedAt.slice(11)}`;
    const body = $('#home-records'); body.replaceChildren();
    data.items.forEach(item => { const tr = document.createElement('tr'); addCells(tr, [item.time, vehicleText(item)]);
      const state = document.createElement('td'); state.append(tag(item.status)); tr.append(state);
      addCells(tr, [item.matchLabel, volume(item.volume)]); body.append(tr); });
    if (!data.items.length) empty(body, 5, '今日暂无已结束的检测记录');
  } catch (error) { $('#home-updated').classList.add('error'); $('#home-updated').textContent = '更新失败：' + error.message; }
  finally { homeBusy = false; }
}
function setPreset(value) {
  document.querySelectorAll('[data-preset]').forEach(button => button.classList.toggle('active', button.dataset.preset === value));
  if (value === 'custom') return;
  const today = chinaParts().day; let first = today;
  if (value === 'month') first = today.slice(0, 7) + '-01';
  if (value === 'week') { const day = new Date(today + 'T12:00:00Z'); day.setUTCDate(day.getUTCDate() - ((day.getUTCDay() + 6) % 7)); first = day.toISOString().slice(0, 10); }
  $('#query-start').value = first + 'T00:00'; $('#query-end').value = today + 'T23:59';
}
function filter() { return { start: $('#query-start').value, end: $('#query-end').value, vehicle: $('#query-vehicle').value.trim() }; }
function markDirty() { if (snapshot) { dirty = true; $('#query-message').textContent = '查询条件已修改，请点击“查询”更新结果。'; $('#export-open').disabled = true; } }
function scopeText(scope) { return `${scope.start.replace('T', ' ')} 至 ${scope.end.replace('T', ' ')} · 车辆：${scope.vehicle || '全部车辆'}`; }
function showResults(data) {
  snapshot = data;
  ['total', 'valid', 'invalid'].forEach(key => $('#stat-' + key).textContent = data.summary[key]);
  $('#stat-volume').textContent = volume(data.summary.volume);
  $('#snapshot-text').textContent = scopeText(data.scope); $('#snapshot-count').textContent = `共 ${data.totalCount} 车次`;
  $('#before-query').hidden = true; $('#query-results').hidden = false;
  const body = $('#query-records'); body.replaceChildren();
  data.items.forEach(item => {
    const tr = document.createElement('tr'); addCells(tr, [item.time, vehicleText(item)]);
    const state = document.createElement('td'); state.append(tag(item.status)); tr.append(state);
    addCells(tr, [item.matchLabel, volume(item.volume)]);
    const cell = document.createElement('td'), button = document.createElement('button');
    button.className = 'view'; button.type = 'button'; button.textContent = '详情';
    button.addEventListener('click', () => openDetail(item.eventId)); cell.append(button); tr.append(cell); body.append(tr);
  });
  if (!data.items.length) empty(body, 6, '当前时间与车辆条件下暂无记录');
  $('#page-count').textContent = `共 ${data.totalCount} 条`; $('#page-index').textContent = `${data.page} / ${data.pages}`;
  $('#prev-page').disabled = data.page === 1; $('#next-page').disabled = data.page === data.pages;
}
async function loadPage(page) {
  if (!snapshot) return; const id = snapshot.snapshotId, serial = ++pageSerial;
  $('#prev-page').disabled = true; $('#next-page').disabled = true;
  try { const data = await json(`/api/queries/${id}?page=${page}&pageSize=${$('#page-size').value}`);
    if (serial === pageSerial && snapshot?.snapshotId === id) showResults(data);
  } catch (error) { toast(error.message); }
}
function setPhoto(kind, url) {
  const image = $('#detail-' + kind), box = $('#detail-' + kind + '-box');
  box.classList.add('missing'); image.removeAttribute('src'); image.dataset.url = url || '';
  image.onload = () => { if (image.dataset.url === url) box.classList.remove('missing'); };
  image.onerror = () => { if (image.dataset.url === url) box.classList.add('missing'); };
  if (url) image.src = url;
}
async function openDetail(eventId) {
  if (!snapshot) return; const id = snapshot.snapshotId, serial = ++detailSerial;
  try { const item = await json(`/api/queries/${id}/records/${encodeURIComponent(eventId)}`);
    if (serial !== detailSerial || snapshot?.snapshotId !== id) return;
    $('#detail-title').textContent = `${vehicleText(item)}　|　${item.time}`;
    $('#detail-result').textContent = labels[item.status] || '未知';
    $('#detail-volume').textContent = item.volume === null ? '—' : `${volume(item.volume)} m³`;
    $('#detail-match').textContent = item.matchLabel;
    $('#detail-speed').textContent = item.speedMps === null ? '—' : `${Number(item.speedMps).toFixed(2)} m/s`;
    $('#detail-reason').textContent = item.reason || '未提供';
    setPhoto('front', null); setPhoto('top', null);
    const metadata = $('#detail-metadata'); metadata.replaceChildren();
    const number = (v, digits = 3) => v === null ? '—' : Number(v).toFixed(digits);
    const percent = v => v === null ? '—' : (Number(v) * 100).toFixed(2) + '%';
    const values = [
      ['事件编号', item.eventId], ['结束时间', item.endTime],
      ['车辆编号', item.vehicleId], ['车辆名称（记录时）', item.vehicleName],
      ['模板编号', item.templateId], ['模板名称', item.templateName],
      ['配准 RMSE（m）', number(item.registrationRmseM)], ['配准匹配率', percent(item.registrationFitness)],
      ['点云覆盖率', percent(item.cloudCoverage)], ['算法版本', item.algorithmVersion],
      ['纬度 / 经度', `${number(item.latitude, 7)} / ${number(item.longitude, 7)}`],
      ['RTK 精度（m）', number(item.accuracyM)], ['RTK 记录时间', item.gpsTime],
      ['标定编号', item.calibrationId], ['数据来源', item.source]
    ];
    values.forEach(([name, value]) => { const dt = document.createElement('dt'), dd = document.createElement('dd');
      dt.textContent = name; dd.textContent = value || '—'; metadata.append(dt, dd); });
    $('#vehicle-detail').showModal();
  } catch (error) { toast(error.message); }
}
async function renderExport() {
  if (!snapshot) return; const id = snapshot.snapshotId, serial = ++exportSerial;
  const body = $('#export-preview'); body.replaceChildren();
  try { const data = await json(`/api/queries/${id}/report?grain=${$('#export-grain').value}`);
    if (serial !== exportSerial || snapshot?.snapshotId !== id) return;
    data.rows.forEach(row => { const tr = document.createElement('tr'); addCells(tr, [row.period, row.total, row.valid, row.invalid, volume(row.volume)]); body.append(tr); });
    if (!data.rows.length) empty(body, 5, '该查询范围无记录');
  } catch (error) { toast(error.message); empty(body, 5, error.message); }
}
document.querySelectorAll('nav .item').forEach(button => button.addEventListener('click', () => {
  document.querySelectorAll('nav .item').forEach(item => item.classList.toggle('active', item === button));
  document.querySelectorAll('.page').forEach(page => page.classList.toggle('active', page.id === 'page-' + button.dataset.page));
  if (button.dataset.page === 'home') refreshHome(); window.scrollTo(0, 0);
}));
document.querySelectorAll('[data-demo]').forEach(button => button.addEventListener('click', () => toast(button.dataset.demo + '：首版尚未接入此功能。')));
document.querySelectorAll('#page-config input,#page-config select,#page-email input').forEach(input => { input.disabled = true; if (input.type === 'checkbox') input.checked = false; });
document.querySelectorAll('[data-preset]').forEach(button => button.addEventListener('click', () => { setPreset(button.dataset.preset); markDirty(); }));
[$('#query-start'), $('#query-end')].forEach(input => input.addEventListener('input', () => { setPreset('custom'); markDirty(); }));
$('#query-vehicle').addEventListener('input', markDirty);
$('#merged-query-form').addEventListener('submit', async event => {
  event.preventDefault(); const scope = filter(), serial = ++querySerial; ++pageSerial;
  if (!scope.start || !scope.end) { $('#query-message').textContent = '请选择开始时间和结束时间。'; return; }
  if (scope.start > scope.end) { $('#query-message').textContent = '开始时间不能晚于结束时间。'; return; }
  const button = event.submitter || $('#merged-query-form button[type="submit"]'); button.disabled = true;
  dirty = true; $('#export-open').disabled = true; $('#query-message').textContent = '正在查询…';
  try { const data = await json('/api/queries', { method: 'POST', body: JSON.stringify(scope) });
    if (serial !== querySerial) return;
    showResults(data); dirty = false; $('#query-message').textContent = ''; $('#export-open').disabled = false;
    if (JSON.stringify(scope) !== JSON.stringify(filter())) markDirty();
    if (data.pageSize !== Number($('#page-size').value)) await loadPage(1);
  } catch (error) { if (serial === querySerial) $('#query-message').textContent = '查询失败：' + error.message; }
  finally { button.disabled = false; }
});
$('#query-reset').addEventListener('click', () => {
  ++querySerial; ++pageSerial; ++detailSerial; snapshot = null; dirty = false;
  $('#query-vehicle').value = ''; setPreset('day'); $('#query-message').textContent = '';
  $('#query-results').hidden = true; $('#before-query').hidden = false; $('#export-open').disabled = true;
});
$('#page-size').addEventListener('change', () => loadPage(1));
$('#prev-page').addEventListener('click', () => loadPage(snapshot.page - 1));
$('#next-page').addEventListener('click', () => loadPage(snapshot.page + 1));
$('#detail-close').addEventListener('click', () => { ++detailSerial; $('#vehicle-detail').close(); });
$('#export-open').addEventListener('click', () => {
  if (!snapshot || dirty) return;
  $('#export-scope').textContent = `查询范围：${scopeText(snapshot.scope)}；记录数：${snapshot.totalCount}`;
  $('#export-dialog').showModal(); renderExport();
});
$('#export-grain').addEventListener('change', renderExport);
$('#export-close').addEventListener('click', () => { ++exportSerial; $('#export-dialog').close(); });
$('#export-confirm').addEventListener('click', async () => {
  if (!snapshot || dirty) { toast('请重新查询后导出。'); return; }
  const button = $('#export-confirm'); button.disabled = true;
  try { const response = await api(`/api/queries/${snapshot.snapshotId}/export.xlsx?grain=${$('#export-grain').value}&includeRecords=${$('#include-records').checked}`);
    const url = URL.createObjectURL(await response.blob()), link = document.createElement('a');
    link.href = url; link.download = `矿车报表_${snapshot.scope.start.slice(0, 10)}.xlsx`; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000); toast('Excel 报表已下载。');
  } catch (error) { toast(error.message); } finally { button.disabled = false; }
});
$('#account-menu').addEventListener('click', () => { const menu = $('#account-dropdown'); menu.hidden = !menu.hidden; $('#account-menu').setAttribute('aria-expanded', String(!menu.hidden)); });
$('#logout').addEventListener('click', async () => { try { await json('/api/logout', { method: 'POST' }); location.replace('/login'); } catch (error) { toast(error.message); } });
async function initialize() {
  try { const me = await json('/api/me'); $('#account-menu').textContent = `${me.role === 'Administrator' ? '管理员' : '查看员'} ▾`;
    $('#account-name').textContent = `当前账号：${me.username}`; setPreset('day'); updateClock(); await refreshHome();
    setInterval(updateClock, 1000); setInterval(() => { if (!document.hidden && $('#page-home').classList.contains('active')) refreshHome(); }, 5000);
  } catch (error) { if (!location.pathname.includes('login')) toast(error.message); }
}
initialize();

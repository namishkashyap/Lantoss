'use strict';
const $ = id => document.getElementById(id);
// Appearance is a per-browser preference; a phone follows its own system, not the host PC.
// Storage can be blocked in private browsers. Keep sharing and the current choice working then.
const systemAppearance = window.matchMedia('(prefers-color-scheme: dark)');
let appearanceMode = 'System';
try { const saved = localStorage.getItem('lantoss.appearance'); if (['System', 'Light', 'Dark'].includes(saved)) appearanceMode = saved; } catch { }
function applyAppearance() {
  document.documentElement.dataset.theme = appearanceMode === 'System' ? (systemAppearance.matches ? 'dark' : 'light') : appearanceMode.toLowerCase();
  $('appearance').value = appearanceMode;
}
$('appearance').addEventListener('change', () => {
  appearanceMode = ['System', 'Light', 'Dark'].includes($('appearance').value) ? $('appearance').value : 'System';
  try { localStorage.setItem('lantoss.appearance', appearanceMode); } catch { }
  applyAppearance();
});
systemAppearance.addEventListener('change', () => { if (appearanceMode === 'System') applyAppearance(); });
applyAppearance();

let csrf = '', selected = [], busy = false, activeUpload = null, offerAbort = null, offerId = '', cancelled = false;
let activeSpace = null, activeFolder = '', transferPolling = false, spaceLoad = 0;
const bytes = size => { const units = ['B','KB','MB','GB']; let at = 0; while (size >= 1024 && at < 3) { size /= 1024; at++; } return `${size.toFixed(at ? 1 : 0)} ${units[at]}`; };
function notice(message, error = false) { $('notice').textContent = message; $('notice').className = error ? 'error' : ''; }
const rateText = speed => speed > 0 ? `${bytes(speed)}/s` : 'Measuring…';
const timeText = seconds => seconds < 60 ? `${Math.max(1, Math.ceil(seconds))}s` : `${Math.floor(seconds / 60)}m ${Math.ceil(seconds % 60)}s`;
const validSelection = () => selected.length > 0 && selected.length <= 100 && selected.every(file => file.size <= 2147483648) && selected.reduce((sum, file) => sum + file.size, 0) <= 10737418240;
function updateTarget() {
  const readOnly = activeSpace && !activeSpace.canUpload;
  $('files').disabled = busy || !!readOnly;
  $('send').disabled = busy || !!readOnly || !validSelection();
  $('send-title').textContent = activeSpace ? `Upload to ${activeSpace.name}` : 'Send to your PC';
  $('upload-hint').textContent = readOnly ? 'This space is read only. Use PC inbox instead to send files.' : activeSpace ? `Destination: ${activeFolder || 'space root'}. Approve the batch on the PC.` : 'Pick files or photos. Approve the request on your PC.';
}
async function request(path, options = {}) {
  const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store', ...options, headers: { 'X-Lantoss-CSRF': csrf, ...options.headers } });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) {
    if (response.status === 401 && path !== '/api/session' && path !== '/api/login') { $('sharing').hidden = true; void connect(); }
    throw new Error(body.error || (response.status === 429 ? 'Too many requests. Wait one minute and try again.' : 'This request could not finish. Check Lantoss on your PC.'));
  }
  return body;
}
async function connect() {
  try {
    const session = await request('/api/session');
    $('device').textContent = session.name;
    $('login').hidden = session.connected;
    $('sharing').hidden = !session.connected;
    if (session.connected) {
      csrf = session.csrf;
      $('connection-label').textContent = session.requiresCode ? 'Connected · code protection on' : 'Connected · no code needed';
      await refreshFiles();
      await refreshSpaces();
      startTransferPolling();
    }
  } catch (error) { notice(error.message, true); }
}
$('login-form').addEventListener('submit', async event => {
  event.preventDefault(); $('connect').disabled = true;
  try {
    const login = await request('/api/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code: $('code').value }) });
    csrf = login.csrf; $('code').value = ''; notice(''); await connect();
  } catch (error) { notice(error.message, true); }
  finally { $('connect').disabled = false; }
});
async function refreshFiles() {
  const files = await request('/api/files');
  $('downloads').replaceChildren();
  if (!files.length) {
    const empty = document.createElement('div'); empty.className = 'empty';
    empty.textContent = 'Nothing shared yet. On your PC, open Phone & browser and choose files to share.';
    $('downloads').append(empty);
  }
  for (const file of files) {
    const row = document.createElement('div'); row.className = 'file-row';
    const details = document.createElement('div');
    const name = document.createElement('div'); name.className = 'file-name'; name.textContent = file.name;
    const meta = document.createElement('div'); meta.className = 'file-meta'; meta.textContent = bytes(file.size);
    details.append(name, meta);
    const download = document.createElement('a'); download.className = 'download'; download.href = '/api/download/' + encodeURIComponent(file.id); download.textContent = 'Download ↓';
    row.append(details, download); $('downloads').append(row);
  }
}
$('refresh').addEventListener('click', () => refreshFiles().catch(error => notice(error.message, true)));
async function refreshSpaces() {
  const spaces = await request('/api/spaces');
  $('spaces').replaceChildren();
  if (!spaces.length) { const empty = document.createElement('div'); empty.className = 'empty'; empty.textContent = 'Create a space from a folder in the Windows app. Only those folders will appear here.'; $('spaces').append(empty); }
  for (const space of spaces) {
    const button = document.createElement('button'); button.className = 'space-card'; button.disabled = !space.available;
    const title = document.createElement('strong'); title.textContent = space.name;
    const permission = document.createElement('span'); permission.textContent = !space.available ? 'Folder unavailable' : space.canUpload ? 'Read & upload →' : 'Read only →';
    button.append(title, permission); button.addEventListener('click', () => openSpace(space).catch(error => notice(error.message, true))); $('spaces').append(button);
  }
  if (activeSpace) {
    const latest = spaces.find(space => space.id === activeSpace.id && space.available);
    if (!latest) { activeSpace = null; activeFolder = ''; $('space-browser').hidden = true; } else activeSpace = latest;
    updateTarget();
  } else {
    const wanted = new URLSearchParams(location.search).get('space');
    const initial = spaces.find(space => space.id === wanted && space.available);
    if (initial) await openSpace(initial);
  }
}
async function openSpace(space, path = '') {
  if (busy) { notice('Finish or cancel this upload before changing its destination.'); return; }
  const load = ++spaceLoad;
  const entries = await request(`/api/spaces/${encodeURIComponent(space.id)}/entries?path=${encodeURIComponent(path)}`);
  if (load !== spaceLoad) return;
  activeSpace = space; activeFolder = path; $('space-browser').hidden = false;
  $('space-path').textContent = `${space.name}${path ? ' / ' + path : ''}`;
  $('space-up').disabled = !path;
  $('space-entries').replaceChildren();
  if (!entries.length) { const empty = document.createElement('div'); empty.className = 'empty'; empty.textContent = 'This folder is empty.'; $('space-entries').append(empty); }
  for (const entry of entries) {
    const row = document.createElement('div'); row.className = 'file-row';
    const label = document.createElement('div'); label.className = 'file-name'; label.textContent = `${entry.directory ? '▤ ' : ''}${entry.name}`;
    const action = document.createElement(entry.directory ? 'button' : 'a'); action.className = 'download';
    if (entry.directory) { action.textContent = 'Open folder →'; action.addEventListener('click', () => openSpace(space, entry.path).catch(error => notice(error.message, true))); }
    else { action.textContent = `Download · ${bytes(entry.size)}`; action.href = `/api/spaces/${encodeURIComponent(space.id)}/download?path=${encodeURIComponent(entry.path)}`; }
    row.append(label, action); $('space-entries').append(row);
  }
  updateTarget();
}
$('spaces-refresh').addEventListener('click', () => refreshSpaces().catch(error => notice(error.message, true)));
$('space-up').addEventListener('click', () => { if (activeSpace && !busy) { const parent = activeFolder.includes('/') ? activeFolder.slice(0, activeFolder.lastIndexOf('/')) : ''; void openSpace(activeSpace, parent).catch(error => notice(error.message, true)); } });
$('inbox').addEventListener('click', () => { if (busy) return; spaceLoad++; activeSpace = null; activeFolder = ''; $('space-browser').hidden = true; updateTarget(); });
async function refreshTransfers() {
  const transfers = await request('/api/transfers');
  $('live-transfers').replaceChildren();
  if (!transfers.length) { const empty = document.createElement('div'); empty.className = 'empty'; empty.textContent = 'Transfer a file to see progress, current speed, and time remaining here.'; $('live-transfers').append(empty); }
  for (const transfer of transfers) {
    const row = document.createElement('div'); row.className = 'transfer-row';
    const title = document.createElement('strong'); title.textContent = `${transfer.direction === 'Sent' ? 'Download' : 'Upload'} · ${transfer.title}`;
    const detail = document.createElement('p'); detail.textContent = `${transfer.state} · ${bytes(transfer.bytes)} / ${bytes(transfer.total)}`;
    const speed = document.createElement('p'); speed.className = 'speed'; speed.textContent = `${transfer.average ? 'Average' : 'Current'}: ${rateText(transfer.bytesPerSecond)}${transfer.remainingSeconds !== null ? ' · ~' + timeText(transfer.remainingSeconds) + ' left' : ''}`;
    const progress = document.createElement('progress'); progress.max = 100; progress.value = transfer.total ? Math.min(100, transfer.bytes * 100 / transfer.total) : transfer.state === 'Completed' ? 100 : 0;
    row.append(title, detail, speed, progress); $('live-transfers').append(row);
  }
}
function startTransferPolling() {
  if (transferPolling) return; transferPolling = true;
  async function tick() { try { if (!$('sharing').hidden) await refreshTransfers(); } catch { } finally { setTimeout(tick, 1000); } }
  void tick();
}
$('files').addEventListener('change', () => {
  if (busy) return;
  selected = Array.from($('files').files);
  $('selection').replaceChildren();
  for (const file of selected.slice(0, 100)) {
    const row = document.createElement('li'); row.textContent = file.name;
    const size = document.createElement('small'); size.textContent = bytes(file.size); row.append(size); $('selection').append(row);
  }
  const invalid = selected.length > 100 || selected.some(file => file.size > 2147483648) || selected.reduce((sum, file) => sum + file.size, 0) > 10737418240;
  updateTarget();
  notice(invalid ? 'Choose up to 100 files, 2 GiB each, and 10 GiB total.' : '', invalid);
});
function upload(file, index, before, total) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest(); activeUpload = xhr;
    let sampledAt = performance.now(), sampledBytes = 0, speed = 0;
    xhr.open('PUT', `/api/uploads/${encodeURIComponent(offerId)}/${index}`);
    xhr.setRequestHeader('X-Lantoss-CSRF', csrf);
    xhr.setRequestHeader('Content-Type', 'application/octet-stream');
    xhr.upload.onprogress = event => {
      const now = performance.now(), elapsed = (now - sampledAt) / 1000;
      if (elapsed >= .25) { speed = Math.max(0, event.loaded - sampledBytes) / elapsed; sampledAt = now; sampledBytes = event.loaded; }
      const amount = before + event.loaded;
      $('progress').value = total ? amount * 100 / total : 100;
      $('transfer-label').textContent = `${file.name} · ${bytes(amount)} of ${bytes(total)}`;
      $('transfer-speed').textContent = `${rateText(speed)}${speed > 0 && amount < total ? ' · ~' + timeText((total - amount) / speed) + ' left' : amount >= total ? ' · waiting for the PC to finish saving' : ''}`;
    };
    xhr.onload = () => { let result = {}; try { result = JSON.parse(xhr.responseText); } catch {} if (xhr.status >= 200 && xhr.status < 300) resolve(result); else reject(new Error(result.error || 'The upload stopped. Check your PC and try again.')); };
    xhr.onerror = () => reject(new Error('Connection lost. Keep both devices on the same Wi-Fi.'));
    xhr.onabort = () => reject(new Error('Upload cancelled. Files already saved on the PC are kept.'));
    xhr.send(file);
  });
}
$('send').addEventListener('click', async () => {
  if (busy || !validSelection() || (activeSpace && !activeSpace.canUpload)) return;
  busy = true; cancelled = false; offerId = ''; $('send').disabled = true; $('files').disabled = true; $('cancel').hidden = false; $('transfer').hidden = false; $('progress').value = 0; notice('');
  $('transfer-label').textContent = 'Waiting for approval on your PC…';
  try {
    offerAbort = new AbortController();
    const offer = await request('/api/upload-offers', { method: 'POST', signal: offerAbort.signal, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ files: selected.map(file => ({ name: file.name, size: file.size })), spaceId: activeSpace ? activeSpace.id : '', folder: activeFolder }) });
    offerAbort = null;
    offerId = offer.id;
    if (cancelled) throw new Error('Upload cancelled.');
    const total = selected.reduce((sum, file) => sum + file.size, 0); let sent = 0; const startedAt = performance.now();
    for (let index = 0; index < selected.length; index++) {
      if (cancelled) throw new Error('Upload cancelled.');
      await upload(selected[index], index, sent, total); sent += selected[index].size;
    }
    $('progress').value = 100; $('transfer-label').textContent = `${selected.length} ${selected.length === 1 ? 'file saved' : 'files saved'} on your PC.`;
    $('transfer-speed').textContent = `Average upload: ${rateText(total / Math.max(.001, (performance.now() - startedAt) / 1000))}`;
    notice('Done. Your files are saved on the PC.'); selected = []; $('selection').replaceChildren(); $('files').value = ''; offerId = '';
  } catch (error) {
    notice(error.message, true); $('transfer-label').textContent = 'Transfer stopped. You can choose files and try again.';
    if (offerId) await request(`/api/uploads/${encodeURIComponent(offerId)}/cancel`, { method: 'POST' }).catch(() => {});
  } finally { busy = false; activeUpload = null; offerAbort = null; $('cancel').hidden = true; updateTarget(); void refreshTransfers().catch(() => {}); if (activeSpace) void openSpace(activeSpace, activeFolder).catch(() => {}); }
});
$('cancel').addEventListener('click', () => { cancelled = true; if (activeUpload) activeUpload.abort(); if (offerAbort) offerAbort.abort(); $('transfer-label').textContent = 'Cancelling…'; });
connect();

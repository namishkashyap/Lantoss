'use strict';
const $ = id => document.getElementById(id);
// Appearance is a per-browser preference; a phone follows its own system, not the host PC.
// Storage can be blocked in private browsers. Keep sharing and the current choice working then.
const systemAppearance = window.matchMedia('(prefers-color-scheme: dark)');
const appearanceModes = ['System', 'Light', 'Dark'];
let appearanceMode = 'System';
try { const saved = localStorage.getItem('lantoss.appearance'); if (appearanceModes.includes(saved)) appearanceMode = saved; } catch { }
function applyAppearance() {
  document.documentElement.dataset.theme = appearanceMode === 'System' ? (systemAppearance.matches ? 'dark' : 'light') : appearanceMode.toLowerCase();
  for (const mode of appearanceModes) $('appearance-' + mode).setAttribute('aria-pressed', String(mode === appearanceMode));
}
for (const mode of appearanceModes) $('appearance-' + mode).addEventListener('click', () => {
  appearanceMode = mode;
  try { localStorage.setItem('lantoss.appearance', appearanceMode); } catch { }
  applyAppearance();
});
systemAppearance.addEventListener('change', () => { if (appearanceMode === 'System') applyAppearance(); });
applyAppearance();
// Tabs follow the design: Send, Get, Text & links and Folders. Arrow keys move between them.
const tabNames = ['send', 'get', 'text', 'folders'];
function showTab(name) {
  for (const tab of tabNames) { const on = tab === name; $('tab-' + tab).className = on ? 'tab on' : 'tab'; $('tab-' + tab).setAttribute('aria-selected', String(on)); $('tab-' + tab).tabIndex = on ? 0 : -1; $('panel-' + tab).hidden = !on; }
}
tabNames.forEach((tab, index) => {
  $('tab-' + tab).addEventListener('click', () => showTab(tab));
  $('tab-' + tab).addEventListener('keydown', event => { const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0; if (!step) return; event.preventDefault(); const next = tabNames[(index + step + tabNames.length) % tabNames.length]; showTab(next); $('tab-' + next).focus(); });
});
showTab('send');
function toggleProfile(open = $('profile').hidden) { $('profile').hidden = !open; $('profile-button').setAttribute('aria-expanded', String(open)); if (open) $('profile-name').focus(); }
$('profile-button').addEventListener('click', () => toggleProfile());
$('profile').addEventListener('keydown', event => { if (event.key === 'Escape') { toggleProfile(false); $('profile-button').focus(); } });
document.addEventListener('click', event => { if (!$('profile').hidden && !$('profile').contains(event.target) && !$('profile-button').contains(event.target)) toggleProfile(false); });
const showHost = name => { for (const id of ['device', 'chip-host', 'host-name', 'strip-name', 'profile-host', 'folders-host']) $(id).textContent = name; };
const photo = name => /\.(jpe?g|png|gif|webp|heic|heif|bmp|avif|tiff?)$/i.test(name);
const fileKind = name => photo(name) ? 'Photo' : name.includes('.') ? name.split('.').pop().toUpperCase().slice(0, 6) : 'File';
$('host-address').textContent = (location.hostname ? location.hostname + ' · ' : '') + 'Home Wi-Fi';

let csrf = '', selected = [], busy = false, activeUpload = null, offerAbort = null, offerId = '', cancelled = false;
let activeSpace = null, activeFolder = '', transferPolling = false, spaceLoad = 0;
let notesRevision = '';
const bytes = size => { const units = ['B','KB','MB','GB']; let at = 0; while (size >= 1024 && at < 3) { size /= 1024; at++; } return `${size.toFixed(at ? 1 : 0)} ${units[at]}`; };
function notice(message, error = false) { $('notice').textContent = message; $('notice').className = error ? 'error' : ''; }
const rateText = speed => speed > 0 ? `${bytes(speed)}/s` : 'Measuring…';
const timeText = seconds => seconds < 60 ? `${Math.max(1, Math.ceil(seconds))}s` : `${Math.floor(seconds / 60)}m ${Math.ceil(seconds % 60)}s`;
const validSelection = () => selected.length > 0 && selected.length <= 100 && selected.every(file => file.size <= 2147483648) && selected.reduce((sum, file) => sum + file.size, 0) <= 10737418240;
function updateTarget() {
  const readOnly = activeSpace && !activeSpace.canUpload;
  $('files').disabled = busy || !!readOnly;
  $('spaces-refresh').disabled = busy; $('selection-clear').disabled = busy;
  for (const row of $('selection').children) row.children[row.children.length - 1].disabled = busy;
  $('send').disabled = busy || !!readOnly || !validSelection();
  $('send-title').textContent = activeSpace ? `Upload to ${activeSpace.name}` : 'Send to your PC'; $('send-title').hidden = !activeSpace;
  $('upload-hint').textContent = readOnly ? 'This space is read only. Use PC inbox instead to send files.' : activeSpace ? `Destination: ${activeFolder || 'space root'}. Approve the batch on the PC.` : 'Pick files or photos. Approve the request on your PC.';
}
async function request(path, options = {}) {
  const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store', ...options, headers: { 'X-Lantoss-CSRF': csrf, ...options.headers } });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) {
    if ((response.status === 401 || (response.status === 403 && body.waiting)) && path !== '/api/session' && path !== '/api/login') { $('sharing').hidden = true; void connect(); }
    throw new Error(body.error || (response.status === 429 ? 'Too many requests. Wait one minute and try again.' : 'This request could not finish. Check hopo on your PC.'));
  }
  return body;
}
let profile = { name: '', picture: '' };
try { const saved = JSON.parse(localStorage.getItem('lantoss.profile') || '{}'); profile = { name: typeof saved.name === 'string' ? saved.name.slice(0, 40) : '', picture: typeof saved.picture === 'string' && saved.picture.startsWith('data:image/jpeg;base64,') ? saved.picture : '' }; } catch { }
function showProfile() {
  $('profile-name').value = profile.name;
  for (const id of ['me-avatar', 'profile-avatar']) {
    const avatar = $(id); avatar.replaceChildren(); avatar.textContent = '';
    if (profile.picture) { const image = document.createElement('img'); image.src = profile.picture; image.alt = ''; avatar.append(image); }
    else avatar.textContent = profile.name.trim().split(/\s+/).filter(Boolean).slice(0, 2).map(word => [...word][0]).join('').toUpperCase() || '?';
  }
}
showProfile();
function sendProfile() { return request('/api/profile', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: profile.name, picture: profile.picture || null }) }); }
// Pictures are cropped and re-encoded here so only a small JPEG leaves the phone.
function resizePicture(file) {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file); const image = new Image();
    image.onload = () => { const side = Math.min(image.naturalWidth, image.naturalHeight); const canvas = document.createElement('canvas'); canvas.width = canvas.height = 128; canvas.getContext('2d').drawImage(image, (image.naturalWidth - side) / 2, (image.naturalHeight - side) / 2, side, side, 0, 0, 128, 128); URL.revokeObjectURL(url); resolve(canvas.toDataURL('image/jpeg', 0.82)); };
    image.onerror = () => { URL.revokeObjectURL(url); reject(new Error('That picture could not be opened.')); };
    image.src = url;
  });
}
$('profile-picture').addEventListener('change', async () => { const file = $('profile-picture').files[0]; $('profile-picture').value = ''; if (!file) return; try { profile.picture = await resizePicture(file); showProfile(); notice('Press Save to show this picture on the PC.'); } catch (error) { notice(error.message, true); } });
$('profile-clear').addEventListener('click', () => { profile.picture = ''; showProfile(); notice('Press Save to remove your picture from the PC.'); });
$('profile-form').addEventListener('submit', async event => {
  event.preventDefault(); profile.name = $('profile-name').value.trim().slice(0, 40); $('profile-save').disabled = true;
  try { await sendProfile(); try { localStorage.setItem('lantoss.profile', JSON.stringify(profile)); } catch { } showProfile(); toggleProfile(false); notice('Saved. The PC now shows your name' + (profile.picture ? ' and picture.' : '.')); }
  catch (error) { notice(error.message, true); }
  finally { $('profile-save').disabled = false; }
});
let waitTimer = 0, knownPersonal = new Set();
async function connect() {
  try {
    const session = await request('/api/session');
    showHost(session.name);
    $('login').hidden = session.connected || session.waiting || !session.requiresCode;
    $('waiting').hidden = !session.waiting;
    $('sharing').hidden = !session.connected; $('connected-chip').hidden = !session.connected;
    $('security').textContent = session.encrypted ? 'Encrypted (HTTPS)' + (session.certificate ? ' · certificate ' + session.certificate : '') : 'Not encrypted · use only on trusted Wi-Fi';
    $('pc-chip').className = session.encrypted ? 'chip safe' : 'chip safe open';
    if (!session.connected) $('connection-label').textContent = $('strip-status').textContent = session.waiting ? 'Waiting for Allow on the PC' : session.requiresCode ? 'Access code needed' : 'Not connected';
    if (session.waiting) {
      csrf = session.csrf; $('waiting-host').textContent = session.name;
      if (profile.name || profile.picture) { try { await sendProfile(); } catch { } }
      clearTimeout(waitTimer); waitTimer = setTimeout(() => void connect(), 3000);
      return;
    }
    if (session.connected) {
      csrf = session.csrf;
      $('connection-label').textContent = $('strip-status').textContent = (session.encrypted ? 'Encrypted · ' : 'Not encrypted · ') + (session.requiresCode ? 'code protection on' : 'approved by the PC');
      $('host-picture').hidden = !session.hasPicture;
      if (session.hasPicture) $('host-picture').src = '/api/picture?' + Date.now();
      showProfile();
      if (profile.name || profile.picture) { try { await sendProfile(); } catch { } }
      await refreshFiles();
      await refreshSpaces();
      await refreshNotes();
      await refreshBrowserDevices();
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
  const fresh = files.filter(file => file.personal && !knownPersonal.has(file.id));
  if (fresh.length) notice(`${$('device').textContent} sent you ${fresh.length} ${fresh.length === 1 ? 'file' : 'files'}. Find ${fresh.length === 1 ? 'it' : 'them'} under Get from PC.`);
  for (const file of fresh) knownPersonal.add(file.id);
  $('downloads').replaceChildren();
  if (!files.length) {
    const empty = document.createElement('div'); empty.className = 'empty big';
    const title = document.createElement('strong'); title.textContent = 'Nothing shared yet';
    const text = document.createElement('span'); text.textContent = 'On your PC, open Phone & browser and pick files. They’ll show up here to download.';
    empty.append(title, text); $('downloads').append(empty);
  }
  for (const file of files) {
    const row = document.createElement('div'); row.className = photo(file.name) ? 'file-row photo' : 'file-row';
    const details = document.createElement('div');
    const name = document.createElement('div'); name.className = 'file-name'; name.textContent = file.name;
    const meta = document.createElement('div'); meta.className = 'file-meta'; meta.textContent = `${fileKind(file.name)} · ${bytes(file.size)}` + (file.personal ? ' · sent to you' : '');
    details.append(name, meta);
    const download = document.createElement('a'); download.className = 'b p sm'; download.href = '/api/download/' + encodeURIComponent(file.id); download.textContent = 'Download';
    row.append(details, download); $('downloads').append(row);
  }
}
$('refresh').addEventListener('click', () => refreshFiles().catch(error => notice(error.message, true)));
async function refreshSpaces(reloadFolder = true) {
  if (busy) { notice('Finish or cancel this upload before refreshing its destination.'); return; }
  const previousSpace = activeSpace, previousFolder = activeFolder, previousLoad = spaceLoad;
  const spaces = await request('/api/spaces');
  if (spaceLoad !== previousLoad || busy) return;
  $('spaces').replaceChildren();
  $('spaces-count').textContent = spaces.length; $('spaces-count').hidden = !spaces.length;
  if (!spaces.length) { const empty = document.createElement('div'); empty.className = 'empty'; empty.textContent = 'Create a space from a folder in the Windows app. Only those folders will appear here.'; $('spaces').append(empty); }
  for (const space of spaces) {
    const button = document.createElement('button'); button.className = 'space-card'; button.disabled = !space.available;
    const title = document.createElement('strong'); title.textContent = space.name;
    const permission = document.createElement('span'); permission.textContent = !space.available ? 'Folder unavailable' : space.canUpload ? 'Read & upload' : 'Read only';
    button.append(title, permission); button.addEventListener('click', () => openSpace(space).catch(error => notice(error.message, true))); $('spaces').append(button);
  }
  if (activeSpace) {
    const latest = spaces.find(space => space.id === activeSpace.id && space.available);
    if (!latest) { activeSpace = null; activeFolder = ''; $('space-browser').hidden = true; } else { activeSpace = latest; if (reloadFolder && previousSpace?.id === latest.id) await openSpace(latest, previousFolder); }
    updateTarget();
  } else {
    const wanted = new URLSearchParams(location.search).get('space');
    const initial = spaces.find(space => space.id === wanted && space.available);
    if (initial) { await openSpace(initial); showTab('folders'); }
  }
}
async function openSpace(space, path = '') {
  if (busy) { notice('Finish or cancel this upload before changing its destination.'); return; }
  const load = ++spaceLoad;
  const entries = await request(`/api/spaces/${encodeURIComponent(space.id)}/entries?path=${encodeURIComponent(path)}`);
  if (load !== spaceLoad) return;
  activeSpace = space; activeFolder = path; $('space-browser').hidden = false;
  $('space-path').textContent = `${space.name}${path ? ' / ' + path : ''}`;
  $('space-up').disabled = !path; $('space-upload').hidden = !space.canUpload;
  $('space-entries').replaceChildren();
  if (!entries.length) { const empty = document.createElement('div'); empty.className = 'empty'; empty.textContent = 'This folder is empty.'; $('space-entries').append(empty); }
  for (const entry of entries) {
    const row = document.createElement('div'); row.className = entry.directory ? 'file-row folder' : photo(entry.name) ? 'file-row photo' : 'file-row';
    const label = document.createElement('div'); label.className = 'file-name'; label.textContent = entry.name;
    const action = document.createElement(entry.directory ? 'button' : 'a'); action.className = entry.directory ? 'b sm' : 'b q sm';
    if (entry.directory) { action.type = 'button'; action.textContent = 'Open'; action.addEventListener('click', () => openSpace(space, entry.path).catch(error => notice(error.message, true))); }
    else { action.textContent = `Download · ${bytes(entry.size)}`; action.href = `/api/spaces/${encodeURIComponent(space.id)}/download?path=${encodeURIComponent(entry.path)}`; }
    row.append(label, action); $('space-entries').append(row);
  }
  updateTarget();
}
$('spaces-refresh').addEventListener('click', () => refreshSpaces().catch(error => notice(error.message, true)));
async function copyNote(content) {
  try {
    if (globalThis.navigator?.clipboard && window.isSecureContext) await navigator.clipboard.writeText(content);
    else {
      const field = document.createElement('textarea'); field.value = content; field.className = 'copy-helper'; document.body.append(field);
      try { field.select(); if (!document.execCommand('copy')) throw new Error('Select and copy the note manually.'); } finally { field.remove(); }
    }
    notice('Copied.');
  } catch (error) { notice(error.message || 'Select and copy the note manually.', true); }
}
async function refreshNotes() {
  const snapshot = await request('/api/notes?revision=' + encodeURIComponent(notesRevision));
  if (snapshot.revision === notesRevision) return;
  notesRevision = snapshot.revision;
  $('notes-list').replaceChildren();
  $('notes-count').textContent = snapshot.notes.length; $('notes-count').hidden = !snapshot.notes.length;
  if (!snapshot.notes.length) { const empty = document.createElement('div'); empty.className = 'empty'; empty.textContent = 'Shared text and links appear here.'; $('notes-list').append(empty); }
  for (const note of snapshot.notes) {
    const row = document.createElement('div'); row.className = note.kind === 'link' ? 'note-row link' : 'note-row';
    const text = document.createElement('div'); text.className = 'note-content'; text.textContent = note.content;
    const meta = document.createElement('div'); meta.className = 'file-meta'; meta.textContent = `From ${note.sender} · ${new Date(note.createdUtc).toLocaleString([], { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' })}`;
    const actions = document.createElement('div'); actions.className = 'actions';
    const copy = document.createElement('button'); copy.type = 'button'; copy.className = 'b sm'; copy.textContent = 'Copy'; copy.addEventListener('click', () => void copyNote(note.content)); actions.append(copy);
    // Link detection is repeated on the client. No received HTML or executable URL is evaluated.
    if (note.kind === 'link') {
      try { const uri = new URL(note.content); if (['http:', 'https:'].includes(uri.protocol)) { const link = document.createElement('a'); link.href = uri.href; link.target = '_blank'; link.rel = 'noopener noreferrer'; link.className = 'b q sm'; link.textContent = 'Open'; actions.append(link); } } catch { }
    }
    row.append(text, meta, actions); $('notes-list').append(row);
  }
}
$('notes-refresh').addEventListener('click', () => refreshNotes().catch(error => notice(error.message, true)));
$('note-form').addEventListener('submit', async event => {
  event.preventDefault(); const content = $('note-content').value; $('note-send').disabled = true;
  try {
    await request('/api/notes', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ content }) });
    if ($('note-content').value === content) $('note-content').value = '';
    notice('Shared with this PC.'); await refreshNotes();
  } catch (error) { notice(error.message, true); }
  finally { $('note-send').disabled = false; }
});
async function pollFiles() { try { if (!$('sharing').hidden && !document.hidden && !busy) await refreshFiles(); } catch { } finally { setTimeout(pollFiles, 8000); } }
setTimeout(pollFiles, 8000);
async function pollNotes() { try { if (!$('sharing').hidden && !document.hidden) await refreshNotes(); } catch { } finally { setTimeout(pollNotes, 5000); } }
setTimeout(pollNotes, 5000);
async function refreshBrowserDevices() {
  const devices = await request('/api/devices');
  $('browser-devices').replaceChildren();
  for (const device of devices) {
    try {
      const url = new URL(device.url);
      if (url.protocol !== 'http:' || (url.port && url.port !== '80') || !/^share-wollvo-[a-f0-9]{16}(?:-\d{1,2})?\.local$/i.test(url.hostname) || url.username || url.password) continue;
      const link = document.createElement('a'); link.className = 'space-card'; link.href = url.href; link.textContent = device.name; $('browser-devices').append(link);
    } catch { }
  }
  $('devices-card').hidden = $('browser-devices').children.length === 0;
}
async function pollBrowserDevices() { try { if (!$('sharing').hidden && !document.hidden) await refreshBrowserDevices(); } catch { } finally { setTimeout(pollBrowserDevices, 15000); } }
setTimeout(pollBrowserDevices, 15000);
$('space-up').addEventListener('click', () => { if (activeSpace && !busy) { const parent = activeFolder.includes('/') ? activeFolder.slice(0, activeFolder.lastIndexOf('/')) : ''; void openSpace(activeSpace, parent).catch(error => notice(error.message, true)); } });
$('inbox').addEventListener('click', () => { if (busy) return; spaceLoad++; activeSpace = null; activeFolder = ''; $('space-browser').hidden = true; updateTarget(); });
$('space-upload').addEventListener('click', () => showTab('send'));
async function refreshTransfers() {
  const transfers = await request('/api/transfers');
  $('live-transfers').replaceChildren();
  if (!transfers.length) { const empty = document.createElement('div'); empty.className = 'empty'; empty.textContent = 'Progress, speed and time left appear here while files move.'; $('live-transfers').append(empty); }
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
  // Add more adds to the list; the same file picked again is not added twice.
  const same = (a, b) => a.name === b.name && a.size === b.size && a.lastModified === b.lastModified;
  for (const file of Array.from($('files').files)) if (!selected.some(chosen => same(chosen, file))) selected.push(file);
  $('files').value = '';
  selectionChanged();
});
function selectionChanged() {
  showSelection();
  const invalid = selected.length > 100 || selected.some(file => file.size > 2147483648) || selected.reduce((sum, file) => sum + file.size, 0) > 10737418240;
  updateTarget();
  notice(invalid ? 'Choose up to 100 files, 2 GiB each, and 10 GiB total.' : '', invalid);
}
// Chosen files replace the picker with a list, its total, Add more and Clear. Each row has × to remove that file.
function showSelection() {
  $('selection').replaceChildren();
  selected.forEach((file, index) => {
    const row = document.createElement('li'); row.className = photo(file.name) ? 'file-row photo' : 'file-row';
    const details = document.createElement('div');
    const name = document.createElement('div'); name.className = 'file-name'; name.textContent = file.name;
    const meta = document.createElement('div'); meta.className = 'file-meta'; meta.textContent = `${fileKind(file.name)} · ${bytes(file.size)}`;
    const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'b q sm remove'; remove.textContent = '×'; remove.disabled = busy; remove.setAttribute('aria-label', 'Remove ' + file.name);
    remove.addEventListener('click', () => { if (busy) return; selected.splice(index, 1); selectionChanged(); });
    details.append(name, meta); row.append(details, remove); $('selection').append(row);
  });
  $('picker').hidden = selected.length > 0; $('selection-head').hidden = !selected.length;
  $('selection-summary').textContent = `${selected.length} ${selected.length === 1 ? 'file' : 'files'} · ${bytes(selected.reduce((sum, file) => sum + file.size, 0))}`;
}
$('selection-clear').addEventListener('click', () => { if (busy) return; selected = []; $('files').value = ''; selectionChanged(); });
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
    notice('Done. Your files are saved on the PC.'); selected = []; $('files').value = ''; showSelection(); offerId = '';
  } catch (error) {
    notice(error.message, true); $('transfer-label').textContent = 'Transfer stopped. You can choose files and try again.';
    if (offerId) await request(`/api/uploads/${encodeURIComponent(offerId)}/cancel`, { method: 'POST' }).catch(() => {});
  } finally { busy = false; activeUpload = null; offerAbort = null; $('cancel').hidden = true; updateTarget(); void refreshTransfers().catch(() => {}); if (activeSpace) void openSpace(activeSpace, activeFolder).catch(() => {}); }
});
$('cancel').addEventListener('click', () => { cancelled = true; if (activeUpload) activeUpload.abort(); if (offerAbort) offerAbort.abort(); $('transfer-label').textContent = 'Cancelling…'; });
connect();

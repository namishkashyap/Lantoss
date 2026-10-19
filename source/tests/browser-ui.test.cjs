'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'Browser', 'portal.js'), 'utf8');
class Element {
  constructor() { this.hidden = false; this.disabled = false; this.value = ''; this.textContent = ''; this.children = []; this.listeners = {}; }
  addEventListener(name, callback) { (this.listeners[name] ??= []).push(callback); }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
}
async function fixture(protectedMode, options = {}) {
  const elements = new Map();
  const element = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
  element('sharing').hidden = true; element('login').hidden = true;
  const state = { protectedMode, connected: !protectedMode, networkFailure: false, spaces: [], entries: [], transfers: [], windowsDark: !!options.dark, savedAppearance: options.saved ?? null };
  const systemChanges = [];
  const systemAppearance = { get matches() { return state.windowsDark; }, addEventListener: (event, callback) => systemChanges.push(callback) };
  const root = { dataset: {} };
  const sandbox = {
    document: { getElementById: element, createElement: () => new Element(), documentElement: root }, AbortController,
    window: { matchMedia: () => systemAppearance },
    localStorage: { getItem: () => { if (options.blockedStorage) throw Error('Storage blocked'); return state.savedAppearance; }, setItem: (key, value) => { if (options.blockedStorage) throw Error('Storage blocked'); state.savedAppearance = value; } },
    URLSearchParams, location: { search: '' }, performance, setTimeout: () => 0,
    fetch: async (url, options = {}) => {
      if (state.networkFailure) throw new Error('Network unavailable');
      let status = 200, body;
      if (url === '/api/session') body = { connected: state.connected, requiresCode: state.protectedMode, csrf: state.connected ? 'csrf-token' : null, name: 'Test PC' };
      else if (url === '/api/files') body = [];
      else if (url === '/api/spaces') body = state.spaces;
      else if (url === '/api/transfers') body = state.transfers;
      else if (/^\/api\/spaces\/[^/]+\/entries\?path=/.test(url)) body = state.entries;
      else if (url === '/api/login') {
        if (JSON.parse(options.body).code === 'ABCDE-FG234') { state.connected = true; body = { connected: true, csrf: 'csrf-token' }; }
        else { status = 401; body = { error: 'The code is incorrect, expired, or already used.' }; }
      } else throw new Error('Unexpected route ' + url);
      return { status, ok: status >= 200 && status < 300, json: async () => body };
    },
  };
  vm.createContext(sandbox); vm.runInContext(source, sandbox);
  await new Promise(resolve => setImmediate(resolve));
  return { element, state, sandbox, root, systemChanged: dark => { state.windowsDark = dark; systemChanges.forEach(callback => callback()); } };
}
(async () => {
  const direct = await fixture(false);
  assert.equal(direct.element('sharing').hidden, false); assert.equal(direct.element('login').hidden, true);
  assert.equal(direct.element('notice').textContent, '');
  console.log('PASS Browser opens directly without showing a code form when protection is off');
  const protectedPage = await fixture(true);
  assert.equal(protectedPage.element('login').hidden, false);
  protectedPage.element('code').value = 'MY-DRAFT'; await protectedPage.sandbox.connect();
  assert.equal(protectedPage.element('code').value, 'MY-DRAFT');
  console.log('PASS Session refresh preserves a code being typed');
  protectedPage.element('code').value = 'wrong';
  await protectedPage.element('login-form').listeners.submit[0]({ preventDefault() {} });
  assert.equal(protectedPage.element('code').value, 'wrong'); assert.equal(protectedPage.element('connect').disabled, false);
  protectedPage.state.networkFailure = true;
  await protectedPage.element('login-form').listeners.submit[0]({ preventDefault() {} });
  assert.equal(protectedPage.element('code').value, 'wrong');
  console.log('PASS Incorrect codes and network failures preserve the input for correction');
  protectedPage.state.networkFailure = false; protectedPage.element('code').value = 'ABCDE-FG234';
  await protectedPage.element('login-form').listeners.submit[0]({ preventDefault() {} });
  assert.equal(protectedPage.element('code').value, ''); assert.equal(protectedPage.element('sharing').hidden, false); assert.equal(protectedPage.element('login').hidden, true);
  console.log('PASS Successful optional-code login enters sharing and clears the submitted code');
  const sharing = await fixture(false);
  sharing.element('files').files = [{ name: 'photo.jpg', size: 1024 }];
  sharing.element('files').listeners.change[0]();
  await sharing.sandbox.openSpace({ id: 'space-one', name: 'Family', canUpload: false });
  assert.equal(sharing.element('files').disabled, true); assert.equal(sharing.element('send').disabled, true);
  await sharing.sandbox.openSpace({ id: 'space-one', name: 'Family', canUpload: true }, 'Photos');
  assert.equal(sharing.element('files').disabled, false); assert.equal(sharing.element('send').disabled, false);
  assert.equal(sharing.element('selection').children.length, 1);
  assert.match(sharing.element('upload-hint').textContent, /Photos/);
  sharing.element('inbox').listeners.click[0]();
  assert.equal(sharing.element('space-browser').hidden, true); assert.equal(sharing.element('send-title').textContent, 'Send to your PC');
  console.log('PASS Shared-space permissions control uploads, preserve selected files, and allow returning to the PC inbox');
  sharing.state.transfers = [{ direction: 'Sent', title: 'movie.mp4', state: 'Sending', bytes: 524288, total: 1048576, bytesPerSecond: 524288, remainingSeconds: 1, average: false }];
  await sharing.sandbox.refreshTransfers();
  const row = sharing.element('live-transfers').children[0];
  assert.equal(row.children[0].textContent, 'Download · movie.mp4');
  assert.equal(row.children[2].textContent, 'Current: 512.0 KB/s · ~1s left');
  assert.equal(row.children[3].value, 50);
  sharing.state.transfers[0] = { ...sharing.state.transfers[0], state: 'Completed', bytes: 1048576, average: true, remainingSeconds: null };
  await sharing.sandbox.refreshTransfers();
  assert.equal(sharing.element('live-transfers').children[0].children[2].textContent, 'Average: 512.0 KB/s');
  console.log('PASS Browser transfer activity displays measured speed, remaining time, progress, and completed averages');
  const themed = await fixture(false);
  assert.equal(themed.root.dataset.theme, 'light');
  assert.equal(themed.element('appearance').value, 'System');
  themed.systemChanged(true); assert.equal(themed.root.dataset.theme, 'dark');
  console.log('PASS System appearance follows live device preference changes');
  themed.element('appearance').value = 'Light'; themed.element('appearance').listeners.change[0]();
  themed.systemChanged(false); themed.systemChanged(true);
  assert.equal(themed.root.dataset.theme, 'light'); assert.equal(themed.state.savedAppearance, 'Light');
  const reopened = await fixture(false, { dark: true, saved: themed.state.savedAppearance });
  assert.equal(reopened.root.dataset.theme, 'light');
  console.log('PASS Explicit Light overrides the system and survives reopening');
  themed.element('appearance').value = 'Dark'; themed.element('appearance').listeners.change[0]();
  themed.systemChanged(false); assert.equal(themed.root.dataset.theme, 'dark');
  themed.element('appearance').value = 'System'; themed.element('appearance').listeners.change[0]();
  assert.equal(themed.root.dataset.theme, 'light');
  console.log('PASS Explicit Dark is independent and switching back to System resumes following');
  const blocked = await fixture(false, { dark: true, blockedStorage: true });
  assert.equal(blocked.element('sharing').hidden, false); assert.equal(blocked.root.dataset.theme, 'dark');
  blocked.element('appearance').value = 'Light'; blocked.element('appearance').listeners.change[0]();
  assert.equal(blocked.root.dataset.theme, 'light'); assert.equal(blocked.element('sharing').hidden, false);
  console.log('PASS Blocked browser storage still allows appearance changes and file sharing');
  const invalid = await fixture(false, { saved: 'obsolete-theme', dark: true });
  assert.equal(invalid.element('appearance').value, 'System'); assert.equal(invalid.root.dataset.theme, 'dark');
  console.log('PASS Invalid saved appearance falls back to System');
  console.log('11 browser UI-state regression checks passed. These use a DOM harness, not a rendered browser.');
})().catch(error => { console.error(error); process.exitCode = 1; });

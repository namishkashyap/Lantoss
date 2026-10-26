#!/usr/bin/env node
'use strict';
/*
 * Checks the packages built by build-macos.ps1 and build-linux.ps1. It parses each archive itself
 * (zip central directory, tar headers, ar members, plist XML, icns, Mach-O, ELF) instead of reusing pack.cjs's writers.
 *   node verify.cjs <file.zip|file.tar.gz|file.deb>... [--arch arm64|x64] [--version 2.0.0] [--list]
 * --arch defaults to a guess from the file name. Exits with code 1 when any check fails.
 */
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const crypto = require('crypto');
const { EXECUTABLE_NAMES, MAINTAINER_SCRIPTS } = require('./pack.cjs');

const DEB_DEPENDS = ['libc6', 'libgcc-s1', 'libstdc++6', 'zlib1g', 'libssl3', 'libssl1.1', 'libfontconfig1', 'libx11-6', 'libice6', 'libsm6'];
const ICNS_SIZES = { icp4: 16, icp5: 32, ic11: 32, ic12: 64, ic07: 128, ic13: 256, ic08: 256, ic14: 512, ic09: 512, ic10: 1024 };
const ARCHES = {
  arm64: { macho: 'arm64', elf: 183, deb: 'arm64', linuxScript: 'aarch64', macScript: 'arm64' },
  x64: { macho: 'x86_64', elf: 62, deb: 'amd64', linuxScript: 'x86_64', macScript: 'x86_64' },
};
const TEXT_FILE = /\.(sh|desktop|plist)$|(^|\/)(control|postinst|postrm|preinst|prerm|md5sums|PkgInfo)$/;

// ---------- readers ----------

function readZip(buffer) {
  let end = -1;
  for (let i = buffer.length - 22; i >= Math.max(0, buffer.length - 65557); i--) {
    if (buffer.readUInt32LE(i) === 0x06054b50) { end = i; break; }
  }
  if (end < 0) throw new Error('zip: end of central directory not found');
  const count = buffer.readUInt16LE(end + 10);
  const entries = [];
  let p = buffer.readUInt32LE(end + 16);
  for (let n = 0; n < count; n++) {
    if (buffer.readUInt32LE(p) !== 0x02014b50) throw new Error(`zip: bad central directory record ${n}`);
    const madeBy = buffer.readUInt16LE(p + 4), method = buffer.readUInt16LE(p + 10), crc = buffer.readUInt32LE(p + 16);
    const compressedSize = buffer.readUInt32LE(p + 20), size = buffer.readUInt32LE(p + 24);
    const nameLength = buffer.readUInt16LE(p + 28), extraLength = buffer.readUInt16LE(p + 30), commentLength = buffer.readUInt16LE(p + 32);
    const external = buffer.readUInt32LE(p + 38), local = buffer.readUInt32LE(p + 42);
    const rawName = buffer.toString('utf8', p + 46, p + 46 + nameLength);
    p += 46 + nameLength + extraLength + commentLength;
    if (buffer.readUInt32LE(local) !== 0x04034b50) throw new Error(`zip: bad local header for ${rawName}`);
    const localNameLength = buffer.readUInt16LE(local + 26);
    if (buffer.toString('utf8', local + 30, local + 30 + localNameLength) !== rawName) throw new Error(`zip: local name differs for ${rawName}`);
    const start = local + 30 + localNameLength + buffer.readUInt16LE(local + 28);
    const stored = buffer.subarray(start, start + compressedSize);
    const data = method === 8 ? zlib.inflateRawSync(stored) : method === 0 ? stored : null;
    if (!data) throw new Error(`zip: ${rawName} uses unsupported method ${method}`);
    if (data.length !== size || (zlib.crc32 && (zlib.crc32(data) >>> 0) !== crc)) throw new Error(`zip: ${rawName} fails its size or CRC check`);
    const unixMode = external >>> 16, kind = unixMode & 0o170000;
    const type = kind === 0o040000 ? 'dir' : kind === 0o120000 ? 'symlink' : kind === 0o100000 ? 'file' : `mode 0${kind.toString(8)}`;
    if ((type === 'dir') !== rawName.endsWith('/')) throw new Error(`zip: ${rawName} name and directory mode disagree`);
    entries.push({ path: rawName.replace(/\/$/, ''), type, mode: unixMode & 0o7777, unix: madeBy >> 8 === 3, method, size, data, target: type === 'symlink' ? data.toString('utf8') : '' });
  }
  return entries;
}

function readTar(buffer) {
  const entries = [];
  let p = 0, pax = {};
  const text = (offset, length) => {
    const field = buffer.subarray(offset, offset + length);
    const zero = field.indexOf(0);
    return field.toString('utf8', 0, zero < 0 ? length : zero);
  };
  const octal = (offset, length) => parseInt(text(offset, length).trim() || '0', 8);
  while (p + 512 <= buffer.length) {
    const base = p;
    const header = buffer.subarray(base, base + 512);
    if (header.every((byte) => byte === 0)) break;
    let sum = 0;
    for (let i = 0; i < 512; i++) sum += i >= 148 && i < 156 ? 0x20 : header[i];
    if (sum !== octal(base + 148, 8)) throw new Error(`tar: bad header checksum at offset ${base}`);
    if (text(base + 257, 6) !== 'ustar') throw new Error(`tar: header at offset ${base} is not ustar`);
    const typeflag = header[156] ? String.fromCharCode(header[156]) : '0';
    const size = octal(base + 124, 12);
    const data = buffer.subarray(base + 512, base + 512 + size);
    p = base + 512 + Math.ceil(size / 512) * 512;
    if (typeflag === 'x') {
      for (const record of data.toString('utf8').split('\n').filter(Boolean)) {
        const match = /^\d+ ([^=]+)=(.*)$/s.exec(record);
        if (match) pax[match[1]] = match[2];
      }
      continue;
    }
    if (typeflag === 'g') continue;
    const prefix = text(base + 345, 155);
    const rawName = pax.path || (prefix ? `${prefix}/${text(base, 100)}` : text(base, 100));
    const target = pax.linkpath || text(base + 157, 100);
    pax = {};
    const type = { 0: 'file', 5: 'dir', 2: 'symlink' }[typeflag] || `typeflag ${typeflag}`;
    entries.push({
      path: rawName === './' ? '.' : rawName.replace(/\/$/, ''), rawName, type, mode: octal(base + 100, 8) & 0o7777,
      uid: octal(base + 108, 8), gid: octal(base + 116, 8), owner: text(base + 265, 32), size, data: Buffer.from(data), target: type === 'symlink' ? target : '',
    });
  }
  return entries;
}

function readAr(buffer) {
  if (buffer.toString('ascii', 0, 8) !== '!<arch>\n') throw new Error('ar: bad magic');
  const members = [];
  for (let p = 8; p < buffer.length;) {
    const header = buffer.toString('ascii', p, p + 60);
    if (header.length < 60 || header.slice(58) !== '`\n') throw new Error(`ar: bad member header at offset ${p}`);
    const size = parseInt(header.slice(48, 58).trim(), 10);
    members.push({ name: header.slice(0, 16).trim().replace(/\/$/, ''), mode: header.slice(40, 48).trim(), size, data: buffer.subarray(p + 60, p + 60 + size) });
    p += 60 + size + (size % 2);
  }
  return members;
}

function pngSize(buffer) {
  if (!buffer || buffer.length < 45 || buffer.readUInt32BE(0) !== 0x89504e47 || buffer.toString('ascii', 12, 16) !== 'IHDR') return null;
  if (buffer.toString('ascii', buffer.length - 8, buffer.length - 4) !== 'IEND') return null;
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

function versionString(value) {
  const patch = value & 0xff;
  return `${value >>> 16}.${(value >>> 8) & 0xff}${patch ? `.${patch}` : ''}`;
}

function compareVersions(a, b) {
  const x = String(a).split('.').map(Number), y = String(b).split('.').map(Number);
  for (let i = 0; i < Math.max(x.length, y.length); i++) if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) - (y[i] || 0);
  return 0;
}

/** Returns [{cpu, minos, signed}] for a thin or universal Mach-O, or null for other files. */
function machO(buffer) {
  const thin = (b) => {
    if (b.length < 32 || b.readUInt32LE(0) !== 0xfeedfacf) return null;
    const cpuType = b.readUInt32LE(4);
    const result = { cpu: { 0x01000007: 'x86_64', 0x0100000c: 'arm64' }[cpuType] || `0x${cpuType.toString(16)}`, minos: null, signed: false };
    for (let i = 0, p = 32, count = b.readUInt32LE(16); i < count && p + 8 <= b.length; i++) {
      const command = b.readUInt32LE(p), size = b.readUInt32LE(p + 4);
      if (command === 0x32) result.minos = versionString(b.readUInt32LE(p + 12)); // LC_BUILD_VERSION
      else if (command === 0x24 && !result.minos) result.minos = versionString(b.readUInt32LE(p + 8)); // LC_VERSION_MIN_MACOSX
      else if (command === 0x1d) result.signed = true; // LC_CODE_SIGNATURE
      if (size < 8) break;
      p += size;
    }
    return result;
  };
  if (buffer.length >= 8 && buffer.readUInt32BE(0) === 0xcafebabe) {
    const count = buffer.readUInt32BE(4);
    if (count === 0 || count > 16) return null;
    const slices = [];
    for (let i = 0; i < count; i++) {
      const offset = buffer.readUInt32BE(8 + i * 20 + 8), size = buffer.readUInt32BE(8 + i * 20 + 12);
      const slice = thin(buffer.subarray(offset, offset + size));
      if (slice) slices.push(slice);
    }
    return slices.length ? slices : null;
  }
  const single = thin(buffer);
  return single ? [single] : null;
}

function elfMachine(buffer) {
  if (buffer.length < 20 || buffer.readUInt32BE(0) !== 0x7f454c46 || buffer[4] !== 2 || buffer[5] !== 1) return null;
  return buffer.readUInt16LE(18);
}

function parseXml(source) {
  const documentNode = { name: '#document', children: [] };
  const stack = [documentNode];
  let i = 0;
  const fail = (message) => { throw new Error(`XML: ${message} at offset ${i}`); };
  const entity = /&(amp|lt|gt|quot|apos|#\d+|#x[0-9a-fA-F]+);/g;
  const decode = (value) => {
    if (/&(?!(amp|lt|gt|quot|apos|#\d+|#x[0-9a-fA-F]+);)/.test(value)) fail('bad entity');
    return value.replace(entity, (_, name) => ({ amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" })[name]
      || String.fromCodePoint(name[1] === 'x' ? parseInt(name.slice(2), 16) : parseInt(name.slice(1), 10)));
  };
  const hasRoot = () => documentNode.children.some((child) => typeof child !== 'string');
  while (i < source.length) {
    if (source.startsWith('<?', i)) {
      if (source.startsWith('<?xml', i) && i !== 0) fail('XML declaration is not at the start');
      const close = source.indexOf('?>', i);
      if (close < 0) fail('unterminated processing instruction');
      i = close + 2;
    } else if (source.startsWith('<!--', i)) {
      const close = source.indexOf('-->', i);
      if (close < 0) fail('unterminated comment');
      i = close + 3;
    } else if (source.startsWith('<!DOCTYPE', i)) {
      if (stack.length > 1 || hasRoot()) fail('DOCTYPE after the root element');
      const close = source.indexOf('>', i);
      if (close < 0) fail('unterminated DOCTYPE');
      i = close + 1;
    } else if (source.startsWith('<![CDATA[', i)) {
      const close = source.indexOf(']]>', i);
      if (close < 0 || stack.length === 1) fail('misplaced CDATA');
      stack[stack.length - 1].children.push(source.slice(i + 9, close));
      i = close + 3;
    } else if (source[i] === '<') {
      const close = source.indexOf('>', i);
      if (close < 0) fail('unterminated tag');
      const inner = source.slice(i + 1, close);
      if (inner.startsWith('/')) {
        const name = inner.slice(1).trim();
        const open = stack.pop();
        if (!stack.length || open.name !== name) fail(`</${name}> does not match <${open.name}>`);
      } else {
        const selfClosing = inner.endsWith('/');
        const match = /^([A-Za-z_][\w.-]*)((?:\s+[A-Za-z_][\w.:-]*\s*=\s*(?:"[^"<]*"|'[^'<]*'))*)\s*$/.exec(selfClosing ? inner.slice(0, -1) : inner);
        if (!match) fail(`malformed tag <${inner}>`);
        if (stack.length === 1 && hasRoot()) fail('second root element');
        const element = { name: match[1], children: [] };
        stack[stack.length - 1].children.push(element);
        if (!selfClosing) stack.push(element);
      }
      i = close + 1;
    } else {
      const next = source.indexOf('<', i);
      const chunk = source.slice(i, next < 0 ? source.length : next);
      if (stack.length === 1) { if (chunk.trim()) fail('text outside the root element'); }
      else stack[stack.length - 1].children.push(decode(chunk));
      i = next < 0 ? source.length : next;
    }
  }
  if (stack.length !== 1) fail(`<${stack[stack.length - 1].name}> is never closed`);
  const roots = documentNode.children.filter((child) => typeof child !== 'string');
  if (roots.length !== 1) fail('there is no root element');
  return roots[0];
}

function parsePlist(source) {
  const elements = (node) => node.children.filter((child) => typeof child !== 'string');
  const textOf = (node) => node.children.filter((child) => typeof child === 'string').join('');
  const value = (node) => {
    const kids = elements(node);
    switch (node.name) {
      case 'dict': {
        if (kids.length % 2) throw new Error('plist: <dict> has a key without a value');
        const result = {};
        for (let k = 0; k < kids.length; k += 2) {
          if (kids[k].name !== 'key') throw new Error(`plist: expected <key>, found <${kids[k].name}>`);
          result[textOf(kids[k])] = value(kids[k + 1]);
        }
        return result;
      }
      case 'array': return kids.map(value);
      case 'string': case 'date': case 'data': return textOf(node);
      case 'integer': case 'real': {
        const number = Number(textOf(node).trim());
        if (!Number.isFinite(number)) throw new Error(`plist: bad <${node.name}>`);
        return number;
      }
      case 'true': return true;
      case 'false': return false;
      default: throw new Error(`plist: unexpected <${node.name}>`);
    }
  };
  const root = parseXml(source);
  if (root.name !== 'plist' || elements(root).length !== 1) throw new Error('plist: root must be <plist> with one value');
  return value(elements(root)[0]);
}

function parseControl(text) {
  const fields = {};
  let last = null;
  for (const line of text.replace(/\n$/, '').split('\n')) {
    if (/^[ \t]/.test(line) && last) fields[last] += `\n${line}`;
    else {
      const match = /^([A-Za-z0-9][A-Za-z0-9-]*):\s*(.*)$/.exec(line);
      if (!match) throw new Error(`control: bad line "${line}"`);
      last = match[1];
      fields[last] = match[2];
    }
  }
  return fields;
}

// ---------- checks ----------

function modeText(mode) { return `0${mode.toString(8).padStart(3, '0')}`; }

function permissions(entry) {
  const kind = entry.type === 'dir' ? 'd' : entry.type === 'symlink' ? 'l' : '-';
  let bits = '';
  for (let shift = 6; shift >= 0; shift -= 3) {
    const value = (entry.mode >> shift) & 7;
    bits += (value & 4 ? 'r' : '-') + (value & 2 ? 'w' : '-') + (value & 1 ? 'x' : '-');
  }
  return kind + bits;
}

function checkCommon(entries, check, { scripts = [] } = {}) {
  const seen = new Set();
  for (const entry of entries) {
    check(!seen.has(entry.path), `${entry.path} appears once`);
    seen.add(entry.path);
    const name = path.posix.basename(entry.path);
    if (entry.type === 'dir') check(entry.mode === 0o755, `${entry.path}/ is 0755 (is ${modeText(entry.mode)})`);
    else if (entry.type === 'file') {
      const wanted = EXECUTABLE_NAMES.includes(name) || name.endsWith('.sh') || scripts.includes(name) ? 0o755 : 0o644;
      check(entry.mode === wanted, `${entry.path} is ${modeText(wanted)} (is ${modeText(entry.mode)})`);
      if (TEXT_FILE.test(entry.path)) {
        const text = entry.data.toString('utf8');
        check(!text.includes('\r'), `${entry.path} has LF line endings`);
        check(!/@[A-Z_]+@/.test(text), `${entry.path} has no unexpanded @PLACEHOLDER@`);
        if (name.endsWith('.sh') || scripts.includes(name)) check(text.startsWith('#!/bin/sh\n'), `${entry.path} starts with #!/bin/sh`);
      }
    } else if (entry.type === 'symlink') check(entry.target.length > 0, `${entry.path} has a symlink target`);
    else check(false, `${entry.path} has an unexpected ${entry.type}`);
  }
}

function checkDesktopEntry(text, check, label) {
  const lines = text.split('\n');
  check(lines[0] === '[Desktop Entry]', `${label} starts with [Desktop Entry]`);
  for (const pair of ['Type=Application', 'Name=Lantoss', 'Icon=lantoss', 'Terminal=false', 'Categories=Network;FileTransfer;', 'StartupWMClass=Lantoss']) {
    check(lines.includes(pair), `${label} has ${pair}`);
  }
  check(lines.some((line) => /^Exec=\S/.test(line)), `${label} has an Exec line`);
}

function verifyMacZip(entries, arch, version, check, notes) {
  const exists = (p) => entries.find((entry) => entry.path === p);
  check(entries.every((entry) => entry.unix), 'every zip entry is marked "made by Unix", so macOS applies the modes');
  checkCommon(entries, check);
  const app = entries.find((entry) => entry.type === 'dir' && path.posix.basename(entry.path) === 'Lantoss.app');
  if (!check(app, 'the zip contains Lantoss.app')) return;
  const contents = `${app.path}/Contents`;
  const plistEntry = exists(`${contents}/Info.plist`);
  if (!check(plistEntry, 'Contents/Info.plist exists')) return;
  let plist;
  try { plist = parsePlist(plistEntry.data.toString('utf8')); } catch (error) { check(false, `Info.plist parses: ${error.message}`); return; }
  notes.push('Info.plist is well-formed XML and a valid property list');
  const expected = {
    CFBundleExecutable: 'Lantoss', CFBundleIdentifier: 'io.github.namishkashyap.lantoss', CFBundleIconFile: 'Lantoss', CFBundlePackageType: 'APPL',
    NSHighResolutionCapable: true, LSApplicationCategoryType: 'public.app-category.utilities',
  };
  if (version) Object.assign(expected, { CFBundleShortVersionString: version });
  for (const [key, value] of Object.entries(expected)) check(plist[key] === value, `Info.plist ${key} is ${value} (is ${plist[key]})`);
  check(/^\d+\.\d+(\.\d+)?$/.test(plist.CFBundleVersion || ''), 'Info.plist CFBundleVersion is numeric');
  check(typeof plist.NSLocalNetworkUsageDescription === 'string' && plist.NSLocalNetworkUsageDescription.length > 20, 'Info.plist has NSLocalNetworkUsageDescription');
  check(Array.isArray(plist.NSBonjourServices) && plist.NSBonjourServices.length > 0 && plist.NSBonjourServices.every((s) => /^_[a-z0-9-]+\._(tcp|udp)$/.test(s)), 'Info.plist NSBonjourServices lists _service._tcp types');
  const minimum = plist.LSMinimumSystemVersion;
  check(/^\d+\.\d+(\.\d+)?$/.test(minimum || ''), 'Info.plist has LSMinimumSystemVersion');

  const executable = exists(`${contents}/MacOS/${plist.CFBundleExecutable}`);
  if (check(executable && executable.type === 'file', 'Contents/MacOS/Lantoss exists')) {
    check(executable.mode === 0o755, 'Contents/MacOS/Lantoss is 0755');
    const slices = machO(executable.data);
    check(slices && slices.some((slice) => slice.cpu === ARCHES[arch].macho), `Contents/MacOS/Lantoss is a ${ARCHES[arch].macho} Mach-O`);
    if (slices) notes.push(`apphost: ${slices.map((s) => `${s.cpu} minos ${s.minos}${s.signed ? ', ad hoc/embedded signature' : ', unsigned'}`).join('; ')}`);
  }
  let newest = { minos: '0', file: '' }, count = 0;
  const unsigned = [];
  for (const entry of entries.filter((e) => e.type === 'file' && e.path.startsWith(`${contents}/MacOS/`))) {
    const slices = machO(entry.data);
    if (!slices) continue;
    count++;
    const name = path.posix.basename(entry.path);
    check(slices.some((slice) => slice.cpu === ARCHES[arch].macho), `${name} contains ${ARCHES[arch].macho} code`);
    for (const slice of slices.filter((s) => s.cpu === ARCHES[arch].macho)) {
      if (slice.minos && compareVersions(slice.minos, newest.minos) > 0) newest = { minos: slice.minos, file: name };
      if (!slice.signed) unsigned.push(name);
    }
  }
  check(compareVersions(newest.minos, minimum) <= 0, `no Mach-O needs a newer macOS than LSMinimumSystemVersion ${minimum} (${newest.file} needs ${newest.minos})`);
  notes.push(`LSMinimumSystemVersion ${minimum}; ${count} Mach-O files, newest deployment target ${newest.minos} (${newest.file})${unsigned.length ? `; unsigned: ${unsigned.join(', ')}` : '; all carry a signature'}`);

  const icon = exists(`${contents}/Resources/${plist.CFBundleIconFile}${plist.CFBundleIconFile.endsWith('.icns') ? '' : '.icns'}`);
  if (check(icon, 'Contents/Resources/Lantoss.icns exists')) {
    const data = icon.data;
    check(data.toString('ascii', 0, 4) === 'icns' && data.readUInt32BE(4) === data.length, 'icns header length matches the file');
    const types = [];
    for (let p = 8; p + 8 <= data.length;) {
      const type = data.toString('ascii', p, p + 4), length = data.readUInt32BE(p + 4);
      if (!check(length > 8 && p + length <= data.length, `icns chunk ${type} fits in the file`)) break;
      const size = pngSize(data.subarray(p + 8, p + length));
      check(size && size.width === ICNS_SIZES[type] && size.height === size.width, `icns ${type} holds a ${ICNS_SIZES[type]}px PNG`);
      types.push(type);
      p += length;
    }
    check(['ic07', 'ic08', 'ic09', 'ic10', 'ic11', 'ic12', 'ic13', 'ic14'].every((t) => types.includes(t)), 'icns has ic07-ic10 and the retina ic11-ic14');
    notes.push(`icns types: ${types.join(' ')}`);
  }
  const install = exists(`${path.posix.dirname(app.path) === '.' ? '' : `${path.posix.dirname(app.path)}/`}install.sh`);
  if (check(install, 'install.sh sits next to Lantoss.app')) {
    const text = install.data.toString('utf8');
    check(text.includes(`APP_ARCH="${ARCHES[arch].macScript}"`), `install.sh targets ${ARCHES[arch].macScript}`);
    check(text.includes('xattr -dr com.apple.quarantine') && text.includes('codesign --force --deep --sign -'), 'install.sh removes quarantine and signs ad hoc');
  }
}

function verifyLinuxTarball(entries, arch, version, check, notes) {
  checkCommon(entries, check);
  const top = entries[0] && entries[0].path.split('/')[0];
  check(entries.every((entry) => entry.path === top || entry.path.startsWith(`${top}/`)), 'everything sits in one top folder');
  if (version) check(top === `lantoss-${version}`, `the top folder is lantoss-${version} (is ${top})`);
  const exists = (p) => entries.find((entry) => entry.path === `${top}/${p}`);
  const executable = exists('app/Lantoss');
  if (check(executable && executable.type === 'file', 'app/Lantoss exists')) {
    check(executable.mode === 0o755, 'app/Lantoss is 0755');
    check(elfMachine(executable.data) === ARCHES[arch].elf, `app/Lantoss is an ELF for ${arch}`);
  }
  for (const script of ['install.sh', 'uninstall.sh']) {
    const entry = exists(script);
    if (check(entry, `${script} exists`)) check(entry.mode === 0o755, `${script} is 0755`);
  }
  const install = exists('install.sh');
  if (install) check(install.data.toString('utf8').includes(`APP_ARCH="${ARCHES[arch].linuxScript}"`), `install.sh targets ${ARCHES[arch].linuxScript}`);
  const desktop = exists('lantoss.desktop');
  if (check(desktop, 'lantoss.desktop exists')) checkDesktopEntry(desktop.data.toString('utf8'), check, 'lantoss.desktop');
  const icon = exists('lantoss.png');
  check(icon && pngSize(icon.data), 'lantoss.png is a PNG');
  check(entries.every((entry) => entry.uid === 0 && entry.gid === 0), 'entries are owned by uid/gid 0');
}

function verifyDeb(buffer, arch, version, check, notes, list) {
  const members = readAr(buffer);
  check(members.map((m) => m.name).join(',') === 'debian-binary,control.tar.gz,data.tar.gz', `ar members are debian-binary, control.tar.gz, data.tar.gz (are ${members.map((m) => m.name).join(', ')})`);
  const member = (name) => members.find((m) => m.name === name);
  check(member('debian-binary') && member('debian-binary').data.toString('ascii') === '2.0\n', 'debian-binary is "2.0"');
  notes.push(`ar: ${members.map((m) => `${m.name} (${m.size} bytes)`).join(', ')}`);
  if (!member('control.tar.gz') || !member('data.tar.gz')) return;
  const controlEntries = readTar(zlib.gunzipSync(member('control.tar.gz').data));
  const dataEntries = readTar(zlib.gunzipSync(member('data.tar.gz').data));
  if (list) {
    console.log('  control.tar.gz:'); printEntries(controlEntries, '    ');
    console.log('  data.tar.gz:'); printEntries(dataEntries, '    ');
  }
  for (const [label, entries] of [['control.tar.gz', controlEntries], ['data.tar.gz', dataEntries]]) {
    check(entries.every((entry) => entry.rawName.startsWith('./')), `${label} paths start with ./`);
    check(entries.length && entries[0].rawName === './' && entries[0].type === 'dir', `${label} starts with ./`);
    check(entries.every((entry) => entry.uid === 0 && entry.gid === 0 && entry.owner === 'root'), `${label} entries are owned by root`);
    checkCommon(entries.filter((entry) => entry.path !== '.'), check, { scripts: label === 'control.tar.gz' ? MAINTAINER_SCRIPTS : [] });
  }
  const controlFile = controlEntries.find((entry) => entry.path === './control');
  if (!check(controlFile, 'control.tar.gz has ./control')) return;
  const fields = parseControl(controlFile.data.toString('utf8'));
  check(fields.Package === 'lantoss', 'Package is lantoss');
  if (version) check(fields.Version === version.replace(/-/g, '~'), `Version is ${version}`);
  check(fields.Architecture === ARCHES[arch].deb, `Architecture is ${ARCHES[arch].deb} (is ${fields.Architecture})`);
  check(/<[^>]+@[^>]+>/.test(fields.Maintainer || ''), 'Maintainer has a name and an email address');
  check(/^\d+$/.test(fields['Installed-Size'] || ''), 'Installed-Size is set');
  check(fields.Description && fields.Description.includes('\n '), 'Description has a summary and a long description');
  const depends = (fields.Depends || '').split(',').flatMap((group) => group.split('|')).map((dep) => dep.trim().split(/[ (]/)[0]);
  for (const dep of DEB_DEPENDS) check(depends.includes(dep), `Depends includes ${dep}`);
  notes.push(`control: ${fields.Package} ${fields.Version} ${fields.Architecture}, Installed-Size ${fields['Installed-Size']} KiB`);
  notes.push(`Depends: ${fields.Depends}`);
  for (const script of ['postinst', 'postrm']) {
    const entry = controlEntries.find((e) => e.path === `./${script}`);
    if (check(entry, `control.tar.gz has ./${script}`)) check(entry.mode === 0o755, `${script} is 0755`);
  }

  const files = new Map(dataEntries.filter((e) => e.type === 'file').map((e) => [e.path.slice(2), e]));
  const md5sums = controlEntries.find((entry) => entry.path === './md5sums');
  if (check(md5sums, 'control.tar.gz has ./md5sums')) {
    const listed = new Map(md5sums.data.toString('utf8').trim().split('\n').map((line) => { const m = /^([0-9a-f]{32}) {2}(.+)$/.exec(line); return m ? [m[2], m[1]] : [line, '']; }));
    check(listed.size === files.size, `md5sums lists every file (${listed.size} of ${files.size})`);
    let bad = 0;
    for (const [file, entry] of files) if (listed.get(file) !== crypto.createHash('md5').update(entry.data).digest('hex')) bad++;
    check(bad === 0, `every md5sum matches its file (${bad} mismatched)`);
  }
  const totalKiB = [...files.values()].reduce((sum, entry) => sum + entry.size, 0) / 1024;
  check(Math.abs(Number(fields['Installed-Size']) - totalKiB) / totalKiB < 0.05, `Installed-Size ${fields['Installed-Size']} is close to the payload (${Math.round(totalKiB)} KiB)`);

  const find = (p) => dataEntries.find((entry) => entry.path === p);
  const link = find('./usr/bin/lantoss');
  check(link && link.type === 'symlink' && link.target === '/opt/lantoss/Lantoss', `./usr/bin/lantoss is a symlink to /opt/lantoss/Lantoss (is ${link ? `${link.type} -> ${link.target}` : 'missing'})`);
  const executable = find('./opt/lantoss/Lantoss');
  if (check(executable && executable.type === 'file', './opt/lantoss/Lantoss exists')) {
    check(executable.mode === 0o755, './opt/lantoss/Lantoss is 0755');
    check(elfMachine(executable.data) === ARCHES[arch].elf, `./opt/lantoss/Lantoss is an ELF for ${arch}`);
  }
  const desktop = find('./usr/share/applications/lantoss.desktop');
  if (check(desktop, './usr/share/applications/lantoss.desktop exists')) checkDesktopEntry(desktop.data.toString('utf8'), check, 'lantoss.desktop');
  const icons = dataEntries.filter((entry) => /^\.\/usr\/share\/icons\/hicolor\/\d+x\d+\/apps\/lantoss\.png$/.test(entry.path));
  for (const icon of icons) {
    const want = Number(/hicolor\/(\d+)x/.exec(icon.path)[1]);
    const size = pngSize(icon.data);
    check(size && size.width === want && size.height === want, `${icon.path} is ${want}x${want}`);
  }
  check(icons.length >= 5, `hicolor icons are installed (${icons.length})`);
  notes.push(`hicolor sizes: ${icons.map((icon) => /hicolor\/(\d+)x/.exec(icon.path)[1]).join(', ')}`);
  notes.push(`symlinks: ${dataEntries.filter((e) => e.type === 'symlink').map((e) => `${e.path} -> ${e.target}`).join(', ')}`);
}

function printEntries(entries, indent = '  ') {
  for (const entry of entries) {
    console.log(`${indent}${permissions(entry)} ${String(entry.type === 'file' ? entry.size ?? entry.data.length : 0).padStart(10)} ${entry.path}${entry.type === 'symlink' ? ` -> ${entry.target}` : ''}`);
  }
}

function guessArch(file) {
  if (/arm64|aarch64|AppleSilicon/i.test(file)) return 'arm64';
  if (/x64|amd64|x86_64|Intel/i.test(file)) return 'x64';
  return null;
}

function verifyFile(file, options) {
  const problems = [], notes = [];
  const check = (ok, description) => { if (!ok) problems.push(description); return Boolean(ok); };
  const arch = { arm64: 'arm64', aarch64: 'arm64', x64: 'x64', x86_64: 'x64', amd64: 'x64' }[options.arch || ''] || guessArch(path.basename(file));
  if (!arch) throw new Error(`Cannot tell the architecture of ${file}; pass --arch`);
  const buffer = fs.readFileSync(file);
  let entries = null;
  if (file.endsWith('.zip')) {
    entries = readZip(buffer);
    verifyMacZip(entries, arch, options.version, check, notes);
  } else if (file.endsWith('.tar.gz')) {
    entries = readTar(zlib.gunzipSync(buffer));
    verifyLinuxTarball(entries, arch, options.version, check, notes);
  } else if (file.endsWith('.deb')) {
    verifyDeb(buffer, arch, options.version, check, notes, options.list);
  } else {
    throw new Error(`Unknown package type: ${file}`);
  }
  if (entries) {
    if (options.list) printEntries(entries);
    const executables = entries.filter((e) => e.type === 'file' && e.mode === 0o755).map((e) => e.path);
    notes.unshift(`${entries.length} entries; 0755 files: ${executables.join(', ')}`);
    const links = entries.filter((e) => e.type === 'symlink');
    if (links.length) notes.push(`symlinks: ${links.map((e) => `${e.path} -> ${e.target}`).join(', ')}`);
  }
  console.log(`${problems.length ? 'FAIL' : 'PASS'} ${path.basename(file)} (${arch}, ${buffer.length} bytes)`);
  for (const note of notes) console.log(`  ${note}`);
  for (const problem of problems) console.log(`  FAILED: ${problem}`);
  return problems.length === 0;
}

function main(argv) {
  const options = { files: [] };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--arch') options.arch = argv[++i];
    else if (argv[i] === '--version') options.version = argv[++i];
    else if (argv[i] === '--list') options.list = true;
    else options.files.push(argv[i]);
  }
  if (!options.files.length) throw new Error('Usage: node verify.cjs <package>... [--arch arm64|x64] [--version x.y.z] [--list]');
  let ok = true;
  for (const file of options.files) ok = verifyFile(file, options) && ok;
  return ok;
}

module.exports = { readZip, readTar, readAr, machO, elfMachine, parsePlist, parseXml, pngSize };

if (require.main === module) {
  try {
    process.exit(main(process.argv.slice(2)) ? 0 : 1);
  } catch (error) {
    console.error(`verify.cjs: ${error.message}`);
    process.exit(1);
  }
}

#!/usr/bin/env node
'use strict';
/*
 * Lantoss packaging helpers: zip, tar.gz, ar/.deb and .icns writers with explicit Unix modes.
 * Only Node built-ins are used, so macOS and Linux packages can be built on Windows, whose file
 * system has no Unix modes or symlinks. Every archive entry therefore gets an explicit mode:
 *   directories 0755, symlinks 0777, files 0644,
 *   executables 0755: the app executable "Lantoss", "createdump", any *.sh, names given with --exec,
 *   and Debian maintainer scripts (postinst, postrm, ...) inside control.tar.gz.
 *
 * CLI:
 *   node pack.cjs zip   <out.zip>    [--add <archive/path>=<local path>]... [--symlink <archive/path>=<target>]... [--exec <name>]...
 *   node pack.cjs targz <out.tar.gz> [--add ...]... [--symlink ...]... [--exec <name>]...
 *   node pack.cjs deb   <out.deb> --control <dir> [--add ...]... [--symlink ...]... [--exec <name>]...
 *   node pack.cjs icns  <out.icns> <png>...
 *
 * --add copies a file, or a directory recursively, to the archive path. Missing parent directories are added.
 * For deb, --add paths are relative to the filesystem root (opt/lantoss=...). The control directory must hold
 * "control"; Installed-Size and md5sums are generated. Text templates (*.sh, *.desktop, *.plist, control files)
 * are normalised to LF line endings. SOURCE_DATE_EPOCH, when set, fixes every timestamp.
 */
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const crypto = require('crypto');

const EXECUTABLE_NAMES = Object.freeze(['Lantoss', 'createdump']);
const MAINTAINER_SCRIPTS = Object.freeze(['preinst', 'postinst', 'prerm', 'postrm', 'config']);
const TEXT_FILE = /\.(sh|desktop|plist)$|^(control|preinst|postinst|prerm|postrm|config|md5sums|conffiles|PkgInfo)$/;
const S_IFDIR = 0o040000, S_IFREG = 0o100000, S_IFLNK = 0o120000;

function buildTime() {
  const epoch = process.env.SOURCE_DATE_EPOCH;
  return epoch && /^\d+$/.test(epoch) ? Number(epoch) : Math.floor(Date.now() / 1000);
}

function baseName(archivePath) {
  return archivePath.slice(archivePath.lastIndexOf('/') + 1);
}

function normalizeArchivePath(value) {
  const parts = String(value).replace(/\\/g, '/').split('/').filter((part) => part && part !== '.');
  if (parts.includes('..')) throw new Error(`Archive path must not contain "..": ${value}`);
  return parts.join('/');
}

function isExecutable(archivePath, execNames = EXECUTABLE_NAMES) {
  const name = baseName(archivePath);
  return execNames.includes(name) || name.endsWith('.sh');
}

/** Returns the file contents, with CRLF converted to LF for text templates. */
function entryData(entry) {
  if (entry.type !== 'file') return Buffer.alloc(0);
  if (entry.data) return entry.data;
  let data = fs.readFileSync(entry.source);
  if (TEXT_FILE.test(baseName(entry.path))) data = Buffer.from(data.toString('utf8').replace(/\r\n?/g, '\n'), 'utf8');
  return data;
}

/**
 * Builds a sorted entry list ({path, type: 'file'|'dir'|'symlink', mode, mtime, source|data, target}).
 * add: [{to, from}] where from is a local file or directory; symlinks: [{to, target}].
 */
function collectEntries({ add = [], symlinks = [], execNames = EXECUTABLE_NAMES, mtime = buildTime() } = {}) {
  const entries = new Map();
  const put = (entry) => {
    const existing = entries.get(entry.path);
    if (existing) {
      if (existing.type === 'dir' && entry.type === 'dir') return;
      throw new Error(`Duplicate archive path: ${entry.path}`);
    }
    entries.set(entry.path, entry);
  };
  const walk = (archivePath, localPath) => {
    const stat = fs.lstatSync(localPath);
    if (stat.isSymbolicLink()) {
      put({ path: archivePath, type: 'symlink', mode: 0o777, mtime, target: fs.readlinkSync(localPath).replace(/\\/g, '/') });
    } else if (stat.isDirectory()) {
      if (archivePath) put({ path: archivePath, type: 'dir', mode: 0o755, mtime });
      for (const name of fs.readdirSync(localPath).sort()) walk(archivePath ? `${archivePath}/${name}` : name, path.join(localPath, name));
    } else if (stat.isFile()) {
      if (!archivePath) throw new Error(`A file needs an archive path: ${localPath}`);
      put({ path: archivePath, type: 'file', mode: isExecutable(archivePath, execNames) ? 0o755 : 0o644, mtime, source: localPath });
    } else {
      throw new Error(`Unsupported file type: ${localPath}`);
    }
  };
  for (const { to, from } of add) {
    if (!fs.existsSync(from)) throw new Error(`Not found: ${from}`);
    walk(normalizeArchivePath(to), from);
  }
  for (const { to, target } of symlinks) {
    if (!target) throw new Error(`Symlink ${to} has no target`);
    put({ path: normalizeArchivePath(to), type: 'symlink', mode: 0o777, mtime, target });
  }
  for (const entryPath of [...entries.keys()]) {
    for (let i = entryPath.lastIndexOf('/'); i > 0; i = entryPath.lastIndexOf('/', i - 1)) {
      const dir = entryPath.slice(0, i);
      const existing = entries.get(dir);
      if (existing && existing.type !== 'dir') throw new Error(`${dir} is both a ${existing.type} and a directory`);
      if (!existing) entries.set(dir, { path: dir, type: 'dir', mode: 0o755, mtime });
    }
  }
  // Plain code-point order puts every directory before its contents.
  return [...entries.values()].sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));
}

const crc32 = typeof zlib.crc32 === 'function'
  ? (buffer) => zlib.crc32(buffer) >>> 0
  : (() => {
      const table = new Uint32Array(256);
      for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        table[n] = c >>> 0;
      }
      return (buffer) => {
        let c = 0xffffffff;
        for (let i = 0; i < buffer.length; i++) c = table[(c ^ buffer[i]) & 0xff] ^ (c >>> 8);
        return (c ^ 0xffffffff) >>> 0;
      };
    })();

function writeAtomic(outPath, writer) {
  fs.mkdirSync(path.dirname(path.resolve(outPath)), { recursive: true });
  const temp = `${outPath}.tmp`;
  try {
    writer(temp);
    fs.renameSync(temp, outPath);
  } catch (error) {
    fs.rmSync(temp, { force: true });
    throw error;
  }
}

function writeFully(fd, buffer) {
  for (let offset = 0; offset < buffer.length;) offset += fs.writeSync(fd, buffer, offset, buffer.length - offset);
}

/** Zip with Unix modes in the external attributes ("made by" Unix), deflate, UTF-8 names and UTC mtimes. */
function writeZip(outPath, entries) {
  if (entries.length > 0xffff) throw new Error('Too many entries for a zip without Zip64');
  writeAtomic(outPath, (temp) => {
    const fd = fs.openSync(temp, 'w');
    const central = [];
    let offset = 0;
    const write = (buffer) => { writeFully(fd, buffer); offset += buffer.length; };
    try {
      for (const entry of entries) {
        const name = Buffer.from(entry.type === 'dir' ? `${entry.path}/` : entry.path, 'utf8');
        const raw = entry.type === 'file' ? entryData(entry) : entry.type === 'symlink' ? Buffer.from(entry.target, 'utf8') : Buffer.alloc(0);
        let method = 0, body = raw;
        if (entry.type === 'file' && raw.length > 0) {
          const deflated = zlib.deflateRawSync(raw, { level: 9 });
          if (deflated.length < raw.length) { method = 8; body = deflated; }
        }
        if (offset + body.length > 0xfffffff0) throw new Error('Archive too large for a zip without Zip64');
        const crc = crc32(raw);
        const when = new Date(entry.mtime * 1000);
        const dosTime = (when.getUTCHours() << 11) | (when.getUTCMinutes() << 5) | (when.getUTCSeconds() >> 1);
        const dosDate = ((Math.max(1980, when.getUTCFullYear()) - 1980) << 9) | ((when.getUTCMonth() + 1) << 5) | when.getUTCDate();
        const typeBits = entry.type === 'dir' ? S_IFDIR : entry.type === 'symlink' ? S_IFLNK : S_IFREG;
        const external = (typeBits | entry.mode) * 0x10000 + (entry.type === 'dir' ? 0x10 : 0);
        const extendedTime = Buffer.alloc(9); // "UT" extra field: modification time in UTC
        extendedTime.writeUInt16LE(0x5455, 0); extendedTime.writeUInt16LE(5, 2); extendedTime.writeUInt8(1, 4); extendedTime.writeUInt32LE(entry.mtime >>> 0, 5);

        const local = Buffer.alloc(30);
        local.writeUInt32LE(0x04034b50, 0); local.writeUInt16LE(20, 4); local.writeUInt16LE(0x0800, 6); local.writeUInt16LE(method, 8);
        local.writeUInt16LE(dosTime, 10); local.writeUInt16LE(dosDate, 12); local.writeUInt32LE(crc, 14);
        local.writeUInt32LE(body.length, 18); local.writeUInt32LE(raw.length, 22); local.writeUInt16LE(name.length, 26); local.writeUInt16LE(extendedTime.length, 28);
        const headerOffset = offset;
        write(local); write(name); write(extendedTime); write(body);

        const record = Buffer.alloc(46);
        record.writeUInt32LE(0x02014b50, 0); record.writeUInt16LE((3 << 8) | 20, 4); record.writeUInt16LE(20, 6); record.writeUInt16LE(0x0800, 8);
        record.writeUInt16LE(method, 10); record.writeUInt16LE(dosTime, 12); record.writeUInt16LE(dosDate, 14); record.writeUInt32LE(crc, 16);
        record.writeUInt32LE(body.length, 20); record.writeUInt32LE(raw.length, 24); record.writeUInt16LE(name.length, 28);
        record.writeUInt16LE(extendedTime.length, 30); record.writeUInt16LE(0, 32); record.writeUInt16LE(0, 34); record.writeUInt16LE(0, 36);
        record.writeUInt32LE(external, 38); record.writeUInt32LE(headerOffset, 42);
        central.push(record, name, extendedTime);
      }
      const centralOffset = offset;
      for (const part of central) write(part);
      const end = Buffer.alloc(22);
      end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(entries.length, 8); end.writeUInt16LE(entries.length, 10);
      end.writeUInt32LE(offset - centralOffset, 12); end.writeUInt32LE(centralOffset, 16);
      write(end);
    } finally {
      fs.closeSync(fd);
    }
  });
}

function splitUstarName(name) {
  // ustar stores long paths as prefix (<=155 bytes) + "/" + name (<=100 bytes).
  for (let i = name.indexOf('/'); i > 0; i = name.indexOf('/', i + 1)) {
    const prefix = name.slice(0, i), rest = name.slice(i + 1);
    if (Buffer.byteLength(prefix) <= 155 && rest && Buffer.byteLength(rest) <= 100) return [prefix, rest];
  }
  return null;
}

function paxRecords(fields) {
  let text = '';
  for (const [key, value] of Object.entries(fields)) {
    const payload = ` ${key}=${value}\n`;
    let length = Buffer.byteLength(payload) + 1;
    while (String(length).length + Buffer.byteLength(payload) !== length) length = String(length).length + Buffer.byteLength(payload);
    text += length + payload;
  }
  return Buffer.from(text, 'utf8');
}

function tarHeader({ name, prefix = '', mode, size = 0, mtime, typeflag, linkname = '', owner = 'root' }) {
  const header = Buffer.alloc(512);
  const text = (value, offset, length) => {
    const bytes = Buffer.from(value, 'utf8');
    if (bytes.length > length) throw new Error(`Tar header field too long: ${value}`);
    bytes.copy(header, offset);
  };
  const octal = (value, offset, length) => text(value.toString(8).padStart(length - 1, '0'), offset, length - 1);
  text(name, 0, 100); octal(mode, 100, 8); octal(0, 108, 8); octal(0, 116, 8); octal(size, 124, 12); octal(mtime, 136, 12);
  header.fill(0x20, 148, 156); text(typeflag, 156, 1); text(linkname, 157, 100);
  text('ustar\0', 257, 6); text('00', 263, 2); text(owner, 265, 32); text(owner, 297, 32); octal(0, 329, 8); octal(0, 337, 8); text(prefix, 345, 155);
  let sum = 0;
  for (const byte of header) sum += byte;
  text(sum.toString(8).padStart(6, '0') + '\0 ', 148, 8);
  return header;
}

/** Returns the tar stream as Buffers. options.prefix is prepended to every path; options.root adds the prefix directory itself (e.g. "./"). */
function tarRecords(entries, { prefix = '', root = false } = {}) {
  const parts = [];
  const pad = (length) => { if (length % 512) parts.push(Buffer.alloc(512 - (length % 512))); };
  if (root) parts.push(tarHeader({ name: prefix, mode: 0o755, mtime: entries[0] ? entries[0].mtime : buildTime(), typeflag: '5' }));
  for (const entry of entries) {
    const fullName = prefix + entry.path + (entry.type === 'dir' ? '/' : '');
    const data = entryData(entry);
    const linkname = entry.type === 'symlink' ? entry.target : '';
    const pax = {};
    let name = fullName, namePrefix = '';
    if (Buffer.byteLength(fullName) > 100) {
      const split = splitUstarName(fullName);
      if (split) [namePrefix, name] = split;
      else { pax.path = fullName; name = Buffer.from(fullName, 'utf8').subarray(0, 100).toString('latin1').replace(/[^\x20-\x7e]/g, '_'); }
    }
    if (Buffer.byteLength(linkname) > 100) pax.linkpath = linkname;
    if (Object.keys(pax).length) {
      const body = paxRecords(pax);
      parts.push(tarHeader({ name: `PaxHeaders/${baseName(entry.path)}`.slice(0, 100), mode: 0o644, size: body.length, mtime: entry.mtime, typeflag: 'x' }), body);
      pad(body.length);
    }
    const typeflag = entry.type === 'dir' ? '5' : entry.type === 'symlink' ? '2' : '0';
    parts.push(tarHeader({ name, prefix: namePrefix, mode: entry.mode, size: data.length, mtime: entry.mtime, typeflag, linkname: pax.linkpath ? '' : linkname }));
    if (data.length) { parts.push(data); pad(data.length); }
  }
  parts.push(Buffer.alloc(1024));
  return parts;
}

function tarGz(entries, options) {
  return zlib.gzipSync(Buffer.concat(tarRecords(entries, options)), { level: 9 });
}

function writeTarGz(outPath, entries, options) {
  const archive = tarGz(entries, options);
  writeAtomic(outPath, (temp) => fs.writeFileSync(temp, archive));
}

/** Common ar archive as used by .deb: "!<arch>\n" then 60-byte headers, data padded to even length. */
function arArchive(members, mtime = buildTime()) {
  const parts = [Buffer.from('!<arch>\n', 'ascii')];
  for (const { name, data } of members) {
    if (name.length > 16) throw new Error(`ar member name too long: ${name}`);
    const header = name.padEnd(16) + String(mtime).padEnd(12) + '0'.padEnd(6) + '0'.padEnd(6) + '100644'.padEnd(8) + String(data.length).padEnd(10) + '`\n';
    parts.push(Buffer.from(header, 'ascii'), data);
    if (data.length % 2) parts.push(Buffer.from('\n', 'ascii'));
  }
  return Buffer.concat(parts);
}

function setControlField(control, field, value) {
  const lines = control.split('\n');
  const existing = lines.findIndex((line) => line.toLowerCase().startsWith(`${field.toLowerCase()}:`));
  if (existing >= 0) lines[existing] = `${field}: ${value}`;
  else {
    const before = lines.findIndex((line) => /^(Depends|Recommends|Section|Priority|Homepage|Description):/i.test(line));
    lines.splice(before >= 0 ? before : lines.length, 0, `${field}: ${value}`);
  }
  return lines.join('\n');
}

function buildDeb(outPath, { controlDir, add = [], symlinks = [], execNames = EXECUTABLE_NAMES }) {
  const mtime = buildTime();
  const data = collectEntries({ add, symlinks, execNames, mtime });
  for (const entry of data) if (entry.type === 'file') entry.data = entryData(entry);

  const controlNames = fs.readdirSync(controlDir).filter((name) => fs.statSync(path.join(controlDir, name)).isFile()).sort();
  if (!controlNames.includes('control')) throw new Error(`${controlDir} has no control file`);
  const readText = (name) => fs.readFileSync(path.join(controlDir, name), 'utf8').replace(/\r\n?/g, '\n');
  const installedKiB = data.reduce((sum, entry) => sum + (entry.type === 'file' ? Math.max(1, Math.ceil(entry.data.length / 1024)) : 1), 0);
  let control = setControlField(readText('control').replace(/\n+$/, ''), 'Installed-Size', installedKiB) + '\n';
  for (const field of ['Package', 'Version', 'Architecture', 'Maintainer', 'Description']) {
    if (!new RegExp(`^${field}: *\\S`, 'm').test(control)) throw new Error(`control is missing ${field}`);
  }
  const badLine = control.split('\n').slice(0, -1).find((line) => !/^[A-Za-z0-9][A-Za-z0-9-]*:|^[ \t]+\S/.test(line));
  if (badLine !== undefined) throw new Error(`Invalid control line: "${badLine}"`);

  const md5sums = data.filter((entry) => entry.type === 'file')
    .map((entry) => `${crypto.createHash('md5').update(entry.data).digest('hex')}  ${entry.path}\n`).join('');
  const controlEntries = [
    { path: 'control', type: 'file', mode: 0o644, mtime, data: Buffer.from(control, 'utf8') },
    { path: 'md5sums', type: 'file', mode: 0o644, mtime, data: Buffer.from(md5sums, 'utf8') },
  ];
  for (const name of controlNames) {
    if (name === 'control' || name === 'md5sums') continue;
    const script = MAINTAINER_SCRIPTS.includes(name);
    const text = readText(name);
    if (script && !text.startsWith('#!')) throw new Error(`${name} must start with #!`);
    controlEntries.push({ path: name, type: 'file', mode: script ? 0o755 : 0o644, mtime, data: Buffer.from(text, 'utf8') });
  }
  controlEntries.sort((a, b) => (a.path < b.path ? -1 : 1));

  const deb = arArchive([
    { name: 'debian-binary', data: Buffer.from('2.0\n', 'ascii') },
    { name: 'control.tar.gz', data: tarGz(controlEntries, { prefix: './', root: true }) },
    { name: 'data.tar.gz', data: tarGz(data, { prefix: './', root: true }) },
  ], mtime);
  writeAtomic(outPath, (temp) => fs.writeFileSync(temp, deb));
  return { installedKiB, files: data.filter((entry) => entry.type === 'file').length };
}

// icns chunk types holding PNG data. ic11-ic14 and ic10 are the @2x (retina) variants.
const ICNS_TYPES = Object.freeze([
  ['icp4', 16], ['icp5', 32], ['ic11', 32], ['ic12', 64], ['ic07', 128], ['ic13', 256], ['ic08', 256], ['ic14', 512], ['ic09', 512], ['ic10', 1024],
]);

function pngSize(buffer) {
  const signature = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
  if (buffer.length < 33 || !buffer.subarray(0, 8).equals(signature) || buffer.toString('ascii', 12, 16) !== 'IHDR') throw new Error('Not a PNG file');
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

function buildIcns(pngBuffers) {
  const bySize = new Map();
  for (const png of pngBuffers) {
    const { width, height } = pngSize(png);
    if (width !== height) throw new Error(`Icon images must be square, got ${width}x${height}`);
    bySize.set(width, png);
  }
  const chunks = [], missing = [];
  for (const [type, size] of ICNS_TYPES) {
    const png = bySize.get(size);
    if (!png) { missing.push(`${type} (${size}px)`); continue; }
    const header = Buffer.alloc(8);
    header.write(type, 0, 'ascii');
    header.writeUInt32BE(png.length + 8, 4);
    chunks.push(header, png);
  }
  if (!chunks.length) throw new Error('No PNG matches an icns size (16, 32, 64, 128, 256, 512, 1024)');
  const body = Buffer.concat(chunks);
  const header = Buffer.alloc(8);
  header.write('icns', 0, 'ascii');
  header.writeUInt32BE(body.length + 8, 4);
  return { icns: Buffer.concat([header, body]), missing };
}

function parseArgs(argv) {
  const options = { add: [], symlinks: [], execNames: [...EXECUTABLE_NAMES], positional: [] };
  const pair = (flag, value) => {
    const i = value.indexOf('=');
    if (i <= 0) throw new Error(`${flag} expects <archive path>=<value>, got "${value}"`);
    return [value.slice(0, i), value.slice(i + 1)];
  };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const value = () => { if (i + 1 >= argv.length) throw new Error(`${arg} needs a value`); return argv[++i]; };
    if (arg === '--add') { const [to, from] = pair(arg, value()); options.add.push({ to, from: path.resolve(from) }); }
    else if (arg === '--symlink') { const [to, target] = pair(arg, value()); options.symlinks.push({ to, target }); }
    else if (arg === '--exec') options.execNames.push(value());
    else if (arg === '--control') options.controlDir = path.resolve(value());
    else if (arg.startsWith('--')) throw new Error(`Unknown option ${arg}`);
    else options.positional.push(arg);
  }
  return options;
}

function main(argv) {
  const [command, ...rest] = argv;
  const options = parseArgs(rest);
  const out = options.positional[0];
  if (!command || !out) throw new Error('Usage: node pack.cjs <zip|targz|deb|icns> <output> [options] (see the header of pack.cjs)');
  const report = (extra) => console.log(`Wrote ${out} (${fs.statSync(out).size} bytes${extra ? `, ${extra}` : ''})`);
  if (command === 'zip' || command === 'targz') {
    const entries = collectEntries(options);
    const executables = entries.filter((entry) => entry.type === 'file' && entry.mode === 0o755).map((entry) => entry.path);
    if (command === 'zip') writeZip(out, entries); else writeTarGz(out, entries);
    report(`${entries.length} entries; executable: ${executables.join(', ') || 'none'}`);
  } else if (command === 'deb') {
    if (!options.controlDir) throw new Error('deb needs --control <dir>');
    const result = buildDeb(out, options);
    report(`${result.files} files, Installed-Size ${result.installedKiB} KiB`);
  } else if (command === 'icns') {
    const pngs = options.positional.slice(1);
    if (!pngs.length) throw new Error('icns needs at least one PNG');
    const { icns, missing } = buildIcns(pngs.map((file) => fs.readFileSync(file)));
    if (missing.length) console.warn(`icns: no PNG for ${missing.join(', ')}`);
    writeAtomic(out, (temp) => fs.writeFileSync(temp, icns));
    report();
  } else {
    throw new Error(`Unknown command "${command}"`);
  }
}

module.exports = {
  EXECUTABLE_NAMES, MAINTAINER_SCRIPTS, ICNS_TYPES,
  isExecutable, collectEntries, entryData, crc32,
  writeZip, tarRecords, tarGz, writeTarGz, arArchive, buildDeb, buildIcns, pngSize,
};

if (require.main === module) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    console.error(`pack.cjs: ${error.message}`);
    process.exit(1);
  }
}

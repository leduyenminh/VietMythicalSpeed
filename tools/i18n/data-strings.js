// Translations of the game data texts (navigators, shop, events, mail, banners, world) as flat "key = text" files.
//
//   node tools/i18n/data-strings.js export <lang> [out.txt]   every data text in <lang> (English where missing,
//                                                               marked "# TODO")
//   node tools/i18n/data-strings.js import <lang> <file.txt>   writes the translations into Resources/Data/*.json
//   node tools/i18n/data-strings.js status                     missing translations per language
//
// Keys look like "navigators.thanh_van.voice.greet1.text": the file, then ids (or indexes) down to the text.
// Values use the same escaping as the UI files: "\n" is a line break.
const fs = require('fs');
const path = require('path');
const { format, isLocalized } = require('./jsonfmt');

const LANGS = ['vi', 'en', 'zh', 'ja', 'ko', 'es', 'de', 'fi'];
const DATA = path.resolve(__dirname, '..', '..', 'UnityClient', 'Assets', 'VietMythicalSpeed', 'Resources', 'Data');
const FILES = ['world', 'navigators', 'banners', 'shop', 'events', 'mail'];

function load() {
  return FILES.map((name) => ({ name, file: path.join(DATA, name + '.json'), json: JSON.parse(fs.readFileSync(path.join(DATA, name + '.json'), 'utf8')) }));
}

/** Every translated text with its key, in file order. */
function texts(docs) {
  const out = [];
  for (const doc of docs) {
    (function walk(node, keyParts, top) {
      if (isLocalized(node) && ('en' in node || 'vi' in node)) {
        out.push({ key: [doc.name, ...keyParts].join('.'), text: node, doc });
        return;
      }
      if (Array.isArray(node)) {
        node.forEach((child, i) => walk(child, [...keyParts, child && typeof child === 'object' && child.id ? child.id : String(i)], false));
      } else if (node && typeof node === 'object') {
        for (const k of Object.keys(node)) {
          // The top-level list of each file (navigators, items, events…) is implied by the ids.
          const skip = top && Array.isArray(node[k]);
          walk(node[k], skip ? keyParts : [...keyParts, k], false);
        }
      }
    })(doc.json, [], true);
  }
  return out;
}

const escape = (s) => s.replace(/\r?\n/g, '\\n');
const unescape = (s) => s.replace(/\\n/g, '\n');

function exportLang(lang, out) {
  const lines = [`# Game data texts – ${lang}. Translate the right-hand side; keep {placeholders} and \\n.`];
  for (const t of texts(load())) {
    const value = t.text[lang];
    if (value) lines.push(`${t.key} = ${escape(value)}`);
    else lines.push(`# TODO vi: ${escape(t.text.vi || '')}`, `${t.key} = ${escape(t.text.en || t.text.vi || '')}`);
  }
  const text = lines.join('\n') + '\n';
  if (out) fs.writeFileSync(out, text);
  else process.stdout.write(text);
}

function importLang(lang, file) {
  const docs = load();
  const byKey = new Map(texts(docs).map((t) => [t.key, t]));
  let set = 0;
  const unknown = [];
  const placeholderIssues = [];
  for (const raw of fs.readFileSync(file, 'utf8').split('\n')) {
    const line = raw.replace(/\r$/, '');
    if (!line || line.startsWith('#')) continue;
    const eq = line.indexOf(' = ');
    if (eq < 0) continue;
    const key = line.slice(0, eq).trim();
    const value = unescape(line.slice(eq + 3).trim());
    const t = byKey.get(key);
    if (!t) { unknown.push(key); continue; }
    const want = (t.text.en || '').match(/\{[a-z0-9]+\}/g) || [];
    const got = value.match(/\{[a-z0-9]+\}/g) || [];
    if (want.sort().join() !== got.sort().join()) placeholderIssues.push(`${key}: ${want.join(' ')} vs ${got.join(' ')}`);
    // Keep the language order stable: vi, en, then the others.
    const ordered = {};
    for (const l of LANGS) {
      const v = l === lang ? value : t.text[l];
      if (v !== undefined) ordered[l] = v;
    }
    for (const k of Object.keys(t.text)) delete t.text[k];
    Object.assign(t.text, ordered);
    set++;
  }
  for (const doc of docs) fs.writeFileSync(doc.file, format(doc.json));
  const missing = texts(docs).filter((t) => !t.text[lang]).map((t) => t.key);
  console.log(`${lang}: ${set} texts written, ${missing.length} still missing, ${unknown.length} unknown keys`);
  if (unknown.length) console.log('  unknown:', unknown.slice(0, 20).join(', '));
  if (missing.length) console.log('  missing:', missing.slice(0, 20).join(', '));
  if (placeholderIssues.length) console.log('  placeholders:', placeholderIssues.join('; '));
  return missing.length === 0 && placeholderIssues.length === 0;
}

function status() {
  const all = texts(load());
  let ok = true;
  for (const lang of LANGS) {
    const missing = all.filter((t) => !t.text[lang]);
    console.log(`${lang}: ${all.length - missing.length}/${all.length}`);
    if (missing.length) ok = false;
  }
  return ok;
}

module.exports = { texts, load, LANGS };

if (require.main === module) {
  const [cmd, lang, file] = process.argv.slice(2);
  if (cmd === 'export' && lang) exportLang(lang, file);
  else if (cmd === 'import' && lang && file) process.exitCode = importLang(lang, file) ? 0 : 1;
  else if (cmd === 'status') process.exitCode = status() ? 0 : 1;
  else console.log('usage: data-strings.js export <lang> [out] | import <lang> <file> | status');
}

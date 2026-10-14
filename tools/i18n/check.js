// Checks the UI language files (Resources/Localization/*.txt) against English:
// same keys, same {placeholders}, no duplicates, and every literal Loc.T/Loc.F key used in code exists.
// Also reports data texts missing a translation (see data-strings.js).
//   node tools/i18n/check.js
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..', '..', 'UnityClient', 'Assets', 'VietMythicalSpeed');
const LOC = path.join(ROOT, 'Resources', 'Localization');
const LANGS = ['vi', 'en', 'zh', 'ja', 'ko', 'es', 'de', 'fi'];

function parse(lang) {
  const file = path.join(LOC, lang + '.txt');
  if (!fs.existsSync(file)) return null;
  const entries = [];
  for (const raw of fs.readFileSync(file, 'utf8').split('\n')) {
    const line = raw.replace(/\r$/, '').trim();
    if (!line || line.startsWith('#')) continue;
    const eq = line.indexOf('=');
    if (eq <= 0) continue;
    entries.push([line.slice(0, eq).trim(), line.slice(eq + 1).trim()]);
  }
  return entries;
}

const placeholders = (s) => (s.match(/\{\d+\}/g) || []).sort().join(' ');

let problems = 0;
const report = (msg) => { problems++; console.log('  ' + msg); };

const en = new Map(parse('en'));
for (const lang of LANGS) {
  const entries = parse(lang);
  console.log(`${lang}: ${entries ? entries.length : 'MISSING FILE'} keys`);
  if (!entries) { problems++; continue; }
  const seen = new Set();
  for (const [k, v] of entries) {
    if (seen.has(k)) report(`${lang}: duplicate ${k}`);
    seen.add(k);
    if (!en.has(k)) report(`${lang}: unknown key ${k}`);
    else if (placeholders(en.get(k)) !== placeholders(v)) report(`${lang}: ${k} placeholders ${placeholders(v) || '-'} vs en ${placeholders(en.get(k)) || '-'}`);
    if (!v) report(`${lang}: empty ${k}`);
  }
  for (const k of en.keys()) if (!seen.has(k)) report(`${lang}: missing ${k}`);
}

// Keys used in code.
const used = new Set();
(function walk(dir) {
  for (const f of fs.readdirSync(dir)) {
    const p = path.join(dir, f);
    if (fs.statSync(p).isDirectory()) walk(p);
    else if (p.endsWith('.cs')) for (const m of fs.readFileSync(p, 'utf8').matchAll(/Loc\.(?:T|F)\("([A-Za-z0-9_.]+)"/g)) used.add(m[1]);
  }
})(path.join(ROOT, 'Scripts'));
for (const k of used) if (!k.endsWith('.') && !k.endsWith('_') && !en.has(k)) report(`code uses missing key ${k}`);

// Data texts.
try {
  const { texts, load } = require('./data-strings');
  const all = texts(load());
  for (const lang of LANGS) {
    const missing = all.filter((t) => !t.text[lang]).length;
    if (missing) report(`data: ${missing} texts without ${lang}`);
  }
  console.log(`data: ${all.length} texts checked`);
} catch (e) {
  report('data check failed: ' + e.message);
}

console.log(problems ? `${problems} problem(s)` : 'all good');
process.exitCode = problems ? 1 : 0;

// Consistent formatting for the game data JSON (Resources/Data): small objects and arrays stay on one line,
// translated texts ({ "vi": …, "en": …, … }) break into one language per line when they are long.
// Usage as a module: const { format } = require('./jsonfmt'); fs.writeFileSync(file, format(obj));
// Usage as a tool:   node tools/i18n/jsonfmt.js <file.json>…   (rewrites the files in place)
const fs = require('fs');

const WIDTH = 120;
const LANG = /^[a-z]{2}$/;

function isLocalized(v) {
  if (!v || typeof v !== 'object' || Array.isArray(v)) return false;
  const keys = Object.keys(v);
  return keys.length > 0 && keys.every((k) => LANG.test(k) && typeof v[k] === 'string');
}

function inline(v) {
  if (Array.isArray(v)) return '[' + v.map(inline).join(', ') + ']';
  if (v && typeof v === 'object') {
    const parts = Object.keys(v).map((k) => JSON.stringify(k) + ': ' + inline(v[k]));
    return parts.length ? '{ ' + parts.join(', ') + ' }' : '{}';
  }
  return JSON.stringify(v);
}

function block(v, indent) {
  const pad = '  '.repeat(indent);
  const inner = '  '.repeat(indent + 1);
  const one = inline(v);
  const multiLanguage = isLocalized(v) && Object.keys(v).length > 2;
  if (v === null || typeof v !== 'object') return one;
  if (!multiLanguage && pad.length + one.length <= WIDTH && !hasLongText(v)) return one;
  if (Array.isArray(v)) {
    if (v.length === 0) return '[]';
    return '[\n' + v.map((x) => inner + block(x, indent + 1)).join(',\n') + '\n' + pad + ']';
  }
  const keys = Object.keys(v);
  if (keys.length === 0) return '{}';
  return '{\n' + keys.map((k) => inner + JSON.stringify(k) + ': ' + block(v[k], indent + 1)).join(',\n') + '\n' + pad + '}';
}

// Objects holding several translated texts read better broken up even when short.
function hasLongText(v) {
  if (!v || typeof v !== 'object') return false;
  let texts = 0;
  for (const k of Object.keys(v)) if (isLocalized(v[k])) texts++;
  return texts >= 2 && inline(v).length > 60;
}

function format(obj) {
  return block(obj, 0) + '\n';
}

module.exports = { format, isLocalized };

if (require.main === module) {
  for (const file of process.argv.slice(2)) {
    const text = fs.readFileSync(file, 'utf8');
    fs.writeFileSync(file, format(JSON.parse(text)));
    console.log('formatted', file);
  }
}

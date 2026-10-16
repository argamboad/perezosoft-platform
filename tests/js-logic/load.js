// Runs one of the wwwroot/js bootstrap files inside a stub browser (v4 audit T53, R142): the client package's boot
// scripts (src/Client, Pkg 4.1) or the seeded UI's page helpers (src/Shared.Ui), found by name.
// These files are plain scripts with no module system, so each test builds the globals the file touches,
// evaluates the real source in that sandbox, and asserts on what it did. No DOM library, no install step:
// `node --test tests/js-logic` needs Node and nothing else.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const JS_DIRS = ['Client', 'Shared.Ui'].map(project => path.join(__dirname, '..', '..', 'src', project, 'wwwroot', 'js'));
const jsPath = file => {
  const found = JS_DIRS.map(dir => path.join(dir, file)).filter(p => fs.existsSync(p));
  if (found.length !== 1) throw new Error(`${file}: expected in exactly one of ${JS_DIRS.join(', ')}, found ${found.length}`);
  return found[0];
};

/** Evaluates wwwroot/js/<file> with `globals` AS its global object (and as `window`), and returns it. */
function load(file, globals) {
  globals.window = globals;
  vm.createContext(globals);
  vm.runInContext(fs.readFileSync(jsPath(file), 'utf8'), globals, { filename: file });
  return globals;
}

/** A matchMedia stub. `api`: 'modern' (addEventListener), 'legacy' (addListener only) or 'none'. */
function mediaStub({ dark = false, api = 'modern', onRegister } = {}) {
  const listeners = [];
  const media = { matches: dark };
  const register = fn => { if (onRegister) onRegister(); listeners.push(fn); };
  if (api === 'modern') media.addEventListener = (type, fn) => { if (type === 'change') register(fn); };
  if (api === 'legacy') media.addListener = register;
  media.flip = value => { media.matches = value; listeners.forEach(fn => fn({ matches: value })); };
  media.listenerCount = () => listeners.length;
  return media;
}

/** The page theme.js runs in: a root element, localStorage, matchMedia. `attributes` records what it set. */
function themeGlobals({ saved = null, storageThrows = false, media = mediaStub() } = {}) {
  const attributes = {};
  return {
    matchMedia: () => media,
    document: { documentElement: { setAttribute: (k, v) => { attributes[k] = v; } } },
    localStorage: { getItem: () => { if (storageThrows) throw new Error('storage denied'); return saved; } },
    attributes,
    media,
  };
}

module.exports = { load, mediaStub, themeGlobals, jsPath };

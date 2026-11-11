const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const html = fs.readFileSync(path.join(__dirname, '../remote/public/index.html'), 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];
const tick = () => new Promise(resolve => setImmediate(resolve));
const response = (body, ok = true) => ({ ok, json: async () => body });
function deferred() { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return { promise, resolve, reject }; }

function app(initialPcs = []) {
  const elements = new Map();
  let document;
  class Element {
    constructor(id) {
      this.id = id; this.handlers = {}; this.hidden = false; this.disabled = false;
      this.value = ''; this.textContent = ''; this.dataset = {}; this.style = { setProperty() {} };
      const classes = new Set();
      this.classList = { add: x => classes.add(x), remove: x => classes.delete(x), contains: x => classes.has(x), toggle: (x, on) => on ? classes.add(x) : classes.delete(x) };
    }
    addEventListener(type, fn) { (this.handlers[type] ||= []).push(fn); }
    async emit(type, event = {}) { for (const fn of this.handlers[type] || []) await fn({ preventDefault() {}, target: this, ...event }); }
    click() { if (!this.disabled) return this.emit('click'); }
    focus() { document.activeElement = this; }
    blur() { if (document.activeElement === this) document.activeElement = null; }
    scrollIntoView() {}
    setPointerCapture() {}
  }
  for (const match of html.matchAll(/id="([^"]+)"([^>]*)/g)) {
    const element = new Element(match[1]); element.hidden = /\bhidden\b/.test(match[2]); elements.set(match[1], element);
  }
  document = new Element('document'); document.getElementById = id => elements.get(id); document.querySelectorAll = () => [];
  const window = new Element('window');
  const storage = new Map([['xdeck-pcs', JSON.stringify(initialPcs)]]);
  if (initialPcs.length) storage.set('xdeck-current', initialPcs[0].pc);
  const requests = [], timers = new Map();
  let timerId = 0;
  const subtle = {
    generateKey: async () => ({ publicKey: {}, privateKey: {} }),
    exportKey: async type => type === 'raw' ? new Uint8Array([1, 2]).buffer : { kty: 'EC' },
    importKey: async () => ({}), deriveBits: async () => new Uint8Array(32).buffer,
    digest: async () => new Uint8Array(32).buffer,
  };
  const context = vm.createContext({
    document, window, navigator: { userAgent: 'iPhone', onLine: true },
    localStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) },
    location: { protocol: 'https:', host: 'test.local' }, matchMedia: () => ({ matches: false }),
    crypto: { subtle }, AbortController, TextEncoder, TextDecoder, Uint8Array,
    btoa: s => Buffer.from(s, 'binary').toString('base64'), atob: s => Buffer.from(s, 'base64').toString('binary'),
    setTimeout: (fn, ms) => { const id = ++timerId; timers.set(id, { fn, ms }); return id; }, clearTimeout: id => timers.delete(id), performance: { now: () => 0 },
    WebSocket: class { close() {} }, confirm: () => true,
    fetch: (url, options) => {
      const pending = deferred(); requests.push({ url, options, ...pending });
      options.signal?.addEventListener('abort', () => pending.reject(Object.assign(new Error('Aborted'), { name: 'AbortError' })), { once: true });
      return pending.promise;
    },
  });
  vm.runInContext(script, context);
  return { el: id => elements.get(id), context, requests, timers, storage, window, document, run: code => vm.runInContext(code, context) };
}
async function enterCode(a, value = '123456') { a.el('code').value = value; await a.el('code').emit('input'); }

test('initial choice, manual stage, installation hints and Back reset without replacing input', async () => {
  const a = app(); const input = a.el('code');
  assert.equal(a.el('pair').classList.contains('on'), true);
  assert.equal(a.el('choiceStep').hidden, false); assert.equal(a.el('codeStep').hidden, true);
  assert.equal(a.el('iosHint').hidden, false);
  await a.window.emit('beforeinstallprompt', { prompt() {}, userChoice: Promise.resolve() });
  assert.equal(a.el('installBtn').hidden, false);
  await a.el('haveCode').click();
  assert.equal(a.el('choiceStep').hidden, true); assert.equal(a.el('codeStep').hidden, false);
  assert.equal(a.el('pairExtras').hidden, true); assert.equal(a.el('pairBack').hidden, false);
  await enterCode(a); await a.el('pairBack').click();
  assert.equal(a.el('choiceStep').hidden, false); assert.equal(a.el('code').value, '');
  assert.equal(a.el('pairBtn').disabled, true); assert.equal(a.el('code'), input);
  assert.deepEqual(JSON.parse(a.storage.get('xdeck-pcs')), []);
});

test('discovery success uses dedicated code stage; empty and failed discovery stay on choice', async () => {
  for (const result of [{ pcs: ['Office PC'] }, { pcs: [] }, { error: 'Discovery unavailable' }]) {
    const a = app(); const work = a.el('findBtn').click();
    assert.equal(a.requests[0].url, '/discover');
    a.requests[0].resolve(response(result, !result.error)); await work;
    const found = !!result.pcs?.length;
    assert.equal(a.el('codeStep').hidden, !found); assert.equal(a.el('choiceStep').hidden, found);
    if (found) assert.match(a.el('codeLead').textContent, /Office PC/);
    else { assert.ok(a.el('pairErr').textContent); assert.equal(a.el('findBtn').disabled, false); }
  }
});

test('late discovery success or failure cannot overwrite manual navigation and Back', async () => {
  for (const fail of [false, true]) {
    const a = app(); const work = a.el('findBtn').click();
    await a.el('haveCode').click(); await a.el('pairBack').click();
    assert.equal(a.requests[0].options.signal.aborted, true);
    if (fail) a.requests[0].reject(new Error('Late failure'));
    else a.requests[0].resolve(response({ pcs: ['Stale PC'] }));
    await work;
    assert.equal(a.el('codeStep').hidden, true); assert.equal(a.el('pairErr').textContent, '');
    assert.equal(a.el('findBtn').textContent, 'Find my PC'); assert.equal(a.el('findBtn').disabled, false);
  }
});

test('six digits format and enable Pair without automatic request; form submission locks navigation and recovers on failure', async () => {
  const a = app(); await a.el('haveCode').click();
  await enterCode(a, '12a34'); assert.equal(a.el('code').value, '123 4'); assert.equal(a.el('pairBtn').disabled, true);
  await enterCode(a, '12a34567'); assert.equal(a.el('code').value, '123 456'); assert.equal(a.el('pairBtn').disabled, false);
  assert.equal(a.requests.length, 0);
  await a.el('pairForm').emit('submit'); await tick();
  assert.equal(a.requests.length, 1); assert.equal(a.requests[0].url, '/pair');
  assert.equal(JSON.parse(a.requests[0].options.body).code, '123456');
  assert.equal(a.el('code').disabled, true); assert.equal(a.el('pairBack').disabled, true);
  await a.el('pairBack').click(); a.run('showPair(true)'); assert.equal(a.el('choiceStep').hidden, true);
  a.requests[0].resolve(response({ error: 'Code expired' }, false)); await tick();
  assert.equal(a.el('pairErr').textContent, 'Code expired'); assert.equal(a.el('code').disabled, false);
  assert.equal(a.el('pairBack').disabled, false); assert.equal(a.el('code').value, ''); assert.equal(a.el('pairBtn').disabled, true);
  assert.equal(a.document.activeElement, a.el('code'));
});

test('adding and cancelling another PC preserves saved pairings, current PC and menu', async () => {
  const oldPc = { pc: 'existing', pcName: 'Existing PC', phone: 'phone', token: 'test', pcPub: '', priv: {} };
  const a = app([oldPc]); await tick(); a.run('showPair(true)');
  assert.equal(a.el('pairCancel').hidden, false);
  const work = a.el('findBtn').click(); await a.el('pairCancel').click();
  assert.equal(a.requests[0].options.signal.aborted, true);
  a.requests[0].resolve(response({ pcs: ['Late PC'] })); await work;
  assert.equal(a.el('pair').classList.contains('on'), false);
  assert.deepEqual(JSON.parse(a.storage.get('xdeck-pcs')), [oldPc]); assert.equal(a.storage.get('xdeck-current'), 'existing');
  await a.el('pcBtn').click(); assert.match(a.el('sheet').innerHTML, /Existing PC/); assert.match(a.el('sheet').innerHTML, /Pair another PC/);
});

test('successful pairing adds a PC without erasing earlier pairings', async () => {
  const oldPc = { pc: 'existing', pcName: 'Existing PC', pcPub: '', priv: {} };
  const a = app([oldPc]); await tick(); a.run('showPair(true)');
  await a.el('haveCode').click(); await enterCode(a); await a.el('pairForm').emit('submit'); await tick();
  a.requests[0].resolve(response({ pc: 'new', phone: 'phone', token: 'test', pcPub: '', pcName: 'New PC' })); await tick();
  assert.equal(a.el('pair').classList.contains('on'), false);
  assert.deepEqual(JSON.parse(a.storage.get('xdeck-pcs')).map(p => p.pc), ['existing', 'new']);
  assert.equal(a.storage.get('xdeck-current'), 'new');
});

test('pairing timeout unlocks and refocuses code, clears timer and allows an explicit retry', async () => {
  const a = app(); await a.el('haveCode').click(); await enterCode(a);
  await a.el('pairForm').emit('submit'); await tick();
  assert.equal(a.requests.length, 1);
  await a.el('pairForm').emit('submit'); assert.equal(a.requests.length, 1);
  const [timerId, timer] = [...a.timers].find(([, timer]) => timer.ms === 15000);
  timer.fn(); await tick();
  assert.equal(a.requests[0].options.signal.aborted, true);
  assert.match(a.el('pairErr').textContent, /timed out.*try the code again/);
  assert.equal(a.el('code').disabled, false); assert.equal(a.el('pairBack').disabled, false);
  assert.equal(a.el('pairBtn').disabled, true); assert.equal(a.el('pairBtn').textContent, 'Pair');
  assert.equal(a.document.activeElement, a.el('code')); assert.equal(a.timers.has(timerId), false);
  await enterCode(a, '654321'); assert.equal(a.el('pairBtn').disabled, false);
  await a.el('pairForm').emit('submit'); await tick(); assert.equal(a.requests.length, 2);
  assert.equal(JSON.parse(a.requests[1].options.body).code, '654321');
  a.requests[1].resolve(response({ error: 'Code expired' }, false)); await tick();
  assert.equal(a.timers.size, 0); assert.equal(a.el('pairBack').disabled, false);
});

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

function app(initialPcs = [], extra = {}) {
  const elements = new Map();
  let document;
  class Element {
    constructor(id) {
      this.id = id; this.handlers = {}; this.hidden = false; this.disabled = false;
      this.parentElement = { classList: { toggle() {} } };
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
  for (const [k, v] of Object.entries(extra.storage || {})) storage.set(k, v);
  const requests = [], timers = new Map();
  let timerId = 0;
  const subtle = {
    generateKey: async () => ({ publicKey: {}, privateKey: {} }),
    exportKey: async type => type === 'raw' ? new Uint8Array([1, 2]).buffer : { kty: 'EC' },
    importKey: async () => ({}), deriveBits: async () => new Uint8Array(32).buffer,
    digest: async () => new Uint8Array(32).buffer,
    // "Encryption" that keeps the text readable: seal() output decodes with unseal(), and the tests
    // build PC messages with pcSays(). The real crypto is covered by the PC side.
    encrypt: async (_alg, _key, data) => data, decrypt: async (_alg, _key, data) => data,
    sign: async () => new Uint8Array(32).buffer, verify: async () => true,
  };
  const sockets = [];
  class FakeSocket {
    constructor(url) { this.url = url; this.readyState = 1; this.sent = []; sockets.push(this); }
    send(d) { this.sent.push(d); }
    close() { this.readyState = 3; }
  }
  const context = vm.createContext({
    document, window, navigator: { userAgent: 'iPhone', onLine: true, ...extra.navigator },
    localStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) },
    location: { protocol: 'https:', host: 'test.local', ...extra.location }, matchMedia: () => ({ matches: false }),
    setInterval: () => 0,
    crypto: { subtle, getRandomValues: a => a }, AbortController, TextEncoder, TextDecoder, Uint8Array,
    btoa: s => Buffer.from(s, 'binary').toString('base64'), atob: s => Buffer.from(s, 'base64').toString('binary'),
    setTimeout: (fn, ms) => { const id = ++timerId; timers.set(id, { fn, ms }); return id; }, clearTimeout: id => timers.delete(id), performance: { now: () => 0 },
    WebSocket: FakeSocket, confirm: () => true,
    fetch: (url, options) => {
      const pending = deferred(); requests.push({ url, options, ...pending });
      options.signal?.addEventListener('abort', () => pending.reject(Object.assign(new Error('Aborted'), { name: 'AbortError' })), { once: true });
      return pending.promise;
    },
  });
  vm.runInContext(script, context);
  return { el: id => elements.get(id), context, requests, timers, sockets, storage, window, document, run: code => vm.runInContext(code, context) };
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

// An installed app stays open for days: when a new service worker takes over (a deploy), the page
// reloads, but only on screen, with no finger on the touchpad, and not on the very first install.
function swApp(hadWorker) {
  const sw = { controller: hadWorker ? {} : null, handlers: {}, updates: 0,
    register: () => Promise.resolve(), getRegistration: () => Promise.resolve({ update: () => { sw.updates++; } }),
    addEventListener(type, fn) { (this.handlers[type] ||= []).push(fn); } };
  const reloads = [];
  const a = app([], { navigator: { serviceWorker: sw }, location: { reload: () => reloads.push(1) } });
  const takeOver = () => sw.handlers.controllerchange.forEach(fn => fn());
  return { a, sw, reloads, takeOver };
}

test('a new deploy reloads an open app when it is on screen and idle', async () => {
  const { a, sw, reloads, takeOver } = swApp(true);
  a.document.hidden = true;
  takeOver();
  assert.equal(reloads.length, 0, 'not while in the background');
  a.run('touches.set(1, {})');
  a.document.hidden = false;
  await a.document.emit('visibilitychange');
  await tick();
  assert.equal(sw.updates, 1, 'coming back asks for a new sw.js');
  assert.equal(reloads.length, 0, 'not with a finger on the touchpad');
  a.run('touches.clear()');
  await a.document.emit('visibilitychange');
  assert.equal(reloads.length, 1);
});

test('the first install of the service worker does not reload the page', () => {
  const { reloads, takeOver } = swApp(false);
  takeOver();
  assert.equal(reloads.length, 0);
});

// ---- PC health: the phone shows "Connected" only when the PC itself answers ----
const unseal = d => JSON.parse(Buffer.from(d, 'base64').subarray(16, -32).toString());
const pcSays = msg => Buffer.concat([Buffer.alloc(16), Buffer.from(JSON.stringify(msg)), Buffer.alloc(32)]).toString('base64');
const PC = { pc: 'pc1', pcName: 'Office PC', phone: 'ph', token: 'tk', pcPub: '', priv: {} };
async function online(extra) {
  const a = app([PC], extra); await tick(); await tick();
  const ws = a.sockets[a.sockets.length - 1];
  await ws.onmessage({ data: JSON.stringify({ t: 'pc', online: true, name: 'Office PC' }) });
  await tick(); await tick();
  const hear = async msg => { await ws.onmessage({ data: pcSays(msg) }); await tick(); await tick(); };
  const sent = () => ws.sent.map(unseal);
  const status = () => [a.el('status').className, a.el('statusText').textContent];
  const fire = async ms => { for (const [id, t] of [...a.timers]) if (t.ms === ms) { a.timers.delete(id); t.fn(); } await tick(); await tick(); };
  return { a, ws, hear, sent, status, fire };
}

test('relay online alone is only "Connecting to PC", and the phone pings and asks state', async () => {
  const h = await online();
  assert.deepEqual(h.status(), ['status ', 'Connecting to PC…']);
  assert.deepEqual(h.sent().filter(m => m.t === 'ping'), [{ t: 'ping', n: 1 }]);
  assert.ok(h.sent().some(m => m.t === 'state'));
});

test('a pong shows Connected and records the PC version and capabilities', async () => {
  const h = await online();
  await h.hear({ t: 'pong', n: 1, ver: '0.7.9', caps: ['input', 'audio', 'ping'] });
  assert.deepEqual(h.status(), ['status on', 'Connected']);
  assert.equal(h.a.run('pcVer'), '0.7.9');
  assert.deepEqual([...h.a.run('pcCaps')], ['input', 'audio', 'ping']);
  await h.fire(5000);   // the 5 s tick pings again with the next number
  assert.deepEqual(h.sent().filter(m => m.t === 'ping').map(m => m.n), [1, 2]);
});

test('an old PC that only answers state is Connected, legacy, with inferred capabilities', async () => {
  const h = await online();
  await h.hear({ t: 'state', outs: [], def: '', vol: 0.5, mute: false, mics: [], apps: [{ id: 'obs', running: false }] });
  assert.deepEqual(h.status(), ['status on', 'Connected']);
  assert.equal(h.a.run('pcLegacy'), true);
  assert.deepEqual([...h.a.run('pcCaps')].sort(), ['apps', 'audio', 'input', 'mics', 'obs']);
  assert.doesNotMatch(h.a.el('mics').innerHTML, /Needs Xdeck/);
  assert.doesNotMatch(h.a.el('appBody').innerHTML, /Needs Xdeck/);
  // a legacy PC is not pinged again: its liveness check is a state request every 10 s
  const before = h.sent().filter(m => m.t === 'ping').length;
  await h.fire(5000); await h.fire(5000);
  assert.equal(h.sent().filter(m => m.t === 'ping').length, before);
  assert.equal(h.sent().filter(m => m.t === 'state').length, 2);
});

test('silence shows PC not responding with a hint, and the next answer recovers it; input stays usable', async () => {
  const h = await online();
  await h.hear({ t: 'pong', n: 1, ver: '0.7.9', caps: ['input', 'ping'] });
  await h.fire(15000);
  assert.deepEqual(h.status(), ['status bad', 'PC not responding']);
  assert.equal(h.a.el('healthHint').hidden, false);
  assert.match(h.a.el('healthHint').textContent, /Restart Xdeck/);
  h.a.run('send({ t: "move", dx: 1, dy: 1 })'); await tick(); await tick();
  assert.ok(h.sent().some(m => m.t === 'move'));
  await h.hear({ t: 'pong', n: 2, ver: '0.7.9', caps: ['input', 'ping'] });
  assert.deepEqual(h.status(), ['status on', 'Connected']);
  assert.equal(h.a.el('healthHint').hidden, true);
});

test('a PC that never answers turns bad after 8 s, and the relay saying offline shows PC offline', async () => {
  const h = await online();
  await h.fire(8000);
  assert.deepEqual(h.status(), ['status bad', 'PC not responding']);
  await h.ws.onmessage({ data: JSON.stringify({ t: 'pc', online: false }) });
  assert.deepEqual(h.status(), ['status bad', 'PC offline']);
  assert.equal(h.a.el('healthHint').hidden, true);
});

test('a legacy PC without apps gets a note naming the needed version, never "Update Xdeck"', async () => {
  const h = await online();
  await h.hear({ t: 'state', outs: [], def: '', vol: 0.5, mute: false });
  assert.match(h.a.el('appBody').innerHTML, /Needs Xdeck 0.7.7 or newer on the PC.</);
  assert.match(h.a.el('mics').innerHTML, /Needs Xdeck 0.7.7 or newer on the PC.</);
  assert.doesNotMatch(h.a.el('appBody').innerHTML + h.a.el('mics').innerHTML, /this PC has|Update Xdeck/);
  assert.equal(h.a.el('brightBox').hidden, true);
  // with a known version the note says what the PC has
  await h.hear({ t: 'pong', n: 1, ver: '0.7.6', caps: ['input', 'audio'] });
  await h.hear({ t: 'state', outs: [], def: '', vol: 0.5, mute: false });
  assert.match(h.a.el('mics').innerHTML, /(this PC has 0.7.6)/);
});

// ---- touchpad settings: pointer speed, scroll speed, reverse scrolling, saved on the phone ----
async function drag(h) {   // one finger, 10 px in 1 ms: gain is SPEED * speed * 3.5
  h.a.run('fingerDown(1, 0, 0, 0); fingerMove(1, 10, 0, 1); fingerUp(1, 400)');
  await h.fire(12);
  return h.sent().filter(m => m.t === 'move').map(m => m.dx);
}
async function scroll(h, dy) {   // two fingers, the first moves dy: scrollAcc grows by dy / 2
  h.a.run(`fingerDown(1, 0, 0, 0); fingerDown(2, 50, 0, 0); fingerMove(1, 0, ${dy}, 1); fingerUp(1, 400); fingerUp(2, 400)`);
  await tick(); await tick();
  return h.sent().filter(m => m.t === 'scroll').map(m => m.v);
}
const pick = (attr, value) => ({ target: { closest: () => ({ dataset: { [attr]: value } }) } });

test('touchpad defaults keep today\'s feel: 1x speed, normal notch, no reversal', async () => {
  const h = await online();
  assert.deepEqual({ ...h.a.run('padCfg') }, { speed: 100, scroll: 'normal', natural: false });
  assert.deepEqual(await drag(h), [56]);
  assert.deepEqual(await scroll(h, 88), [240]);
});

test('pointer speed multiplies the gain and is saved; scroll speed changes the notch; reverse scrolling flips', async () => {
  const h = await online();
  h.a.el('padSpeed').value = '200'; await h.a.el('padSpeed').emit('input');
  assert.deepEqual(JSON.parse(h.a.storage.get('xdeck-pad')), { speed: 200, scroll: 'normal', natural: false });
  assert.match(h.a.el('padSpeedPct').textContent, /^2\.0/);
  assert.deepEqual(await drag(h), [112]);
  h.a.el('padSpeed').value = '50'; await h.a.el('padSpeed').emit('input');
  assert.deepEqual((await drag(h)).slice(-1), [28]);
  h.a.el('padSpeed').value = '999'; await h.a.el('padSpeed').emit('input');   // clamped to 2.5x
  assert.equal(h.a.run('padCfg.speed'), 250);

  await h.a.el('padCfg').emit('click', pick('scroll', 'slow'));
  assert.deepEqual(await scroll(h, 88), [120]);
  await h.a.el('padCfg').emit('click', pick('scroll', 'fast'));
  assert.deepEqual((await scroll(h, 88)).slice(-1), [360]);
  await h.a.el('padCfg').emit('click', pick('scroll', 'normal'));
  await h.a.el('padNatural').emit('click');
  assert.equal(h.a.run('padCfg.natural'), true);
  assert.equal(h.a.el('padNatural').textContent, 'Reverse scrolling: On');
  assert.deepEqual((await scroll(h, 88)).slice(-1), [-240]);
  assert.deepEqual(JSON.parse(h.a.storage.get('xdeck-pad')), { speed: 250, scroll: 'normal', natural: true });
});

test('touchpad settings load from storage, clamp bad values, and survive storage that throws', () => {
  const saved = app([], { storage: { 'xdeck-pad': JSON.stringify({ speed: 150, scroll: 'fast', natural: true }) } });
  assert.deepEqual({ ...saved.run('padCfg') }, { speed: 150, scroll: 'fast', natural: true });
  const bad = app([], { storage: { 'xdeck-pad': JSON.stringify({ speed: 9999, scroll: 'bogus', natural: 'yes' }) } });
  assert.deepEqual({ ...bad.run('padCfg') }, { speed: 250, scroll: 'normal', natural: false });
  const junk = app([], { storage: { 'xdeck-pad': '{not json' } });
  assert.deepEqual({ ...junk.run('padCfg') }, { speed: 100, scroll: 'normal', natural: false });
  assert.equal(junk.run('cleanPad({ speed: 0 }).speed'), 50);
  // a storage that refuses writes must not break the controls
  junk.run('localStorage.setItem = () => { throw new Error("full"); }');
  assert.doesNotThrow(() => junk.run('savePad()'));
});

test('the gear opens and closes the settings sheet; switching tab closes it', async () => {
  const a = app();
  assert.equal(a.el('padCfg').hidden, true);
  await a.el('padCfgBtn').click(); assert.equal(a.el('padCfg').hidden, false);
  await a.el('padCfgBtn').click(); assert.equal(a.el('padCfg').hidden, true);
  await a.el('padCfgBtn').click(); await a.el('padCfgDone').click(); assert.equal(a.el('padCfg').hidden, true);
  await a.el('padCfgBtn').click();
  await a.el('tabs').emit('click', { target: { closest: () => ({ dataset: { tab: 'keys' } }) } });
  assert.equal(a.el('padCfg').hidden, true);
});

test('the manifest allows landscape and the service worker cache was bumped', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '../remote/public/manifest.webmanifest'), 'utf8'));
  assert.equal(manifest.orientation, 'any');
  assert.match(fs.readFileSync(path.join(__dirname, '../remote/public/sw.js'), 'utf8'), /xdeck-remote-v(1\d|[2-9]\d)"/);
  assert.match(html, /@media \(orientation: landscape\)/);
});

// ---- per-app volume (mixer) ----
const MIXER = [{ id: '12', name: 'Firefox', vol: 0.5, mute: false }, { id: '40', name: 'Spotify', vol: 0.2, mute: true }];
const state = extra => ({ t: 'state', outs: [], def: '', vol: 0.5, mute: false, mics: [], apps: [], bright: -1, ...extra });

test('the mixer lists each app with its volume and mute, and sends appvol (throttled) and appmute', async () => {
  const h = await online();
  await h.hear({ t: 'pong', n: 1, ver: '0.7.9', caps: ['input', 'audio', 'mixer', 'padbattery', 'ping'] });
  await h.hear(state({ mixer: MIXER }));
  assert.equal(h.a.el('mixBox').hidden, false);
  const list = h.a.el('mixList').innerHTML;
  assert.match(list, /Firefox/); assert.match(list, /value="50"/); assert.match(list, /Spotify/); assert.match(list, /Muted/);
  assert.doesNotMatch(list, /Needs Xdeck/);

  const slider = { dataset: { vid: '12' }, value: '30', style: { setProperty() {} } };
  await h.a.el('mixList').emit('input', { target: slider });
  slider.value = '35'; await h.a.el('mixList').emit('input', { target: slider });
  assert.equal(h.sent().filter(m => m.t === 'appvol').length, 0);   // held back 40 ms
  await h.fire(40);
  assert.deepEqual(h.sent().filter(m => m.t === 'appvol'), [{ t: 'appvol', id: '12', v: 0.35 }]);

  // while the slider is held a new state does not redraw the list; it catches up on release
  const before = h.a.el('mixList').innerHTML;
  await h.hear(state({ mixer: [{ id: '12', name: 'Firefox', vol: 0.9, mute: false }] }));
  assert.equal(h.a.el('mixList').innerHTML, before);
  await h.a.el('mixList').emit('change', { target: slider });
  assert.deepEqual(h.sent().filter(m => m.t === 'appvol').pop(), { t: 'appvol', id: '12', v: 0.35 });
  assert.match(h.a.el('mixList').innerHTML, /value="90"/);

  await h.a.el('mixList').emit('click', pick('mid', '12')); await tick(); await tick();
  assert.deepEqual(h.sent().filter(m => m.t === 'appmute'), [{ t: 'appmute', id: '12' }]);
});

test('an empty mixer says so; a mixer without the caps (error on the PC) stays hidden', async () => {
  const h = await online();
  await h.hear({ t: 'pong', n: 1, ver: '0.7.9', caps: ['input', 'audio', 'mixer'] });
  await h.hear(state({ mixer: [] }));
  assert.equal(h.a.el('mixBox').hidden, false);
  assert.match(h.a.el('mixList').innerHTML, /No app is playing sound/);
  await h.hear(state({}));   // the PC could not list sessions this time: no mixer field
  assert.equal(h.a.el('mixBox').hidden, true);
});

test('old PCs: no mixer at all when it never answered ping; one "Needs Xdeck 0.7.9" note after a pong without the cap', async () => {
  const legacy = await online();
  await legacy.hear(state({}));
  assert.equal(legacy.a.run('pcLegacy'), true);
  assert.equal(legacy.a.el('mixBox').hidden, true);
  assert.equal(legacy.a.el('padsBox').hidden, true);

  const h = await online();
  await h.hear({ t: 'pong', n: 1, ver: '0.7.8', caps: ['input', 'audio', 'mics', 'apps', 'bright', 'ping'] });
  await h.hear(state({}));
  assert.equal(h.a.el('mixBox').hidden, false);
  const note = h.a.el('mixList').innerHTML;
  assert.equal(note.split('Needs Xdeck 0.7.9 or newer').length - 1, 1);
  assert.match(note, /this PC has 0\.7\.8/);
  await h.hear(state({}));
  assert.equal(h.a.el('mixList').innerHTML.split('Needs Xdeck').length - 1, 1);
  // input keeps working
  h.a.run('send({ t: "move", dx: 1, dy: 1 })'); await tick(); await tick();
  assert.ok(h.sent().some(m => m.t === 'move'));
});

test('a legacy state that does carry a mixer infers the capability and shows it', async () => {
  const h = await online();
  await h.hear(state({ mixer: MIXER, pads: [] }));
  assert.ok(h.a.run('pcCaps').has('mixer') && h.a.run('pcCaps').has('padbattery'));
  assert.equal(h.a.el('mixBox').hidden, false);
});

// ---- controllers' batteries ----
const pads = (...list) => state({ pads: list });
const xbox = (battery, charging = false) => ({ name: 'Xbox', battery, charging, wired: false });

test('the Controllers block lists each pad with its battery, wired and unknown ones too; hidden with none', async () => {
  const h = await online();
  await h.hear({ t: 'pong', n: 1, ver: '0.7.9', caps: ['input', 'padbattery'] });
  await h.hear(pads(xbox(80), { name: 'Wheel', battery: -1, charging: false, wired: true }, { name: 'Pro', battery: -1, charging: false, wired: false }, xbox(0, true)));
  assert.equal(h.a.el('padsBox').hidden, false);
  const html_ = h.a.el('padsList').innerHTML;
  assert.match(html_, /Xbox/); assert.match(html_, /80%/); assert.match(html_, /Wired/); assert.match(html_, /Battery unknown/);
  assert.equal((html_.match(/class="on"/g) || []).length, 4 + 0);   // 80% = 4 pills; the 0% charging pad has none
  await h.hear(pads());
  assert.equal(h.a.el('padsBox').hidden, true);
  await h.hear(state({}));
  assert.equal(h.a.el('padsBox').hidden, true);
});

test('low battery: one toast and one vibration when it first drops to 15% or below, not again until it charges above 20%', async () => {
  const buzz = [];
  const h = await online({ navigator: { vibrate: ms => buzz.push(ms) } });
  const toast = () => [h.a.el('toast').hidden, h.a.el('toast').textContent];
  await h.hear(pads(xbox(50)));
  assert.equal(h.a.el('toast').hidden, true);
  await h.hear(pads(xbox(16)));
  assert.equal(h.a.el('toast').hidden, true);
  await h.hear(pads(xbox(12)));
  assert.deepEqual(toast(), [false, 'Xbox battery low (12%)']);
  assert.equal(buzz.length, 1);
  await h.fire(4500);
  assert.equal(h.a.el('toast').hidden, true);
  await h.hear(pads(xbox(11))); await h.hear(pads(xbox(18))); await h.hear(pads(xbox(14)));
  assert.equal(h.a.el('toast').hidden, true); assert.equal(buzz.length, 1);   // climbed to 18 only: no repeat
  await h.hear(pads(xbox(30)));                                              // above 20: re-armed
  await h.hear(pads(xbox(15)));
  assert.deepEqual(toast(), [false, 'Xbox battery low (15%)']);
  assert.equal(buzz.length, 2);
});

test('no low-battery toast for a pad already low when the app opens, one that is charging, or a wired one', async () => {
  const buzz = [];
  const h = await online({ navigator: { vibrate: ms => buzz.push(ms) } });
  await h.hear(pads(xbox(10)));                       // first reading is already low: it is on the list, in red, no toast
  await h.hear(pads(xbox(9)));
  await h.hear(pads(xbox(40, true)));
  await h.hear(pads(xbox(10, true)));                 // charging
  await h.hear(pads({ name: 'Wheel', battery: 5, charging: false, wired: true }));
  assert.equal(h.a.el('toast').hidden, true);
  assert.equal(buzz.length, 0);
});

const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

function panel() {
  const appEvents = {}, documentEvents = {}, messages = [];
  const app = { innerHTML: "", addEventListener(type, callback) { appEvents[type] = callback; } };
  let now = 1700000000000, nextTimer = 0, interval;
  const timeouts = new Map();
  let receive;
  const context = vm.createContext({
    window: { addEventListener() {}, chrome: { webview: { postMessage(value) { messages.push(value); }, addEventListener(_, fn) { receive = fn; } } } },
    document: {
      getElementById: (id) => id === "app" ? app : null,
      addEventListener(type, callback) { documentEvents[type] = callback; }, querySelectorAll: () => [], querySelector: () => null,
      documentElement: { style: { setProperty() {} } }, body: { classList: { toggle() {} } },
    },
    location: { hash: "" }, requestAnimationFrame: () => 0,
    Date: class extends Date { static now() { return now; } },
    setTimeout(fn, delay) { const id = ++nextTimer; timeouts.set(id, { fn, at: now + delay }); return id; },
    clearTimeout(id) { timeouts.delete(id); },
    setInterval(fn) { interval = fn; return 1; }, clearInterval() { interval = null; },
  });
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../ui/app.js"), "utf8"), context);
  return {
    state(value) {
      receive({ data: { type: "state", outs: [], mics: [], pads: [], devices: [], ...value } });
      return app.innerHTML;
    },
    click(act) {
      appEvents.click({ target: { closest: () => ({ dataset: { act } }) } });
      return app.innerHTML;
    },
    escape() { documentEvents.keydown({ key: "Escape" }); return app.innerHTML; },
    advance(ms) {
      now += ms;
      for (const [id, timer] of timeouts) if (timer.at <= now) { timeouts.delete(id); timer.fn(); }
      if (interval) interval();
      return app.innerHTML;
    },
    get html() { return app.innerHTML; },
    messages,
    context,
  };
}

test("empty categories disappear, including after devices disconnect", () => {
  const ui = panel();
  const present = ui.state({
    outs: [{ id: "out", name: "Speakers" }], mics: [{ id: "mic", name: "Microphone" }],
    pads: [{ key: "pad", name: "Xbox controller", kind: "xbox", conn: "USB" }],
    devices: [{ name: "Mouse", glyph: "mouse", conn: "USB" }],
  });
  for (const title of ["Audio output", "Microphone", "Controllers", "Devices"])
    assert.ok(present.includes(`<h2>${title}</h2>`));
  const empty = ui.state({});
  assert.doesNotMatch(empty, /<h2>|No controllers|No outputs|No microphones|ring-row/);
});

test("categories reconnect independently", () => {
  const ui = panel();
  const output = ui.state({ outs: [{ id: "out", name: "Speakers" }] });
  assert.match(output, /<h2>Audio output<\/h2>/);
  assert.doesNotMatch(output, /<h2>Microphone|<h2>Controllers|<h2>Devices/);
  const mic = ui.state({ mics: [{ id: "mic", name: "Microphone" }] });
  assert.match(mic, /<h2>Microphone<\/h2>/);
  assert.doesNotMatch(mic, /<h2>Audio output|<h2>Controllers|<h2>Devices/);
});

test("unidentified USB nodes do not create a device section", () => {
  const html = panel().state({ devices: [
    { name: "USB", glyph: "mouse", conn: "USB" },
    { name: "USB Input Device", glyph: "mouse", conn: "USB" },
    { name: "USB Composite Device", glyph: "keyboard", conn: "USB" },
    { name: "Unknown adapter", glyph: "usb", conn: "USB" },
    { name: "Disconnected keyboard", glyph: "keyboard", conn: "USB", connected: false },
  ] });
  assert.doesNotMatch(html, /<h2>Devices|ring-row/);
});

test("Type Cover is named and uses a keyboard icon, without an empty controller section", () => {
  const ui = panel();
  const html = ui.state({ devices: [{ name: "Surface Type Cover", glyph: "mouse", conn: "USB", battery: -1 }] });
  assert.match(html, /<span class="ring-name">Surface Type Cover<\/span>/);
  assert.ok(html.includes(vm.runInContext("I.keyboard", ui.context)));
  assert.doesNotMatch(html, /<h2>Controllers|No controllers|>USB<|class="ring-lbl"/);
});

test("named USB inputs and wireless battery readings remain visible and escaped", () => {
  const html = panel().state({ devices: [
    { name: "Office keyboard", glyph: "keyboard", conn: "USB", battery: -1 },
    { name: 'Mouse <A&B> "test"', glyph: "mouse", conn: "Bluetooth", battery: 42, charging: true },
  ] });
  assert.match(html, /Office keyboard/);
  assert.match(html, /Mouse &lt;A&amp;B&gt; &quot;test&quot;/);
  assert.match(html, /42%/);
  assert.match(html, /ring-fill/);
  assert.match(html, /class="bolt"/);
});

test("Settings generates a code through the existing native bridge and stays open on updates", () => {
  const ui = panel();
  assert.match(ui.state({ remote: { online: true } }), /data-act="settings"/);
  assert.match(ui.click("settings"), /<h1>Settings<\/h1>/);
  assert.match(ui.click("remote-code"), /Generating code/);
  ui.click("remote-code");
  assert.equal(ui.messages.filter(m => m.type === "remote-code").length, 1);
  const received = ui.state({ remote: { online: true, code: "123456", codeLeft: 60000, phones: [] } });
  assert.match(received, /<h1>Settings<\/h1>/);
  assert.match(received, /123 456/);
  assert.match(received, /1:00 left/);
  assert.match(received, /Generate new code/);
  ui.click("remote-cancel");
  assert.equal(ui.messages.at(-1).type, "remote-cancel");
  assert.doesNotMatch(ui.state({ remote: { online: true } }), /123 456/);
});

test("Settings handles offline, native errors and generation timeout without repeated requests", () => {
  const ui = panel();
  ui.state({ remote: { online: false } });
  assert.match(ui.click("settings"), /Waiting for an internet connection/);
  ui.click("remote-code");
  assert.equal(ui.messages.filter(m => m.type === "remote-code").length, 0);
  ui.state({ remote: { online: true } });
  ui.click("remote-code");
  assert.match(ui.advance(10001), /Could not get a code/);
  ui.click("remote-code");
  assert.equal(ui.messages.filter(m => m.type === "remote-code").length, 2);
  const failure = ui.state({ remote: { online: false, error: "Relay <unavailable>" } });
  assert.match(failure, /Relay &lt;unavailable&gt;/);
  assert.doesNotMatch(failure, /Generating code/);
});

test("Code expiry restores generation and Settings has Back, Escape and controller navigation", () => {
  const ui = panel();
  ui.state({ remote: { online: true, code: "123456", codeLeft: 1000 } });
  ui.click("settings");
  const expired = ui.advance(1100);
  assert.doesNotMatch(expired, /123 456/);
  assert.match(expired, /Generate access code/);
  assert.match(ui.click("back"), /brand-name/);
  ui.click("settings");
  assert.match(ui.escape(), /brand-name/);
  ui.click("settings");
  vm.runInContext('onPad("b")', ui.context);
  assert.match(ui.html, /brand-name/);
  assert.equal(ui.messages.filter(m => m.type === "close").length, 0);
});

test("Settings lists existing phones safely without unpairing them", () => {
  const ui = panel();
  ui.state({ remote: { online: true, phones: [
    { id: "p1", name: "Phone <one>", online: true },
    { id: "p2", name: "Tablet", online: false },
  ] } });
  const html = ui.click("settings");
  assert.match(html, /Phone &lt;one&gt;/);
  assert.match(html, /Tablet/);
  assert.match(html, /Connected/);
  assert.match(html, /Offline/);
  assert.equal(ui.messages.filter(m => m.type === "remote-unpair").length, 0);
});

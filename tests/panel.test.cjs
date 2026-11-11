const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

function panel() {
  const app = { innerHTML: "", addEventListener() {} };
  let receive;
  const context = vm.createContext({
    window: { addEventListener() {}, chrome: { webview: { postMessage() {}, addEventListener(_, fn) { receive = fn; } } } },
    document: {
      getElementById: (id) => id === "app" ? app : null,
      addEventListener() {}, querySelectorAll: () => [], querySelector: () => null,
      documentElement: { style: { setProperty() {} } }, body: { classList: { toggle() {} } },
    },
    location: { hash: "" }, requestAnimationFrame: () => 0,
    setInterval: () => 0, clearInterval() {},
  });
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../ui/app.js"), "utf8"), context);
  return {
    state(value) {
      receive({ data: { type: "state", outs: [], mics: [], pads: [], devices: [], ...value } });
      return app.innerHTML;
    },
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

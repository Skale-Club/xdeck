// Xdeck UI. The native host (Xdeck.exe) sends:
//   {type:"state", accent, outs:[{id,name,kind}], mics:[...], defOut, defMic, pads:[{key,kind,name,index,battery,charging,conn}]}
//   {type:"levels", levels:{endpointId: 0..1}}
//   {type:"show", pad, combo}                   the window was just opened (pad: from the controller shortcut;
//                                               combo: that shortcut as written in shortcuts.ini, "" if off)
//   {type:"shortcuts", combo}                   shortcuts.ini was saved
//   {type:"pad", btn}                           controller navigation: up down left right a b x start bhold
// and receives:
//   {type:"size", w, h}  {type:"close"}  {type:"select", id}  {type:"mouse", key, on}  {type:"update"}
// state.version is this build; state.update the downloaded release waiting for a restart (or null).
// each pad in state also carries canMouse / mouse (that controller as a mouse); state.mouseKey is
// the mouse shortcut.
"use strict";

const host = window.chrome && window.chrome.webview;
const send = (m) => { if (host) host.postMessage(m); };
const $app = document.getElementById("app");

let state = { outs: [], mics: [], pads: [], defOut: null, defMic: null, accent: "#0078d4" };
let levels = {};
let view = "panel";     // panel | tester
let expanded = 0;       // 0 none, 1 outputs, 2 microphones
let testing = null;     // pad under test
let padMode = false;    // a controller is driving the panel: show the focus ring and the button help
let focusKey = null;    // data-nav of the focused item
let panelCombo = "L1 + Right + Triangle";   // the controller shortcut that opens / hides the panel
const svgCache = {};

// ------------------------------------------------------------ icons
const I = {
  monitor: '<svg viewBox="0 0 24 24"><rect x="2.5" y="3.5" width="19" height="13" rx="2" fill="none" stroke="currentColor" stroke-width="2"/><path d="M12 16.5v3.5M8 20.5h8" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>',
  tv: '<svg viewBox="0 0 24 24"><rect x="1.5" y="4.5" width="21" height="13" rx="2" fill="none" stroke="currentColor" stroke-width="2"/><path d="M6.5 20.5l1.2-3M17.5 20.5l-1.2-3" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>',
  headphones: '<svg viewBox="0 0 24 24"><path d="M4 15v-3a8 8 0 0 1 16 0v3" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/><rect x="2.5" y="13.5" width="5.5" height="8" rx="2.4" fill="currentColor"/><rect x="16" y="13.5" width="5.5" height="8" rx="2.4" fill="currentColor"/></svg>',
  speaker: '<svg viewBox="0 0 24 24"><path d="M3.5 9h3.8L12 5v14l-4.7-4H3.5z" fill="currentColor" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"/><path d="M15.5 9.2a4 4 0 0 1 0 5.6M18.2 6.6a7.6 7.6 0 0 1 0 10.8" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>',
  mic: '<svg viewBox="0 0 24 24"><rect x="8.6" y="2.5" width="6.8" height="11.5" rx="3.4" fill="currentColor"/><path d="M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5v3M8.5 21h7" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>',
  pad: '<svg viewBox="0 0 24 24"><path fill-rule="evenodd" fill="currentColor" d="M7.2 5.5h9.6a5 5 0 0 1 4.9 4.1l1 5.6a3.1 3.1 0 0 1-5.4 2.5L15.4 15.6H8.6l-1.9 2.1a3.1 3.1 0 0 1-5.4-2.5l1-5.6a5 5 0 0 1 4.9-4.1zM6.2 9.6v1.5H4.7v1.6h1.5v1.5h1.6v-1.5h1.5v-1.6H7.8V9.6zM16.6 9.1a1 1 0 1 0 0 .01zM18.6 11.1a1 1 0 1 0 0 .01zM14.6 11.1a1 1 0 1 0 0 .01zM16.6 13.1a1 1 0 1 0 0 .01z"/></svg>',
  wheel: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" stroke-width="2.4"/><circle cx="12" cy="12" r="2.6" fill="currentColor"/><path d="M3.6 11h6M14.4 11h6M12 14.6v6" stroke="currentColor" stroke-width="2.4"/></svg>',
  handbrake: '<svg viewBox="0 0 24 24"><path d="M6.5 21h11" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/><rect x="9.8" y="10" width="4.4" height="10" rx="1.6" fill="currentColor"/><path d="M12 11L16.2 4.2" stroke="currentColor" stroke-width="2.8" stroke-linecap="round"/><circle cx="16.6" cy="3.6" r="2.2" fill="currentColor"/></svg>',
  chev: '<svg viewBox="0 0 12 12"><path d="M2 4.2l4 4 4-4" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  close: '<svg viewBox="0 0 14 14"><path d="M2 2l10 10M12 2L2 12" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>',
  mouse: '<svg viewBox="0 0 24 24"><rect x="6" y="2.5" width="12" height="19" rx="6" fill="none" stroke="currentColor" stroke-width="2.2"/><path d="M12 6.5v4" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/></svg>',
  keyboard: '<svg viewBox="0 0 24 24"><rect x="1.8" y="6" width="20.4" height="12.5" rx="2.2" fill="none" stroke="currentColor" stroke-width="2"/><path d="M5.5 9.6h1.2M9.2 9.6h1.2M13 9.6h1.2M16.8 9.6h1.2M5.5 12.4h1.2M9.2 12.4h1.2M13 12.4h1.2M16.8 12.4h1.2M8 15.2h8" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"/></svg>',
  bt: '<svg viewBox="0 0 24 24"><path d="M7 7.5l10 9-5 4.5V3l5 4.5-10 9" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  usb: '<svg viewBox="0 0 24 24"><path d="M12 21V4M9 6.5L12 3l3 3.5M12 16l-4.5-3V10M12 13.5l4.5-2.5V8.5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/><circle cx="12" cy="21" r="1.6" fill="currentColor"/><rect x="5.9" y="8.4" width="3.2" height="3" rx=".6" fill="currentColor"/><circle cx="16.5" cy="7.7" r="1.5" fill="currentColor"/></svg>',
  update: '<svg viewBox="0 0 24 24"><path d="M12 3v11M7.5 9.5L12 14l4.5-4.5M4.5 15.5v3a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2v-3" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  cursor: '<svg viewBox="0 0 24 24"><path d="M5 2.8l13.6 11.1-6.1.6 3.6 6.9-2.6 1.3-3.5-7L5.6 20z" fill="currentColor" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"/></svg>',
  back: '<svg viewBox="0 0 16 16"><path d="M10 3L5 8l5 5" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>',
};

// The app mark: the same 8-tooth gear the tray icon draws (src/Xdeck.cs, Gear()).
I.gear = (() => {
  const c = 16, rOut = 15, rIn = 15 * 0.74, teeth = 8, half = Math.PI / teeth, pts = [];
  const at = (a, r) => (c + Math.cos(a) * r).toFixed(2) + "," + (c + Math.sin(a) * r).toFixed(2);
  for (let i = 0; i < teeth; i++) {
    const a = (i * 2 * Math.PI) / teeth;
    pts.push(at(a - half * 0.55, rIn), at(a - half * 0.38, rOut), at(a + half * 0.38, rOut), at(a + half * 0.55, rIn), at(a + half, rIn * 0.98));
  }
  const h = 15 * 0.34;
  return `<svg viewBox="0 0 32 32"><path fill="currentColor" fill-rule="evenodd" d="M${pts.join("L")}Z M${c - h},${c} a${h},${h} 0 1 0 ${2 * h},0 a${h},${h} 0 1 0 ${-2 * h},0Z"/></svg>`;
})();

const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));

function audioKind(name) {
  const n = name.toLowerCase();
  if (/airpods|air pods|pulse|head/.test(n)) return "headphones";
  if (/\btv\b/.test(n)) return "tv";
  if (/monitor|display/.test(n)) return "monitor";
  return "speaker";
}

function padIcon(kind) {
  if (kind === "g29") return I.wheel;
  if (kind === "handbrake") return I.handbrake;
  return I.pad;
}

// Brand logos (ui/brands, CC0 Simple Icons / Apache Material Design Icons), tinted with CSS masks.
const PAD_BRAND = { xbox: "xbox", dualsense: "playstation", switchpro: "nintendo", joycon: "nintendo", g29: "logitechg" };

function audioBrand(name) {
  const n = name.toLowerCase();
  if (/roku/.test(n)) return "roku";
  if (/acer/.test(n)) return "acer";
  if (/airpods|apple/.test(n)) return "apple";
  if (/pulse|playstation|dualsense/.test(n)) return "playstation";
  if (/yeti|logitech/.test(n)) return "logitech";
  if (/xbox/.test(n)) return "xbox";
  if (/lark|hollyland|wireless microphone/.test(n)) return "hollyland";
  return null;
}

const BRAND_FILE = { hollyland: "hollyland.png" };   // everything else is brands/<name>.svg
const logo = (brand, cls) => (brand ? `<span class="logo ${cls || ""}" style="--logo:url(brands/${BRAND_FILE[brand] || brand + ".svg"})" title="${brand}"></span>` : "");

const meter = (id) => `<span class="meter" data-id="${esc(id)}">${"<i></i>".repeat(12)}</span>`;

// ------------------------------------------------------------ volume
I.speakerOff = '<svg viewBox="0 0 24 24"><path d="M3.5 9h3.8L12 5v14l-4.7-4H3.5z" fill="currentColor" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"/><path d="M16 9.5l5 5M21 9.5l-5 5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>';
I.micOff = '<svg viewBox="0 0 24 24"><rect x="8.6" y="2.5" width="6.8" height="11.5" rx="3.4" fill="currentColor"/><path d="M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5v3M8.5 21h7" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M4 3l16 18" stroke="var(--card)" stroke-width="4"/><path d="M4 3l16 18" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>';
let dragging = false, pendingState = null, volTimer = 0;

function volumeHtml(e, sec) {
  if (e.vol === undefined) return "";
  const pct = Math.round(e.vol * 100);
  const on = sec === 2 ? I.mic : I.speaker, off = sec === 2 ? I.micOff : I.speakerOff;
  return `<div class="vol${e.mute ? " muted" : ""}" data-id="${esc(e.id)}" data-sec="${sec}" data-nav="vol-${sec}">
      <button class="icon-btn mute" data-act="mute" data-id="${esc(e.id)}" data-m="${e.mute ? 0 : 1}" title="${e.mute ? "Unmute" : "Mute"}">${e.mute ? off : on}</button>
      <input type="range" min="0" max="100" value="${pct}" style="--p:${pct}%" aria-label="Volume">
      <span class="pct">${e.mute ? "Muted" : pct + "%"}</span></div>`;
}

function setVolume(row, pct) {
  pct = Math.max(0, Math.min(100, Math.round(pct)));
  const input = row.querySelector("input");
  input.value = pct;
  input.style.setProperty("--p", pct + "%");
  row.querySelector(".pct").textContent = pct + "%";
  row.classList.remove("muted");
  clearTimeout(volTimer);                     // ~30 messages a second at most while dragging
  volTimer = setTimeout(() => send({ type: "volume", id: row.dataset.id, v: pct / 100 }), 30);
}

$app.addEventListener("input", (e) => {
  const row = e.target.closest(".vol");
  if (row && e.target.type === "range") setVolume(row, +e.target.value);
});
$app.addEventListener("pointerdown", (e) => { if (e.target.type === "range") dragging = true; });
window.addEventListener("pointerup", () => {
  if (!dragging) return;
  dragging = false;
  if (pendingState) { state = pendingState; pendingState = null; render(); }
});
// mouse wheel over a device's volume row: 2% steps
$app.addEventListener("wheel", (e) => {
  const row = e.target.closest(".vol");
  if (!row) return;
  e.preventDefault();
  setVolume(row, +row.querySelector("input").value + (e.deltaY < 0 ? 2 : -2));
}, { passive: false });

// ------------------------------------------------------------ panel
function sectionHtml(sec, title, list, def) {
  let h = `<h2>${title}</h2>`;
  if (!list.length) return h + `<div class="row empty">${sec === 1 ? "No outputs connected" : "No microphones connected"}</div>`;
  const active = list.find((e) => e.id === def) || list[0];
  // Brand logo in the icon column when the brand is known, otherwise the device-type icon.
  const icon = (e) => logo(audioBrand(e.name)) || (sec === 2 ? I.mic : I[audioKind(e.name)]);
  const open = expanded === sec;
  h += `<div class="dev${open ? " open" : ""}"><div class="row head" data-act="toggle" data-sec="${sec}" data-nav="head-${sec}">
          <span class="ico">${icon(active)}</span><span class="name">${esc(active.name)}</span>${meter(active.id)}<span class="chev">${I.chev}</span></div>
          ${volumeHtml(active, sec)}</div>`;
  if (open)
    for (const e of list)
      if (e.id !== active.id)
        h += `<div class="row item" data-act="select" data-id="${esc(e.id)}" data-sec="${sec}" data-nav="item-${esc(e.id)}"><span class="ico">${icon(e)}</span><span class="name">${esc(e.name)}</span>${meter(e.id)}</div>`;
  return h;
}

const BOLT = '<svg class="bolt" viewBox="0 0 12 16"><title>Charging</title><path d="M7.2 0L.8 9.2h4.3L3.9 16l6.6-9.6H6.1z" fill="currentColor"/></svg>';

// Battery: percentage + 10 vertical pills. Charging: a bolt, and the next pill pulses.
function batteryHtml(p) {
  if (p.conn === "USB") return `<span class="wired">USB</span>`;
  if (p.battery < 0) return `<span class="battery" style="color:var(--dim)">${p.charging ? BOLT : ""}—</span>`;
  const color = p.battery <= 20 ? "var(--red)" : p.battery <= 45 ? "var(--amber)" : "var(--green)";
  const lit = Math.max(1, Math.ceil(p.battery / 10));
  let pills = "";
  for (let i = 0; i < 10; i++) pills += `<i class="${i < lit ? "lit" : p.charging && i === lit ? "next" : ""}"></i>`;
  return `<span class="battery${p.charging ? " charging" : ""}" style="--bat:${color};color:${color}" title="${p.charging ? "Charging" : "On battery"}">${p.charging ? BOLT : ""}${p.battery}%<span class="pills">${pills}</span></span>`;
}

// A device as a circle: its icon, and a ring that fills with the battery (red when low). No
// battery known: a faint ring; a USB receiver: a dashed one. The name shows on hover.
function ringHtml(d) {
  const r = 19, c = 2 * Math.PI * r;
  const has = d.conn !== "USB" && d.battery >= 0;
  const color = !has ? "var(--dim)" : d.battery <= 20 ? "var(--red)" : d.battery <= 45 ? "var(--amber)" : "var(--green)";
  const arc = has ? `<circle class="ring-fill" cx="22" cy="22" r="${r}" stroke="${color}" stroke-dasharray="${(c * d.battery / 100).toFixed(1)} ${c.toFixed(1)}"/>` : "";
  const label = d.conn === "USB" ? "USB" : has ? `${d.battery}%` : "";
  const tip = `${d.name}${has ? " · " + d.battery + "%" : d.conn === "USB" ? " · USB" : ""}${d.charging ? " · charging" : ""}`;
  return `<div class="ring-dev" title="${esc(tip)}">
      <span class="ring">
        <svg viewBox="0 0 44 44"><circle class="ring-track${d.conn === "USB" ? " usb" : ""}" cx="22" cy="22" r="${r}"/>${arc}</svg>
        <span class="ring-ico">${I[d.glyph] || (d.conn === "USB" ? I.usb : I.bt)}</span>
      </span>
      <span class="ring-lbl" style="color:${has ? color : "var(--dim)"}">${d.charging ? BOLT : ""}${label}</span>
    </div>`;
}

function panelHtml() {
  let h = `<div class="view panel"><header><span class="mark">${I.gear}</span><h1>Xdeck</h1><button class="icon-btn close" data-act="close" data-nav="close" title="Close">${I.close}</button></header>`;
  h += sectionHtml(1, "Audio output", state.outs, state.defOut);
  h += sectionHtml(2, "Microphone", state.mics, state.defMic);
  h += `<h2>Controllers</h2>`;
  if (!state.pads.length) h += `<div class="none">No controllers connected</div>`;
  for (const p of state.pads)
    h += `<div class="card" data-act="test" data-key="${esc(p.key)}" data-nav="card-${esc(p.key)}">
            <span class="disc">${PAD_BRAND[p.kind] ? logo(PAD_BRAND[p.kind], "big") : padIcon(p.kind)}</span>
            <span class="who"><div class="nm">${esc(p.name)}</div></span>
            ${p.mouse ? `<span class="wired mouse-chip">Mouse</span>` : ""}${batteryHtml(p)}</div>`;
  // mice, keyboards and other wireless devices: a circle each under the controllers, the icon
  // inside and a ring around it that fills with the battery
  const devs = state.devices || [];
  if (devs.length) {
    h += `<div class="ring-row">`;
    for (const d of devs) h += ringHtml(d);
    h += `</div>`;
  }
  h += `<div id="updates">${updatesHtml()}</div>`;
  if (padMode)
    h += `<div class="pad-help"><span><b>A ✕</b> select</span><span><b>B ◯</b> back</span><span><b>X ▢</b> mute</span><span><b>◀ ▶</b> volume · device</span></div>
          ${panelCombo ? `<div class="hint-line pad-combo">${esc(panelCombo)} hides the panel</div>` : ""}`;
  return h + `</div>`;
}

// ------------------------------------------------------------ updates
// The bottom of the panel: this version, what the updater is doing (a progress bar while it
// downloads), and the one button that fits: check now, restart to update, or try again.
function ago(ms) {
  const s = Math.max(0, Math.round((Date.now() - ms) / 1000));
  if (s < 60) return "just now";
  const m = Math.round(s / 60);
  if (m < 60) return `${m} min ago`;
  const hr = Math.round(m / 60);
  return hr < 24 ? `${hr} h ago` : `${Math.round(hr / 24)} d ago`;
}

function updatesHtml() {
  const u = state.upd;
  if (!u) return "";
  let status, button = "", bar = "";
  const v = esc(u.version || "");
  switch (u.state) {
    case "checking": status = "Checking for updates…"; bar = `<div class="upd-bar busy"><b></b></div>`; break;
    case "downloading":
      status = u.progress >= 0 ? `Downloading ${v}… ${u.progress}%` : `Downloading ${v}…`;
      bar = u.progress >= 0 ? `<div class="upd-bar"><b style="width:${u.progress}%"></b></div>` : `<div class="upd-bar busy"><b></b></div>`;
      break;
    case "ready":
      status = u.error ? `${v} is ready · ${esc(u.error)}` : `Xdeck ${v} is ready to install`;
      button = `<button class="upd-btn go" data-act="update" data-nav="update">Restart to update</button>`;
      break;
    case "installing": status = `Installing ${v}… Xdeck restarts in a moment`; bar = `<div class="upd-bar busy"><b></b></div>`; break;
    case "failed":
      status = `Couldn't check for updates${u.error ? ": " + esc(u.error) : ""}`;
      button = `<button class="upd-btn" data-act="check" data-nav="update">Try again</button>`;
      break;
    case "uptodate":
      status = `Up to date${u.checked ? " · checked " + ago(u.checked) : ""}`;
      button = `<button class="upd-btn" data-act="check" data-nav="update">Check now</button>`;
      break;
    default:
      status = "Checks for updates on its own";
      button = `<button class="upd-btn" data-act="check" data-nav="update">Check now</button>`;
  }
  return `<h2>Updates</h2>
    <div class="card upd${u.state === "ready" ? " ready" : ""}">
      <span class="disc">${I.update}</span>
      <span class="who"><div class="nm">Xdeck ${esc(u.current)}</div><div class="sub" title="${status}">${status}</div>${bar}</span>
      ${button}</div>`;
}

// Live while it downloads: redraw only the updates area, keeping the controller focus.
function updateUpdates() {
  const el = document.getElementById("updates");
  if (!el || view !== "panel") return;
  el.innerHTML = updatesHtml();
  applyFocus();
  postSize();
}

// ------------------------------------------------------------ tester
const SHOULDERS = { dualsense: ["L1", "R1", "L2", "R2"], switchpro: ["L", "R", "ZL", "ZR"], joycon: ["L", "R", "ZL", "ZR"] };

function testerHtml(p) {
  let h = `<div class="view tester k-${esc(p.kind)}"><header>
      <button class="icon-btn back" data-act="back" title="Back">${I.back}</button>
      ${logo(PAD_BRAND[p.kind], "head")}<h1 style="flex:none">${esc(p.name)}</h1><span class="chip wait" id="live">Press any button</span><span style="flex:1"></span>
      <span id="tbat">${p.conn === "USB" ? "" : batteryHtml(p)}</span>
      <button class="icon-btn close" data-act="close" title="Close">${I.close}</button></header>`;
  const sh = SHOULDERS[p.kind];
  if (sh && p.kind !== "dualsense")
    h += `<div class="shoulders">
        <div class="side"><span class="trig" data-trig="6"><b></b><span>${sh[2]}</span></span><span class="sh" data-btn="4">${sh[0]}</span></div>
        <div class="side"><span class="sh" data-btn="5">${sh[1]}</span><span class="trig" data-trig="7"><b></b><span>${sh[3]}</span></span></div></div>`;
  h += `<div class="stage" id="stage">${stageHtml(p)}</div>`;
  if (p.canMouse) h += `<div id="tmouse">${mouseSwitchHtml(p)}</div>`;
  if (["xbox", "dualsense", "switchpro", "joycon"].includes(p.kind))
    h += `<div class="hint-line">Audio output: hold D-pad right + ${p.kind === "xbox" ? "LB" : (SHOULDERS[p.kind] || ["L1"])[0]} and flick the right stick</div>`;
  h += `<div class="hint-line">Back to the panel: hold ${p.kind === "dualsense" ? "◯" : p.kind === "switchpro" || p.kind === "joycon" ? "A" : "B"} on any controller</div>`;
  return h + `</div>`;
}

// This controller as a mouse. The mouse waits while the panel is open (the controller drives the
// panel then), so it starts working once the panel is closed.
function mouseSwitchHtml(p) {
  const key = state.mouseKey ? `${esc(state.mouseKey)} on this controller` : "Turn it on here";
  return `<div class="card switch-row" data-act="mouse" data-key="${esc(p.key)}" role="switch" aria-checked="${p.mouse ? "true" : "false"}">
      <span class="disc">${I.cursor}</span>
      <span class="who"><div class="nm">Use as mouse</div><div class="sub">${p.mouse ? "On · works once the panel is closed" : key}</div></span>
      <span class="switch${p.mouse ? " on" : ""}"><b></b></span></div>`;
}

function stageHtml(p) {
  switch (p.kind) {
    case "xbox":
      return `<div class="xb-wrap"><div class="xb">
        <span class="trigger left" data-trig="6"></span><span class="trigger right" data-trig="7"></span>
        <span class="bumper left" data-btn="4"></span><span class="bumper right" data-btn="5"></span>
        <span class="home" data-btn="16"></span>
        <span class="select" data-btn="8"></span><span class="start" data-btn="9"></span>
        <span class="button a" data-btn="0"></span><span class="button b" data-btn="1"></span>
        <span class="button x" data-btn="2"></span><span class="button y" data-btn="3"></span>
        <span class="stick left" data-btn="10" data-stick="0"></span><span class="stick right" data-btn="11" data-stick="2"></span>
        <span class="face up" data-btn="12"></span><span class="face down" data-btn="13"></span>
        <span class="face left" data-btn="14"></span><span class="face right" data-btn="15"></span>
      </div></div>`;
    case "dualsense": return `<div class="art ds" data-svg="pads/dualsense.svg"></div>`;
    case "switchpro": return `<div class="art sil" data-svg="pads/pro.svg"></div>`;
    case "joycon": return `<div class="art sil" data-svg="pads/joycon.svg"></div>`;
    case "g29":
      return g29Html();
    case "handbrake":
      return `<div class="rig">
        <svg class="hb" viewBox="-40 -10 300 290">
          <rect x="40" y="226" width="140" height="34" rx="10" fill="#34343a"/>
          <rect x="74" y="196" width="72" height="44" rx="12" fill="#44444b"/>
          <g id="hb-lever">
            <rect x="99" y="38" width="22" height="176" rx="11" fill="#7c7c86"/>
            <rect x="91" y="26" width="38" height="96" rx="17" fill="#55555d"/>
            <rect x="97" y="34" width="26" height="80" rx="12" fill="#62626b"/>
            <circle cx="110" cy="26" r="15" fill="var(--accent)"/>
          </g>
          <circle cx="110" cy="214" r="12" fill="#2a2a2f" stroke="#7c7c86" stroke-width="4"/>
        </svg>
        <div class="bars lever" id="axes"></div></div>`;
    default:
      return `<div class="rig"><div class="bars" id="axes"></div><div class="btn-grid" id="btns"></div></div>`;
  }
}

// Element ids per artwork, in standard Gamepad button order (0..17), and the stick element.
const ART = {
  dualsense: {
    btn: { 0: "cross", 1: "circle", 2: "rect", 3: "triangle", 4: "l1", 5: "r1", 6: "l2", 7: "r2", 8: "create", 9: "options",
           10: "l3", 11: "r3", 12: "dpad-up", 13: "dpad-down", 14: "dpad-right", 15: "dpad-left", 16: "ps", 17: "touchpad" },
    sticks: [["l3group", 0], ["r3group", 2]], travel: 26,
  },
  switchpro: {
    btn: { 0: "circle7838-3-6", 1: "circle7838-3-9", 2: "circle7838-3", 3: "circle7910", 8: "path7756", 9: "circle7838",
           10: "path7483-4-9", 11: "path7483-4", 12: "rect6544", 13: "rect6544", 14: "rect6544", 15: "rect6544", 16: "circle7838-4", 17: "rect7609" },
    sticks: [["path7483-4-9", 0], ["path7483-4", 2]], travel: 2.6,
  },
  joycon: {
    btn: { 0: "circle2209", 1: "circle2207", 2: "circle2211", 3: "path2168-9", 8: "rect3315", 9: "rect2587",
           10: "circle2205", 11: "path2168-0", 12: "circle2525", 13: "circle2529", 14: "circle2531", 15: "circle2527", 16: "path2168", 17: "rect7609" },
    sticks: [["circle2205", 0], ["path2168-0", 2]], travel: 1.2,
  },
};

async function loadArt() {
  const box = document.querySelector("[data-svg]");
  if (!box) return;
  const url = box.getAttribute("data-svg");
  if (!svgCache[url]) svgCache[url] = await (await fetch(url)).text();
  box.innerHTML = svgCache[url];
  const svg = box.querySelector("svg");
  svg.removeAttribute("width");
  svg.removeAttribute("height");
  postSize();
}

// ------------------------------------------------------------ gamepad matching
function classify(gp) {
  const m = /Vendor:\s*([0-9a-f]{4})\s*Product:\s*([0-9a-f]{4})/i.exec(gp.id) || [];
  const v = (m[1] || "").toLowerCase(), pr = (m[2] || "").toLowerCase();
  if (v === "045e" || /xbox|xinput/i.test(gp.id)) return "xbox";
  if (v === "054c") return "dualsense";
  if (v === "057e" && pr === "2009") return "switchpro";
  if (v === "057e") return "joycon";
  if (v === "046d" && pr === "c24f") return "g29";
  if (v === "7864") return "handbrake";
  return "generic";
}

function findGamepad(p) {
  const same = [...navigator.getGamepads()].filter((g) => g && g.connected && classify(g) === p.kind);
  return same[p.index] || same[0] || null;
}

let raf = 0, lastStamp = 0, lastChange = 0;
let axisRest = {}, axisSeen = new Set();   // per tester session

function loop() {
  raf = requestAnimationFrame(loop);
  if (view !== "tester" || !testing) return;
  const gp = findGamepad(testing);
  const live = document.getElementById("live");
  if (gp && gp.timestamp !== lastStamp) { lastStamp = gp.timestamp; lastChange = performance.now(); }
  const isLive = gp && performance.now() - lastChange < 4000;
  if (live) { live.className = "chip " + (isLive ? "live" : "wait"); live.textContent = isLive ? "Live" : gp ? "Idle" : "Press any button"; }
  if (!gp) return;
  const pressed = (i) => !!(gp.buttons[i] && (gp.buttons[i].pressed || gp.buttons[i].value > 0.5));
  const value = (i) => (gp.buttons[i] ? gp.buttons[i].value : 0);

  // generic attribute hooks (xbox artwork + shoulder strip)
  document.querySelectorAll("[data-btn]").forEach((el) => el.classList.toggle("on", pressed(+el.dataset.btn)));
  document.querySelectorAll("[data-trig]").forEach((el) => {
    const v = value(+el.dataset.trig);
    if (el.classList.contains("trigger")) { el.style.opacity = v > 0.01 ? 1 : 0; el.style.clipPath = `inset(${(1 - v) * 100}% 0 0 0)`; }
    else { const b = el.querySelector("b"); if (b) b.style.width = v * 100 + "%"; }
  });
  document.querySelectorAll("[data-stick]").forEach((el) => {
    const a = +el.dataset.stick, x = gp.axes[a] || 0, y = gp.axes[a + 1] || 0;
    el.style.transform = `translate(${Math.round(x * 25)}px, ${Math.round(y * 25)}px)`;
  });

  // SVG artwork with element ids
  const art = ART[testing.kind];
  const svg = document.querySelector(".art svg");
  if (art && svg) {
    const on = new Set();
    for (const [i, id] of Object.entries(art.btn)) {
      const idx = +i;
      if (idx === 6 || idx === 7 ? value(idx) > 0.05 : pressed(idx)) on.add(id);
    }
    svg.querySelectorAll(".on").forEach((el) => { if (!on.has(el.id)) el.classList.remove("on"); });
    on.forEach((id) => { const el = svg.getElementById(id); if (el) el.classList.add("on"); });
    for (const [id, a] of art.sticks) {
      const el = svg.getElementById(id);
      if (el) el.setAttribute("transform", `translate(${(gp.axes[a] || 0) * art.travel} ${(gp.axes[a + 1] || 0) * art.travel})`);
    }
  }

  // wheel, pedals, handbrake, generic
  if (testing.kind === "g29") updateG29(gp, pressed);
  const wheel = null;
  // Pedals / levers: only axes that are actually wired (unused ones sit at exactly 0).
  // Each bar fills from its own resting value, so +1-at-rest and -1-at-rest both read 0%.
  const pedals = document.getElementById("pedals") || document.getElementById("axes");
  if (pedals) {
    const first = wheel ? 1 : 0;
    for (let i = first; i < gp.axes.length; i++) {
      if (Math.abs(gp.axes[i]) > 0.001) axisSeen.add(i);
    }
    const shown = [...axisSeen].sort((a, b) => a - b);
    const key = shown.join(",");
    if (pedals.dataset.axes !== key) {
      pedals.dataset.axes = key;
      pedals.innerHTML = shown.map((i) => `<div data-axis="${i}"><div class="bar"><b></b></div><div class="bar-label">${testing.kind === "handbrake" && shown.length === 1 ? "Handbrake" : "Axis " + (i + 1)}</div><div class="bar-label pct">0%</div></div>`).join("");
    }
    pedals.querySelectorAll("[data-axis]").forEach((el) => {
      const i = +el.dataset.axis, v = gp.axes[i] || 0, rest = restEnd(i, v, 0);
      const span = rest ? 2 : 1;                           // rest at an end: full travel is 2
      const f = Math.max(0, Math.min(1, Math.abs(v - rest) / span));
      el.querySelector("b").style.height = f * 100 + "%";
      el.querySelector(".pct").textContent = Math.round(f * 100) + "%";
      const lever = document.getElementById("hb-lever");
      if (lever) lever.setAttribute("transform", `rotate(${-f * 42} 110 214)`);   // pulled = tilted back
    });
  }
  const grid = document.getElementById("btns");
  if (grid) {
    if (grid.childElementCount !== gp.buttons.length) grid.innerHTML = gp.buttons.map((_, i) => `<i>${i + 1}</i>`).join("");
    [...grid.children].forEach((el, i) => el.classList.toggle("on", pressed(i)));
  }
}

// ------------------------------------------------------------ G29 racing wheel
// Drawn in the spirit of the real wheel (black rim, blue top marker, silver spokes, PlayStation
// buttons, red dial, paddles) with the 900° range gauge on top. The wheel group turns with the
// real wheel; buttons light up; the three pedals fill as they are pressed.
// Chromium's G29 layout: axis 0 wheel, 1 throttle, 2 brake, 5 clutch (all +1 released), 9 hat.
const G29_BTN = { 0: "g-cross", 1: "g-square", 2: "g-circle", 3: "g-triangle", 4: "g-paddle-r", 5: "g-paddle-l",
  6: "g-r2", 7: "g-l2", 8: "g-share", 9: "g-options", 10: "g-r3", 11: "g-l3", 19: "g-plus", 20: "g-minus",
  21: "g-dial", 22: "g-dial", 23: "g-enter", 24: "g-ps" };
const G29_PEDALS = [["Clutch", 5], ["Brake", 2], ["Throttle", 1]];

function g29Html() {
  const cx = 230, cy = 262, R = 206;
  let ticks = "";
  for (let i = -30; i <= 30; i++) {                         // the 900° scale: one tick per 15°
    const a = (i * 2 * Math.PI) / 180 - Math.PI / 2, long = i % 5 === 0;
    const r1 = R - (long ? 16 : 10);
    ticks += `<line x1="${(cx + Math.cos(a) * r1).toFixed(1)}" y1="${(cy + Math.sin(a) * r1).toFixed(1)}" x2="${(cx + Math.cos(a) * R).toFixed(1)}" y2="${(cy + Math.sin(a) * R).toFixed(1)}"/>`;
  }
  const btn = (id, x, y, sym) => `<g id="${id}" class="g-btn"><circle cx="${x}" cy="${y}" r="9"/>${sym}</g>`;
  const pedals = G29_PEDALS.map(([name], i) => `
      <g transform="translate(${i * 62},0)">
        <rect class="pedal-shell" x="0" y="0" width="44" height="120" rx="10"/>
        <clipPath id="pclip${i}"><rect x="0" y="0" width="44" height="120" rx="10"/></clipPath>
        <rect class="pedal-fill" id="pedal${i}" x="0" y="120" width="44" height="0" clip-path="url(#pclip${i})"/>
        ${[24, 44, 64, 84, 104].map((y) => `<line class="pedal-groove" x1="9" y1="${y}" x2="35" y2="${y}"/>`).join("")}
        <text class="pedal-name" x="22" y="140">${name}</text>
        <text class="pedal-pct" id="pedalpct${i}" x="22" y="158">0%</text>
      </g>`).join("");
  return `<div class="g29">
    <svg class="g29-wheel" viewBox="0 30 460 380">
      <defs>
        <linearGradient id="spoke" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#b9b9c0"/><stop offset=".55" stop-color="#7d7d85"/><stop offset="1" stop-color="#55555c"/></linearGradient>
        <radialGradient id="dial" cx=".4" cy=".35" r=".7"><stop offset="0" stop-color="#ff6b5f"/><stop offset="1" stop-color="#b32a24"/></radialGradient>
      </defs>
      <g class="g29-scale">${ticks}</g>
      <text class="g29-range" x="${cx}" y="${cy - R + 46}">900° OPERATING RANGE</text>
      <path id="g29-pointer" class="g29-pointer" d="M${cx - 7},${cy - R - 16} h14 l-7,10z"/>
      <g id="g29-wheel">
        <path id="g-paddle-l" class="g-paddle" d="M126,218 q-20,30 -10,64 l18,-4 q-6,-28 8,-54z"/>
        <path id="g-paddle-r" class="g-paddle" d="M334,218 q20,30 10,64 l-18,-4 q6,-28 -8,-54z"/>
        <!-- silver frame: side wings to the rim, two legs and a centre spoke down to it (ends hide under the rim) -->
        <path class="g29-spokes" d="M98,242 Q138,204 186,198 L274,198 Q322,204 362,242 L362,282 Q344,300 322,306 L300,352 L290,376 L262,376 L256,340 L204,340 L198,376 L170,376 L160,352 L138,306 Q116,300 98,282 Z"/>
        <path class="g29-brush" d="M104,248 Q140,214 186,208 M274,208 Q320,214 356,248"/>
        <circle class="g29-rim" cx="${cx}" cy="${cy}" r="124"/>
        <circle class="g29-stitch" cx="${cx}" cy="${cy}" r="110"/>
        <rect class="g29-marker" x="${cx - 7}" y="${cy - 141}" width="14" height="32" rx="2"/>
        <!-- black parts on the silver: the hub console and recessed panels for each button cluster -->
        <path class="g29-console" d="M196,220 L264,220 Q284,222 288,244 L292,288 L276,312 L184,312 L168,288 L172,244 Q176,222 196,220 Z"/>
        <rect class="g29-console" x="138" y="226" width="44" height="70" rx="11"/>
        <rect class="g29-console" x="278" y="226" width="44" height="70" rx="11"/>
        <rect class="g29-console" x="172" y="288" width="32" height="50" rx="10"/>
        <circle class="g29-console" cx="272" cy="316" r="22"/>
        <g id="g-dpad">
          <path id="g-up" class="g-arm" d="M154,233 h12 v12 h-12z"/><path id="g-down" class="g-arm" d="M154,257 h12 v12 h-12z"/>
          <path id="g-left" class="g-arm" d="M142,245 h12 v12 h-12z"/><path id="g-right" class="g-arm" d="M166,245 h12 v12 h-12z"/>
          <rect class="g-arm" x="154" y="245" width="12" height="12"/>
        </g>
        <rect id="g-l3" class="g-blue" x="176" y="270" width="16" height="10" rx="3"/>
        <rect id="g-l2" class="g-blue" x="151" y="278" width="18" height="10" rx="3"/>
        <rect id="g-r3" class="g-blue" x="268" y="270" width="16" height="10" rx="3"/>
        <rect id="g-r2" class="g-blue" x="291" y="278" width="18" height="10" rx="3"/>
        <rect id="g-share" class="g-small" x="194" y="228" width="16" height="8" rx="4"/>
        <rect id="g-options" class="g-small" x="250" y="228" width="16" height="8" rx="4"/>
        ${btn("g-triangle", 300, 236, `<path class="g-sym" d="M300,231.5 l4.5,8 h-9z"/>`)}
        ${btn("g-circle", 314, 250, `<circle class="g-sym" cx="314" cy="250" r="4.2"/>`)}
        ${btn("g-square", 286, 250, `<rect class="g-sym" x="282" y="246" width="8" height="8"/>`)}
        ${btn("g-cross", 300, 264, `<path class="g-sym" d="M296,260 l8,8 m0,-8 l-8,8"/>`)}
        <g id="g-plus" class="g-rocker"><rect x="178" y="294" width="20" height="18" rx="6"/><path class="g-sym" d="M188,298 v10 m-5,-5 h10"/></g>
        <g id="g-minus" class="g-rocker"><rect x="178" y="314" width="20" height="18" rx="6"/><path class="g-sym" d="M183,323 h10"/></g>
        <circle id="g-dial" class="g-dial" cx="272" cy="316" r="17"/>
        <g id="g-enter" class="g-enter"><circle cx="272" cy="316" r="9"/><path class="g-sym" d="M276,312 v5 h-8 m3,-3 l-3,3 l3,3"/></g>
        <circle id="g-ps" class="g-hub" cx="${cx}" cy="${cy}" r="40"/>
        ${[0, 1, 2, 3, 4, 5, 6, 7].map((k) => { const a = (k * Math.PI) / 4 + Math.PI / 8; return `<circle class="g-screw" cx="${(cx + Math.cos(a) * 32).toFixed(1)}" cy="${(cy + Math.sin(a) * 32).toFixed(1)}" r="3.5"/>`; }).join("")}
        <image href="brands/playstation.svg" x="${cx - 15}" y="${cy - 15}" width="30" height="30" class="g-pslogo"/>
      </g>
    </svg>
    <div class="g29-side">
      <div class="g29-angle"><b id="g29-deg">0°</b><span>wheel angle</span></div>
      <svg class="g29-pedals" viewBox="-4 -4 176 172">${pedals}</svg>
    </div>
  </div>`;
}

// The resting end (+1 or -1) of a pedal/lever axis, learnt from the first reading at an end.
function restEnd(axis, v, fallback) {
  if (!(axis in axisRest) && Math.abs(v) > 0.9) axisRest[axis] = v > 0 ? 1 : -1;
  return axis in axisRest ? axisRest[axis] : fallback;
}

function updateG29(gp, pressed) {
  const deg = (gp.axes[0] || 0) * 450;                       // 900° lock to lock
  const w = document.getElementById("g29-wheel");
  if (!w) return;
  w.setAttribute("transform", `rotate(${deg.toFixed(1)} 230 262)`);
  document.getElementById("g29-pointer").setAttribute("transform", `rotate(${(deg / 450 * 60).toFixed(1)} 230 262)`);
  document.getElementById("g29-deg").textContent = Math.round(deg) + "°";
  const on = new Set();
  for (const [i, id] of Object.entries(G29_BTN)) if (pressed(+i)) on.add(id);
  const hat = gp.axes[9];                                   // -1 up … 1 up-left; released reads ~1.29 or 0
  if (hat !== undefined && hat <= 1.01 && Math.abs(hat) > 0.05) {
    const dir = Math.round((hat + 1) * 3.5) % 8;            // 0 up, 1 up-right, 2 right … 7 up-left
    if ([7, 0, 1].includes(dir)) on.add("g-up");
    if ([1, 2, 3].includes(dir)) on.add("g-right");
    if ([3, 4, 5].includes(dir)) on.add("g-down");
    if ([5, 6, 7].includes(dir)) on.add("g-left");
  }
  document.querySelectorAll(".g29 [id^='g-']").forEach((el) => el.classList.toggle("on", on.has(el.id)));
  // Pedals: released is an end of the axis (+1 on the G29). The first readings can be 0 before the
  // device reports, so the resting end is learnt from the first reading that is at an end.
  G29_PEDALS.forEach(([, axis], i) => {
    const v = gp.axes[axis];
    const f = v === undefined ? 0 : Math.max(0, Math.min(1, Math.abs(v - restEnd(axis, v, 1)) / 2));
    const fill = document.getElementById("pedal" + i);
    fill.setAttribute("y", 120 - f * 120);
    fill.setAttribute("height", f * 120);
    document.getElementById("pedalpct" + i).textContent = Math.round(f * 100) + "%";
  });
}

// ------------------------------------------------------------ render + events
function render() {
  document.documentElement.style.setProperty("--accent", state.accent || "#0078d4");
  if (view === "tester" && testing) {
    const p = state.pads.find((x) => x.key === testing.key);
    if (!p) { view = "panel"; testing = null; }
    else if (!document.querySelector(".tester")) { testing = p; $app.innerHTML = testerHtml(p); loadArt(); }
    else {                                       // tester stays mounted; only refresh its battery and mouse switch
      testing = p;
      const tb = document.getElementById("tbat");
      if (tb) tb.innerHTML = p.conn === "USB" ? "" : batteryHtml(p);
      const tm = document.getElementById("tmouse");
      if (tm) tm.innerHTML = mouseSwitchHtml(p);
      return postSize();
    }
  }
  if (view === "panel") $app.innerHTML = panelHtml();
  applyFocus();
  updateMeters();
  postSize();
}

// ------------------------------------------------------------ controller navigation
// The host turns any controller's D-pad / left stick and face buttons into {type:"pad"} messages
// while the panel is open. Everything the mouse can do here, the controller can do too.
const navItems = () => [...document.querySelectorAll(".panel [data-nav]")];

function applyFocus() {
  document.body.classList.toggle("pad-mode", padMode);
  document.querySelectorAll(".nav-focus").forEach((e) => e.classList.remove("nav-focus"));
  if (!padMode || view !== "panel") return;
  const items = navItems();
  if (!items.length) return;
  let el = items.find((e) => e.dataset.nav === focusKey);
  if (!el) { el = items.find((e) => e.dataset.nav !== "close") || items[0]; focusKey = el.dataset.nav; }
  el.classList.add("nav-focus");
  el.scrollIntoView({ block: "nearest" });
}

function focusAt(el) { if (el) { focusKey = el.dataset.nav; applyFocus(); } }

function openTester(key) {
  testing = state.pads.find((p) => p.key === key);
  if (testing) { view = "tester"; lastStamp = 0; lastChange = 0; axisRest = {}; axisSeen = new Set(); render(); }
}

function backToPanel() { view = "panel"; testing = null; render(); }

// The active device of a section, its list, and the next / previous one.
function stepDevice(sec, dir) {
  const list = sec === 1 ? state.outs : state.mics, def = sec === 1 ? state.defOut : state.defMic;
  if (list.length < 2) return;
  const i = Math.max(0, list.findIndex((e) => e.id === def));
  send({ type: "select", id: list[(i + dir + list.length) % list.length].id });
}

function onPad(btn) {
  if (view === "tester") {                       // every button is being tested: only a long B leaves
    if (btn === "bhold") backToPanel();
    return;
  }
  if (btn === "bhold") return;
  if (btn === "start") return send({ type: "close" });
  if (!padMode && btn !== "b") {                 // opened with the mouse: the first press shows the focus
    padMode = true;
    return render();
  }
  const items = navItems();
  const i = items.findIndex((e) => e.dataset.nav === focusKey);
  const el = items[i];
  const kind = el ? el.dataset.nav.split("-")[0] : "";
  switch (btn) {
    case "up": case "down":
      focusAt(items[Math.max(0, Math.min(items.length - 1, (i < 0 ? 0 : i) + (btn === "up" ? -1 : 1)))]);
      break;
    case "left": case "right": {
      const d = btn === "right" ? 1 : -1;
      if (kind === "vol") setVolume(el, +el.querySelector("input").value + d * 5);
      else if (kind === "head") stepDevice(+el.dataset.sec, d);
      break;
    }
    case "a":
      if (!el) break;
      if (kind === "vol") { const m = el.querySelector("[data-act=mute]"); if (m) m.click(); }
      else if (kind === "head") {
        const sec = +el.dataset.sec;
        expanded = expanded === sec ? 0 : sec;
        render();
        const first = expanded && document.querySelector(`.row.item[data-sec="${sec}"]`);
        if (first) focusAt(first);
      }
      else if (kind === "item") { const sec = +el.dataset.sec; el.click(); focusKey = "head-" + sec; applyFocus(); }
      else el.click();                           // close button, controller card
      break;
    case "x": {
      const dev = el && el.closest(".dev");
      const m = dev && dev.querySelector("[data-act=mute]");
      if (m) m.click();
      break;
    }
    case "b":
      if (expanded) { focusKey = "head-" + expanded; expanded = 0; render(); }
      else send({ type: "close" });
      break;
  }
}

// Moving the real mouse hands the panel back to it (the focus ring goes away).
let lastMouse = "";
window.addEventListener("pointermove", (e) => {
  const at = e.screenX + "," + e.screenY;
  if (at === lastMouse) return;                  // layout changes fire moves without a real move
  const first = !lastMouse;
  lastMouse = at;
  if (first || !padMode || e.pointerType !== "mouse") return;
  padMode = false;
  render();
});

function updateMeters() {
  document.querySelectorAll(".meter").forEach((m) => {
    const lit = Math.round((levels[m.dataset.id] || 0) * 12);
    [...m.children].forEach((pill, i) => pill.classList.toggle("on", i < lit));
  });
}

let lastSize = "";
function postSize() {
  requestAnimationFrame(() => {
    const r = $app.getBoundingClientRect();
    const s = Math.ceil(r.width) + "x" + Math.ceil(r.height);
    if (s !== lastSize) { lastSize = s; send({ type: "size", w: Math.ceil(r.width), h: Math.ceil(r.height) }); }
  });
}

$app.addEventListener("click", (e) => {
  const t = e.target.closest("[data-act]");
  if (!t) return;
  const act = t.dataset.act;
  if (act === "close") { send({ type: "close" }); }
  else if (act === "mute") { send({ type: "mute", id: t.dataset.id, m: t.dataset.m === "1" }); }
  else if (act === "toggle") { expanded = expanded === +t.dataset.sec ? 0 : +t.dataset.sec; render(); }
  else if (act === "select") { send({ type: "select", id: t.dataset.id }); expanded = 0; render(); }
  else if (act === "test") openTester(t.dataset.key);
  else if (act === "back") backToPanel();
  else if (act === "update") send({ type: "update" });
  else if (act === "check") { send({ type: "check" }); state.upd = { ...state.upd, state: "checking", error: null }; updateUpdates(); }
  else if (act === "mouse") {
    const p = state.pads.find((x) => x.key === t.dataset.key);
    if (p) send({ type: "mouse", key: p.key, on: !p.mouse });
  }
});

document.addEventListener("keydown", (e) => {
  if (e.key !== "Escape") return;
  if (view === "tester") backToPanel();
  else send({ type: "close" });
});

if (host) host.addEventListener("message", (e) => {
  const m = e.data;
  if (m.type === "state") { if (dragging) pendingState = m; else { state = m; render(); } }
  else if (m.type === "levels") { levels = m.levels || {}; updateMeters(); }
  else if (m.type === "show") {
    view = "panel"; expanded = 0; testing = null; padMode = !!m.pad; focusKey = null;
    if (typeof m.combo === "string") panelCombo = m.combo;
    render();
  }
  else if (m.type === "shortcuts") { panelCombo = m.combo || ""; if (view === "panel") render(); }
  else if (m.type === "update") { state.upd = m.u; updateUpdates(); }
  else if (m.type === "pad") onPad(m.btn);
});

// Design preview without the app: index.html#preview=g29 (or xbox, dualsense, …) opens that tester.
const preview = /^#preview=(\w+)/.exec(location.hash);
if (preview) {
  const kind = preview[1];
  state.pads = [{ key: "preview", kind, name: { g29: "G29 Racing Wheel", handbrake: "Handbrake" }[kind] || kind, index: 0, battery: 64, charging: false, conn: kind === "g29" || kind === "handbrake" ? "USB" : "Bluetooth" }];
  testing = state.pads[0];
  view = "tester";
}

render();
raf = requestAnimationFrame(loop);
send({ type: "ready" });

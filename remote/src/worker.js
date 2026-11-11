// Xdeck phone remote relay, at https://xdeck.skale.club
//
// One Room (Durable Object) per PC. Xdeck.exe keeps a WebSocket open to /pc?id=<pcId>&secret=<s>;
// phones open /phone?pc=<pcId>&phone=<phoneId>&token=<t>. The room passes messages between them.
// It never sees what they say: phone and PC encrypt end to end (ECDH P-256 keys exchanged at
// pairing, then AES-CBC + HMAC-SHA256), so a message here is an opaque base64 string.
//
// Pairing starts on the phone: "Find my PC" posts to /discover, and every Xdeck PC seen on the
// same network (the same public IPv4 address, or the same IPv6 /64) gets a 6-digit code to show on
// its screen. The phone posts the code it reads there, with its public key, to /pair and gets back
// the PC's id, its own id and token, and the PC's public key. Only someone looking at that PC's
// screen can pair. (The PC can also ask for a code itself: tray menu › Pair a phone.) The room
// keeps only a hash of each token and of the PC secret (the first connection sets it). PCs report
// where they are seen from: their WebSocket, plus /ip over the other IP family.
//
// Wire format
//   PC -> room   {"t":"hello","name","pub"}  {"t":"code"}  {"t":"m","phone","d"}  {"t":"unpair","phone"}
//   room -> PC   {"t":"code","code","exp"}  {"t":"pairreq","code","exp","phone"}  {"t":"paired","phone","name","pub"}
//                {"t":"online","phones":[...]}  {"t":"m","phone","d"}
//   phone -> room  d (the encrypted string)
//   room -> phone  d, or {"t":"pc","online":true|false,"name"}

const CODE_MS = 10 * 60 * 1000;
const OFFER_MS = 3 * 60 * 1000;            // a code shown because a phone asked
const SEEN_MS = 3 * 24 * 3600 * 1000;      // how long a PC counts as "on this network"
const ID = /^[\w-]{16,64}$/;

// The network a request comes from: the IPv4 address, or the /64 of an IPv6 one.
function netKey(ip) {
  ip = String(ip || "");
  if (!ip.includes(":")) return ip;
  const [head, tail] = ip.split("::");
  const a = head ? head.split(":") : [], b = tail !== undefined && tail ? tail.split(":") : [];
  const full = tail !== undefined ? a.concat(new Array(8 - a.length - b.length).fill("0"), b) : a;
  return full.slice(0, 4).map((x) => parseInt(x || "0", 16).toString(16)).join(":") + "::/64";
}
const clientIp = (req) => req.headers.get("cf-connecting-ip") || "127.0.0.1";
const pairing = (env) => env.PAIRING.get(env.PAIRING.idFromName("codes"));

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    if (url.pathname === "/pc" || url.pathname === "/phone" || url.pathname === "/ip") {
      const pc = url.searchParams.get(url.pathname === "/phone" ? "pc" : "id") || "";
      if (!ID.test(pc)) return new Response("bad id", { status: 400 });
      return env.ROOM.get(env.ROOM.idFromName(pc)).fetch(req);
    }
    if (url.pathname === "/pair" || url.pathname === "/discover") {
      if (req.method !== "POST") return new Response("POST only", { status: 405 });
      return pairing(env).fetch(req);
    }
    return env.ASSETS.fetch(req);
  },
};

const json = (obj, status = 200) => new Response(JSON.stringify(obj), { status, headers: { "content-type": "application/json", "cache-control": "no-store" } });

function random(bytes) {
  const b = crypto.getRandomValues(new Uint8Array(bytes));
  return btoa(String.fromCharCode(...b)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

async function sha256(text) {
  const h = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return btoa(String.fromCharCode(...new Uint8Array(h)));
}

function same(a, b) {
  if (typeof a !== "string" || typeof b !== "string" || a.length !== b.length) return false;
  let d = 0;
  for (let i = 0; i < a.length; i++) d |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return d === 0;
}

// ------------------------------------------------------------ pairing codes
export class Pairing {
  constructor(ctx, env) {
    this.ctx = ctx;
    this.env = env;
    this.tries = new Map();   // ip -> [timestamps]: 10 wrong codes per 10 minutes
    this.asks = new Map();    // network -> [timestamps]: 6 "Find my PC" per 10 minutes
  }

  async newCode(codes, pc, ms) {
    const now = Date.now();
    for (const [c, v] of Object.entries(codes)) if (v.exp < now || v.pc === pc) delete codes[c];
    let code;
    do { code = String(crypto.getRandomValues(new Uint32Array(1))[0] % 1000000).padStart(6, "0"); } while (codes[code]);
    codes[code] = { pc, exp: now + ms };
    return code;
  }

  async fetch(req) {
    const url = new URL(req.url);
    const now = Date.now();
    if (url.pathname === "/seen") {       // from a room: this PC is on that network
      const { pc, ip } = await req.json();
      const seen = (await this.ctx.storage.get("seen")) || {};
      const key = netKey(ip);
      seen[key] = seen[key] || {};
      seen[key][pc] = now;
      for (const [k, pcs] of Object.entries(seen)) {
        for (const [p, t] of Object.entries(pcs)) if (now - t > SEEN_MS) delete pcs[p];
        if (!Object.keys(pcs).length) delete seen[k];
      }
      await this.ctx.storage.put("seen", seen);
      return json({ ok: true });
    }
    if (url.pathname === "/discover") {   // from a phone: "Find my PC"
      const key = netKey(clientIp(req));
      const recent = (this.asks.get(key) || []).filter((t) => now - t < CODE_MS);
      if (recent.length >= 6) return json({ error: "Too many tries. Wait a few minutes." }, 429);
      recent.push(now);
      this.asks.set(key, recent);
      let body = {};
      try { body = await req.json(); } catch {}
      const seen = (await this.ctx.storage.get("seen")) || {};
      const pcs = Object.entries(seen[key] || {}).filter(([, t]) => now - t < SEEN_MS).map(([p]) => p);
      const codes = (await this.ctx.storage.get("codes")) || {};
      const found = [];
      for (const pc of pcs) {
        const code = await this.newCode(codes, pc, OFFER_MS);
        const r = await this.env.ROOM.get(this.env.ROOM.idFromName(pc)).fetch("https://room/offer", {
          method: "POST", body: JSON.stringify({ code, exp: now + OFFER_MS, phone: String(body.name || "A phone").slice(0, 40) }),
        });
        const res = await r.json().catch(() => ({}));
        if (res.online) found.push(res.name || "PC"); else delete codes[code];
      }
      await this.ctx.storage.put("codes", codes);
      return json({ pcs: found });
    }
    if (url.pathname === "/register") {   // from a room: a fresh code for this PC
      const { pc } = await req.json();
      const codes = (await this.ctx.storage.get("codes")) || {};
      const code = await this.newCode(codes, pc, CODE_MS);
      await this.ctx.storage.put("codes", codes);
      return json({ code, exp: now + CODE_MS });
    }
    if (url.pathname === "/pair") {       // from a phone
      const ip = req.headers.get("cf-connecting-ip") || "?";
      const recent = (this.tries.get(ip) || []).filter((t) => now - t < CODE_MS);
      if (recent.length >= 10) return json({ error: "Too many tries. Wait a few minutes." }, 429);
      let body;
      try { body = await req.json(); } catch { return json({ error: "bad request" }, 400); }
      const code = String(body.code || "").replace(/\D/g, "");
      const codes = (await this.ctx.storage.get("codes")) || {};
      const hit = codes[code];
      if (!hit || hit.exp < now) {
        recent.push(now);
        this.tries.set(ip, recent);
        return json({ error: "That code is not valid. Check it in the Xdeck panel on the PC." }, 404);
      }
      delete codes[code];
      await this.ctx.storage.put("codes", codes);
      const room = this.env.ROOM.get(this.env.ROOM.idFromName(hit.pc));
      const r = await room.fetch("https://room/pair", { method: "POST", body: JSON.stringify({ pc: hit.pc, pub: body.pub, name: body.name }) });
      return new Response(r.body, { status: r.status, headers: { "content-type": "application/json", "cache-control": "no-store" } });
    }
    return new Response("not found", { status: 404 });
  }
}

// ------------------------------------------------------------ one room per PC
export class Room {
  constructor(ctx, env) {
    this.ctx = ctx;
    this.env = env;
  }

  pc() { return this.ctx.getWebSockets("pc")[0] || null; }

  async checkSecret(url) {
    const secret = url.searchParams.get("secret") || "", known = await this.ctx.storage.get("secret");
    return !!known && secret.length >= 32 && same(known, await sha256(secret));
  }

  // tells Pairing which network this PC is on, so a phone there can find it
  async seen(ip) {
    const id = await this.ctx.storage.get("id");
    if (id) await pairing(this.env).fetch("https://pairing/seen", { method: "POST", body: JSON.stringify({ pc: id, ip }) });
  }
  phones(id) { return this.ctx.getWebSockets(id ? "phone:" + id : "phone"); }

  sendPc(obj) { const p = this.pc(); if (p) try { p.send(JSON.stringify(obj)); } catch {} }

  async onlineList(except) {
    const ids = new Set();
    for (const ws of this.phones()) if (ws !== except) for (const t of this.ctx.getTags(ws)) if (t.startsWith("phone:")) ids.add(t.slice(6));
    return [...ids];
  }

  async tellPhones(online) {
    const name = (await this.ctx.storage.get("name")) || "PC";
    for (const ws of this.phones()) try { ws.send(JSON.stringify({ t: "pc", online, name })); } catch {}
  }

  async fetch(req) {
    const url = new URL(req.url);

    if (url.pathname === "/pair" && req.method === "POST") {   // from Pairing, with a valid code
      const { pc, pub, name } = await req.json();
      if (typeof pub !== "string" || pub.length < 80 || pub.length > 120) return json({ error: "bad key" }, 400);
      const pcPub = await this.ctx.storage.get("pub");
      if (!pcPub || !this.pc()) return json({ error: "The PC went offline. Try again." }, 409);
      const phone = random(12), token = random(32);
      const phones = (await this.ctx.storage.get("phones")) || {};
      const label = String(name || "Phone").slice(0, 40);
      phones[phone] = { hash: await sha256(token), name: label };
      await this.ctx.storage.put("phones", phones);
      this.sendPc({ t: "paired", phone, name: label, pub });
      return json({ pc, phone, token, pcPub, pcName: (await this.ctx.storage.get("name")) || "PC" });
    }

    if (url.pathname === "/offer" && req.method === "POST") {   // from Pairing: a phone on this network asks
      const { code, exp, phone } = await req.json();
      const pc = this.pc();
      if (!pc) return json({ online: false });
      try { pc.send(JSON.stringify({ t: "pairreq", code, exp, phone })); } catch { return json({ online: false }); }
      return json({ online: true, name: (await this.ctx.storage.get("name")) || "PC" });
    }

    if (url.pathname === "/ip") {   // the PC reports itself over its other IP family
      if (!(await this.checkSecret(url))) return new Response("wrong secret", { status: 403 });
      await this.seen(clientIp(req));
      return json({ ok: true });
    }

    if (req.headers.get("upgrade") !== "websocket") return new Response("WebSocket only", { status: 426 });

    if (url.pathname === "/pc") {
      const id = url.searchParams.get("id"), secret = url.searchParams.get("secret") || "";
      if (secret.length < 32) return new Response("bad secret", { status: 403 });
      const known = await this.ctx.storage.get("secret");
      const hash = await sha256(secret);
      if (known && !same(known, hash)) return new Response("wrong secret", { status: 403 });
      if (!known) { await this.ctx.storage.put("secret", hash); await this.ctx.storage.put("id", id); }
      await this.seen(clientIp(req));
      for (const old of this.ctx.getWebSockets("pc")) try { old.close(4000, "replaced"); } catch {}
      const [client, server] = Object.values(new WebSocketPair());
      this.ctx.acceptWebSocket(server, ["pc"]);
      server.send(JSON.stringify({ t: "online", phones: await this.onlineList() }));
      await this.tellPhones(true);
      return new Response(null, { status: 101, webSocket: client });
    }

    if (url.pathname === "/phone") {
      const phone = url.searchParams.get("phone") || "", token = url.searchParams.get("token") || "";
      const phones = (await this.ctx.storage.get("phones")) || {};
      const entry = phones[phone];
      if (!entry || !same(entry.hash, await sha256(token))) return new Response("not paired", { status: 403 });
      const [client, server] = Object.values(new WebSocketPair());
      this.ctx.acceptWebSocket(server, ["phone", "phone:" + phone]);
      server.send(JSON.stringify({ t: "pc", online: !!this.pc(), name: (await this.ctx.storage.get("name")) || "PC" }));
      this.sendPc({ t: "online", phones: await this.onlineList() });
      return new Response(null, { status: 101, webSocket: client });
    }

    return new Response("not found", { status: 404 });
  }

  async webSocketMessage(ws, message) {
    if (typeof message !== "string" || message.length > 65536) return;
    const tags = this.ctx.getTags(ws);
    if (tags.includes("pc")) {
      let m;
      try { m = JSON.parse(message); } catch { return; }
      if (m.t === "hello") {
        if (typeof m.pub === "string" && m.pub.length < 120) await this.ctx.storage.put("pub", m.pub);
        if (typeof m.name === "string") await this.ctx.storage.put("name", m.name.slice(0, 40));
        await this.tellPhones(true);
      } else if (m.t === "code") {
        const id = await this.ctx.storage.get("id");
        const r = await this.env.PAIRING.get(this.env.PAIRING.idFromName("codes"))
          .fetch("https://pairing/register", { method: "POST", body: JSON.stringify({ pc: id }) });
        const { code, exp } = await r.json();
        ws.send(JSON.stringify({ t: "code", code, exp }));
      } else if (m.t === "m" && typeof m.phone === "string" && typeof m.d === "string") {
        for (const p of this.phones(m.phone)) try { p.send(m.d); } catch {}
      } else if (m.t === "unpair" && typeof m.phone === "string") {
        const phones = (await this.ctx.storage.get("phones")) || {};
        delete phones[m.phone];
        await this.ctx.storage.put("phones", phones);
        for (const p of this.phones(m.phone)) try { p.close(4003, "unpaired"); } catch {}
      }
      return;
    }
    const phoneTag = tags.find((t) => t.startsWith("phone:"));
    if (phoneTag) this.sendPc({ t: "m", phone: phoneTag.slice(6), d: message });
  }

  async webSocketClose(ws, code, reason) {
    try { ws.close(code === 1005 ? 1000 : code, reason); } catch {}
    const tags = this.ctx.getTags(ws);
    if (tags.includes("pc")) {
      // a replaced PC socket closes after the new one is in: only say offline when none is left
      if (!this.ctx.getWebSockets("pc").some((w) => w !== ws)) await this.tellPhones(false);
    } else {
      this.sendPc({ t: "online", phones: await this.onlineList(ws) });
    }
  }

  async webSocketError(ws) { await this.webSocketClose(ws, 1011, "error"); }
}

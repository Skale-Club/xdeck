// Xdeck phone remote relay, at https://xdeck.skale.club
//
// One Room (Durable Object) per PC. Xdeck.exe keeps a WebSocket open to /pc?id=<pcId>&secret=<s>;
// phones open /phone?pc=<pcId>&phone=<phoneId>&token=<t>. The room passes messages between them.
// It never sees what they say: phone and PC encrypt end to end (ECDH P-256 keys exchanged at
// pairing, then AES-CBC + HMAC-SHA256), so a message here is an opaque base64 string.
//
// Pairing: the PC asks its room for a code (6 digits, 10 minutes, kept by the Pairing object);
// the phone posts that code and its public key to /pair and gets back the PC's id, its own id and
// token, and the PC's public key. The room keeps only a hash of each token and of the PC secret
// (the first connection sets it).
//
// Wire format
//   PC -> room   {"t":"hello","name","pub"}  {"t":"code"}  {"t":"m","phone","d"}  {"t":"unpair","phone"}
//   room -> PC   {"t":"code","code","exp"}  {"t":"paired","phone","name","pub"}  {"t":"online","phones":[...]}
//                {"t":"m","phone","d"}
//   phone -> room  d (the encrypted string)
//   room -> phone  d, or {"t":"pc","online":true|false,"name"}

const CODE_MS = 10 * 60 * 1000;
const ID = /^[\w-]{16,64}$/;

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    if (url.pathname === "/pc" || url.pathname === "/phone") {
      const pc = url.searchParams.get(url.pathname === "/pc" ? "id" : "pc") || "";
      if (!ID.test(pc)) return new Response("bad id", { status: 400 });
      return env.ROOM.get(env.ROOM.idFromName(pc)).fetch(req);
    }
    if (url.pathname === "/pair") {
      if (req.method !== "POST") return new Response("POST only", { status: 405 });
      return env.PAIRING.get(env.PAIRING.idFromName("codes")).fetch(req);
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
  }

  async fetch(req) {
    const url = new URL(req.url);
    const now = Date.now();
    if (url.pathname === "/register") {   // from a room: a fresh code for this PC
      const { pc } = await req.json();
      const codes = (await this.ctx.storage.get("codes")) || {};
      for (const [c, v] of Object.entries(codes)) if (v.exp < now || v.pc === pc) delete codes[c];
      let code;
      do { code = String(crypto.getRandomValues(new Uint32Array(1))[0] % 1000000).padStart(6, "0"); } while (codes[code]);
      codes[code] = { pc, exp: now + CODE_MS };
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

    if (req.headers.get("upgrade") !== "websocket") return new Response("WebSocket only", { status: 426 });

    if (url.pathname === "/pc") {
      const id = url.searchParams.get("id"), secret = url.searchParams.get("secret") || "";
      if (secret.length < 32) return new Response("bad secret", { status: 403 });
      const known = await this.ctx.storage.get("secret");
      const hash = await sha256(secret);
      if (known && !same(known, hash)) return new Response("wrong secret", { status: 403 });
      if (!known) { await this.ctx.storage.put("secret", hash); await this.ctx.storage.put("id", id); }
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

// A small QR code generator for the phone-remote pairing link: byte mode, error correction level M,
// versions 1 to 10 (up to 213 bytes, plenty for a link). Follows ISO/IEC 18004; the structure is
// that of Project Nayuki's reference generator (MIT). qrSvg(text) returns an <svg> string.
"use strict";

const QR = (() => {
  // per version (index = version): total codewords, ECC codewords per block, blocks
  const TOTAL = [0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346];
  const ECC_M = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
  const BLOCKS_M = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];
  const ALIGN = [[], [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50]];

  function mul(x, y) {
    let z = 0;
    for (let i = 7; i >= 0; i--) {
      z = (z << 1) ^ ((z >>> 7) * 0x11d);
      z ^= ((y >>> i) & 1) * x;
    }
    return z & 0xff;
  }

  function divisor(degree) {
    const r = new Array(degree).fill(0);
    r[degree - 1] = 1;
    let root = 1;
    for (let i = 0; i < degree; i++) {
      for (let j = 0; j < degree; j++) {
        r[j] = mul(r[j], root);
        if (j + 1 < degree) r[j] ^= r[j + 1];
      }
      root = mul(root, 0x02);
    }
    return r;
  }

  function remainder(data, div) {
    const r = new Array(div.length).fill(0);
    for (const b of data) {
      const f = b ^ r.shift();
      r.push(0);
      for (let i = 0; i < div.length; i++) r[i] ^= mul(div[i], f);
    }
    return r;
  }

  const bit = (x, i) => ((x >>> i) & 1) !== 0;

  function encode(text) {
    const bytes = Array.from(new TextEncoder().encode(text));
    let ver = 1;
    for (; ver <= 10; ver++) {
      const cap = (TOTAL[ver] - ECC_M[ver] * BLOCKS_M[ver]) * 8;
      if (4 + (ver < 10 ? 8 : 16) + bytes.length * 8 <= cap) break;
    }
    if (ver > 10) throw new Error("text too long for a QR code");
    const dataLen = TOTAL[ver] - ECC_M[ver] * BLOCKS_M[ver];

    // data bits: mode 0100 (bytes), count, the bytes, terminator, padding
    const bits = [];
    const put = (v, n) => { for (let i = n - 1; i >= 0; i--) bits.push((v >>> i) & 1); };
    put(4, 4);
    put(bytes.length, ver < 10 ? 8 : 16);
    for (const b of bytes) put(b, 8);
    put(0, Math.min(4, dataLen * 8 - bits.length));
    put(0, (8 - bits.length % 8) % 8);
    const data = [];
    for (let i = 0; i < bits.length; i += 8) { let v = 0; for (let j = 0; j < 8; j++) v = (v << 1) | bits[i + j]; data.push(v); }
    for (let pad = 0xec; data.length < dataLen; pad ^= 0xec ^ 0x11) data.push(pad);

    // split into blocks (short ones first), add ECC, interleave
    const nb = BLOCKS_M[ver], ecc = ECC_M[ver], raw = TOTAL[ver];
    const shortLen = Math.floor(raw / nb), numShort = nb - raw % nb;
    const div = divisor(ecc), blocks = [];
    for (let i = 0, k = 0; i < nb; i++) {
      const len = shortLen - ecc + (i < numShort ? 0 : 1);
      const d = data.slice(k, k + len);
      k += len;
      blocks.push({ d, e: remainder(d, div) });
    }
    const words = [];
    for (let i = 0; i <= shortLen - ecc; i++) for (const b of blocks) if (i < b.d.length) words.push(b.d[i]);
    for (let i = 0; i < ecc; i++) for (const b of blocks) words.push(b.e[i]);

    // the matrix
    const size = ver * 4 + 17;
    const mod = Array.from({ length: size }, () => new Array(size).fill(false));
    const fn = Array.from({ length: size }, () => new Array(size).fill(false));
    const set = (x, y, dark) => { mod[y][x] = dark; fn[y][x] = true; };

    for (let i = 0; i < size; i++) { set(6, i, i % 2 === 0); set(i, 6, i % 2 === 0); }
    for (const [cx, cy] of [[3, 3], [size - 4, 3], [3, size - 4]])
      for (let dy = -4; dy <= 4; dy++) for (let dx = -4; dx <= 4; dx++) {
        const x = cx + dx, y = cy + dy, d = Math.max(Math.abs(dx), Math.abs(dy));
        if (x >= 0 && x < size && y >= 0 && y < size) set(x, y, d !== 2 && d !== 4);
      }
    const al = ALIGN[ver], last = al.length - 1;
    for (let i = 0; i < al.length; i++) for (let j = 0; j < al.length; j++) {
      if ((i === 0 && j === 0) || (i === 0 && j === last) || (i === last && j === 0)) continue;
      for (let dy = -2; dy <= 2; dy++) for (let dx = -2; dx <= 2; dx++) set(al[i] + dx, al[j] + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
    }

    const format = (mask) => {
      const d = (0 << 3) | mask;   // level M = 00
      let r = d;
      for (let i = 0; i < 10; i++) r = (r << 1) ^ ((r >>> 9) * 0x537);
      const b = ((d << 10) | r) ^ 0x5412;
      for (let i = 0; i <= 5; i++) set(8, i, bit(b, i));
      set(8, 7, bit(b, 6)); set(8, 8, bit(b, 7)); set(7, 8, bit(b, 8));
      for (let i = 9; i < 15; i++) set(14 - i, 8, bit(b, i));
      for (let i = 0; i < 8; i++) set(size - 1 - i, 8, bit(b, i));
      for (let i = 8; i < 15; i++) set(8, size - 15 + i, bit(b, i));
      set(8, size - 8, true);
    };
    format(0);
    if (ver >= 7) {
      let r = ver;
      for (let i = 0; i < 12; i++) r = (r << 1) ^ ((r >>> 11) * 0x1f25);
      const b = (ver << 12) | r;
      for (let i = 0; i < 18; i++) {
        const a = size - 11 + i % 3, c = Math.floor(i / 3);
        set(a, c, bit(b, i)); set(c, a, bit(b, i));
      }
    }

    // codewords in the zigzag
    let k = 0;
    for (let right = size - 1; right >= 1; right -= 2) {
      if (right === 6) right = 5;
      for (let v = 0; v < size; v++) for (let j = 0; j < 2; j++) {
        const x = right - j, up = ((right + 1) & 2) === 0, y = up ? size - 1 - v : v;
        if (!fn[y][x] && k < words.length * 8) { mod[y][x] = bit(words[k >>> 3], 7 - (k & 7)); k++; }
      }
    }

    const masks = [
      (x, y) => (x + y) % 2 === 0, (x, y) => y % 2 === 0, (x) => x % 3 === 0, (x, y) => (x + y) % 3 === 0,
      (x, y) => (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0, (x, y) => (x * y) % 2 + (x * y) % 3 === 0,
      (x, y) => ((x * y) % 2 + (x * y) % 3) % 2 === 0, (x, y) => ((x + y) % 2 + (x * y) % 3) % 2 === 0,
    ];
    const apply = (m) => { for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) if (!fn[y][x] && masks[m](x, y)) mod[y][x] = !mod[y][x]; };

    const penalty = () => {
      let p = 0, dark = 0;
      const line = (get) => {
        for (let a = 0; a < size; a++) {
          let run = 1, prev = get(a, 0), seq = [];
          for (let b = 0; b < size; b++) {
            const c = get(a, b);
            if (b > 0) { if (c === prev) { run++; if (run === 5) p += 3; else if (run > 5) p++; } else { run = 1; prev = c; } }
            seq.push(c ? 1 : 0);
          }
          const s = seq.join("");
          for (const pat of ["10111010000", "00001011101"]) { let i = -1; while ((i = s.indexOf(pat, i + 1)) >= 0) p += 40; }
        }
      };
      line((a, b) => mod[a][b]);
      line((a, b) => mod[b][a]);
      for (let y = 0; y < size - 1; y++) for (let x = 0; x < size - 1; x++) {
        const c = mod[y][x];
        if (c === mod[y][x + 1] && c === mod[y + 1][x] && c === mod[y + 1][x + 1]) p += 3;
      }
      for (const row of mod) for (const c of row) if (c) dark++;
      const total = size * size;
      p += (Math.ceil(Math.abs(dark * 20 - total * 10) / total) - 1) * 10;
      return p;
    };

    let best = 0, bestP = Infinity;
    for (let m = 0; m < 8; m++) {
      apply(m); format(m);
      const p = penalty();
      if (p < bestP) { bestP = p; best = m; }
      apply(m);
    }
    apply(best); format(best);
    return mod;
  }

  // dark modules as one path, with the 4-module quiet zone
  function svg(text, px) {
    const m = encode(text), n = m.length + 8;
    let d = "";
    for (let y = 0; y < m.length; y++) for (let x = 0; x < m.length; x++) if (m[y][x]) d += `M${x + 4} ${y + 4}h1v1h-1z`;
    return `<svg class="qr" viewBox="0 0 ${n} ${n}" width="${px || n * 4}" height="${px || n * 4}" shape-rendering="crispEdges">` +
      `<rect width="${n}" height="${n}" fill="#fff"/><path d="${d}" fill="#000"/></svg>`;
  }

  return { encode, svg };
})();

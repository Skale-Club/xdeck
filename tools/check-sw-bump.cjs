// Fails when something under remote/public changed but the CACHE line of remote/public/sw.js did not.
// Open phone apps only reload into a new version when sw.js changes, so every deploy needs a bump.
// Usage: node tools/check-sw-bump.cjs [baseRef]   (default HEAD~1; all-zero or empty ref = skip)
const { execFileSync } = require("node:child_process");

const base = (process.argv[2] || "HEAD~1").trim();
if (!base || /^0+$/.test(base)) {
  console.log("check-sw-bump: no base ref (new branch or first push), skipping.");
  process.exit(0);
}
const git = (...args) => execFileSync("git", args, { encoding: "utf8" });

let changed;
try {
  changed = git("diff", "--name-only", base, "HEAD", "--", "remote/public").split("\n").filter(Boolean);
} catch (e) {
  console.error(`check-sw-bump: cannot diff against "${base}" (shallow checkout? use fetch-depth: 0).`);
  process.exit(2);
}
if (!changed.length) {
  console.log("check-sw-bump: remote/public unchanged, nothing to check.");
  process.exit(0);
}

const cacheLine = (text) => (text.split("\n").find((l) => /^\s*const CACHE\s*=/.test(l)) || "").match(/const CACHE\s*=\s*"[^"]*"/)?.[0] || "";
const before = cacheLine(git("show", `${base}:remote/public/sw.js`));
const after = cacheLine(git("show", "HEAD:remote/public/sw.js"));
if (!after) {
  console.error("check-sw-bump: no `const CACHE = \"...\"` line found in remote/public/sw.js.");
  process.exit(2);
}
if (before === after) {
  console.error(`check-sw-bump: FAIL. These files under remote/public changed since ${base}:\n  ${changed.join("\n  ")}\nbut the CACHE line in remote/public/sw.js is still ${after}.\nBump it (e.g. xdeck-remote-v9 -> v10), otherwise open phone apps will not reload into the new version.`);
  process.exit(1);
}
console.log(`check-sw-bump: OK (${before} -> ${after}, ${changed.length} file(s) changed).`);

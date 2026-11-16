# Publishing from another Windows computer

The installed updater accepts only releases signed with the existing Xdeck signing key.
Copy the original key through an authenticated file-transfer channel from the release computer:

`%APPDATA%\Skale\xdeck-update-key.xml`

Place it at the same path on the new computer. If the original computer uses a custom
`XDECK_UPDATE_KEY` path, use that file instead. Keep the private key outside this repository.
Do not run `New-UpdateKey.ps1` when adding another release computer: a new key would not match
the public key trusted by installed clients. Do not paste private key contents into chat or logs.

Run releases from a normal PowerShell terminal so Microsoft Store app file redirection does not
hide the key. Git, GitHub CLI (authenticated for this repository), .NET Framework 4.8 and the
repository's checked-in WebView2 libraries are required. Node is used only for the panel tests.

After the owner authorizes publication, validate the change on dev, update VERSION and CHANGELOG,
commit, and run:

```powershell
node --test tests/panel.test.cjs
node --check ui/app.js
git diff --check
git switch main
git merge --ff-only dev
git push origin main
.\build.ps1 -Release
```

Use a machine with sufficient available memory. Verify the new GitHub release includes
`XdeckSetup.exe`, the versioned portable ZIP and `latest.json`. Verify the downloaded ZIP against
the manifest signature using `Updater.Verify` before reporting the automatic update as published.

As of 2026-11-11, the Surface checkout and GitHub authentication are ready. The original key was
created on the owner's home computer and still needs to be transferred to the Surface.

The release build (`.\build.ps1 -Release`) runs the self-test (`Xdeck.exe --selftest`: pairing storage, crypto, ping, banner scale, in a temporary data folder) and stops if any check fails. CI (`.github/workflows/ci.yml`) runs the same build plus the node tests on every push to `dev` and `main` and on pull requests; it must be green before a release.

## Phone remote (xdeck.skale.club)

The phone app and the relay (`remote/`, Cloudflare Worker `xdeck-remote`) deploy by themselves: when a release reaches `main` (dev fast-forwarded into `main`) and `remote/**` changed, `.github/workflows/deploy-remote.yml` runs the phone tests and the sw.js check, then `wrangler deploy` (pinned wrangler 4.88.0), then checks that the live `https://xdeck.skale.club/sw.js` carries the new `CACHE`. It can also be run by hand from the Actions tab (workflow_dispatch). Nothing deploys from `dev`, so phones never get features the PC release does not have yet.

One-time setup (owner):
1. Cloudflare dashboard, My Profile, API Tokens, Create Token, template "Edit Cloudflare Workers". Limit it to the account and to the `skale.club` zone.
2. GitHub repo Settings, Secrets and variables, Actions: add `CLOUDFLARE_API_TOKEN` (that token) and `CLOUDFLARE_ACCOUNT_ID` (the Cloudflare account id).
3. GitHub repo Settings, Environments: create `production` (optionally with required reviewers).

CACHE bump rule: `const CACHE = "xdeck-remote-vN"` in `remote/public/sw.js` must change in every change that touches `remote/public`, because open phone apps only reload into a new version when sw.js changes. `node tools/check-sw-bump.cjs [baseRef]` enforces it (CI runs it on dev, pull requests and the deploy workflow).

Emergency only: a manual `wrangler deploy` from a laptop (in `remote/`, after `wrangler login`). If the browser login ends in HTTP 431 on the localhost callback, copy the callback address from the browser and request it with curl against `127.0.0.1:8976` (`curl "http://127.0.0.1:8976/oauth/callback?code=...&state=..."`), or log in with `wrangler login --callback-host 127.0.0.1`. Bump `CACHE` first and keep the laptop deploy on the same commit as `main`.

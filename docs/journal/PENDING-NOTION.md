# Pending journal entries

## 2026-11-11 — Hide empty device sections and identify Surface Type Cover

Target: Projects / Xdeck / Journal. Data source unresolved: Notion search for Xdeck returned a
Skale Club website Journal and website inventory entries, not an Xdeck journal. Do not write this
entry to the website's journal. Sync when the Xdeck journal destination is available.

- Date: 2026-11-11
- Type: Fix
- Area: UI
- Status: Done (local installation; release not published)
- Summary: Hide empty device categories, name connected peripherals, suppress unidentified USB
  entries and identify Surface Type Cover as a keyboard. Applied to the Surface's installed UI.
- Agent: Codex
- Model: Session orchestrator; gpt-6-luna for read-only source mapping
- Repo / Branch: Skale-Club/xdeck / fix/hide-empty-device-sections
- Commits / PR: uncommitted
- Tests: 5 Node panel regression tests passed; JavaScript syntax and git diff whitespace checks passed.

## Context

The panel always rendered Controllers and empty audio categories. A mouse icon labeled USB was
actually the Surface Type Cover, as confirmed by invoking the installed app's USB enumeration.
Windows exposed the cover's touchpad as mouse input. The owner chose to keep the device visible
under its Surface Type Cover name.

## What changed

Empty audio output, microphone and controller categories are omitted. Connected peripherals have
their own conditional Devices heading and visible escaped names. Surface Type Cover gets a keyboard
icon. Unidentified USB nodes and explicitly disconnected peripherals do not render in the panel.

## How it was built

A read-only gpt-6-luna explorer mapped UI and native device enumeration. The orchestrator checked
the real Windows device identity, edited and audited the UI, wrote focused tests, and copied the
JavaScript/CSS to the current user's installation after backing up the original files under the
Codex task's work/xdeck-panel-backup directory. No native executable was rebuilt.

## Decisions and why

Owner request: all device categories hide when empty. Owner answer: keep Surface Type Cover visible
by name. The UI selects a keyboard icon for that combined keyboard/touchpad device. Connection type
remains in the tooltip rather than replacing the peripheral name with USB.

## Tests and evidence

`node --test tests/panel.test.cjs`: 5 passed, covering populated-to-empty transitions, independent
audio categories, unidentified USB exclusion, Type Cover identity and escaped wireless battery data.
`node --check ui/app.js` and `git diff --check` passed. Installed and repository UI file hashes match.
Xdeck restarted successfully. Native visual capture was unavailable because the tray tool window
was not returned by the computer-use window inventory. No visual verification is claimed.

## Not done / risks

Not published, pushed or merged. A later official automatic update can replace this local UI patch.
Full build and browser automation were skipped: Surface had only 0.3 GB RAM free. Physical unplug/
replug verification was simulated through the actual UI state-message handler in the focused tests.

## Next

Sync to the Xdeck journal once located. Include this fix in the next owner-authorized release.

Estimated duration: 15 minutes. Implementation and focused verification: approximately 9 minutes.

## 2026-11-11 — Prepare the device-panel fix for release 0.7.5

Target: Projects / Xdeck / Journal; data source still unresolved.

- Date: 2026-11-11
- Type: Release
- Area: UI
- Status: Partial
- Summary: Owner authorized production publication. Prepare version 0.7.5 and rerun the five
  regression tests. Signed release publication awaits the existing update signing key.
- Agent: Codex
- Model: Session orchestrator (production preparation and verification)
- Repo / Branch: Skale-Club/xdeck / fix/hide-empty-device-sections, targeting dev
- Commits / PR: see Git history for the fix/hide-empty-device-sections branch
- Tests: 5 panel tests passed; node --check and git diff --check passed.

## Context

The owner explicitly requested production publication after the local UI fix was applied.

## What changed

Set VERSION to 0.7.5 and date the corresponding changelog section. Prepare the tested source for
integration into dev and signed publication through the existing build.ps1 -Release procedure.

## How it was built

Orchestrator verified origin/main was unchanged, the existing release was v0.7.4 and no original
signing key was available in the standard or known redirected app-data locations on this Surface.
No secret contents were printed. The machine had 0.3 GB of free RAM, so no heavy build was started.

## Decisions and why

Reuse the original signing key: installed clients trust its public half and reject other keys.
Do not replace it or publish an unsigned release. The owner was asked only for the key location
or the computer normally used for publishing, never for its contents.

## Tests and evidence

Five panel regression tests passed again. Syntax and whitespace checks passed. The webhook-security
skill was applied: no provider webhook endpoints were found in this release, and no remote service
code changed. Existing signed-release assets and the source signing procedure were inspected.

## Not done / risks

Production release and main promotion remain pending the existing signing key or an accessible
release environment. No installer or updater manifest for 0.7.5 has been published yet.

## Next

Once the original key is available, build and verify the signed artifacts, fast-forward main from
validated dev, publish v0.7.5 and verify downloaded release assets and an installed update.

### Release environment follow-up

The owner confirmed the original key was created on the home Vanildo-PC and requested support for
publishing from the Surface too. Vanildo-PC is online over the existing Tailscale network, but its
SMB administrative share returned Access denied, SSH was unavailable, and the RustDesk UI could
not be automated reliably (cached element unavailable after activation and refreshed observation).
No credentials, network permissions or signing keys were changed. No existing GitHub Actions
signing secrets or release workflow are configured. Added docs/RELEASING.md with the secure
one-time key transfer and release steps, and ignored signing-key XML filenames defensively.

Source integration: commit 2485fcc was fast-forwarded into dev and pushed to origin/dev. main and
the latest published release remain at the previous production version (0.7.4). The fix branch was
removed locally after integration. Publication is pending a manual authenticated transfer of the
original key; no unsigned installer or replacement signing key was published.

## 2026-11-11 — Center mobile pairing and show the Xdeck name

Target: Projects / Xdeck / Journal; data source unresolved.

- Date: 2026-11-11
- Type: Fix
- Area: UI
- Status: Partial (implemented locally; not deployed)
- Summary: Center the PWA pairing content vertically and display the Xdeck name below its icon.
- Agent: Codex
- Model: Session orchestrator; no delegation for this small edit
- Repo / Branch: Skale-Club/xdeck / fix/remote-pairing-layout
- Commits / PR: uncommitted
- Tests: Diff whitespace and HTML structure reviewed; mobile visual verification not run.

## Context

The owner identified the mobile Connect to your PC screen, requested vertical centering and the
application name, and asked what I already have a code means.

## What changed

Wrap pairing content in a bounded flex column with automatic vertical margins. Use symmetric
safe-area-aware outer spacing; retain scrolling when content exceeds the screen. Add the Xdeck
name below its existing icon. Pairing behavior is unchanged.

## How it was built

Read the actual pairing markup, CSS and click handlers in remote/public/index.html. Update only
the layout and brand markup. Surface remained in critical memory mode, so no browser automation
or development server was started.

## Decisions and why

Automatic vertical margins center short content while collapsing on overflow, keeping the top of
long code-entry and error states reachable. The existing manual option is useful for codes created
on the PC through Phone remote > Pair a phone, including when network discovery is unavailable.

## Tests and evidence

git diff --check passed. Reviewed element nesting and preservation of the existing pairing element
IDs and event handlers. The Find my PC flow discovers PCs; I already have a code reveals manual
code entry and the PC menu instructions. Both use the same six-digit pairing endpoint.

## Not done / risks

No Cloudflare deployment, commit or push. Mobile visual verification remains pending. This PWA is
deployed independently through remote/wrangler.toml; it does not require the desktop signing key.

## Next

Review on a phone and publish the PWA when authorized. Estimated 2 minutes; completed in about
2 minutes.

## 2026-11-11 — Publish the centered Remote PWA pairing screen

Target: Projects / Xdeck / Journal; data source unresolved.

- Date: 2026-11-11
- Type: Release
- Area: UI
- Status: Done (Cloudflare deployment and production asset verification passed)
- Summary: Prepare the centered pairing screen and visible Xdeck name for the Cloudflare PWA,
  with a new service-worker shell cache. Desktop signed release remains separate.
- Agent: Codex
- Model: Session orchestrator (production work)
- Repo / Branch: Skale-Club/xdeck / fix/remote-pairing-layout
- Commits / PR: see Git history for the remote-pairing-layout change
- Tests: Inline JavaScript syntax, service-worker syntax, unique HTML IDs and git diff --check passed.

## Context

The owner explicitly requested PWA production publication to see the installed mobile app update.

## What changed

Prepare the centered pairing UI and Xdeck brand for deployment. Bump the service-worker shell cache
from xdeck-remote-v2 to v3 so an installed client fetches the refreshed offline assets. The existing
network-first navigation strategy, skipWaiting and clients.claim behavior is preserved.

## How it was built

Read the live production HTML and service worker and save pre-deploy copies in the Codex task's
work/xdeck-pwa-release folder. Confirm the relay worker and Wrangler configuration are unchanged
relative to origin/main. The Surface's Cloudflare OAuth credentials were rejected (400); open the
official Wrangler OAuth login for the owner to complete without handling authentication UI.

## Decisions and why

PWA deployment uses Cloudflare Workers and static assets, independently of the Windows update
signing key. Reopening or refreshing the installed PWA loads the new document without clearing
localStorage, so existing PC pairings are preserved.

## Tests and evidence

node --check remote/public/sw.js passed. The inline script parsed through node:vm, all element IDs
were unique and git diff --check passed. Production baseline still lacked the new centered content
and brand markup. The webhook-security skill was applied previously; no webhook routes or relay
authentication code changed in this publication.

## Not done / risks

Deployment and production read-back await successful Cloudflare authentication. No browser test
was run on the memory-constrained Surface. An already open page keeps its current document until
it is refreshed or reopened; service-worker activation alone does not replace its DOM.

## Next

Verify the Cloudflare account and current deployment, publish the approved PWA, read back the live
HTML and service worker, and record the production version and result here.

### Production result

The first OAuth attempt timed out; the second succeeded with the owner's interactive browser
authorization. Cloudflare authentication is now available on the Surface. Wrangler dry-run passed.
Commit 8b23050 was integrated through dev into main and pushed before deployment. The previous
production version was 9b47b863-4de8-471e-aebd-339efa132107 (the rollback target).

At approximately 15:19 America/New_York, xdeck-remote was deployed to xdeck.skale.club as version
7833c0c5-3e0d-4155-989e-225cb117d471. Production GETs of /, /sw.js and /manifest.webmanifest returned
200 and SHA-256 hashes matching the deployed local assets exactly. Read-only routing checks passed:
GET /pair and /discover returned 405; /pc and /phone without IDs returned 400. No pairing state,
Durable Object migration or relay authentication code was changed. No browser capture was used.

The owner can close and reopen the installed PWA to load the new screen. No reinstall or pairing
reset is required. The desktop installer release still awaits its original signing key; this
successful PWA deployment is independent of it. Journal synchronization to Notion remains pending
because an Xdeck journal destination has not been located.

Estimated publication duration: 5 minutes. Actual: approximately 7 minutes, including the OAuth
retry. The deployment itself took approximately 8 seconds; read-back verification took 3 seconds.

## 2026-11-11 — Apply the approved pairing interface and shared brand

Target: Projects / Xdeck / Journal; data source unresolved.

- Date: 2026-11-11
- Type: Fix
- Area: UI
- Status: Done (desktop applied locally and PWA production assets verified)
- Summary: Apply the approved compact shared branding, desktop Settings code generation and
  separate phone pairing-code screen with Back and adjacent Pair action.
- Agent: Codex
- Model: Session orchestrator for desktop, review and production; gpt-6.1-sol for PWA and tests
- Repo / Branch: Skale-Club/xdeck / feat/approved-pairing-interface
- Commits / PR: see Git history for this feature branch
- Tests: Desktop panel 9 passed; PWA pairing 7 passed, including the 15-second timeout and retry.

## Context

The owner requested visual previews before implementation, revised typography and vertical
alignment, requested a shared desktop/mobile icon and font, then approved with a smaller icon-name
gap. The phone needs a dedicated code-entry step and the PC panel needs Settings to generate it.

## What changed

Use an 8px gap and the same app icon plus bundled Inter Bold 700 wordmark on both platforms. The
original mockups used a Windows system font; the portable licensed font keeps the intended bold
appearance consistent on Android, iOS and Windows. Include its SIL OFL license, avoid runtime font
service requests, and precache it in service-worker v4.

Desktop Settings reuses existing native remote-code and remote-cancel bridge commands. It displays
code generation status, expiry, network failures and paired phone status, with Back/Escape/controller
navigation. No native host or relay security code changes. PWA discovery and manual input now use
separate stages; the code input and explicit Pair button remain adjacent. Back aborts discovery
and stale responses cannot replace the current screen. Existing pairings and install hints remain.

## How it was built

One smaller worker owned only the PWA, service worker and PWA tests. The orchestrator owned desktop
UI, shared assets, docs, final review and publication. Work was serialized on the Surface, with no
development server, full build or browser capture. Desktop UI files were backed up and copied into
the current user's installation; Xdeck restarted and installed asset hashes matched source.

## Decisions and why

Explicit Pair/Enter submission replaces automatic submission on the sixth digit. Generation and
pairing have bounded waits, and actions cannot race an in-progress pairing. Existing paired phones
are listed read-only in Settings; no removal action or authentication changes were introduced.

## Tests and evidence

Desktop tests cover Settings navigation, native bridge requests, offline/error/timeout/expiry states,
safe phone names and existing device-section behavior. PWA tests cover manual/back/discovery,
stale responses, explicit submission, failure recovery and preservation of saved PCs. Syntax and
unique-ID checks passed. Physical phone keyboard rendering remains for owner device verification.
The added 15-second /pair timeout test confirms that failure releases navigation, refocuses the
code field, clears the timer and allows another explicit attempt. All 16 focused tests passed.

## Not done / risks

Official Windows installer publication still needs the original signing key from the home PC.
The local Surface panel can use the existing host without recompilation. No new key was generated.
The installed phone page must be reopened to load the new document; pairings do not need resetting.

## Next

Sync this entry when an Xdeck Notion journal destination is available. Publish the official Windows
installer after the existing original signing key becomes available.

### Verified delivery

Commit 26f256f was merged through dev into main and pushed. Cloudflare deployment
94e2520a-0593-45a8-a636-925312729edc serves xdeck.skale.club. Its rollback target is
7833c0c5-3e0d-4155-989e-225cb117d471. At 16:07 America/New_York, GETs for /, /sw.js, the bundled
font and its OFL license returned 200 and SHA-256 hashes identical to source. Read-only relay checks
passed (GET /pair and /discover: 405; /pc and /phone without IDs: 400). No webhook, native-host,
relay or migration changes were made. The existing webhook-security review still applies.

The Surface's installed desktop JavaScript, CSS, icon and font hashes match source; Xdeck is running
and responding. During this work the owner asked to close Claude and Thunderbird; their windows
were closed and remaining application background processes were stopped. Zero Claude/Thunderbird
processes remained in the final check. No additional browser capture was started.

Estimated duration: 20 minutes; actual approximately 22 minutes including the requested application
shutdowns and memory-constrained tool execution. All 16 focused tests passed. The owner can reopen
the PWA to load the new screen and use desktop Settings > Generate access code without reinstalling
the app or clearing existing paired PCs.

## 2026-11-14 — Xdeck 0.7.6: controller volume, quiet OBS shortcuts, white banner icons

Target: Projects / Xdeck / Journal (destination still unresolved; sync when it exists).

- Date: 2026-11-14
- Type: Feature + Fix
- Area: Controller input, OBS, banners
- Status: Released (v0.7.6 on GitHub, signed; installed copies update within six hours)
- Summary: Audio shortcut (L1 + D-pad right) now maps right stick left / right to the output and
  up / down to the volume (5% steps, repeats while held, volume banner). OBS shortcuts are ignored
  silently while OBS Studio is closed (R1 + L3 / R1 + R3 fired by chance in games kept popping
  error banners); repeated OBS errors show once a minute; the phone remote still says "OBS is not
  open". Banner icons are white instead of the accent blue. Also ships the uncommitted 0.7.6 brand
  logo work and the unreleased 0.7.5 changes.
- Agent: Claude Code
- Model: claude-opus-5-5
- Repo / Branch: Skale-Club/xdeck / feat/pad-volume-obs-quiet -> dev -> main
- Commits / PR: 3c07763, merge 1649d90; release https://github.com/Skale-Club/xdeck/releases/tag/v0.7.6
- Tests: 16 Node tests passed; node --check ui/app.js and git diff --check passed; `--banners`
  sheet checked visually; release ZIP verified against the embedded public key. The stick combo
  itself was not exercised with a physical controller.

Ficou fora do dev: nothing.

## 2026-11-14 — Phone remote: Apps tab (OBS, Steam, PC settings) and phone audio hardening

Target: Projects / Xdeck / Journal (destination still unresolved; sync when it exists).

- Date: 2026-11-14
- Type: Feature + Fix
- Area: Phone remote (PWA + PC side)
- Status: On branch, not released (needs a Windows build and the relay deploy)
- Summary: The OBS tab becomes Apps: the PC reports which apps it has (OBS from HKLM\SOFTWARE\OBS
  Studio, Steam from HKCU\Software\Valve\Steam, Settings always) and whether each runs; the phone
  shows a button only for those. OBS gets open / close; Steam gets open / close, Big Picture,
  library, store, friends, downloads, settings; PC settings gets Settings pages plus lock, sleep,
  restart, shut down (confirmed on the phone). Phone audio: banners on the PC, state pushed every
  2 s when it changes, replies sent from a sender thread instead of the UI thread, remote.log for
  failures. The reported "volume / output on the phone does not work" did not reproduce against a
  simulated PC through the local relay; real-PC cause still unconfirmed.
- Agent: Claude Code
- Repo / Branch: Skale-Club/xdeck / ccr-c48f921e-b3t4rf
- Tests: 16 Node tests passed; C# compiled with Mono mcs -langversion:5 (CNG ECDH stubbed);
  end-to-end Playwright run (phone app + wrangler dev relay + Node PC stand-in): outputs listed,
  output switch and volume reached the PC, app open / OBS record / Settings page sent, shut down
  asked first. Not run on Windows.

## 2026-11-14 — Phone remote fix merged to dev and installed on the Surface

Target: Projects / Xdeck / Journal (destination still unresolved; sync when it exists).

- Date: 2026-11-14
- Type: Integration
- Area: Phone remote (PC side + PWA)
- Status: In dev, installed on the Surface, relay and phone app deployed; signed public release pending
- Summary: Fast-forwarded dev to ccr-c48f921e-b3t4rf (Apps tab, phone audio thread, microphones,
  "PC not answering", remote.log) and pushed it. Built with build.ps1 -Install (VERSION still
  0.7.6, 9 s) and installed for the current user on VANILDO-TABLET; the installed exe matches the
  build and Xdeck restarted. The signed public release could not run here: the update signing key
  is not on this Surface (same blocker as 0.7.5), so it waits for the desktop, with VERSION bumped
  to 0.7.7 there. The relay and phone app were deployed with `wrangler deploy` (version
  3f44e824, xdeck.skale.club, cache xdeck-remote-v5) after the owner's `wrangler login`; the agent
  shell kept reading a stale OAuth token, so the deploy ran in the owner's terminal.
- Agent: Claude Code (Opus 5.5, no subagents; machine in critical memory mode)
- Repo / Branch: Skale-Club/xdeck / dev
- Tests: Compiled on Windows (csc, one pre-existing CS0108 warning). Not yet exercised from the
  phone; remote.log not created yet (it is written on the first phone request).

## 2026-11-14 — Xdeck 0.7.7 released: tray icon hint + phone remote apps and audio

Target: Projects / Xdeck / Journal (destination still unresolved; sync when it exists).

- Date: 2026-11-14
- Type: Release
- Area: Tray / panel UI; phone remote (PC side)
- Status: Done (public signed release v0.7.7; installed copies update within six hours)
- Summary: On Windows 11 25H2 (build 26200) the tray icon stayed under ^ although Xdeck had written
  IsPromoted=1; Explorer ignored it, a registry toggle and an Xdeck restart did not help, and the
  Settings switch showed "on" without effect. Dragging the icon out worked and did not change the
  registry. New TrayPlace class asks the taskbar through UI Automation (NotifyItemIcon buttons under
  Shell_TrayWnd, named after the tooltip) each time the panel opens; while the icon is hidden the
  panel shows "Keep Xdeck on the taskbar" with Show me (invokes ^) and Don't show again
  (tray-hint-off flag in Data). TrayPromote kept (harmless). Released together with the phone
  remote work already on dev (Apps tab, phone audio thread, microphones). feat/tray-hint and
  ccr-c48f921e-b3t4rf merged and deleted; main = dev = v0.7.7 (db6c56b).
- Agent: Claude Code (Opus 5.5, no subagents)
- Repo / Branch: Skale-Club/xdeck / main
- Tests: 17 Node panel tests passed; node --check; csc build (one pre-existing CS0108 warning);
  build copy run on the desktop showed the hint while its icon was hidden; installed copy kept its
  dragged icon on the taskbar after a restart. Release run outside the app context via WMI;
  assets XdeckSetup.exe, Xdeck-0.7.7.zip and latest.json present, and the downloaded ZIP passed
  Updater.Verify against the manifest signature (a tampered input failed). Show me / Don't show
  again were not clicked in the real panel.

## 2026-11-14 — Xdeck 0.7.8 released: Material 3 volume sliders (panel + phone remote)

Target: Projects / Xdeck / Journal (destination still unresolved; sync when it exists).

- Date: 2026-11-14
- Type: Release
- Area: Panel UI; phone remote PWA
- Status: Done (signed release v0.7.8; phone app deployed)
- Summary: Replaced the volume sliders with a Material 3 style: thick rounded track (12px panel,
  16px phone), accent fill ending in a gap, slim upright handle, end dot, grey while muted. The
  phone app now sets --p while sliding and on each state update, marks the row muted, and its
  service worker cache moved to xdeck-remote-v6. Released as v0.7.8 (3e360fa) and deployed the
  relay + phone app with wrangler (version e69043c5, xdeck.skale.club). Both the release and the
  wrangler deploy ran outside the Claude app context through WMI, so the real signing key and
  wrangler login were used; the deploy worked from the agent this time.
- Agent: Claude Code (Opus 5.5, no subagents)
- Repo / Branch: Skale-Club/xdeck / main
- Tests: 17 Node panel tests; node --check; panel and phone Audio tab checked visually in the
  in-app browser (20%, 85%, muted; phone --p follows input). Release assets present and the ZIP
  passed Updater.Verify. Production serves sw.js with xdeck-remote-v6 and the new slider CSS.
  Not dragged with a real mouse or tried on a real phone.

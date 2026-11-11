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

# Changelog

## 0.4.0 — 2026-11-11

- **Every device that connects shows up on screen**: a controller, a Bluetooth device (headphones,
  mouse, keyboard, phone) or a USB device (pen drive, headset, keyboard, phone) gets a banner with its
  name, an icon for its kind and its battery when it reports one (pills and percent, *charging*;
  `USB` for a controller on a cable). A mouse or keyboard (as Windows sees it, or by its name) gets
  a small one-line banner instead. Several at once come one after another.
- **Low battery alert**: when a wireless device drops to 15%, a red card slides in at the top centre
  of the screen with a pulsing battery, the device and how much is left, for six seconds (a click
  closes it, as it does any banner). Again only after the device has charged.
- A USB or Bluetooth headset gets that one banner instead of one for its audio output and one for
  its microphone. No banner for devices built into the PC, for devices back within a minute
  (Bluetooth mice and keyboards waking up), or for what reconnects when the PC wakes from sleep.
- **OBS Studio from the controller**, over OBS's own WebSocket server (OBS 28+, Tools › WebSocket
  Server Settings): R1 + D-pad right + Cross / Square / Triangle / Circle start or stop recording,
  pause it, start or stop streaming and save the replay buffer; R1 + R3 / R1 + L3 switch to the next
  / previous scene. `shortcuts.ini` also takes `obs.virtualcam`, `obs.scene.<name>`,
  `obs.mute.<source>` and `obs.hotkey.<OBS hotkey>`, plus `obs.address` and `obs.password`. A
  banner says what OBS did, or why it could not (OBS closed, wrong password, no such scene).
  An existing `shortcuts.ini` gets the OBS lines added.

## 0.3.2 — 2026-11-10

- The updater writes what it does and why it stops to `update.log` in the data folder
  (`%LOCALAPPDATA%\Xdeck`).

## 0.3.1 — 2026-11-10

- **XdeckSetup.exe**: install Xdeck like any Windows app, from one file. Pick everyone on this PC
  or only me and Start with Windows; it adds Start menu entries and an entry in Settings › Apps,
  which uninstalls it (settings stay). `/S`, `/CurrentUser` and `/NoAutostart` install silently.
  Every release carries it at `releases/latest/download/XdeckSetup.exe`.
- An update now also refreshes the version shown in Settings › Apps.

## 0.3.0 — 2026-11-10

- Xdeck updates itself, like the other Skale apps: a minute after start and every six hours it
  checks the latest GitHub release, downloads it in the background, keeps it only if it matches its
  signature, and offers **Restart to update** in a notification, the tray menu and the panel.
  Quitting Xdeck installs it too. Settings stay. In `C:\Program Files` Windows asks for
  administrator approval once per update. Tray menu › **Check for updates** checks right away.
- The panel shows the version at the bottom; `Xdeck.exe` carries it too (Explorer › Properties).
- `build.ps1 -Release` signs the zip, writes `latest.json` and publishes the GitHub release.

## 0.2.0 — 2026-11-10

- Open and hide the panel from any controller: hold L1 + D-pad right and press Triangle (LB + right
  + Y on Xbox). Hiding it gives the focus back to the window that had it, usually the game.
- Each controller can be a mouse on its own: L1 + D-pad right + Square (LB + right + X on Xbox) on
  that controller, or the **Use as mouse** switch in its page, turns it on and off; its card shows
  **Mouse**. Left stick = pointer (LT / L2 for fine moves), right stick = scroll, A / B / X = left /
  right / middle click, D-pad = arrow keys, Menu = Enter, View = Esc. It waits while the panel is open.
- A click anywhere outside the panel closes it, like other tray panels.
- `Xdeck.exe` has its own icon (the white gear on a blue tile) in Explorer, Start and the Task
  Manager; `tools\MakeIcon.cs` draws it on every build.
- Shortcuts live in `shortcuts.ini` in the data folder (tray menu › **Edit shortcuts…**): the panel
  shortcut (buttons held together with `+` or one after another with `,`), the audio combo buttons and
  the two keyboard hotkeys. Xdeck reads the file again when it is saved, with no restart or new
  build, and names any line it cannot use.
- The whole panel works from the controller: D-pad / left stick to move (held directions repeat),
  left/right to switch the device or set the volume, A to choose, X to mute, B to go back or close,
  Menu to close, and holding B to leave the controller test. A focus ring and a button help line show
  while a controller drives the panel; moving the mouse hands it back.

## 0.1.0 — 2026-11-10

First version, moved into `Dev\xdeck` (it began the same day as AudioCycle, then PadDeck and Joybox).

- Tray app with a gear icon; panel in HTML/SVG on WebView2.
- Audio output and microphone: switch the default device, live level meters, volume and mute.
- Hotkeys `Ctrl+Alt+F11` / `Ctrl+Alt+F10` and the controller combo (D-pad right + L1 + right stick)
  cycle the audio output, with a banner on the primary screen.
- Banner when an output or a microphone is connected.
- Controllers: Xbox (XInput), DualSense and Pro Controller / Joy-Cons (HID), Logitech G29 and a USB
  handbrake; battery with a charging indicator in the panel, the tester and the tray tooltip.
- Controller tester with real artwork (Xbox, DualSense, Pro Controller, Joy-Cons), G29 wheel and
  pedals, handbrake lever.
- Brand logos per device; **Start with Windows** in the tray menu; portable `Data\` folder.

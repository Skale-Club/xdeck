# Changelog

## Unreleased

- **Apps on the phone remote** (the tab that was OBS): one button per app the PC has, and only for
  the apps installed there. Under it, the app's state (open / closed) with Open and Close, and its
  own buttons:
  - **OBS Studio**: open and close it from the phone (closing is like its X, so OBS still asks
    while it records); record, pause, stream, save replay, scenes and virtual camera once it is open.
  - **Steam**: open / close, Big Picture (and leave it), library, store, friends, downloads, settings.
  - **PC settings**: open Settings or its Sound, Display, Bluetooth, Wi-Fi, Windows Update or Power
    page; lock, sleep, restart and shut down the PC (sleep, restart and shut down ask first).
  A PC on an older Xdeck shows "Update Xdeck on the PC" there.
- **Phone audio, fixed**: the Audio tab stayed empty (no outputs, volume "–") and its volume did
  nothing, because everything the PC did for it waited in the tray's UI queue. The phone's audio
  requests now run on their own audio thread and the answers go out from a sender thread, so they no
  longer depend on the UI thread. If the PC still does not answer, the tab says so
  ("Update Xdeck on the PC") instead of staying empty.
- **Microphones on the phone**: the Audio tab lists the outputs and the microphones like the panel,
  with an icon for each (speaker, headphones, TV / monitor, mic); tap one to make it the default;
  volume and mute for both.
- Switching the output or microphone, or moving a volume, on the phone shows the same banner on
  the PC as the controller's audio shortcut. The phone follows changes made on the PC (panel,
  controller, Windows) within 2 seconds, and the apps' open / closed state too. Replies to the phone
  go out from their own thread, so the PC's interface never waits on the network; what goes wrong
  with a phone's request is written to `remote.log` in the Xdeck data folder.
- **The panel says when the tray icon is hidden**: Windows 11 hides new tray icons under ^ and
  ignores the "show this icon" setting (written by Xdeck, and even its own Settings switch); only
  dragging the icon out works. Each time the panel opens, Xdeck asks the taskbar (UI Automation)
  whether its icon is there, and while it is not, a card explains how to drag it out, with
  **Show me** (opens ^) and **Don't show again**. It goes away by itself once the icon is out.

## 0.7.6 — 2026-11-14

- **Volume from the controller**: hold L1 + D-pad right (the audio shortcut) and push the right
  stick up or down for the volume, 5% at a time (hold it to keep going); a banner shows the output
  and a level bar. Left / right still switch the output (up / down used to switch it too).
- **No more stray OBS banners**: with OBS Studio closed, an OBS shortcut pressed by chance in a game
  (R1 + L3, R1 + R3, ...) does nothing and says nothing. The same OBS error is shown once a minute at
  most. The phone remote's OBS buttons still say "OBS is not open".
- The icon in the banners is white, like the maker's logo, instead of the Windows accent color.
- **The connection banner shows the maker's logo** in white before the name, like the panel's
  controller cards: Xbox, PlayStation, Nintendo, Logitech G (G29), Logitech (receiver mice and
  keyboards, Yeti), Apple (AirPods), Hollyland, Roku, Acer. Also in the one-line mouse and keyboard
  banner, the low-battery alert, and the audio output / microphone banners. The logos are masks
  made from `ui\brands` by `tools\Render-BrandLogos.ps1`, embedded in the exe.
- **The right icon for USB devices**: a webcam gets a camera, a pen drive, card reader or external
  disk gets a drive, a USB microphone or headset its own icon, from what Windows installed under
  the device instead of the generic USB plug.
- A USB device without a name of its own ("USB Composite Device") is named after its webcam,
  microphone or disk ("EMEET SmartCam S600", "Wireless microphone").
- `Xdeck.exe --banners <file.png>` renders sample banners of every look for a visual check.

## 2026-11-11 — Remote PWA

- Center the phone pairing screen vertically, keep overflow scrollable, and show the Xdeck name
  below its icon. Refresh the offline shell cache for the updated screen.
- Add a dedicated code-entry step with Back and an adjacent Pair button. Cancel stale discovery
  responses when navigating, require explicit confirmation after six digits and preserve paired PCs.
- Use the same bundled bold wordmark font and app icon as the Windows panel, with a compact 8px gap.

## 0.7.5 — 2026-11-11

- Hide audio output, microphone and controller sections when no devices are connected.
- Give connected peripherals their own Devices section, display their names, and omit unidentified
  USB entries. Surface Type Cover uses a keyboard icon instead of its touchpad's mouse icon.
- Add Settings to the panel with code generation, countdown, connection feedback and paired phones.
  Use the same app icon and bundled bold wordmark font as the phone remote.

## 0.7.4 — 2026-11-11

- **Mice and keyboards on a Logitech receiver** (Unifying, Bolt, Lightspeed) show up by name with
  their battery, instead of a "USB" circle for the receiver. Xdeck asks the receiver what is paired
  to it and each device that is awake for its battery (Logitech's HID++), once a minute; a device
  that is asleep keeps the battery it last reported.

## 0.7.3 — 2026-11-11

- The tray gear gets a dot when an update is ready to install.

## 0.7.2 — 2026-11-11

- Controller cards show the controller's own model in the circle (Xbox, DualSense, Pro Controller,
  Joy-Cons, the wheel, the handbrake), and the brand's logo in white before the name when there is
  one (no logo for the handbrake).
- The banner's generic controller is the generic silhouette from the same artwork, and the G29 and
  the handbrake get their own icons there too.

## 0.7.1 — 2026-11-11

- The "Controller connected" banner (and the low-battery alert) shows the controller's own model:
  Xbox, DualSense, Pro Controller or Joy-Cons, from the same artwork as the controller test
  (`tools\Render-PadIcons.ps1` turns it into the masks in `ui\pads\model`).

## 0.7.0 — 2026-11-11

- **Pairing starts on the phone**: in the Xdeck Remote app, **Find my PC** makes a 6-digit code
  pop up on the screen of every Xdeck PC on the same network (the panel opens on it, with the
  phone's name); type it in the app and it is paired. Nothing to do on the PC. The relay knows
  which network a PC is on from where it connects (over IPv4 and IPv6). Tray menu › Phone remote ›
  **Pair a phone…** remains for a PC on another network.

## 0.6.2 — 2026-11-11

- The panel has no Phone remote section any more. Pairing moved to the tray menu: right-click the
  gear › **Phone remote** › **Pair a phone…** shows the code in the panel (only while it is good),
  and a notification says when the phone paired. The same menu lists the paired phones (● connected
  now); click one to remove it.

## 0.6.1 — 2026-11-11

- Phone remote has no on/off switch any more: Xdeck is always ready for the Xdeck Remote app. The
  panel shows how it is connected, **Pair a phone** and the paired phones.

## 0.6.0 — 2026-11-11

- **Xdeck Remote is an app now**, installable on Android and iPhone from
  [xdeck.skale.club](https://xdeck.skale.club), and it pairs with a 6-digit code instead of a QR code:
  **Pair a phone** in the panel shows the code, you type it in the app once, and from then on it
  connects by itself, from anywhere (mobile data too). Several PCs can be paired from one phone.
- Phone and PC meet through a relay (a Cloudflare Worker with Durable Objects, `remote/`), so the PC
  listens on no port: the firewall rule 0.5.0 made is removed. Everything between them is encrypted
  end to end (ECDH P-256, AES-CBC + HMAC-SHA256), so the relay cannot read it.
- The panel lists the paired phones, with a dot for the ones connected, and **Remove**.

## 0.5.1 — 2026-11-11

- The Updates area shows up only when there is something to do (downloading, ready, installing);
  otherwise the bottom of the panel just shows the version. Tray menu › **Check for updates**
  still checks on demand.

## 0.5.0 — 2026-11-11

- **Phone remote**, like Unified Remote: turn on **Phone remote** in the panel and scan its QR code
  with a phone on the same Wi-Fi. The phone's browser becomes a touchpad (tap to click, two fingers
  to scroll, two-finger tap for a right click, hold to drag), a keyboard (what you type goes to the
  PC, with Ctrl / Alt / Shift / Win, arrows and shortcuts), media keys, the audio output switch and
  volume, and OBS buttons. Nothing to install on the phone; add the page to the home screen to open
  it like an app. Only a phone that opened the pairing link can send anything; **New link** unpairs
  every phone. An install for everyone (and its updates) opens port 8765 on private networks.

## 0.4.2 — 2026-11-11

- Mice, keyboards and other wireless devices are circles now, side by side under the controllers:
  the icon inside, and a ring around it that fills with the battery (green, amber, red when low),
  the percent below, the name on hover. A USB receiver gets a dashed ring.

## 0.4.1 — 2026-11-11

- **Mice, keyboards and other wireless devices in the panel**: one thin line each under the
  controllers, with the battery (percent and five pills) when the device reports it, or `USB` for a
  receiver on a cable.
- **Updates inside the panel**: the bottom of the panel shows the version and what the updater is
  doing (checking, downloading with a progress bar and percent, ready, installing, or why it could
  not check), with the button that fits: **Check now**, **Restart to update** or **Try again**. It
  works from the controller too.
- Clicking an old "is ready" notification after that version is installed now says Xdeck is up to
  date instead of "no update is ready".

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

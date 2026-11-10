# Changelog

## 0.2.0 — 2026-11-10

- Open and hide the panel from any controller: hold L1 + D-pad right and press Triangle (LB + right
  + Y on Xbox). Hiding it gives the focus back to the window that had it, usually the game.
- Controller as a mouse: L1 + D-pad right + Square (LB + right + X on Xbox), the **Use as mouse**
  switch in the panel or the tray menu turn it on and off. Left stick = pointer (LT / L2 for fine
  moves), right stick = scroll, A / B / X = left / right / middle click, D-pad = arrow keys,
  Menu = Enter, View = Esc. It waits while the panel is open.
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

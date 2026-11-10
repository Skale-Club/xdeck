# Xdeck

Your audio and your controllers, from the tray. Switch the audio output and the microphone, set
their volume, see the battery of each controller, and test controllers, wheels and handbrakes —
from the panel, a hotkey, or the controller in your hands.

🇧🇷 [Português](#português)

Made by **[Skale Club](https://skale.club)**.

## Install

It needs Windows 10/11 64-bit with the Microsoft Edge WebView2 Runtime (built into Windows 11) and
.NET Framework 4.8 (built into Windows 10 1903+). Unzip the release, then either:

| How | What happens |
|---|---|
| **For every user of the PC** — right-click `install.ps1` › *Run with PowerShell* (approve the admin prompt) | Installs to `C:\Program Files\Xdeck`, adds it to everyone's Start menu and starts it with Windows for everyone. |
| **Only for you** — `install.ps1 -CurrentUser` | Installs to `%LOCALAPPDATA%\Programs\Xdeck`, your Start menu and your startup. No admin. |
| **Portable** — double-click `Xdeck.exe` in the unzipped folder | Runs from there; its settings stay in the `Data\` folder beside it. |

`install.ps1 -Uninstall` (add `-CurrentUser` for a per-user install) removes it. An installed Xdeck
keeps each user's settings in `%LOCALAPPDATA%\Xdeck`. On first run the gear is pinned to the taskbar
instead of hiding under the ^ arrow. Opening Xdeck a second time does nothing; the first copy keeps
running (one per signed-in user).

Xdeck lives in the system tray as a gear:

- **Left click**: open or close the panel. It stays open until you close it (×, Esc or another click).
- **Right click**: open the panel, next / previous audio output, **Start with Windows**,
  **Controller as mouse**, **Edit shortcuts…**, quit.

## The panel

| Section | What you can do |
|---|---|
| **Audio output** | See the active output and its live level. Click it to list the others and pick one. Volume slider (drag or scroll), mute. |
| **Microphone** | The same for the default microphone; it is also set as the communications device, so calls use it. |
| **Controllers** | Every connected controller with its battery (10 pills, a bolt while charging) or `USB`. Click one to test it. |

The controller test shows the real controller: Xbox, DualSense, Pro Controller and Joy-Cons light up
button by button; the G29 shows the wheel angle and pedals; the handbrake shows the lever.

## The panel from the controller

Hold **L1 + D-pad right** and press **Triangle** (Xbox: LB + right + Y; Nintendo: L + right + X) to
open the panel over whatever is running. The same combination hides it again and gives the focus
back to the game. Any controller Xdeck reads works. To use other buttons, see
[Change the shortcuts](#change-the-shortcuts).

While the panel is open, the controller drives all of it:

| Button | What it does |
|---|---|
| D-pad / left stick up and down | Move between the close button, each device, its volume and the controllers |
| Left / right on a device | Switch to the previous / next output or microphone |
| Left / right on a volume | Volume −/+ 5% |
| A (✕) | Open the device list, pick a device, mute or unmute a volume, test a controller, close |
| X (▢) | Mute or unmute the focused device |
| B (◯) | Close the device list, or close the panel |
| Menu / Options | Close the panel |
| Hold B (◯) | Leave the controller test (every other button is being tested there) |

Opened with the mouse, the panel shows the focus ring at the first controller press; moving the mouse
hides it. Windows sends controller input to every app, so a game that keeps reading the controller in
the background still sees these presses.

## The controller as a mouse

Hold **L1 + D-pad right** and press **Square** (Xbox: LB + right + X) to turn any controller into a
mouse, and again to turn it back. The **Use as mouse** switch in the panel and **Controller as
mouse** in the tray menu do the same; a banner says On or Off.

| Controller | Mouse / keyboard |
|---|---|
| Left stick | Pointer (faster the further you push; hold LT / L2 for fine moves) |
| Right stick | Scroll up / down, left / right |
| A (✕) | Left click (hold to drag) |
| B (◯) | Right click |
| X (▢) | Middle click |
| D-pad | Arrow keys (held = repeat) |
| Menu / Options | Enter |
| View / Share | Esc |

While the panel is open the controller drives the panel instead. The pointer speed is
`mouse.speed` in `shortcuts.ini`. Windows does not let Xdeck click in windows that run as
administrator.

## Switch the audio output without the panel

- `Ctrl+Alt+F11` / `Ctrl+Alt+F10`: next / previous output.
- On a controller: hold **D-pad right + L1** (LB / L) and flick the **right stick** right or up for
  the next output, left or down for the previous one.
- A banner on the primary screen names the new output.
- `Data\devices.txt` (optional): output IDs, one per line, in the order to cycle. Without it every
  connected output is used, alphabetically.

## Change the shortcuts

Tray menu › **Edit shortcuts…** opens `shortcuts.ini` (in `%LOCALAPPDATA%\Xdeck`, or `Data\` for a
portable copy). Save it and Xdeck uses the new shortcuts right away — no restart, no new build. A
line it cannot use is named in a notification and keeps the shortcut that was in use.

```ini
panel = L1 + Right + Triangle      # open / hide the panel
audio = L1 + Right                 # hold, then flick the right stick to switch the output
audio.next = Ctrl+Alt+F11
audio.previous = Ctrl+Alt+F10
mouse = L1 + Right + Square        # controller as mouse on / off
mouse.speed = 1400                 # pointer pixels per second at full tilt (200 to 6000)
```

- Controller: `+` = held together, `,` = one after another (at most 1.5 s apart), e.g.
  `L1, Right, L1, Triangle`. PlayStation, Xbox and Nintendo names work (`L1`/`LB`, `Triangle`/`Y`,
  `Options`/`Menu`, `Share`/`View`, `Home`, `L3`, …); buttons go by position.
- Keep a button the panel does not use (L1, R1, L3, R3, Share, Home) in a controller shortcut, so
  pressing it does not also move the focus in the panel.
- Keyboard: Ctrl / Alt / Shift / Win + one key. Leave a value empty or write `none` to turn it off.

## Build

```powershell
.\build.ps1                     # build\Xdeck
.\build.ps1 -Zip                # + dist\Xdeck-<version>.zip (portable, with install.ps1)
.\build.ps1 -Install            # + install for the current user
.\build.ps1 -Install -AllUsers  # + install for every user (admin prompt)
```

C# 5 on the .NET Framework 4.8 compiler that ships with Windows (no SDK needed). `src\Xdeck.cs` is
the native host; `ui\` is the panel in HTML/SVG; `lib\` holds the WebView2 SDK DLLs
(`Microsoft.Web.WebView2` 1.0.4258.31 from NuGet). Third-party artwork and its licences:
[`ui/licenses/README.md`](ui/licenses/README.md).

---

## Português

Seu áudio e seus controles, pelo tray. Troque a saída de som e o microfone, ajuste o volume, veja
a bateria de cada controle e teste controles, volante e freio de mão — pelo painel, por atalho ou
pelo próprio controle.

- **Instalar para todos os usuários do PC:** botão direito em `install.ps1` › *Executar com o
  PowerShell* e aprove o pedido de administrador. Vai para `C:\Program Files\Xdeck`, entra no Menu
  Iniciar e abre com o Windows para todo mundo. Só para você: `install.ps1 -CurrentUser`.
  Portátil: dois cliques em `Xdeck.exe` na pasta descompactada.
- **Painel:** clique esquerdo na engrenagem. Fica aberto até você fechar.
- **Trocar a saída:** `Ctrl+Alt+F11` / `Ctrl+Alt+F10`, ou no controle segure **D-pad direita + L1**
  e empurre o **analógico direito** (direita/cima = próxima, esquerda/baixo = anterior).
- **Testar um controle:** clique no cartão dele no painel.
- **Painel pelo controle:** segure **L1 + direcional direita** e aperte **triângulo** (Xbox: LB +
  direita + Y). A mesma combinação esconde o painel e devolve o foco ao jogo. Com o painel aberto, tudo funciona pelo controle: direcional ou analógico esquerdo
  navega, esquerda/direita troca o dispositivo ou ajusta o volume, **A (✕)** escolhe, **X (▢)** muta,
  **B (◯)** volta ou fecha, e segurar **B (◯)** sai do teste de controle.
- **Controle como mouse:** segure **L1 + direcional direita** e aperte **quadrado** (Xbox: LB +
  direita + X) para ligar ou desligar; também tem a chave **Use as mouse** no painel. Analógico
  esquerdo move o cursor (segure L2 / LT para ir devagar), analógico direito rola, **✕/A** clica (segure
  para arrastar), **◯/B** é o botão direito, **▢/X** o do meio, direcional = setas, Options = Enter,
  Share = Esc. Com o painel aberto, o controle volta a mexer no painel.
- **Trocar os atalhos:** menu do tray › **Edit shortcuts…** abre o `shortcuts.ini`. Salve e o Xdeck
  já usa os novos atalhos, sem reiniciar e sem compilar de novo. `+` = segurar junto, `,` = um depois
  do outro (ex.: `panel = L1, Right, L1, Triangle`).

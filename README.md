# Xdeck

Your audio and your controllers, from the tray. Switch the audio output and the microphone, set
their volume, see the battery of each controller, test controllers, wheels and handbrakes, and drive
OBS Studio — from the panel, a hotkey, or the controller in your hands.

🇧🇷 [Português](#português)

Made by **[Skale Club](https://skale.club)**.

## Install

**[Download XdeckSetup.exe](https://github.com/Skale-Club/xdeck/releases/latest/download/XdeckSetup.exe)**
and run it. Pick **Install for everyone on this PC** (`C:\Program Files\Xdeck`, everyone's Start
menu, asks for administrator approval) or **Install only for me** (`%LOCALAPPDATA%\Programs\Xdeck`,
no approval), and whether it starts with Windows. Xdeck then shows up in Start and in
Settings › Apps › Installed apps, which also uninstalls it.

Windows may say *Windows protected your PC* the first time, because the file is not signed with a
paid certificate: click **More info** › **Run anyway**.

It needs Windows 10/11 64-bit with the Microsoft Edge WebView2 Runtime (built into Windows 11) and
.NET Framework 4.8 (built into Windows 10 1903+).

Also on the [releases page](https://github.com/Skale-Club/xdeck/releases/latest): the zip, a portable
copy (run `Xdeck.exe` from the unzipped folder; settings stay in `Data\` beside it). Silent install:
`XdeckSetup.exe /S` (everyone) or `/S /CurrentUser`, add `/NoAutostart` to skip Start with Windows.

**Updates are automatic.** Xdeck checks for a new release a minute after it starts and every six
hours, downloads it in the background and checks its signature. Then a notification, the tray menu
and the panel offer **Restart to update**; quitting Xdeck installs it too. Settings are kept.
Installed for every user (in `C:\Program Files`), Windows asks for administrator approval once per
update. When one is on its way, an **Updates** area at the bottom of the panel shows how it goes (a
progress bar while it downloads) and **Restart to update**; otherwise the panel just shows the
version there. Tray menu › **Check for updates** checks right away.

Uninstall from Settings › Apps (or `"Uninstall Xdeck.exe" /uninstall` in its folder). An installed
Xdeck keeps each user's settings in `%LOCALAPPDATA%\Xdeck`, also after uninstalling. On first run the gear is pinned to the taskbar
instead of hiding under the ^ arrow. Opening Xdeck a second time does nothing; the first copy keeps
running (one per signed-in user).

Xdeck lives in the system tray as a gear:

- **Left click**: open or close the panel. A click anywhere outside it, ×, or Esc closes it too.
- **Right click**: open the panel, next / previous audio output, **Start with Windows**,
  **Edit shortcuts…**, **Check for updates** (or **Restart to update** when one is ready), quit.

## The panel

| Section | What you can do |
|---|---|
| **Audio output** | See the active output and its live level. Click it to list the others and pick one. Volume slider (drag or scroll), mute. |
| **Microphone** | The same for the default microphone; it is also set as the communications device, so calls use it. |
| **Controllers** | Every connected controller with its battery (10 pills, a bolt while charging) or `USB`. Click one to test it. Below them, a circle per mouse, keyboard or other wireless device: its icon, with a ring that fills with the battery. |
| **Updates** | Only while an update is on its way: a progress bar while it downloads, then **Restart to update**. Otherwise just the version. |

## Devices that connect

Every device that connects shows up in a banner at the top left of the screen: a controller, a
Bluetooth device (headphones, mouse, keyboard, phone, speaker) or a USB device (a pen drive, a headset,
a keyboard, a phone), with its battery when it reports one (the pills and the percent, *charging*;
a controller on a cable says `USB`). A mouse or keyboard gets a small one-line banner instead, with
its battery when it has one. Several at once come one after another.

When a wireless device drops to 15%, an alert slides in at the top centre of the screen: a red card
with a pulsing battery, the device and how much is left. It stays six seconds (a click closes it) and
comes back only after the device has charged.

A headset that brings its own audio output and microphone gets one banner, not three. Devices that
come back within a minute (a Bluetooth mouse waking up), devices built into the PC, and everything
that reconnects when the PC wakes up get none.

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

Each controller can be a mouse on its own. Hold **L1 + D-pad right** and press **Square** (Xbox: LB
+ right + X) on a controller to turn it into a mouse, and again to turn it back; or open its page in
the panel (click its card) and flip **Use as mouse**. A banner names the controller and says on or
off, and its card shows **Mouse** while it is one.

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

## OBS Studio from the controller

Start and stop recording or streaming, pause, save a replay, and switch scenes in OBS Studio (28 or
later) from the controller, while you play. A banner says what OBS did.

1. In OBS: **Tools › WebSocket Server Settings › Enable WebSocket server**.
2. If **Enable Authentication** is on there, click **Show Connect Info**, copy the password, and paste
   it after `obs.password =` in `shortcuts.ini` (tray menu › **Edit shortcuts…**).

| Controller (default) | OBS |
|---|---|
| R1 + D-pad right + ✕ (RB + right + A) | Start / stop recording |
| R1 + D-pad right + ▢ (RB + right + X) | Pause / resume the recording |
| R1 + D-pad right + △ (RB + right + Y) | Start / stop streaming |
| R1 + D-pad right + ◯ (RB + right + B) | Save the replay buffer |
| R1 + R3 / R1 + L3 | Next / previous scene (in the order of OBS's Scenes list) |

More in `shortcuts.ini`: `obs.virtualcam` (virtual camera), `obs.scene.<scene name>` (go to that
scene), `obs.mute.<audio source name>` (mute / unmute it) and `obs.hotkey.<OBS hotkey name>` (one of
OBS's own hotkeys, e.g. `OBSBasic.Screenshot`). Xdeck connects only when an OBS shortcut is pressed.
OBS on another PC: set `obs.address` to its IP and port.

## Phone remote

The **Xdeck Remote** app makes a phone (Android or iPhone) a remote for the PC, from anywhere:

1. On the phone, open **[xdeck.skale.club](https://xdeck.skale.club)** and install it: Android
   offers **Install the app**; on iPhone tap **Share › Add to Home Screen**.
2. On the PC, right-click the Xdeck gear in the tray › **Phone remote** › **Pair a phone…**: the
   panel shows a 6-digit code.
3. Type the 6-digit code from the panel in the app. That is all: from then on the app connects by
   itself. Several PCs can be paired (the PC name at the top switches between them).

The app makes the phone:

| Tab | What it does |
|---|---|
| **Mouse** | Touchpad: move, tap = click, two fingers = scroll, two-finger tap = right click, hold = drag; Left / Middle / Right buttons |
| **Keyboard** | What you type goes to the PC (any language, autocorrect too); Ctrl / Alt / Shift / Win, Esc, Tab, arrows, Alt+Tab, copy / paste |
| **Media** | Play / pause, previous, next, volume, mute, fullscreen |
| **Audio** | Switch the audio output, volume and mute |
| **OBS** | Record, pause, stream, save replay, scenes, virtual camera |

Phone and PC meet through a relay at xdeck.skale.club (a Cloudflare Worker, `remote/`), so the phone
works on mobile data too and the PC opens no port. Everything between them is encrypted end to end
(ECDH P-256 keys exchanged at pairing, then AES + HMAC): the relay only passes it on. The same tray
menu lists the paired phones (● connected now); click one to remove it.

Deploy the relay and the app (from a normal terminal, logged in with `wrangler login`):

```powershell
cd remote; wrangler deploy
```

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

obs.address = 127.0.0.1:4455       # OBS's WebSocket server
obs.password =                     # its password, if it asks for one
obs.record = R1 + Right + Cross    # start / stop recording
obs.pause = R1 + Right + Square    # pause / resume the recording
obs.stream = R1 + Right + Triangle # start / stop streaming
obs.replay = R1 + Right + Circle   # save the replay buffer
obs.scene.next = R1 + R3           # next / previous scene
obs.scene.previous = R1 + L3
obs.scene.Gameplay = R1 + Up       # that scene (any scene name after obs.scene.)
obs.mute.Mic/Aux = R1 + Down       # mute / unmute that audio source
```

- Controller: `+` = held together, `,` = one after another (at most 1.5 s apart), e.g.
  `L1, Right, L1, Triangle`. PlayStation, Xbox and Nintendo names work (`L1`/`LB`, `Triangle`/`Y`,
  `Options`/`Menu`, `Share`/`View`, `Home`, `L3`, …); buttons go by position.
- Keep a button the panel does not use (L1, R1, L3, R3, Share, Home) in a controller shortcut, so
  pressing it does not also move the focus in the panel.
- Keyboard: Ctrl / Alt / Shift / Win + one key. Leave a value empty or write `none` to turn it off.

## Build

```powershell
.\build.ps1 -Release            # zip + sign + latest.json + GitHub release v<VERSION> (from a clean main)
.\build.ps1                     # build\Xdeck
.\build.ps1 -Zip                # + dist\XdeckSetup.exe and dist\Xdeck-<version>.zip (portable)
.\build.ps1 -Install            # + install for the current user (XdeckSetup.exe /S /CurrentUser)
.\build.ps1 -Install -AllUsers  # + install for every user (admin prompt)
```

C# 5 on the .NET Framework 4.8 compiler that ships with Windows (no SDK needed). `src\Xdeck.cs` is
the native host; `ui\` is the panel in HTML/SVG; `tools\MakeIcon.cs` draws the app icon at build time; `lib\` holds the WebView2 SDK DLLs
(`Microsoft.Web.WebView2` 1.0.4258.31 from NuGet). Third-party artwork and its licences:
[`ui/licenses/README.md`](ui/licenses/README.md).

---

## Português

Seu áudio e seus controles, pelo tray. Troque a saída de som e o microfone, ajuste o volume, veja
a bateria de cada controle e teste controles, volante e freio de mão — pelo painel, por atalho ou
pelo próprio controle.

- **Instalar:** baixe o **[XdeckSetup.exe](https://github.com/Skale-Club/xdeck/releases/latest/download/XdeckSetup.exe)**
  e abra. Escolha **para todos do PC** (pede aprovação de administrador) ou **só para mim**, e se abre
  com o Windows. Ele entra no Menu Iniciar e em Configurações › Aplicativos, por onde também
  desinstala. Se o Windows disser *O Windows protegeu o computador*, clique em **Mais informações** ›
  **Executar assim mesmo** (o arquivo não tem certificado pago). Portátil: o zip da release.
- **Atualizações:** automáticas. O Xdeck procura versão nova ao abrir e a cada 6 horas, baixa
  sozinho e avisa **Restart to update** (notificação, menu do tray e painel); fechar o Xdeck também
  instala. Instalado para todos os usuários, o Windows pede aprovação de administrador uma vez por
  atualização. As configurações ficam. Quando chega uma versão nova, o rodapé do painel mostra
  **Updates** com a barra de progresso e **Restart to update**; fora isso, só a versão.
- **Controle pelo celular (tipo Unified Remote):** no celular, abra **xdeck.skale.club** e instale o
  app (Android: **Install the app**; iPhone: **Compartilhar › Adicionar à Tela de Início**). No PC,
  botão direito na engrenagem › **Phone remote** › **Pair a phone…** (o painel mostra o código); digite o código de 6 dígitos no app,
  uma vez só. Depois ele conecta sozinho, de qualquer lugar (até no 4G). O celular vira touchpad,
  teclado, teclas de mídia, troca de saída de áudio e volume, e botões do OBS. Tudo vai criptografado
  de ponta a ponta. O mesmo menu lista os celulares pareados; clique num para remover.
- **Mouse, teclado e outros aparelhos sem fio:** aparecem no painel como círculos embaixo dos
  controles: o ícone no meio e um anel em volta que enche com a bateria (vermelho quando está baixa).
- **Painel:** clique esquerdo na engrenagem. Clicar fora dele, no × ou Esc fecha.
- **Trocar a saída:** `Ctrl+Alt+F11` / `Ctrl+Alt+F10`, ou no controle segure **D-pad direita + L1**
  e empurre o **analógico direito** (direita/cima = próxima, esquerda/baixo = anterior).
- **Dispositivo conectado:** qualquer dispositivo que conectar aparece num aviso no canto da tela —
  controle, Bluetooth (fone, mouse, teclado, celular, caixa de som) ou USB (pendrive, headset,
  teclado, celular) — com a bateria quando ele informa (barrinhas e porcentagem, *charging*). Mouse
  e teclado aparecem de forma discreta, numa linha só, com a bateria se tiverem. Quando um
  dispositivo sem fio chega a 15%, um alerta vermelho desliza no topo da tela, com a bateria pulsando,
  o nome e quanto resta (clique para fechar); só volta depois de carregar.
  Headset com saída de áudio e microfone gera um aviso só; dispositivos internos do PC, os que voltam
  em menos de um minuto e os que reconectam quando o PC acorda não geram aviso.
- **Testar um controle:** clique no cartão dele no painel.
- **Painel pelo controle:** segure **L1 + direcional direita** e aperte **triângulo** (Xbox: LB +
  direita + Y). A mesma combinação esconde o painel e devolve o foco ao jogo. Com o painel aberto, tudo funciona pelo controle: direcional ou analógico esquerdo
  navega, esquerda/direita troca o dispositivo ou ajusta o volume, **A (✕)** escolhe, **X (▢)** muta,
  **B (◯)** volta ou fecha, e segurar **B (◯)** sai do teste de controle.
- **Controle como mouse:** segure **L1 + direcional direita** e aperte **quadrado** (Xbox: LB +
  direita + X) no controle que vai virar mouse, para ligar ou desligar; ou abra a tela dele no painel
  (clique no cartão) e use a chave **Use as mouse**. Cada controle liga o seu. Analógico
  esquerdo move o cursor (segure L2 / LT para ir devagar), analógico direito rola, **✕/A** clica (segure
  para arrastar), **◯/B** é o botão direito, **▢/X** o do meio, direcional = setas, Options = Enter,
  Share = Esc. Com o painel aberto, o controle volta a mexer no painel.
- **OBS Studio pelo controle:** no OBS, **Ferramentas › Configurações do servidor WebSocket ›
  Ativar servidor WebSocket**; se ali pedir senha, copie em **Mostrar informações de conexão** e cole
  em `obs.password =` no `shortcuts.ini`. Atalhos padrão: **R1 + direcional direita + ✕** grava/para,
  **+ ▢** pausa, **+ △** transmite, **+ ◯** salva o replay; **R1 + R3 / R1 + L3** próxima / cena
  anterior. Também dá para ir a uma cena (`obs.scene.Nome = …`), mutar uma fonte de áudio
  (`obs.mute.Mic/Aux = …`), ligar a câmera virtual (`obs.virtualcam`) ou apertar um atalho do próprio
  OBS (`obs.hotkey.…`). Um aviso na tela diz o que o OBS fez.
- **Trocar os atalhos:** menu do tray › **Edit shortcuts…** abre o `shortcuts.ini`. Salve e o Xdeck
  já usa os novos atalhos, sem reiniciar e sem compilar de novo. `+` = segurar junto, `,` = um depois
  do outro (ex.: `panel = L1, Right, L1, Triangle`).

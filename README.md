# Xdeck

Your audio and your controllers, from the tray. Switch the audio output and the microphone, set
their volume, see the battery of each controller, and test controllers, wheels and handbrakes —
from the panel, a hotkey, or the controller in your hands.

🇧🇷 [Português](#português)

Made by **[Skale Club](https://skale.club)**.

## Run it

Double-click `Xdeck.exe`. No installer. It needs Windows 10/11 64-bit with the Microsoft Edge
WebView2 Runtime (built into Windows 11) and .NET Framework 4.8 (built into Windows 10 1903+).
Everything Xdeck writes lives in `Data\` next to the exe, so the folder can move between PCs.
Opening it a second time does nothing; the first copy keeps running.

Xdeck lives in the system tray as a gear:

- **Left click**: open or close the panel. It stays open until you close it (×, Esc or another click).
- **Right click**: open the panel, next / previous audio output, **Start with Windows**, quit.

## The panel

| Section | What you can do |
|---|---|
| **Audio output** | See the active output and its live level. Click it to list the others and pick one. Volume slider (drag or scroll), mute. |
| **Microphone** | The same for the default microphone; it is also set as the communications device, so calls use it. |
| **Controllers** | Every connected controller with its battery (10 pills, a bolt while charging) or `USB`. Click one to test it. |

The controller test shows the real controller: Xbox, DualSense, Pro Controller and Joy-Cons light up
button by button; the G29 shows the wheel angle and pedals; the handbrake shows the lever.

## Switch the audio output without the panel

- `Ctrl+Alt+F11` / `Ctrl+Alt+F10`: next / previous output.
- On a controller: hold **D-pad right + L1** (LB / L) and flick the **right stick** right or up for
  the next output, left or down for the previous one.
- A banner on the primary screen names the new output.
- `Data\devices.txt` (optional): output IDs, one per line, in the order to cycle. Without it every
  connected output is used, alphabetically.

## Build

```powershell
.\build.ps1            # build\Xdeck, the portable folder
.\build.ps1 -Zip       # + dist\Xdeck-<version>.zip
.\build.ps1 -Install   # + copy to %LOCALAPPDATA%\Programs\Xdeck and restart it (keeps Data)
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

- **Rodar:** dois cliques em `Xdeck.exe`, sem instalador. Tudo que ele grava fica em `Data\`, ao
  lado do exe. Botão direito na engrenagem › **Start with Windows** para abrir com o Windows.
- **Painel:** clique esquerdo na engrenagem. Fica aberto até você fechar.
- **Trocar a saída:** `Ctrl+Alt+F11` / `Ctrl+Alt+F10`, ou no controle segure **D-pad direita + L1**
  e empurre o **analógico direito** (direita/cima = próxima, esquerda/baixo = anterior).
- **Testar um controle:** clique no cartão dele no painel.

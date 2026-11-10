# Install Xdeck from this folder (the build output or the unzipped release).
#   .\install.ps1                 for every user of this PC: C:\Program Files\Xdeck, Start menu and
#                                 Start-with-Windows for everyone (asks for administrator approval)
#   .\install.ps1 -CurrentUser    only for you: %LOCALAPPDATA%\Programs\Xdeck, no admin needed
#   .\install.ps1 -NoAutostart    either of the above, without starting with Windows
#   .\install.ps1 -Uninstall [-CurrentUser]
# Each user's settings live in %LOCALAPPDATA%\Xdeck and are kept on update and uninstall.
[CmdletBinding()]
param([switch]$CurrentUser, [switch]$NoAutostart, [switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $CurrentUser -and -not $isAdmin) {
    # re-run elevated (UAC prompt), passing the same switches
    $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($NoAutostart) { $args += '-NoAutostart' }
    if ($Uninstall) { $args += '-Uninstall' }
    $p = Start-Process powershell -Verb RunAs -ArgumentList $args -Wait -PassThru
    exit $p.ExitCode
}

if ($CurrentUser) {
    $target = Join-Path $env:LOCALAPPDATA 'Programs\Xdeck'
    $menu = [Environment]::GetFolderPath('Programs')
    $startup = [Environment]::GetFolderPath('Startup')
} else {
    $target = Join-Path $env:ProgramFiles 'Xdeck'
    $menu = [Environment]::GetFolderPath('CommonPrograms')
    $startup = [Environment]::GetFolderPath('CommonStartup')
}

function Shortcut($path, $exe) {
    $ws = New-Object -ComObject WScript.Shell
    $s = $ws.CreateShortcut($path); $s.TargetPath = $exe; $s.WorkingDirectory = Split-Path $exe
    $s.Description = 'Xdeck - audio and controllers'; $s.Save()
}

# stop every running copy (all users when elevated) so the files can be replaced
Get-Process Xdeck -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

if ($Uninstall) {
    Remove-Item (Join-Path $menu 'Xdeck.lnk'), (Join-Path $startup 'Xdeck.lnk') -ErrorAction SilentlyContinue
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Write-Host "Xdeck removed from $target (each user's settings in %LOCALAPPDATA%\Xdeck were kept)."
    exit 0
}

if (-not (Test-Path (Join-Path $src 'Xdeck.exe'))) { throw "Xdeck.exe not found next to install.ps1 ($src)" }
New-Item -ItemType Directory $target -Force | Out-Null
Get-ChildItem $target -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
# copy the app, but not a portable Data folder: installed copies keep data per user
Get-ChildItem $src | Where-Object { $_.Name -notin 'Data', 'install.ps1' } | Copy-Item -Destination $target -Recurse
Copy-Item (Join-Path $src 'install.ps1') $target   # kept for uninstall

$exe = Join-Path $target 'Xdeck.exe'
Shortcut (Join-Path $menu 'Xdeck.lnk') $exe
if ($NoAutostart) { Remove-Item (Join-Path $startup 'Xdeck.lnk') -ErrorAction SilentlyContinue }
else { Shortcut (Join-Path $startup 'Xdeck.lnk') $exe }

# start it for the signed-in user (through Explorer, so it does not run elevated)
Start-Process explorer.exe -ArgumentList "`"$exe`""
Write-Host "Xdeck installed in $target ($(if ($CurrentUser) { 'current user' } else { 'all users' }))."

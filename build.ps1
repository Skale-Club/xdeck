# Build Xdeck into build\Xdeck, optionally zip it and/or install it on this PC.
#   .\build.ps1                     compile + assemble build\Xdeck
#   .\build.ps1 -Zip                ... and dist\Xdeck-<version>.zip (portable: ships a Data folder)
#   .\build.ps1 -Install            ... and install for the current user (install.ps1 -CurrentUser)
#   .\build.ps1 -Install -AllUsers  ... and install for every user of this PC (asks for admin)
[CmdletBinding()]
param([switch]$Zip, [switch]$Install, [switch]$AllUsers)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$out = Join-Path $root 'build\Xdeck'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw ".NET Framework 4.x compiler not found: $csc" }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null
$lib = Join-Path $root 'lib'

# the app icon (tools\MakeIcon.cs draws it), embedded in Xdeck.exe for Explorer, Start and shortcuts
$tmp = Join-Path $root 'build\tmp'
New-Item -ItemType Directory $tmp -Force | Out-Null
& $csc /nologo /target:exe /codepage:65001 /r:System.Drawing.dll "/out:$tmp\MakeIcon.exe" (Join-Path $root 'tools\MakeIcon.cs')
if ($LASTEXITCODE -ne 0) { throw "Icon tool build failed ($LASTEXITCODE)" }
& "$tmp\MakeIcon.exe" "$tmp\Xdeck.ico"
if ($LASTEXITCODE -ne 0) { throw "Icon failed ($LASTEXITCODE)" }

& $csc /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ `
    "/win32manifest:$root\src\app.manifest" "/win32icon:$tmp\Xdeck.ico" `
    "/r:$lib\Microsoft.Web.WebView2.Core.dll" "/r:$lib\Microsoft.Web.WebView2.WinForms.dll" /r:System.Web.Extensions.dll `
    "/out:$out\Xdeck.exe" (Join-Path $root 'src\Xdeck.cs')
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Copy-Item "$lib\*.dll" $out
Copy-Item (Join-Path $root 'ui') (Join-Path $out 'ui') -Recurse
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'CHANGELOG.md'), (Join-Path $root 'install.ps1') $out
Write-Host "Built Xdeck $version -> $out"

if ($Zip) {
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory $dist -Force | Out-Null
    $zipFile = Join-Path $dist "Xdeck-$version.zip"
    if (Test-Path $zipFile) { Remove-Item $zipFile }
    # the zip runs portable straight away (Data next to the exe); install.ps1 installs without it
    $stage = Join-Path $env:TEMP "xdeck-zip-$PID\Xdeck"
    Copy-Item $out $stage -Recurse
    New-Item -ItemType Directory (Join-Path $stage 'Data') | Out-Null
    Set-Content (Join-Path $stage 'Data\devices.txt') -Encoding utf8 -Value @(
        '# Xdeck: audio outputs in the order Ctrl+Alt+F11 / the controller combo cycles through them.',
        '# One Windows endpoint ID per line. Leave empty to use every connected output, alphabetically.')
    Compress-Archive -Path $stage -DestinationPath $zipFile
    Remove-Item (Split-Path $stage) -Recurse -Force
    Write-Host "Zipped -> $zipFile ($([int]((Get-Item $zipFile).Length / 1KB)) KB)"
}

if ($Install) {
    $installer = Join-Path $out 'install.ps1'
    if ($AllUsers) { & $installer } else { & $installer -CurrentUser }
}

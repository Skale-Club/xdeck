# Renders the controller silhouettes the banner draws (src\res\pad-<kind>.png) from the artwork in
# ui\pads, once, with headless Microsoft Edge. Run it again only when that artwork changes; the PNGs
# are committed, so a normal build does not need Edge.
#   .\tools\Render-PadIcons.ps1
# Each PNG is a mask: white where the controller's body is, see-through where the artwork has its
# sticks, buttons and D-pad, so the banner can tint it like its other icons.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }

$icons = [ordered]@{
    xbox      = 'pads/xbox-icon.svg'
    dualsense = 'pads/dualsense-icon.svg'
    switchpro = 'pads/pro.svg'
    joycon    = 'pads/joycon.svg'
    generic   = 'pads/generic.svg'
}
$tmp = Join-Path $root 'build\tmp\padicons'
New-Item -ItemType Directory $tmp -Force | Out-Null
$out = Join-Path $root 'src\res'
New-Item -ItemType Directory $out -Force | Out-Null

# the mask step lives in MakeIcon (MakeIcon --mask in.png out.png)
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /codepage:65001 /r:System.Drawing.dll "/out:$tmp\MakeIcon.exe" (Join-Path $root 'tools\MakeIcon.cs')
if ($LASTEXITCODE -ne 0) { throw "MakeIcon build failed ($LASTEXITCODE)" }

$ui = (Join-Path $root 'ui').Replace('\', '/')
foreach ($kind in $icons.Keys) {
    $html = Join-Path $tmp "$kind.html"
    Set-Content $html -Encoding UTF8 -Value ("<html><body style='margin:0;background:transparent'>" +
        "<img src='file:///$ui/$($icons[$kind])' style='display:block;width:256px;height:256px;object-fit:contain'></body></html>")
    $png = Join-Path $tmp "$kind.png"
    if (Test-Path $png) { Remove-Item $png }
    $p = Start-Process $edge -ArgumentList @('--headless=new', '--disable-gpu', '--allow-file-access-from-files', '--hide-scrollbars',
        '--default-background-color=00000000', '--window-size=256,256', "--screenshot=$png", "file:///$($html.Replace('\', '/'))") -Wait -PassThru -WindowStyle Hidden
    if (-not (Test-Path $png)) { throw "Edge did not render $kind" }
    & "$tmp\MakeIcon.exe" --mask $png (Join-Path $out "pad-$kind.png")
    if ($LASTEXITCODE -ne 0) { throw "mask failed for $kind" }
    Write-Host "src\res\pad-$kind.png"
}

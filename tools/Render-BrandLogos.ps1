# Renders the brand logos (ui\brands\mask\<brand>.png: the banner draws them, inside the exe as
# brand-<brand>.png) from the logos in ui\brands, once, with headless Microsoft Edge. Run it again
# only when a logo is added or changed; the PNGs are committed, so a normal build does not need Edge.
#   .\tools\Render-BrandLogos.ps1
# Each PNG is a mask: white where the logo is, see-through elsewhere, trimmed tight to the logo,
# so the banner can tint it and size it by its own width.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }

$tmp = Join-Path $root 'build\tmp\brandlogos'
New-Item -ItemType Directory $tmp -Force | Out-Null
$out = Join-Path $root 'ui\brands\mask'
New-Item -ItemType Directory $out -Force | Out-Null

# the mask step lives in MakeIcon (MakeIcon --logo in.png out.png)
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /codepage:65001 /r:System.Drawing.dll "/out:$tmp\MakeIcon.exe" (Join-Path $root 'tools\MakeIcon.cs')
if ($LASTEXITCODE -ne 0) { throw "MakeIcon build failed ($LASTEXITCODE)" }

$brands = Get-ChildItem (Join-Path $root 'ui\brands') -File |
    Where-Object { ($_.Extension -eq '.svg' -or $_.Extension -eq '.png') -and $_.BaseName -ne 'xdeck-app' }
$ui = (Join-Path $root 'ui').Replace('\', '/')
foreach ($b in $brands) {
    $name = $b.BaseName
    $html = Join-Path $tmp "$name.html"
    Set-Content $html -Encoding UTF8 -Value ("<html><body style='margin:0;background:transparent'>" +
        "<img src='file:///$ui/brands/$($b.Name)' style='display:block;width:256px;height:256px;object-fit:contain'></body></html>")
    $png = Join-Path $tmp "$name.png"
    if (Test-Path $png) { Remove-Item $png }
    Start-Process $edge -ArgumentList @('--headless=new', '--disable-gpu', '--allow-file-access-from-files', '--hide-scrollbars',
        "--user-data-dir=$tmp\edge", '--default-background-color=00000000', '--window-size=256,256', "--screenshot=$png",
        "file:///$($html.Replace('\', '/'))") -Wait -WindowStyle Hidden | Out-Null
    if (-not (Test-Path $png)) { throw "Edge did not render $name" }
    & "$tmp\MakeIcon.exe" --logo $png (Join-Path $out "$name.png")
    if ($LASTEXITCODE -ne 0) { throw "mask failed for $name" }
    Write-Host "ui\brands\mask\$name.png"
}

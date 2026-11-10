# Build Xdeck into build\Xdeck (portable folder), optionally zip it and/or install it locally.
#   .\build.ps1            compile + assemble build\Xdeck
#   .\build.ps1 -Zip       ... and dist\Xdeck-<version>.zip
#   .\build.ps1 -Install   ... and copy to %LOCALAPPDATA%\Programs\Xdeck (keeps its Data), restart it
[CmdletBinding()]
param([switch]$Zip, [switch]$Install)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
$out = Join-Path $root 'build\Xdeck'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw ".NET Framework 4.x compiler not found: $csc" }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null
$lib = Join-Path $root 'lib'
& $csc /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ `
    "/r:$lib\Microsoft.Web.WebView2.Core.dll" "/r:$lib\Microsoft.Web.WebView2.WinForms.dll" /r:System.Web.Extensions.dll `
    "/out:$out\Xdeck.exe" (Join-Path $root 'src\Xdeck.cs')
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Copy-Item "$lib\*.dll" $out
Copy-Item (Join-Path $root 'ui') (Join-Path $out 'ui') -Recurse
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'CHANGELOG.md') $out
Write-Host "Built Xdeck $version -> $out"

if ($Zip) {
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory $dist -Force | Out-Null
    $zipFile = Join-Path $dist "Xdeck-$version.zip"
    if (Test-Path $zipFile) { Remove-Item $zipFile }
    Compress-Archive -Path $out -DestinationPath $zipFile
    Write-Host "Zipped -> $zipFile ($([int]((Get-Item $zipFile).Length / 1KB)) KB)"
}

if ($Install) {
    $target = Join-Path $env:LOCALAPPDATA 'Programs\Xdeck'
    Get-Process Xdeck -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    New-Item -ItemType Directory $target -Force | Out-Null
    # replace everything except Data (settings + WebView2 profile)
    Get-ChildItem $target | Where-Object { $_.Name -ne 'Data' } | Remove-Item -Recurse -Force
    Copy-Item "$out\*" $target -Recurse
    # start outside this shell's job so it survives the build window
    Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "`"$target\Xdeck.exe`"" } | Out-Null
    Write-Host "Installed and started -> $target"
}

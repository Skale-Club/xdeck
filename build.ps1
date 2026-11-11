# Build Xdeck into build\Xdeck, optionally zip it and/or install it on this PC.
#   .\build.ps1                     compile + assemble build\Xdeck and build\XdeckSetup.exe
#   .\build.ps1 -Zip                ... and dist\XdeckSetup.exe + dist\Xdeck-<version>.zip (portable)
#   .\build.ps1 -Install            ... and install for the current user (XdeckSetup.exe /S /CurrentUser)
#   .\build.ps1 -Install -AllUsers  ... and install for every user of this PC (asks for admin)
#   .\build.ps1 -Release            ... zip, sign it, write latest.json and publish GitHub release
#                                   v<VERSION>; every installed Xdeck picks it up by itself
#                                   (needs a clean tree on main, gh, and the update key from
#                                   tools\New-UpdateKey.ps1: $env:XDECK_UPDATE_KEY or
#                                   %APPDATA%\Skale\xdeck-update-key.xml)
[CmdletBinding()]
param([switch]$Zip, [switch]$Install, [switch]$AllUsers, [switch]$Release)
if ($Release) { $Zip = $true }
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

# the version from VERSION, stamped into the exe (the updater compares it; Explorer shows it)
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION must be major.minor.patch, not '$version'" }
Set-Content "$tmp\AssemblyInfo.cs" -Encoding utf8 -Value @(
    'using System.Reflection;',
    "[assembly: AssemblyTitle(""Xdeck"")] [assembly: AssemblyProduct(""Xdeck"")] [assembly: AssemblyCompany(""Skale Club"")]",
    "[assembly: AssemblyVersion(""$version.0"")] [assembly: AssemblyFileVersion(""$version.0"")] [assembly: AssemblyInformationalVersion(""$version"")]")

# the controller silhouettes the banner draws (tools\Render-PadIcons.ps1 makes them)
$resources = Get-ChildItem (Join-Path $root 'src\res') -Filter 'pad-*.png' | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }

& $csc /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ $resources `
    "/win32manifest:$root\src\app.manifest" "/win32icon:$tmp\Xdeck.ico" `
    "/r:$lib\Microsoft.Web.WebView2.Core.dll" "/r:$lib\Microsoft.Web.WebView2.WinForms.dll" /r:System.Web.Extensions.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Security.dll /r:System.Core.dll `
    "/out:$out\Xdeck.exe" (Join-Path $root 'src\Xdeck.cs') "$tmp\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Copy-Item "$lib\*.dll" $out
Copy-Item (Join-Path $root 'ui') (Join-Path $out 'ui') -Recurse
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'CHANGELOG.md'), (Join-Path $root 'install.ps1') $out
Write-Host "Built Xdeck $version -> $out"

# XdeckSetup.exe: the installer, with build\Xdeck inside (setup\Setup.cs)
$payload = Join-Path $tmp 'payload.zip'
if (Test-Path $payload) { Remove-Item $payload }
Compress-Archive -Path $out -DestinationPath $payload
Set-Content "$tmp\SetupInfo.cs" -Encoding utf8 -Value @(
    'using System.Reflection;',
    "[assembly: AssemblyTitle(""Xdeck Setup"")] [assembly: AssemblyProduct(""Xdeck"")] [assembly: AssemblyCompany(""Skale Club"")]",
    "[assembly: AssemblyVersion(""$version.0"")] [assembly: AssemblyFileVersion(""$version.0"")] [assembly: AssemblyInformationalVersion(""$version"")]")
$setupExe = Join-Path $root 'build\XdeckSetup.exe'
& $csc /nologo /target:winexe /platform:anycpu /codepage:65001 /optimize+ `
    "/win32manifest:$root\setup\setup.manifest" "/win32icon:$tmp\Xdeck.ico" `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/resource:$payload,Xdeck.payload.zip" "/resource:$tmp\Xdeck.ico,Xdeck.ico" `
    "/out:$setupExe" (Join-Path $root 'setup\Setup.cs') "$tmp\SetupInfo.cs"
if ($LASTEXITCODE -ne 0) { throw "Setup build failed ($LASTEXITCODE)" }
Write-Host "Built XdeckSetup.exe ($([int]((Get-Item $setupExe).Length / 1KB)) KB)"

if ($Zip) {
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory $dist -Force | Out-Null
    Copy-Item $setupExe (Join-Path $dist 'XdeckSetup.exe') -Force
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

if ($Release) {
    # Only what is committed on main ships, so the release matches its tag.
    $branch = (git -C $root rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne 'main') { throw "Release from main, not '$branch'." }
    if (git -C $root status --porcelain) { throw 'Commit or stash your changes first: the release must match main.' }
    $tag = "v$version"
    cmd /c "gh release view $tag --repo Skale-Club/xdeck >nul 2>&1"   # through cmd: PowerShell 5.1 turns gh's stderr into an error
    if ($LASTEXITCODE -eq 0) { throw "Release $tag already exists: raise VERSION first." }
    $keyPath = if ($env:XDECK_UPDATE_KEY) { $env:XDECK_UPDATE_KEY } else { Join-Path $env:APPDATA 'Skale\xdeck-update-key.xml' }
    if (-not (Test-Path $keyPath)) { throw "Update key not found at $keyPath (tools\New-UpdateKey.ps1 makes one)." }

    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    $rsa.PersistKeyInCsp = $false
    $rsa.FromXmlString([IO.File]::ReadAllText($keyPath))
    $bytes = [IO.File]::ReadAllBytes($zipFile)
    $signature = [Convert]::ToBase64String($rsa.SignData($bytes, 'SHA256'))

    # this version's CHANGELOG section is the release notes
    $log = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
    $m = [regex]::Match($log, "(?ms)^## $([regex]::Escape($version))\b[^\n]*\n(.*?)(?=^## |\z)")
    $notes = if ($m.Success) { $m.Groups[1].Value.Trim() } else { "Xdeck $version" }

    $manifest = [ordered]@{
        version   = $version
        url       = "https://github.com/Skale-Club/xdeck/releases/download/$tag/Xdeck-$version.zip"
        signature = $signature
        notes     = $notes
    }
    $latest = Join-Path $dist 'latest.json'
    [IO.File]::WriteAllText($latest, ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
    $notesFile = Join-Path $tmp 'notes.md'
    [IO.File]::WriteAllText($notesFile, $notes + "`n`n**Install:** download **XdeckSetup.exe** and run it. Installed copies update themselves.`n`nThe zip is the portable copy (run Xdeck.exe from the unzipped folder), and what installed copies download to update.", (New-Object Text.UTF8Encoding $false))
    gh release create $tag (Join-Path $dist 'XdeckSetup.exe') $zipFile $latest --repo Skale-Club/xdeck --target main --title "Xdeck $version" --notes-file $notesFile
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed ($LASTEXITCODE)" }
    Write-Host "Released ${tag}: every installed Xdeck picks it up within six hours, or at its next start."
}

if ($Install) {
    # the same installer users run, silently (it asks for administrator approval for -AllUsers)
    $p = Start-Process $setupExe -ArgumentList $(if ($AllUsers) { '/S' } else { '/S /CurrentUser' }) -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Setup failed ($($p.ExitCode))" }
    Write-Host "Installed Xdeck $version ($(if ($AllUsers) { 'all users' } else { 'current user' }))."
}

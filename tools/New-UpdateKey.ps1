# Creates the Xdeck update signing key, once. build.ps1 -Release signs each release zip with the
# private half; Xdeck.exe carries the public half (Updater.PublicKey in src\Xdeck.cs) and refuses
# any download that does not match it.
#   .\tools\New-UpdateKey.ps1            writes the private key to $env:XDECK_UPDATE_KEY, or
#                                        %APPDATA%\Skale\xdeck-update-key.xml, and prints the public key
# Keep the private key out of the repository and back it up: without it no installed copy accepts
# a new release, and every PC would need a manual reinstall with a new key.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$path = if ($env:XDECK_UPDATE_KEY) { $env:XDECK_UPDATE_KEY } else { Join-Path $env:APPDATA 'Skale\xdeck-update-key.xml' }
if (Test-Path $path) { throw "A key already exists at $path; delete it first only if you mean to replace it." }
New-Item -ItemType Directory (Split-Path $path) -Force | Out-Null
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider 3072
$rsa.PersistKeyInCsp = $false
[IO.File]::WriteAllText($path, $rsa.ToXmlString($true))
Write-Host "Private key: $path"
Write-Host "Public key (Updater.PublicKey):"
Write-Output $rsa.ToXmlString($false)

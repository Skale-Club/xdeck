# Publishing from another Windows computer

The installed updater accepts only releases signed with the existing Xdeck signing key.
Copy the original key through an authenticated file-transfer channel from the release computer:

`%APPDATA%\Skale\xdeck-update-key.xml`

Place it at the same path on the new computer. If the original computer uses a custom
`XDECK_UPDATE_KEY` path, use that file instead. Keep the private key outside this repository.
Do not run `New-UpdateKey.ps1` when adding another release computer: a new key would not match
the public key trusted by installed clients. Do not paste private key contents into chat or logs.

Run releases from a normal PowerShell terminal so Microsoft Store app file redirection does not
hide the key. Git, GitHub CLI (authenticated for this repository), .NET Framework 4.8 and the
repository's checked-in WebView2 libraries are required. Node is used only for the panel tests.

After the owner authorizes publication, validate the change on dev, update VERSION and CHANGELOG,
commit, and run:

```powershell
node --test tests/panel.test.cjs
node --check ui/app.js
git diff --check
git switch main
git merge --ff-only dev
git push origin main
.\build.ps1 -Release
```

Use a machine with sufficient available memory. Verify the new GitHub release includes
`XdeckSetup.exe`, the versioned portable ZIP and `latest.json`. Verify the downloaded ZIP against
the manifest signature using `Updater.Verify` before reporting the automatic update as published.

As of 2026-11-11, the Surface checkout and GitHub authentication are ready. The original key was
created on the owner's home computer and still needs to be transferred to the Surface.

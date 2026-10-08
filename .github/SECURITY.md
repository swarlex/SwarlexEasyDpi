# Security

## What SwarlexEasyDpi does on your computer

- Runs **as administrator**: the WinDivert packet driver that GoodbyeDPI uses needs it. On the first run it
  copies itself to `C:\Program Files\SwarlexEasyDpi` (writable by administrators only) and runs from there.
- Starts one program: its embedded `goodbyedpi.exe` from `C:\Program Files\SwarlexEasyDpi\bin`, hidden,
  tied to the app with a job object so it never outlives it. Besides that it only runs `schtasks.exe`
  (*Start with Windows*), `notepad.exe` (*Diagnostics*), and itself (relaunch from Program Files on the
  first run, restart after an update) — by full path, without the shell.
- So that two copies of WinDivert do not fight, it stops a running `GoodbyeDPI` service and any other
  `goodbyedpi.exe` process when it starts.
- Writes only to `C:\Program Files\SwarlexEasyDpi` (engine, settings `ayarlar.ini`, log `kayit.txt`) and,
  when enabled, the scheduled task *SwarlexEasyDpi* (start at logon). Upgrading from a pre-release build
  that had changed the Windows proxy setting restores that setting once.
- **DNS:** the first settings tried leave DNS alone. If only a setting with DNS redirection works on your
  network, all plain DNS queries go to that resolver (Quad9 or Cloudflare); its name is shown in the panel
  and the tray tooltip.
- Network access: a request to `https://discord.com/api/v10/gateway` to verify the connection, and the
  update check below. No telemetry.

## Updates

- The app reads `https://github.com/swarlex/SwarlexEasyDpi/releases/latest` (a redirect to the latest tag).
- A download whose address is not `https://github.com/swarlex/SwarlexEasyDpi/releases/download/...` is refused;
  every redirect must stay on HTTPS and on github.com / *.githubusercontent.com, and the download is capped at 20 MB.
- The exe must start with `MZ` and match the SHA-256 in `SwarlexEasyDpi.exe.sha256` from the same release,
  otherwise it is deleted. Updates are applied only when you click *Update*.
- The checksum comes from the same release: it protects against damaged downloads, not against a
  compromised GitHub account. Release files are built on the maintainer's PC (the exe is not code-signed);
  the CI build on every push shows the published source compiles and passes its tests.

## Known limits

- Like any program that runs as administrator, it cannot fully protect itself from malware already running
  under your user account: on a first run from a folder such as *Downloads*, .NET reads files placed next to
  the exe (e.g. `SwarlexEasyDpi.exe.config`) before the app's own code runs. Put the exe in a clean folder
  before the first run.

## Reporting a vulnerability

Please report security problems privately through
[GitHub Security Advisories](https://github.com/swarlex/SwarlexEasyDpi/security/advisories/new).

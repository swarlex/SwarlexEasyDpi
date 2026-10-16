# Security

## What SwarlexEasyDpi does on your computer

- Runs **as administrator**: the WinDivert packet driver that GoodbyeDPI uses needs it. On the first run it
  copies itself to `C:\Program Files\SwarlexEasyDpi` (writable by administrators only) and runs from there.
- Runs the DPI engine (GoodbyeDPI, built as `gdpi.dll`) **inside its own process**, loaded from
  `C:\Program Files\SwarlexEasyDpi\bin` together with the signed WinDivert driver. Besides that it only runs `schtasks.exe`
  (*Start with Windows*), `notepad.exe` (*Diagnostics*), and itself (relaunch from Program Files on the
  first run, restart after an update) — by full path, without the shell.
- So that two copies of WinDivert do not fight, it stops a running `GoodbyeDPI` service and any other
  `goodbyedpi.exe` process when it starts.
- Writes only to `C:\Program Files\SwarlexEasyDpi` (engine, settings `ayarlar.ini`, log `kayit.txt`),
  when enabled the scheduled task *SwarlexEasyDpi* (start at logon), and - with *Keep the icon next to the
  clock* (on by default) - its own tray icon's `IsPromoted` value under
  `HKCU\Control Panel\NotifyIconSettings` (Windows 11). Upgrading from a pre-release build
  that had changed the Windows proxy setting restores that setting once.
- **DNS:** settings without encrypted DNS are tried first, unless the system DNS gives a poisoned answer
  for discord.com (a block page or a bogon address) or the last working setting used it. When a setting with
  encrypted DNS is in use, outgoing DNS queries (UDP 53) are captured while the bypass is on and sent over HTTPS to
  `https://1.1.1.1/dns-query` (fallback `1.0.0.1`, `8.8.8.8`; certificates checked, no proxy); the answer is
  handed back to Windows. Only standard queries are sent; if DoH fails or answers "no such name / refused /
  server error" (e.g. a router or VPN name), the query goes out unchanged to your own DNS. DNS servers on
  VPN/CGNAT (100.64.0.0/10) and link-local addresses are not captured. After repeated DoH failures it is skipped
  for 30 s. The panel and tooltip show when it is in use. No DNS setting is changed.
- Network access: a system DNS lookup of `discord.com` (poisoning check) and a request to
  `https://discord.com/api/v10/gateway` to verify the connection, the encrypted DNS above, and the update check
  below. No telemetry.

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

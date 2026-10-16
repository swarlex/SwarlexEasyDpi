# Changelog

All notable changes. Downloads: [Releases](https://github.com/swarlex/SwarlexEasyDpi/releases).

## 1.1.0

### Changed
- **One process:** the DPI engine (GoodbyeDPI) now runs inside SwarlexEasyDpi itself instead of as a separate
  `goodbyedpi.exe`, so Task Manager shows only SwarlexEasyDpi. Engine errors (a bad setting, a filter that
  cannot open) are shown in the panel and the log instead of ending the engine process.
- **Right-click menu** like SwarlexBattery's: *Language* and *Preferences* open in a window beside the menu.
- Faster and steadier connection check: the whole check is limited to 5 s, and a connection shown as "not
  verified" turns green on its own once the network is back.

### Added
- **Encrypted DNS (DoH) inside the engine:** when a network also blocks by DNS, DNS queries go over HTTPS to
  Cloudflare (Google as fallback) and the answers are handed back to Windows - without changing any DNS setting.
  It replaces the old plain-DNS redirection to Quad9/Cloudflare, which leaked queries and failed on networks that
  block port 53 to outside servers.
- **Detects poisoned DNS** (Türk Telekom's block page, bogon answers) and then tries the encrypted-DNS settings
  first, so connecting is faster on such networks.
- **Preferences:** start with Windows, keep the icon next to the clock (so the panel opens right above it),
  opening animation, theme (auto / dark / light), check for updates automatically.

### Fixed
- Turning it off while it was still connecting is no longer lost.
- Each release now comes with `LICENSES.txt` (GoodbyeDPI and WinDivert licenses).

## 1.0.0

First release.

### Added
- **One-click DPI bypass** for the whole system, from a tray icon. GoodbyeDPI is built from source and
  embedded; the signed WinDivert driver comes from the official GoodbyeDPI release (SHA-256 checked at build).
- **Automatic setting:** four settings known to work in Turkey are tried in turn (DNS-free ones first) until
  Discord's API answers; the working one is remembered and used first next time.
- **Connect automatically:** at start, and again after a network change or waking from sleep.
- **Panel** above the tray icon with one big on/off button and animations; **menu** on right-click
  (start with Windows, language, updates, diagnostics).
- **Updates** from GitHub Releases, SHA-256 checked, applied with one click.
- **English and Turkish.**

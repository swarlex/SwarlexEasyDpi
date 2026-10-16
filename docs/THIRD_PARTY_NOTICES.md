# Third-party notices

SwarlexEasyDpi includes, in binary form inside `SwarlexEasyDpi.exe`:

| Component | Version | License | Source |
|---|---|---|---|
| GoodbyeDPI (built from source as `gdpi.dll`, `native/goodbyedpi`) | 0.2.3rc3 | Apache License 2.0 — [native/goodbyedpi/LICENSE](../native/goodbyedpi/LICENSE) | https://github.com/ValdikSS/GoodbyeDPI |
| WinDivert (`WinDivert.dll`, `WinDivert64.sys`, signed driver) | 2.2 | LGPL-3.0 or GPL-2.0 — [native/windivert/LICENSE](../native/windivert/LICENSE) | https://github.com/basil00/WinDivert |

Changes to GoodbyeDPI: `native/goodbyedpi/engine.c` is new (runs GoodbyeDPI inside the app and stops it on
request); when built for the app (`GDPI_EMBED`) `goodbyedpi.c` does not wait 20 s before exiting on an error, and
its packet loop returns, and it does not enter the loop when a stop was requested while opening filters;
`service.h` declares `service_register` with its real parameters. `dns.c` (encrypted DNS) in the same folder
is SwarlexEasyDpi's own code.

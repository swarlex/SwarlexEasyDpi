# Contributing to SwarlexEasyDpi

Thanks for wanting to help. Bug reports, ideas and pull requests are all welcome.

## Reporting a bug

[Open a bug report](https://github.com/swarlex/SwarlexEasyDpi/issues/new?template=bug_report.yml) and paste the
log: right-click the tray icon > *Diagnostics…*. Say which country and internet provider you are on - DPI
blocking differs between them.

Security problems go through [private vulnerability reporting](SECURITY.md), not public issues.

## How it is built

| Folder | What |
|---|---|
| `app/` | The tray app (C#, .NET Framework 4.8, WPF panel and menu) |
| `native/goodbyedpi/` | GoodbyeDPI, built as `gdpi.dll`; `engine.c` runs it inside the app |
| `native/windivert/` | WinDivert header (the signed driver comes from the official GoodbyeDPI release) |
| `lang/` | Texts: `en.json` and `tr.json` must have the same keys (a test checks this) |
| `tests/` | Tests, run by `build.ps1 -Test` |

`powershell -ExecutionPolicy Bypass -File build.ps1 -Test` builds everything with the C# compiler that ships
with Windows and a pinned llvm-mingw; no Visual Studio needed. A few rules:

- **C# 5.** The Windows compiler does not know newer syntax (`$"..."`, `?.`, `=>` members).
- **Admin-only paths.** The app runs elevated: start programs by full path, never from the current folder, and
  write only under `C:\Program Files\SwarlexEasyDpi`.
- **Both languages.** Every new text goes into `lang/en.json` and `lang/tr.json`.

## Pull requests

1. Fork the repository and create a branch.
2. Make your change, run `build.ps1 -Test`, and describe it in [docs/CHANGELOG.md](../docs/CHANGELOG.md).
3. Open a pull request. The **Build** check runs the same build and tests on Windows.

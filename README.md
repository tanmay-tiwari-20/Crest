# Crest

Crest is a native Windows 11 desktop utility built from Crest PRD v1.1 (18 November 2026). It stays hidden until a fresh hover at the top-center of an eligible desktop. The default strict policy suppresses it whenever an application is active or an ordinary visible app intersects the selected display.

## Use the application

Run `artifacts/Crest-Setup-0.1.0-x64.exe` to install the self-contained x64 build. No SDK, account, or developer tools are needed. A portable build is in `artifacts/portable/Crest`; keep its files together and run `Crest.exe`.

Open **Crest Settings** from Start, or double-click the tray icon. Close Settings, press **Win+D**, move the pointer away from the top edge, then enter the top-center edge and wait briefly. Nothing remains visible when hidden. Right-click the tray icon for Settings, Pause / Resume, and Quit.

The top island was redesigned using the owner's two images and video: a compact black pill, horizontal activity cards, embedded3D focus artwork, and an icon dock. See `docs/REFERENCE_DESIGN.md` for source observations.

## Included

- Verified Explorer desktop identity, strict coverage and virtual-desktop checks, fresh-entry dwell, immediate app suppression, pointer grace, interrupted close reversal, and an opt-in keyboard shortcut.
- Native Windows media sessions; system volume and mute; OS-reported battery; supported built-in brightness.
- One persistent timer with monotonic countdown, paused recovery, and a retained completion.
- Explicit app, file, folder, website, and shortcut entries.
- Session-only desktop file shelf holding up to 20 references without modifying originals.
- Dark, light, and system themes; motion, trigger, monitor, module, sound, startup, and accessibility settings.
- A per-user installer, graceful upgrade/quit IPC, and uninstall data-retention choice.

## Status

Version **0.1.0 is a release candidate**, with P0/P1 implementation complete. All 24 automated outcome tests and selected native checks pass. The PRD requires additional real desktop-drag, shell-transition, accessibility, frame-pacing, and clean-account acceptance before a first usable release can be certified. The selected concurrent-activity overview is included; other P2/P3 catalogue entries remain future work, as the PRD's staged roadmap specifies; they are not represented as working integrations.

Read [the user guide](docs/USER_GUIDE.md), [requirements analysis](docs/PRD_ANALYSIS.md), [test report](docs/TEST_REPORT.md), [release notes](docs/RELEASE_NOTES.md), and [build instructions](docs/BUILD.md).

## Maintain the source

The application uses C#/.NET 10, WPF, Windows media APIs, Core Audio, and WMI. Run `scripts/build.ps1 -Package` with a .NET 10 SDK to restore, compile, run outcome tests, publish the self-contained app, and build the installer. The local SDK under `.tools` is used automatically when present. End users only need the packaged application.

The application has no analytics, process injection, required administrator service, or background network requests. Files are referenced only; clipboard capture and account integrations are absent.

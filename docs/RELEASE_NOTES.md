# Crest 0.2.0 — local tools release candidate

Crest 0.2.0 adds the remaining requested P2 local tools and core reliability fixes to the top-center Windows island. Desktop-only access remains the default; **Allow Crest over other apps** is an explicit opt-in. Background activity never reveals the island automatically.

This is a **prerelease candidate for Windows 11 x64**. Automated checks pass, while the complete physical Windows and performance acceptance matrix remains open.

## Downloads

- **Crest-Setup-0.2.0-x64.exe** — per-user installer with Start-menu shortcuts and Windows uninstall support.
- **Crest-Portable-0.2.0-x64.zip** — extract the complete `Crest` folder and run `Crest.exe`.
- **SHA256SUMS.txt** — SHA-256 checksums for both packages.

Both application packages include the .NET runtime. No SDK, separate runtime, administrator access or Crest account is required. The installer is unsigned. GitHub's source ZIP/tar archives are development source, not application packages.

## New local tools

| Feature | Included behavior |
|---|---|
| Audio outputs (F08) | Select connected outputs, switch with default-device readback, refresh the list, or open Windows sound settings. Direct switching probes undocumented Windows PolicyConfig and may be unavailable on a Windows build. |
| Productivity (F09) | Eight named timers, a stopwatch with 100 laps, durable work/break focus sessions with manual phase continuation, and up to 32 reminders with local/relative scheduling, 5/15-minute snooze and dismissal. |
| Clipboard history (F10) | Explicit opt-in text capture, search, copy, pin/unpin, remove, durable pause, clear and 1–30-day unpinned retention. Limits are 100 entries, 8,192 characters per entry and 120,000 stored characters. |
| System activity/connectivity (F11/F12) | Real CPU, physical-memory and network rates while Crest is visible, Wi-Fi/Bluetooth provider status and Windows settings links. |
| Capture references (F13) | Launch Windows screenshot capture and retain up to 12 image references you explicitly select; screenshot folders are not monitored. |
| Workspaces (F14) | Save up to 20 groups of 20 explicit app/file/folder/HTTP(S) targets; one target failure does not stop the remaining launches. |
| Shelf recovery (F16) | Pin references across restarts, locate moved files/folders or open the nearest existing parent. Unpinned references remain session-only; originals are untouched. |
| Personalization (F17/F18) | Separate display appearance/trigger profiles, Ocean/Forest/Rose themes, size presets, icon-dock ordering and configurable surface double-click/middle-click/wheel actions. |

Clipboard capture starts off. Enable it in **Settings → Privacy**; existing clipboard content is not imported when enabling, resuming or clearing history. Pins are bounded and remain until removed or cleared. Disabling capture erases retained text. Saved text stays in the local Windows profile and is not encrypted by Crest.

## Core improvements

- Compact metadata, accessible names and controls follow the displayed enabled activity, including focus sessions and due reminders. Their actions open the relevant continuation or snooze/dismissal controls.
- The tray's **Timers, focus and reminders**, and local completion notices, open **Settings → Activities** without revealing an overlay over another app.
- Settings navigation preserves keyboard focus and tab order across page changes.
- Timer completion has an original bundled 3.54-second chime, off by default. **Settings → Modules → Test timer sound** previews it without enabling completion sound.
- File drops negotiate Copy/Link synchronously, reject Move-only sources, and cancel stale imports on reset/disposal. Background import results leave unrelated controls intact.
- Localized date/time, retained Windows battery warnings, stationary reduced-motion fades and high-contrast colors improve essential operation.
- Desktop recovery requires a fresh trigger entry, stale motion/control completions are rejected, and diagnostics and activity collections remain bounded.

## Install and upgrade

For an installed copy, run the 0.2.0 installer. Setup requests graceful Quit, reuses the installation location and preserves saved configuration and startup preferences. For portable use, Quit Crest, extract the complete new package and run its executable. Keep all package files together.

Existing enabled-module choices are retained. Enable **Capture**, **Workspaces** or **Clipboard** in **Settings → Modules** if they are not in the dock; enable clipboard collection separately in **Privacy**. Saved timer state recovers across restart. Unpinned shelf references clear when the previous app quits; pinned references survive future restarts.

To open Crest in desktop-only mode, close Settings, press **Win+D**, move away from the top edge, then enter the top-center trigger and pause briefly. Enable **Allow Crest over other apps** in Behavior for access while another app is active. Settings, Pause, lock, suspend and secure desktop still suppress the island.

Startup is off by default for new users. Interactive uninstall offers retain/delete choices for local data; silent uninstall retains data. Source files referenced by the shelf, captures and shortcuts are never removed by Crest.

## Verification and remaining acceptance

Local implementation verification passes **97 domain outcomes, 107 WPF/native UI outcomes, eight release-provenance checks and three observer-lifecycle checks**, with zero compilation warnings/errors. Static theme/DPI renders provide layout samples. Native clipboard checks cover capture boundaries and restore original user clipboard data afterward; hardware probes read real Windows providers.

The preceding P2 build also completed an existing-account upgrade: all **492 distribution files** matched, saved data and the Windows startup preference were retained, and the updated app reached lifecycle readiness. That observation does not establish clean-account installation or both uninstall retention choices. Exact candidate identities and current validation are recorded in the [test report](TEST_REPORT.md).

Earlier timer hearing, desktop/shelf operation and ten-minute resource measurements remain historical for their recorded binaries. The previous core candidate's 12 passing gates do not qualify this changed release. Physical multi-display/DPI and accessibility checks, broader media/hardware and shell/lifecycle transitions, installed cold readiness, presentation/latency traces, hidden resource use, battery impact and clean-account distribution remain acceptance work. See the [acceptance runbook](P1_ACCEPTANCE_RUNBOOK.md).

Weather, calendars, notification/account access, accessory batteries, meeting/download/phone integrations and AI commands remain deferred. ARM64 and Windows 10 packages and automatic updating are not included.

Read the [user guide](USER_GUIDE.md), [build guide](BUILD.md) and [third-party notices](../THIRD_PARTY_NOTICES.md). Report reproducible problems through [GitHub Issues](https://github.com/tanmay-tiwari-20/Crest/issues).

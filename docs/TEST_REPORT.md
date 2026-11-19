# Compatibility and test report

Reviewed: 19 November 2026. Source candidate: Crest 0.1.0 with F09 productivity and optional access over other apps. Strict desktop is the default; app access is opt-in. Clipboard, account, notification, and AI integrations are absent.

## Current automated verification

The local Release build passed with zero warnings and errors:

- 46 domain outcomes, including independent timers, stopwatch recovery/laps, storage and settings recovery, pending control edits, interaction cancellation, and the new access preference.
- Eight WPF productivity UI outcomes exercising timer and stopwatch controls. Sixteen dark/light renders at 100/125/150/200% DPI densities provide layout samples; they do not certify physical mixed-display operation.
- Eight release-provenance checks, including matching source fingerprints in Windows PowerShell and PowerShell 7. Packaging rejects skipped tests, Debug output, stale source, modified binaries, and unverified files.

A successful build is automated evidence, not certification of the complete Windows acceptance matrix. CI results must be checked for the pushed commit.

## Optional access over other apps

**Settings → Behavior → Allow Crest over other apps** is off by default. When enabled, the selected-display hover trigger and separately enabled Ctrl+Alt+Space shortcut can open Crest while another app is active, including File Explorer. Settings, Pause, lock, suspend, and secure-desktop suppression remain required. Background timer/media events never reveal the island automatically.

Four access outcomes cover default and legacy/corrupt/reset preferences, saved opt-in, foreground-app and registered-input access versus unregistered own-window exclusion, shared environment guards, and cancellation/fresh-entry behavior when modes change.

A read-only native comparison loaded the current locally packaged DLL on the normal input desktop at 125% effective DPI with all five hooks installed. A browser remained foreground throughout: access was rejected with the option off, allowed with it on, and rejected after it was switched off again. Only the diagnostic process's in-memory preference changed. Actual pointer/hotkey input across apps, fullscreen presentation, and secure transitions remain native acceptance work.

The original P1 desktop-only cases must run with `AllowOverApps=false`. An explicitly requested overlay is expected when the option is enabled. Changing the implementation invalidates earlier DLL/source-bound evidence.

## P1 release status

P1 qualification remains open. The public [gate record](release-gates.json) lists 22 applicable acceptance criteria and nine measured quality gates. AC18 clipboard and AC19 optional integrations remain deferred. Partial outcomes and historical observations do not qualify the changed app-access candidate.

`scripts/verify-release-gates.ps1` writes a candidate-bound report to `artifacts/release-gate-report.json`. A gate marked `pass` requires current observed evidence, matching DLL/source fingerprints, and checked evidence-file hashes. `scripts/release.ps1 -RequireP1Gates` refuses qualification while required evidence is open, failed, or stale. Default packaging produces a clearly labelled release candidate.

Raw native window/PID snapshots, resource samples, settings backups, and local screenshots are retained locally and excluded from the source commit and application packages. This public report summarizes their relevant results without publishing the recordings. Evidence must be collected again for the precise candidate being qualified.

## Historical native and resource observations

These observations describe earlier core/F09 DLLs and do not qualify current app-access source:

- A native desktop-policy probe loaded the exact tested DLL, installed five hooks, rejected an unrelated foreground browser, recovered shell identity, and found all 12 selected Crest HWNDs hidden. The display was 1920 × 1080 at 125% effective DPI.
- A 30.169-second warm-core capture had seven complete samples and a maximum resident working set of 151,633,920 bytes (144.61 MiB), within the independent decimal 200 MB memory budget for that DLL. Its short duration did not qualify the ten-minute CPU gate.
- A prior 600.212-second run had 121 samples, average total-device CPU of 0.1786%, and maximum resident working set of 131,436,544 bytes (125.35 MiB). Three samples contained visible Crest windows while the desktop was foreground, so the run did not qualify as all-hidden idle. No visible-over-app incident was observed in those samples; transient event delivery and input interception remain unverified.

For a fresh hidden resource check, run `scripts/Measure-HiddenIdle.ps1 -CrestProcessId <pid> -DurationSeconds 600` with Settings closed and all Crest windows hidden throughout. Any visible sample invalidates hidden-idle qualification. Early exit, absent samples, or a changed DLL invalidate resource results. Memory uses decimal 200 MB, with MiB values reported separately.

## Historical baseline and interface review

The recorded laptop baseline was Windows 11 Home Single Language build 10.0.26300, x64 Ryzen 5 5600H, approximately 7.34 GiB RAM, Radeon integrated graphics/RX6500M, 1920 × 1080 at 125% effective DPI, and an OS-reported 144 Hz refresh rate. A refresh-rate value does not establish delivered-frame performance.

Earlier validation recorded 24 automated outcomes, actual audio/brightness callbacks and same-value writes, OS-reported plugged-in battery data, safe media empty states, and an existing-account installer lifecycle. Upgrade preserved settings, graceful quit worked, and uninstall removed executable/registration components. These checks did not establish clean-account installation or both retention choices.

Earlier dark/light renders and native Settings inspection found named UI Automation controls and readable 125% layouts. Minimized Settings restored through the app's single-instance Settings signal. A keyboard navigation attempt was interrupted by physical input, so keyboard-only operation is not claimed as verified. Screen-reader, high-contrast, complete keyboard, and presented-frame acceptance remain open.

Warm installed readiness was observed near 2 seconds, but the initial cold launch exceeded 5 seconds; the 3-second cold-start budget has not been established. Historical automated results are retained in [local tests](local-tests.md), [desktop interaction tests](desktop-foundation-tests.md), and [control-edit tests](control-edit-tests.md).

## Outstanding acceptance

| Gate | Remaining evidence |
|---|---|
| AC01–12 default visibility | Clear-desktop hover, 100 actual pointer cycles, Win+D fresh entry, small/maximized app switching, shell panels, second display, no hidden mouse-blocking region |
| AC13 media | Active playback/seek, multiple sessions, preferred player, session removal |
| AC14–15 hardware | Hardware-key changes, endpoint disconnects, charging transitions, unavailable hardware |
| AC16 lifecycle | Actual sleep/resume/clock changes/restart alongside deterministic recovery tests |
| AC17 desktop shelf | Real desktop OLE reveal/drop/drag-out, unavailable sources, unchanged originals; Explorer cannot summon in desktop-only mode |
| AC20 display | Physical 100/150/200% scaling, mixed DPI, disconnect/fallback, reachable geometry |
| AC21 accessibility | Keyboard-only navigation, screen reader, high contrast, Windows reduced motion |
| AC22 performance | Actual presented-frame tracing, suppression p95/p99, readiness, uninterrupted ten-minute hidden idle, current memory and repeatable battery impact |
| AC23 recovery | Explorer restart, lock/unlock, display changes, tray pause/resume/quit |
| AC24 distribution | Clean-account install/upgrade/uninstall and both data-retention choices |
| Optional app access | Actual hover/shortcut and interaction over representative apps, fullscreen behavior, secure/lifecycle transitions, return to desktop-only mode |

The default policy requires no unexpected visible-over-app incident and no retained hidden hit region. Opt-in app access permits requested overlays; hidden hit regions remain prohibited in both modes.

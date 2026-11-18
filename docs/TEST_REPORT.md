# Compatibility and test report

Date: 18 November 2026. Build: Crest 0.1.0 release candidate. Visibility: strict desktop default. Optional AI/accounts/clipboard: absent.

## Baseline laptop

Windows 11 Home Single Language build10.0.26300; x64; AMD Ryzen 5 5600H; approximately7.34GiB RAM; Radeon integrated graphics and RX6500M;1920×1080 at125% effective DPI; OS-reported144Hz; Balanced power scheme. See hardware-baseline.json. Higher-refresh animation has **not** been certified from the refresh-rate value.

## Verified evidence

- Release compilation: zero errors, zero warnings.
- 24 deterministic outcome tests pass: nine interaction tests, ten timer/storage/launcher/shelf tests, and five pending-control-edit tests. See local-tests.md, desktop-foundation-tests.md, and control-edit-tests.md.
- A 100-cycle state-machine sequence verifies interrupted hover, immediate suppression requests, and stale callback rejection. This is not 100 actual pointer cycles on the desktop.
- Native desktop probe identifies a Chrome-class foreground as ineligible, preserves hiding through pause/resume, and reads the physical display/DPI correctly. See native-desktop-probe.json.
- Packaged process HWND probe finds the Settings window as the only visible Crest HWND, with the island/helper HWNDs hidden. See native-packaged-window-probe.json.
- Hardware adapters: actual default audio endpoint, volume/mute notifications, battery, built-in brightness, dispatcher callbacks, and clean disposal. Same-value audio/brightness writes preserve the owner's settings. See hardware-adapter-test.json.
- Media empty state: zero compatible sessions; unsupported playback/seek calls safely no-op. An active player was not introduced merely for testing.
- Reference redesign: compact pill, activity deck, and five detail panels rendered in both themes; original3D focus crop and circular dock inspected. Native Settings window inspected with UI Automation names, roles, and slider values.
- Normal-state text and meaningful control-track colors calculated against contrast targets; decorative card edges use a separate softer outline. See color-contrast.json.
- Installed previous candidate lifecycle passed on the current account: install, runtime/docs, Start-menu entries, startup off, graceful running-app upgrade and uninstall, executable and registration removal. Clean-account testing and explicit settings-file preservation remain pending. See install-lifecycle-test.json.
- Warm installed readiness observed at1,998–2,252ms. The initial cold launch exceeded5s; the3s cold-start budget has not been established.
- Graceful quit IPC shuts down a running packaged process; installer uses this IPC and refuses to force-kill an unresponsive process.
- Pending-control tests verify that hardware refresh cannot overwrite a queued value, stale completions cannot erase newer edits, cancellation rejects obsolete writes, and seek acknowledgement has a bounded recovery period. The final installed Settings window was minimized through its native button, then restored to its previous Normal state through Crest's existing single-instance Settings signal. Maximized restoration and 200% Settings operation remain native acceptance checks.

Installer lifecycle and hidden-idle measurements are recorded separately as those checks complete. They do not imply a clean-account or presentation-timing result.

## Installed native interface review

Final polish build: the installed DLL matches the frozen portable publish, and the normal per-user upgrade preserved the existing settings file byte-for-byte. The final installed app rendered all dark/light previews successfully. Its Settings window was confirmed minimized, then the app's `--settings` entry point restored the same window without an external activation call. The restored layout and hardware-ready states were inspected; the screenshot is `artifacts/previews/native-settings-restored.png`, with evidence in `final-native-qa.json`. The desktop design composition is a static WPF render over a supplied video frame, not a native desktop capture.

The installed Settings window rendered cleanly at the laptop's actual 125% effective DPI. The Appearance page fit its labels and controls without clipping. UI Automation exposed meaningful names for the color-theme, motion-preference, island-size, and motion-speed controls. The installed capability view reported system audio and built-in brightness ready.

An attempted keyboard navigation check was interrupted by physical user input, so keyboard-only operation is not claimed as verified. Screen-reader operation, complete keyboard navigation, and the PRD's presented-frame motion budget remain acceptance gates. The dock fade and repeated-module-selection refinements were reviewed for cancellation and continuity in source; that review does not substitute for native frame tracing.

## Outstanding acceptance

| Gate | Pending evidence |
|---|---|
| AC01–12 desktop visibility | Clear-desktop hover, 100 real pointer cycles, Win+D fresh entry, small/maximized app switching, shell panels, second display, no mouse-blocking region |
| AC13 media | Active playback/seek, multiple sessions, preferred player, session removal |
| AC14–15 hardware edge cases | Hardware-key changes, unplugged endpoints, charging transitions, unavailable hardware |
| AC16 live lifecycle | Actual sleep/resume/clock adjustments/restart alongside deterministic clock tests |
| AC17 desktop shelf | Real desktop OLE drag reveal/drop/drag-out; Explorer foreground cannot summon; original unchanged |
| AC20 display |100/150/200% scaling, mixed DPI, disconnect/fallback, off-screen geometry |
| AC21 accessibility | Real keyboard-only navigation, screen reader, high contrast, Windows reduced motion |
| AC22 motion/performance | Presented-frame tracing over30s, suppression p95/p99, readiness latency, repeatable battery impact |
| AC23 lifecycle | Explorer restart, lock/unlock, actual sleep/display changes, tray pause/resume/quit |
| AC24 distribution | Clean user account install/upgrade/uninstall and both retention choices |

AC18 clipboard and AC19 optional permissions/accounts are deferred with their P2/P3 modules. The core does not collect clipboard/account/notification data.

The PRD requires no visible-over-app incident and no retained hidden hit region. Current-app suppression probes are evidence for a tested state, not a proof of every Windows transition. Optional integrations will inherit the same controller and tests when separately implemented.

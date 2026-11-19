# Changelog

## Unreleased — core completion and F09

- Added an off-by-default **Allow Crest over other apps** setting so hover and the separately enabled keyboard shortcut can reveal Crest while another app is active. Strict desktop remains the default.

- Added eight independent named timers, per-timer pause/resume/cancel/acknowledgement, and migration of the previous saved timer.
- Added a persistent stopwatch with 100 bounded laps, split/total times, pause/resume, and reset. Live time is monotonic; restart recovery uses saved UTC state.
- Fixed repeated keyboard reveal, locked-session handling, shell recovery subscriptions, and incomplete native-hook initialization.
- Completed high-contrast control colors/boundaries and live repaint; clarified fresh-hover onboarding.
- Bounded optional asynchronous technical diagnostics to 5 MiB, 128 queued codes, and 72-hour retention.
- Added source/binary-bound packaging verification and 31 explicit P1 acceptance gates. Strict P1 release verification refuses open, failed, or stale evidence.
- Current verification: 46 domain outcomes, eight WPF productivity UI outcomes, eight provenance checks, and dark/light renders at four DPI densities. A native Chrome-foreground comparison rejects/allows/rejects access as the option is switched off/on/off. Native P1 acceptance remains open; see the test report.

## 0.1.0 - prerelease candidate

### Added

- A Windows 11 x64 island that appears on fresh top-center desktop hover and hides on unrelated app activation.
- A compact pill and expanded focus, music, and battery deck with a circular module dock.
- Windows media controls, default audio volume and mute, OS battery information, and supported built-in brightness.
- A persistent timer, explicit app/file/folder/website shortcuts, and a session-only shelf of up to 20 file references.
- Theme, motion, size, trigger, monitor, module, and startup preferences; tray Settings, Pause/Resume, and Quit.
- A self-contained portable build and per-user installer with graceful upgrade/quit behavior and an uninstall data-retention choice.

### Refined

- Coordinated dock and deck animation, current-progress continuation during detail changes, and stable repeated module selection.
- Pending slider values remain visible during hardware refresh and asynchronous writes. A stale completion cannot erase a newer edit; seek acknowledgement expires after a bounded grace period.
- Minimized Settings restores its prior window state and constrains its size to the display working area.

### Validation and known limitations

The 24 automated outcome tests and selected native hardware, installation, and Settings checks pass. User testing confirmed desktop hover after a WhatsApp WebView background window stopped blocking strict coverage. A successful build is not certification of every Windows behavior; see the [test report](docs/TEST_REPORT.md).

Strict mode treats visible background app windows as desktop coverage, including some WebView helpers that remain after minimizing their parent app. Real desktop dragging, active/multiple media sessions, full shell/display/lifecycle transitions, accessibility, cold-start performance, hidden-idle measurement, and presented-frame timing still require acceptance evidence. Later PRD integrations are not included in this candidate.

See [release notes](docs/RELEASE_NOTES.md) for user-facing installation and upgrade information.

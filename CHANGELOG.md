# Changelog

## 0.2.0 — local tools prerelease candidate — 20 November 2026

### Added

- Connected audio-output selection, runtime-probed switching with default readback, and Windows sound-settings fallback (F08).
- Eight independent named timers, a persistent stopwatch with 100 laps, and migration of the previous saved timer. Added work/break focus sessions with manual phase continuation and local reminders with relative/local-time scheduling, 5/15-minute snooze and dismissal (F09).
- Off-by-default text clipboard history with search/copy/pins, durable pause, clear/disable erasure, bounded storage and 1–30-day unpinned retention through Settings → Privacy (F10). Enabling/resuming never imports the unchanged current clipboard.
- Actual CPU, physical-memory and active-adapter network statistics sampled only while visible (F11), plus Windows Wi-Fi/Bluetooth status and settings links (F12).
- Windows screenshot-tool launch and up to 12 explicitly selected persistent image references (F13).
- Up to 20 workspace presets of 20 selected app/file/folder/HTTP(S) targets, with editing and independent launch-failure handling (F14).
- Shelf pins, persistent reference paths, explicit file/folder relinking and nearest existing parent recovery. Unpinned references remain session-only (F16).
- Separate appearance and trigger profiles for each display, default fallback and override reset while using one selected island display (F17).
- Ocean/Forest/Rose themes, Compact/Comfortable/Large size presets, dock ordering and configurable surface double-click/middle-click/wheel gestures (F18).
- Original bundled 3.54-second timer chime, off by default, with an independent Settings preview. Local focus/reminder tray notices open Settings → Activities and never reveal the island.
- Opt-in **Allow Crest over other apps**, preserving strict desktop defaults and secure/lifecycle guards.

### Fixed

- Compact metadata, artwork, accessible names and actions now respect enabled modules and the displayed timer, focus session, due reminder, media or battery notice.
- Settings page changes retain navigation controls, keyboard focus and tab order.
- File drops publish Copy/Link synchronously, reject Move-only sources and cancel stale/reentrant imports. Late results stay in Shelf without recreating another module's controls; equivalent folder paths share a slot.
- Pointer/OLE hit regions follow current card transforms and exclude invisible or input-disabled areas.
- Local date/time formatting, retained Windows-reported low/critical battery warnings and stationary reduced-motion fades handle hidden/recovery and preference-change cases.
- Repeated keyboard reveal, lock/shell recovery and incomplete native-hook initialization cannot leave stale reveal work active.
- Passive observer health survives clean cleanup and retains unexpected event-loop failure. Resource summaries include actual/requested duration, sample count, early exit and qualification status.
- Diagnostics, saved activities, clipboard history and references enforce local retention and capacity limits.

### Verification and release status

- Local checks pass 97 domain outcomes, 107 WPF/native UI outcomes, eight release-provenance checks and three observer-lifecycle checks, with zero compilation warnings/errors.
- An existing-account P2 upgrade matched all 492 distribution files, preserved saved data/startup preference and restarted successfully. Clean-account installation and both uninstall retention choices remain separate acceptance work.
- Source/binary-bound packaging verification and 31 P1 acceptance gates reject incomplete, failed or stale qualification. This release remains a prerelease candidate.
- Prior timer-sound, desktop/shelf and resource observations retain their original binary identities. The previous core candidate's 12 passing gates do not qualify 0.2.0. Current evidence and remaining physical acceptance are in the [test report](docs/TEST_REPORT.md).
- Added user, build and release documentation for the expanded local tools. Installer and ZIP include reviewed README illustrations and dependency notices.

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

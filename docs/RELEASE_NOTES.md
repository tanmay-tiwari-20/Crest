# Crest 0.1.0 - release candidate

Crest brings music, a focus timer, laptop controls, shortcuts, and a temporary file shelf to a top-center Windows island. By default, it opens after a fresh hover on an eligible desktop and hides when an unrelated app becomes active. An opt-in setting allows opening it over other apps.

This is a **prerelease candidate** for Windows 11 x64. It is ready for testing and feedback; the complete Windows acceptance matrix remains in progress.

The current source adds F09 multiple timers and stopwatch plus optional access over other apps; these changes have not been published to the existing GitHub release assets.

## Downloads

- **Crest-Setup-0.1.0-x64.exe:** per-user installer with Start-menu entries and Windows uninstall support.
- **Crest-Portable-0.1.0-x64.zip:** extract the archive, keep the `Crest` folder intact, and run `Crest.exe`.
- **SHA256SUMS.txt:** checksums for both application packages.

Both packages include the .NET runtime. No SDK, separate runtime, administrator access, or Crest account is needed. The installer is unsigned. GitHub's source-code archives are development source, not ready-to-run application packages.

## Included

- Compact island, expanded focus/music/battery cards, circular dock, and coordinated animations.
- Original blue focus character and project source under MIT, with dependency notices included.
- Windows media metadata/artwork, supported playback/seek controls, and preferred-player selection.
- Default audio volume/mute, OS-reported battery state, and supported built-in brightness.
- Eight independent named timers with presets/custom duration, pause/resume, cancellation, and retained completion.
- Persistent stopwatch with pause/resume, reset, and up to 100 split/total laps.
- Explicit app, file, folder, website, and shortcut entries.
- Up to 20 session-only shelf references; original files remain unchanged.
- Theme, motion, size, trigger, display, module, sound, startup, and accessibility preferences.
- Tray Settings, Pause/Resume, and Quit; graceful upgrades and an uninstall data-retention choice.
- **Settings → Behavior → Allow Crest over other apps**, off by default. Reveal by the top-edge trigger on your selected display, or Ctrl+Alt+Space when the keyboard shortcut is separately enabled. Settings, Pause, lock, suspend, and secure desktop still suppress Crest.

Pending control adjustments survive debounce and hardware refresh, stale writes preserve newer requests, and a lagging media timeline has bounded seek acknowledgement. Repeated module selection keeps its controls steady. Settings restores its prior window state and fits the display working area.

## First use and upgrades

For default desktop-only use, close Settings, press **Win+D**, move away from the top edge, then enter the top-center edge and pause briefly. Click the chevron to expand. Crest stays hidden while an app or Windows panel is active in that mode. To use it while another app is active, enable **Allow Crest over other apps** in Behavior. Some background WebView windows can also block strict desktop mode; closing a WhatsApp WebView window restored hover in user testing.

Run the installer to upgrade an installed copy. Setup requests graceful quit and preserves preferences. For a portable upgrade, quit Crest and extract the new complete folder. Startup with Windows is off by default. Silent uninstall retains settings; interactive uninstall offers a retention choice.

## Validation and limitations

The current candidate passes 46 domain outcomes, eight productivity UI outcomes, and eight release-provenance checks. Selected historical native hardware, installation, Settings restoration, and user hover checks are recorded. The full shell/app/display transition matrix, desktop OLE dragging, active/multiple media sessions, hardware/lifecycle edges, screen-reader and keyboard-only operation, frame delivery, and cold-start budget remain acceptance work. Current measurements and open gates are recorded in the test report.

ARM64/Windows 10 packages, clipboard history, weather, accounts, AI commands, and later PRD integrations are not included. There is no automatic updater, analytics, or background account service.

Read the [user guide](https://github.com/tanmay-tiwari-20/Crest/blob/v0.1.0/docs/USER_GUIDE.md), [test report](https://github.com/tanmay-tiwari-20/Crest/blob/v0.1.0/docs/TEST_REPORT.md), [third-party notices](https://github.com/tanmay-tiwari-20/Crest/blob/v0.1.0/THIRD_PARTY_NOTICES.md), and [build instructions](https://github.com/tanmay-tiwari-20/Crest/blob/v0.1.0/docs/BUILD.md). Report reproducible problems through [GitHub Issues](https://github.com/tanmay-tiwari-20/Crest/issues).

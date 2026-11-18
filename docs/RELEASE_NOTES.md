# Crest 0.1.0 — release candidate

18 November 2026. Target: Windows 11 x64. Built against .NET 10; packaged runtime included.

Owner-reference redesign adds a260×44 DIP compact pill,820×150 DIP focus/music/battery deck, circular dock, embedded3D focus image, and lower detail cards. It retains desktop-only eligibility and precisely tests the visible rounded regions.

Initial P0/P1 implementation includes desktop-only reveal and suppression, a single interaction controller, the five UI panels covering seven local modules, appearance/behavior settings, tray pause/resume/quit, optional startup, protected local scope, and installer/uninstaller support.

Validated by 24 automated outcome tests, warning-free compilation, actual laptop audio/battery/brightness probes, hidden-window/native desktop checks, and dark/light WPF visual review. Additional test evidence is recorded in TEST_REPORT.md and the companion JSON reports.

Volume, brightness, and seek edits now retain the requested value through hardware refresh, debounce, and asynchronous completion. Stale completions preserve newer edits, and a lagging media timeline gets bounded time to acknowledge a seek. Settings restores its previous Normal or Maximized state from the tray and fits its dimensions and minimums to the display working area across DPI changes. The final installed app passed Normal-state minimized restoration through its Settings entry point; Maximized restoration and 200% sizing still need acceptance testing.

Remaining acceptance includes real desktop file dragging, active/multiple media sessions, the complete app/shell switching matrix, lock/sleep/Explorer/display recovery, keyboard/screen-reader/high-contrast testing, measured presented-frame performance, and clean-account install/upgrade/uninstall. P2/P3 features are future phases.

Upgrades require a deliberate installation action and preserve preferences. No automatic updater, account, analytics, clipboard capture, or online module is included.

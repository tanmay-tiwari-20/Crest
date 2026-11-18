# Requirements analysis and implementation map

Source: Crest Product Requirements v1.1, dated 18 November 2026, supplied as `Crest_PRD.pdf` (18 pages).

The document defines a Windows 11 utility and a staged roadmap. Its central constraint is desktop-only visibility, including the invisible input region. P0 is the behavioral foundation; P1 is the local first release. P2 refinements and P3 integrations are later, individually verified work. The application must never represent future API access as already available.

## Decisions used

| Decision | Implemented starting choice |
|---|---|
| Architecture | x64, confirmed on owner's Ryzen 5 5600H laptop |
| Platform | Windows 11; observed build 10.0.26300 |
| Renderer | WPF/.NET 10, transparent non-activating island |
| Desktop policy | Strict mode default; optional desktop-active mode still rejects unrelated foreground apps |
| Trigger | Top center, 240 × 3 DIP; centered/full-width and geometry settings |
| Geometry | Owner-reference redesign:260 ×44 DIP compact,820 ×150 DIP activity deck,284 ×40 DIP dock,420 ×316 DIP detail card; automatic fit to display work area |
| Timing | 120 ms dwell; 240 ms reveal; 190 ms close; 220 ms expansion; 250 ms grace |
| Theme | Follow Windows by default; original dark/light token palette |
| Motion | Follow Windows preference; reduced fade; adjustable speed |
| Sounds/startup/shortcut | Off by default, enabled explicitly in Settings |
| Data | Versioned local JSON; no analytics or account; shelf memory-only |
| Optional catalogue | P2/P3 deferred; no implied permissions or fake data |

The owner supplied two images and a62.2-second recording during implementation. Those references supersede the initial visual geometry: a near-black horizontal deck, focus/music/battery cards, small round controls, and a separate dock. The3D focus character is a transparent crop from the supplied image. Expansion clips stable content instead of resizing it every frame. Deck, dock, and lower detail card share one HWND and precise rounded hit regions; transparent gaps do not retain input. Presented-frame pacing remains to be measured.

## Implementation traceability

| PRD scope | Implementation | Verification |
|---|---|---|
| VIS01–06: shell identity, strict desktop, any-app suppression, recovery/fresh entry | `Platform/DesktopPolicy.cs`, `Core/InteractionStateMachine.cs` | Native current-app/DPI probe; automated fresh-entry and interruption tests; full desktop matrix pending |
| VIS07–09: registered focus/popups, immediate hide, no placeholder/hit region | `UI/IslandWindow.cs`, `DesktopPolicy.cs`, `App.xaml.cs` | Native hidden HWND probe; popup and desktop focus integration pending |
| INT01–06: dwell cancel, corridor/grace, reversal, cancellation, no duplicate reveal | Single `InteractionController` and deterministic state machine | Nine outcome tests, including 100 interrupted cycles |
| DES01–08: compact/expanded design, themes, stable metadata, capability states | `UI/Design.cs`, `App.xaml`, `IslandWindow.cs` | Dark/light WPF render review and native Settings review |
| ACC01–05: names/roles, keyboard, contrast preferences, DPI | WPF UI Automation controls; explicit keyboard focus; theme/motion preferences | Settings accessibility tree inspected; screen-reader/keyboard and DPI matrix pending |
| F01 music | `Features/HardwareHub.cs`, WinRT media sessions | Empty-state/native calls verified; active session/removal pending |
| F02 audio | `Features/CoreAudioAdapter.cs` | Actual endpoint, callbacks, and same-value volume write verified |
| F03 battery | Native Windows power status | Actual 100%, plugged-in status; no invented time estimate |
| F04 brightness | Asynchronous WMI provider | Built-in capability and same-value write verified |
| F05 clock and timer | `Features/LocalHub.cs`, GetTickCount64 and saved UTC state | Clock shift, sleep-equivalent elapsed time, restart, pause, acknowledgement tests |
| F06 launcher | Explicit `LauncherEntry` settings and shell open | Reference/protocol validation tests; native user-launch matrix pending |
| F07 shelf | Bounded session-only reference list; native WPF OLE drag/drop | Cap/missing/source-preservation tests; real desktop drag acceptance pending |
| P1 settings/tray/startup | `SettingsWindow`, tray, registry preference, named IPC | Native Settings and graceful quit checks; installer record in test report |
| REL01–06 | Fail-closed policy, display fallback, schema recovery, mutex, cleanup | Automated recovery checks and selected native probes; lock/sleep/Explorer matrix pending |
| DAT/privacy/storage | Local-only preferences and timer, memory shelf, reset/uninstall retention | Storage/cap tests and source review |
| PER/resource budgets | Event-driven native updates; bounded fallback and artwork; hidden rendering stopped | Hardware event checks; idle and presentation measurements separately recorded |

## Deferred catalogue

The visual concurrent-activity overview (part ofF15) was selected explicitly through the owner's reference redesign and is included. Other P2 refinements remain deferred.

P2 F08–F18 covers playback-device switching, multiple productivity tools, opt-in clipboard history, system statistics, connectivity, capture references, workspace presets, concurrent chips, persistent shelf references, per-display appearance, and gestures. The remaining catalogue is not presented as working in this local candidate. Device switching especially needs a supported verified mechanism.

P3 F19–F26 covers notification access, weather, calendars, accessory batteries, named meeting integrations, download progress, AI commands, and supported phone integrations. Each needs an independent specification, actual API compatibility, permissions, bounded retention, offline handling, and disconnect behavior. All are absent/off in this build.

## Release gate

The PRD's measured budgets and real Windows acceptance are requirements, not facts inferred from a successful compile. This candidate is not certified as passing every P0/P1 release gate until desktop dragging, shell and focus transitions, accessibility, presentation timing, and clean-account install/upgrade/uninstall have been recorded. The test report separates completed evidence from outstanding checks.

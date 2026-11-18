# Desktop foundation verification

Build: Crest 0.1.0, Windows x64, .NET 10.0.404. Recorded on 18 November 2026. Strict desktop mode is the default. The hardware baseline is in `hardware-baseline.json`.

## Completed checks

The combined console suite passes 24 outcome tests; nine cover the deterministic desktop interaction controller. They verify fresh entry after startup and eligibility recovery, canceled dwell without reveal, suppression from every visible transition, stale animation rejection, pointer grace and close reversal, retained registered interaction, explicit Escape dismissal, paused trigger deactivation, and 100 repeated interrupted hover cycles. Ten cover local settings, timer recovery, launcher validation and shelf references; five cover pending control writes and seek acknowledgement (see control-edit-tests.md).

The native read-only probe uses the application's PerMonitorV2 manifest and real Windows APIs. Its recorded foreground window had class `Chrome_WidgetWin_1`; the shell was `Progman`. The strict policy returned ineligible, remained hidden while paused, and remained ineligible after resuming. It reported a 1920 by 1080 display at 125 percent DPI. This identifies an unrelated foreground window by its class; it does not assert which browser or application owned that window.

One hundred synchronous eligibility reconciliations on that unrelated-foreground path measured p95 0.0983 ms, p99 0.1509 ms, maximum 0.1619 ms. These timings exclude Windows event delivery and the WPF dispatch queue. They therefore **do not establish** the PRD's foreground-suppression latency percentiles. Exact machine-readable output is in `native-desktop-probe.json`.

## Implementation and resource review

The foreground hook is event driven and uses the WPF dispatcher at Send priority. Object show/hide, minimize and top-level location events are coalesced until the queued reconciliation runs. Child control events are filtered out. A one-second reconciliation covers missed or uncertain shell changes; shell-tree discovery is cached for up to one second. Ordinary unrelated foreground windows take the short ineligible path without strict coverage enumeration. No full-screen hover HWND, global mouse hook or hidden rendering loop is created. Pointer sampling runs every 30 ms only while eligible and unpaused; its callbacks perform geometry checks rather than layout or rendering.

Visibility fails closed when shell, foreground, normal input desktop, virtual-desktop coverage or window identity cannot be established. Registered interaction HWNDs are excluded from app coverage; the primary island's precise point predicate prevents transparent gaps and rounded-corner margins from retaining hover. Explorer-owned `SysDragImage` is excluded narrowly as a drag adornment, while File Explorer foreground still suppresses the island.

Pause removes native WinEvent hooks, stops reconciliation, unregisters the optional reveal hotkey and stops pointer sampling. Resume reattaches hooks and requires a fresh pointer entry. Lock/unlock, sleep/resume, display reconfiguration and a replaced shell identity invalidate existing transitions before rearming. Pending invalidation callbacks check disposal before publishing changes.

The application uses a per-session named mutex for one instance and named events for Settings and Quit. Normal Quit suppresses and closes the island, disposes the controller, desktop hooks, hardware subscriptions, local timer, tray and IPC handles, then releases the mutex. Guarded OnExit cleanup covers startup/exit paths outside normal Quit and attempts every resource release even if one fails.

## Hidden resource measurements

A reproducible read-only sampler is supplied at `scripts/Measure-HiddenIdle.ps1`. It accepts an actual Crest process ID, requested duration and JSON output path. It records normalized process CPU, working set, private bytes, visible top-level windows and foreground class every five seconds. It never changes focus, minimizes or closes windows, alters device settings or enables permissions.

A short run is provisional and cannot satisfy the ten-minute hidden-CPU gate. Any visible Crest window observed during sampling invalidates hidden-idle qualification. CPU is divided by all logical processors to match the total-device CPU budget. The final report must name the actual duration, build path and observed module state. A packaged ten-minute run is pending until the Settings review completes; append the actual result below after it finishes.

## Required acceptance still unverified

The current automated and native checks do not close the P0/P1 release gates. Real desktop-only reveal on a clear active desktop, foreground suppression during interrupted motion, Start/Search/Task View/Quick Settings/Widgets, second-display foreground changes, strict partially off-screen windows, actual inactive virtual desktops, lock/resume and Explorer restart require Windows integration or owner review. Desktop file-drag reveal and drop must be exercised with a real desktop file. Native hidden-window snapshots can confirm HWND visibility but cannot replace pointer-interception and desktop-drag checks.

Presented-frame pacing, p95/p99 suppression including event-delivery delay, UI-thread stalls, 100–200 percent DPI layouts, screen reader behavior, high contrast, mixed-DPI displays, monitor disconnect and 144 Hz behavior have not been measured by this probe. No 60 fps, high-refresh or battery-impact claim is made. Clean-account install/upgrade/uninstall remains a separate packaging gate.

## Reproduction

Run the native snapshot from the source root with the local SDK:

```powershell
.tools\dotnet\dotnet.exe run --project tests\Crest.PlatformProbe\Crest.PlatformProbe.csproj -- <CrestProcessId>
```

The probe's temporary hooks are disposed before the process exits. It prints JSON and does not activate any window. Synchronous reconciliation timings are diagnostic costs, not end-to-end suppression performance.

Run a hidden idle sample after the owner closes Settings and Crest remains in the tray:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Measure-HiddenIdle.ps1 -CrestProcessId <id> -DurationSeconds 600 -OutputPath docs\hidden-idle-measurement.json
```


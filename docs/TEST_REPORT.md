# Compatibility and test report

Reviewed: 20 November 2026. Source candidate: Crest 0.2.0 with P0/P1 acceptance corrections and requested P2 local features F08–F14 and F16–F18. Strict desktop is the default; app access and clipboard history are opt-in. Account, notification-access, weather, calendar and AI integrations remain absent. Local reminder/focus tray notices never reveal the island.

The previous priority DLL was `3810FBA3EBFC813E15C28EA1D1946C6E86702E1E2C7447D22A8644483072D159`, with source fingerprint `ACEF535FD408A1632156315E46FF1F5478C78F3BC7A7A4F8152A1445D855E086`. This P2 change invalidates its binary-bound acceptance evidence. Earlier installed, audible-timer, shelf and resource observations retain their original candidate identities. The final local build identity and automated results are recorded below.

## P2 local automated verification

The required Release workflow passed with zero warnings/errors: **97 domain outcomes, 107 WPF/native UI outcomes, eight release-provenance checks, and three observer-lifecycle checks**. The self-contained x64 application is in `artifacts/portable/Crest`; versioned installer/ZIP/checksum assets are in `artifacts/release`. Its manifest binds the distribution to the tested source; [P2 verification](p2-local-verification.json) records the final identities and counts.

The normalized staged checkout has the same source fingerprint as the tested working tree. A fresh 0.2.0 isolated native instance reached dispatcher readiness with all five hooks, stayed hidden with clipboard history off, and quit with exit code 0. This smoke test does not establish cold-start timing or the broader physical acceptance matrix.

The preceding local P2 preview, DLL `649244A9F08AF5C437C7423FA179A75B9187B9973E0E7312B6BB8A4D40779937`, reached isolated dispatcher readiness with all five hooks, stayed hidden with clipboard history off, and quit gracefully with exit code 0. It was also installed over the existing account: all 492 distribution files matched, saved data and the Windows startup value were unchanged, and the restarted installed instance reached lifecycle readiness. These observations describe that preview binary; the versioned 0.2.0 release must use its own recorded build identities and does not inherit broader native qualification.

| Scope | Evidence in this run |
|---|---|
| F08 audio outputs | Real active-endpoint enumeration; absent-output rejection; selector capability states and exact sound-settings fallback callback. Direct switching reads the actual default after each explicit action. No live output was changed in the test run. |
| F09 focus/reminders | Monotonic focus duration, paused/overdue restart, explicit work/break progression, final-round termination, due-once reminder scheduling, local-time input, snooze/dismissal, bounded/reset state and stale-notification rejection. Compact actions open the correct subtab and preserve independent timers. |
| F10 clipboard | Real Windows clipboard-update capture, off/enable/pause/resume/clear boundaries, privacy-exclusion markers, accessible search/pin/copy/clear actions, durable retention/capacity and safe malformed recovery. Native fixtures restore stable original user formats and exact original Unicode text. |
| F11/F12 statistics/connectivity | Real Windows CPU and physical-memory readings, network counter-delta/reset tests, hidden sampling stop, native Wi-Fi/Bluetooth queries and exact settings-link callbacks. |
| F13/F14 capture/workspaces | Explicit selected capture paths, restart/missing/remove/clear states with original bytes preserved; workspace create/rename/HTTP(S) validation/remove/persistence and launch failure isolation. Automated UI checks do not launch external applications or open picker dialogs. |
| F16 shelf | Pin/unpin persistence, unpinned session isolation, explicit relinking, duplicate/type rejection, nearest-existing-parent recovery and source-byte preservation. Existing native OLE tests remain passing. |
| F17/F18 preferences | Independent display theme/size/trigger settings, saved defaults and removal of an override, theme/size choices, persisted module ordering reflected in the dock, configurable pointer actions and actual surface-wheel/middle-click routing without hijacking controls. |

Twenty existing theme/DPI renders and three new capture/shelf theme renders provide static layout samples. Native clipboard and hardware probes establish those local behaviors on this machine, not the entire physical display, accessibility, device-switch, shell-transition, performance or installation matrix. Reminder/focus tray notices are guarded against stale deferred callbacks; they never reveal the island. Existing P1 hardware/resource/distribution evidence remains historical for its recorded binary.

## Previous priority automated verification

The preceding priority Release build passed with zero warnings and errors:

- 64 domain outcomes, including independent timers, stopwatch recovery/laps, storage and settings recovery, pending control edits, interaction cancellation, access preferences, nine retained battery-warning outcomes, six shelf alias/reset/disposal/source-preservation outcomes, and three completion-event lifecycle outcomes.
- 45 WPF UI outcomes exercising productivity, date/time, motion, battery warnings, seven compact activity regressions, two Settings navigation outcomes, routed drop negotiation, import isolation, translated hit regions, provider reentrancy, four native CF_HDROP target calls, and seven timer-audio outcomes. Sixteen productivity theme/DPI renders and four date/time DPI renders provide layout samples; they do not certify physical mixed-display operation.
- Eight release-provenance checks, including matching source fingerprints in Windows PowerShell and PowerShell 7. Packaging rejects skipped tests, Debug output, stale source, modified binaries, and unverified files.
- Three native observer lifecycle checks cover clean/idempotent shutdown, unexpected event-loop termination, and rejection of an otherwise passing capture after observer failure. These run in the required build and send messages only to their own observer thread.

A successful build is automated evidence, not certification of the complete Windows acceptance matrix. CI results must be checked for the pushed commit.

## P0/P1 implementation corrections

The detail footer now displays local date and time using the current Windows culture, with an accessible name and long-date help text. Reduced motion uses opacity fades with stationary island/detail transforms and immediate expansion geometry; enabling it during motion clears existing movement clocks. Low/critical battery warnings use actual Windows battery flags, remain session-local until a successful visible reveal, and retain the historical percentage separately from current charging data. Aborted reveals, interrupted detail fades, and disabled Controls preserve the notice; exact-ID acknowledgement cannot erase a newer critical warning. Battery events have no reveal or sound path.

Native measurement uses a fresh `--acceptance-profile` directory and independent IPC names. It neither loads the user's saved productivity data nor changes their startup registration or running Crest instance. Startup readiness is observed after a dispatcher turn; asynchronous hardware availability and independently cold-cache launches require additional checks.

The shelf handles preview drag events before child controls and publishes its final non-move effect before asynchronous work starts. It prefers an allowed Link and falls back to an allowed Copy; Move-only, disabled/ineligible, hidden, transparent-gap and overlapping-import cases are rejected. A bounded status remains in Shelf, and a completed import never rebuilds unrelated active controls. Atomic shelf clearing cancels queued/preparing work, including reentrant data-provider callbacks. Folder aliases share a slot, and detail/dock hit tests use their live transforms and opacity.

The priority audit corrected disabled modules leaking into compact metadata and artwork, battery warnings opening Music instead of Controls, and compact actions addressing a different activity from the displayed one. Compact status, icon, details action, pause/resume and accessible names now describe one enabled activity. Settings navigation retains its controls across page changes so Enter/Space activation preserves keyboard focus and Tab/Shift+Tab order. The hidden-window observer now permanently rejects unexpected loop exit, clears its active hook state, and records unhook failure. Native measurement summaries expose actual/requested duration and early exit alongside qualification flags.

## Historical timer completion sound and installed upgrade

The preceding timer-sound DLL was `98C806E924BDA5C9C35C5F1421E83E329E6B658D6136AF5E51AEE69A8BC38991`, with source fingerprint `3D761913BB1C9A55AFA58BB916F7F2401B968D70055BF2ADC9D95628EC26A222`. Timer completion uses an embedded original 3.54-second chime rather than relying on the Windows Asterisk assignment. Completion sound remains off by default; Settings → Modules → Test timer sound previews it without enabling or saving the preference. The owner's existing preference was enabled, and the Windows Asterisk assignment pointed to an existing sound file, so a missing or disabled sound scheme was not established as the cause of their report.

A real 20-second timer completed in an isolated desktop-only profile with sound enabled. Its saved phase was Completed with zero seconds remaining, no timer-audio error was logged, and the owner confirmed hearing the chime. The test instance quit gracefully. [Timer sound evidence](timer-sound-evidence.json) binds this historical observation to the preceding timer-sound candidate. Output volume and user preferences were unchanged by that test.

The packaged candidate was then installed over the owner's existing per-user Crest installation. All 492 distributed files matched the installed files, saved JSON data and the Windows startup preference were unchanged by the upgrade, and the matching installed app reached lifecycle readiness in the tray. The owner's timer-sound preference remained enabled. This existing-account upgrade does not establish clean-account installation, uninstall choices or a second hearing observation.

Silent injectable checks cover disabled and stale completion events, pending cancellation, pruning stale queue entries, coalescing simultaneous completions, preview with sound disabled, shutdown, the Settings preview action and embedded WAV validity. Local completion subscribers are revalidated after reset/disposal and before compatibility notifications. Playback runs on a worker; disabling sound, reset and shutdown cancel queued completion audio. Completion never reveals the island. Endpoint disconnect, actual sleep/resume and the wider native matrix remain separate acceptance work.

## Historical owner-operated desktop shelf acceptance

With the regular instance paused, the owner tested the preceding shelf candidate in an isolated desktop-only profile. They confirmed desktop-drag reveal/drop, preserved originals after import, drag-out into a distinct folder, missing-source display and open rejection after deleting an original, reference removal without deleting the original, suppression while File Explorer was active, and an occupied shelf becoming empty after restart. The isolated instance quit gracefully; the existing instance and its saved data were preserved.

Part of the owner test used their own file rather than the supplied scratch file. Its content hash was not recorded. Byte-for-byte preservation is independently checked by the domain/routed/native OLE fixtures. Passive observations captured 360 samples and 43 visible island samples, all with the verified desktop foreground; the separate diagnostic policy does not reproduce the actual app's registered-HWND map, so its eligibility field cannot label legitimate visible islands as incidents. [Historical shelf evidence](shelf-native-evidence.json) binds these observations to DLL `4C822F23A46C6B01021ACC0AD156A302227183796B74359E078499DAF0074B16`. AC17 passed for that binary and requires requalification for the current candidate; the broader visibility, latency, display and accessibility matrices remain open.

## Optional access over other apps

**Settings → Behavior → Allow Crest over other apps** is off by default. When enabled, the selected-display hover trigger and separately enabled Ctrl+Alt+Space shortcut can open Crest while another app is active, including File Explorer. Settings, Pause, lock, suspend, and secure-desktop suppression remain required. Background timer/media events never reveal the island automatically.

Four access outcomes cover default and legacy/corrupt/reset preferences, saved opt-in, foreground-app and registered-input access versus unregistered own-window exclusion, shared environment guards, and cancellation/fresh-entry behavior when modes change.

A read-only native comparison loaded an earlier locally packaged DLL on the normal input desktop at 125% effective DPI with all five hooks installed. A browser remained foreground throughout: access was rejected with the option off, allowed with it on, and rejected after it was switched off again. Only the diagnostic process's in-memory preference changed. Actual pointer/hotkey input across apps, fullscreen presentation, and secure transitions remain native acceptance work.

The original P1 desktop-only cases must run with `AllowOverApps=false`. An explicitly requested overlay is expected when the option is enabled. Changing the implementation invalidates earlier DLL/source-bound evidence.

## P1 release status

P1 qualification remains open. The public [gate record](release-gates.json) lists 22 applicable acceptance criteria and nine measured quality gates. The preceding priority candidate had 12 passing gates and 19 open gates. Recorded passes are AC01–05, AC07, AC09–10, AC17, AC23, PER-idle and PER-memory, bound to their recorded DLL/source; the new P2 source requires fresh qualification. AC18 clipboard is now implemented and has automated native listener evidence, but still requires candidate-specific acceptance. AC19 optional integrations remains deferred. The owner previously deferred the additional display, clean-account/VM and administrator recording work. The [native acceptance runbook](P1_ACCEPTANCE_RUNBOOK.md) gives those procedures.

`scripts/verify-release-gates.ps1` writes a candidate-bound report to `artifacts/release-gate-report.json`. A gate marked `pass` requires current observed evidence, matching DLL/source fingerprints, and checked evidence-file hashes. `scripts/release.ps1 -RequireP1Gates` refuses qualification while required evidence is open, failed, or stale. Default packaging produces a clearly labelled release candidate.

Raw native window/PID snapshots, resource samples, settings backups, and local screenshots are retained locally and excluded from the source commit and application packages. This public report summarizes their relevant results without publishing the recordings. Evidence must be collected again for the precise candidate being qualified.

## Historical priority candidate native evidence

The preceding priority candidate completed a **600.336-second all-hidden capture with 121 samples**. Average Crest CPU was **0.0301% of total device CPU**; peak resident working set was **152,748,032 bytes (145.67 MiB)**. The observer remained healthy with zero visible samples, zero top-level show events, no enumeration failures, no early exit and an unchanged DLL. All five core modules were enabled. A separate isolated Settings instance was exercised during part of the capture; the measured process remained hidden. It quit gracefully. [Historical resource evidence](priority-resource-evidence.json) qualified PER-idle and PER-memory for that binary.

Launch-to-dispatcher readiness was **3365.598 ms**, exceeding 3000 ms in this isolated observation with uncontrolled cache and a concurrent UI-profile launch. This does not establish installed cold-start readiness; that gate remains pending. Resource results do not qualify actual frames, latency, hidden rendering work or battery impact.

Native Windows keyboard checks confirmed Tab reaches Appearance, Enter retains Appearance focus after page activation, Tab reaches Behavior, Enter retains Behavior focus, and Shift+Tab returns to Appearance. Maximized Settings minimized and reopened maximized through the isolated single-instance entry point. Exact-candidate HWND snapshots found the island hidden with Settings minimized and restored. [Settings evidence](priority-settings-evidence.json) records these seven observations as partial AC09/AC21 evidence. Full island keyboard and screen-reader acceptance remains open.

The owner then tested the exact candidate with the normal instance paused. Clear-desktop reveal/dismissal, Chrome/Explorer maximized and ordinary foreground suppression with working app controls, strict visible-window coverage, stationary Win+D recovery, and 100 grouped interrupted cycles passed. Start, Search, Task View, Quick Settings and Widgets suppressed reveal; tooltip/app switching, Settings and valid/invalid folder launcher outcomes passed. These are owner-operated outcomes, supplemented by passive native snapshots rather than measured presentation or input latency. [Desktop evidence](priority-desktop-evidence.json) binds the individual tested cases to the candidate.

The desktop shelf accepted an owned synthetic file through drag reveal and dragged it out to a distinct desktop folder. Root verified original/copy hashes matched before moving only the synthetic source for the missing-file test. Missing-source display, safe opening failure and reference removal passed; an occupied folder shelf became empty after restart. Actual sleep of at least 75 seconds and lock/unlock required fresh entry; the owner confirmed the one-minute timer completed and the paused five-minute timer was unchanged. Root observed saved phases before the owner intentionally cleared those fixtures. A new paused fixture subsequently retained **296.171 seconds exactly** across graceful restart. Native clock adjustment and future running UTC-deadline restart remain separate work.

Explorer restart changed the desktop shell PID from 5988 to 23752; owner fresh-entry and tray Pause/Resume checks passed. Both test instances quit gracefully. The original user process remained running, the owner resumed it, and the verified synthetic desktop files/folder were removed. Raw profiles and diagnostic captures remain local. An initial hover attempt was inconclusive because the computer-use tool left full-screen cursor overlays that strict mode correctly rejected; the tool was closed before the passing retry.

The earlier preliminary capture was deliberately stopped before source corrections and records early exit with false qualification flags. It is local provisional evidence and is not used for passing gates.

Windows Performance Recorder was present and idle, but starting GPU/DesktopComposition/CPU capture failed with `0xc5585011` (failed to enable performance profiling policy). A subsequent status check confirmed no recording remained. Actual frame and hidden-render analysis therefore requires an elevated operator as described in the runbook. Clean-account distribution also requires a separate account/VM because this account contains the owner's installation and startup registration.

The current resource snapshot records 1920 × 1080 at 125% DPI. Refresh rate and power scheme were not captured for that run; the public summary explicitly marks them unrecorded. Full Section 11 baseline/presentation qualification remains open.

## Historical shelf candidate resource measurements

The changed shelf/input candidate completed a fresh 600.856-second all-hidden run with 122 samples. Average Crest CPU was **0.0230% of total device CPU**; maximum resident working set was **152,539,136 bytes (145.47 MiB)**. No visible samples, transient top-level show events, enumeration failures, early exit or candidate changes were recorded. The passive observer remained healthy. All eight startup/native subset checks passed, and the test process quit gracefully. [Historical resource evidence](native-resource-evidence.json) binds these results to the preceding shelf candidate. These measurements do not qualify the changed timer-sound candidate.

One launch-to-dispatcher-readiness observation was 2269.648 ms. It does not establish installed cold-cache startup or synchronous hardware readiness. Frame presentation, suppression/control latency, hidden rendering traces, battery impact and other physical acceptance remain separate gates.

## Previous P0/P1 candidate resource measurements

The preceding P0/P1 correction candidate ran in a fresh acceptance profile on Windows 11 build 26300, Ryzen 5 5600H, 12 logical processors, 7.34 GiB RAM, 1920 × 1080 at 125% DPI, and the Balanced power scheme. The actual app and separate diagnostic each installed five native hooks. All 12 candidate HWNDs were hidden, the normal input desktop and Explorer desktop identity were established, and an unrelated foreground app was rejected by the default policy. This historical capture does not qualify the later shelf/input candidate.

The completed 600.869-second hidden capture contained 122 samples: average Crest CPU was **0.0245% of total device CPU**, and maximum resident working set was **152,772,608 bytes (145.70 MiB)**, below decimal 200 MB. It recorded zero visible samples, zero transient top-level show events, a healthy observer, no enumeration failures, no early exit, and an unchanged DLL/source identity. The isolated app quit gracefully afterward. These observations qualified only that earlier binary; its raw records remain local.

External launch-to-dispatcher-readiness was 2445.466 ms. Cache state was uncontrolled, the app used a fresh portable acceptance profile, and hardware initialized asynchronously, so the installed cold-start gate remains pending. This capture does not qualify frame presentation, feedback/suppression latency, hidden rendering work, battery impact, physical interaction, or clean-account distribution.

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
| AC01–12 default visibility | Current clear-desktop/app/shell and 100-cycle cases pass; independently distinguished interruption phases, full registered control/popup operation, active media background behavior and second-display cases remain open |
| AC13 media | Active playback/seek, multiple sessions, preferred player, session removal |
| AC14–15 hardware | Hardware-key changes, endpoint disconnects, charging transitions, unavailable hardware |
| AC16 lifecycle | Actual sleep/lock and paused restart pass; native clock adjustment and future running UTC-deadline restart remain open |
| AC17 desktop shelf | Current owner-operated drag/drop/out, missing source, removal, original/copy hashes and occupied shelf restart pass |
| AC20 display | Physical 100/150/200% scaling, mixed DPI, disconnect/fallback, reachable geometry |
| AC21 accessibility | Keyboard-only navigation, screen reader, high contrast, Windows reduced motion |
| AC22 performance | Actual presented-frame tracing, feedback/suppression p95/p99, installed cold readiness, hidden rendering trace and repeatable battery impact; current hidden CPU/memory gates pass |
| AC23 recovery | Current Explorer restart, lock/unlock, tray Pause/Resume and graceful Quit pass; physical display changes remain AC20 work |
| AC24 distribution | Clean-account install/upgrade/uninstall and both data-retention choices |
| Optional app access | Actual hover/shortcut and interaction over representative apps, fullscreen behavior, secure/lifecycle transitions, return to desktop-only mode |

The default policy requires no unexpected visible-over-app incident and no retained hidden hit region. Opt-in app access permits requested overlays; hidden hit regions remain prohibited in both modes.

# P0/P1 native acceptance runbook

Collect the remaining physical evidence from PRD v1.1, Sections 11–13. These are procedures, not completed results. Record status in [release-gates.json](release-gates.json) and [TEST_REPORT.md](TEST_REPORT.md).

## Prepare one verified candidate

- Build/test Release using [BUILD.md](BUILD.md). Freeze `src`, `tests`, `assets`, and `scripts`. Record the tested `Crest.dll` SHA-256, `.crest-build-verification.json` source fingerprint, and installer/package hashes. Changed inputs require a new build and evidence.
- Use the owner's Windows 11 laptop at native resolution and normal power settings. Record Windows build, CPU/RAM/GPU, resolution/refresh/DPI, displays, power scheme, and charging state. Mark unavailable hardware cases unexecuted.
- Pause the regular Crest instance, then start the verified candidate with `--acceptance-profile <new absolute folder> --settings`, using a fresh folder under `tmp`. Restore the regular instance after the test candidate quits.
- Set **Strict desktop mode on**, **Allow Crest over other apps off**, and all five core modules enabled. Sound/startup/reveal shortcut start off; enable an option explicitly for its case. Close Settings before hover tests.

The agent can build/test, inspect native hooks/hidden windows, and capture isolated resources with `scripts/Measure-NativeAcceptance.ps1 -DurationSeconds 600`. Physical interaction, cold installed startup, accessibility, display transitions, and frame delivery need separate evidence.

## Run the physical cases

1. **Desktop, shell, and 100 cycles — AC01–07, AC10–12.** Start hidden. Press Win+D and minimize remaining ordinary windows. Fresh trigger entry reveals once after dwell; early departure must not flash or block input. A stationary pointer must not reopen after eligibility returns. Test ordinary/maximized Chrome and Explorer, including their top-edge controls. Clicking wallpaper while an app remains visible suppresses strict mode; minimizing it permits hover. Include partially off-screen/inactive-virtual-desktop windows, desktop icons, and taskbar interaction. Tally **25 normal entry/exit cycles, 25 early cancellations, 25 closing reversals, and 25 app-switch interruptions during dwell/reveal/expansion**; check jumps, duplicates, stuck states, and hidden hit regions. Test Start, Search, Task View, Quick Settings, notification center, Widgets, fullscreen, and foreground apps on another display. Complete a timer/change tracks with Chrome active: no reveal; pending completion appears after fresh desktop hover.

2. **Focus and launch — AC08–09, AC21.** Operate audio/media controls and player popup; registered interaction remains usable. Type a timer name with the pointer outside, then dismiss with Escape. Settings hides the island. Launch an app/file/folder/website/Windows shortcut; foreground changes hide Crest. A removed scratch target produces an actionable in-island error.

3. **Media and hardware — AC13–15.** Use two Windows media-session players: playback/seek, preferred selection, session removal, and empty-state recovery. Test hardware/Windows volume changes, output disconnect/reconnect, Windows brightness changes, and charging transitions. Show only Windows-reported battery/estimate values; explain unsupported capabilities. Test warning retention when Windows actually reports low/critical state. Repeat local controls offline; restore hardware settings.

4. **Desktop shelf — AC17.** Use scratch desktop files/folders; hash contents before/after. Drag through trigger reveal/drop; originals remain. Drag out to a distinct folder and compare contents. Move/rename a source: missing state and safe open rejection. Remove a reference: original survives. Check the 20-reference cap. Quit/restart the profile: empty shelf. Explorer-foreground drag must not summon Crest.

5. **Recovery/timekeeping — AC01, AC16, AC23.** Start one timer and pause another. Lock/unlock, sleep past a deadline, and restart Crest: running time includes sleep and recovers against saved UTC; paused duration remains. Supervise forward/backward clock changes and restore Windows time settings: live countdown stays monotonic; restart uses saved deadline. Restart **Windows Explorer** through Task Manager. Recovery/Resume require fresh entry, never stationary-pointer reopening. Tray Pause disables hover/shortcut; Quit leaves no test process, overlay, or trigger.

6. **Display/accessibility — AC20–21.** Physically test 100/125/150/200% DPI and light/dark themes: all modules/Settings remain readable and reachable. Test mixed DPI and display disconnection where available; primary fallback stays hidden until hover. Enable reveal shortcut; verify eligibility, Tab/Shift+Tab, Enter/Space, slider keys, focus, and Escape. Narrator announces meaningful names/roles/media/audio/battery/timer values. Test live Windows high-contrast/reduced-motion changes: stationary fades/immediate geometry. Contrast targets: ordinary text 4.5:1; large text/boundaries 3:1. Restore Windows preferences.

7. **Clean-account distribution — AC24, PER-readiness.** Install the hashed package as a normal user in a fresh account without SDK/runtime installation. Verify quiet tray launch, startup initially off, single instance, shortcuts, and startup opt-in. Upgrade from the supported previous package with saved preferences/timer state; verify migration. Uninstall with **retain**, reinstall/check retained data, then uninstall with **delete**; executable/startup/registration components and chosen data disappear, shelf originals survive. Measure independently cold installed readiness; record reboot/cache conditions. Warm/portable observations do not qualify cold startup.

Test optional **Allow Crest over other apps** separately over apps/fullscreen using hover/shortcut/controls. Settings, Pause, lock, suspend, and secure desktop still suppress; background events cannot reveal. Turn it off and repeat desktop-only suppression. Record the mode for every observation.

## Measure performance on the same candidate

| Gate | Required observation |
|---|---|
| PER-readiness | Installed interactive readiness within 3,000 ms, island hidden; record cold/cache conditions |
| PER-hover | About 360 ms including the configured 120 ms dwell and 240 ms reveal |
| PER-feedback | Actual visible control feedback within 100 ms |
| PER-suppression | Native foreground-to-hide/input-disable: p95 ≤100 ms, p99 ≤150 ms, maximum ≤250 ms under normal load |
| PER-frames | Actual displayed frames in a repeated 30-second interaction sequence: p95 interval ≤20 ms, p99 ≤33.4 ms |
| PER-idle | Ten minutes entirely hidden, average Crest CPU <1% of total device CPU; healthy observer and unchanged candidate |
| PER-memory | Warm core resident working set ≤200,000,000 bytes, no AI model |
| PER-hidden-rendering | Trace armed/ineligible intervals: no continuous animation or rendering loop |
| PER-battery | Repeat matched idle runs with and without Crest; record duration, power/display conditions, and measured battery impact |

Full graphics/kernel recording needs an elevated operator; the agent has neither elevation nor Performance Log Users membership. In an elevated terminal, check `wpr -status`, preserve unrelated recordings, and choose a new output filename. When idle:

```powershell
wpr -start GPU -start DesktopComposition -start CPU -filemode
# Perform the 30-second interaction sequence, then a separately timed hidden interval.
wpr -stop C:\Temp\crest-presentation.etl
```

Analyze Crest's process/surfaces and **displayed frames** with WPA or a suitable analyzer; report event loss/limitations. Global DWM cadence, refresh rate, WPF callbacks, and screenshots do not prove application frame delivery. Correlate native input/foreground timestamps with visible feedback and visibility/hit-test removal, including Windows delivery/dispatcher delay. Annotate motion and hidden intervals; ordinary idle gaps are not dropped animation frames.

## Record and close evidence

Record gate IDs, date/time, expected/observed outcomes, mode, DLL/source identity, baseline, and limitations. Keep raw traces/screenshots under local `artifacts`; publish sanitized summaries and SHA-256 digests. Unexpected reveal, stale reopening, hidden hit region, or animation continuing over apps fails acceptance despite aggregate latency. Unexecuted cases stay pending.

Update only gates supported by current candidate-bound evidence, then run `scripts/verify-release-gates.ps1 -RequireP1Gates`. P1 qualification requires every applicable acceptance/quality gate. Clipboard AC18 and optional-integration AC19 remain outside this P1 gate set. The requested P2 local features have separate automated verification and require native acceptance for the release candidate.

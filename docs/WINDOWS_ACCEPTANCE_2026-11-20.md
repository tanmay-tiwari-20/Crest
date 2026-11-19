# Windows acceptance — Crest 0.2.1 and historical 0.2.0

## Final 0.2.1 candidate

Reviewed 20 November 2026, Asia/Calcutta. The [final sanitized verification](p2-windows-verification-0.2.1.json) records commit [950b8df7f63f3f17c4a45709ab36300ca9d16211](https://github.com/tanmay-tiwari-20/Crest/commit/950b8df7f63f3f17c4a45709ab36300ca9d16211), the exact downloaded GitHub 0.2.1 assets, and fresh candidate-bound observations. The assets were verified while the release was a draft, then published as the [0.2.1 prerelease](https://github.com/tanmay-tiwari-20/Crest/releases/tag/v0.2.1) on 20 November 2026 at 03:21 IST. Post-publication asset digests match the tested downloads. **Two gates pass, 14 have partial coverage and 15 remain pending. Crest remains a prerelease.**

| Identity | SHA-256 |
|---|---|
| Actual tested DLL | `C5B5AB77A4FC6CFA7134024BE44CB4455D1EA5068ACF3FE5E87B1B13ED902195` |
| Source fingerprint | `FECD8248B9B3B91417AB52541AC19EBD4CEFDD3EF5721FFECA7AB46387E65E7B` |
| Portable ZIP | `8E2F5AF6FD23E29D2F7997560D7F5AC0322969326A64511335B5AD606EA9A80F` |
| Installer | `8C48DD2410ED1080923CFB7D3ACFA8BFF6690434E5547642C5FD64BDFBB804A2` |
| SHA256SUMS.txt | `E66CA5685BD91105EB1C935B27DFF358DF1679101990C481802C7049C28D29E8` |

Both Windows workflows passed: [44609376617](https://github.com/tanmay-tiwari-20/Crest/actions/runs/44609376617) and [44609182129](https://github.com/tanmay-tiwari-20/Crest/actions/runs/44609182129). All three downloaded asset digests matched GitHub. Unlike 0.2.0, the downloaded ZIP contains its build-verification manifest. Its **492 distribution files plus the verification file** were present; manifest hashes, Release/source identity and self-contained x64 distribution checks passed. The downloaded DLL/source match the canonical local candidate. Hardware and media fixtures originally recorded that canonical local origin; subsequent byte correspondence establishes the same tested binary, rather than transferring results between different binaries.

The patch fixes completed-countdown compact action/detail routing, includes the build manifest in packages, and rejects historical or unrelated gate evidence. It also repairs detached clipboard test snapshots and diagnostic trigger/DPI geometry. The exact downloaded ReadyToRun DLL passed **111 UI outcomes**. Canonical local Release verification passed **97 domain outcomes, 111 UI outcomes, 11 release-provenance checks, 16 gate-evidence checks, 10 diagnostic geometry checks and three observer checks**, with zero warnings/errors.

Fresh native resource evidence lasted **600.136 seconds**, with **121 entirely hidden samples**, zero window-show events or enumeration failures, a healthy observer and an unchanged candidate. Average total-device CPU was **0.0456%**. Maximum warm working set was **157,081,600 bytes (149.80 MiB)**. These qualify only PER-idle and PER-memory. Dispatcher readiness was **2,948.890 ms** in one warm/uncontrolled-cache portable launch; installed cold readiness remains pending. The baseline was Windows 11 x64 build 26300, Ryzen 5 5600H, 12 logical processors, 7.34 GiB physical memory, one 1920 × 1080 display at 125%, OS-reported 144 Hz and Balanced power. Strict desktop was on, app access off, and resource profiles used default System theme/motion, core modules, with startup, sound, clipboard capture and reveal shortcut off.

Seventeen native API outcomes passed: nine real hardware/provider checks and eight Windows SMTC media checks. Endpoint enumeration matched Windows, real volume/mute writes and alternate-output switching were verified independently across all three default roles, and every original role/endpoint volume/mute value was restored. Two owned media sessions stayed muted at volume zero while preferred selection, playback, pause, timeline seek, resume, removal fallback and empty recovery were tested. Owned hosts were stopped. Audible external players, product media UI, hardware keys/disconnection, radio/charging/brightness transitions and settings fallback remain open.

Six actual Windows keyboard observations passed in the downloaded candidate's Settings: Tab focus through Overview/Appearance/Behavior, Enter page activation retaining focus, and Shift+Tab returning to Appearance. They do not qualify island keyboard, Escape, Narrator or high contrast. A fresh-profile quiet/single-instance smoke stayed hidden with five hooks and startup/clipboard off; the secondary launch exited zero while retaining the first process, which then quit gracefully. Installer registration/shortcuts/clean-account checks were not exercised.

Real activity recovery retained a running UTC deadline with **30.021 seconds** left before restart, and a paused timer at exactly **90 seconds**. A healthy observer began before the timer/reminder deadlines and recorded **49.970 seconds, 50 samples**, Chrome foreground, zero visible windows/show events and no enumeration failures. Final saved state showed the timer complete, paused timer still 90 seconds, focus waiting after work round 1/2, reminder due, and running stopwatch elapsed **68.9049121 seconds**. Both test processes quit with exit code zero. This is partial background/restart evidence; lock/sleep, clock changes, repeated snooze and interactive phase progression remain open.

| Gate | Final 0.2.1 status | Remaining scope |
|---|---|---|
| AC01 | Partial | Startup/restart hidden; stationary Win+D/unlock/resume recovery pending |
| AC02 | Pending | Physical clear-desktop hover/reveal/dismissal |
| AC03 | Pending | Physical dwell cancellation/input interception |
| AC04 | Partial | Foreground rejection/hidden samples; full Chrome/Explorer interaction pending |
| AC05 | Pending | Strict visible/minimized/off-screen/virtual-desktop matrix |
| AC06 | Pending | Distinct dwell/reveal/expansion app-switch cases |
| AC07 | Pending | Windows panels and fullscreen |
| AC08 | Partial | Exact control/native fixtures pass; actual registered popups/interaction pending |
| AC09 | Partial | Settings focus/keyboard; external launcher matrix pending |
| AC10 | Pending | Physical reversal and all 100 native cycles |
| AC11 | Partial | Background timer/focus/reminder no-reveal subset; track-change matrix pending |
| AC12 | Pending | Second physical display |
| AC13 | Partial | Two actual synthetic SMTC sessions; external audible players/UI pending |
| AC14 | Partial | Actual API audio changes/readback/restoration; keys/disconnect pending |
| AC15 | Partial | Actual capability/current power states; physical transitions pending |
| AC16 | Partial | Real restart/state/deadline subset; lock/sleep/clock changes pending |
| AC17 | Partial | Native OLE/reference fixtures; physical desktop drag/drop/out/recovery pending |
| AC20 | Partial | Actual 125% and static layouts; other physical scales/mixed displays pending |
| AC21 | Partial | Six Settings keyboard observations/named controls; full accessibility pending |
| AC22 | Partial | CPU/memory pass; other measured budgets pending |
| AC23 | Partial | Quiet single instance/graceful quit/restart; Explorer/Pause/hidden input pending |
| AC24 | Pending | Clean-account installation/migration/retain/reinstall/delete |
| PER-readiness | Pending | Warm portable 2,948.890 ms; installed cold procedure unexecuted |
| PER-hover | Pending | Actual eligible hover timing |
| PER-feedback | Pending | Actual visible feedback latency |
| PER-suppression | Pending | Native foreground-to-hide/input-disable percentiles |
| PER-frames | Pending | Actual displayed-frame attribution/intervals |
| PER-idle | Pass | 600.136 s entirely hidden; 0.0456% total-device CPU |
| PER-memory | Pass | Maximum 157,081,600 bytes |
| PER-hidden-rendering | Pending | Actual graphics/rendering trace |
| PER-battery | Pending | Repeated matched battery comparison |

The normal process **28404 was never quit**. Final Settings bytes match the initial backup, Windows startup is unchanged, the user resumed clipboard capture, the reveal shortcut remains off, and all test Crest processes are closed. Retaining the normal process preserves its unpinned in-memory shelf references. The regular installation was not replaced or uninstalled for testing. The initial 0.2.0 clipboard fixture did fail to restore original Unicode text; the owner re-copied it. The repaired 13-outcome fixture and later exact-candidate 111-outcome suite verified detached formats/Unicode restoration and history preservation. Continuous clipboard preservation through the initial failure is not claimed.

Raw traces, profiles, backups and private state remain ignored. See the JSON for sanitized outcomes and raw-file digests, and the [prepared operator procedures](#prepared-operator-procedures) below for elevation, clean account, displays, accessibility, recovery and battery work. Optional app-access mode remains a separate unexecuted case. Historical evidence below keeps its original candidate identities.

## Historical published 0.2.0 acceptance

Reviewed 20 November 2026, Asia/Calcutta. This report concerns the exact [published v0.2.0 prerelease](https://github.com/tanmay-tiwari-20/Crest/releases/tag/v0.2.0), commit `cdb256058ad219f4ca39648b5078bdc5d3639718`. The [sanitized evidence summary](p2-published-0.2.0-acceptance.json) qualifies **PER-idle and PER-memory only**. It does not qualify the remaining P0/P1 gates or a changed 0.2.1 binary.

## Candidate identity and baseline

| Item | Observed identity or condition |
|---|---|
| Actual tested published DLL | `5DB16954608DFBA410DDA40476405FDFA2AD62B5B0C8CAB87A7BBE6181E7B5A3` |
| Release source fingerprint | `9028DB144AA8A6A8BC75959155AE046D6155B86F8777A1E8AE3308D0977E7DF0` |
| Portable ZIP SHA-256 | `FE0301C179729D4298B1F330ECF65B12CA829ECD323669EE7D5D9B11EEFEDDE5` |
| Installer SHA-256 | `1CCAB230130214C858A50B8683989513E905E6A8C55105A49EBFF477A594F4CE` |
| SHA256SUMS.txt SHA-256 | `AC91D391581E27D228B4D6CFEED78DE5222EC17C404D8AE4C17B0EBB5EB74BD8` |
| Windows/hardware | Windows 11 build 26300, x64 Ryzen 5 5600H, 12 logical processors, 7.34 GiB usable physical memory |
| Display/power | One physical 1920 × 1080 display, 125% scaling, OS-reported 144 Hz, Balanced power scheme |
| Resource-test preferences | Strict desktop on; Allow Crest over other apps off; System theme/motion; normal size/speed; 120 ms dwell and 250 ms grace; Music, Controls, Timer, Launch and Shelf enabled; startup, sound and reveal shortcut off; clipboard capture off |
| Regular installed process | Historical local P2 preview, DLL `649244A9F08AF5C437C7423FA179A75B9187B9973E0E7312B6BB8A4D40779937`, reporting 0.1.0; retained process 28404 |

Downloaded ZIP, installer and checksum-file hashes matched GitHub's asset digests; both package hashes also matched SHA256SUMS.txt. The 476 runtime files matched the original locally tested build record. That original local record and the passing release CI identify the release source; they are separate provenance sources from these downloaded files.

The published ZIP omitted `.crest-build-verification.json`. An **acceptance-only local sidecar** was constructed after the hash/runtime comparison so the existing isolated measurement script could validate the frozen package. Its 492 entries include distribution files. The sidecar was not downloaded from GitHub and is not proof that the published ZIP contained a build manifest. This packaging omission is being addressed for the subsequent patch; v0.2.0 assets remain unchanged.

The regular installed preview differs from the published DLL. The owner's manual checks described the copy currently installed on the laptop. Those observations belong to the 649244A9 preview and cannot close gates for 5DB16954 or a future patch. Earlier priority-build evidence at DLL 3810FBA3/source ACEF535F is also historical. The pre-test gate report correctly had **zero of 31 current passes**, including 12 stale historical passes.

## Fresh published-binary results

The exact downloaded DLL ran in a new acceptance profile with separate IPC. The actual candidate and the diagnostic each installed all five native hooks; all eight startup/native subset checks passed. The candidate remained hidden for **600.027 seconds**, with **121 samples**, zero visible samples, zero top-level show events, zero enumeration failures, a healthy observer, no early process exit and an unchanged DLL. Average total-device CPU was **0.0360%**. Maximum warm resident working set was **170,774,528 bytes (162.86 MiB)**. These close PER-idle and PER-memory for this binary.

Launch-to-dispatcher readiness was **7,303.449 ms** in one uncontrolled-cache portable observation. It exceeds 3,000 ms in that observation. It does not measure independently cold installed readiness, so PER-readiness remains unqualified. Five hooks, a screenshot, the configured motion duration and the reported refresh rate do not establish actual displayed frames, visible feedback latency, suppression percentiles or absence of hidden rendering.

An isolated WPF fixture loaded the exact published DLL and passed six of eight compact-productivity outcomes. Two failed: when a completed countdown, due reminder, completed focus session and running countdown coexist, the completed countdown's compact action and details open the reminder/focus controls instead of the countdown acknowledgement controls. These are confirmed routing regressions. The six passing controls checks do not exercise the actual desktop hooks or registered product popups.

A corrected standalone native provider fixture passed **nine checks with zero failures**. Published endpoint enumeration matched two actual Windows outputs. The published APIs changed real endpoint volume by one percentage point, toggled mute twice, and switched to the alternate output; independent Windows readback verified the switch for all three default-device roles. Final independent readback verified restoration of every original role and each snapshotted endpoint's volume/mute state, including the initially different communications default. These were API actions, rather than product UI clicks. Hardware keys, disconnect/reconnect and the sound-settings fallback remain unexecuted.

Earlier standalone audio failures were caused by the fixture's duplicate COM activation definitions and cached RCW collision. Corrected activation and the valid nine-check run supersede those attempts. They remain ignored diagnostic records and are not classified as published product defects.

The same provider fixture recorded seven real CPU/memory/network samples and verified its sampling boundary stops when marked hidden. Wi-Fi reported connected, Bluetooth off, brightness available at 0%, and battery 100%, plugged in and not charging. These are provider observations, with no radio/brightness/charging transition or active media session. They do not prove island visibility behavior or continuous hidden rendering.

The first fresh full WPF attempt aborted at a clipboard Unicode-restoration assertion. The earlier **107 UI outcomes are not a fresh passing result** for the published DLL. After the owner re-copied the original clipboard and capture was paused, the corrected isolated clipboard fixture passed **13 outcomes with exit code 0** against the exact published DLL. These cover detached snapshots, off-before-opt-in, Unicode capture/search, enable/resume/clear sequence exclusion, pause, privacy exclusion, accessible pin/copy/clear controls and disable. Final readback verified every snapshotted original format and exact Unicode text; the regular history file and paused state were unchanged. This fixes the test-fixture restoration failure and supplies fresh clipboard subset evidence. It does not rerun the full published UI suite or qualify every clipboard retention/capacity/restart case. No private clipboard text or history hashes are published here.

Two real isolated published-productivity runs exercised seeded running and paused countdowns, focus, a reminder and a running stopwatch. The first process quit gracefully and restarted before its running deadline; final saved state showed the countdown completed, the paused countdown retained exactly 90 seconds, focus completed its first work round of two and awaited manual continuation, the reminder was due, and stopwatch elapsed time continued. The first run has readiness/state snapshots rather than continuous visibility observation, so it establishes only restart/state subsets.

A second real background run began a healthy passive visibility observer **before the seeded running timer and reminder deadlines**. For **45.726 seconds and 46 samples**, Chrome remained foreground and Crest had no visible sample or top-level show event. The candidate DLL stayed unchanged, window enumeration succeeded and the observer stayed healthy. Final saved state showed the 20-second countdown completed, the paused countdown still at 90 seconds, focus at completed work round 1/2, the reminder due and the stopwatch running with elapsed time. The process quit with exit code 0. This supplies partial AC11/AC16/AC23 evidence; track changes, repeat snooze, work/break/final-round interaction, physical lock/sleep, supervised clock changes and hidden input regions remain unexecuted. The short background run is not another ten-minute CPU qualification.

## Acceptance matrix for the published DLL

“Partial” means only the listed subset was exercised. “Unexecuted” identifies a missing prerequisite or a procedure not run. Manual preview observations and earlier source/UI outcomes do not fill these gaps.

| Gate | Current status | Fresh evidence and remaining work |
|---|---|---|
| AC01 | Partial | Hidden startup with five hooks; stationary Win+D, unlock, resume and fresh-entry recovery unexecuted on this DLL |
| AC02 | Unexecuted | Clear-desktop physical hover/reveal/dismissal |
| AC03 | Unexecuted | Early dwell cancellation and input interception |
| AC04 | Partial | Foreground rejection and hidden native snapshot; Chrome/Explorer physical input and top-edge matrix outstanding |
| AC05 | Partial | One foreground scene rejected; visible/minimized/off-screen/virtual-desktop matrix outstanding |
| AC06 | Unexecuted | Independently timed app switches during dwell, reveal and expansion |
| AC07 | Unexecuted | Start, Search, Task View, Quick Settings, Widgets and fullscreen |
| AC08 | Partial with defect | Six compact WPF outcomes passed; two countdown-routing outcomes failed; real controls/popups outstanding |
| AC09 | Unexecuted | Settings/launcher foreground changes and harmless valid/invalid launches |
| AC10 | Unexecuted | Closing reversal and the full 100-cycle native sequence |
| AC11 | Partial | Timer/focus/reminder deadlines under Chrome with healthy 45.726 s passive observation and no reveal; track changes outstanding |
| AC12 | Unexecuted | Second physical display unavailable |
| AC13 | Partial | Native empty media-session state; playback, seek, multiple players and removal outstanding |
| AC14 | Partial | Real API volume/mute/switch and all-role readback/restoration passed; hardware keys and reconnect outstanding |
| AC15 | Partial | Current battery/brightness provider states; physical transitions and unavailable-capability hardware outstanding |
| AC16 | Partial | Real restart before deadline; completed running timer, exact paused 90 s, retained focus/reminder/stopwatch state; lock/sleep/clock changes outstanding |
| AC17 | Unexecuted | Published-binary desktop OLE/drop/drag-out, pins, relinking and original-file preservation |
| AC20 | Partial | 125% native geometry snapshot; physical 100/150/200%, mixed DPI and display fallback outstanding |
| AC21 | Partial | Selected compact accessible names/actions; keyboard-only product use, Narrator, high contrast and live reduced motion outstanding |
| AC22 | Partial | Ten-minute CPU and warm memory pass; other measured budgets outstanding |
| AC23 | Partial | Isolated graceful quit and restart passed; Explorer restart, tray Pause/Resume and invisible input regions outstanding |
| AC24 | Unexecuted | Disposable clean-account install/migration, retention/reinstall and deletion choices |
| PER-readiness | Unqualified | 7,303.449 ms uncontrolled-cache portable observation; installed cold procedure unexecuted |
| PER-hover | Unexecuted | Native eligible hover timing at default settings |
| PER-feedback | Unexecuted | Actual visible feedback within 100 ms |
| PER-suppression | Unexecuted | Native foreground-to-hide/input-disable p95/p99/max and continuous incident observation |
| PER-frames | Unexecuted | Actual displayed-frame attribution and intervals |
| PER-idle | Pass | 600.027 s entirely hidden, 121 samples, 0.0360% total-device CPU |
| PER-memory | Pass | Maximum 170,774,528 bytes, below decimal 200 MB |
| PER-hidden-rendering | Partial | Native statistics provider sampling stops; graphics/rendering trace outstanding |
| PER-battery | Unexecuted | Repeated matched battery-power comparison |

Optional **Allow Crest over other apps** native testing remains unexecuted for this DLL. Record that mode separately: deliberate overlays with it enabled are expected, while Settings, Pause, lock/suspend and secure desktop still suppress. Strict-mode overlay incidents and invisible hit regions fail acceptance regardless of aggregate timing.

P2 native work also remains for clipboard retention/capacity and durable restart behavior, repeated reminder snooze and interactive focus continuation/final rounds, selected capture dialogs and screenshot launch, harmless workspace failure isolation, pinned and unpinned shelf recovery, independent physical display profiles, and gestures on actual interactive controls. Clipboard capture boundaries/privacy markers/search/pin/copy/clear now have the fresh 13-outcome fixture evidence above; real provider, state and isolated WPF subsets remain partial evidence.

## Preservation and local evidence

The normal installation was not uninstalled to simulate a clean account. Its process was retained to preserve in-memory unpinned references. Existing local data was backed up under ignored `tmp/acceptance-2026-11-20/normal-data-before`; profiles, raw provider/clipboard diagnostics and recordings remain ignored. Clipboard capture was paused for isolation, and final restoration of the owner's preferred pause/running state belongs to the overall test-session closeout. This dated published-binary snapshot does not claim a future patch install or final session restoration.

Raw resource records are under `artifacts/native-acceptance/published-0.2.0-idle`. Downloaded assets and the locally constructed sidecar are under `artifacts/native-acceptance/published-0.2.0`. The sanitized JSON publishes only candidate/package identities, measured results, outcome descriptions and raw-file digests. It excludes clipboard text, media metadata, endpoint names, SSIDs/IP addresses and personal file contents.

## Prepared operator procedures

Use the [native runbook](P1_ACCEPTANCE_RUNBOOK.md), a frozen verified candidate and a new acceptance profile for every new binary. Save the original Windows preferences and regular Crest state first. Raw output must remain in a unique ignored folder.

**Elevated displayed-frame and latency trace.** An administrator operator should run `wpr -status` and preserve any unrelated recording. When idle, start `wpr -start GPU -start DesktopComposition -start CPU -filemode`. Perform and annotate 30 seconds of reveal/close/expansion/control interaction, then separate hidden armed and app-ineligible intervals. Stop to a new ETL file under the ignored native-acceptance folder. Analyze Crest's process/surfaces and actual displayed frames in WPA or a suitable analyzer, including event loss, foreground/input delivery, visible feedback and hit-region removal. Report frame p95 <=20 ms and p99 <=33.4 ms; suppression p95 <=100 ms, p99 <=150 ms and maximum <=250 ms. DWM cadence and WPF callbacks are insufficient. Recheck that recording has stopped.

**Clean-account distribution and cold readiness.** Use a disposable local Windows account or VM without Crest registration/data, SDK or separately installed runtime. Verify package hashes, quiet launch, single instance, shortcuts, startup initially off and explicit opt-in. Seed only synthetic activities, paused clipboard history, pins and selected captures; hash their original scratch files. Upgrade from the supported earlier package and compare state. Uninstall interactively choosing **No**, verify retained data, reinstall and verify recovery. Then uninstall choosing **Yes**, verify chosen test-account data/registration/startup/shortcuts removal and untouched reference originals. The silent lifecycle smoke does not test deletion. Measure independently cold installed readiness after a documented reboot/cache baseline; warm portable timing cannot close that gate. Never run this deletion matrix against the owner's account.

**Physical displays and accessibility.** Record each monitor's identity, resolution, refresh, work area and effective DPI. Test 100/125/150/200% scaling, all modules/Settings and keyboard reachability; mixed DPI; different appearance/trigger preferences on two displays; another-display foreground suppression; selected-display disconnect and hidden primary fallback; reconnect with retained profiles. A second display is required for those cases. Exercise Tab/Shift+Tab, Enter/Space, slider keys, Escape, Narrator names/roles, live high contrast and reduced motion. Restore the original display/accessibility preferences afterward.

**Physical hardware, recovery and time.** With a second actual audio endpoint, record all three Windows default roles and endpoint volume/mute before testing. Verify direct output switching through independent Windows readback, disconnect/reconnect and hardware keys, then restore every recorded value. Open and verify the Windows sound-settings fallback separately. Unplug/replug the charging cable and compare Windows status to Crest; do not deliberately drain into a critical condition. Lock/unlock and sleep across short synthetic deadlines, verify running versus paused state and fresh-entry recovery. A supervised operator should save automatic-time options before forward/backward clock changes, verify live monotonic timing versus UTC reminder/restart behavior, and restore/synchronize Windows time.

**Battery comparison.** Repeat paired battery-power idle runs with Crest absent and with the exact candidate armed/hidden. Match display brightness/refresh, power scheme, connectivity, background apps and duration; record energy/charge, uncertainty and discharge conditions. The ten-minute CPU result alone cannot establish battery impact.

The 0.2.1 source fixes were committed and pushed at [a55778793ae5c8d127adfe8d68021371d0880de7](https://github.com/tanmay-tiwari-20/Crest/commit/a55778793ae5c8d127adfe8d68021371d0880de7). Its local Release checks passed 97 domain outcomes, 111 UI outcomes, 11 provenance checks, 16 gate-evidence checks, 10 geometry checks and three observer checks, with zero warnings/errors. These are patch-source validation. Its DLL/source identity, CI, uploaded assets and fresh patch-native resource run belong in the current [test report](TEST_REPORT.md); this published-0.2.0 record makes no 0.2.1 native qualification claim. No v0.2.0 evidence transfers automatically to that candidate.

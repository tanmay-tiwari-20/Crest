# Windows acceptance — published Crest 0.2.0

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

The fresh full WPF suite aborted at a clipboard Unicode-restoration assertion. The earlier **107 UI outcomes are not a fresh passing result** of this acceptance run. The failure concerns the test fixture's snapshot/restoration reliability; its repair and rerun are separate from published product acceptance. The owner re-copied the original clipboard. Further clipboard mutations require the regular history listener to be confirmed paused; an unsafe preflight aborts before synthetic clipboard operations. No private clipboard text is included in this report.

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
| AC11 | Unexecuted | Real timer/focus/reminder completion and track changes while another app is active |
| AC12 | Unexecuted | Second physical display unavailable |
| AC13 | Partial | Native empty media-session state; playback, seek, multiple players and removal outstanding |
| AC14 | Partial | Real API volume/mute/switch and all-role readback/restoration passed; hardware keys and reconnect outstanding |
| AC15 | Partial | Current battery/brightness provider states; physical transitions and unavailable-capability hardware outstanding |
| AC16 | Unexecuted | Published-binary lock/sleep, running-deadline and paused recovery, supervised clock changes |
| AC17 | Unexecuted | Published-binary desktop OLE/drop/drag-out, pins, relinking and original-file preservation |
| AC20 | Partial | 125% native geometry snapshot; physical 100/150/200%, mixed DPI and display fallback outstanding |
| AC21 | Partial | Selected compact accessible names/actions; keyboard-only product use, Narrator, high contrast and live reduced motion outstanding |
| AC22 | Partial | Ten-minute CPU and warm memory pass; other measured budgets outstanding |
| AC23 | Unexecuted | Published-binary Explorer restart, tray Pause/Resume and invisible-input-region removal after Quit |
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

P2 native work also remains for clipboard sequence exclusion/privacy markers/retention/capacity/pins, repeated reminder snooze and focus continuation/restart, selected capture dialogs and screenshot launch, harmless workspace failure isolation, pinned and unpinned shelf recovery, independent physical display profiles, and gestures on actual interactive controls. Real provider and isolated WPF subsets above remain useful partial evidence.

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

The 0.2.1 fixes, build, CI, asset verification and any fresh patch-binary acceptance are recorded separately when completed. No v0.2.0 evidence transfers automatically to that candidate.

# Crest 0.2.1 desktop acceptance — 24 November 2026

The desktop captures were collected late on 23 November; automated checks and review continued into 24 November in Asia/Calcutta. The exact candidate remains a prerelease with **2 passed, 16 partial and 13 pending gates: 29 gates remain open**. This run adds partial AC02 and AC10 coverage and completes no new acceptance gate.

Candidate DLL: `C5B5AB77A4FC6CFA7134024BE44CB4455D1EA5068ACF3FE5E87B1B13ED902195`. Source fingerprint: `FECD8248B9B3B91417AB52541AC19EBD4CEFDD3EF5721FFECA7AB46387E65E7B`. The downloaded candidate and canonical source stayed unchanged. [Structured evidence](desktop-acceptance-0.2.1-2026-11-24.json) binds the current observations and raw-file digests. The [20 November record](WINDOWS_ACCEPTANCE_2026-11-20.md) retains its dated results; its CPU/memory passes remain valid for this unchanged DLL/source.

## Conditions and setup

Read-only machine inspection recorded Windows 11 build 26300, Ryzen 5 5600H with 12 logical processors, 7,883,554,816 bytes of physical RAM, AMD integrated graphics driving 1920 × 1080 at an OS-reported 144 Hz, and the Balanced power scheme. The RX 6500M was also enumerated, without active display counters. The physical probe reported one display at 125% scaling. Refresh metadata does not establish Crest's displayed-frame performance.

The candidate used a separate acceptance profile and IPC identity. Strict desktop mode was enabled, access over other apps was disabled, and default trigger settings were 240 × 3 DIP, 120 ms dwell and 250 ms grace. The owner confirmed the regular instance was **“Paused”**. The actual **Close and try Crest** button was exercised; a quiet secondary launch exited with code 0 and retained the primary candidate.

An initial `dotnet.dll`-hosted diagnostic was DPI virtualized and is excluded from physical geometry acceptance. The corrected manifested probe reported the 125% display and a physical trigger rectangle at x=810, y=0, width=300, height=4 pixels. Computer Use cursor overlays initially produced two strict-mode blockers. Resetting its node kernel removed them; the pre-hover snapshot then showed desktop eligibility, five native hooks and all 17 candidate top-level windows hidden.

## Owner reports and observed behavior

Owner reports are recorded separately from analysis. The exact final response **“All passed”** referred to a requested batch of five quick cancellation attempts, five closing reversals and Pause/Resume. It does not assert completion of the full PRD matrix. Reports of Settings returning to FxSound and the initial failure to appear are retained as paraphrases, without inventing exact quotations.

The initial attempt was ineligible, rather than evidence of a stuck controller. Independent foreground inspection during capture 03 confirmed that closing Settings returned focus to FxSound; Win+D restored the wallpaper desktop and fresh entry revealed Crest. The **Close and try Crest** flow therefore retains a Settings-to-desktop focus usability gap. No source fix was made during this acceptance run.

Capture 04 ran for 180.017 seconds with 8,282 samples, a maximum sample gap of 77.194 ms, a healthy observer, no enumeration failures and an unchanged DLL. It recorded 985 visible samples and 10 candidate top-level show events. Time-filtered controller logs contain **18 Open states and 12 Closing → Revealing transitions**. These are partial reveal/reversal observations; show events and sampled rectangles do not establish frame delivery, continuous smoothness, duplicate-free presentation or input delivery.

One sample still contained a visible island just after foreground changed to a shell window; the next sample, approximately 22 ms later, was hidden. This records a bounded suppression transition. Sustained overlay failure was not established, and neither zero non-desktop visible samples nor suppression latency percentiles are claimed. WindowFromPoint identified no Crest-owned target in completely hidden samples; that diagnostic observation does not establish actual click delivery.

Pause left the candidate hidden, and a later fresh entry revealed Crest. The owner reported Pause/Resume recovery; full stationary-pointer recovery was not independently qualified. The owner's quick-flick report could not be corroborated as an eligible departure before the 120 ms dwell completed. **AC03 remains pending.**

| Gate | Current conclusion |
|---|---|
| AC01 | Partial: hidden startup/restart, reported Pause/Resume recovery, pause-hidden and later fresh-entry observations; full stationary Win+D/unlock/sleep/secure recovery remains open. |
| AC02 | Partial, previously pending: owner hover and native/controller reveal observations; complete timing, placeholder and physical input acceptance remains open. |
| AC03 | Pending: no verified eligible sub-120 ms cancellation or complete input-interception evidence. |
| AC04 | Existing partial evidence retained; no additional complete Chrome/Explorer ordinary/maximized input cases were collected. |
| AC09 | Partial: current Settings/focus behavior recorded; the FxSound focus gap and external launcher matrix remain open. |
| AC10 | Partial, previously pending: 12 controller reversals plus the owner's scoped report; continuous motion assessment and the full 100-cycle sequence remain open. |
| AC23 | Partial: Pause/Resume subset, quiet secondary launch and process/window cleanup observed; Explorer restart and the remaining recovery matrix remain open. |

## Automated checks and clipboard limits

The fresh domain run passed **97 outcomes**. Isolated copies of the canonical fixture scripts passed **11 release-provenance checks and 16 evidence-validation checks**. Their only changes routed dot-sourcing to the canonical helpers and fixture output to new directories. Hash checks preserved the original scripts and all seven historical fixture files. The canonical observer self-test passed **3 checks**; the manifested diagnostic executable loaded the exact candidate DLL and passed **10 geometry checks**. These checks qualify their stated logic, rather than physical acceptance gates.

Two fresh guarded attempts at the full **111-outcome UI suite aborted** on an unsupported original clipboard format, before native clipboard writes. Both child processes exited with code 1. The clipboard listener was restored and history stayed unchanged after each attempt. **No fresh 111-outcome pass is claimed.** The successful 20 November exact-DLL run retains its own date and evidence.

A temporary clipboard-free adaptation passed **100 checks**, built with zero warnings and errors, and verified that it loaded the exact candidate DLL. It retains two in-memory snapshot checks and excludes 11 native Windows clipboard listener/search/copy/clear/restoration checks. The source fingerprint and canonical inputs stayed unchanged. This pass covers that limited scope; the complete fresh 111-outcome suite remains unqualified.

## Cleanup and remaining constraints

The quit-signalling process exited with code 0. The candidate exited, its process was absent, and the post-quit native snapshot found zero candidate top-level windows. The candidate's own exit code was unavailable and remains `null`; the signal process's exit code is not substituted for it.

The normal instance remained alive, and its settings and Windows startup value were unchanged. Clipboard listener/history preservation is separate from tray Pause: **the regular instance remains paused, and no Resume is claimed**. Available automation exposes neither tray Resume nor a Pause/Resume IPC command.

The user requested automated testing only from this point. Supported UI APIs provide neither an unpressed pointer move nor direct default-desktop targeting, and Computer Use cursor overlays block strict eligibility. Dependent physical hover cases remain unexecuted. No additional physical display/accessibility matrix, clean-account distribution, cold installed readiness, latency/frame trace, hidden graphics trace or battery-impact comparison was completed. Read-only trace-capability inspection confirmed no elevation or Performance Log Users membership, and no recording was started.

Raw profiles, snapshots and logs remain ignored locally. The shareable summary omits personal paths, private window titles, clipboard content and settings/history hashes.

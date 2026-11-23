# Crest 0.2.1 owner approval — 24 November 2026

**All 31 release-gate reviews are closed as approved by the owner. Further testing stopped at the owner's request.** This is an owner release decision; complete P1 technical qualification has not been established.

The owner confirmed the test Settings displayed **0.2.1** and the normal installed instance remained paused, then instructed: “for now close all the tests and check it approved.” The full instruction and candidate-bound results are in [the approval evidence](owner-release-approval-0.2.1-2026-11-24.json). The owner will report any subsequently discovered issue.

Candidate DLL SHA-256: `C5B5AB77A4FC6CFA7134024BE44CB4455D1EA5068ACF3FE5E87B1B13ED902195`. Source fingerprint: `FECD8248B9B3B91417AB52541AC19EBD4CEFDD3EF5721FFECA7AB46387E65E7B`. Source and distribution files remained unchanged.

| Record | Result |
|---|---|
| Owner review decision | 31 approved, review closed |
| Verified acceptance gates | 4 passed: AC02, AC03, PER-idle, PER-memory |
| Remaining technical evidence | 18 partial, 9 pending; 27 incompletely verified |
| Full P1 qualification | Not established; technical verifier still reports open gates |
| Fresh automated checks | 97 domain, 11 release-provenance, 16 evidence-validation and 3 observer outcomes passed |

AC02 now passes with scoped physical confirmation of a clean desktop reveal and 20 corroborating fresh controller/native reveal sequences. AC03 now passes after the retry recorded 25 eligible controller cancellations below 120 ms, with the island hidden and no new show event; 20 also have tighter physical cursor residence bounds. The owner's no-flash and working-click confirmations supplement these observations. Presentation timing remains separate.

The owner reported the Chrome/Explorer, strict-mode, shell/fullscreen and phase-interruption cases passed. Their reports remain in the evidence alongside the narrower native captures. The phase retry recorded only one reveal/Open despite the owner's reaffirmed eight reveals and nine expansions. Complete phase coverage and the 100-cycle sequence are therefore unqualified. Remaining hardware, display, recovery, accessibility, clean-account distribution, cold-start, frame/latency/hidden-rendering and battery checks stopped without passing measurement claims. No fresh complete UI/clipboard suite was run in this closeout.

The test candidate exited; the post-quit native snapshot found **zero candidate windows**. Quit signalling exited with code 0; the candidate's own exit code was unavailable. The normal installed process, settings and Windows startup value were preserved. The normal instance remains paused; use its tray **Resume** command to resume normal operation.

[release-gates.json](release-gates.json) records approval separately from each gate's technical status. Raw profiles, observations and logs remain ignored locally; the shareable JSON records their SHA-256 digests. Public release metadata was not changed.

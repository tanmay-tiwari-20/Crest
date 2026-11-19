# Crest 0.2.1 — Windows acceptance fixes

This Windows 11 x64 prerelease fixes compact timer navigation and strengthens the tools used to verify Crest. Desktop-only access remains the default; app access and clipboard collection require explicit opt-in.

When a countdown completes alongside a due reminder and focus session, both **Open completed timers** and its details action now open countdown acknowledgement. Independent running activities remain intact.

Acceptance evidence now requires matching DLL/source identities and an explicit satisfied outcome for each claimed gate. Historical observations stay historical. Native diagnostics honor the real 80–800 DIP trigger range and saved display overrides. The clipboard test fixture detaches original data before changing the clipboard, rejects unsupported formats before any write, and verifies restoration. Packaged applications now include a public file-hash verification record so exact downloaded packages can be measured without manufacturing a local sidecar.

## Downloads

- **Crest-Setup-0.2.1-x64.exe** — per-user installer.
- **Crest-Portable-0.2.1-x64.zip** — extract the complete Crest folder.
- **SHA256SUMS.txt** — package digests.

The .NET runtime is included. Startup is off for fresh profiles. Upgrades preserve saved data; unpinned shelf references are session-only and end when the prior instance quits. This acceptance task preserved the owner's running preview rather than upgrading or uninstalling it.

## Verification

The [test report](TEST_REPORT.md) and [dated Windows acceptance record](WINDOWS_ACCEPTANCE_2026-11-20.md) identify each tested binary and its results. Earlier owner-operated manual checks describe the installed 0.1.0 P2 preview. They do not qualify 0.2.0 or this changed binary.

The release remains a **prerelease** while native interaction, physical displays/accessibility, hardware transitions, clean-account installation/removal, cold startup, actual presented frames/latency, hidden rendering and battery comparisons remain open. Successful compilation, static renders and refresh rate do not qualify those measurements. See the [acceptance runbook](P1_ACCEPTANCE_RUNBOOK.md).

P2 local tools remain included. P3 notification/account access, weather, calendars, accessories, meetings, downloads, AI and phone integrations remain deferred.

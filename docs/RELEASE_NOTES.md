# Crest 0.3.0 — Optional integrations

**Prerelease candidate.** Version 0.3.0 extends Crest's local desktop tools with optional sources for weather, calendars, meeting links, managed downloads, read-only local AI, Windows notification previews, accessory batteries, and Phone Link. Every source starts off and uses the existing island controller. Background updates never reveal Crest.

The [v0.3.0 release](https://github.com/tanmay-tiwari-20/Crest/releases/tag/v0.3.0) is being prepared as a prerelease. Its page and assets become publicly downloadable when published; this document does not claim they are already public.

## What's new

- **City weather:** Search and choose a city through Open-Meteo, view current temperature, conditions and wind, and see last-refresh or stale/offline states. Clear the bounded forecast cache or disconnect the source. No device-location permission or weather account is needed.
- **Local calendar and meeting links:** Select a read-only `.ics` file for up to 200 upcoming events over 90 days. Common recurrence, exclusions, overrides, timezones, and all-day dates are supported. Event details start hidden. Validated Microsoft Teams, Zoom, and Google Meet links open provider join pages explicitly.
- **Crest-managed downloads:** Start HTTP(S) transfers to a chosen new filename, track known/unknown-length progress, and cancel or retry individual transfers. Two downloads can run at once, with a 4 GiB limit each. Existing destination files are preserved, temporary partial files are cleaned up, and completed files remain in their chosen folders after list removal.
- **Read-only local AI:** Ask an existing local Ollama service using your selected installed model. Requests, responses, and conversation are bounded; cancel and clear are available. Only typed conversation is sent, and no suggestions are executed.
- **Windows sources:** Request notification-preview access from Settings, read standard BLE accessory battery values on demand, and check for Microsoft Phone Link before handing off pairing or supported phone actions.
- **Privacy and resource fixes:** Retained clipboard text now uses current-user Windows DPAPI, with enabled legacy-history migration and explicit truncation labels. System-statistics sampling stops when Controls is disabled as well as when Crest is hidden.
- **Packaging:** An optional MSIX manifest and packaging path declare Windows notification-listener capabilities. Installable MSIX output requires a suitable signing certificate and separate package acceptance; ordinary installer and portable packages remain the standard distributions.

Open **Settings → Integrations** to configure sources. Enabling the first source adds **Optional integrations** to the dock. **Settings → Modules** can hide that entry independently. All existing music, controls, timers, stopwatch, focus sessions, reminders, shortcuts, workspaces, capture references, shelf pins, per-display settings, and themes remain included.

## Application packages

The planned 0.3.0 release assets are:

| Asset | Purpose |
| --- | --- |
| `Crest-Setup-0.3.0-x64.exe` | Per-user Windows installer, Start-menu entries, and ordinary uninstall support. |
| `Crest-Portable-0.3.0-x64.zip` | Extract the complete archive and run `Crest.exe`; keep the supporting files together. |
| `SHA256SUMS.txt` | SHA-256 digests for the installer and portable ZIP. |
| `release-manifest.json` | Exact version, source/DLL identities, test scope and asset hashes. |
| `release-gate-report.json` | Current native acceptance status and remaining gates. |

Both application packages target **Windows 11 x64** and include the .NET runtime. End users do not need an SDK, a separate runtime, administrator access, or a Crest account. The standard installer is unsigned. GitHub's source-code ZIP and tarball are source archives, not application packages. Windows 10 and ARM64 distributions are unavailable.

The previously published [0.2.1 release](https://github.com/tanmay-tiwari-20/Crest/releases/tag/v0.2.1) remains a separate historical candidate and does not include these new integrations.

## Install and upgrade

For a fresh install, run the installer or extract the portable package. Crest starts quietly in the tray; open **Crest Settings** from Start, double-click the tray icon, or use the tray's **Settings** command. Startup with Windows, clipboard capture, optional integrations, timer sound, keyboard reveal, and access over other apps are off by default.

To try Crest in its default strict-desktop mode, close Settings, press **Win+D**, minimize remaining visible apps on the selected display, move away from the top edge, then enter the top-center trigger afresh. A stationary pointer does not reveal Crest after desktop recovery. Opening Settings, pausing, locking, suspending, or changing to the secure desktop still suppresses the island.

For an installed upgrade, run the newer installer. It requests graceful quit and preserves local settings and saved activities. For portable use, quit Crest, extract the new package, and run its `Crest.exe`. If its folder moved, re-enable startup from the new location. Unpinned shelf references and in-memory integration previews end when the old instance quits; pinned references and saved activities retain their local state.

Optional integrations remain off until chosen. Enabled legacy clipboard history migrates to DPAPI-protected storage for the same Windows user. Earlier versions do not understand that protected format; clipboard-history downgrade compatibility is not promised. Interactive uninstall offers a retain-or-delete choice for local Crest data. Original shelf/shortcut/capture/calendar files and completed downloads are preserved.

See the [user guide](USER_GUIDE.md) for checksums, source setup, upgrades, and removal.

## Supported scopes and limits

- **Notifications:** Windows must grant access, and the package must expose the required identity/capability. Unavailable or denied access is reported. Previews are memory-only, limited to 50 items with bounded text and 24-hour expiry. Crest does not reply, dismiss the original Windows notification, or change Windows notification banners.
- **Weather:** City search and selected coordinates go to Open-Meteo after opt-in. The public provider's availability and free non-commercial usage terms apply. There is no continuous location tracking.
- **Calendar and meetings:** Only the selected local `.ics` source is read. Complex unsupported recurrence/timezone rules are reported and skipped. Meeting actions open validated named-provider pages; no automatic join, mute, microphone, or hang-up integration is advertised.
- **Accessories:** Battery readings require a connected paired Bluetooth LE device exposing the standard service. Proprietary or classic Bluetooth values may be unavailable, and percentages are never inferred.
- **Downloads:** Only transfers explicitly started in Crest are tracked. Other programs' downloads are not monitored, and existing filenames are never overwritten.
- **AI:** Ollama and its chosen model must already exist locally. Remote endpoints are rejected. Crest provides no model tools, file/clipboard/system access, external actions, or bundled model. Local model CPU/memory use is separate from the core application.
- **Phone:** Calls, messages, and pairing stay in Microsoft Phone Link and depend on the selected phone and Windows setup. Crest retains no phone account or messages and offers no in-island phone controls.

Sources can be disabled independently. Clear/disconnect controls remove Crest's retained previews or source selections as described in the [implementation map](PRD_COMPLETION.md). They do not unpair Windows devices, revoke Windows' own permission choices, or delete source files.

## Local implementation snapshot and candidate status

The table below records the earlier local implementation snapshot. The generated package notice and `release-manifest.json` report each release build's exact counts and exclusions; GitHub's fresh Windows runners execute the full automated suite.

| Local implementation snapshot | Result |
| --- | --- |
| Domain outcomes | **139 passed**. |
| WPF/UI outcomes against the packaged DLL | **147 passed**. |
| Native clipboard-writing outcomes | **11 explicitly omitted; not counted as passed**. |
| Supporting automated checks | 11 release-provenance, 16 evidence-verification, 10 geometry, and 3 observer-lifecycle checks passed. |
| Panel inspection | 12 light/dark static panel renders generated and inspected. |
| Isolated packaged lifecycle | Candidate started hidden and exited with code 0. |

Fixture tests and static renders establish their stated logic. They do not certify physical notification access, accessory/phone operation, external meeting/player compatibility, screen-reader and display acceptance, clean-account installation/upgrade/removal, cold startup, delivered frames/latency, hidden rendering, or battery impact. These remain separate acceptance work, so **0.3.0 remains a prerelease candidate**.

The prior 0.2.1 owner approval and native acceptance records remain bound to their recorded DLL/source. They do not qualify the changed 0.3.0 candidate. The owner's installed instance was not upgraded by source implementation, and Windows permissions, pairing, model installation, and trust settings were not changed.

Read the [test report](TEST_REPORT.md), [candidate verification record](prd-completion-verification.json), [acceptance runbook](P1_ACCEPTANCE_RUNBOOK.md), and [changelog](../CHANGELOG.md) for details. Maintainers can use the [build guide](BUILD.md) and [release instructions](RELEASING.md) to prepare and verify the exact tag and assets.

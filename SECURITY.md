# Security policy

## Reporting a vulnerability

Use the repository's **Security > Advisories > Report a vulnerability** flow when private vulnerability reporting is enabled:

[Crest security advisories](https://github.com/tanmay-tiwari-20/Crest/security/advisories)

If that option is unavailable, open an issue requesting a private reporting channel without including exploit details, credentials, personal files, or sensitive diagnostic output. The maintainer can then enable private reporting or arrange a channel. No response-time guarantee is currently published.

Please include the affected version, Windows version, reproduction steps, impact, and a minimal safe example. Do not attach another person's files or device data.

## Current support

Version 0.3.0 is the current release candidate. This project has no long-term support branch or established security-response schedule. Fixes target the current development version and should be described in its release notes.

## Data and distribution

Crest has no analytics or required account. Settings, activities, selected launcher/capture/calendar paths, pinned shelf references, and the weather cache stay in the local Windows profile. Unpinned shelf references are session-only. Source-file references never authorize deleting or changing the originals.

Clipboard collection starts off, captures new text only after opt-in, and supports pause, bounded retention, pinning, clear, and disable erasure. Retained clipboard data uses Windows DPAPI for the current Windows user; readable text exists in memory while Crest is running. This protection does not prevent another process running as the same user from accessing the data, and automatic secret detection is not guaranteed.

Every optional source starts off. Weather sends city searches and forecast coordinates to Open-Meteo. **Use Windows location** requests a single position from Windows and rounds coordinates to 0.1° before saving them or requesting a forecast. Calendar sources are read-only and event previews stay in memory. Outlook reads the default calendar of the running classic client. Subscription feeds use HTTPS, and Crest encrypts the saved link for the current Windows user. Windows notification previews and accessory batteries depend on supported, granted access. Notification previews stay in memory with bounded retention. Phone Link owns its pairing, account, and phone-data permissions. Disconnect and clear actions stay inside Crest's normal Settings or island surface.

Crest-owned downloads contact the explicit HTTP(S) URL and save to a selected new filename. They never overwrite an existing destination or open downloaded content automatically. AI sends the user's question and bounded in-memory conversation only to a selected loopback Ollama service; Crest supplies no command, file, clipboard, or external-action tools. Treat downloaded content and model answers according to their source and verify them before use.

Download binaries from this repository's [Releases](https://github.com/tanmay-tiwari-20/Crest/releases) page and compare their hashes with the accompanying `SHA256SUMS.txt`. Checksums detect a changed download; they do not replace publisher signing. The current Windows installer is unsigned.

Keep public reports free of clipboard content, notification bodies, calendar details and subscription links, private download URLs, model conversations, credentials, and full local paths. Report technical codes and synthetic reproduction data instead of attaching the Crest data directory.

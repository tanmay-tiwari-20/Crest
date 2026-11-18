# Security policy

## Reporting a vulnerability

Use the repository's **Security > Advisories > Report a vulnerability** flow when private vulnerability reporting is enabled:

https://github.com/tanmay-tiwari-20/Crest/security/advisories

If that option is unavailable, open an issue requesting a private reporting channel without including exploit details, credentials, personal files, or sensitive diagnostic output. The maintainer can then enable private reporting or arrange a channel. No response-time guarantee is currently published.

Please include the affected version, Windows version, reproduction steps, impact, and a minimal safe example. Do not attach another person's files or device data.

## Current support

Version 0.1.0 is a prerelease candidate. This project currently has no long-term support branch or established security-response schedule. Fixes will target the current development version and should be described in its release notes.

## Data and distribution

Crest uses local settings and timer state. The shelf holds session-only file references. This build has no analytics, account login, clipboard history, notification access, or background network requests. Optional future integrations require separate review.

Download binaries from this repository's [Releases](https://github.com/tanmay-tiwari-20/Crest/releases) page and compare their hashes with the accompanying `SHA256SUMS.txt`. Checksums detect a changed download; they do not replace publisher signing. The current Windows installer is unsigned.

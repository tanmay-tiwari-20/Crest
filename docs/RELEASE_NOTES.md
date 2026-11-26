# Crest 0.4.0 — Settings, themes, and easier integration setup

Crest 0.4.0 is a Windows 11 x64 prerelease. It redesigns Settings and lets you configure weather and calendars from sources you already use. [GitHub release](https://github.com/tanmay-tiwari-20/Crest/releases/tag/v0.4.0)

## What's new

- **Weather.** Choose **Use Windows location** from **Settings → Integrations → Weather** to set up your forecast in one step. Crest rounds the location before saving it and sending it to Open-Meteo. Use city search for a fixed location or when Windows location is unavailable.
- **Calendar.** Choose **Connect Outlook** while classic Outlook is running with a configured profile, add a compatible iCalendar subscription link, or select a local `.ics` file. Calendar access is read-only. Crest keeps event previews in memory and protects subscription links using Windows current-user encryption.
- **Integrations.** See which sources are enabled, choose a source, and find status and refresh actions together. Downloads, read-only local Ollama, Windows notification previews, accessory batteries, and Phone Link are available from the same page.
- **Settings and themes.** Find common tasks on Overview, manage visibility and trigger options in Behavior, and reorder or show tools in Modules. Appearance previews System, Dark, Light, Ocean, Forest, and Rose. Per-display overrides and Windows high-contrast colors are supported.
- **Contextual island.** Click the compact live activity to expand it. Up to two secondary bubbles give you access to concurrent activities. Open **Tools** to switch into the full catalogue.

[Setup and troubleshooting](USER_GUIDE.md) · [Integrations preview](images/settings-integrations-0.4.0.png) · [Theme previews](images/settings-appearance-0.4.0.png) · [Contextual-island design](CONTEXTUAL_ISLAND_REPORT.md)

## Windows packages

| Asset | Purpose |
| --- | --- |
| `Crest-Setup-0.4.0-x64.exe` | Per-user Windows installer and Start-menu entries |
| `Crest-Portable-0.4.0-x64.zip` | Complete portable application |
| `SHA256SUMS.txt` | SHA-256 digests for the installer and ZIP |
| `release-manifest.json` | Package versions, file hashes, and exact automated test scope |
| `release-gate-report.json` | Candidate-bound Windows acceptance status |

Both packages include the .NET runtime. The standard installer is unsigned. Use the installer for an in-place upgrade that preserves your saved preferences and activities. For portable use, quit Crest, extract every file from the new ZIP, then run its `Crest.exe`. Re-enable startup if you move an existing portable installation. [Install and upgrade guide](USER_GUIDE.md#install)

## Integration compatibility

- Windows location access depends on Windows privacy settings. Crest requests it on selection and uses the saved approximate area for later forecasts.
- The Outlook connection reads the default calendar of a running classic Outlook desktop app. It reads event times, subjects, and locations, including supported meeting links in the location field. Outlook bodies and attendee details are outside this connection's scope. The new Outlook app, Google Calendar, and Apple Calendar can be used through a compatible subscription link or file when the provider offers one.
- Online and local iCalendar sources have a 2 MiB read limit. Crest displays up to 200 upcoming events over 90 days. Event details start hidden and meeting links open supported provider pages only when you select them.
- Windows notifications can require package identity, capability, and permission. BLE batteries depend on standard service values on paired accessories. Phone actions open Microsoft Phone Link. Local AI requires a running Ollama instance and a model you already installed.

## Verification and release status

The attached release manifest records the Windows build and outcome checks for these packages. Physical device and provider access, clean-account installation, screen readers, mixed-DPI desktop transitions, and measured performance have separate acceptance requirements. The gate report lists the remaining checks. [Test report](TEST_REPORT.md) · [Acceptance runbook](P1_ACCEPTANCE_RUNBOOK.md)

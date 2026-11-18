# Local feature verification

The automated console suite verifies observable timer, settings, launcher validation, and shelf outcomes. It links the same local-feature source files used by the application and uses an injected clock and isolated temporary storage. It does not simulate successful Windows hardware, foreground, pointer, drag/drop, or screen-reader integration.

Run from the project root with a supported .NET 10 SDK:

```powershell
dotnet run --project tests/Crest.Tests/Crest.Tests.csproj
```

During development the SDK was installed within `.tools/dotnet`, so the equivalent command was:

```powershell
& .tools/dotnet/dotnet.exe run --project tests/Crest.Tests/Crest.Tests.csproj
```

## Verified outcomes

All 10 tests passed on 18 November 2026:

1. Forward and backward wall-clock changes during a running timer do not change its monotonic countdown.
2. Simulated suspended elapsed time advances the countdown, creates one completion, and preserves acknowledgement across restart.
3. Restart restores a running timer from the saved UTC deadline.
4. A forward UTC change before restart produces one retained overdue completion.
5. A backward UTC change before restart uses the actual saved deadline; pausing preserves the resulting remaining duration.
6. Paused duration survives restart and resumes from that duration.
7. Invalid JSON and unsupported settings versions recover strict desktop, no sound, no startup, and no accessibility shortcut defaults.
8. Settings and explicitly selected launcher entries round trip, while invalid timing values clamp to supported bounds.
9. The shelf caps references at 20, leaves source contents untouched, identifies a moved source, removes references without deleting originals, and clears on disposal/restart.
10. The launcher rejects absent targets and unselected URI protocols rather than executing them.

## Integration checks still required

These automated results do not certify the complete first-release acceptance gate. The packaged application must also be tested for:

- Real sleep/resume timer behavior and normal app restart on the owner's laptop.
- File drag from the actual Explorer desktop through the top-edge reveal into the island, with the original file unchanged.
- File drag out of the shelf, ordinary shelf clicks, and drag cancellation.
- Missing-source refresh on reveal and actionable errors inside the visible island.
- Successful opening of user-selected apps, folders, websites, and `.lnk` shortcuts, followed by foreground suppression.
- Native foreground visibility, virtual desktops, Windows shell panels, pointer grace, and registered popup focus.
- Keyboard-only module operation, text-input focus, screen-reader values, high contrast, and reduced motion.
- Clean-account installation, upgrades, startup preference, uninstallation, and retained/deleted data choices.
- Presented frames, hidden CPU, memory, and suppression latency under the PRD measurement conditions.

No physical external display, mixed-DPI setup, missing battery, disconnected audio output, or multiple real media sessions is implied by these local-feature tests.

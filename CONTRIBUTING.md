# Contributing to Crest

Crest is a Windows 11 desktop utility. Contributions should preserve its central behavior: it appears after a fresh hover on an eligible desktop, hides when an unrelated application becomes active, and leaves no visible or interactive placeholder while hidden.

## Set up

Use Windows 11 x64 and the .NET SDK specified in [global.json](global.json). Clone the repository and run:

```powershell
git clone https://github.com/tanmay-tiwari-20/Crest.git
cd Crest
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
```

The build restores dependencies, compiles Release, runs the outcome tests, and publishes a self-contained application. It does not install or launch Crest. Run `artifacts/portable/Crest/Crest.exe` when you want to exercise it. Quit an existing Crest instance before testing another build; the app uses one instance per Windows session.

For a focused test run:

```powershell
dotnet run --project tests/Crest.Tests/Crest.Tests.csproj --configuration Release
```

See [build instructions](docs/BUILD.md) for packaging and SDK details.

## Make a change

1. Open an issue describing the problem or feature, particularly for changes to desktop eligibility, data access, or native integrations.
2. Create a branch with a short, descriptive name.
3. Keep the change focused. Add a meaningful regression test when behavior warrants it; use the single interaction controller for every island surface.
4. Run the build and relevant checks. Exercise Windows-specific behavior on a real desktop when the change affects it.
5. Open a pull request explaining the trigger, the resulting behavior, and the validation performed. Include a screenshot for visual changes and distinguish a static render from a native capture.

Source layout:

| Path | Purpose |
| --- | --- |
| `src/Crest/Core` | Settings, interaction state, and pending control writes |
| `src/Crest/Platform` | Windows desktop eligibility and native APIs |
| `src/Crest/Features` | Media, audio, brightness, timer, launcher, and shelf |
| `src/Crest/UI` | Island, activity deck, Settings, and theme controls |
| `tests` | Outcome tests and optional read-only Windows probes |
| `scripts` and `installer` | Build, distribution, and per-user setup |

Do not commit SDK installations, build output, credentials, local diagnostic recordings, or personal files. GitHub Actions checks builds; it does not certify physical desktop dragging, screen-reader operation, display transitions, or frame delivery. Keep the [test report](docs/TEST_REPORT.md) accurate when adding evidence.

## Design and data constraints

- Check actual Windows capabilities and expose useful unavailable states. Do not fill disconnected media or hardware with invented data.
- Preserve timer recovery and original files referenced by the shelf.
- Keep accounts, permissions, clipboard capture, and future integrations out of flows until their behavior and data handling are specified.
- Keep desktop suppression immediate even when an animation is running; registered popups must share the same eligibility rules.
- Respect the Windows reduced-motion and high-contrast preferences.

Original project code is available under the [MIT license](LICENSE). See [third-party notices](THIRD_PARTY_NOTICES.md) for dependency and artwork provenance. Do not add assets that you cannot redistribute.

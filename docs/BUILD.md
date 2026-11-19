# Build and package Crest

End users should download an application from [GitHub Releases](https://github.com/tanmay-tiwari-20/Crest/releases). These instructions are for maintainers.

## Requirements

Use Windows 11 x64, Git, PowerShell, and .NET SDK **10.0.404**, pinned by [global.json](../global.json). The build uses `.tools/dotnet/dotnet.exe` when available, otherwise the system `dotnet`. SDK roll-forward is disabled. The first restore and compiler bootstrap require network access.

Crest targets `net10.0-windows10.0.19041.0`, uses WPF and Windows APIs, and is distributed for Windows 11. Its direct NuGet dependency is `System.Management` 10.0.0. The self-contained `win-x64` publish includes the .NET and Windows Desktop 10.0.15 runtimes.

## Build and test

```powershell
git clone https://github.com/tanmay-tiwari-20/Crest.git
cd Crest
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
```

The script restores committed app/test/UI-probe lockfiles in locked mode, compiles with deterministic settings and warnings treated as errors, runs domain outcomes, the isolated WPF productivity UI harness, and release-provenance checks, then publishes `artifacts/portable/Crest`. Fingerprints use ordinal input ordering across Windows PowerShell and PowerShell 7. The UI harness briefly opens a test window, uses temporary isolated data, exercises timer/stopwatch controls, and renders both themes at four DPI densities. It does not install or launch the Crest product. See the [test report](TEST_REPORT.md) for scope and release-gate evidence.

For a focused test run or development launch:

```powershell
dotnet run --project tests/Crest.Tests/Crest.Tests.csproj --configuration Release
dotnet run --project src/Crest/Crest.csproj -- --settings
```

Quit another Crest instance before launching a development copy.

## Create release assets

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Tag v0.1.0
```

This runs the Release build/tests and creates a clean distribution containing the executable, runtime, public user/build/release documentation, MIT license, and dependency notices. Local screenshots, diagnostic output, PDBs, and machine-specific test reports are excluded.

| File under `artifacts/release` | Purpose |
| --- | --- |
| `Crest-Setup-0.1.0-x64.exe` | Per-user Windows installer |
| `Crest-Portable-0.1.0-x64.zip` | Complete application in a `Crest` folder |
| `SHA256SUMS.txt` | SHA-256 hashes of both application packages |
| `release-notes.md` | Release-description text |
| `release-gate-report.json` | Candidate-bound acceptance status for review |

`scripts/build.ps1 -Package` builds an installer without the release ZIP. `scripts/package.ps1` packages an existing publish after checking architecture, versions, manifests, runtime files, and its source/test-bound build record. `scripts/release.ps1 -SkipBuild -Tag v0.1.0` does not rerun tests; it requires the existing publish to match the current verified source and every file hash. Changed source, changed binaries, skipped tests, or Debug output require a fresh Release build.

Default release packaging remains a release-candidate operation while native acceptance is open. Use `scripts/verify-release-gates.ps1` to inspect the candidate-bound report. Add `-RequireP1Gates` to `scripts/release.ps1` when preparing a P1-qualified release; it refuses to package if any applicable gate is incomplete, failed, or stale. The [gate record](release-gates.json) must link hashed observed evidence to the current candidate before a gate can pass. CI compilation cannot certify physical desktop, accessibility, hardware, or presentation behavior.

The portable ZIP uses sorted entries, normalized paths, and fixed timestamps; every entry is checked against the clean distribution. Fixed inputs improve repeatability. Byte-for-byte installer reproducibility across machines is not claimed without comparing actual outputs.

## Installer tooling and behavior

Packaging downloads official Inno Setup **6.7.4** when needed, verifies the pinned release-asset SHA-256 and Authenticode publisher, and extracts it in portable mode under `.tools/innosetup`. Cached compiler provenance is checked before reuse. The build tool is not registered or included in Crest packages.

The installer runs per user without elevation, installs in `%LOCALAPPDATA%\Programs\Crest`, creates Start-menu entries, and offers an unchecked desktop shortcut. It creates no startup entry. Upgrades preserve preferences and request graceful app shutdown; an unresponsive app is reported rather than force-killed. Uninstall offers retention of `%LOCALAPPDATA%\Crest` by default and does not remove shelf originals.

Crest's installer is unsigned. Publisher signing requires a maintainer-owned certificate and signing before final checksums are generated. Build-tool signature verification does not sign Crest.

## Dependency changes

App/test NuGet lockfiles are committed. Deliberately refresh them with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -UpdateLockFiles
git diff -- src/Crest/packages.lock.json tests/Crest.Tests/packages.lock.json tests/Crest.UIProbe/packages.lock.json
```

Review dependency changes and notices before committing. Normal CI/release builds use locked restore. SDK/runtime upgrades also require updating `global.json` and reviewed metadata in `scripts/release-common.ps1`, then appropriate Windows acceptance.

## GitHub automation

[CI](../.github/workflows/ci.yml) builds/tests/packages on Windows and retains temporary downloadable Actions artifacts. [The release workflow](../.github/workflows/release.yml) runs for a matching tag or manual dispatch and creates an unpublished prerelease draft.

See [publishing steps](RELEASING.md). CI does not certify physical desktop dragging, devices, accessibility, or frame delivery.

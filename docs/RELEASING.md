# Publish a Crest release

Crest is a standalone Windows app. Publish its installer and portable ZIP as **GitHub Release assets**. A GitHub Packages/NuGet feed is not required. Actions artifacts are temporary CI downloads; release assets are the user-facing distribution.

## Repository setup

The repository is [tanmay-tiwari-20/Crest](https://github.com/tanmay-tiwari-20/Crest). Workflows use GitHub-hosted Windows runners and the pinned SDK.

1. Under **Settings > Actions > General**, allow the GitHub-owned actions referenced by the workflows. If you use an allowlist, permit their exact pinned references.
2. Workflows use GitHub's `GITHUB_TOKEN`. No personal token is needed. The release job requests `contents: write`; CI uses read access. Repository/organization policy may restrict that permission and must allow it for draft creation.
3. Optionally require the CI checks on `main` through branch protection/rulesets. Use the exact job name shown after the first successful run.
4. Optionally enable **Private vulnerability reporting** so the channel in `SECURITY.md` is available.

These are maintainer choices; the documentation does not imply they have already been enabled.

## Prepare a version

Use a clean, reviewed commit with passing checks. The current version is `0.1.0`, matching tag `v0.1.0`. Scripts reject a tag that disagrees with `src/Crest/Crest.csproj`.

For a new version, update project `<Version>`, app/tray/Settings version strings, user-facing download names, `CHANGELOG.md`, and `docs/RELEASE_NOTES.md`. The installer/package scripts read their version from the project. Review dependency lockfile changes before committing.

Run from the source you intend to tag:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Tag v0.1.0
```

Inspect `artifacts/release`, included licenses, and documentation. Leave local diagnostic output, SDK/compiler folders, and reference crops out of releases. Keep native acceptance claims accurate. Version 0.1.0 is a **prerelease** until its recorded release gates are complete.

Inspect `artifacts/release/release-gate-report.json` with the packages. Builds/tests and binary provenance are checked automatically; desktop, accessibility, device, clean-account distribution, and measured quality gates require recorded observations. `docs/release-gates.json` contains the complete P0/P1 checklist. Passing evidence must match the candidate DLL and source fingerprint and include hashes of the evidence files.

When preparing a P1-qualified release, run `scripts/release.ps1 -Tag v0.1.0 -RequireP1Gates`. This refuses packaging while any gate remains open, failed, or stale, or any visible-over-app incident is recorded. Default packaging permits a release candidate and prefixes release notes with its open acceptance status.

## Preferred: tag workflow

After committing and pushing reviewed source:

```powershell
git tag -a v0.1.0 -m "Crest 0.1.0 release candidate"
git push origin v0.1.0
```

The release workflow checks out the existing tag, builds/tests/packages it, and creates an **unpublished prerelease draft** with three assets. Follow the run in [Actions](https://github.com/tanmay-tiwari-20/Crest/actions).

Do not move an existing tag to another build. To retry a failed draft build, use **Actions > Draft Windows prerelease > Run workflow** with the same existing tag. The workflow refreshes a draft and refuses to replace a published release.

Before pressing **Publish release**, check the tagged commit, passing job, attached installer/portable/checksum files, matching versions, truthful release notes, and **Set as a pre-release**. Do not mark this candidate as the latest stable release.

## Manual fallback: GitHub website

1. Build the three assets with `scripts/release.ps1 -Tag v0.1.0`.
2. Open [Releases](https://github.com/tanmay-tiwari-20/Crest/releases) and choose **Draft a new release**.
3. Select the existing `v0.1.0` tag at the reviewed commit.
4. Title it **Crest 0.1.0 release candidate** and paste `artifacts/release/release-notes.md`.
5. Attach `Crest-Setup-0.1.0-x64.exe`, `Crest-Portable-0.1.0-x64.zip`, and `SHA256SUMS.txt`.
6. Check **Set as a pre-release**, save/inspect the draft, then publish when ready.

## Manual fallback: GitHub CLI

Install GitHub CLI and sign in, then run:

```powershell
gh auth login
gh release create v0.1.0 `
  artifacts/release/Crest-Setup-0.1.0-x64.exe `
  artifacts/release/Crest-Portable-0.1.0-x64.zip `
  artifacts/release/SHA256SUMS.txt `
  --repo tanmay-tiwari-20/Crest --verify-tag --draft --prerelease --latest=false `
  --title "Crest 0.1.0 release candidate" `
  --notes-file artifacts/release/release-notes.md
```

After reviewing the draft:

```powershell
gh release edit v0.1.0 --repo tanmay-tiwari-20/Crest --draft=false --prerelease --latest=false
```

Do not overwrite published binaries for a tag. Issue a new version for changed source. If signing is added, sign before final checksums and upload the signed files and matching list together.

## Package contents

The installer supplies per-user Start-menu entries and Windows uninstall support. The ZIP contains a complete `Crest` folder. Both include the runtime, public user/build/release documentation, the reviewed timer and stopwatch fixture images used by README, MIT license, and dependency notices. Raw local captures and diagnostics are excluded.

GitHub automatically adds source ZIP/tar archives. They are for developers, not substitutes for application packages.

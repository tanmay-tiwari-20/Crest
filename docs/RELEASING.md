# Release Crest 0.4.0

Crest ships as a Windows 11 x64 installer and portable ZIP through GitHub Releases. GitHub Packages and NuGet publication are not required. Actions artifacts retain build output; an unpublished GitHub prerelease draft is the review point before public distribution.

## Current candidate

Project version `0.4.0` matches annotated tag `v0.4.0`. It adds the contextual island, redesigned Settings and themes, Windows location, and more calendar sources. The native P0/P1 checklist remains open. The 0.3.0 gate record and owner-approved 0.2.1 gate record preserve their respective candidate identities. [0.3.0 gate record](release-gates-0.3.0.json) · [0.2.1 gate record](release-gates-0.2.1.json)

The standard packages are unsigned. The optional MSIX packaging source declares Windows capabilities for notification access and location. See the [user guide](USER_GUIDE.md#optional-integrations) for supported integration scopes.

## Prepare source and assets

Use the pinned SDK and locked dependencies in [BUILD.md](BUILD.md). Review the source, version, changelog, release notes, supported-platform claims and dependency notices together. Keep private profiles, clipboard data, raw diagnostics, SDK/compiler folders and generated packages out of Git.

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Tag v0.4.0 -ArtifactRoot artifacts/release-0.4.0
```

This restores locked dependencies, compiles with warnings treated as errors, runs domain/WPF/native-probe/provenance/documentation checks, tests the exact self-contained DLL, stages public documentation and licenses, compiles the installer, verifies every portable ZIP entry, and writes checksums and release manifests. The custom artifact root preserves older candidate packages.

If a local user's clipboard cannot be safely detached and restored, omit only that write suite explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Tag v0.4.0 -ArtifactRoot artifacts/release-0.4.0 -SkipNativeClipboardVerification
```

This still runs the remaining checks. Eleven clipboard outcomes are excluded, the provenance/manifest/release body record that exclusion, and the package cannot claim a complete automated suite or P1 qualification. CI and tag workflows use the full suite on fresh Windows runners. The option cannot be combined with `-SkipBuild`, `-SkipTests`, or `-RequireP1Gates` as a way to erase missing evidence.

`-SkipBuild` repackages only a current source-bound verified build and its summaries; it cannot change its recorded test scope. `-RequireP1Gates` requires the complete automated suite and every current candidate-bound native gate. Default packaging produces a prerelease candidate with open gates disclosed.

Inspect these files under `artifacts/release-0.4.0/release/`:

| File | Purpose |
| --- | --- |
| `Crest-Setup-0.4.0-x64.exe` | Per-user installation, Start-menu entries and uninstall retention choice. |
| `Crest-Portable-0.4.0-x64.zip` | Complete self-contained app; extract every file before running. |
| `SHA256SUMS.txt` | SHA-256 of the installer and ZIP. |
| `release-manifest.json` | Version, tag, runtime, source/DLL identities, test scope and asset hashes. |
| `release-gate-report.json` | Current native acceptance status and open gates. |
| `release-notes.md` | GitHub-ready body with links expanded to this tag and exact package verification scope. |

The distribution's `.crest-build-verification.json` binds its complete file set and retained test exclusions. Review the installer version, ZIP contents, dependency license record and absence of private data. Signing, when introduced, must happen before final checksums/manifests and upload; never replace published unsigned bytes silently.

## Commit and tag

Commit all reviewed release source and documentation. Inspect the intended author/committer using `git log -1 --format=fuller`. Push with a normal fast-forward; use a pull request when repository rules require it. Do not force-push or move existing tags.

```powershell
git push origin main
git tag -a v0.4.0 -m "Crest 0.4.0 release candidate"
git push origin v0.4.0
```

The tag must identify the reviewed release commit. The tag workflow builds the tagged source and creates an **unpublished prerelease draft**. It requires only its scoped `GITHUB_TOKEN` with `contents: write`; CI uses read permission. Repository policy must permit the pinned GitHub-owned actions and draft creation.

Follow [Windows CI and release runs](https://github.com/tanmay-tiwari-20/Crest/actions). The release job creates a draft only if none exists, refuses lookup/authentication errors, and refuses to modify either an existing draft or a published release. It does not mark the candidate as latest stable. A failed build before draft creation can be retried against the same immutable tag. If a draft already exists, review it manually; deleting an unwanted unpublished draft is a separate explicit action. Changed source requires a new version and tag.

## Review and publication

Before publishing, verify the existing tag/commit, passing Windows jobs, five attached release assets, matching hashes, exact package test scope, truthful compatibility notes and prerelease status. The upload assets are the installer, ZIP, checksum list, release manifest and gate report; `release-notes.md` supplies the body. Source ZIP/tar archives generated by GitHub are developer downloads, not application packages.

Public publication is a separate deliberate step. Keep this candidate a prerelease while native acceptance remains open. Do not mark it as latest stable.

For a manual fallback with authenticated GitHub CLI:

```powershell
gh release create v0.4.0 --repo tanmay-tiwari-20/Crest --verify-tag --draft --prerelease --latest=false --title "Crest 0.4.0 release candidate" --notes-file artifacts/release-0.4.0/release/release-notes.md artifacts/release-0.4.0/release/Crest-Setup-0.4.0-x64.exe artifacts/release-0.4.0/release/Crest-Portable-0.4.0-x64.zip artifacts/release-0.4.0/release/SHA256SUMS.txt artifacts/release-0.4.0/release/release-manifest.json artifacts/release-0.4.0/release/release-gate-report.json
```

The GitHub website's **Draft a new release** provides the same fallback: select the existing tag, attach those five files, paste the generated body and retain prerelease status. Authentication is required only for the chosen remote operation; local documentation and packaging can be completed independently.

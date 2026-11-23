param(
    [string]$Tag = '',
    [switch]$SkipBuild,
    [switch]$UpdateLockFiles,
    [switch]$RequireP1Gates,
    [switch]$SkipNativeClipboardVerification,
    [string]$ArtifactRoot = ''
)
. (Join-Path $PSScriptRoot 'release-common.ps1')
$metadata = Get-CrestBuildMetadata
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) { $ArtifactRoot = Join-Path $script:CrestRoot 'artifacts' }
$ArtifactRoot = Resolve-CrestArtifactPath ([IO.Path]::GetFullPath($ArtifactRoot)) -AllowRoot
Assert-CrestTag $Tag $metadata
if ($SkipBuild -and $UpdateLockFiles) { throw '-UpdateLockFiles requires a build.' }
if ($SkipBuild -and $SkipNativeClipboardVerification) { throw 'A skipped build must use the recorded verification scope; it cannot request a new omission.' }
if ($RequireP1Gates -and $SkipNativeClipboardVerification) { throw 'P1 qualification requires the full automated suite, including native clipboard verification.' }
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -UpdateLockFiles:$UpdateLockFiles -SkipNativeClipboardVerification:$SkipNativeClipboardVerification -ArtifactRoot $ArtifactRoot
}
$gateReport = & (Join-Path $PSScriptRoot 'verify-release-gates.ps1') -PublishDir (Join-Path $ArtifactRoot 'portable\Crest') -OutputPath (Join-Path $ArtifactRoot 'release-gate-report.json') -RequireP1Gates:$RequireP1Gates
$package = & (Join-Path $PSScriptRoot 'package.ps1') -ArtifactRoot $ArtifactRoot
$releaseDir = Reset-CrestArtifactDirectory (Join-Path $ArtifactRoot 'release')
$installerPath = Join-Path $releaseDir ([IO.Path]::GetFileName($package.InstallerPath))
Copy-Item -LiteralPath $package.InstallerPath -Destination $installerPath -Force
$zipPath = Join-Path $releaseDir "Crest-Portable-$($metadata.Version)-x64.zip"
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
$files = @(Get-ChildItem -LiteralPath $package.DistributionDir -File -Recurse | Sort-Object FullName)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($package.DistributionDir.TrimEnd('\').Length + 1).Replace('\', '/')
        $entry = $archive.CreateEntry(('Crest/' + $relative), [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $inputStream = [IO.File]::OpenRead($file.FullName)
        $entryStream = $entry.Open()
        try { $inputStream.CopyTo($entryStream) } finally { $entryStream.Dispose(); $inputStream.Dispose() }
    }
} finally { $archive.Dispose(); $stream.Dispose() }
# Verify the finished archive against the staged distribution without launching it.
$stream = [IO.File]::OpenRead($zipPath)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $false)
try {
    if ($archive.Entries.Count -ne $files.Count) { throw 'Portable archive entry count differs from the clean distribution.' }
    foreach ($entry in $archive.Entries) {
        $relative = $entry.FullName.Substring('Crest/'.Length).Replace('/', '\')
        $sourceFile = Join-Path $package.DistributionDir $relative
        if ($entry.FullName -notmatch '^Crest/' -or $relative -match '(^|\\)\.\.(\\|$)' -or -not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) { throw 'Unexpected portable archive entry.' }
        $entryStream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $entryHash = [BitConverter]::ToString($sha.ComputeHash($entryStream)).Replace('-', '') } finally { $sha.Dispose(); $entryStream.Dispose() }
        if ($entryHash -ne (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash) { throw "Portable archive content mismatch: $relative" }
    }
} finally { $archive.Dispose(); $stream.Dispose() }
$checksumLines = @(@($installerPath, $zipPath) | Sort-Object { [IO.Path]::GetFileName($_) } | ForEach-Object {
    (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_)
})
$checksumPath = Join-Path $releaseDir 'SHA256SUMS.txt'
Write-CrestUtf8 $checksumPath (($checksumLines -join "`n") + "`n")
Copy-Item -LiteralPath (Join-Path $package.DistributionDir 'docs\RELEASE_NOTES.md') -Destination (Join-Path $releaseDir 'release-notes.md') -Force
Copy-Item -LiteralPath $gateReport.ReportPath -Destination (Join-Path $releaseDir 'release-gate-report.json') -Force
if (-not $gateReport.P1GatesPassed) {
    $notePath = Join-Path $releaseDir 'release-notes.md'
    $candidateNotice = "**Release candidate: P1 acceptance gates remain open.** Build and outcome tests do not certify desktop, accessibility, hardware, distribution, or measured performance acceptance.`n`n"
    Write-CrestUtf8 $notePath ($candidateNotice + [IO.File]::ReadAllText($notePath))
}
$verifiedBuild = Assert-CrestBuildVerification $package.DistributionDir $metadata
$domainRecord = Get-Content -LiteralPath (Join-Path $ArtifactRoot 'domain-outcomes.json') -Raw | ConvertFrom-Json
$uiRecord = Get-Content -LiteralPath (Join-Path $ArtifactRoot 'productivity-qa\ui-outcomes.json') -Raw | ConvertFrom-Json
$provenanceRecord = Get-Content -LiteralPath (Join-Path $ArtifactRoot 'release-verification-tests\results.json') -Raw | ConvertFrom-Json
$evidenceRecord = Get-Content -LiteralPath (Join-Path $ArtifactRoot 'gate-verification-tests\results.json') -Raw | ConvertFrom-Json
$documentationRecord = Get-Content -LiteralPath (Join-Path $ArtifactRoot 'documentation-qa\results.json') -Raw | ConvertFrom-Json
if ($domainRecord.SourceFingerprint -ne $verifiedBuild.SourceFingerprint -or $uiRecord.assemblySha256 -ne (Get-FileHash -LiteralPath (Join-Path $package.DistributionDir 'Crest.dll') -Algorithm SHA256).Hash) { throw 'Release verification summaries do not match the packaged source/binary.' }
$skipped = @(); if ($null -ne $verifiedBuild.PSObject.Properties['SkippedChecks']) { $skipped = @($verifiedBuild.SkippedChecks) }
if ($skipped.Count -gt 0) {
    $notePath = Join-Path $releaseDir 'release-notes.md'
    $scopeNotice = "**Local automated verification exclusions:** " + ($skipped -join '; ') + ". Omitted checks are not counted as passed. GitHub runners execute the full suite.`n`n"
    Write-CrestUtf8 $notePath ($scopeNotice + [IO.File]::ReadAllText($notePath))
}
$notePath = Join-Path $releaseDir 'release-notes.md'
$verificationNotice = "**This package's automated checks:** $($domainRecord.Passed) domain outcomes and $($uiRecord.passed) WPF/UI outcomes against its exact DLL. Full automated suite: $($skipped.Count -eq 0). Native acceptance remains separate.`n`n"
Write-CrestUtf8 $notePath ($verificationNotice + [IO.File]::ReadAllText($notePath))
$manifestPath = Join-Path $releaseDir 'release-manifest.json'
$manifest = [ordered]@{
    SchemaVersion = 1; ApplicationVersion = $metadata.Version; Tag = "v$($metadata.Version)"
    CandidateDllSHA256 = (Get-FileHash -LiteralPath (Join-Path $package.DistributionDir 'Crest.dll') -Algorithm SHA256).Hash
    SourceFingerprint = $verifiedBuild.SourceFingerprint; RuntimeIdentifier = $metadata.Rid; Runtime = $metadata.Runtime
    FullAutomatedSuiteVerified = ($skipped.Count -eq 0); SkippedChecks = $skipped
    DomainOutcomes = $domainRecord.Passed; PackagedUiOutcomes = $uiRecord.passed
    ProvenanceChecks = @($provenanceRecord.Outcomes).Count; EvidenceChecks = @($evidenceRecord.Outcomes).Count; DocumentationFilesValidated = $documentationRecord.Documents
    P1GatesPassed = $gateReport.P1GatesPassed; OpenGateCount = $gateReport.OpenGateCount
    UnsignedInstaller = $true; OptionalMsixIncluded = $false
    Assets = @(@($installerPath, $zipPath, $checksumPath, (Join-Path $releaseDir 'release-gate-report.json'), (Join-Path $releaseDir 'release-notes.md')) | ForEach-Object { [ordered]@{ Name = [IO.Path]::GetFileName($_); SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash; Bytes = (Get-Item -LiteralPath $_).Length } })
}
Write-CrestUtf8 $manifestPath (($manifest | ConvertTo-Json -Depth 6) + "`n")
Write-Host "Validated Crest $($metadata.Version) release candidate assets: $releaseDir; P1 gates passed: $($gateReport.P1GatesPassed)"
return [pscustomobject]@{ Version = $metadata.Version; Tag = "v$($metadata.Version)"; Installer = $installerPath; PortableZip = $zipPath; Checksums = $checksumPath; Notes = (Join-Path $releaseDir 'release-notes.md'); GateReport = (Join-Path $releaseDir 'release-gate-report.json'); Manifest = $manifestPath; P1GatesPassed = $gateReport.P1GatesPassed; BuildTestsRun = (-not $SkipBuild) }

param(
    [string]$Tag = '',
    [switch]$SkipBuild,
    [switch]$UpdateLockFiles
)
. (Join-Path $PSScriptRoot 'release-common.ps1')
$metadata = Get-CrestBuildMetadata
Assert-CrestTag $Tag $metadata
if ($SkipBuild -and $UpdateLockFiles) { throw '-UpdateLockFiles requires a build.' }
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -UpdateLockFiles:$UpdateLockFiles
}
$package = & (Join-Path $PSScriptRoot 'package.ps1')
$releaseDir = Reset-CrestArtifactDirectory (Join-Path $script:CrestRoot 'artifacts\release')
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
Write-Host "Validated Crest $($metadata.Version) release assets: $releaseDir"
return [pscustomobject]@{ Version = $metadata.Version; Tag = "v$($metadata.Version)"; Installer = $installerPath; PortableZip = $zipPath; Checksums = $checksumPath; Notes = (Join-Path $releaseDir 'release-notes.md'); BuildTestsRun = (-not $SkipBuild) }

param([string]$SourceDir = '')
. (Join-Path $PSScriptRoot 'release-common.ps1')
if ($env:OS -ne 'Windows_NT') { throw 'Crest packaging requires Windows.' }
$metadata = Get-CrestBuildMetadata
$repository = Get-CrestGitHubRepository
$documentationTag = "v$($metadata.Version)"
if ([string]::IsNullOrWhiteSpace($SourceDir)) { $SourceDir = Join-Path $script:CrestRoot 'artifacts\portable\Crest' }
$SourceDir = [IO.Path]::GetFullPath($SourceDir)
Assert-CrestDistribution $SourceDir $metadata
$null = Assert-CrestBuildVerification $SourceDir $metadata
foreach ($item in Get-ChildItem -LiteralPath $SourceDir -Recurse -Force) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish input cannot contain reparse points.' }
}
$distributionDir = Resolve-CrestArtifactPath (Join-Path $script:CrestRoot 'artifacts\distribution\Crest')
if ($distributionDir.StartsWith($SourceDir.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $SourceDir.StartsWith($distributionDir.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($distributionDir, $SourceDir, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The clean staging directory must be separate from the publish source.'
}
$distributionDir = Reset-CrestArtifactDirectory $distributionDir
# Runtime allowlist excludes every PDB, QA report, screenshot, and machine log.
foreach ($file in Get-ChildItem -LiteralPath $SourceDir -File -Recurse) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish input cannot contain reparse points.' }
    $relative = $file.FullName.Substring($SourceDir.TrimEnd('\').Length + 1)
    if ($relative -match '^(docs|licenses)\\') { continue }
    if ($file.Extension -notin @('.dll', '.exe', '.ico') -and $relative -notin @('Crest.deps.json', 'Crest.runtimeconfig.json')) { continue }
    $destination = Join-Path $distributionDir $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}
foreach ($doc in @('USER_GUIDE.md', 'RELEASE_NOTES.md', 'BUILD.md')) {
    $destination = Join-Path $distributionDir ('docs\' + $doc)
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-CrestReleaseDocument (Join-Path $script:CrestRoot ('docs\' + $doc)) $destination $repository $documentationTag
}
foreach ($doc in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    if ([IO.Path]::GetExtension($doc) -eq '.md') {
        Copy-CrestReleaseDocument (Join-Path $script:CrestRoot $doc) (Join-Path $distributionDir $doc) $repository $documentationTag
    } else { Copy-Item -LiteralPath (Join-Path $script:CrestRoot $doc) -Destination (Join-Path $distributionDir $doc) -Force }
}
$licenseDir = Join-Path $distributionDir 'licenses'
New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
$assets = Get-Content -LiteralPath (Join-Path $script:CrestRoot 'src\Crest\obj\project.assets.json') -Raw | ConvertFrom-Json
$licensedComponents = @(Copy-CrestDependencyLicenses $assets $metadata $licenseDir)
$packages = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | Sort-Object Name | ForEach-Object {
    $identity = $_.Name.Split('/')
    [ordered]@{ Name = $identity[0]; Version = $identity[1] }
})
$dependencyRecord = [ordered]@{ Application = 'Crest'; Version = $metadata.Version; Architecture = $metadata.Rid; Framework = $metadata.Framework; Sdk = $metadata.Sdk; Runtime = $metadata.Runtime; Distribution = 'Self-contained'; Packages = $packages; LicensedComponents = $licensedComponents }
Write-CrestUtf8 (Join-Path $licenseDir 'dependency-record.json') (($dependencyRecord | ConvertTo-Json -Depth 6) + "`n")
foreach ($file in Get-ChildItem -LiteralPath $distributionDir -File -Recurse) { $file.LastWriteTimeUtc = [datetime]'2000-01-01T00:00:00Z' }
Assert-CrestDistribution $distributionDir $metadata

# Pin the official compiler installer by release-asset SHA256 and publisher.
$compilerHash = 'A549929319F9120716677C685535CA4671E4B937B5FDC87E2585F32D8EB30E8C'
$builderDir = Join-Path $script:CrestRoot '.tools\innosetup'
$compiler = Join-Path $builderDir 'ISCC.exe'
$marker = Join-Path $builderDir 'crest-compiler.json'
$needBootstrap = $true
if ((Test-Path -LiteralPath $compiler) -and (Test-Path -LiteralPath $marker)) {
    $record = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    $needBootstrap = $record.SourceSHA256 -ne $compilerHash -or $record.CompilerSHA256 -ne (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash
}
if ($needBootstrap) {
    $downloadDir = Join-Path $script:CrestRoot '.tools\inno-download'
    New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null
    $archive = Join-Path $downloadDir 'innosetup.exe'
    if (-not (Test-Path -LiteralPath $archive)) {
        Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe' -OutFile $archive -TimeoutSec 120
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $compilerHash) { throw 'Inno Setup compiler download does not match the pinned official SHA256.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $archive
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys') { throw 'Inno Setup compiler publisher verification failed.' }
    $arguments = '/PORTABLE=1 /CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /TASKS="" /DIR="' + $builderDir + '"'
    $process = Start-Process -FilePath $archive -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Portable compiler extraction exceeded two minutes.' }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw 'Portable compiler extraction failed.' }
    Write-CrestUtf8 $marker (([ordered]@{ Version = '6.7.4'; SourceSHA256 = $compilerHash; CompilerSHA256 = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash } | ConvertTo-Json) + "`n")
}
$installerPath = Resolve-CrestArtifactPath (Join-Path $script:CrestRoot "artifacts\Crest-Setup-$($metadata.Version)-x64.exe")
$outputDir = Split-Path -Parent $installerPath
if (Test-Path -LiteralPath $installerPath) { Remove-Item -LiteralPath $installerPath -Force }
$arguments = '/Qp "/DSourceDir=' + $distributionDir + '" "/DOutputDir=' + $outputDir + '" /DAppVersion=' + $metadata.Version + ' /DAppFileVersion=' + $metadata.AssemblyVersion + ' "' + (Join-Path $script:CrestRoot 'installer\Crest.iss') + '"'
$process = Start-Process -FilePath $compiler -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(300000)) { $process.Kill(); throw 'Installer compilation exceeded five minutes.' }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $installerPath)) { throw 'Installer compilation failed.' }
$installerInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($installerPath)
# Inno Setup reserves padded version-resource strings for later updates/signing.
if ($installerInfo.FileVersion.Trim() -ne $metadata.AssemblyVersion -or $installerInfo.ProductVersion.Trim() -ne $metadata.Version) { throw 'Compiled installer version does not match Crest.csproj.' }
$checksum = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-CrestUtf8 (Join-Path $outputDir "Crest-Setup-$($metadata.Version)-x64.sha256") ($checksum + '  ' + [IO.Path]::GetFileName($installerPath) + "`n")
Write-Host "Installer: $installerPath"
return [pscustomobject]@{ Version = $metadata.Version; InstallerPath = $installerPath; DistributionDir = $distributionDir }

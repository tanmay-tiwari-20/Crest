param([string]$ArtifactRoot = '')
. (Join-Path $PSScriptRoot 'release-common.ps1')
$metadata = Get-CrestBuildMetadata
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) { $ArtifactRoot = Join-Path $script:CrestRoot 'artifacts' }
$ArtifactRoot = Resolve-CrestArtifactPath ([IO.Path]::GetFullPath($ArtifactRoot)) -AllowRoot
$directory = Reset-CrestArtifactDirectory (Join-Path $ArtifactRoot 'release-verification-tests\publish')
$file = Join-Path $directory 'Crest.dll'
$recordPath = Join-Path $directory '.crest-build-verification.json'
Write-CrestUtf8 $file 'isolated hash-verification fixture; never launched'
$fingerprint = Get-CrestSourceFingerprint
$outcomes = [Collections.Generic.List[object]]::new()
function Assert-Rejected([string]$Name, [scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Release verification did not reject: $Name" }
    $outcomes.Add([ordered]@{ Test = $Name; Passed = $true })
}
Assert-Rejected 'A publish without build/test evidence is rejected' { Assert-CrestBuildVerification $directory $metadata }
Write-CrestBuildVerification $directory $metadata 'Release' $true $fingerprint
$null = Assert-CrestBuildVerification $directory $metadata
$outcomes.Add([ordered]@{ Test = 'A matching Release source/file record with passing tests is accepted'; Passed = $true })
Write-CrestUtf8 $file 'modified publish content'
Assert-Rejected 'Changed binary content is rejected even at the same version' { Assert-CrestBuildVerification $directory $metadata }
Write-CrestBuildVerification $directory $metadata 'Release' $false $fingerprint
Assert-Rejected 'A build that skipped outcome tests is rejected' { Assert-CrestBuildVerification $directory $metadata }
Write-CrestBuildVerification $directory $metadata 'Debug' $true $fingerprint
Assert-Rejected 'A Debug publish is rejected' { Assert-CrestBuildVerification $directory $metadata }
Write-CrestBuildVerification $directory $metadata 'Release' $true $fingerprint
$record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
$record.SourceFingerprint = 'not-the-current-source'
Write-CrestUtf8 $recordPath ($record | ConvertTo-Json -Depth 5)
Assert-Rejected 'A stale source/test fingerprint is rejected' { Assert-CrestBuildVerification $directory $metadata }
Write-CrestBuildVerification $directory $metadata 'Release' $true $fingerprint
Write-CrestUtf8 (Join-Path $directory 'unexpected.dll') 'extra unverified file'
Assert-Rejected 'Added unverified publish files are rejected' { Assert-CrestBuildVerification $directory $metadata }
Remove-Item -LiteralPath (Join-Path $directory 'unexpected.dll')
Write-CrestBuildVerification $directory $metadata 'Release' $true $fingerprint
$verifiedBuild = Assert-CrestBuildVerification $directory $metadata
$documentation = Join-Path $directory 'README.md'
Write-CrestUtf8 $documentation 'reviewed public distribution documentation'
Write-CrestPackagedVerification $directory $metadata $verifiedBuild
$packaged = Assert-CrestBuildVerification $directory $metadata
if ($packaged.Files.Count -ne 2 -or $packaged.VerifiedUtc -ne $verifiedBuild.VerifiedUtc) { throw 'Packaged provenance lost documentation hashes or the verified build timestamp.' }
$outcomes.Add([ordered]@{ Test = 'Packaged provenance binds runtime and reviewed documents while retaining build identity'; Passed = $true })
Write-CrestBuildVerification $directory $metadata 'Release' $true $fingerprint @('Native clipboard listener and write verification (11 outcomes)')
$scopedBuild = Assert-CrestBuildVerification $directory $metadata
Write-CrestPackagedVerification $directory $metadata $scopedBuild
$scopedPackage = Assert-CrestBuildVerification $directory $metadata
if ($scopedPackage.FullAutomatedSuiteVerified -ne $false -or @($scopedPackage.SkippedChecks).Count -ne 1) { throw 'Packaged verification discarded its explicitly skipped checks.' }
$outcomes.Add([ordered]@{ Test = 'Candidate packages retain explicit automated verification exclusions without claiming a complete suite'; Passed = $true })
$scopedPackage.FullAutomatedSuiteVerified = $true
Write-CrestUtf8 $recordPath ($scopedPackage | ConvertTo-Json -Depth 5)
Assert-Rejected 'Contradictory full-suite claims with skipped checks' { Assert-CrestBuildVerification $directory $metadata }
$scopedPackage.PSObject.Properties.Remove('FullAutomatedSuiteVerified')
Write-CrestUtf8 $recordPath ($scopedPackage | ConvertTo-Json -Depth 5)
Assert-Rejected 'Skipped checks without an explicit full-suite status' { Assert-CrestBuildVerification $directory $metadata }
Write-CrestBuildVerification $directory $metadata 'Release' $true $fingerprint
$verifiedBuild = Assert-CrestBuildVerification $directory $metadata
$defaultRoot = Join-Path $script:CrestRoot 'artifacts'
Assert-Rejected 'Artifact reset validation still rejects the entire artifacts root' { Resolve-CrestArtifactPath $defaultRoot }
Assert-Rejected 'A trailing separator cannot bypass artifact-root reset protection' { Resolve-CrestArtifactPath ($defaultRoot + [IO.Path]::DirectorySeparatorChar) }
if ((Resolve-CrestArtifactPath $defaultRoot -AllowRoot) -ne $defaultRoot) { throw 'Default artifact-root validation did not retain the exact repository root.' }
$outcomes.Add([ordered]@{ Test = 'Default artifact-root configuration is permitted without relaxing recursive-reset validation'; Passed = $true })
Assert-Rejected 'Artifact root opt-in cannot admit paths outside artifacts' { Resolve-CrestArtifactPath (Join-Path $script:CrestRoot 'tmp') -AllowRoot }
Write-CrestUtf8 $documentation 'tampered packaged documentation'
Assert-Rejected 'Changed packaged documentation is rejected' { Assert-CrestBuildVerification $directory $metadata }
$verifiedBuild.TestsPassed = $false
Assert-Rejected 'Packaged provenance cannot promote a build with skipped tests' { Write-CrestPackagedVerification $directory $metadata $verifiedBuild }
$fingerprintProbe = Join-Path (Split-Path -Parent $directory) 'source-fingerprint.ps1'
$helperPath = (Join-Path $script:CrestRoot 'scripts\release-common.ps1').Replace("'", "''")
Write-CrestUtf8 $fingerprintProbe (". '" + $helperPath + "'`nGet-CrestSourceFingerprint`n")
$windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$legacyFingerprint = & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $fingerprintProbe
if ($LASTEXITCODE -ne 0 -or $legacyFingerprint.Trim() -ne $fingerprint) { throw 'Windows PowerShell source fingerprint differs from the verification host.' }
$modernPowerShell = Get-Command pwsh -ErrorAction SilentlyContinue
$modernChecked = $false
if ($null -ne $modernPowerShell) {
    $modernFingerprint = & $modernPowerShell.Source -NoProfile -File $fingerprintProbe
    if ($LASTEXITCODE -ne 0 -or $modernFingerprint.Trim() -ne $fingerprint) { throw 'PowerShell 7 source fingerprint differs from Windows PowerShell.' }
    $modernChecked = $true
}
$outcomes.Add([ordered]@{ Test = 'Ordinal source fingerprint matches Windows PowerShell and the available PowerShell 7 host'; Passed = $true; PowerShell7Available = $modernChecked })
$report = [ordered]@{ SchemaVersion = 1; TimestampUtc = [DateTimeOffset]::UtcNow.ToString('O'); SourceFingerprint = $fingerprint; Passed = $true; Outcomes = $outcomes.ToArray() }
Write-CrestUtf8 (Join-Path (Split-Path -Parent $directory) 'results.json') (($report | ConvertTo-Json -Depth 5) + "`n")
Write-Host "$($outcomes.Count) release provenance checks passed."

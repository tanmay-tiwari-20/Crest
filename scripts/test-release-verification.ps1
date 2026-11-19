. (Join-Path $PSScriptRoot 'release-common.ps1')
$metadata = Get-CrestBuildMetadata
$directory = Reset-CrestArtifactDirectory (Join-Path $script:CrestRoot 'artifacts\release-verification-tests\publish')
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
$fingerprintProbe = Join-Path (Split-Path -Parent $directory) 'source-fingerprint.ps1'
Write-CrestUtf8 $fingerprintProbe ". (Join-Path `$PSScriptRoot '..\..\scripts\release-common.ps1')`nGet-CrestSourceFingerprint`n"
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

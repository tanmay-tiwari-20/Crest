param(
    [string]$PublishDir = '',
    [string]$GatePath = '',
    [string]$OutputPath = '',
    [switch]$RequireP1Gates
)
. (Join-Path $PSScriptRoot 'release-common.ps1')
$metadata = Get-CrestBuildMetadata
if ([string]::IsNullOrWhiteSpace($PublishDir)) { $PublishDir = Join-Path $script:CrestRoot 'artifacts\portable\Crest' }
if ([string]::IsNullOrWhiteSpace($GatePath)) { $GatePath = Join-Path $script:CrestRoot 'docs\release-gates.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $script:CrestRoot 'artifacts\release-gate-report.json' }
$PublishDir = [IO.Path]::GetFullPath($PublishDir)
Assert-CrestDistribution $PublishDir $metadata
$build = Assert-CrestBuildVerification $PublishDir $metadata
$dllHash = (Get-FileHash -LiteralPath (Join-Path $PublishDir 'Crest.dll') -Algorithm SHA256).Hash
$gates = Get-Content -LiteralPath $GatePath -Raw | ConvertFrom-Json
if ($gates.SchemaVersion -ne 1 -or $gates.ApplicationVersion -ne $metadata.Version) { throw 'Release gate schema/version does not match the candidate.' }
$requiredIds = @((1..17) + (20..24) | ForEach-Object { 'AC{0:D2}' -f $_ }) + @('PER-readiness', 'PER-hover', 'PER-feedback', 'PER-suppression', 'PER-frames', 'PER-idle', 'PER-memory', 'PER-hidden-rendering', 'PER-battery')
$recordedIds = @($gates.Gates | ForEach-Object { $_.Id })
if ($recordedIds.Count -ne $requiredIds.Count -or @($recordedIds | Sort-Object -Unique).Count -ne $recordedIds.Count -or @(Compare-Object $requiredIds $recordedIds).Count -gt 0) {
    throw 'Release gate record must contain every required P0/P1 acceptance and performance gate exactly once.'
}
$candidateMatches = $gates.CandidateDllSHA256 -eq $dllHash -and $gates.SourceFingerprint -eq $build.SourceFingerprint
$evaluated = @(foreach ($gate in $gates.Gates) {
    if ($gate.Status -notin @('pending', 'partial', 'pass', 'fail')) { throw "Invalid gate status: $($gate.Id)" }
    $status = $gate.Status
    $reason = $gate.Notes
    if ($status -eq 'pass') {
        if (-not $candidateMatches) { $status = 'stale'; $reason = 'Evidence is not bound to this candidate DLL and source fingerprint.' }
        elseif (@($gate.Evidence).Count -eq 0) { throw "Passing gate has no recorded evidence: $($gate.Id)" }
        else {
            foreach ($evidence in $gate.Evidence) {
                if ([IO.Path]::IsPathRooted($evidence.Path) -or $evidence.Path -match '(^|[/\\])\.\.([/\\]|$)') { throw 'Gate evidence paths must be repository-relative.' }
                $evidencePath = [IO.Path]::GetFullPath((Join-Path $script:CrestRoot $evidence.Path))
                if (-not $evidencePath.StartsWith($script:CrestRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { throw "Gate evidence missing: $($evidence.Path)" }
                $ancestor = $evidencePath
                while ($ancestor -ne $script:CrestRoot) {
                    if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Gate evidence cannot traverse reparse points.' }
                    $ancestor = Split-Path -Parent $ancestor
                }
                if ($evidence.SHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash -ne $evidence.SHA256) { throw "Gate evidence hash mismatch: $($evidence.Path)" }
            }
        }
    }
    [ordered]@{ Id = $gate.Id; Requirement = $gate.Requirement; Status = $status; Notes = $reason; Evidence = @($gate.Evidence) }
})
$open = @($evaluated | Where-Object { $_.Status -ne 'pass' })
$passed = $open.Count -eq 0 -and @($gates.VisibleOverAppIncidents).Count -eq 0
$OutputPath = Resolve-CrestArtifactPath $OutputPath
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
$report = [ordered]@{
    SchemaVersion = 1; GeneratedUtc = [DateTimeOffset]::UtcNow.ToString('O'); ApplicationVersion = $metadata.Version
    CandidateDllSHA256 = $dllHash; SourceFingerprint = $build.SourceFingerprint; BuildTestsVerified = $true
    P1GatesPassed = $passed; ReleaseStatus = $(if ($passed) { 'P1 acceptance recorded' } else { 'Release candidate; P1 gates open' })
    OpenGateCount = $open.Count; VisibleOverAppIncidents = @($gates.VisibleOverAppIncidents); Gates = $evaluated
    Limitations = 'A passing source-bound build is automatic evidence only. Native acceptance must be observed and recorded. AC18 clipboard and AC19 optional integrations remain outside the P1 gate set; P2 local functionality has separate domain/UI verification. Historical evidence does not qualify a changed candidate.'
}
Write-CrestUtf8 $OutputPath (($report | ConvertTo-Json -Depth 8) + "`n")
Write-Host "P1 gate verification: $($evaluated.Count - $open.Count)/$($evaluated.Count) acceptance gates recorded as passing; $($open.Count) open."
if ($RequireP1Gates -and -not $passed) { throw "P1 release gates are incomplete or failed. Review $OutputPath. Candidate packaging remains available without -RequireP1Gates." }
return [pscustomobject]@{ ReportPath = $OutputPath; P1GatesPassed = $passed; OpenGateCount = $open.Count }

param(
    [string]$PublishDir = '',
    [string]$GatePath = '',
    [string]$OutputPath = '',
    [switch]$RequireP1Gates
)
. (Join-Path $PSScriptRoot 'release-common.ps1')
. (Join-Path $PSScriptRoot 'release-gate-common.ps1')
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
$evaluated = @(Get-CrestEvaluatedGates $gates $dllHash $build.SourceFingerprint $script:CrestRoot)
$open = @($evaluated | Where-Object { $_.Status -ne 'pass' })
$passed = $open.Count -eq 0 -and @($gates.VisibleOverAppIncidents).Count -eq 0
$OutputPath = Resolve-CrestArtifactPath $OutputPath
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
$report = [ordered]@{
    SchemaVersion = 1; GeneratedUtc = [DateTimeOffset]::UtcNow.ToString('O'); ApplicationVersion = $metadata.Version
    CandidateDllSHA256 = $dllHash; SourceFingerprint = $build.SourceFingerprint; BuildTestsVerified = $true
    P1GatesPassed = $passed; ReleaseStatus = $(if ($passed) { 'P1 acceptance recorded' } else { 'Release candidate; P1 gates open' })
    OpenGateCount = $open.Count; VisibleOverAppIncidents = @($gates.VisibleOverAppIncidents); Gates = $evaluated
    Limitations = 'A passing source-bound build is automatic evidence only. Native acceptance must be observed and recorded. Passing JSON evidence must identify this DLL/source and explicitly declare a satisfied GateOutcomes entry for each linked gate. Historical pass, partial and fail observations remain stale for changed candidates. AC18 clipboard and AC19 optional integrations remain outside the P1 gate set; P2 local functionality has separate domain/UI verification.'
}
Write-CrestUtf8 $OutputPath (($report | ConvertTo-Json -Depth 8) + "`n")
Write-Host "P1 gate verification: $($evaluated.Count - $open.Count)/$($evaluated.Count) acceptance gates recorded as passing; $($open.Count) open."
if ($RequireP1Gates -and -not $passed) { throw "P1 release gates are incomplete or failed. Review $OutputPath. Candidate packaging remains available without -RequireP1Gates." }
return [pscustomobject]@{ ReportPath = $OutputPath; P1GatesPassed = $passed; OpenGateCount = $open.Count }

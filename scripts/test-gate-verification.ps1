. (Join-Path $PSScriptRoot 'release-common.ps1')
. (Join-Path $PSScriptRoot 'release-gate-common.ps1')
$directory = Reset-CrestArtifactDirectory (Join-Path $script:CrestRoot 'artifacts\gate-verification-tests')
$summaryPath = Join-Path $directory 'summary.json'
$relativeSummaryPath = $summaryPath.Substring($script:CrestRoot.Length + 1).Replace('\', '/')
# Synthetic identities exercise the same evaluator used by packaging; no application is built or launched.
$candidateHash = 'A' * 64
$candidateSource = 'B' * 64
$template = Get-Content -LiteralPath (Join-Path $script:CrestRoot 'docs\release-gates.json') -Raw | ConvertFrom-Json
$outcomes = [Collections.Generic.List[object]]::new()
function New-GateFixture {
    $record = $template | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $record.CandidateDllSHA256 = $candidateHash
    $record.SourceFingerprint = $candidateSource
    foreach ($gate in $record.Gates) { $gate.Status = 'pass'; $gate.Notes = 'Synthetic gate-validation fixture; no native acceptance performed.' }
    return $record
}
function New-SummaryFixture {
    return [pscustomobject]@{
        SchemaVersion = 1; CandidateDllSHA256 = $candidateHash; SourceFingerprint = $candidateSource
        GateOutcomes = @($template.Gates | ForEach-Object { [pscustomobject]@{ Id = $_.Id; Satisfied = $true } })
    }
}
function Write-SummaryFixture($Summary, $Record) {
    Write-CrestUtf8 $summaryPath (($Summary | ConvertTo-Json -Depth 8) + "`n")
    $hash = (Get-FileHash -LiteralPath $summaryPath -Algorithm SHA256).Hash
    foreach ($gate in $Record.Gates) { $gate.Evidence = @([pscustomobject]@{ Path = $relativeSummaryPath; SHA256 = $hash }) }
}
function Get-FixtureEvaluation($Record) {
    return @(Get-CrestEvaluatedGates $Record $candidateHash $candidateSource $script:CrestRoot)
}
function Assert-GateRejected([string]$Name, [string]$ExpectedMessage, [scriptblock]$Action) {
    $message = $null
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    if ($null -eq $message -or $message -notmatch $ExpectedMessage) { throw "Gate verification did not reject $Name for the expected reason: $message" }
    $outcomes.Add([ordered]@{ Test = $Name; Passed = $true })
}
function Assert-GatesStale([string]$Name, $Record) {
    $evaluated = @(Get-FixtureEvaluation $Record)
    if ($evaluated.Count -ne 31 -or @($evaluated | Where-Object { $_.Status -ne 'stale' }).Count -gt 0) { throw "Historical evidence was accepted: $Name" }
    $outcomes.Add([ordered]@{ Test = $Name; Passed = $true })
}

$record = New-GateFixture
$summary = New-SummaryFixture
Write-SummaryFixture $summary $record
$evaluated = @(Get-FixtureEvaluation $record)
if ($evaluated.Count -ne 31 -or @($evaluated | Where-Object { $_.Status -ne 'pass' }).Count -gt 0) { throw 'Explicit candidate-bound outcomes did not qualify their 31 gates.' }
$outcomes.Add([ordered]@{ Test = 'Matching candidate summaries explicitly satisfying every linked gate qualify'; Passed = $true })

$record = New-GateFixture
$record.CandidateDllSHA256 = 'C' * 64
$record.Gates[1].Status = 'partial'
$record.Gates[2].Status = 'fail'
Write-SummaryFixture (New-SummaryFixture) $record
Assert-GatesStale 'Historical top-level pass, partial and fail observations remain stale' $record
$evaluated = @(Get-FixtureEvaluation $record)
if ($evaluated[1].RecordedStatus -ne 'partial' -or $evaluated[1].Notes -notmatch 'Historical notes') { throw 'Stale partial provenance was lost.' }

$record = New-GateFixture
$summary = New-SummaryFixture
$summary.CandidateDllSHA256 = 'C' * 64
Write-SummaryFixture $summary $record
Assert-GatesStale 'Current top-level identity cannot transfer historical DLL evidence' $record

$record = New-GateFixture
$summary = New-SummaryFixture
$summary.SourceFingerprint = 'C' * 64
Write-SummaryFixture $summary $record
Assert-GatesStale 'Current top-level identity cannot transfer historical source evidence' $record

foreach ($field in @('CandidateDllSHA256', 'SourceFingerprint')) {
    $record = New-GateFixture
    $summary = New-SummaryFixture
    $summary.PSObject.Properties.Remove($field)
    Write-SummaryFixture $summary $record
    Assert-GatesStale "Evidence without $field cannot qualify" $record
}

$record = New-GateFixture
$summary = New-SummaryFixture
$summary.PSObject.Properties.Remove('GateOutcomes')
Write-SummaryFixture $summary $record
Assert-GateRejected 'An unrelated current summary without explicit gate outcomes' 'must declare GateOutcomes' { Get-FixtureEvaluation $record }

$record = New-GateFixture
$summary = New-SummaryFixture
$summary.GateOutcomes = @([pscustomobject]@{ Id = 'AC02'; Satisfied = $true })
Write-SummaryFixture $summary $record
Assert-GateRejected 'Evidence for a different gate' 'must explicitly satisfy AC01 exactly once' { Get-FixtureEvaluation $record }

foreach ($invalidSatisfied in @($false, 'true')) {
    $record = New-GateFixture
    $summary = New-SummaryFixture
    $summary.GateOutcomes[0].Satisfied = $invalidSatisfied
    Write-SummaryFixture $summary $record
    Assert-GateRejected "Unsatisfied or non-Boolean outcome ($invalidSatisfied)" 'must explicitly satisfy AC01 exactly once' { Get-FixtureEvaluation $record }
}

$record = New-GateFixture
$summary = New-SummaryFixture
$summary.GateOutcomes += [pscustomobject]@{ Id = 'AC01'; Satisfied = $true }
Write-SummaryFixture $summary $record
Assert-GateRejected 'Duplicate qualification for the same gate' 'must explicitly satisfy AC01 exactly once' { Get-FixtureEvaluation $record }

$record = New-GateFixture
Write-SummaryFixture (New-SummaryFixture) $record
Write-CrestUtf8 $summaryPath 'tampered after the recorded hash'
Assert-GateRejected 'Changed evidence bytes' 'evidence hash mismatch' { Get-FixtureEvaluation $record }

$record = New-GateFixture
Write-CrestUtf8 $summaryPath 'not JSON; never actual evidence'
$hash = (Get-FileHash -LiteralPath $summaryPath -Algorithm SHA256).Hash
foreach ($gate in $record.Gates) { $gate.Evidence = @([pscustomobject]@{ Path = $relativeSummaryPath; SHA256 = $hash }) }
Assert-GateRejected 'Hashed unstructured evidence' 'must be a structured JSON summary' { Get-FixtureEvaluation $record }

$record = New-GateFixture
$summary = New-SummaryFixture
$summary.CandidateDllSHA256 = 'C' * 64
foreach ($gate in $record.Gates) { $gate.Status = 'partial' }
Write-SummaryFixture $summary $record
Assert-GatesStale 'Historical partial evidence remains stale with a current top-level identity' $record

$record = New-GateFixture
Write-SummaryFixture (New-SummaryFixture) $record
$record.Gates[0].Status = 'partial'
$record.Gates[0].Evidence = @()
$evaluated = @(Get-FixtureEvaluation $record)
if ($evaluated[0].Status -ne 'partial' -or $evaluated[0].Notes -notmatch 'No structured candidate-bound evidence') { throw 'Unsupported partial status did not expose its evidence limitation.' }
$outcomes.Add([ordered]@{ Test = 'A current partial row without evidence remains open and exposes the limitation'; Passed = $true })

$record.Gates[0].Status = 'pass'
Assert-GateRejected 'A passing gate without evidence' 'Passing gate has no recorded evidence' { Get-FixtureEvaluation $record }

$report = [ordered]@{ SchemaVersion = 1; TimestampUtc = [DateTimeOffset]::UtcNow.ToString('O'); SourceFingerprint = (Get-CrestSourceFingerprint); Passed = $true; SyntheticFixturesOnly = $true; Outcomes = $outcomes.ToArray() }
Write-CrestUtf8 (Join-Path $directory 'results.json') (($report | ConvertTo-Json -Depth 6) + "`n")
Write-Host "$($outcomes.Count) acceptance-evidence verification checks passed."

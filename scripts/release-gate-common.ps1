# Candidate-bound acceptance records; no build, launch, or machine-state changes.
function Get-CrestEvaluatedGates {
    param($GateRecord, [string]$CandidateDllSHA256, [string]$SourceFingerprint, [string]$RepositoryRoot)
    $requiredIds = @((1..17) + (20..24) | ForEach-Object { 'AC{0:D2}' -f $_ }) + @('PER-readiness', 'PER-hover', 'PER-feedback', 'PER-suppression', 'PER-frames', 'PER-idle', 'PER-memory', 'PER-hidden-rendering', 'PER-battery')
    $recordedIds = @($GateRecord.Gates | ForEach-Object { $_.Id })
    if ($recordedIds.Count -ne $requiredIds.Count -or @($recordedIds | Sort-Object -Unique).Count -ne $recordedIds.Count -or @(Compare-Object $requiredIds $recordedIds).Count -gt 0) {
        throw 'Release gate record must contain every required P0/P1 acceptance and performance gate exactly once.'
    }
    $candidateMatches = $GateRecord.CandidateDllSHA256 -is [string] -and $GateRecord.SourceFingerprint -is [string] -and
        $GateRecord.CandidateDllSHA256 -eq $CandidateDllSHA256 -and $GateRecord.SourceFingerprint -eq $SourceFingerprint
    foreach ($gate in $GateRecord.Gates) {
        if ($gate.Status -notin @('pending', 'partial', 'pass', 'fail')) { throw "Invalid gate status: $($gate.Id)" }
        $status = $gate.Status
        $reason = $gate.Notes
        if (-not $candidateMatches -and $status -ne 'pending') {
            $status = 'stale'
            $reason = "Recorded $($gate.Status) observations are not bound to this candidate DLL and source fingerprint. Historical notes: $($gate.Notes)"
        } elseif ($status -ne 'pending') {
            if ($status -eq 'pass' -and @($gate.Evidence).Count -eq 0) { throw "Passing gate has no recorded evidence: $($gate.Id)" }
            if (@($gate.Evidence).Count -eq 0) {
                $reason = "No structured candidate-bound evidence is recorded; this gate remains open. $($gate.Notes)"
            }
            foreach ($evidence in $gate.Evidence) {
                if ([IO.Path]::IsPathRooted($evidence.Path) -or $evidence.Path -match '(^|[/\\])\.\.([/\\]|$)') { throw 'Gate evidence paths must be repository-relative.' }
                $evidencePath = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $evidence.Path))
                if (-not $evidencePath.StartsWith($RepositoryRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { throw "Gate evidence missing: $($evidence.Path)" }
                $ancestor = $evidencePath
                while ($ancestor -ne $RepositoryRoot) {
                    if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Gate evidence cannot traverse reparse points.' }
                    $ancestor = Split-Path -Parent $ancestor
                }
                if ($evidence.SHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash -ne $evidence.SHA256) { throw "Gate evidence hash mismatch: $($evidence.Path)" }
                try { $summary = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json -ErrorAction Stop }
                catch { throw "Gate evidence must be a structured JSON summary: $($evidence.Path)" }
                if ($null -eq $summary -or $null -eq $summary.PSObject.Properties['SchemaVersion'] -or $summary.SchemaVersion -ne 1) {
                    throw "Gate evidence must have summary SchemaVersion 1: $($evidence.Path)"
                }
                if ($null -eq $summary.PSObject.Properties['CandidateDllSHA256'] -or $null -eq $summary.PSObject.Properties['SourceFingerprint'] -or
                    $summary.CandidateDllSHA256 -isnot [string] -or $summary.SourceFingerprint -isnot [string] -or
                    $summary.CandidateDllSHA256 -ne $CandidateDllSHA256 -or $summary.SourceFingerprint -ne $SourceFingerprint) {
                    $status = 'stale'
                    $reason = "Evidence document does not identify this candidate DLL and source fingerprint: $($evidence.Path). Recorded $($gate.Status) notes: $($gate.Notes)"
                    continue
                }
                if ($gate.Status -eq 'pass') {
                    if ($null -eq $summary.PSObject.Properties['GateOutcomes']) { throw "Passing evidence must declare GateOutcomes: $($evidence.Path)" }
                    $matching = @($summary.GateOutcomes | Where-Object { $null -ne $_ -and $null -ne $_.PSObject.Properties['Id'] -and $_.Id -ceq $gate.Id })
                    if ($matching.Count -ne 1 -or $null -eq $matching[0].PSObject.Properties['Satisfied'] -or
                        $matching[0].Satisfied -isnot [bool] -or $matching[0].Satisfied -ne $true) {
                        throw "Passing evidence must explicitly satisfy $($gate.Id) exactly once: $($evidence.Path)"
                    }
                }
            }
        }
        [ordered]@{ Id = $gate.Id; Requirement = $gate.Requirement; Status = $status; RecordedStatus = $gate.Status; Notes = $reason; Evidence = @($gate.Evidence) }
    }
}

[CmdletBinding()]
param(
    [string]$CandidateDirectory = '',
    [ValidateRange(10,3600)][int]$DurationSeconds = 600,
    [ValidateRange(0,60)][int]$WarmupSeconds = 15,
    [ValidateRange(5,60)][int]$ReadinessTimeoutSeconds = 30,
    [string]$OutputDirectory = ''
)
. (Join-Path $PSScriptRoot 'release-common.ps1')
if ($env:OS -ne 'Windows_NT') { throw 'Native acceptance measurements require Windows.' }
if ([string]::IsNullOrWhiteSpace($CandidateDirectory)) { $CandidateDirectory = Join-Path $script:CrestRoot 'artifacts\portable\Crest' }
$CandidateDirectory = [IO.Path]::GetFullPath($CandidateDirectory)
$metadata = Get-CrestBuildMetadata
Assert-CrestDistribution $CandidateDirectory $metadata
$build = Assert-CrestBuildVerification $CandidateDirectory $metadata
$candidateDllHash = (Get-FileHash -LiteralPath (Join-Path $CandidateDirectory 'Crest.dll') -Algorithm SHA256).Hash
$runId = [Guid]::NewGuid().ToString('N')
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $script:CrestRoot ('artifacts\native-acceptance\' + $runId) }
$OutputDirectory = Resolve-CrestArtifactPath $OutputDirectory
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory so earlier native evidence is preserved.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

# This compiles only the diagnostic host. It does not rebuild the verified application.
$dotnet = Get-CrestDotnet
$probeProject = Join-Path $script:CrestRoot 'tests\Crest.PlatformProbe\Crest.PlatformProbe.csproj'
& $dotnet build $probeProject --configuration Release --no-restore --nologo -p:BuildProjectReferences=false -p:TreatWarningsAsErrors=true -p:DebugType=None -p:DebugSymbols=false -nr:false -maxcpucount:2
if ($LASTEXITCODE -ne 0) { throw 'Native probe build failed. Restore the probe project before measuring.' }
$probeBuildDirectory = Join-Path $script:CrestRoot ('tests\Crest.PlatformProbe\bin\Release\' + $metadata.Framework)
$probeDirectory = Join-Path $OutputDirectory 'probe'
New-Item -ItemType Directory -Path $probeDirectory | Out-Null
Copy-Item -Path (Join-Path $probeBuildDirectory '*') -Destination $probeDirectory -Recurse
Copy-Item -LiteralPath (Join-Path $CandidateDirectory 'Crest.dll') -Destination (Join-Path $probeDirectory 'Crest.dll') -Force
if ((Get-FileHash -LiteralPath (Join-Path $probeDirectory 'Crest.dll') -Algorithm SHA256).Hash -ne $candidateDllHash) { throw 'Diagnostic DLL differs from the verified candidate.' }

$profileDirectory = [IO.Path]::GetFullPath((Join-Path $script:CrestRoot ('tmp\acceptance-' + $runId)))
New-Item -ItemType Directory -Path $profileDirectory | Out-Null
$settingsPath = Join-Path $profileDirectory 'settings.json'
$settings = [ordered]@{
    SchemaVersion = 1; Theme = 'System'; Motion = 'System'; StrictDesktop = $true; AllowOverApps = $false
    SelectedDisplay = ''; TriggerWidth = 240; TriggerDepth = 3; DwellMs = 120; GraceMs = 250
    MotionSpeed = 1; SizeScale = 1; StartWithWindows = $false; TimerSound = $false; AccessibilityShortcut = $false
    EnabledModules = @('Music','Controls','Timer','Launch','Shelf'); Launchers = @()
}
Write-CrestUtf8 $settingsPath (($settings | ConvertTo-Json -Depth 5) + "`n")
$sha = [Security.Cryptography.SHA256]::Create()
try { $profileKey = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($profileDirectory.ToUpperInvariant()))).Replace('-', '').Substring(0,24) }
finally { $sha.Dispose() }
$quitEventName = 'Local\Crest.Acceptance.' + $profileKey + '.Quit'
$readyPath = Join-Path $profileDirectory 'acceptance-ready.json'
$candidateExe = Join-Path $CandidateDirectory 'Crest.exe'
$createdProcess = $null
$createdStartTime = $null
$launchClock = [Diagnostics.Stopwatch]::StartNew()
try {
    $createdProcess = Start-Process -FilePath $candidateExe -ArgumentList @('--acceptance-profile', ('"{0}"' -f $profileDirectory)) -WindowStyle Hidden -PassThru
    $createdStartTime = $createdProcess.StartTime.ToUniversalTime()
    $ready = $null
    while ($launchClock.Elapsed.TotalSeconds -lt $ReadinessTimeoutSeconds) {
        $createdProcess.Refresh()
        if ($createdProcess.HasExited) { throw 'The isolated candidate exited before its readiness record.' }
        if (Test-Path -LiteralPath $readyPath -PathType Leaf) {
            try { $ready = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json; break }
            catch { $ready = $null }
        }
        Start-Sleep -Milliseconds 20
    }
    $launchClock.Stop()
    if ($null -eq $ready) { throw 'The isolated candidate did not record readiness before the timeout.' }
    if ($ready.ProcessId -ne $createdProcess.Id -or $ready.CandidateDllSHA256 -ne $candidateDllHash -or $ready.AllowOverApps -ne $false -or $ready.StrictDesktop -ne $true -or $ready.InstancePrefix + '.Quit' -ne $quitEventName) {
        throw 'The readiness record does not identify this isolated desktop-only candidate.'
    }
    if (-not [string]::Equals($createdProcess.Path,$candidateExe,[StringComparison]::OrdinalIgnoreCase)) { throw 'Launched process path differs from the candidate executable.' }
    $readiness = [ordered]@{
        RecordedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); CandidateDllSHA256 = $candidateDllHash
        SourceFingerprint = $build.SourceFingerprint; ProcessId = $createdProcess.Id
        ExternalLaunchToReadinessMilliseconds = [Math]::Round($launchClock.Elapsed.TotalMilliseconds,3)
        WithinThreeSecondObservation = ($launchClock.Elapsed.TotalMilliseconds -le 3000)
        IsolatedProfile = $true; Readiness = $ready
        Limitations = 'One external process-launch-to-readiness observation with OS cache state uncontrolled. This is not a clean-account install or cold-start qualification. The ready record represents completed application startup, not synchronous completion of every hardware refresh.'
    }
    Write-CrestUtf8 (Join-Path $OutputDirectory 'readiness.json') (($readiness | ConvertTo-Json -Depth 8) + "`n")
    Write-Host ('Isolated candidate ready in {0} ms; collecting passive native evidence.' -f $readiness.ExternalLaunchToReadinessMilliseconds)

    $oldDotnetRoot = $env:DOTNET_ROOT
    $oldDotnetRootX64 = $env:DOTNET_ROOT_X64
    try {
        $env:DOTNET_ROOT = Split-Path -Parent $dotnet
        $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
        $snapshotText = & (Join-Path $probeDirectory 'Crest.PlatformProbe.exe') $createdProcess.Id --settings-file $settingsPath --desktop-only | Out-String
        if ($LASTEXITCODE -ne 0) { throw 'The exact-candidate native snapshot failed.' }
        $snapshot = $snapshotText | ConvertFrom-Json
        if ($snapshot.AppDllSHA256 -ne $candidateDllHash) { throw 'The native snapshot loaded a different application DLL.' }
        Write-CrestUtf8 (Join-Path $OutputDirectory 'snapshot.json') $snapshotText
    } finally {
        $env:DOTNET_ROOT = $oldDotnetRoot
        $env:DOTNET_ROOT_X64 = $oldDotnetRootX64
    }
    $visibleWindows = @($snapshot.CrestWindows | Where-Object { $_.Visible })
    $nativeChecks = [ordered]@{
        ExactCandidateDllLoaded = $true
        ActualCandidateInstalledFiveHooksAtReadiness = ($ready.NativeHookCount -eq 5)
        ActualCandidateHiddenAtReadiness = ($ready.IslandVisible -eq $false -and $ready.SettingsVisible -eq $false)
        SeparateDiagnosticInstalledFiveHooks = ($snapshot.NativeHookCount -eq 5)
        NormalInputDesktop = ($snapshot.InputDesktop -eq 'Default')
        DesktopShellIdentityRecovered = (@($snapshot.VerifiedDesktopRoots).Count -gt 0)
        ActualCandidateWindowsHidden = ($visibleWindows.Count -eq 0 -and @($snapshot.CrestWindows).Count -gt 0)
        CurrentForegroundRejectedByDesktopPolicy = (-not $snapshot.CurrentForegroundEligible)
    }
    if ($WarmupSeconds -gt 0) { Start-Sleep -Seconds $WarmupSeconds }
    Write-Host ('Measuring hidden CPU and memory for {0} seconds without driving the desktop.' -f $DurationSeconds)
    $idlePath = Join-Path $OutputDirectory 'hidden-idle.json'
    & (Join-Path $PSScriptRoot 'Measure-HiddenIdle.ps1') -CrestProcessId $createdProcess.Id -DurationSeconds $DurationSeconds -OutputPath $idlePath | Out-Host
    $idle = Get-Content -LiteralPath $idlePath -Raw | ConvertFrom-Json
    if ($idle.CandidateDllSHA256 -ne $candidateDllHash -or $idle.SourceFingerprint -ne $build.SourceFingerprint) { throw 'Resource evidence does not match the verified candidate.' }
    $report = [pscustomobject][ordered]@{
        SchemaVersion = 1; RecordedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); ApplicationVersion = $metadata.Version
        CandidateDllSHA256 = $candidateDllHash; SourceFingerprint = $build.SourceFingerprint; ProcessId = $createdProcess.Id
        IsolatedProfile = $true; StrictDesktop = $true; AllowOverApps = $false; CoreModulesEnabled = $settings.EnabledModules
        ExternalLaunchToReadinessMilliseconds = $readiness.ExternalLaunchToReadinessMilliseconds
        NativeChecks = $nativeChecks
        RequestedDurationSeconds = $idle.RequestedDurationSeconds; ActualDurationSeconds = $idle.ActualDurationSeconds
        SampleCount = $idle.SampleCount; ProcessExitedEarly = $idle.ProcessExitedEarly; AllSampledWindowsHidden = $idle.AllSampledWindowsHidden
        HiddenCpuBudgetSatisfied = $idle.CpuBudgetBelowOnePercent; WarmMemoryBudgetSatisfied = $idle.WorkingSetBudgetAtMost200MB
        Evidence = @('readiness.json','snapshot.json','hidden-idle.json') | ForEach-Object { [ordered]@{ Path = $_; SHA256 = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $_) -Algorithm SHA256).Hash } }
        Limitations = 'Partial candidate-bound native evidence. No pointer, keyboard, app-switch, desktop drag, lock/sleep, display disconnect, accessibility, or clean-account installation was exercised. Foreground rejection describes the observed scene only. Presented frames, feedback/suppression percentiles, cold-start readiness, hidden rendering and battery impact remain separate gates. The user profile and existing Crest process were untouched.'
    }
    Write-CrestUtf8 (Join-Path $OutputDirectory 'report.json') (($report | ConvertTo-Json -Depth 10) + "`n")
    $report | Select-Object CandidateDllSHA256,ExternalLaunchToReadinessMilliseconds,RequestedDurationSeconds,ActualDurationSeconds,SampleCount,ProcessExitedEarly,AllSampledWindowsHidden,HiddenCpuBudgetSatisfied,WarmMemoryBudgetSatisfied | Format-List
    Write-Host ('Native evidence: ' + (Join-Path $OutputDirectory 'report.json'))
} finally {
    if ($null -ne $createdProcess) {
        $createdProcess.Refresh()
        if (-not $createdProcess.HasExited -and $createdProcess.StartTime.ToUniversalTime() -eq $createdStartTime -and [string]::Equals($createdProcess.Path,$candidateExe,[StringComparison]::OrdinalIgnoreCase)) {
            try { $quitEvent = [Threading.EventWaitHandle]::OpenExisting($quitEventName); try { $quitEvent.Set() | Out-Null } finally { $quitEvent.Dispose() } } catch { }
            if (-not $createdProcess.WaitForExit(5000)) { Stop-Process -Id $createdProcess.Id -Force }
        }
    }
}

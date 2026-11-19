[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][int]$CrestProcessId,
    [ValidateRange(10,3600)][int]$DurationSeconds = 600,
    [ValidateRange(1,60)][int]$SampleSeconds = 5,
    [string]$OutputPath = 'docs\hidden-idle-measurement.json'
)
$ErrorActionPreference = 'Stop'
if (-not ('CrestIdleNative' -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class CrestIdleNative {
  public delegate bool EnumProc(IntPtr hwnd, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
  public static int[] WindowCounts(int pid) {
    int all=0,visible=0;
    EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==(uint)pid){all++;if(IsWindowVisible(h))visible++;}return true;},IntPtr.Zero);
    return new [] {all,visible};
  }
  public static string ForegroundClass(){var s=new StringBuilder(200);GetClassName(GetForegroundWindow(),s,200);return s.ToString();}
}
"@
}
$crestProcess = Get-Process -Id $CrestProcessId
if ($crestProcess.ProcessName -ne 'Crest') { throw 'The selected process is not Crest.' }
$candidateDirectory = Split-Path -Parent $crestProcess.Path
$candidateDll = Join-Path $candidateDirectory 'Crest.dll'
if (-not (Test-Path -LiteralPath $candidateDll -PathType Leaf)) { throw 'The selected Crest process has no matching app DLL.' }
$candidateDllHash = (Get-FileHash -LiteralPath $candidateDll -Algorithm SHA256).Hash
$buildRecordPath = Join-Path $candidateDirectory '.crest-build-verification.json'
$sourceFingerprint = $null
if (Test-Path -LiteralPath $buildRecordPath -PathType Leaf) {
    $buildRecord = Get-Content -LiteralPath $buildRecordPath -Raw | ConvertFrom-Json
    $recordedDll = @($buildRecord.Files | Where-Object { $_.Path -eq 'Crest.dll' })
    if ($recordedDll.Count -eq 1 -and $recordedDll[0].SHA256 -eq $candidateDllHash) { $sourceFingerprint = $buildRecord.SourceFingerprint }
}
$cpuStart = $crestProcess.TotalProcessorTime.TotalSeconds
$logicalProcessors = [Environment]::ProcessorCount
$clock = [Diagnostics.Stopwatch]::StartNew()
$startedUtc = [DateTimeOffset]::UtcNow
$samples = [Collections.Generic.List[object]]::new()
$exitedEarly = $false
while ($true) {
    try { $crestProcess.Refresh(); if ($crestProcess.HasExited) { $exitedEarly=$true; break } }
    catch { $exitedEarly=$true; break }
    $counts = [CrestIdleNative]::WindowCounts($CrestProcessId)
    $samples.Add([PSCustomObject]@{
        ElapsedSeconds=[Math]::Round($clock.Elapsed.TotalSeconds,3)
        ProcessCpuSeconds=$crestProcess.TotalProcessorTime.TotalSeconds
        WorkingSetBytes=$crestProcess.WorkingSet64
        PrivateBytes=$crestProcess.PrivateMemorySize64
        TopLevelWindowCount=$counts[0]
        VisibleWindowCount=$counts[1]
        ForegroundClass=[CrestIdleNative]::ForegroundClass()
    })
    if ($clock.Elapsed.TotalSeconds -ge $DurationSeconds) { break }
    Start-Sleep -Seconds ([Math]::Min($SampleSeconds,[Math]::Max(1,$DurationSeconds-$clock.Elapsed.TotalSeconds)))
}
$clock.Stop()
$elapsed = $clock.Elapsed.TotalSeconds
$cpuEnd = if ($samples.Count -gt 0) { $samples[$samples.Count-1].ProcessCpuSeconds } else { $cpuStart }
$measurementElapsed = if ($samples.Count -gt 1) { $samples[$samples.Count-1].ElapsedSeconds } else { $elapsed }
$visibleSamples = @($samples | Where-Object {$_.VisibleWindowCount -gt 0}).Count
$candidateUnchanged = (Test-Path -LiteralPath $candidateDll -PathType Leaf) -and (Get-FileHash -LiteralPath $candidateDll -Algorithm SHA256).Hash -eq $candidateDllHash
$validCandidateSample = $samples.Count -gt 0 -and -not $exitedEarly -and $candidateUnchanged
$allHidden = $visibleSamples -eq 0 -and $samples.Count -gt 0 -and -not $exitedEarly -and $candidateUnchanged
$cpuPercent = if ($measurementElapsed -gt 0) {100*($cpuEnd-$cpuStart)/($measurementElapsed*$logicalProcessors)} else {0}
$result = [PSCustomObject]@{
    StartedUtc=$startedUtc.ToString('O')
    ProcessId=$CrestProcessId
    CandidateDllSHA256=$candidateDllHash
    SourceFingerprint=$sourceFingerprint
    CandidateDllUnchanged=$candidateUnchanged
    RequestedDurationSeconds=$DurationSeconds
    ActualDurationSeconds=[Math]::Round($elapsed,3)
    CpuMeasurementDurationSeconds=$measurementElapsed
    LogicalProcessors=$logicalProcessors
    SampleCount=$samples.Count
    ProcessExitedEarly=$exitedEarly
    AllSampledWindowsHidden=$allHidden
    VisibleSamples=$visibleSamples
    MeanTotalDeviceCpuPercent=[Math]::Round($cpuPercent,4)
    MeanWorkingSetMiB=[Math]::Round((($samples | Measure-Object WorkingSetBytes -Average).Average/1MB),2)
    MaximumWorkingSetMiB=[Math]::Round((($samples | Measure-Object WorkingSetBytes -Maximum).Maximum/1MB),2)
    MaximumWorkingSetBytes=($samples | Measure-Object WorkingSetBytes -Maximum).Maximum
    CpuBudgetBelowOnePercent=($allHidden -and $measurementElapsed -ge 600 -and $cpuPercent -lt 1)
    WorkingSetBudgetAtMost200MB=($validCandidateSample -and (($samples | Measure-Object WorkingSetBytes -Maximum).Maximum -le 200000000))
    Limitations='Sampled HWND visibility cannot establish presentation timing, input interception during transient reveals, or foreground-suppression percentiles. CPU is normalized to all logical processors. Visible-window samples invalidate hidden-idle qualification. Early exit, absent samples or changed candidate DLL invalidate both resource results. A shorter run cannot satisfy the ten-minute CPU gate. Resident memory is an independent warm-core budget and can qualify while visible; it uses decimal 200 MB, with MiB fields as display conversions. An absent source fingerprint means only the recorded DLL is bound.'
    Samples=@($samples)
}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$result | Select-Object StartedUtc,CandidateDllSHA256,ActualDurationSeconds,SampleCount,AllSampledWindowsHidden,MeanTotalDeviceCpuPercent,MeanWorkingSetMiB,MaximumWorkingSetMiB,CpuBudgetBelowOnePercent,WorkingSetBudgetAtMost200MB | Format-List

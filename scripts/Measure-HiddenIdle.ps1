[CmdletBinding(DefaultParameterSetName='Measure')]
param(
    [Parameter(Mandatory=$true,ParameterSetName='Measure')][int]$CrestProcessId,
    [Parameter(Mandatory=$true,ParameterSetName='ObserverSelfTest')][switch]$ObserverSelfTest,
    [ValidateRange(10,3600)][int]$DurationSeconds = 600,
    [ValidateRange(1,60)][int]$SampleSeconds = 5,
    [string]$OutputPath = 'docs\hidden-idle-measurement.json'
)
$ErrorActionPreference = 'Stop'
function Test-CrestHiddenWindowQualification {
    param([int]$VisibleSamples, [int]$WindowShowEvents, [bool]$ObserverHealthy,
        [int]$EnumerationFailures, [int]$SampleCount, [bool]$ProcessExitedEarly, [bool]$CandidateUnchanged)
    return $VisibleSamples -eq 0 -and $WindowShowEvents -eq 0 -and $ObserverHealthy -and
        $EnumerationFailures -eq 0 -and $SampleCount -gt 0 -and -not $ProcessExitedEarly -and $CandidateUnchanged
}
if (-not ('CrestIdleNative' -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
public static class CrestIdleNative {
  public delegate bool EnumProc(IntPtr hwnd, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
  public static int[] WindowCounts(int pid) {
    int all=0,visible=0;
    bool succeeded=EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==(uint)pid){all++;if(IsWindowVisible(h))visible++;}return true;},IntPtr.Zero);
    return new [] {all,visible,succeeded?1:0};
  }
  public static string ForegroundClass(){var s=new StringBuilder(200);GetClassName(GetForegroundWindow(),s,200);return s.ToString();}
}
public sealed class CrestIdleWindowShowMonitor : IDisposable {
  private delegate void WinEventProc(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time);
  [System.Runtime.InteropServices.StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Hwnd;public uint Id;public UIntPtr WParam;public IntPtr LParam;public uint Time;public int X,Y;public uint Private; }
  [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventProc callback,uint process,uint thread,uint flags);
  [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
  [DllImport("user32.dll")] private static extern int GetMessage(out Message message,IntPtr hwnd,uint min,uint max);
  [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message,IntPtr hwnd,uint min,uint max,uint remove);
  [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread,uint message,UIntPtr wParam,IntPtr lParam);
  [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
  [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
  [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
  private readonly Thread worker;
  private readonly ManualResetEventSlim ready=new ManualResetEventSlim(false);
  private readonly WinEventProc callback;
  private readonly uint process;
  private IntPtr hook;
  private uint threadId;
  private int shows;
  private int initialized, active, stopping, disposed;
  private volatile bool failed;
  // Health is retained after a clean shutdown, but cannot recover from a blind interval.
  public bool Healthy { get { return Volatile.Read(ref initialized)==1&&!failed; } }
  public bool ObservationActive { get { return Volatile.Read(ref active)==1&&worker.IsAlive; } }
  public int ShowEvents { get { return Volatile.Read(ref shows); } }
  public CrestIdleWindowShowMonitor(int pid) {
    process=(uint)pid;
    callback=(h,e,w,o,c,t,ms)=>{if(w!=IntPtr.Zero&&o==0&&c==0&&(GetAncestor(w,2)==w||!IsWindow(w)))Interlocked.Increment(ref shows);};
    worker=new Thread(Run);worker.IsBackground=true;worker.Start();
    if(!ready.Wait(5000)){failed=true;Dispose();throw new InvalidOperationException("Window-show observation thread did not initialize.");}
    if(!Healthy||!ObservationActive){Dispose();throw new InvalidOperationException("Window-show observation hook could not be installed.");}
  }
  private void Run() {
    try {
      threadId=GetCurrentThreadId();
      Message message;PeekMessage(out message,IntPtr.Zero,0,0,0);
      hook=SetWinEventHook(0x8002,0x8002,IntPtr.Zero,callback,process,0,0);
      if(hook==IntPtr.Zero)failed=true;
      else {Volatile.Write(ref initialized,1);Volatile.Write(ref active,1);}
      ready.Set();
      if(hook==IntPtr.Zero)return;
      int result;while((result=GetMessage(out message,IntPtr.Zero,0,0))>0){}
      if(result<0||Volatile.Read(ref stopping)==0)failed=true;
    }
    catch {failed=true;ready.Set();}
    finally {
      Volatile.Write(ref active,0);
      if(hook!=IntPtr.Zero){if(!UnhookWinEvent(hook))failed=true;hook=IntPtr.Zero;}
    }
  }
  public void Dispose() {
    if(Interlocked.Exchange(ref disposed,1)!=0)return;
    if(!ObservationActive||!Healthy)failed=true;
    Volatile.Write(ref stopping,1);
    if(Volatile.Read(ref active)==1&&threadId!=0&&!PostThreadMessage(threadId,0x0012,UIntPtr.Zero,IntPtr.Zero))failed=true;
    if(!worker.Join(5000))failed=true;else ready.Dispose();
  }
}
"@
}
if ($ObserverSelfTest) {
    # Observe only this PowerShell process. These checks send no input or messages to user windows.
    $normal = [CrestIdleWindowShowMonitor]::new($PID)
    try {
        if (-not $normal.Healthy -or -not $normal.ObservationActive) { throw 'A newly installed observer was not active and healthy.' }
    } finally { $normal.Dispose() }
    if (-not $normal.Healthy -or $normal.ObservationActive) { throw 'Clean shutdown lost lifetime health or left the observer active.' }
    $normal.Dispose()
    if (-not $normal.Healthy) { throw 'Repeated disposal changed a clean observer result.' }
    if (-not (Test-CrestHiddenWindowQualification 0 0 $normal.Healthy 0 121 $false $true)) { throw 'A healthy all-hidden fixture was rejected.' }

    $interrupted = [CrestIdleWindowShowMonitor]::new($PID)
    try {
        $instanceFlags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance
        $staticFlags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
        $observerThread = [CrestIdleWindowShowMonitor].GetField('threadId',$instanceFlags).GetValue($interrupted)
        $postQuit = [CrestIdleWindowShowMonitor].GetMethod('PostThreadMessage',$staticFlags)
        $posted = $postQuit.Invoke($null,[object[]]@([uint32]$observerThread,[uint32]0x0012,[UIntPtr]::Zero,[IntPtr]::Zero))
        $worker = [CrestIdleWindowShowMonitor].GetField('worker',$instanceFlags).GetValue($interrupted)
        if (-not $posted -or -not $worker.Join(5000)) { throw 'The isolated observer interruption could not be delivered.' }
        if ($interrupted.Healthy -or $interrupted.ObservationActive) { throw 'Unexpected event-loop exit left the observer healthy or active.' }
        if (Test-CrestHiddenWindowQualification 0 0 $interrupted.Healthy 0 121 $false $true) { throw 'An interrupted observer qualified an otherwise passing hidden-idle fixture.' }
    } finally { $interrupted.Dispose() }
    if ($interrupted.Healthy) { throw 'Disposal erased an earlier observer failure.' }
    Write-Host '3 observer lifecycle checks passed: clean/idempotent shutdown, unexpected exit, failed-observer qualification rejection.'
    return
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
$showMonitor = [CrestIdleWindowShowMonitor]::new($CrestProcessId)
$clock = [Diagnostics.Stopwatch]::StartNew()
$startedUtc = [DateTimeOffset]::UtcNow
$samples = [Collections.Generic.List[object]]::new()
$exitedEarly = $false
try {
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
        WindowEnumerationSucceeded=($counts[2] -eq 1)
        ForegroundClass=[CrestIdleNative]::ForegroundClass()
    })
    if ($clock.Elapsed.TotalSeconds -ge $DurationSeconds) { break }
    Start-Sleep -Seconds ([Math]::Min($SampleSeconds,[Math]::Max(1,$DurationSeconds-$clock.Elapsed.TotalSeconds)))
}
$clock.Stop()
} finally { $showMonitor.Dispose() }
$windowShowEvents = $showMonitor.ShowEvents
$windowShowObservationHealthy = $showMonitor.Healthy
$elapsed = $clock.Elapsed.TotalSeconds
$cpuEnd = if ($samples.Count -gt 0) { $samples[$samples.Count-1].ProcessCpuSeconds } else { $cpuStart }
$measurementElapsed = if ($samples.Count -gt 1) { $samples[$samples.Count-1].ElapsedSeconds } else { $elapsed }
$visibleSamples = @($samples | Where-Object {$_.VisibleWindowCount -gt 0}).Count
$enumerationFailures = @($samples | Where-Object {-not $_.WindowEnumerationSucceeded}).Count
$candidateUnchanged = (Test-Path -LiteralPath $candidateDll -PathType Leaf) -and (Get-FileHash -LiteralPath $candidateDll -Algorithm SHA256).Hash -eq $candidateDllHash
$validCandidateSample = $samples.Count -gt 0 -and -not $exitedEarly -and $candidateUnchanged
$allHidden = Test-CrestHiddenWindowQualification $visibleSamples $windowShowEvents $windowShowObservationHealthy $enumerationFailures $samples.Count $exitedEarly $candidateUnchanged
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
    ObservedTopLevelWindowShowEvents=$windowShowEvents
    WindowShowObservationHealthy=$windowShowObservationHealthy
    WindowEnumerationFailures=$enumerationFailures
    MeanTotalDeviceCpuPercent=[Math]::Round($cpuPercent,4)
    MeanWorkingSetMiB=[Math]::Round((($samples | Measure-Object WorkingSetBytes -Average).Average/1MB),2)
    MaximumWorkingSetMiB=[Math]::Round((($samples | Measure-Object WorkingSetBytes -Maximum).Maximum/1MB),2)
    MaximumWorkingSetBytes=($samples | Measure-Object WorkingSetBytes -Maximum).Maximum
    CpuBudgetBelowOnePercent=($allHidden -and $measurementElapsed -ge 600 -and $cpuPercent -lt 1)
    WorkingSetBudgetAtMost200MB=($validCandidateSample -and (($samples | Measure-Object WorkingSetBytes -Maximum).Maximum -le 200000000))
    Limitations='Sampled HWND visibility and passive top-level window-show events cannot establish presentation timing, input interception, foreground-suppression percentiles or absence of hidden render work. CPU is normalized to all logical processors. Any visible sample, observed top-level window-show event, failed enumeration or failed show-event hook invalidates hidden-idle qualification. Early exit, absent samples or changed candidate DLL invalidate both resource results. A shorter run cannot satisfy the ten-minute CPU gate. Resident memory is an independent warm-core budget and can qualify while visible; it uses decimal 200 MB, with MiB fields as display conversions. An absent source fingerprint means only the recorded DLL is bound.'
    Samples=@($samples)
}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$result | Select-Object StartedUtc,CandidateDllSHA256,ActualDurationSeconds,SampleCount,AllSampledWindowsHidden,MeanTotalDeviceCpuPercent,MeanWorkingSetMiB,MaximumWorkingSetMiB,CpuBudgetBelowOnePercent,WorkingSetBudgetAtMost200MB | Format-List

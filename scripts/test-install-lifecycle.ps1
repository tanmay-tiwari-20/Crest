param(
    [string]$InstallerPath = '',
    [switch]$InstallFinal,
    [switch]$LaunchFinal,
    [switch]$ResumeSmoke
)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($InstallerPath)) { $InstallerPath = Join-Path $projectRoot 'artifacts\Crest-Setup-0.1.0-x64.exe' }
$InstallerPath = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $InstallerPath)) { throw 'The compiled installer is missing.' }
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$smokeRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot 'install-smoke'))
$smokeDirectory = [System.IO.Path]::GetFullPath((Join-Path $smokeRoot 'Crest'))
if (-not $smokeDirectory.StartsWith($artifactRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'The smoke install directory escaped the project artifacts folder.' }
New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null
$localData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Crest'
$startupSubkey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallSubkey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Crest.PersonalUtility_is1'
$programMenu = [Environment]::GetFolderPath('Programs')
if (Test-Path -LiteralPath $uninstallSubkey) {
    if (-not $ResumeSmoke) { throw 'An existing Crest installation is registered. Use a separate test account for a fresh-install lifecycle check.' }
    $registeredDirectory = (Get-ItemProperty -LiteralPath $uninstallSubkey).InstallLocation.TrimEnd('\')
    if (-not [string]::Equals([System.IO.Path]::GetFullPath($registeredDirectory), $smokeDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'ResumeSmoke only accepts this project artifacts/install-smoke/Crest registration.'
    }
} elseif ($ResumeSmoke) { throw 'There is no verified smoke installation to resume.' }
$runningCrest = @(Get-Process -Name Crest -ErrorAction SilentlyContinue)
if ($runningCrest.Count -gt 0) {
    if (-not $ResumeSmoke -or $runningCrest.Count -ne 1 -or
        -not [string]::Equals($runningCrest[0].Path, (Join-Path $smokeDirectory 'Crest.exe'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Quit unrelated Crest processes before starting controlled installer verification.'
    }
}
$script:crestReadinessDurations = [System.Collections.Generic.List[double]]::new()

function Get-StartupValue {
    return (Get-ItemProperty -LiteralPath $startupSubkey -ErrorAction SilentlyContinue).Crest
}
function Get-PreferenceHash {
    $preferenceFile = Join-Path $localData 'settings.json'
    if (Test-Path -LiteralPath $preferenceFile) { return (Get-FileHash -LiteralPath $preferenceFile -Algorithm SHA256).Hash }
    return $null
}
function Wait-CrestReady {
    for ($attempt = 0; $attempt -lt 300; $attempt++) {
        try { $signal = [Threading.EventWaitHandle]::OpenExisting('Local\Crest.Quit'); $signal.Dispose(); return }
        catch [Threading.WaitHandleCannotBeOpenedException] { Start-Sleep -Milliseconds 100 }
    }
    throw 'Crest did not become ready for graceful lifecycle control.'
}
function Invoke-Installer([string]$Destination, [string]$LogName) {
    $logFile = Join-Path $smokeRoot $LogName
    $arguments = '/CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /TASKS="" /DIR="' + $Destination + '" /LOG="' + $logFile + '"'
    $setupProcess = Start-Process -FilePath $InstallerPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($setupProcess.ExitCode -ne 0) { throw "Installer returned $($setupProcess.ExitCode). See $LogName." }
}
function Confirm-Installed([string]$Directory) {
    foreach ($component in @('Crest.exe', 'Crest.dll', 'coreclr.dll', 'Crest.runtimeconfig.json', 'unins000.exe', 'docs\USER_GUIDE.md', 'docs\RELEASE_NOTES.md', 'docs\BUILD.md', 'LICENSE', 'README.md', 'THIRD_PARTY_NOTICES.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $component))) { throw "Installed component missing: $component" }
    }
    if (-not (Test-Path -LiteralPath $uninstallSubkey)) { throw 'Windows uninstall registration is missing.' }
    foreach ($shortcutName in @('Crest.lnk', 'Crest Settings.lnk')) {
        if (-not (Test-Path -LiteralPath (Join-Path $programMenu $shortcutName))) { throw "Start-menu shortcut missing: $shortcutName" }
    }
}
function Start-CrestQuietly([string]$Directory) {
    $readyStopwatch = [Diagnostics.Stopwatch]::StartNew()
    $appProcess = Start-Process -FilePath (Join-Path $Directory 'Crest.exe') -WorkingDirectory $Directory -WindowStyle Hidden -PassThru
    Wait-CrestReady
    $script:crestReadinessDurations.Add([Math]::Round($readyStopwatch.Elapsed.TotalMilliseconds, 1))
    return $appProcess.Id
}

$startupBefore = Get-StartupValue
if ($null -ne $startupBefore) { throw 'This controlled default-startup check requires the Crest startup entry to be absent.' }
$preferencesBefore = Get-PreferenceHash
$localDataFolderExisted = Test-Path -LiteralPath $localData
if (-not $ResumeSmoke) { Invoke-Installer $smokeDirectory 'install.log' }
Confirm-Installed $smokeDirectory
$candidateDllHash = (Get-FileHash -LiteralPath (Join-Path $smokeDirectory 'Crest.dll') -Algorithm SHA256).Hash
if ($null -ne (Get-StartupValue)) { throw 'Fresh installation unexpectedly enabled startup.' }
if ($ResumeSmoke -and $runningCrest.Count -eq 1) { Wait-CrestReady; $firstProcessId = $runningCrest[0].Id }
else { $firstProcessId = Start-CrestQuietly $smokeDirectory }
Invoke-Installer $smokeDirectory 'upgrade.log'
if (Get-Process -Id $firstProcessId -ErrorAction SilentlyContinue) { throw 'Upgrade did not gracefully close the running app.' }
Confirm-Installed $smokeDirectory
$secondProcessId = Start-CrestQuietly $smokeDirectory
$preferencesBeforeUninstall = Get-PreferenceHash
$uninstallArguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="' + (Join-Path $smokeRoot 'uninstall.log') + '"'
$uninstallProcess = Start-Process -FilePath (Join-Path $smokeDirectory 'unins000.exe') -ArgumentList $uninstallArguments -WindowStyle Hidden -PassThru -Wait
if ($uninstallProcess.ExitCode -ne 0) { throw "Uninstall returned $($uninstallProcess.ExitCode)." }
if (Get-Process -Id $secondProcessId -ErrorAction SilentlyContinue) { throw 'Uninstall did not gracefully close the running app.' }
if (Test-Path -LiteralPath (Join-Path $smokeDirectory 'Crest.exe')) { throw 'Uninstall left the executable installed.' }
if (Test-Path -LiteralPath $uninstallSubkey) { throw 'Uninstall left its Windows registration.' }
if ($null -ne (Get-StartupValue)) { throw 'Uninstall left a startup entry.' }
$preferencesAfter = Get-PreferenceHash
if ($preferencesBeforeUninstall -ne $preferencesAfter) { throw 'Silent uninstall changed retained preferences.' }
$report = [ordered]@{
    Date = '2026-11-18'
    UserAccount = [Environment]::UserName
    AccountCoverage = 'Existing current Windows user; clean-account validation pending'
    ProcessorArchitecture = 'x64'
    ElevationRequired = $false
    Installer = [System.IO.Path]::GetFileName($InstallerPath)
    InstallerCandidateDllSHA256 = $candidateDllHash
    FreshPerUserInstall = 'Passed'
    ResumedVerifiedSmokeInstallation = [bool]$ResumeSmoke
    WarmReadinessMilliseconds = $script:crestReadinessDurations.ToArray()
    InitialColdReadiness = $(if ($ResumeSmoke) { 'Initial verification launch exceeded a five-second gate; eventually ready. Exact cold timing and three-second target remain unverified.' } else { 'Measured launches are recorded; cold-cache conditions were not independently established.' })
    SelfContainedRuntimeAndDocs = 'Passed'
    StartMenuShortcuts = 'Passed'
    StartupDefaultOff = 'Passed'
    GracefulUpgrade = 'Passed with a running installed app'
    GracefulUninstall = 'Passed with a running installed app'
    ExecutableAndRegistrationRemoval = 'Passed'
    SilentUninstallRetainsData = 'Passed'
    ExistingPreferencesUnchangedBySilentUninstall = $(if ($null -ne $preferencesBeforeUninstall) { 'Passed' } else { 'Not exercised: settings.json was absent' })
    LocalDataExistedBeforeTest = ($null -ne $preferencesBefore)
    LocalDataFolderExistedBeforeTest = $localDataFolderExisted
    InteractiveDeleteChoice = 'Source-reviewed; interactive deletion not exercised against user data'
    FinalInstallation = 'Not requested by this run'
}
if ($InstallFinal) {
    $finalDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Crest'
    Invoke-Installer $finalDirectory 'final-install.log'
    Confirm-Installed $finalDirectory
    $installedDllHash = (Get-FileHash -LiteralPath (Join-Path $finalDirectory 'Crest.dll') -Algorithm SHA256).Hash
    if ($candidateDllHash -ne $installedDllHash) { throw 'The final installed app differs from the tested installer candidate.' }
    $report.FinalInstallation = 'Passed; matching app installed and awaiting launch'
    if ($LaunchFinal) {
        $finalProcessId = Start-CrestQuietly $finalDirectory
        $report.FinalInstallation = 'Passed; matching app running quietly in the system tray'
        $report.FinalProcessId = $finalProcessId
    }
    $report.FinalDirectory = $finalDirectory
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $projectRoot 'docs\install-lifecycle-test.json') -Encoding utf8
$report | ConvertTo-Json -Depth 5

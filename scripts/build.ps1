param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$Package,
    [switch]$UpdateLockFiles,
    [switch]$SkipNativeClipboardVerification,
    [string]$ArtifactRoot = ''
)
. (Join-Path $PSScriptRoot 'release-common.ps1')
if ($env:OS -ne 'Windows_NT') { throw 'Crest must be built on Windows.' }
if ($Package -and ($Configuration -ne 'Release' -or $SkipTests)) { throw 'Packages require Release configuration and passing tests.' }
$metadata = Get-CrestBuildMetadata
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) { $ArtifactRoot = Join-Path $script:CrestRoot 'artifacts' }
$ArtifactRoot = Resolve-CrestArtifactPath ([IO.Path]::GetFullPath($ArtifactRoot)) -AllowRoot
if ($SkipTests -and $SkipNativeClipboardVerification) { throw 'Clipboard omission must accompany the remaining outcome tests, not -SkipTests.' }
$dotnetExe = Get-CrestDotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$sdkVersion = & $dotnetExe --version
if ($LASTEXITCODE -ne 0 -or $sdkVersion.Trim() -ne $metadata.Sdk) { throw "Building Crest requires pinned .NET SDK $($metadata.Sdk)." }
$appProject = Join-Path $script:CrestRoot 'src\Crest\Crest.csproj'
$testProject = Join-Path $script:CrestRoot 'tests\Crest.Tests\Crest.Tests.csproj'
$uiProbeProject = Join-Path $script:CrestRoot 'tests\Crest.UIProbe\Crest.UIProbe.csproj'
$platformProbeProject = Join-Path $script:CrestRoot 'tests\Crest.PlatformProbe\Crest.PlatformProbe.csproj'
$commonProperties = @('-p:Deterministic=true', '-p:ContinuousIntegrationBuild=true', '-p:IncludeSourceRevisionInInformationalVersion=false', '-p:TreatWarningsAsErrors=true', '-p:DebugType=None', '-p:DebugSymbols=false', "-p:PathMap=$($script:CrestRoot)=/_/crest", '-nr:false', '-maxcpucount:2')
$restoreMode = if ($UpdateLockFiles) { '--force-evaluate' } else { '--locked-mode' }
# Restore the app last: the UI probe references it, and its restore must not
# replace the final self-contained/ReadyToRun runtime assets.
foreach ($project in @($testProject, $uiProbeProject, $platformProbeProject, $appProject)) {
    if (-not $UpdateLockFiles -and -not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $project) 'packages.lock.json'))) {
        throw 'Committed NuGet lockfiles are required. Use -UpdateLockFiles explicitly when reviewing dependency updates.'
    }
    $restoreArguments = @('restore', $project, '--use-lock-file', $restoreMode, '--source', 'https://api.nuget.org/v3/index.json', '--nologo', '-p:NuGetAudit=true')
    if ($project -eq $appProject) { $restoreArguments += @('--runtime', $metadata.Rid, '-p:SelfContained=true', '-p:PublishReadyToRun=true') }
    & $dotnetExe @restoreArguments
    if ($LASTEXITCODE -ne 0) { throw "Locked dependency restore failed: $project" }
}
$sourceFingerprint = Get-CrestSourceFingerprint
& $dotnetExe build $appProject --configuration $Configuration --runtime $metadata.Rid --self-contained true --no-restore --nologo @commonProperties -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { throw 'Crest did not compile.' }
# ReadyToRun rewrites the published DLL. Publish before UI verification so the
# tests exercise the final artifact rather than its pre-publication IL assembly.
$portableDir = Reset-CrestArtifactDirectory (Join-Path $ArtifactRoot 'portable\Crest')
& $dotnetExe publish $appProject --configuration $Configuration --runtime $metadata.Rid --self-contained true --no-build --no-restore --output $portableDir --nologo @commonProperties -p:PublishSingleFile=false -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publication failed.' }
Assert-CrestDistribution $portableDir $metadata
if (-not $SkipTests) {
    & $dotnetExe build $testProject --configuration $Configuration --no-restore --nologo @commonProperties
    if ($LASTEXITCODE -ne 0) { throw 'Crest verification did not compile.' }
    $domainOutput = & $dotnetExe run --project $testProject --configuration $Configuration --no-build --no-restore --no-launch-profile
    $domainExit = $LASTEXITCODE
    $domainOutput | ForEach-Object { Write-Host $_ }
    if ($domainExit -ne 0) { throw 'Crest verification failed. No package was produced.' }
    $domainSummary = @($domainOutput | Select-String '^PASS: (\d+) outcome tests$')
    if ($domainSummary.Count -ne 1) { throw 'Domain verification did not report one complete outcome summary.' }
    New-Item -ItemType Directory -Path $ArtifactRoot -Force | Out-Null
    Write-CrestUtf8 (Join-Path $ArtifactRoot 'domain-outcomes.json') (([ordered]@{ SchemaVersion = 1; Passed = [int]$domainSummary[0].Matches[0].Groups[1].Value; SourceFingerprint = $sourceFingerprint } | ConvertTo-Json) + "`n")
    & $dotnetExe build $uiProbeProject --configuration $Configuration --no-restore --nologo @commonProperties
    if ($LASTEXITCODE -ne 0) { throw 'Crest productivity UI verification did not compile.' }
    $uiRunner = Reset-CrestArtifactDirectory (Join-Path $ArtifactRoot 'ui-runner')
    $uiBin = Join-Path $script:CrestRoot "tests\Crest.UIProbe\bin\$Configuration\$($metadata.Framework)"
    Get-ChildItem -LiteralPath $uiBin -Force | Copy-Item -Destination $uiRunner -Recurse -Force
    $candidateDll = Join-Path $portableDir 'Crest.dll'
    Copy-Item -LiteralPath $candidateDll -Destination (Join-Path $uiRunner 'Crest.dll') -Force
    $uiArguments = @((Join-Path $ArtifactRoot 'productivity-qa'))
    if ($SkipNativeClipboardVerification) { $uiArguments += '--skip-native-clipboard' }
    & $dotnetExe (Join-Path $uiRunner 'Crest.UIProbe.dll') @uiArguments
    if ($LASTEXITCODE -ne 0) { throw 'Crest productivity UI verification failed. No package was produced.' }
    $uiRecord = Get-Content -LiteralPath (Join-Path $ArtifactRoot 'productivity-qa\ui-outcomes.json') -Raw | ConvertFrom-Json
    if ($uiRecord.assemblySha256 -ne (Get-FileHash -LiteralPath $candidateDll -Algorithm SHA256).Hash) { throw 'UI outcomes did not run against the exact self-contained candidate DLL.' }
    & (Join-Path $PSScriptRoot 'test-release-verification.ps1') -ArtifactRoot $ArtifactRoot
    & (Join-Path $PSScriptRoot 'test-gate-verification.ps1') -ArtifactRoot $ArtifactRoot
    & (Join-Path $PSScriptRoot 'verify-release-docs.ps1') -ArtifactRoot $ArtifactRoot
    & $dotnetExe build $platformProbeProject --configuration $Configuration --no-restore --nologo @commonProperties -p:BuildProjectReferences=false
    if ($LASTEXITCODE -ne 0) { throw 'Native diagnostic verification did not compile.' }
    & $dotnetExe run --project $platformProbeProject --configuration $Configuration --no-build --no-restore --no-launch-profile -- --geometry-self-test
    if ($LASTEXITCODE -ne 0) { throw 'Native diagnostic geometry verification failed.' }
    & (Join-Path $PSScriptRoot 'Measure-HiddenIdle.ps1') -ObserverSelfTest
}
Assert-CrestDistribution $portableDir $metadata
$skippedChecks = @()
if ($SkipNativeClipboardVerification) { $skippedChecks = @('Native clipboard listener and write verification (11 outcomes)') }
Write-CrestBuildVerification $portableDir $metadata $Configuration (-not $SkipTests) $sourceFingerprint $skippedChecks
Write-Host "Validated self-contained Windows x64 application: $portableDir"
if ($Package) { & (Join-Path $PSScriptRoot 'package.ps1') -SourceDir $portableDir -ArtifactRoot $ArtifactRoot | Out-Host }

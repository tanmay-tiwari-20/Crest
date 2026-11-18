param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetExe = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sdkVersion = & $dotnetExe --version
if ($LASTEXITCODE -ne 0 -or [version]($sdkVersion.Split('-')[0]) -lt [version]'10.0.0') {
    throw 'Building Crest requires the .NET 10 SDK. The packaged application includes its runtime and needs no SDK.'
}
$appProject = Join-Path $projectRoot 'src\Crest\Crest.csproj'
$testProject = Join-Path $projectRoot 'tests\Crest.Tests\Crest.Tests.csproj'
$portableDir = Join-Path $projectRoot 'artifacts\portable\Crest'
& $dotnetExe restore $appProject --runtime win-x64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
& $dotnetExe build $appProject --configuration $Configuration --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Crest did not compile.' }
if (-not $SkipTests) {
    & $dotnetExe run --project $testProject --configuration $Configuration --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw 'Crest verification failed. No package was produced.' }
}
& $dotnetExe publish $appProject --configuration $Configuration --runtime win-x64 --self-contained true --output $portableDir --nologo -p:PublishSingleFile=false -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publication failed.' }
Write-Output "Self-contained Windows x64 application: $portableDir"
if ($Package) { & (Join-Path $PSScriptRoot 'package.ps1') -SourceDir $portableDir }

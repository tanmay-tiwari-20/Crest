param([string]$SourceDir = '')
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($SourceDir)) { $SourceDir = Join-Path $projectRoot 'artifacts\portable\Crest' }
$SourceDir = [System.IO.Path]::GetFullPath($SourceDir)
if (-not (Test-Path -LiteralPath (Join-Path $SourceDir 'Crest.exe'))) { throw 'Publish Crest first with scripts/build.ps1.' }
$runtimeConfigPath = Join-Path $SourceDir 'Crest.runtimeconfig.json'
if (-not (Test-Path -LiteralPath $runtimeConfigPath) -or
    -not (Test-Path -LiteralPath (Join-Path $SourceDir 'coreclr.dll')) -or
    -not (Test-Path -LiteralPath (Join-Path $SourceDir 'hostpolicy.dll'))) {
    throw 'Packaging requires the self-contained publish folder, including its .NET runtime.'
}
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
$includedFrameworks = @($runtimeConfig.runtimeOptions.includedFrameworks | ForEach-Object { $_.name })
if ($includedFrameworks -notcontains 'Microsoft.NETCore.App' -or $includedFrameworks -notcontains 'Microsoft.WindowsDesktop.App') {
    throw 'The publish configuration does not include both required runtimes. Run scripts/build.ps1 before packaging.'
}
$depsPath = Join-Path $SourceDir 'Crest.deps.json'
if (-not (Test-Path -LiteralPath $depsPath) -or
    ((Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json).runtimeTarget.name -notmatch '/win-x64$')) {
    throw 'This installer requires a Windows x64 publish. Run scripts/build.ps1 before packaging.'
}
$guideSourceDirectory = Join-Path $projectRoot 'docs'
$publishedDocDirectory = Join-Path $SourceDir 'docs'
if (Test-Path -LiteralPath $guideSourceDirectory) {
    New-Item -ItemType Directory -Path $publishedDocDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $guideSourceDirectory | Copy-Item -Destination $publishedDocDirectory -Recurse -Force
}
$dependencyDir = Join-Path $SourceDir 'licenses'
New-Item -ItemType Directory -Path $dependencyDir -Force | Out-Null
foreach ($noticeName in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
    $sdkNotice = Join-Path $projectRoot ('.tools\dotnet\' + $noticeName)
    if (Test-Path -LiteralPath $sdkNotice) { Copy-Item -LiteralPath $sdkNotice -Destination (Join-Path $dependencyDir ('dotnet-' + $noticeName)) -Force }
}
$assetsFile = Join-Path $projectRoot 'src\Crest\obj\project.assets.json'
if (Test-Path -LiteralPath $assetsFile) {
    $assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json
    $packages = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object {
        $identity = $_.Name.Split('/')
        [ordered]@{ Name = $identity[0]; Version = $identity[1] }
    })
    [ordered]@{ Application = 'Crest'; Version = '0.1.0'; Architecture = 'win-x64'; Framework = 'net10.0-windows10.0.19041.0'; Distribution = 'Self-contained'; Packages = $packages } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $dependencyDir 'dependency-record.json') -Encoding utf8
}
$builderDir = Join-Path $projectRoot '.tools\innosetup'
$compiler = Join-Path $builderDir 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $systemCompiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $systemCompiler) { $compiler = $systemCompiler.Source }
    else {
        $downloadDir = Join-Path $projectRoot '.tools\inno-download'
        New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null
        $builderArchive = Join-Path $downloadDir 'innosetup.exe'
        Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe' -OutFile $builderArchive
        $signature = Get-AuthenticodeSignature -LiteralPath $builderArchive
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys') {
            throw 'The official Inno Setup compiler download failed publisher verification.'
        }
        # Official portable mode extracts the compiler without registration, associations, shortcuts, or an uninstaller.
        $arguments = '/PORTABLE=1 /CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /TASKS="" /DIR="' + $builderDir + '"'
        $builderProcess = Start-Process -FilePath $builderArchive -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
        if ($builderProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw 'The portable installer compiler could not be extracted.' }
    }
}
$outputDir = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
& $compiler '/Qp' "/DSourceDir=$SourceDir" "/DOutputDir=$outputDir" (Join-Path $projectRoot 'installer\Crest.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installerPath = Join-Path $outputDir 'Crest-Setup-0.1.0-x64.exe'
$checksum = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
($checksum.Hash + '  ' + [System.IO.Path]::GetFileName($installerPath)) | Set-Content -LiteralPath (Join-Path $outputDir 'Crest-Setup-0.1.0-x64.sha256') -Encoding ascii
Write-Output "Installer: $installerPath"

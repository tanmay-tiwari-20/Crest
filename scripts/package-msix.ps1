[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AppDirectory,
    [string]$Publisher = 'CN=Crest',
    [string]$Version = '',
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$')]
    [string]$PackageName = 'Crest.Desktop',
    [string]$SdkBin = '',
    [string]$CertificateThumbprint = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $PSScriptRoot '..\artifacts' }

# Package an existing self-contained candidate. This script never builds, installs,
# creates/trusts a certificate, or removes a prior package or acceptance artifact.
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'MSIX packaging requires Windows and the Windows SDK packaging tools.'
}
$appRoot = (Resolve-Path -LiteralPath $AppDirectory).Path.TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not [IO.Directory]::Exists($appRoot)) { throw 'AppDirectory must be an existing self-contained Crest directory.' }
foreach ($name in @('Crest.exe', 'Crest.dll', 'Crest.deps.json', 'Crest.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll')) {
    if (-not [IO.File]::Exists((Join-Path $appRoot $name))) {
        throw "The candidate is not a complete self-contained x64 Crest distribution: missing $name."
    }
}
$executableStream = [IO.File]::OpenRead((Join-Path $appRoot 'Crest.exe'))
$executableReader = [IO.BinaryReader]::new($executableStream)
try {
    $executableStream.Position = 0x3c
    $headerPosition = $executableReader.ReadInt32()
    if ($headerPosition -lt 0 -or $headerPosition + 6 -gt $executableStream.Length) { throw 'Crest.exe has an invalid executable header.' }
    $executableStream.Position = $headerPosition
    if ($executableReader.ReadUInt32() -ne 0x00004550 -or $executableReader.ReadUInt16() -ne 0x8664) {
        throw 'AppDirectory must contain the x64 Crest executable.'
    }
}
finally { $executableReader.Dispose(); $executableStream.Dispose() }
if ([string]::IsNullOrWhiteSpace($Publisher)) { throw 'Publisher must be the exact certificate subject, for example CN=Crest.' }
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $appRoot 'Crest.dll')).FileVersion
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Version must have four numeric parts, such as 0.3.0.0.' }
$parts = $Version.Split('.') | ForEach-Object { [long]$_ }
if (@($parts | Where-Object { $_ -lt 0 -or $_ -gt 65535 }).Count -gt 0 -or $parts[0] -eq 0 -and $parts[1] -eq 0 -and $parts[2] -eq 0 -and $parts[3] -eq 0) {
    throw 'MSIX version components must be between 0 and 65535, and the version must be nonzero.'
}

function Find-SdkTool([string]$Name) {
    if (-not [string]::IsNullOrWhiteSpace($SdkBin)) {
        $resolvedBin = (Resolve-Path -LiteralPath $SdkBin).Path
        $explicit = Join-Path $resolvedBin $Name
        if ([IO.File]::Exists($explicit)) { return $explicit }
        throw "Windows SDK tool $Name was not found in SdkBin. Pass the SDK's x64 bin directory."
    }
    $onPath = Get-Command -Name $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $onPath) { return $onPath.Source }
    $kits = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits\10\bin'
    if ([IO.Directory]::Exists($kits)) {
        $versions = Get-ChildItem -LiteralPath $kits -Directory | Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending
        foreach ($kit in $versions) {
            $candidate = Join-Path $kit.FullName ('x64\' + $Name)
            if ([IO.File]::Exists($candidate)) { return $candidate }
        }
    }
    throw "Windows SDK $Name was not found. Install Windows 11 SDK packaging tools or pass -SdkBin with the installed SDK's x64 bin directory. No package was created."
}

$makeAppx = Find-SdkTool 'MakeAppx.exe'
$signTool = $null
if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $CertificateThumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
    if ($CertificateThumbprint -notmatch '^[A-F0-9]{40}$') { throw 'CertificateThumbprint must be a 40-character SHA-1 certificate thumbprint.' }
    $signTool = Find-SdkTool 'SignTool.exe'
    $certificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $CertificateThumbprint) -ErrorAction SilentlyContinue
    if ($null -eq $certificate -or -not $certificate.HasPrivateKey) {
        throw 'The requested signing certificate with a private key must already exist in CurrentUser\My. No certificate is created or trusted by this script.'
    }
    if (-not [string]::Equals($certificate.Subject, $Publisher, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publisher must exactly match the subject of the requested signing certificate.'
    }
    if ($certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) { throw 'The requested signing certificate is not currently valid.' }
}

$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ([string]::Equals($resolvedOutputRoot, $appRoot, [StringComparison]::OrdinalIgnoreCase) -or $resolvedOutputRoot.StartsWith($appRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputRoot must be outside AppDirectory so packaging cannot recursively copy its own output.'
}
$runDirectory = Join-Path $resolvedOutputRoot ('msix-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$stage = Join-Path $runDirectory 'stage'
[IO.Directory]::CreateDirectory($stage) | Out-Null
Get-ChildItem -LiteralPath $appRoot -Force | Copy-Item -Destination $stage -Recurse -Force

$manifestTemplate = Join-Path $PSScriptRoot '..\installer\AppxManifest.xml'
[xml]$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$namespaces = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaces.AddNamespace('m', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$namespaces.AddNamespace('uap3', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/3')
$identity = $manifest.SelectSingleNode('/m:Package/m:Identity', $namespaces)
if ($null -eq $identity -or $null -eq $manifest.SelectSingleNode('/m:Package/m:Capabilities/uap3:Capability[@Name="userNotificationListener"]', $namespaces)) {
    throw 'The manifest template is missing its identity or notification-listener capability.'
}
$identity.SetAttribute('Publisher', $Publisher)
$identity.SetAttribute('Version', $Version)
$identity.SetAttribute('Name', $PackageName)
$settings = [Xml.XmlWriterSettings]::new()
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$settings.Indent = $true
$writer = [Xml.XmlWriter]::Create((Join-Path $stage 'AppxManifest.xml'), $settings)
try { $manifest.Save($writer) } finally { $writer.Dispose() }

Add-Type -AssemblyName System.Drawing
$assets = Join-Path $stage 'Assets'
[IO.Directory]::CreateDirectory($assets) | Out-Null
$sourceImage = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot '..\assets\crest.png'))
try {
    foreach ($logo in @(@('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150), @('StoreLogo.png', 50))) {
        $size = [int]$logo[1]
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $ratio = [Math]::Min($size / $sourceImage.Width, $size / $sourceImage.Height)
            $width = [int][Math]::Round($sourceImage.Width * $ratio)
            $height = [int][Math]::Round($sourceImage.Height * $ratio)
            $graphics.DrawImage($sourceImage, [int](($size - $width) / 2), [int](($size - $height) / 2), $width, $height)
            $bitmap.Save((Join-Path $assets ([string]$logo[0])), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $sourceImage.Dispose() }

$package = Join-Path $runDirectory ($PackageName + '-' + $Version + '-x64.msix')
& $makeAppx pack /d $stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx validation or packing failed. Staged files are preserved in $runDirectory." }
if ($null -ne $signTool) {
    & $signTool sign /fd SHA256 /sha1 $CertificateThumbprint $package
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed. The unsigned package and staged files are preserved in $runDirectory." }
    & $signTool verify /pa /v $package
    if ($LASTEXITCODE -ne 0) { throw "Signature verification failed. Check the existing certificate trust and publisher; files are preserved in $runDirectory." }
}
$checksum = Get-FileHash -LiteralPath $package -Algorithm SHA256
[IO.File]::WriteAllText($package + '.sha256', $checksum.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($package) + [Environment]::NewLine)
Write-Output "Created $package"
if ($null -eq $signTool) { Write-Output 'The package is unsigned. A trusted, matching publisher signature is required before normal Windows installation.' }
Write-Output 'Packaging declares optional capabilities; it does not establish notification access compatibility or grant permission. No package or certificate was installed.'

# Shared, read-only validation and guarded artifact helpers.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:CrestRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Get-CrestBuildMetadata {
    [xml]$project = Get-Content -LiteralPath (Join-Path $script:CrestRoot 'src\Crest\Crest.csproj') -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    $framework = [string]$project.Project.PropertyGroup.TargetFramework
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Crest Version must be a three-part release version in Crest.csproj.' }
    if ($framework -ne 'net10.0-windows10.0.19041.0') { throw 'The release pipeline requires the reviewed Windows .NET 10 target.' }
    $sdkConfig = Get-Content -LiteralPath (Join-Path $script:CrestRoot 'global.json') -Raw | ConvertFrom-Json
    if ($sdkConfig.sdk.version -ne '10.0.404' -or $sdkConfig.sdk.rollForward -ne 'disable' -or $sdkConfig.sdk.allowPrerelease) {
        throw 'global.json must pin reviewed SDK 10.0.404 without roll-forward or prereleases.'
    }
    return [pscustomobject]@{ Version = $version; AssemblyVersion = "$version.0"; Framework = $framework; Sdk = '10.0.404'; Runtime = '10.0.15'; Rid = 'win-x64' }
}

function Get-CrestDotnet {
    $localSdk = Join-Path $script:CrestRoot '.tools\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $localSdk) { return $localSdk }
    return (Get-Command dotnet -ErrorAction Stop).Source
}

function Assert-CrestTag([string]$Tag, $Metadata) {
    if (-not [string]::IsNullOrWhiteSpace($Tag) -and $Tag -cne "v$($Metadata.Version)") {
        throw "Release tag '$Tag' does not match project version v$($Metadata.Version)."
    }
}

function Resolve-CrestArtifactPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $artifactRoot = [IO.Path]::GetFullPath((Join-Path $script:CrestRoot 'artifacts'))
    if (-not $resolved.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Artifact target must be inside this repository's artifacts directory: $resolved"
    }
    # Validate existing ancestors even when the requested output has not been created.
    $ancestorPath = $resolved
    while (-not [string]::IsNullOrWhiteSpace($ancestorPath)) {
        if ((Test-Path -LiteralPath $ancestorPath) -and ((Get-Item -LiteralPath $ancestorPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Artifact targets and ancestors cannot be reparse points.'
        }
        if ([string]::Equals($ancestorPath, $script:CrestRoot, [StringComparison]::OrdinalIgnoreCase)) { break }
        $ancestorPath = Split-Path -Parent $ancestorPath
    }
    return $resolved
}

function Reset-CrestArtifactDirectory([string]$Path) {
    $resolved = Resolve-CrestArtifactPath $Path
    if (Test-Path -LiteralPath $resolved) {
        foreach ($item in Get-ChildItem -LiteralPath $resolved -Recurse -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Artifact contents cannot be reparse points.' }
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
    return $resolved
}

function Assert-CrestNativeX64([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D -or $stream.Length -lt 64) { throw "Not a Windows executable: $Path" }
        $stream.Position = 0x3C
        $offset = $reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt ($stream.Length - 26)) { throw "Invalid PE header: $Path" }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x8664) { throw "Expected an x64 PE binary: $Path" }
        $stream.Position = $offset + 24
        if ($reader.ReadUInt16() -ne 0x20B) { throw "Expected PE32+ format: $Path" }
    } finally { $reader.Dispose(); $stream.Dispose() }
}

function Assert-CrestDistribution([string]$Directory, $Metadata) {
    foreach ($name in @('Crest.exe', 'Crest.dll', 'Crest.deps.json', 'Crest.runtimeconfig.json', 'coreclr.dll', 'hostpolicy.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'System.Management.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $name) -PathType Leaf)) { throw "Self-contained runtime component missing: $name" }
    }
    foreach ($name in @('Crest.exe', 'coreclr.dll', 'hostpolicy.dll', 'hostfxr.dll')) { Assert-CrestNativeX64 (Join-Path $Directory $name) }
    $config = Get-Content -LiteralPath (Join-Path $Directory 'Crest.runtimeconfig.json') -Raw | ConvertFrom-Json
    $frameworks = @($config.runtimeOptions.includedFrameworks)
    foreach ($name in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
        $matching = @($frameworks | Where-Object { $_.name -eq $name -and $_.version -eq $Metadata.Runtime })
        if ($matching.Count -ne 1) { throw "Expected included $name runtime $($Metadata.Runtime)." }
    }
    $deps = Get-Content -LiteralPath (Join-Path $Directory 'Crest.deps.json') -Raw | ConvertFrom-Json
    if ($deps.runtimeTarget.name -notmatch '/win-x64$' -or $null -eq $deps.libraries.PSObject.Properties["Crest/$($Metadata.Version)"]) {
        throw 'Dependency manifest does not match the project version and win-x64 runtime.'
    }
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $Directory 'Crest.dll')).Version.ToString()
    if ($assemblyVersion -ne $Metadata.AssemblyVersion) { throw "Assembly version mismatch: $assemblyVersion" }
    foreach ($name in @('Crest.exe', 'Crest.dll')) {
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Directory $name))
        if ($info.FileVersion -ne $Metadata.AssemblyVersion -or $info.ProductVersion.Split('+')[0] -ne $Metadata.Version) {
            throw "Product/file version mismatch in $name. Publish from the current project before packaging."
        }
    }
}

function Write-CrestUtf8([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function Get-CrestGitHubRepository {
    $repository = $env:GITHUB_REPOSITORY
    if ([string]::IsNullOrWhiteSpace($repository)) {
        $origin = & git -C $script:CrestRoot remote get-url origin 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Packaging documentation requires a GitHub origin or GITHUB_REPOSITORY.' }
        if ($origin -match '^(?:https://(?:[^/@]+@)?github\.com/|git@github\.com:)(?<repository>[^/]+/[^/]+?)(?:\.git)?$') {
            $repository = $Matches.repository
        } else { throw 'The configured origin is not a supported GitHub repository URL.' }
    }
    if ($repository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$') { throw 'Invalid GitHub repository identity.' }
    return $repository
}

function ConvertTo-CrestReleaseTarget([string]$Target, [string]$SourcePath, [string]$Repository, [string]$Tag, [bool]$Image = $false) {
    if ($Target -match '^[A-Za-z]:') { throw 'Windows drive paths cannot be distributed as documentation links.' }
    if ([string]::IsNullOrEmpty($Target) -or $Target -match '^(?:[A-Za-z][A-Za-z0-9+.-]*:|//|#|\?)') { return $Target }
    $suffixIndex = $Target.IndexOfAny([char[]]'?#')
    $pathPart = if ($suffixIndex -lt 0) { $Target } else { $Target.Substring(0, $suffixIndex) }
    $suffix = if ($suffixIndex -lt 0) { '' } else { $Target.Substring($suffixIndex) }
    $pathPart = [regex]::Replace($pathPart, '\\([ ()\[\]{}])', '$1')
    $pathPart = [Uri]::UnescapeDataString($pathPart).Replace('/', '\')
    if ([IO.Path]::IsPathRooted($pathPart)) { throw "Documentation target must be repository-relative: $Target" }
    $targetPath = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $SourcePath) $pathPart))
    if (-not $targetPath.StartsWith($script:CrestRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals($targetPath, $script:CrestRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Documentation link escapes the repository: $Target" }
    if (-not (Test-Path -LiteralPath $targetPath)) { throw "Documentation link target is missing: $Target in $SourcePath" }
    $ancestorPath = $targetPath
    while ($ancestorPath -ne $script:CrestRoot) {
        if ((Get-Item -LiteralPath $ancestorPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Documentation target contains a reparse point: $Target" }
        $ancestorPath = Split-Path -Parent $ancestorPath
    }
    $relative = $targetPath.Substring($script:CrestRoot.Length).TrimStart('\').Replace('\', '/')
    $encodedPath = (@($relative.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/')
    $encodedTag = [Uri]::EscapeDataString($Tag)
    if ($Image) {
        if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) { throw "Image target must be a file: $Target" }
        return "https://raw.githubusercontent.com/$Repository/$encodedTag/$encodedPath$suffix"
    }
    $view = if (Test-Path -LiteralPath $targetPath -PathType Container) { 'tree' } else { 'blob' }
    return "https://github.com/$Repository/$view/$encodedTag/$encodedPath$suffix"
}

function ConvertTo-CrestReleaseMarkdown([string]$Content, [string]$SourcePath, [string]$Repository, [string]$Tag) {
    $SourcePath = [IO.Path]::GetFullPath($SourcePath)
    if (-not $SourcePath.StartsWith($script:CrestRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) { throw 'Source document must be an existing repository file.' }
    if ($Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$' -or $Tag -notmatch '^v\d+\.\d+\.\d+$') { throw 'Release documentation requires a valid GitHub repository and version tag.' }
    if ($Content.Contains([string][char]2) -or $Content.Contains([string][char]3)) { throw 'Unsupported control character in Markdown document.' }
    # Protect fenced and inline code so examples and literal Markdown remain exact.
    $protected = [Collections.Generic.List[string]]::new()
    $codePattern = '(?ms)^[ ]{0,3}(?<fence>`{3,}|~{3,})[^\r\n]*\r?\n.*?^[ ]{0,3}\k<fence>[ \t]*(?:\r?\n|$)|(?s)(?<ticks>`+).*?\k<ticks>'
    $body = [regex]::Replace($Content, $codePattern, [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        $index = $protected.Count
        $protected.Add($match.Value)
        return ([string][char]2 + 'CREST-CODE-' + $index + [char]3)
    })
    # Match destinations independently of labels, including nested badge labels.
    $linkPattern = '(?<open>\]\(\s*)(?<destination><[^>\r\n]*>|(?:\\.|[^\s()\\]|\((?:\\.|[^()\\])*\))+)(?<close>(?:\s+(?:"(?:\\.|[^"])*"|''(?:\\.|[^''])*''|\([^)]*\)))?\s*\))'
    $body = [regex]::Replace($body, $linkPattern, [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        $depth = 1
        $isImage = $false
        for ($index = $match.Index - 1; $index -ge 0; $index--) {
            if ($index -gt 0 -and $body[$index - 1] -eq '\') { continue }
            if ($body[$index] -eq ']') { $depth += 1 }
            elseif ($body[$index] -eq '[') {
                $depth -= 1
                if ($depth -eq 0) { $isImage = $index -gt 0 -and $body[$index - 1] -eq '!'; break }
            }
        }
        $destination = $match.Groups['destination'].Value
        $angled = $destination.StartsWith('<')
        $target = if ($angled) { $destination.Substring(1, $destination.Length - 2) } else { $destination }
        $url = ConvertTo-CrestReleaseTarget $target $SourcePath $Repository $Tag $isImage
        if ($url -eq $target) { return $match.Value }
        if ($angled) { $url = '<' + $url + '>' }
        return $match.Groups['open'].Value + $url + $match.Groups['close'].Value
    })
    $referencePattern = '(?m)^(?<open>[ ]{0,3}\[(?!\^)(?<label>[^\]\r\n]+)\]:[ \t]*)(?<destination><[^>\r\n]*>|\S+)(?<close>[^\r\n]*)'
    $body = [regex]::Replace($body, $referencePattern, [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        $label = [regex]::Escape($match.Groups['label'].Value)
        $isImage = $body -match ('(?i)!\[[^\]\r\n]*\][ \t]*\[' + $label + '\]|!\[' + $label + '\](?![ \t]*[\[(])')
        $destination = $match.Groups['destination'].Value
        $angled = $destination.StartsWith('<')
        $target = if ($angled) { $destination.Substring(1, $destination.Length - 2) } else { $destination }
        $url = ConvertTo-CrestReleaseTarget $target $SourcePath $Repository $Tag $isImage
        if ($url -eq $target) { return $match.Value }
        if ($angled) { $url = '<' + $url + '>' }
        return $match.Groups['open'].Value + $url + $match.Groups['close'].Value
    })
    $htmlPattern = '(?is)(?<open><[A-Za-z][^>]*?\bsrc\s*=\s*)(?<quote>["''])(?<destination>(?:(?!\k<quote>).)*)\k<quote>'
    $body = [regex]::Replace($body, $htmlPattern, [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        $target = [Net.WebUtility]::HtmlDecode($match.Groups['destination'].Value)
        $url = ConvertTo-CrestReleaseTarget $target $SourcePath $Repository $Tag $true
        if ($url -eq $target) { return $match.Value }
        return $match.Groups['open'].Value + $match.Groups['quote'].Value + [Net.WebUtility]::HtmlEncode($url) + $match.Groups['quote'].Value
    })
    return [regex]::Replace($body, ([string][char]2 + 'CREST-CODE-(?<index>\d+)' + [char]3), [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        return $protected[[int]$match.Groups['index'].Value]
    })
}

function Copy-CrestReleaseDocument([string]$SourcePath, [string]$DestinationPath, [string]$Repository, [string]$Tag) {
    $destination = Resolve-CrestArtifactPath $DestinationPath
    $content = [IO.File]::ReadAllText($SourcePath)
    $rebased = ConvertTo-CrestReleaseMarkdown $content $SourcePath $Repository $Tag
    Write-CrestUtf8 $destination $rebased
}

function Get-CrestDownloadedPackageVersion($Assets, [string]$Name) {
    $downloads = @($Assets.project.frameworks.PSObject.Properties | ForEach-Object { $_.Value.downloadDependencies } | Where-Object { $_.name -eq $Name })
    $versions = @($downloads | ForEach-Object {
        if ($_.version -notmatch '^\[(?<minimum>[^,]+),\s*(?<maximum>[^\]]+)\]$' -or $Matches.minimum.Trim() -ne $Matches.maximum.Trim()) { throw "An exact restored version is required for $Name." }
        $Matches.minimum.Trim()
    } | Select-Object -Unique)
    if ($versions.Count -ne 1) { throw "Restored framework package version missing or ambiguous: $Name" }
    return $versions[0]
}

function Get-CrestRestoredPackageDirectory($Assets, [string]$Name, [string]$Version) {
    if ($Name -notmatch '^[A-Za-z0-9_.-]+$' -or $Version -notmatch '^\d+(?:\.\d+)+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid restored package identity.' }
    foreach ($folder in $Assets.packageFolders.PSObject.Properties) {
        $candidate = Join-Path $folder.Name ($Name.ToLowerInvariant() + '\' + $Version)
        if (Test-Path -LiteralPath $candidate -PathType Container) { return $candidate }
    }
    $candidate = Join-Path (Split-Path -Parent (Get-CrestDotnet)) ('packs\' + $Name + '\' + $Version)
    if (Test-Path -LiteralPath $candidate -PathType Container) { return $candidate }
    throw "Restored package directory missing: $Name/$Version"
}

function Copy-CrestDependencyLicenses($Assets, $Metadata, [string]$LicenseDirectory) {
    $LicenseDirectory = Resolve-CrestArtifactPath $LicenseDirectory
    New-Item -ItemType Directory -Path $LicenseDirectory -Force | Out-Null
    $components = [Collections.Generic.List[object]]::new()
    foreach ($name in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
        $version = Get-CrestDownloadedPackageVersion $Assets $name
        if ($version -ne $Metadata.Runtime) { throw "Runtime license version differs from the reviewed runtime: $name/$version" }
        $sourceDirectory = Get-CrestRestoredPackageDirectory $Assets $name $version
        $sourceNames = if ($name -eq 'Microsoft.NETCore.App.Runtime.win-x64') { @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') } else { @('LICENSE') }
        $distributedNames = @($sourceNames | ForEach-Object {
            $fileName = "$name-$version-$_"
            Copy-Item -LiteralPath (Join-Path $sourceDirectory $_) -Destination (Join-Path $LicenseDirectory $fileName) -Force
            'licenses/' + $fileName
        })
        $components.Add([ordered]@{ Name = $name; Version = $version; Role = 'Runtime'; LicenseExpression = 'MIT'; NoticeFiles = $distributedNames })
    }
    $management = @($Assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' -and $_.Name -match '^System\.Management/' })
    if ($management.Count -ne 1) { throw 'A single restored System.Management package is required.' }
    $managementVersion = $management[0].Name.Split('/')[1]
    $managementDirectory = Get-CrestRestoredPackageDirectory $Assets 'System.Management' $managementVersion
    [xml]$managementSpec = Get-Content -LiteralPath (Join-Path $managementDirectory 'system.management.nuspec') -Raw
    if ($managementSpec.package.metadata.license.InnerText -ne 'MIT') { throw 'System.Management license changed and requires review.' }
    $managementNotice = "System.Management-$managementVersion-THIRD-PARTY-NOTICES.TXT"
    Copy-Item -LiteralPath (Join-Path $managementDirectory 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $LicenseDirectory $managementNotice) -Force
    $managementAttribution = "System.Management-$managementVersion-ATTRIBUTION.txt"
    $mitText = "Microsoft.NETCore.App.Runtime.win-x64-$($Metadata.Runtime)-LICENSE.TXT"
    $attribution = "System.Management $managementVersion`nLicense expression supplied by its NuGet package: MIT`n" + [string]$managementSpec.package.metadata.copyright + "`n`nThe MIT license text is included in $mitText.`nThe package's supplied third-party notices are included unchanged in $managementNotice.`n"
    Write-CrestUtf8 (Join-Path $LicenseDirectory $managementAttribution) $attribution
    $components.Add([ordered]@{ Name = 'System.Management'; Version = $managementVersion; Role = 'Runtime library'; LicenseExpression = 'MIT'; NoticeFiles = @(('licenses/' + $managementNotice), ('licenses/' + $managementAttribution), ('licenses/' + $mitText)) })
    $sdkVersion = Get-CrestDownloadedPackageVersion $Assets 'Microsoft.Windows.SDK.NET.Ref'
    $sdkDirectory = Get-CrestRestoredPackageDirectory $Assets 'Microsoft.Windows.SDK.NET.Ref' $sdkVersion
    [xml]$sdkSpec = Get-Content -LiteralPath (Join-Path $sdkDirectory 'microsoft.windows.sdk.net.ref.nuspec') -Raw
    $sourceRecord = Join-Path $script:CrestRoot 'licenses\windows-sdk-license.source.json'
    $licenseSource = Join-Path $script:CrestRoot 'licenses\windows-sdk-license.rtf'
    $provenance = Get-Content -LiteralPath $sourceRecord -Raw | ConvertFrom-Json
    if ($provenance.Component -ne 'Microsoft.Windows.SDK.NET.Ref' -or $provenance.Version -ne $sdkVersion -or
        $provenance.LicenseUrl -ne [string]$sdkSpec.package.metadata.licenseUrl -or
        $provenance.SHA256 -ne (Get-FileHash -LiteralPath $licenseSource -Algorithm SHA256).Hash) { throw 'The reviewed Windows SDK license provenance/version/hash does not match the restored package.' }
    Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $LicenseDirectory 'windows-sdk-license.rtf') -Force
    Copy-Item -LiteralPath $sourceRecord -Destination (Join-Path $LicenseDirectory 'windows-sdk-license.source.json') -Force
    $components.Add([ordered]@{ Name = 'Microsoft.Windows.SDK.NET.Ref'; Version = $sdkVersion; Role = 'Windows projections and WinRT support'; License = 'Microsoft Windows SDK license'; RuntimeAssemblies = @('Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll'); NoticeFiles = @('licenses/windows-sdk-license.rtf', 'licenses/windows-sdk-license.source.json') })
    return $components.ToArray()
}

param([string]$ArtifactRoot = '')
. (Join-Path $PSScriptRoot 'release-common.ps1')
$metadata = Get-CrestBuildMetadata
$repository = Get-CrestGitHubRepository
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) { $ArtifactRoot = Join-Path $script:CrestRoot 'artifacts' }
$ArtifactRoot = Resolve-CrestArtifactPath ([IO.Path]::GetFullPath($ArtifactRoot)) -AllowRoot
$output = Reset-CrestArtifactDirectory (Join-Path $ArtifactRoot 'documentation-qa')
$sources = @('README.md', 'CHANGELOG.md', 'CONTRIBUTING.md', 'SECURITY.md', 'THIRD_PARTY_NOTICES.md', '.github\pull_request_template.md') | ForEach-Object { Get-Item -LiteralPath (Join-Path $script:CrestRoot $_) }
$sources += @(Get-ChildItem -LiteralPath (Join-Path $script:CrestRoot 'docs') -File -Recurse -Filter '*.md')
foreach ($source in $sources) {
    $relative = $source.FullName.Substring($script:CrestRoot.Length + 1)
    $destination = Join-Path $output $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-CrestReleaseDocument $source.FullName $destination $repository "v$($metadata.Version)"
}
foreach ($name in @('README.md', 'docs\RELEASE_NOTES.md', 'docs\RELEASING.md')) {
    if ([IO.File]::ReadAllText((Join-Path $script:CrestRoot $name)) -notmatch [regex]::Escape($metadata.Version)) { throw "Current version is missing from $name." }
}
$report = [ordered]@{ SchemaVersion = 1; ApplicationVersion = $metadata.Version; Passed = $true; Documents = $sources.Count; Tag = "v$($metadata.Version)"; Checks = @('Repository-relative Markdown and HTML image links resolve without escaping the repository', 'Release document copies convert relative links to the selected GitHub tag', 'Current version appears in README, release notes and publishing instructions'); Limits = 'Prospective public release URLs and external service pages are not claimed to be published or available by this static check.' }
Write-CrestUtf8 (Join-Path $output 'results.json') (($report | ConvertTo-Json -Depth 4) + "`n")
Write-Host "$($sources.Count) GitHub release documents validated."

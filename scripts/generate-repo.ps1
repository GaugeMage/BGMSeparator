<#
    Regenerates repo.json (the Dalamud custom-repo plugin master) from the built
    plugin manifest, so AssemblyVersion / API level always match the built plugin.
    Run after a Release build:  pwsh ./scripts/generate-repo.ps1
#>
param(
    [string]$ManifestPath = "BgmSeparator/bin/x64/Release/BgmSeparator.json",
    [string]$Output = "repo.json",
    [string]$Owner = "GaugeMage",
    [string]$Repo = "BGMSeparator"
)

if (-not (Test-Path $ManifestPath)) {
    throw "Manifest not found at $ManifestPath. Build the plugin in Release first."
}

$m = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$zip = "https://github.com/$Owner/$Repo/releases/latest/download/latest.zip"

# Preserve the existing download count if we already have a repo.json.
$count = 0
if (Test-Path $Output) {
    try { $count = (Get-Content $Output -Raw | ConvertFrom-Json)[0].DownloadCount } catch { $count = 0 }
}

$entry = [ordered]@{
    Author              = $m.Author
    Name                = $m.Name
    InternalName        = $m.InternalName
    AssemblyVersion     = $m.AssemblyVersion
    Description         = $m.Description
    Punchline           = $m.Punchline
    ApplicableVersion   = $m.ApplicableVersion
    RepoUrl             = $m.RepoUrl
    Tags                = $m.Tags
    DalamudApiLevel     = $m.DalamudApiLevel
    IconUrl             = $m.IconUrl
    DownloadCount       = $count
    DownloadLinkInstall = $zip
    DownloadLinkUpdate  = $zip
    DownloadLinkTesting = $zip
}

ConvertTo-Json @($entry) -Depth 8 | Set-Content $Output -Encoding UTF8
Write-Host "Wrote $Output (version $($m.AssemblyVersion), API $($m.DalamudApiLevel))."

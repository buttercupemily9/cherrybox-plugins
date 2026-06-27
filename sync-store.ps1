# Sync plugins/store.json from each plugin's plugin.json (versions, names, folders).
# Preserves description, changelog, homepage, and iconUrl from the existing catalog when present.
# Bumps catalogVersion when any plugin version changes or plugins are added/removed.
param(
    [string]$StorePath = (Join-Path $PSScriptRoot "store.json"),
    [string]$RepoOwner = "buttercupemily9",
    [string]$RepoName = "cherrybox-plugins",
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

function Get-DefaultHomepage([string]$folder) {
    "https://github.com/$RepoOwner/$RepoName/tree/$Branch/$folder"
}

function Get-DefaultIconUrl([string]$folder) {
    if (Test-Path (Join-Path $root $folder "icon.png")) {
        return "https://raw.githubusercontent.com/$RepoOwner/$RepoName/$Branch/$folder/icon.png"
    }

    return "https://raw.githubusercontent.com/$RepoOwner/$RepoName/$Branch/StorySites/icon.png"
}

function Get-NormalizedScreenshotUrls($value) {
    if ($null -eq $value) { return @() }
    if ($value -is [string]) { return @([string]$value) }
    if ($value -is [System.Array] -or $value -is [System.Collections.IList]) {
        return @($value | ForEach-Object { [string]$_ })
    }

    return @()
}

function Format-ScreenshotUrlsJson($value) {
    $items = Get-NormalizedScreenshotUrls $value
    if ($items.Count -eq 0) { return "[]" }

    $encoded = ($items | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ', '
    return "[$encoded]"
}

function Write-StoreJson([string]$path, [string]$content) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($path, $content, $utf8NoBom)
}

$existing = @{}
$catalogVersion = 1
if (Test-Path $StorePath) {
    $current = Get-Content $StorePath -Raw | ConvertFrom-Json
    $catalogVersion = [int]$current.catalogVersion
    foreach ($plugin in $current.plugins) {
        $existing[[string]$plugin.id] = $plugin
    }
}

$discoveredIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$entries = New-Object System.Collections.Generic.List[object]
$versionChanges = New-Object System.Collections.Generic.List[string]

Get-ChildItem -Path $root -Directory | ForEach-Object {
    $manifestPath = Join-Path $_.FullName "plugin.json"
    if (-not (Test-Path $manifestPath)) { return }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.storeHidden -eq $true) { return }

    $id = [string]$manifest.id
    if (-not $id) { throw "Missing id in $manifestPath" }

    [void]$discoveredIds.Add($id)
    $folder = $_.Name
    $version = [string]$manifest.version
    if (-not $version) { throw "Missing version in $manifestPath" }

    $name = if ($manifest.name) { [string]$manifest.name } else { $id }
    $prev = if ($existing.ContainsKey($id)) { $existing[$id] } else { $null }

    if ($null -eq $prev) {
        $versionChanges.Add("$id (new - update changelog in store.json)") | Out-Null
    }
    elseif ([string]$prev.version -ne $version) {
        $versionChanges.Add("$id ($($prev.version) -> $version)") | Out-Null
    }

    $description = if ($manifest.description) {
        [string]$manifest.description
    }
    elseif ($prev -and $prev.description) {
        [string]$prev.description
    }
    else {
        $name
    }

    $entry = [ordered]@{
        id = $id
        folder = $folder
        name = $name
        version = $version
        description = $description
        author = if ($prev -and $prev.author) { [string]$prev.author } else { "CherryBox" }
        homepage = if ($prev -and $prev.homepage) { [string]$prev.homepage } else { Get-DefaultHomepage $folder }
        iconUrl = if ($prev -and $prev.iconUrl) { [string]$prev.iconUrl } else { Get-DefaultIconUrl $folder }
        screenshotUrls = Get-NormalizedScreenshotUrls $(if ($prev) { $prev.screenshotUrls } else { $null })
        changelog = if ($prev -and $prev.changelog) { [string]$prev.changelog } else { "Initial release." }
    }

    $entries.Add([pscustomobject]$entry) | Out-Null
}

$removed = @($existing.Keys | Where-Object { -not $discoveredIds.Contains($_) })
if ($removed.Count -gt 0) {
    foreach ($id in $removed) {
        $versionChanges.Add("$id (removed)") | Out-Null
    }
}

if ($versionChanges.Count -gt 0) {
    $catalogVersion++
}

$sorted = $entries | Sort-Object id
$store = [ordered]@{
    catalogVersion = $catalogVersion
    plugins = @($sorted)
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("{") | Out-Null
$lines.Add('  "catalogVersion": ' + $catalogVersion + ',') | Out-Null
$lines.Add('  "plugins": [') | Out-Null
for ($i = 0; $i -lt $sorted.Count; $i++) {
    $plugin = $sorted[$i]
    $screenshots = Format-ScreenshotUrlsJson $plugin.screenshotUrls
    $lines.Add("    {") | Out-Null
    $lines.Add('      "id": "' + ($plugin.id -replace '"', '\"') + '",') | Out-Null
    $lines.Add('      "folder": "' + ($plugin.folder -replace '"', '\"') + '",') | Out-Null
    $lines.Add('      "name": "' + ($plugin.name -replace '"', '\"') + '",') | Out-Null
    $lines.Add('      "version": "' + ($plugin.version -replace '"', '\"') + '",') | Out-Null
    $lines.Add('      "description": "' + ($plugin.description -replace '"', '\"') + '",') | Out-Null
    $lines.Add('      "author": "' + ($plugin.author -replace '"', '\"') + '",') | Out-Null
    $lines.Add('      "homepage": "' + ($plugin.homepage -replace '"', '\"') + '",') | Out-Null
    $lines.Add('      "iconUrl": "' + ($plugin.iconUrl -replace '"', '\"') + '",') | Out-Null
    $lines.Add('      "screenshotUrls": ' + $screenshots + ',') | Out-Null
    $lines.Add('      "changelog": "' + ($plugin.changelog -replace '"', '\"') + '"') | Out-Null
    $suffix = if ($i -lt ($sorted.Count - 1)) { "    }," } else { "    }" }
    $lines.Add($suffix) | Out-Null
}
$lines.Add("  ]") | Out-Null
$lines.Add("}") | Out-Null
Write-StoreJson $StorePath (($lines -join [Environment]::NewLine) + [Environment]::NewLine)

Write-Host "Updated $StorePath"
Write-Host "  catalogVersion: $catalogVersion"
Write-Host "  plugins: $($sorted.Count)"

if ($versionChanges.Count -gt 0) {
    Write-Host ""
    Write-Host "Version/catalog changes detected:"
    foreach ($change in $versionChanges) {
        Write-Host "  - $change"
    }

    Write-Host ""
    Write-Host "Update each plugin's changelog entry in store.json before committing."
}

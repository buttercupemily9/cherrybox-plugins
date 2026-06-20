# Regenerate store.json entries from plugin folders (ids/descriptions preserved when possible).
param(
    [string]$StorePath = (Join-Path $PSScriptRoot "store.json")
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$existing = @{}
if (Test-Path $StorePath) {
    $current = Get-Content $StorePath -Raw | ConvertFrom-Json
    foreach ($plugin in $current.plugins) {
        $existing[$plugin.id] = $plugin
    }
}

$entries = @()
Get-ChildItem -Path $root -Directory | ForEach-Object {
    $manifestPath = Join-Path $_.FullName "plugin.json"
    if (-not (Test-Path $manifestPath)) { return }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $id = [string]$manifest.id
    $description = $null
    if ($existing.ContainsKey($id) -and $existing[$id].description) {
        $description = [string]$existing[$id].description
    }

    $entry = [ordered]@{
        id = $id
        folder = $_.Name
    }
    if ($description) { $entry.description = $description }
    $entries += [pscustomobject]$entry
}

$store = [ordered]@{
    catalogVersion = 2
    plugins = $entries | Sort-Object id
}

$store | ConvertTo-Json -Depth 4 | Set-Content -Path $StorePath -Encoding UTF8
Write-Host "Updated $StorePath with $($entries.Count) plugin(s)."

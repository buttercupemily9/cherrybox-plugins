# Copy shared CherryBox plugin theme assets into each plugin web/cherrybox/ folder.
param(
    [string]$Source = "",
    [string]$PluginsRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Source)) {
    $repoRoot = Split-Path -Parent $PluginsRoot
    $Source = Join-Path $repoRoot "web\public\skins"
}

$files = @(
    "tokens.css",
    "stock-components.css",
    "stock-plugin.css",
    "cherrybox-plugin-shell.js",
    "cherrybox-plugin-api.js"
)

if (-not (Test-Path $Source)) {
    $fallback = Join-Path (Split-Path -Parent $PluginsRoot) "web\public\skins"
    if (Test-Path $fallback) {
        $Source = $fallback
    }
}

if (-not (Test-Path $Source)) {
    throw "CherryBox theme source not found: $Source"
}

$synced = 0
Get-ChildItem -Path $PluginsRoot -Directory | ForEach-Object {
    $manifestPath = Join-Path $_.FullName "plugin.json"
    $webDir = Join-Path $_.FullName "web"
    if (-not (Test-Path $manifestPath) -or -not (Test-Path $webDir)) { return }

    $targetDir = Join-Path $webDir "cherrybox"
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

    foreach ($file in $files) {
        Copy-Item -Force (Join-Path $Source $file) (Join-Path $targetDir $file)
    }

    Write-Host "Synced theme -> $($_.Name)/web/cherrybox"
    $synced++
}

Write-Host "Synced CherryBox theme into $synced plugin(s)."

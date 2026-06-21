# Copy shared CherryBox plugin theme assets into each plugin web/cherrybox/ folder.
param(
    [string]$Source = "",
    [string]$PluginsRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

function Resolve-CherryBoxThemeSource {
    param(
        [string]$PluginsRoot,
        [string]$ExplicitSource
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitSource)) {
        if (-not (Test-Path $ExplicitSource)) {
            throw "CherryBox theme source not found: $ExplicitSource"
        }
        return (Resolve-Path $ExplicitSource).Path
    }

    $candidates = @(
        (Join-Path $PluginsRoot "_cherrybox\web\public\skins"),
        (Join-Path (Split-Path -Parent $PluginsRoot) "web\public\skins")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    $tried = ($candidates | ForEach-Object { "  - $_" }) -join [Environment]::NewLine
    throw "CherryBox theme source not found. Pass -Source or check out cherrybox beside plugins. Tried:$([Environment]::NewLine)$tried"
}

$Source = Resolve-CherryBoxThemeSource -PluginsRoot $PluginsRoot -ExplicitSource $Source

$files = @(
    "tokens.css",
    "stock-components.css",
    "stock-plugin.css",
    "cherrybox-plugin-shell.js",
    "cherrybox-plugin-api.js",
    "cherrybox-folder-browser.js"
)

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

Write-Host "Synced CherryBox theme into $synced plugin(s) from $Source"

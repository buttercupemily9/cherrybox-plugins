# Publish plugins from this repo into %ProgramData%\CherryBox\plugins for local development.
param(
    [string]$Configuration = "Release",
    [string]$ProgramDataRoot = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

& (Join-Path $root "sync-cherrybox-theme.ps1")

if ([string]::IsNullOrWhiteSpace($ProgramDataRoot)) {
    $ProgramDataRoot = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) "CherryBox\plugins"
}

New-Item -ItemType Directory -Force -Path $ProgramDataRoot | Out-Null
$MetadataRoot = Split-Path $ProgramDataRoot -Parent
$PluginConfigRoot = Join-Path $MetadataRoot "config"
New-Item -ItemType Directory -Force -Path $PluginConfigRoot | Out-Null

function Get-PluginConfigDest {
    param(
        [string]$PluginId,
        [string]$FileName
    )
    if ($FileName -eq 'settings.json') {
        return Join-Path $PluginConfigRoot ($PluginId + '-settings.json')
    }
    if ($FileName -like "$PluginId-*") {
        return Join-Path $PluginConfigRoot $FileName
    }
    $base = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
    $ext = [System.IO.Path]::GetExtension($FileName)
    return Join-Path $PluginConfigRoot ($PluginId + '-' + $base + $ext)
}

function Copy-PluginStorageIfMissing {
    param(
        [string]$PluginId,
        [string]$SourceDir
    )
    if (-not (Test-Path $SourceDir)) { return }

    Get-ChildItem -Path $SourceDir -File | ForEach-Object {
        if ($_.Name -eq 'plugin.json' -or $_.Extension -in '.dll', '.pdb') { return }
        if ($_.Name -like '*.deps.json' -or $_.Name -like '*.runtimeconfig.json') { return }

        if ($_.Extension -eq '.db') {
            $baseName = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
            if ($baseName.StartsWith("$PluginId-")) {
                $dest = Join-Path $MetadataRoot ($baseName + '.db')
            } else {
                $dest = Join-Path $MetadataRoot ($PluginId + '-' + $baseName + '.db')
            }
        } else {
            $dest = Get-PluginConfigDest -PluginId $PluginId -FileName $_.Name
        }

        if (-not (Test-Path $dest)) {
            New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
            Copy-Item -Force $_.FullName $dest
        }
    }

    Get-ChildItem -Path $SourceDir -Directory | Where-Object { $_.Name -ne 'web' } | ForEach-Object {
        Get-ChildItem -Path $_.FullName -Recurse -File | ForEach-Object {
            $dest = Get-PluginConfigDest -PluginId $PluginId -FileName $_.Name
            if (-not (Test-Path $dest)) {
                New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
                Copy-Item -Force $_.FullName $dest
            }
        }
    }

    $legacyDataDir = Join-Path $MetadataRoot "plugin-data\$PluginId"
    if (Test-Path $legacyDataDir) {
        Copy-PluginStorageIfMissing -PluginId $PluginId -SourceDir $legacyDataDir
    }

    $legacyConfigDir = Join-Path $PluginConfigRoot $PluginId
    if (Test-Path $legacyConfigDir) {
        Copy-PluginStorageIfMissing -PluginId $PluginId -SourceDir $legacyConfigDir
    }
}

$plugins = Get-ChildItem -Path $root -Directory | ForEach-Object {
    $manifestPath = Join-Path $_.FullName "plugin.json"
    $project = Get-ChildItem -Path $_.FullName -Filter "*.csproj" | Select-Object -First 1
    if (-not (Test-Path $manifestPath) -or $null -eq $project) { return }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    [pscustomobject]@{
        Folder = $_.Name
        Id = [string]$manifest.id
        Project = $project.Name
    }
} | Where-Object { $_ -ne $null }

foreach ($plugin in $plugins) {
    $sourceDir = Join-Path $root $plugin.Folder
    $targetDir = Join-Path $ProgramDataRoot $plugin.Id
    $stagingDir = Join-Path $env:TEMP ("cherrybox-plugin-" + $plugin.Id)

    if (Test-Path $stagingDir) { Remove-Item -Recurse -Force $stagingDir }

    Write-Host "Installing $($plugin.Id) to $targetDir ..."
    dotnet publish (Join-Path $sourceDir $plugin.Project) -c $Configuration -o $stagingDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($plugin.Id)" }

    Copy-Item -Force (Join-Path $sourceDir "plugin.json") $stagingDir
    $webDir = Join-Path $sourceDir "web"
    if (Test-Path $webDir) {
        Copy-Item -Recurse -Force $webDir (Join-Path $stagingDir "web")
    }

    Get-ChildItem -Path $stagingDir -Filter "CherryBox.Plugins.Abstractions.*" | Remove-Item -Force

    if (Test-Path $targetDir) {
        Copy-PluginStorageIfMissing -PluginId $plugin.Id -SourceDir $targetDir
        Remove-Item -Recurse -Force $targetDir
    }
    Copy-Item -Recurse -Force $stagingDir $targetDir
    Remove-Item -Recurse -Force $stagingDir
}

Write-Host "Installed $($plugins.Count) plugin(s) to $ProgramDataRoot"
Write-Host "Plugin config: $PluginConfigRoot\{plugin-id}-settings.json"
Write-Host "Reload plugins from Settings -> Plugins, or restart CherryBox."

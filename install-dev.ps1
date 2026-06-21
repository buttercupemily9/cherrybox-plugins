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

    if (Test-Path $targetDir) { Remove-Item -Recurse -Force $targetDir }
    Copy-Item -Recurse -Force $stagingDir $targetDir
    Remove-Item -Recurse -Force $stagingDir
}

Write-Host "Installed $($plugins.Count) plugin(s) to $ProgramDataRoot"
Write-Host "Reload plugins from Settings -> Plugins, or restart CherryBox."

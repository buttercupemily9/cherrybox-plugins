# Build installable plugin packages for the CherryBox plugin store.
param(
    [string]$Configuration = "Release",
    [string]$Output = "packages"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$outDir = Join-Path $root $Output
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& (Join-Path $root "sync-cherrybox-theme.ps1") -PluginsRoot $root

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
} | Where-Object { $_ -ne $null } | Sort-Object Id

if ($plugins.Count -eq 0) {
    throw "No plugin folders with plugin.json and a .csproj were found."
}

foreach ($plugin in $plugins) {
    $sourceDir = Join-Path $root $plugin.Folder
    $stagingDir = Join-Path $outDir ("stage-" + $plugin.Id)
    $zipPath = Join-Path $outDir ($plugin.Id + ".zip")

    if (Test-Path $stagingDir) { Remove-Item -Recurse -Force $stagingDir }
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

    Write-Host "Publishing $($plugin.Id) from $($plugin.Folder)..."
    dotnet publish (Join-Path $sourceDir $plugin.Project) -c $Configuration -o $stagingDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($plugin.Id)" }

    Copy-Item -Force (Join-Path $sourceDir "plugin.json") $stagingDir

    $webDir = Join-Path $sourceDir "web"
    if (Test-Path $webDir) {
        Copy-Item -Recurse -Force $webDir (Join-Path $stagingDir "web")
    }

    # Abstractions are provided by the CherryBox host; bundling a copy breaks plugin loading.
    Get-ChildItem -Path $stagingDir -Filter "CherryBox.Plugins.Abstractions.*" | Remove-Item -Force

    $assemblyName = ([xml](Get-Content (Join-Path $sourceDir $plugin.Project))).Project.PropertyGroup.AssemblyName
    if (-not $assemblyName) {
        $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($plugin.Project)
    }
    $dllPath = Join-Path $stagingDir ($assemblyName + ".dll")
    if (-not (Test-Path $dllPath)) {
        throw "Expected plugin assembly was not produced: $dllPath"
    }

    Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath
    Remove-Item -Recurse -Force $stagingDir
    Write-Host "  -> $zipPath"
}

Write-Host "Built $($plugins.Count) package(s) in $outDir"

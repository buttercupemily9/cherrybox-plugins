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

function Get-SourceFilesWithoutReparse {
    param([string]$RootDir)

    if (-not (Test-Path -LiteralPath $RootDir)) { return @() }

    $resolvedRoot = (Resolve-Path -LiteralPath $RootDir).Path
    $files = New-Object System.Collections.Generic.List[object]
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $visited = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $pending.Push($resolvedRoot)

    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        if (-not $visited.Add($current)) { continue }

        foreach ($entry in Get-ChildItem -LiteralPath $current -Force -ErrorAction SilentlyContinue) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            if ($entry.PSIsContainer) {
                if ($entry.Name -eq 'web') { continue }
                $pending.Push($entry.FullName)
            } else {
                [void]$files.Add($entry)
            }
        }
    }

    return $files
}

function Copy-PluginStorageIfMissing {
    param(
        [string]$PluginId,
        [string]$SourceDir,
        [System.Collections.Generic.HashSet[string]]$VisitedSources = $null
    )
    if ($null -eq $VisitedSources) {
        $VisitedSources = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    }
    if (-not (Test-Path -LiteralPath $SourceDir)) { return }

    $normalizedSource = (Resolve-Path -LiteralPath $SourceDir).Path
    if (-not $VisitedSources.Add($normalizedSource)) { return }

    Get-ChildItem -LiteralPath $SourceDir -File -ErrorAction SilentlyContinue | ForEach-Object {
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

        if (-not (Test-Path -LiteralPath $dest)) {
            New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
            Copy-Item -Force $_.FullName $dest
        }
    }

    foreach ($subdir in Get-ChildItem -LiteralPath $SourceDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'web' }) {
        foreach ($file in Get-SourceFilesWithoutReparse -RootDir $subdir.FullName) {
            $dest = Get-PluginConfigDest -PluginId $PluginId -FileName $file.Name
            if (-not (Test-Path -LiteralPath $dest)) {
                New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
                Copy-Item -Force $file.FullName $dest
            }
        }
    }

    $legacyDataDir = Join-Path $MetadataRoot "plugin-data\$PluginId"
    if (Test-Path -LiteralPath $legacyDataDir) {
        Copy-PluginStorageIfMissing -PluginId $PluginId -SourceDir $legacyDataDir -VisitedSources $VisitedSources
    }

    $legacyConfigDir = Join-Path $PluginConfigRoot $PluginId
    if (Test-Path -LiteralPath $legacyConfigDir) {
        Copy-PluginStorageIfMissing -PluginId $PluginId -SourceDir $legacyConfigDir -VisitedSources $VisitedSources
    }
}

function Install-StagedPluginDirectory {
    param(
        [string]$StagingDir,
        [string]$TargetDir
    )

    New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

    Get-ChildItem -LiteralPath $StagingDir -Force | ForEach-Object {
        $dest = Join-Path $TargetDir $_.Name
        if ($_.PSIsContainer) {
            Copy-Item -LiteralPath $_.FullName -Destination $dest -Recurse -Force
        } else {
            try {
                Copy-Item -LiteralPath $_.FullName -Destination $dest -Force -ErrorAction Stop
            } catch {
                Write-Warning "Could not update locked file (stop CherryBox to refresh plugins): $dest"
            }
        }
    }

    Get-ChildItem -LiteralPath $TargetDir -Recurse -File -Force | ForEach-Object {
        $relative = $_.FullName.Substring($TargetDir.Length).TrimStart('\', '/')
        $stagingPath = Join-Path $StagingDir $relative
        if (Test-Path -LiteralPath $stagingPath) { return }
        if ($_.Extension -eq '.db') { return }

        try {
            Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
        } catch {
            Write-Warning "Could not remove stale plugin file (may be in use): $($_.FullName)"
        }
    }

    Get-ChildItem -LiteralPath $TargetDir -Recurse -Directory -Force |
        Sort-Object { $_.FullName.Length } -Descending |
        ForEach-Object {
            if (-not (Get-ChildItem -LiteralPath $_.FullName -Force | Select-Object -First 1)) {
                try {
                    Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
                } catch {
                    Write-Warning "Could not remove empty plugin folder: $($_.FullName)"
                }
            }
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

    if (Test-Path -LiteralPath $targetDir) {
        Copy-PluginStorageIfMissing -PluginId $plugin.Id -SourceDir $targetDir
        Install-StagedPluginDirectory -StagingDir $stagingDir -TargetDir $targetDir
    } else {
        Copy-Item -Recurse -Force $stagingDir $targetDir
    }
    Remove-Item -Recurse -Force $stagingDir
}

Write-Host "Installed $($plugins.Count) plugin(s) to $ProgramDataRoot"
Write-Host "Plugin config: $PluginConfigRoot\{plugin-id}-settings.json"
Write-Host "If DLL updates failed, stop CherryBox and run this script again."
Write-Host "Reload plugins from Settings -> Plugins, or restart CherryBox."

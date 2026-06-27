# Fail CI when store.json is out of sync with plugin.json manifests.
param(
    [string]$StorePath = (Join-Path $PSScriptRoot "store.json")
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

if (-not (Test-Path $StorePath)) {
    throw "Missing store catalog: $StorePath. Run .\sync-store.ps1"
}

$store = Get-Content $StorePath -Raw | ConvertFrom-Json
$storeById = @{}
foreach ($entry in $store.plugins) {
    $storeById[[string]$entry.id] = $entry
}

$manifestIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$errors = New-Object System.Collections.Generic.List[string]

Get-ChildItem -Path $root -Directory | ForEach-Object {
    $manifestPath = Join-Path $_.FullName "plugin.json"
    if (-not (Test-Path $manifestPath)) { return }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.storeHidden -eq $true) { return }

    $id = [string]$manifest.id
    [void]$manifestIds.Add($id)

    if (-not $storeById.ContainsKey($id)) {
        $errors.Add("store.json is missing catalog entry for plugin '$id'. Run .\sync-store.ps1") | Out-Null
        return
    }

    $entry = $storeById[$id]
    $manifestVersion = [string]$manifest.version
    $storeVersion = [string]$entry.version

    if ($manifestVersion -ne $storeVersion) {
        $errors.Add(
            "store.json version for '$id' is $storeVersion but plugin.json is $manifestVersion. Run .\sync-store.ps1 and update the changelog."
        ) | Out-Null
    }

    if ([string]$entry.folder -ne $_.Name) {
        $errors.Add(
            "store.json folder for '$id' is '$($entry.folder)' but plugin folder is '$($_.Name)'."
        ) | Out-Null
    }

    if ($manifest.name -and [string]$entry.name -ne [string]$manifest.name) {
        $errors.Add(
            "store.json name for '$id' is '$($entry.name)' but plugin.json name is '$($manifest.name)'."
        ) | Out-Null
    }
}

foreach ($id in $storeById.Keys) {
    if (-not $manifestIds.Contains($id)) {
        $errors.Add("store.json lists removed or hidden plugin '$id'. Run .\sync-store.ps1") | Out-Null
    }
}

if ($errors.Count -gt 0) {
    Write-Host "store.json verification failed:" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host "  - $err" -ForegroundColor Red
    }

    throw "store.json is out of sync with plugin manifests."
}

Write-Host "store.json is in sync with $($manifestIds.Count) plugin manifest(s)."

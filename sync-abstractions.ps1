# Copy plugin abstractions from the main CherryBox tree into this repo.
param(
    [string]$CherryBoxRoot = (Join-Path $PSScriptRoot "..")
)

$ErrorActionPreference = "Stop"
$source = Join-Path $CherryBoxRoot "src\CherryBox.Plugins.Abstractions"
$target = Join-Path $PSScriptRoot "CherryBox.Plugins.Abstractions"

if (-not (Test-Path $source)) {
    throw "CherryBox abstractions not found at $source"
}

Get-ChildItem -Path $source -File | Where-Object { $_.Extension -in ".cs", ".csproj" } | ForEach-Object {
    Copy-Item -Force $_.FullName (Join-Path $target $_.Name)
}

Write-Host "Synced abstractions from $source to $target"

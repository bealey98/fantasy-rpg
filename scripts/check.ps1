# Builds everything, runs every test, then a short benchmark.
# Run from anywhere in PowerShell:  scripts\check.ps1
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

Write-Host "== build"
dotnet build Battle.sln -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== tests"
dotnet run --project tests/Battle.Tests -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== benchmark"
dotnet run --project src/Battle.Sim -c Release --no-build -- bench --seconds 3
exit $LASTEXITCODE

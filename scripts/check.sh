#!/usr/bin/env bash
# Builds everything, runs every test, then a short benchmark.
# Run from anywhere:  scripts/check.sh
set -euo pipefail
cd "$(dirname "$0")/.."

echo "== build"
dotnet build Battle.sln -c Release --nologo -v quiet

echo "== tests"
dotnet run --project tests/Battle.Tests -c Release --no-build

echo "== benchmark"
dotnet run --project src/Battle.Sim -c Release --no-build -- bench --seconds 3 --no-save

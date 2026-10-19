#!/usr/bin/env bash
# Builds the phone version into dist/web:
#   index.html   the page, ready to open from any web server
#   page.html    the same page without the document wrapper, for publishing as a Claude artifact
#   app.js and _framework/   the page's code and the battle engine (WebAssembly)
# Run from anywhere:  scripts/build-web.sh   then try it with:  python3 -m http.server -d dist/web
set -euo pipefail
cd "$(dirname "$0")/.."

out=dist/web
publish=$(mktemp -d)
trap 'rm -rf "$publish"' EXIT

echo "== publishing the engine"
dotnet publish src/Battle.Web/Battle.Web.csproj -c Release -o "$publish" --nologo -v quiet

echo "== assembling $out"
rm -rf "$out"
mkdir -p "$out/_framework"
# Only what the browser loads: no precompressed copies (most hosts can't serve them) and no debug symbols.
find "$publish/wwwroot/_framework" -maxdepth 1 -type f ! -name '*.br' ! -name '*.gz' ! -name '*.pdb' -exec cp {} "$out/_framework/" \;
cp src/Battle.Web/wwwroot/app.js src/Battle.Web/wwwroot/page.html "$out/"
# The same wrapper the artifact host puts around a page, so both look alike.
{
  printf '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">'
  printf '<style>:root{color-scheme:light;padding-top:env(safe-area-inset-top,0px);padding-bottom:env(safe-area-inset-bottom,0px)}body{margin:0;font:14px system-ui,sans-serif}img{max-width:100%%}[hidden]{display:none!important}</style></head><body>\n'
  cat src/Battle.Web/wwwroot/page.html
  printf '\n</body></html>\n'
} > "$out/index.html"

echo "== done: $(du -sh "$out" | cut -f1) in $out"

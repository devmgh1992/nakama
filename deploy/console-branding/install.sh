#!/usr/bin/env bash
# Copy the Gayvelopers console assets into place and sanity-check them.
# nginx edits are not applied automatically — review nginx-console-branding.conf first.
set -euo pipefail

SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST="${1:-/var/www/nakama-branding}"
HOST="${2:-}"

echo "→ installing assets into $DEST"
mkdir -p "$DEST"
cp "$SRC"/{icon.svg,logo.svg,logo-dark.svg,banner.svg,brand.css,brand.js} "$DEST"/
chmod -R a+r "$DEST"
ls -1 "$DEST"

if [[ -n "$HOST" ]]; then
  echo "→ checking $HOST"
  curl -s -o /dev/null -w '  icon.svg   %{http_code}\n' "https://$HOST/branding/icon.svg"
  curl -s -o /dev/null -w '  brand.css  %{http_code}\n' "https://$HOST/branding/brand.css"
  echo -n "  injected:  "
  curl -s "https://$HOST/" | grep -o 'brand\.\(css\|js\)?v=[0-9]*' | tr '\n' ' ' || echo "NOT INJECTED — apply the nginx snippet"
  echo
else
  echo "→ next: paste nginx-console-branding.conf into the console server block,"
  echo "  then nginx -t && systemctl reload nginx"
  echo "  re-run with a host to verify: $0 $DEST console.example.com"
fi

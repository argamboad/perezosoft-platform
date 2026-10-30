#!/usr/bin/env bash
#
# What this repo's projects and identities are called (Pkg 5.4), found by convention so that one workflow
# (.github/workflows/app-ci.yml) serves the platform and every app: the host is src/Host/*.csproj, the WASM client
# src/Web/*.csproj, the MAUI shell src/Maui/*.csproj, the journeys tests/E2E.Tests/*.csproj, and every other
# tests/*/*.csproj is a test project. The MAUI app's id is its <ApplicationId>; the dev override of its API base URL is
# <BRAND>_API_BASE_URL, the brand being what platform-stamp.json maps "Perezosoft" to. The Android smoke spec is the
# repo's own tests/native-smoke-android, else the platform's ($PLATFORM_DIR, the workflow's checkout of the platform).
#
# Prints NAME=value lines for $GITHUB_ENV, run from the repo root. A project the repo does not have prints empty.
set -euo pipefail

first() { for f in $1; do [ -f "$f" ] && { printf '%s' "$f"; return 0; }; done; return 0; }

host=$(first 'src/Host/*.csproj')
web=$(first 'src/Web/*.csproj')
maui=$(first 'src/Maui/*.csproj')
e2e=$(first 'tests/E2E.Tests/*.csproj')
tests=""
for f in tests/*/*.csproj; do
  [ -f "$f" ] || continue
  case "$f" in tests/E2E.Tests/*) ;; *) tests="${tests:+$tests }$f" ;; esac
done

maui_name=""; app_id=""
if [ -n "$maui" ]; then
  maui_name=$(basename "$maui" .csproj)
  app_id=$(sed -n 's:.*<ApplicationId>\([^<]*\)</ApplicationId>.*:\1:p' "$maui" | head -1)
fi

brand=Perezosoft
if [ -f platform-stamp.json ]; then
  brand=$(python3 -c 'import json; print((json.load(open("platform-stamp.json")).get("brand") or {}).get("Perezosoft") or "Perezosoft")')
fi

smoke=tests/native-smoke-android
[ -d "$smoke" ] || smoke="${PLATFORM_DIR:-.}/tests/native-smoke-android"

echo "HOST_PROJ=$host"
echo "WEB_PROJ=$web"
echo "MAUI_PROJ=$maui"
echo "E2E_PROJ=$e2e"
echo "TEST_PROJS=$tests"
echo "MAUI_NAME=$maui_name"
echo "APP_ID=$app_id"
echo "API_URL_VAR=$(printf '%s' "$brand" | tr '[:lower:]' '[:upper:]')_API_BASE_URL"
echo "SMOKE_DIR=$smoke"

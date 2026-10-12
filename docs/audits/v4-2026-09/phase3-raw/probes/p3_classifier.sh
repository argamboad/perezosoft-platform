#!/usr/bin/env bash
# P3: the `changes` classifier (both ci.yml copies, lines 396-433 / 300-337) run VERBATIM against a real
# `git diff --name-only` of a temp repo, so git's own path quoting is part of the probe.
set -u
W=$(mktemp -d); cd "$W"; git init -q . && git config user.email a@b && git config user.name a && git config core.autocrlf false
mkdir -p src/Api docs tests/E2E.Tests .forgejo/scripts tools
echo base > src/Api/Program.cs && echo d > docs/x.md && git add -A && git commit -qm base
BASE_SHA=$(git rev-parse HEAD)
classify() { # verbatim from .forgejo/workflows/ci.yml lines 411-431 (the regexes are the Forgejo copy's)
  files=$(git diff --name-only "$BASE_SHA" HEAD)
  codefiles=$(printf '%s\n' "$files" | grep -vE '\.md$' || true)
  match() { printf '%s\n' "$1" | grep -qE "$2" && echo true || echo false; }
  code=$(match "$codefiles" '^(src/|tests/|Directory\.Packages\.props|Directory\.Build\.props|global\.json|Dockerfile|docker-compose\.yml|\.config/dotnet-tools\.json|\.github/(workflows/ci\.yml|scripts/)|.*packages\.lock\.json|\.forgejo/workflows/ci\.yml)')
  native=$(match "$codefiles" '^(src/|tests/E2E\.Tests/|tests/native-smoke-android/|Directory\.Packages\.props|\.github/workflows/ci\.yml|global\.json|\.forgejo/workflows/ci\.yml)')
  docs=$(match "$files" '^docs/')
  printf 'code=%-5s native=%-5s docs=%-5s  diff=[%s]\n' "$code" "$native" "$docs" "$(echo "$files" | tr '\n' ' ')"
}
case_() { git checkout -q "$BASE_SHA" 2>/dev/null; git checkout -q -B probe; printf '%-58s ' "$1"; }
case_ "1 src/Api/Program.cs edited";                echo x > src/Api/Program.cs; git commit -qam c; classify
case_ "2 src/Api/README.md only";                   echo r > src/Api/README.md; git add -A; git commit -qm c; classify
case_ "3 src/Api/README.MD (upper-case ext)";       echo r > src/Api/README.MD; git add -A; git commit -qm c; classify
case_ "4 src/Api/Features/Añadir.cs (non-ASCII)";   mkdir -p src/Api/Features; echo x > "src/Api/Features/Añadir.cs"; git add -A; git commit -qm c; classify
case_ "5 docs/Guía.md (non-ASCII docs)";            echo x > "docs/Guía.md"; git add -A; git commit -qm c; classify
case_ "6 .forgejo/scripts/push-to-github.sh (DEP-15)"; echo x > .forgejo/scripts/push-to-github.sh; git add -A; git commit -qm c; classify
case_ "7 tools/publish-native.ps1 (NAT-16)";        echo x > tools/publish-native.ps1; git add -A; git commit -qm c; classify
case_ "8 Directory.Build.props (S0-G6)";            echo x > Directory.Build.props; git add -A; git commit -qm c; classify
case_ "9 rename Program.cs -> Programme.cs";        git mv src/Api/Program.cs src/Api/Programme.cs; git commit -qm c; classify
case_ "10 delete src/Api/Program.cs";               git rm -q src/Api/Program.cs; git commit -qm c; classify
case_ "11 empty commit";                            git commit -q --allow-empty -m c; classify
case_ "12 quotePath=false, same as case 4";         echo x > "src/Api/Añadir.cs"; git add -A; git commit -qm c; git config core.quotePath false; classify; git config --unset core.quotePath
case_ "13 path with a space: src/Api/My File.cs";   echo x > "src/Api/My File.cs"; git add -A; git commit -qm c; classify

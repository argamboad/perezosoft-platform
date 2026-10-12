#!/usr/bin/env bash
# P1: the non-FF vs "ruleset" diagnosis in push-to-github.sh (lines 38-53), local bare repo = GitHub.
set -u
W=$(mktemp -d); cd "$W"; git init -q --bare gh.git
git init -q work && cd work && git config user.email a@b && git config user.name a && git config core.autocrlf false
echo 1 > f && git add f && git commit -qm c1 && git branch -M develop && git push -q ../gh.git develop:develop
diagnose() { # verbatim logic of lines 38-53, URL swapped for the local bare repo
  local REMOTE="$1" BRANCH="$2"
  if ! git push "$REMOTE" "$SHA:refs/heads/$BRANCH" 2>/dev/null; then
    if git fetch --quiet "$REMOTE" "refs/heads/$BRANCH" 2>/dev/null \
       && ! git merge-base --is-ancestor FETCH_HEAD "$SHA"; then echo "VERDICT: non-fast-forward (pull GitHub's branch)"
    else echo "VERDICT: refused but IS an ancestor -> ruleset/token"; fi
    return 1
  fi; echo "VERDICT: pushed"
}
echo 2 > f && git commit -qam c2; SHA=$(git rev-parse HEAD)
echo "--- A: plain fast-forward"; diagnose ../gh.git develop
echo "--- B: GitHub has a commit Forgejo does not (real non-FF)"
( cd "$W" && git clone -q -b develop gh.git other 2>/dev/null && cd other && git config user.email a@b && git config user.name a && echo x > g && git add g && git commit -qm other && git push -q origin develop:develop )
echo 3 > f && git commit -qam c3; SHA=$(git rev-parse HEAD); diagnose ../gh.git develop
echo "--- C: ruleset-style refusal (pre-receive exit 1), GitHub's branch IS an ancestor"
git fetch -q ../gh.git develop && git merge -q --no-edit FETCH_HEAD && echo 4 > f && git commit -qam c4; SHA=$(git rev-parse HEAD)
printf '#!/bin/sh\necho "GH013: refusing (fake ruleset)" >&2\nexit 1\n' > ../gh.git/hooks/pre-receive; chmod +x ../gh.git/hooks/pre-receive
diagnose ../gh.git develop
echo "--- D: the fetch itself fails (repo not found / 401 / DEPLOY_MIRROR_REPO typo)"; diagnose ../does-not-exist.git develop
echo "--- E: branch absent on the mirror (first prod deploy) and the push refused"; diagnose ../gh.git main

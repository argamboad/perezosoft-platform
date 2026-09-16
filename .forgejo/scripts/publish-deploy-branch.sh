#!/usr/bin/env bash
#
# Publish the checked-out commit to a deploy branch on the GitHub mirror (LOCALCI-4, ADR-028).
#
# Render builds from GitHub, never from Forgejo, so a deploy starts by making the commit exist there.
# It goes to a `deploy/*` branch, not to develop/main: GitHub's ci.yml only triggers on main, develop
# and pull requests, so this push runs no GitHub Actions. The branch is a pointer the pipeline owns —
# it is force-moved, which is also how a rollback (re-running an older develop run) lands.
#
# Usage: publish-deploy-branch.sh <branch>          e.g. deploy/staging
# Env:   HOOK          the Render deploy hook for this target — unset ⇒ nothing will deploy, skip quietly
#        MIRROR_TOKEN  a GitHub token with contents:write on MIRROR_REPO
#        MIRROR_REPO   owner/name on github.com (repo variable DEPLOY_MIRROR_REPO)
# Exit:  0 = published (or deliberately skipped); 1 = a deploy is configured but cannot be published.

set -euo pipefail

BRANCH="${1:-}"
case "$BRANCH" in
  deploy/*) ;;
  *) echo "::error::refusing to publish to '$BRANCH' — only deploy/* branches (a push to develop/main would run GitHub's CI)"; exit 1 ;;
esac

if [ -z "${HOOK:-}" ]; then
  echo "::notice::no deploy hook for $BRANCH — nothing will deploy, so nothing is published."
  exit 0
fi
if [ -z "${MIRROR_TOKEN:-}" ] || [ -z "${MIRROR_REPO:-}" ]; then
  echo "::error::a deploy hook is set but DEPLOY_MIRROR_TOKEN / DEPLOY_MIRROR_REPO are not — Render would rebuild a stale $BRANCH. Add them in Settings → Actions."
  exit 1
fi

SHA="$(git rev-parse HEAD)"
# The token travels in a header, never in the URL: git prints remote URLs in its errors.
AUTH="$(printf 'x-access-token:%s' "$MIRROR_TOKEN" | base64 -w0)"
echo "::add-mask::$AUTH"
git -c "http.https://github.com/.extraheader=AUTHORIZATION: basic $AUTH" \
  push --force "https://github.com/$MIRROR_REPO.git" "$SHA:refs/heads/$BRANCH"
echo "Published $SHA to $MIRROR_REPO@$BRANCH."

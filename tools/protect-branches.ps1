#Requires -Version 7.0
<#
.SYNOPSIS
  Protects a repo's develop and main branches on Forgejo (the primary forge) and, on request, on GitHub.

.DESCRIPTION
  Forgejo (the default, -Forge forgejo): develop and main become protected branches, which Forgejo never lets
  anyone force-push or delete. Pushes are allowed only for -PushUsers (default: the repo owner), so the CI job
  token - which Forgejo gives write access and does not let a workflow narrow - cannot push to them (v4 audit
  DEP-13/DEP-14, ADR-028). A pull request merges only when every -Checks job is green; each name becomes the
  pattern "CI / <name> *", which covers the push and pull_request runs and every matrix leg, and a skipped job
  counts as passed, so a docs-only pull request still merges. The rules bind admins too (the owner stays on the
  push list, so ordinary pushes still work). Re-running updates the rules in place.
  Needs FORGEJO_TOKEN in the environment (a token with admin rights on the repo); -ForgejoUrl defaults to
  http://localhost:3000.

  GitHub (-Forge github or both): develop needs -Checks green on an up-to-date branch, resolved conversations,
  no force pushes, no deletion; main the same plus a pull request (0 approvals - a solo maintainer). Admins are
  not bound. Needs the gh CLI signed in. GitHub names checks by job, so list matrix legs as they report
  ("e2e (1)"). A private repo on the free plan cannot be protected - GitHub answers 403, reported as a failure.

  A branch that does not exist is skipped. The script exits 1 if any branch could not be protected.

.EXAMPLE
  $env:FORGEJO_TOKEN = Get-Content ~/.config/forgejo/token
  ./tools/protect-branches.ps1 -Repo argamboad/my-app

.EXAMPLE
  ./tools/protect-branches.ps1 -Repo argamboad/my-app -Forge github -Checks build-test,secret-scan
#>
param(
    [Parameter(Mandatory)] [string] $Repo,
    [ValidateSet('forgejo', 'github', 'both')] [string] $Forge = 'forgejo',
    [string[]] $Checks = @('changes', 'secret-scan', 'qa-artifacts', 'build-test', 'license-scan', 'docker-build', 'native-build', 'e2e'),
    [string[]] $PushUsers = @(),
    [string] $ForgejoUrl = 'http://localhost:3000'
)

$ErrorActionPreference = 'Stop'
$script:failed = $false
if ($PushUsers.Count -eq 0) { $PushUsers = @($Repo.Split('/')[0]) }

function Fail([string] $message) {
    "  $message"
    $script:failed = $true
}

function Protect-Forgejo([string] $branch) {
    $api = "$($ForgejoUrl.TrimEnd('/'))/api/v1/repos/$Repo"
    $headers = @{ Authorization = "token $env:FORGEJO_TOKEN" }
    $call = @{ Headers = $headers; SkipHttpErrorCheck = $true; StatusCodeVariable = 'status'; ContentType = 'application/json' }

    $null = Invoke-RestMethod @call -Method Get -Uri "$api/branches/$branch"
    if ($status -eq 404) { "  forgejo $branch - no such branch, skipped"; return }
    if ($status -ne 200) { Fail "forgejo $branch - FAILED: reading the branch answered $status"; return }

    $rules = Invoke-RestMethod @call -Method Get -Uri "$api/branch_protections"
    if ($status -ne 200) { Fail "forgejo $branch - FAILED: listing branch protections answered $status (the token needs admin rights on the repo)"; return }
    $exists = @($rules | Where-Object { $_.rule_name -eq $branch }).Count -gt 0

    $body = [ordered]@{
        enable_push              = $true
        enable_push_whitelist    = $true
        push_whitelist_usernames = $PushUsers
        enable_status_check      = $Checks.Count -gt 0
        status_check_contexts    = @($Checks | ForEach-Object { "CI / $_ *" })
        apply_to_admins          = $true
        block_on_outdated_branch = $false
        required_approvals       = 0
    }
    if ($exists) {
        $null = Invoke-RestMethod @call -Method Patch -Uri "$api/branch_protections/$branch" -Body ($body | ConvertTo-Json -Depth 4)
    } else {
        $body.rule_name = $branch
        $null = Invoke-RestMethod @call -Method Post -Uri "$api/branch_protections" -Body ($body | ConvertTo-Json -Depth 4)
    }
    if ($status -notin 200, 201) { Fail "forgejo $branch - FAILED: saving the rule answered $status"; return }

    # Read it back: the branch must now report itself protected.
    $read = Invoke-RestMethod @call -Method Get -Uri "$api/branches/$branch"
    if ($status -ne 200 -or -not $read.protected) { Fail "forgejo $branch - FAILED: the rule was saved but the branch does not report protected"; return }
    "  forgejo $branch - protected (push: $($PushUsers -join ', '); checks: $($body.status_check_contexts -join ', '))"
}

function Protect-GitHub([string] $branch, [bool] $requirePr) {
    $null = gh api "repos/$Repo/branches/$branch" --silent 2>$null
    if ($LASTEXITCODE -ne 0) { "  github $branch - no such branch, skipped"; return }

    $body = [ordered]@{
        required_status_checks           = if ($Checks.Count) { @{ strict = $true; checks = @($Checks | ForEach-Object { @{ context = $_ } }) } } else { $null }
        enforce_admins                   = $false
        required_pull_request_reviews    = if ($requirePr) { @{ required_approving_review_count = 0; dismiss_stale_reviews = $true; require_code_owner_reviews = $false } } else { $null }
        restrictions                     = $null
        required_conversation_resolution = $true
        allow_force_pushes               = $false
        allow_deletions                  = $false
    }
    $file = New-TemporaryFile
    $body | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 $file
    $out = gh api -X PUT "repos/$Repo/branches/$branch/protection" --input $file 2>&1
    $code = $LASTEXITCODE
    Remove-Item $file
    if ($code -ne 0) { Fail "github $branch - FAILED: $($out | Select-Object -Last 1)"; return }
    "  github $branch - protected (checks: $(if ($Checks.Count) { $Checks -join ', ' } else { 'none' }); PR required: $requirePr)"
}

"$Repo"
if ($Forge -in 'forgejo', 'both') {
    if (-not $env:FORGEJO_TOKEN) { Fail 'forgejo - FAILED: set FORGEJO_TOKEN (a token with admin rights on the repo)' }
    else { Protect-Forgejo 'develop'; Protect-Forgejo 'main' }
}
if ($Forge -in 'github', 'both') {
    Protect-GitHub 'develop' $false
    Protect-GitHub 'main' $true
}
if ($script:failed) { exit 1 }

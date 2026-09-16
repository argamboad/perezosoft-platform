<#
.SYNOPSIS
  Protects a repo's develop and main branches the way jigger-jot's are protected.

.DESCRIPTION
  develop: the given checks must pass on an up-to-date branch; conversations resolved; no force pushes, no deletion.
  main:    the same, plus changes arrive through a pull request (0 approvals — a solo maintainer), stale approvals dismissed.
  Admins are not bound (enforce_admins false), so the owner can still bypass, as GitHub's "bypass rules" box shows.
  Branches that do not exist are skipped. Pass only checks the repo's CI actually reports on pull requests:
  a required check that never runs blocks every merge.

.EXAMPLE
  ./protect-branches.ps1 -Repo argamboad/jigger-jot -Checks build-test,e2e,secret-scan
#>
param(
    [Parameter(Mandatory)] [string] $Repo,
    [string[]] $Checks = @()
)

$ErrorActionPreference = 'Stop'

function Protect([string] $branch, [bool] $requirePr) {
    $exists = gh api "repos/$Repo/branches/$branch" --silent 2>$null; if ($LASTEXITCODE -ne 0) { "  $branch — no such branch, skipped"; return }

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
    if ($code -ne 0) { "  $branch — FAILED: $($out | Select-Object -Last 1)"; return }
    $checksText = if ($Checks.Count) { $Checks -join ', ' } else { 'none' }
    "  $branch — protected (checks: $checksText; PR required: $requirePr)"
}

"$Repo"
Protect 'develop' $false
Protect 'main' $true

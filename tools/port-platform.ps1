#Requires -Version 7.0
<#
.SYNOPSIS
  Ports the platform's changes into this app: a three-way merge per register file, and a report of what the platform
  changed in the rest (Arch A2, R160; Pkg 6.4).

.DESCRIPTION
  platform-stamp.json says which platform this app runs on and its brand map (platform token -> this app's token).

  An app on the packages (the stamp says "packages": true; ADR-034) was written by `dotnet new perezosoft-app` from
  starter/ in the platform, and its stamp's "commit" is the platform release it is on. The script reads starter/ at the
  release tag of that version (the base; -From names another ref when there is no tag, as for 0.0.0-platform) and at the
  target release (-To, default the PlatformPackagesVersion in this app's Directory.Packages.props), renames the
  starter's placeholder brand (AppBrand, appbrand, APPBRAND) to this app's in both, and merges every file the ownership
  map classes `register` into this app's copy with `git merge-file --diff3` (conflict markers are left for a human).
  `seed` files are never written: the ones the platform added, changed or deleted since the app's version are listed,
  for the app to take what it wants. Packaged source (`platform`) and release machinery (`producer`) found in the repo
  are listed for removal: a clone converting to the packages works through that list. No manifest is written (the
  package is the manifest, #450); the stamp gets the version and the date.

  A clone (no "packages" in its stamp; ADR-019) is synced to a platform commit. For every `register` and `seed` file of
  the platform's tree at the target (-To, default develop) the script merges the platform's change since the stamped
  commit into this repo's copy, the same way, then writes tests/Api.Tests/App/platform-manifest.json (the
  brand-normalised hash of every register and seed file at the target, with its class) and the stamp (the target commit
  and the date). PlatformOwnershipTests holds the clone to those two files. Packaged source is not ported into a clone
  any more: a clone converts to the packages (#423). A map from before Pkg 6.4 (`platform`, `adapts`) reads as
  `register` and `seed`.

  Without -Apply the script prints the plan and writes nothing.

.PARAMETER Platform
  Path to a perezosoft-platform checkout with its tags (any branch; the script reads commits, not the working tree).

.PARAMETER To
  On the packages: the platform release to port to (its tag v<To> must exist). A clone: the platform commit or branch.

.PARAMETER From
  On the packages: the platform ref to read the app's starter from, when its version has no tag.

.PARAMETER Apply
  Write the merged files and the stamp (a clone: and the manifest). Without it, report only.

.PARAMETER Repo
  This app's repo root. Default: the parent of tools/.

.EXAMPLE
  pwsh tools/port-platform.ps1 -Platform ..\perezosoft-platform
  pwsh tools/port-platform.ps1 -Platform ..\perezosoft-platform -To 1.0.0-beta.1 -Apply
  dotnet platform port -Platform ..\perezosoft-platform -From 0fd0d76 -Apply
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Platform,
    [string]$To,
    [string]$From,
    [switch]$Apply,
    [string]$Repo = $(if ($env:PLATFORM_REPO_ROOT) { $env:PLATFORM_REPO_ROOT } else { Split-Path -Parent $PSScriptRoot })
)

$ErrorActionPreference = 'Stop'
$Platform = (Resolve-Path $Platform).Path
$Repo = (Resolve-Path $Repo).Path

# --- git plumbing, binary-safe ------------------------------------------------------------------------------------------
function Git-Text([string]$repo, [string[]]$arguments) {
    $out = & git -C $repo @arguments 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($out -join "`n")
}
function Git-Bytes([string]$repo, [string]$spec) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new('git')
    foreach ($a in @('-C', $repo, 'cat-file', 'blob', $spec)) { $psi.ArgumentList.Add($a) }
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($psi)
    $ms = [System.IO.MemoryStream]::new()
    $p.StandardOutput.BaseStream.CopyTo($ms)
    $p.StandardError.ReadToEnd() | Out-Null
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { return $null }
    return $ms.ToArray()
}
function Resolve-Commit([string]$ref) { return Git-Text $Platform @('rev-parse', '--verify', '--quiet', "$ref^{commit}") }
function Tree-Files([string]$commit, [string]$under) {
    $all = (Git-Text $Platform @('ls-tree', '-r', '--name-only', $commit)) -split "`n" | Where-Object { $_ }
    if (-not $under) { return @($all) }
    return @($all | Where-Object { $_.StartsWith("$under/") } | ForEach-Object { $_.Substring($under.Length + 1) })
}

# --- the stamp, the brand map, the mode -----------------------------------------------------------------------------------
$stampPath = Join-Path $Repo 'platform-stamp.json'
if (-not (Test-Path $stampPath)) { throw "$stampPath is missing: this repo carries no platform stamp" }
# -AsHashtable: the brand map's keys differ only by case ("Perezosoft", "perezosoft"), which a PSObject cannot hold.
$stamp = Get-Content $stampPath -Raw | ConvertFrom-Json -AsHashtable
$onPackages = $stamp['packages'] -eq $true
# A case-SENSITIVE map: [ordered]@{} would fold "Perezosoft" and "perezosoft" into one key.
$brand = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
foreach ($k in $stamp['brand'].Keys) { $brand[$k] = [string]$stamp['brand'][$k] }
$appTokens = @($brand.Values)

# What the source spells and this app spells otherwise. A clone renames the platform's brand; an app on the packages
# renames the starter's placeholder, as dotnet new did (PlatformOwnership.PlaceholderBrand): the starter names the
# packages' namespaces (Perezosoft.Infrastructure), which every app keeps.
$renames = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
$source = $null   # the folder of the platform's tree an app is made from: starter/ on the packages, the root for a clone
if ($onPackages) {
    $source = 'starter'
    if (-not $To) {
        $props = Join-Path $Repo 'Directory.Packages.props'
        $m = if (Test-Path $props) { [regex]::Match((Get-Content $props -Raw), '<PlatformPackagesVersion>([^<]+)</PlatformPackagesVersion>') } else { $null }
        if (-not $m -or -not $m.Success) { throw "no -To, and $props names no PlatformPackagesVersion" }
        $To = $m.Groups[1].Value
    }
    $version = $To
    $target = Resolve-Commit "refs/tags/v$version"
    if (-not $target) { throw "'v$version' is not a release tag in $Platform (git fetch --tags), so '$version' is not a release to port to" }
    $fromVersion = [string]$stamp['commit']
    if ($fromVersion -eq $version -and -not $From) { Write-Host "Already on platform $version. Nothing to port."; exit 0 }
    $base = if ($From) { Resolve-Commit $From } else { Resolve-Commit "refs/tags/v$fromVersion" }
    if ($From -and -not $base) { throw "'$From' is not a commit in $Platform" }
    if (-not $base) { Write-Host "No release tag v$fromVersion in ${Platform}: every register file merges without a base (-From <ref> names one)." }
} else {
    if ($From) { throw '-From applies to an app on the packages; a clone ports from its stamped commit' }
    if (-not $To) { $To = 'develop' }
    $target = Resolve-Commit $To
    if (-not $target) { throw "'$To' is not a commit in $Platform" }
    $base = $stamp['commit']
    if ($base -and $base -eq $target) { Write-Host "Already at platform $target. Nothing to port."; exit 0 }
}
$templateJson = if ($onPackages) { Git-Text $Platform @('show', "${target}:starter/.template.config/template.json") } else { $null }
if ($onPackages -and -not $templateJson) { throw "the platform at v$version has no starter/ (it predates Pkg 6.1): convert by hand" }
if ($onPackages) {
    $placeholder = [string]($templateJson | ConvertFrom-Json).sourceName
    foreach ($k in $brand.Keys) {
        if ([char]::IsUpper($k[0])) { $renames[$placeholder] = $brand[$k]; $renames[$placeholder.ToUpperInvariant()] = $brand[$k].ToUpperInvariant() }
        else { $renames[$placeholder.ToLowerInvariant()] = $brand[$k] }
    }
} else {
    foreach ($k in $brand.Keys) { $renames[$k] = $brand[$k] }
}

# Tokens the rename never rewrites. The platform repo's own name ("perezosoft-platform") carries the brand token but is
# a name, not the brand, or every file that names the platform would read "<app>-platform" downstream. And the map's
# keep-list (Pkg 2.5, read below from the ownership map AT THE TARGET): the platform's package ids (Perezosoft.Platform.*)
# and its feed key keep their names in every app (ADR-034). PlatformOwnership.LocalPath honours the same list.
$keepName = ([string]$stamp['platform']).Split('/')[-1]
$keepTokens = [System.Collections.Generic.List[string]]::new()
function Rebrand([string]$text) {
    # Each kept token is parked behind a private-use character the text does not already contain (a file may hold such
    # characters itself: the ownership tests do), then restored after the rename ran.
    $guards = @{}
    $c = 0xE000
    foreach ($t in (@($keepName) + @($keepTokens) | Where-Object { $_ })) {
        while ($text.Contains([string][char]$c)) { $c++ }
        $g = [string][char]$c++; $guards[$g] = $t; $text = $text.Replace($t, $g)
    }
    foreach ($k in ($renames.Keys | Sort-Object { $_.Length } -Descending)) { $text = $text.Replace($k, [string]$renames[$k]) }
    foreach ($g in $guards.Keys) { $text = $text.Replace($g, $guards[$g]) }
    return $text
}

# --- the ownership map at the target ------------------------------------------------------------------------------------
function Glob-ToRegex([string]$glob) {
    $sb = [System.Text.StringBuilder]::new('^')
    for ($i = 0; $i -lt $glob.Length; $i++) {
        if ($i + 2 -lt $glob.Length -and $glob.Substring($i, 3) -eq '**/') { [void]$sb.Append('(?:.*/)?'); $i += 2; continue }
        if ($i + 1 -lt $glob.Length -and $glob.Substring($i, 2) -eq '**') { [void]$sb.Append('.*'); $i += 1; continue }
        $c = $glob[$i]
        if ($c -eq '*') { [void]$sb.Append('[^/]*') } elseif ($c -eq '?') { [void]$sb.Append('[^/]') } else { [void]$sb.Append([regex]::Escape([string]$c)) }
    }
    return [regex]::new($sb.Append('$').ToString())
}
$mapJson = Git-Text $Platform @('show', "${target}:platform-ownership.json")
if (-not $mapJson) { throw "the platform at $target has no platform-ownership.json (it predates Arch A2)" }
$mapDoc = $mapJson | ConvertFrom-Json
# A map from before Pkg 6.4 named what was held `platform` and what an app edits `adapts`.
$legacy = -not (@($mapDoc.rules.class) -contains 'register')
$rules = @($mapDoc.rules | ForEach-Object {
    $class = if ($legacy -and $_.class -eq 'platform') { 'register' } elseif ($legacy -and $_.class -eq 'adapts') { 'seed' } else { $_.class }
    [pscustomobject]@{ Regex = (Glob-ToRegex $_.glob); Class = $class }
})
if ($mapDoc.PSObject.Properties.Name -contains 'keep') { foreach ($k in $mapDoc.keep) { $keepTokens.Add([string]$k) } }
function Classify([string]$path) {
    foreach ($r in $rules) { if ($r.Regex.IsMatch($path)) { return $r.Class } }
    return $null
}

# --- the hash both sides compare (PlatformOwnership.NormalizedHash in C#; keep the two identical) -------------------------
function Normalized-Hash([byte[]]$bytes, [string[]]$tokens) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $probe = [Math]::Min(8000, $bytes.Length)
    for ($i = 0; $i -lt $probe; $i++) { if ($bytes[$i] -eq 0) { return ([System.Convert]::ToHexString($sha.ComputeHash($bytes))).ToLowerInvariant() } }
    $start = 0
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $start = 3 }
    $text = [System.Text.Encoding]::UTF8.GetString($bytes, $start, $bytes.Length - $start).Replace("`r`n", "`n")
    foreach ($t in ($tokens | Sort-Object { $_.Length } -Descending)) {
        $ph = if ([char]::IsUpper($t[0])) { '{BRAND}' } else { '{brand}' }
        $text = $text.Replace($t, $ph)
    }
    $lines = $text.Split("`n") | ForEach-Object { $_.TrimEnd(' ', "`t") }
    $text = ([string]::Join("`n", $lines)).TrimEnd("`n", ' ', "`t")
    return ([System.Convert]::ToHexString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text)))).ToLowerInvariant()
}
function Bytes-Equal([byte[]]$a, [byte[]]$b) {
    if ($null -eq $a -or $null -eq $b) { return $false }
    return ([System.Convert]::ToHexString($a)) -eq ([System.Convert]::ToHexString($b))
}
function Is-Binary([byte[]]$bytes) {
    $probe = [Math]::Min(8000, $bytes.Length)
    for ($i = 0; $i -lt $probe; $i++) { if ($bytes[$i] -eq 0) { return $true } }
    return $false
}
function Decode([byte[]]$bytes) {
    $start = 0
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $start = 3 }
    return [System.Text.Encoding]::UTF8.GetString($bytes, $start, $bytes.Length - $start).Replace("`r`n", "`n")
}
function Spec([string]$commit, [string]$path) { if ($source) { return "${commit}:$source/$path" } else { return "${commit}:$path" } }

# One file's three-way merge into this repo: the plan item (added / same / clean / no-op / CONFLICT / BINARY).
function Merge-Into([string]$path, [byte[]]$theirs, [byte[]]$baseBytes, [string]$tmp) {
    $local = Rebrand $path
    $localFull = Join-Path $Repo $local
    $ours = if (Test-Path $localFull) { [System.IO.File]::ReadAllBytes($localFull) } else { $null }
    if (Is-Binary $theirs) {
        if ($null -eq $ours) { return [pscustomobject]@{ Verb = 'added'; Path = $local; Bytes = $theirs } }
        if (Bytes-Equal $ours $theirs) { return [pscustomobject]@{ Verb = 'same'; Path = $local } }
        if (Bytes-Equal $ours $baseBytes) { return [pscustomobject]@{ Verb = 'clean'; Path = $local; Bytes = $theirs } }
        return [pscustomobject]@{ Verb = 'BINARY'; Path = $local }
    }
    $theirsText = Rebrand (Decode $theirs)
    if ($null -eq $ours) { return [pscustomobject]@{ Verb = 'added'; Path = $local; Text = $theirsText; Crlf = $false; Bom = $false } }
    $crlf = [System.Text.Encoding]::UTF8.GetString($ours).Contains("`r`n")
    $bom = $ours.Length -ge 3 -and $ours[0] -eq 0xEF -and $ours[1] -eq 0xBB -and $ours[2] -eq 0xBF
    $oursText = Decode $ours
    if ($oursText -eq $theirsText) { return [pscustomobject]@{ Verb = 'same'; Path = $local } }
    $baseText = if ($null -ne $baseBytes) { Rebrand (Decode $baseBytes) } else { '' }
    $fo = Join-Path $tmp 'ours'; $fb = Join-Path $tmp 'base'; $ft = Join-Path $tmp 'theirs'
    [System.IO.File]::WriteAllText($fo, $oursText, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($fb, $baseText, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($ft, $theirsText, [System.Text.UTF8Encoding]::new($false))
    & git merge-file --diff3 -L app -L platform-before -L platform $fo $fb $ft 2>$null | Out-Null
    $conflicts = $LASTEXITCODE
    $merged = [System.IO.File]::ReadAllText($fo)
    if ($conflicts -lt 0) { throw "git merge-file failed on $local" }
    $verb = if ($conflicts -gt 0) { "CONFLICT x$conflicts" } elseif ($merged -eq $oursText) { 'no-op' } else { 'clean' }
    if ($null -eq $baseBytes -and $conflicts -gt 0) { $verb = "CONFLICT (no base: first port)" }
    return [pscustomobject]@{ Verb = $verb; Path = $local; Text = $merged; Crlf = $crlf; Bom = $bom }
}

# --- the plan ----------------------------------------------------------------------------------------------------------
$skip = { param($p) $onPackages -and $p.StartsWith('.template.config/') }   # the template's own, never in an app
$targetFiles = @(Tree-Files $target $source | Where-Object { -not (& $skip $_) })
$fromFiles = if ($base) { @(Tree-Files $base $source | Where-Object { -not (& $skip $_) }) } else { @() }
$plan = [System.Collections.Generic.List[object]]::new()
$manifest = [System.Collections.Generic.List[object]]::new()
$packagedSkipped = 0
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("port-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    foreach ($path in $targetFiles) {
        $class = Classify $path
        if (-not $class) { $plan.Add([pscustomobject]@{ Verb = 'UNCLASSIFIED'; Path = $path }); continue }
        if ($class -in @('app', 'sample')) { continue }
        if ($class -eq 'platform') { $packagedSkipped++; continue }   # packaged source: an app takes it as a package
        # Producer-only (Pkg 2.5): the publisher's release machinery. Never ported, never in the manifest. A copy an earlier
        # port left in a clone is reported for removal (the tool never deletes; an app on the packages is scanned below).
        if ($class -eq 'producer') {
            if (-not $onPackages -and (Test-Path (Join-Path $Repo (Rebrand $path)))) { $plan.Add([pscustomobject]@{ Verb = 'producer-only: remove'; Path = (Rebrand $path) }) }
            continue
        }
        $theirs = Git-Bytes $Platform (Spec $target $path)
        $baseBytes = if ($base -and ($fromFiles -contains $path)) { Git-Bytes $Platform (Spec $base $path) } else { $null }
        if (-not $onPackages) {
            # Hashed as this repo holds it: the platform's text after the rename, normalised with this repo's tokens. The
            # platform's own text normalised with the platform's tokens could never match where it already names this app.
            $asHeld = if (Is-Binary $theirs) { $theirs } else { [System.Text.Encoding]::UTF8.GetBytes((Rebrand (Decode $theirs))) }
            $manifest.Add([ordered]@{ path = $path; class = $class; hash = (Normalized-Hash $asHeld $appTokens) })
        }
        if ($onPackages -and $class -eq 'seed') {
            # The app's after dotnet new: never written, only what the platform changed in it since this app's version.
            if ($null -eq $baseBytes -and $base) { $plan.Add([pscustomobject]@{ Verb = 'seed: added upstream'; Path = (Rebrand $path) }) }
            elseif ($null -ne $baseBytes -and -not (Bytes-Equal $baseBytes $theirs)) { $plan.Add([pscustomobject]@{ Verb = 'seed: changed upstream'; Path = (Rebrand $path) }) }
            continue
        }
        $plan.Add((Merge-Into $path $theirs $baseBytes $tmp))
    }
    foreach ($gone in ($fromFiles | Where-Object { $targetFiles -notcontains $_ })) {
        $class = Classify $gone
        if ($class -eq 'register' -or (-not $onPackages -and $class -eq 'seed')) { $plan.Add([pscustomobject]@{ Verb = 'deleted upstream'; Path = (Rebrand $gone) }) }
        elseif ($class -eq 'seed') { $plan.Add([pscustomobject]@{ Verb = 'seed: deleted upstream'; Path = (Rebrand $gone) }) }
    }
    if ($onPackages) {
        # What an app on the packages must not carry: the packaged source and the release machinery it takes as packages,
        # and a clone's manifest (the package is the manifest). The checklist of a clone converting to the packages.
        $tracked = if (Test-Path (Join-Path $Repo '.git')) { (Git-Text $Repo @('ls-files')) -split "`n" | Where-Object { $_ } }
                   else { Get-ChildItem $Repo -Recurse -File | ForEach-Object { [System.IO.Path]::GetRelativePath($Repo, $_.FullName).Replace('\', '/') } |
                          Where-Object { $_ -notmatch '(^|/)(bin|obj|\.git|node_modules)/' } }
        foreach ($path in $tracked) {
            $class = Classify $path
            if ($class -eq 'platform') { $plan.Add([pscustomobject]@{ Verb = 'packaged: remove'; Path = $path }) }
            elseif ($class -eq 'producer') { $plan.Add([pscustomobject]@{ Verb = 'producer-only: remove'; Path = $path }) }
        }
        if (Test-Path (Join-Path $Repo 'tests/Api.Tests/App/platform-manifest.json')) {
            $plan.Add([pscustomobject]@{ Verb = 'clone manifest: remove'; Path = 'tests/Api.Tests/App/platform-manifest.json' })
        }
    }
}
finally { try { Remove-Item -Recurse -Force $tmp -ErrorAction Stop } catch { <# a scratch dir; the OS temp sweep gets it #> } }

# --- report, then apply ----------------------------------------------------------------------------------------------
$counts = $plan | Group-Object { ($_.Verb -split '[ :]')[0] } | ForEach-Object { "$($_.Name)=$($_.Count)" }
$fromLabel = if ($onPackages) { if ($From) { "$fromVersion (from $From)" } else { $fromVersion } } elseif ($base) { $base.Substring(0, 7) } else { '(no stamp)' }
$toLabel = if ($onPackages) { $version } else { $target.Substring(0, 7) }
Write-Host ("Port {0} -> {1}: {2}" -f $fromLabel, $toLabel, ($counts -join ', '))
foreach ($item in ($plan | Where-Object { $_.Verb -ne 'same' -and $_.Verb -ne 'no-op' })) { Write-Host ("  {0,-30} {1}" -f $item.Verb, $item.Path) }
if (-not $onPackages -and $packagedSkipped -gt 0) {
    Write-Host ("  {0} packaged file(s) not ported: a clone converts to the packages (dotnet new perezosoft-app, NEW_APP_GUIDE)." -f $packagedSkipped)
}
if (-not $Apply) {
    Write-Host ('Dry run: nothing written. Add -Apply to merge, and to write the {0}.' -f $(if ($onPackages) { 'stamp' } else { 'manifest and the stamp' }))
    exit 0
}

$written = @('added', 'clean')
foreach ($item in $plan) {
    if ($item.Verb -notin $written -and $item.Verb -notlike 'CONFLICT*') { continue }
    $full = Join-Path $Repo $item.Path
    New-Item -ItemType Directory -Force (Split-Path -Parent $full) | Out-Null
    if ($item.PSObject.Properties.Name -contains 'Bytes' -and $null -ne $item.Bytes) {
        [System.IO.File]::WriteAllBytes($full, $item.Bytes)
    } else {
        $text = if ($item.Crlf) { $item.Text.Replace("`n", "`r`n") } else { $item.Text }
        [System.IO.File]::WriteAllText($full, $text, [System.Text.UTF8Encoding]::new([bool]$item.Bom))
    }
}

if (-not $onPackages) {
    $manifestPath = Join-Path $Repo 'tests/Api.Tests/App/platform-manifest.json'
    New-Item -ItemType Directory -Force (Split-Path -Parent $manifestPath) | Out-Null
    $manifestDoc = [ordered]@{
        '$comment' = @(
            'Written by tools/port-platform.ps1 (Arch A2): the brand-normalised hash of every register and seed file at the',
            'platform commit this repo is synced to. PlatformOwnershipTests compares this repo against it. Do not edit by hand.')
        platform = [string]$stamp['platform']
        commit = $target
        files = @($manifest | Sort-Object { $_.path })
    }
    [System.IO.File]::WriteAllText($manifestPath, ($manifestDoc | ConvertTo-Json -Depth 5), [System.Text.UTF8Encoding]::new($false))
}
$stampDoc = [ordered]@{}
if ($stamp.ContainsKey('$comment')) { $stampDoc['$comment'] = $stamp['$comment'] }
$stampDoc['platform'] = [string]$stamp['platform']
$stampDoc['commit'] = if ($onPackages) { $version } else { $target }
$stampDoc['date'] = (Get-Date).ToString('yyyy-MM-dd')
if ($onPackages) { $stampDoc['packages'] = $true }
$stampDoc['brand'] = $brand
[System.IO.File]::WriteAllText($stampPath, ($stampDoc | ConvertTo-Json -Depth 5), [System.Text.UTF8Encoding]::new($false))

$conflicted = @($plan | Where-Object { $_.Verb -like 'CONFLICT*' -or $_.Verb -in @('BINARY', 'deleted upstream', 'producer-only: remove', 'packaged: remove', 'clone manifest: remove') })
$seeds = @($plan | Where-Object { $_.Verb -like 'seed:*' })
$writtenCount = @($plan | Where-Object { $_.Verb -in $written -or $_.Verb -like 'CONFLICT*' }).Count
if ($onPackages) { Write-Host ("Wrote {0} register file(s) and the stamp ({1})." -f $writtenCount, $version) }
else { Write-Host ("Wrote {0} file(s), the manifest ({1} entries) and the stamp ({2})." -f $writtenCount, $manifest.Count, $target.Substring(0, 7)) }
if ($conflicted.Count -gt 0) {
    Write-Host ("{0} file(s) need a hand: resolve the <<<<<<< markers, copy the binaries, delete what the platform deleted and whatever is listed for removal." -f $conflicted.Count)
}
if ($seeds.Count -gt 0) {
    Write-Host ("{0} seed file(s) changed in the platform's starter since this app's version: yours to take or leave (diff starter/ between the two releases)." -f $seeds.Count)
}
if ($onPackages) { Write-Host 'Then bump PlatformPackagesVersion if -To differs from it, build and run the tests: the ownership suite holds the register to the package.' }
else { Write-Host 'Then build and run the tests: PlatformOwnershipTests holds this repo to the manifest.' }
exit 0

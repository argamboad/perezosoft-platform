# P8: protect-branches.ps1 lines 20/23/36 and publish-native.ps1 lines 25/61 — a native command writing to
# stderr under $ErrorActionPreference='Stop' with 2>$null / 2>&1. Run under BOTH hosts.
$ErrorActionPreference = 'Stop'
"host: $($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)"
try {
  $exists = cmd /c "echo gh: warning to stderr 1>&2 & exit 1" 2>$null; if ($LASTEXITCODE -ne 0) { "  line-23 shape: '2>`$null' -> continued, LASTEXITCODE=$LASTEXITCODE (branch would be 'skipped')" }
} catch { "  line-23 shape: '2>`$null' -> TERMINATED: $($_.Exception.GetType().Name)" }
try {
  $out = cmd /c "echo HTTP 403 forbidden 1>&2 & exit 1" 2>&1; $code = $LASTEXITCODE
  "  line-36 shape: '2>&1' -> continued, code=$code, last line='$($out | Select-Object -Last 1)'"
} catch { "  line-36 shape: '2>&1' -> TERMINATED: $($_.Exception.GetType().Name)" }
try {
  $v = cmd /c "echo Verified using v2 scheme (APK Signature Scheme v2): false & echo some-warning 1>&2" 2>&1 | Select-String "Verified using v[123] "
  "  publish-native line-61 shape: continued; Select-String printed: '$v' (a 'false' line is printed, not asserted - NAT-13)"
} catch { "  publish-native line-61 shape: TERMINATED: $($_.Exception.GetType().Name)" }

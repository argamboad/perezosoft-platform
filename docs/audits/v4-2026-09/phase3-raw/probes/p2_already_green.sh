#!/usr/bin/env bash
# P2: deploy.yml lines 53-78 (already-green verification) against fixture task lists. Real field shapes
# were confirmed by a read-only GET of /actions/tasks on 2026-09-23 (status "success", names "e2e (1)"…).
set -e   # deploy.yml runs `bash -e {0}` — no pipefail
SHA=deadbeef; export SHA
mk() { # $1 = file, rest = "name|status" pairs; all for env.SHA / ci.yml unless prefixed with sha=
  local f=$1; shift; : > "$f"
  for e in "$@"; do
    local sha=$SHA wf=ci.yml n s
    case "$e" in sha=*) sha=${e#sha=}; sha=${sha%%:*}; e=${e#*:};; esac
    case "$e" in wf=*) wf=${e#wf=}; wf=${wf%%:*}; e=${e#*:};; esac
    n=${e%|*}; s=${e#*|}
    printf '{"name":"%s","status":"%s","head_sha":"%s","workflow_id":"%s"}\n' "$n" "$s" "$sha" "$wf" >> "$f"
  done
}
verify() { # verbatim lines 63-78 with /tmp/jobs.json = $1
  local J=$1
  green() { jq -r --arg n "$1" 'select(.name == $n and .status == "success") | .name' "$J" | head -1; }
  missing=""
  for job in changes build-test secret-scan qa-artifacts license-scan docker-build; do
    [ -n "$(green "$job")" ] || missing="$missing $job"
  done
  natives=$(jq -r 'select(.name | startswith("native-build (")) | select(.status == "success") | .name' "$J" | sort -u | wc -l)
  [ "$natives" -ge 2 ] || missing="$missing native-build($natives/2)"
  shards=$(jq -r 'select(.name | startswith("e2e (")) | select(.status == "success") | .name' "$J" | sort -u | wc -l)
  [ "$shards" -ge 3 ] || missing="$missing e2e($shards/3)"
  if [ -n "$missing" ]; then echo "REFUSE:$missing"; else echo "DEPLOY"; fi
}
GATES="changes|success build-test|success secret-scan|success qa-artifacts|success license-scan|success docker-build|success"
NB="native-build (ubuntu-latest, net10.0-android)|success native-build (windows-latest, net10.0-windows10.0.19041.0)|success"
E3="e2e (1)|success e2e (2)|success e2e (3)|success"
T=$(mktemp -d)
mk $T/a.json $GATES $NB $E3;                                   echo "A all green:                          $(verify $T/a.json)"
mk $T/b.json $GATES $NB $E3 "e2e (4)|failure";                 echo "B 4-shard matrix, shard 4 RED:        $(verify $T/b.json)"
mk $T/c.json $GATES $NB $E3 "native-build (macos-latest, net10.0-ios)|failure"; echo "C 3-leg native matrix, ios RED:       $(verify $T/c.json)"
mk $T/d.json $GATES $NB $E3 "build-test|failure";              echo "D build-test green THEN red re-run:   $(verify $T/d.json)   (DEP-19, filed)"
mk $T/e.json $GATES $NB $E3 "native-smoke-android|failure";    echo "E selected smoke RED:                 $(verify $T/e.json)   (DEP-19, filed)"
mk $T/f.json "${GATES/build-test|success/build-and-test|success}" $NB $E3; echo "F build-test renamed build-and-test:  $(verify $T/f.json)"
mk $T/g.json $GATES $NB "e2e-1|success e2e-2|success e2e-3|success"; echo "G e2e matrix renamed e2e-N:           $(verify $T/g.json)"
: > $T/h.json;                                                 echo "H empty task list:                    $(verify $T/h.json)"
mk $T/i.json "wf=nightly.yml:build-test|success" $GATES $NB $E3; echo "I gate green in another workflow only (filtered upstream by workflow_id): n/a — the jq filter on line 59 keys on workflow_id"
mk $T/j.json $GATES $NB $E3 "changes|running";                 echo "J re-run in progress alongside old green: $(verify $T/j.json)"
# set -e + the pagination loop's last statement `[ … ] && break` on a full last page:
n=0; for page in 1 2 3 4 5; do n=$((n+1)); [ 50 -lt 50 ] && break; done; echo "K set -e survives the loop whose last [ ] is false: yes (n=$n)"
echo "L wc -l output on this bash: '$(printf 'a\nb\n' | sort -u | wc -l)'"

#!/usr/bin/env bash
# P4: the e2e shard step (ci.yml lines 1091-1108) against a synthetic `--list-tests` output.
set -e
list=$'The following Tests are available:\r\n    Alpha_Journey\r\n    Beta_Journey(arg1)\r\n    Beta_Journey(arg2)\r\n    Gamma\r\n    Delta_Journey\r\n    Epsilon_Journey\r\n    Zeta_Journey\r\n    Eta_Journey\r\n    Theta_Journey\r\n    Iota_Journey\r\n    Kappa_Journey\r\n    Lambda_Journey\r\n    Delta_Journey_Extended\r\n'
names=$(printf '%s' "$list" | tr -d '\r' | sed -n 's/^    \([A-Za-z0-9_]\{4,\}\).*$/\1/p' | sort -u)
total=$(echo "$names" | grep -c .)
echo "total=$total (parameterised Beta collapsed to one name; 'Gamma' kept — 5 chars)"
all=""
for s in 1 2 3; do
  mine=$(echo "$names" | awk -v s=$s -v n=3 'NR % n == s % n')
  filter=$(echo "$mine" | sed 's/^/FullyQualifiedName~/' | paste -sd'|')
  echo "shard $s: $(echo "$mine" | grep -c .) names -> filter=$filter"
  all="$all"$'\n'"$mine"
done
echo "partition complete & disjoint: $( [ "$(echo "$all" | grep -c .)" = "$total" ] && [ "$(echo "$all" | grep . | sort -u | wc -l)" = "$total" ] && echo yes || echo NO)"
echo "substring hazard (DEP-22): 'Delta_Journey' shard filter also matches Delta_Journey_Extended -> that journey runs in two shards"
echo "--- an empty --list-tests (build broke) without pipefail:"
names=$(false | tr -d '\r' | sed -n 's/^    \([A-Za-z0-9_]\{4,\}\).*$/\1/p' | sort -u); total=$(echo "$names" | grep -c .); echo "total=$total -> the '-ge 10' guard fails the step loudly: $( [ "$total" -ge 10 ] || echo yes)"

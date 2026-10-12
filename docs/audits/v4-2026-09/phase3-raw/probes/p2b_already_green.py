# P2b: python re-implementation of deploy.yml lines 63-73 (jq absent on this host). Semantics are 1:1:
#   green(n)  = any task with name==n and status=="success"          (jq select | head -1)
#   natives   = |{name : name startswith "native-build (" and success}| ; shards likewise for "e2e ("
def verify(tasks):
    green = lambda n: any(t["name"] == n and t["status"] == "success" for t in tasks)
    missing = [j for j in "changes build-test secret-scan qa-artifacts license-scan docker-build".split() if not green(j)]
    natives = len({t["name"] for t in tasks if t["name"].startswith("native-build (") and t["status"] == "success"})
    shards  = len({t["name"] for t in tasks if t["name"].startswith("e2e (") and t["status"] == "success"})
    if natives < 2: missing.append(f"native-build({natives}/2)")
    if shards < 3: missing.append(f"e2e({shards}/3)")
    return "REFUSE: " + " ".join(missing) if missing else "DEPLOY"
T = lambda n, s="success": {"name": n, "status": s}
GATES = [T(j) for j in "changes build-test secret-scan qa-artifacts license-scan docker-build".split()]
NB = [T("native-build (ubuntu-latest, net10.0-android)"), T("native-build (windows-latest, net10.0-windows10.0.19041.0)")]
E3 = [T(f"e2e ({i})") for i in (1, 2, 3)]
cases = {
 "A all green": GATES+NB+E3,
 "B 4-shard matrix, shard 4 RED": GATES+NB+E3+[T("e2e (4)","failure")],
 "C 3-leg native matrix, ios RED": GATES+NB+E3+[T("native-build (macos-latest, net10.0-ios)","failure")],
 "D build-test green THEN red re-run (DEP-19)": GATES+NB+E3+[T("build-test","failure")],
 "E selected native smoke RED (DEP-19)": GATES+NB+E3+[T("native-smoke-android","failure")],
 "F build-test renamed build-and-test": [t for t in GATES if t["name"]!="build-test"]+[T("build-and-test")]+NB+E3,
 "G e2e legs renamed e2e-N": GATES+NB+[T("e2e-1"),T("e2e-2"),T("e2e-3")],
 "H empty task list": [],
 "J re-run of changes in progress beside the old green": GATES+NB+E3+[T("changes","running")],
 "M docs-only run (gates skipped)": [T("changes")]+[T(j,"skipped") for j in "build-test secret-scan qa-artifacts license-scan docker-build".split()],
}
for k, v in cases.items(): print(f"{k:55s} -> {verify(v)}")

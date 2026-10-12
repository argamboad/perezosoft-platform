# P5: verbatim "Slowest journeys" step (ci.yml lines 1117-1139) against a .trx with zero results.
import glob, xml.etree.ElementTree as ET, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
files = glob.glob("e2e-results/*.trx")
if not files:
    print("no .trx written — nothing to report"); raise SystemExit
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
rows = []
for r in ET.parse(files[0]).getroot().findall(".//t:UnitTestResult", ns):
    d = r.get("duration") or "0:00:00"
    h, m, s = d.split(":")
    rows.append((float(h) * 3600 + float(m) * 60 + float(s), r.get("testName", "?"), r.get("outcome", "?")))
rows.sort(reverse=True)
total = sum(x[0] for x in rows)
print(f"{len(rows)} journeys, {total/60:.1f} min of test time (wall clock is lower only if they run in parallel)")
slow = sum(x[0] for x in rows[:5])
print(f"\nthe 5 slowest are {slow/total*100:.0f}% of the suite's time")

import json
import re
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]")

UNIMPL = [
    "Criticism", "BadDream", "Panic", "SleepDeprivation", "AnxietyWave", "MentalFatigue",
    "ShadowParanoia", "FreezeResponse", "Isolation", "Breakdown", "Collapse", "Numbness",
    "Despair", "Critical",
]

disabled = {}
for name in UNIMPL:
    disabled[f"topicStress{name}"] = None
    disabled[f"topicStressTreatment{name}Followup"] = None
    disabled[f"topicStressTreatment{name}Cured"] = None
    disabled[f"topicStressTreatment{name}Started"] = None

patch = {
    "$schema": "https://smapi.io/schemas/content-patcher.json",
    "Format": "2.7.0",
    "Changes": [{
        "Action": "EditData",
        "Priority": "Late + 10",
        "Target": "Characters/Dialogue/Harvey",
        "LogName": "HarveyStressMeter: disable unimplemented stress topics",
        "Entries": disabled,
    }],
}

out = CP / "assets" / "Code" / "stressTopicsUnimplementedDisabled.json"
out.write_text(json.dumps(patch, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"Wrote {out} ({len(disabled)} entries)")

started_hits = []
for p in (CP / "assets").rglob("*.json"):
    if p.name == "stressTopicsUnimplementedDisabled.json":
        continue
    text = p.read_text(encoding="utf-8")
    for m in re.finditer(r"topicStressTreatment\w+Started", text):
        started_hits.append((str(p.relative_to(CP)), m.group(0)))

print(f"Started key hits in CP assets: {len(started_hits)}")
for hit in started_hits:
    print(" ", hit)

for rel in [
    "assets/Code/dialoguesHarveyCureStress.json",
    "assets/Code/dialoguesHarveyStress.json",
    "assets/Code/stressTopicsUnimplementedDisabled.json",
]:
    json.loads((CP / rel).read_text(encoding="utf-8"))
    print(f"JSON OK: {rel}")

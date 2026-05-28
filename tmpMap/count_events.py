import re
from pathlib import Path

base = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
ids: dict[str, set[str]] = {}
for fn in ["events.json", "eventsCare.json", "eventsMineRescue.json"]:
    text = (base / fn).read_text(encoding="utf-8")
    text = re.sub(r"//[^\n]*", "", text)
    for m in re.finditer(r'"([A-Za-z0-9_.]+)/', text):
        eid = m.group(1)
        if eid.isdigit() or eid.startswith("528"):
            continue
        ids.setdefault(eid, set()).add(fn)

print("Count:", len(ids))
for eid in sorted(ids):
    print(f"  {eid}  [{', '.join(sorted(ids[eid]))}]")

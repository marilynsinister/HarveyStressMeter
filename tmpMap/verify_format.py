from pathlib import Path
import re

CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
)
SAFE = {
    "HarveyMod_FirstTreatment",
    "eventHarveyCheckHealthFarmer",
    "eventHarveyFirstDate",
    "eventHarveyLateNightCollapse",
    "eventHarveyMountainDate",
    "eventHarveyPropose",
    "eventHarveyRoomCheckup",
    "eventHarveyRoomCheckup2",
    "eventHarveyTraumaExam",
    "eventStayInHospital",
    "eventHarveyEmergencyCare",
    "eventHarveyMineInterception",
}

for fn in ["events.json", "eventsCare.json"]:
    text = (CP / fn).read_text(encoding="utf-8")
    for eid in SAFE:
        m = re.search(rf'"{re.escape(eid)}(?:/[^"]*)?"\s*:\s*"', text)
        if not m:
            continue
        start = m.end()
        # find closing quote
        i = start
        while i < len(text):
            if text[i] == "\\":
                i += 2
                continue
            if text[i] == '"':
                break
            i += 1
        body = text[start:i]
        lines = body.split("\n")
        first_line = lines[0] if lines else body[:40]
        has_inline_cmds = bool(re.match(r"^[^/\n]+/[^/\n]", first_line))
        ok_open = body.startswith("\n    ")
        print(f"{eid} ({fn}): open_ok={ok_open}, inline_after_open={has_inline_cmds}, lines={len(lines)}")

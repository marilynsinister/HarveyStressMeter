from pathlib import Path
import re

CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
)
SAFE = [
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
]


def find_json_string_end(text: str, value_start: int) -> int:
    i = value_start + 1
    while i < len(text):
        if text[i] == "\\":
            i += 2
            continue
        if text[i] == '"':
            return i + 1
        i += 1
    raise ValueError("unterminated")


def extract(text: str, eid: str) -> str:
    m = re.search(rf'"{re.escape(eid)}(?:/[^"]*)?"\s*:\s*"', text)
    if not m:
        m = re.search(rf'"{re.escape(eid)}"\s*:\s*"', text)
    if not m:
        return ""
    open_q = m.end() - 1
    end = find_json_string_end(text, open_q)
    return text[open_q + 1 : end - 1]


for fn in ["events.json", "eventsCare.json"]:
    text = (CP / fn).read_text(encoding="utf-8")
    for eid in SAFE:
        body = extract(text, eid)
        if not body:
            continue
        flags = []
        if "quickQuestion" in body:
            flags.append("quickQuestion")
        if "(break)" in body:
            flags.append("(break)")
        if re.search(r"\bquestion\b", body):
            flags.append("question")
        if re.search(r"\bmessage\s+Harvey\b", body):
            flags.append("message Harvey")
        ok = body.startswith("\n    ")
        print(f"{eid} ({fn}): lines={body.count(chr(10))+1}, ok={ok}, flags={flags or 'none'}")

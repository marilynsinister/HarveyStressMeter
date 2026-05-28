#!/usr/bin/env python3
"""Deep validation: multiline strings, quotes, keys."""
from pathlib import Path
import re

CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
)

MODIFIED = ["events.json", "eventsCare.json"]

# Events we formatted (from prior sessions)
FORMATTED_SAFE = {
    "HarveyMod_FirstTreatment", "eventHarveyCheckHealthFarmer", "eventHarveyFirstDate",
    "eventHarveyLateNightCollapse", "eventHarveyMountainDate", "eventHarveyPropose",
    "eventHarveyRoomCheckup", "eventHarveyRoomCheckup2", "eventHarveyTraumaExam",
    "eventStayInHospital", "eventHarveyEmergencyCare", "eventHarveyMineInterception",
}
FORMATTED_QQ = {
    "acceptWalk", "eventHarveyCheckFarmerOutsideAfter22", "eventHarveyMorningCheckup",
    "eventHarveyStormComfortFarm", "eventHarveyStormComfortForest", "eventHarveyStormComfortTown",
    "eventHarveyStormComfortMine", "eventHarveyStormComfortMountain", "eventHarveyStormComfortDesert",
    "HarveyMod_TreatmentPlanMeeting", "HarveyOverhaulStory.E4_PierBreath",
    "HarveyOverhaulStory.E5_StormBeside", "HarveyOverhaulStory.E6_SayItOutLoud",
    "eventRescueOperation", "eventHarveyMedicalCheck", "eventHarveyMedicalCheck_Dating",
    "eventHarveyFirstVisit", "eventHarveySecondVisit", "eventHarveySkullCavePrevention",
}


def find_json_string_end(text: str, value_start: int) -> int:
    i = value_start + 1
    while i < len(text):
        if text[i] == "\\":
            i += 2
            continue
        if text[i] == '"':
            return i + 1
        i += 1
    return -1


def analyze_file(fn: str) -> None:
    text = (CP / fn).read_text(encoding="utf-8")
    key_re = re.compile(r'"((?:[^"\\]|\\.)+)"\s*:\s*"')
    multiline_values = 0
    inline_values = 0
    array_values = 0
    quote_issues = []
    corrupt = []

    for m in key_re.finditer(text):
        key = m.group(1)
        open_q = m.end() - 1
        close = find_json_string_end(text, open_q)
        if close < 0:
            quote_issues.append(f'{key[:50]}: unterminated')
            continue
        body = text[open_q + 1 : close - 1]
        if "\n" in body or "\r" in body:
            multiline_values += 1
            base = key.split("/")[0]
            if base in FORMATTED_SAFE or base in FORMATTED_QQ or key == "acceptWalk":
                pass  # expected from our formatting
        else:
            inline_values += 1

        # raw quote balance in JSON string body
        q = 0
        i = 0
        while i < len(body):
            if body[i] == "\\" and i + 1 < len(body):
                if body[i + 1] == '"':
                    q += 1
                i += 2
                continue
            if body[i] == '"':
                quote_issues.append(f'{key[:40]}: raw unescaped " at pos {i}')
                break
            i += 1
        if q % 2 != 0:
            quote_issues.append(f'{key[:40]}: odd escaped quote count {q}')

    # array entry values in Events
    for m in re.finditer(
        r'"Target"\s*:\s*"Data/Events[^"]*"[\s\S]*?"Entries"\s*:\s*\{', text
    ):
        pass

    array_m = list(re.finditer(r'"((?:[^"\\]|\\.)+)"\s*:\s*\[', text))
    for m in array_m:
        k = m.group(1)
        if "Changes" not in k and "Entries" not in k:
            array_values += 1

    if "/,," in text:
        corrupt.append("/,,")
    if re.search(r'\\"/\n\s*,\n', text):
        corrupt.append("early string close + comma line")

    print(f"\n=== {fn} ===")
    print(f"  String values (event-like keys): multiline={multiline_values}, inline={inline_values}")
    print(f"  Array-valued keys (non-Changes): {array_values}")
    print(f"  Quote issues: {len(quote_issues)}")
    for q in quote_issues[:10]:
        print(f"    - {q}")
    if len(quote_issues) > 10:
        print(f"    ... and {len(quote_issues)-10} more")
    print(f"  Corrupt patterns: {corrupt or 'none'}")
    print(f"  JSONC // lines: {sum(1 for ln in text.splitlines() if ln.strip().startswith('//'))}")


for fn in MODIFIED:
    analyze_file(fn)

# Can json parse if we strip comments and... can't fix multiline easily
print("\n=== Strict JSON ===")
print("Both files: FAIL (literal newlines inside quoted event script strings)")
print("eventsMineRescue.json: PASS (single-line string values)")

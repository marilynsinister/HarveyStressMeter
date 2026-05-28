#!/usr/bin/env python3
"""Final validation: C# topic/mail IDs vs CP JSON (read-only)."""
from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(r"C:\Users\Admin\HarveyOverhaulInjury")
CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
)

PHASED = [
    "Concussion", "FracturedBone", "TornMuscles", "SprainedAnkle", "BruisedRibs",
    "DeepCuts", "BurnWounds", "InfectedWound", "BackStrain", "ShrapnelWounds", "Cold",
]
TRAUMAS = [
    "Hurt", "BadlyHurt", "SprainedAnkle", "BruisedRibs", "BackStrain", "DeepCuts",
    "BurnWounds", "InfectedWound", "TornMuscles", "Concussion", "FracturedBone",
    "ShrapnelWounds", "SurgicalWound", "Cold",
]
COMPLICATIONS = [
    "topicHarvey_WetBandage", "topicHarvey_DirtyWound", "topicHarvey_WetStitches",
    "topicHarvey_Neglect", "topicHarvey_AllergicRash", "topicHarvey_PainFlare",
]
STATIC_TOPICS = [
    "topicHealthDamageCritical", "topicHealthDamageSevere", "topicPostOperativeCare",
    "topicFarmerExhausted", "topicPassedOutInTown", "topicTooCold",
    "topicColdCured", "topicSurgicalWoundCured", "topicTreatmentCompleted",
    "topicMineInjuryRescue", "topicMineRescuePending", "topicHarveyNeedsFirstTreatment",
    "topicFirstTreatmentComplete", "topicHarvey_ForcedHospitalization",
    "topicHarvey_NightRound",
]
MAIL_SEND = [
    "mailHarveySleepControl", "mailHarveyMineForbidden",
    "HarveyMod_DirtyWoundInfection", "HarveyMod_WetBandageInfection",
    "HarveyMod_TreatmentUrgentReminder", "HarveyMod_TreatmentFinalWarning",
    "HarveyMod_NeglectWarning",
]

DIALOGUE_FILES = [
    "dialoguesHarvey.json", "dialoguesHarveyCare.json", "dialoguesHarveyCure.json",
    "dialoguesHarveyCureStress.json", "dialoguesHarveyInjury.json", "dialoguesHarveyPregnant.json",
]
MAIL_FILES = ["mail.json", "mailCare.json", "mailCure.json", "mailInjury.json", "mailStress.json"]

KEY_RE = re.compile(r'"([^"]+)"\s*:\s*"')


def strip_jsonc(text: str) -> str:
    out = []
    for line in text.splitlines():
        if re.match(r"^\s*//", line):
            continue
        if "//" in line:
            in_str = False
            esc = False
            cut = len(line)
            for i, ch in enumerate(line):
                if esc:
                    esc = False
                    continue
                if ch == "\\":
                    esc = True
                    continue
                if ch == '"':
                    in_str = not in_str
                    continue
                if not in_str and line[i : i + 2] == "//":
                    cut = i
                    break
            line = line[:cut]
        out.append(line)
    text = "\n".join(out)
    return re.sub(r"/\*[\s\S]*?\*/", "", text)


def collect_keys(path: Path) -> set[str]:
    text = strip_jsonc(path.read_text(encoding="utf-8"))
    return set(KEY_RE.findall(text))


def main() -> None:
    dialogue_keys: set[str] = set()
    for f in DIALOGUE_FILES:
        p = CP / f
        if p.exists():
            dialogue_keys |= collect_keys(p)

    mail_keys: set[str] = set()
    for f in MAIL_FILES:
        p = CP / f
        if p.exists():
            mail_keys |= collect_keys(p)

    csharp_topics: set[str] = set()
    for t in TRAUMAS:
        csharp_topics.add(f"topic{t}")
    for t in TRAUMAS:
        csharp_topics.add(f"topic{t}Cured")
    for p in PHASED:
        csharp_topics.add(f"topicTreatment{p}")
        for stage in ("Acute", "Healing", "Recovery"):
            csharp_topics.add(f"topic{p}Phase{stage}")
    csharp_topics.update(COMPLICATIONS)
    csharp_topics.update(STATIC_TOPICS)

    missing_topics = sorted(csharp_topics - dialogue_keys)
    missing_mail = sorted(set(MAIL_SEND) - mail_keys)

    print("=== C# topics missing in CP dialogue ===")
    for x in missing_topics:
        print(x)
    print(f"TOTAL missing topics: {len(missing_topics)}")

    print("\n=== C# mail missing in CP ===")
    for x in missing_mail:
        print(x)
    print(f"TOTAL missing mail: {len(missing_mail)}")

    # JSON parse check for dialogue/mail files
    print("\n=== JSON parse (dialogue/mail only) ===")
    bad = []
    for f in DIALOGUE_FILES + MAIL_FILES:
        p = CP / f
        if not p.exists():
            continue
        try:
            json.loads(strip_jsonc(p.read_text(encoding="utf-8")))
        except json.JSONDecodeError as e:
            bad.append(f"{f}: {e}")
    if bad:
        for b in bad:
            print("FAIL", b)
    else:
        print("All dialogue/mail JSON parse OK (after strip comments)")


if __name__ == "__main__":
    main()

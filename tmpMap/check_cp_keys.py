#!/usr/bin/env python3
from pathlib import Path
import re

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
KEY_RE = re.compile(r'"([^"]+)"\s*:\s*"')

def keys_in(*files):
    s = set()
    for f in files:
        p = CP / f
        if p.exists():
            s |= set(KEY_RE.findall(p.read_text(encoding="utf-8")))
    return s

dialogue = keys_in(
    "dialoguesHarvey.json", "dialoguesHarveyCare.json", "dialoguesHarveyCure.json",
    "dialoguesHarveyCureStress.json", "dialoguesHarveyInjury.json", "dialoguesHarveyPregnant.json",
)
mail = keys_in("mail.json", "mailCare.json", "mailCure.json", "mailInjury.json", "mailStress.json")

for c in [
    "topicDiagnosisComplete", "topicRescueOperation", "topicHarveyMinorMineRescue",
    "topicHarveyStormStress", "topicSurgicalWoundHealed", "topicSurgicalWoundCured",
    "mailHarvey_Neglect", "HarveyMod_NeglectWarning",
]:
    loc = []
    if c in dialogue:
        loc.append("dialogue")
    if c in mail:
        loc.append("mail")
    print(c, loc or ["MISSING"])

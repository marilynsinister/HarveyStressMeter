#!/usr/bin/env python3
"""Audit MailIds constants vs CP JSON (exact or tiered suffix)."""
import re
from pathlib import Path

CS = Path(r"C:\Users\Admin\HarveyOverhaulInjury\Core\Constants.cs")
CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
)

text = CS.read_text(encoding="utf-8")
block = re.search(r"class MailIds\s*\{(.*?)\n\s*\}", text, re.S).group(1)
mail_ids = re.findall(r'=\s*"([^"]+)"', block)

suffixes = ["LowHearts", "MidHearts", "Dating", "Married"]
mail_keys: set[str] = set()
for f in CP.glob("*.json"):
    raw = f.read_text(encoding="utf-8")
    mail_keys |= set(re.findall(r'"(mail[^"]+)"\s*:', raw))
    mail_keys |= set(re.findall(r'"(HarveyMod_[^"]+)"\s*:', raw))


def exists(mid: str) -> tuple[bool, str | None]:
    if mid in mail_keys:
        return True, mid
    for s in suffixes:
        tiered = f"{mid}_{s}"
        if tiered in mail_keys:
            return True, tiered
    return False, None


missing = []
ok = []
for mid in sorted(set(mail_ids)):
    found, key = exists(mid)
    if found:
        ok.append((mid, key))
    else:
        missing.append(mid)

print("=== MailIds audit (C# vs CP JSON) ===")
print(f"Total MailIds: {len(set(mail_ids))}")
print(f"OK: {len(ok)}, MISSING: {len(missing)}")
if missing:
    print("\nMISSING:")
    for m in missing:
        print(f"  {m}")
else:
    print("\nAll MailIds resolved in CP (exact or tiered).")

print("\n--- TreatmentPlan resolve check ---")
map_entries = {
    "buffConcussion": "mailHarveyTreatmentPlan_Severe",
    "buffFracturedBone": "mailHarveyTreatmentPlan_Severe",
    "buffBurnWounds": "mailHarveyTreatmentPlan_Severe",
    "buffInfectedWound": "mailHarveyTreatmentPlan_Severe",
    "buffShrapnelWounds": "mailHarveyTreatmentPlan_Severe",
    "buffSurgicalWound": "mailHarveyTreatmentPlan_Severe",
    "buffCold": "mailHarveyTreatmentPlan_Minor",
}
for injury, base in map_entries.items():
    found, key = exists(base)
    status = "OK" if found else "MISSING"
    print(f"  {injury} -> {base} [{status}] ({key or '-'})")

old = [
    "mailHarveyTreatmentPlan_Infection",
    "mailHarveyTreatmentPlan_Concussion",
    "mailHarveyTreatmentPlan_Fracture",
    "mailHarveyTreatmentPlan_Burn",
    "mailHarveyTreatmentPlan_Cold",
]
print("\n--- Removed mailIds (should be absent from C# constants) ---")
for o in old:
    print(f"  {o}: in MailIds={o in mail_ids}")

import re
import os

cp = r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
files = ["dialoguesHarveyCure.json", "dialoguesHarveyInjury.json"]
keys = set()
for fn in files:
    path = os.path.join(cp, fn)
    text = open(path, encoding="utf-8").read()
    keys |= {m.group(1) for m in re.finditer(r'"([^"]+)"\s*:', text)}

injuries = [
    "Concussion", "FracturedBone", "DeepCuts", "BurnWounds", "InfectedWound",
    "ShrapnelWounds", "TornMuscles", "BackStrain", "BruisedRibs", "SprainedAnkle", "Cold",
]
three_phase = ["Concussion", "FracturedBone", "DeepCuts", "ShrapnelWounds", "TornMuscles"]
two_phase = ["BackStrain", "BruisedRibs", "SprainedAnkle", "BurnWounds", "InfectedWound", "Cold"]


def has_prefix(p):
    return any(k.startswith(p) for k in keys)


print("=== Treat Before/After ===")
for inj in injuries:
    b = has_prefix(f"Treat_{inj}_Before")
    a = has_prefix(f"Treat_{inj}_After")
    status = "OK" if b and a else "MISSING"
    print(f"{inj}: Before={b} After={a} [{status}]")

print("\n=== PhaseTransition ===")
for inj in three_phase:
    for p in [2, 3]:
        ok = has_prefix(f"PhaseTransition_{inj}_{p}")
        print(f"PhaseTransition_{inj}_{p}: {ok}")
for inj in two_phase:
    ok = has_prefix(f"PhaseTransition_{inj}_2")
    print(f"PhaseTransition_{inj}_2: {ok}")

print("\n=== Recovery_Complete ===")
for inj in injuries:
    ok = has_prefix(f"Recovery_Complete_{inj}")
    print(f"Recovery_Complete_{inj}: {ok}")

print("\nRecovery_Complete count:", sum(1 for k in keys if k.startswith("Recovery_Complete_")))

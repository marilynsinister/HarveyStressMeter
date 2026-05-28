import re
import os

cp_root = r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
files = ["dialoguesHarveyCure.json", "dialoguesHarveyInjury.json", "dialoguesHarvey.json"]
keys = {
    "Treat_": [],
    "PhaseTransition_": [],
    "Recovery_Complete_": [],
    "topicCured": [],
    "topicPhase": [],
    "topicConcussion": [],
}

for fn in files:
    path = os.path.join(cp_root, fn)
    if not os.path.exists(path):
        continue
    text = open(path, encoding="utf-8").read()
    for m in re.finditer(r'"([^"]+)"\s*:', text):
        k = m.group(1)
        if k.startswith("Treat_"):
            keys["Treat_"].append(k)
        elif k.startswith("PhaseTransition_"):
            keys["PhaseTransition_"].append(k)
        elif k.startswith("Recovery_Complete_"):
            keys["Recovery_Complete_"].append(k)
        elif k.endswith("Cured") and k.startswith("topic"):
            keys["topicCured"].append(k)
        elif "Phase" in k and k.startswith("topic"):
            keys["topicPhase"].append(k)
        elif k.startswith("topicConcussion"):
            keys["topicConcussion"].append(k)

injuries = [
    "Hurt", "BadlyHurt", "BruisedRibs", "SprainedAnkle", "BackStrain",
    "DeepCuts", "BurnWounds", "TornMuscles", "Concussion", "FracturedBone",
    "ShrapnelWounds", "InfectedWound", "SurgicalWound", "Cold",
]
phased = [
    "Concussion", "FracturedBone", "TornMuscles", "SprainedAnkle", "BruisedRibs",
    "DeepCuts", "BurnWounds", "InfectedWound", "BackStrain", "ShrapnelWounds", "Cold",
]
stages = ["Acute", "Healing", "Recovery"]

expected_treat = set()
for inj in injuries:
    for suffix in ["Before", "After"]:
        for i in range(1, 8):
            expected_treat.add(f"Treat_{inj}_{suffix}{i}")

expected_phase_trans = set()
for inj in phased:
    total = 2 if inj == "Cold" else 3
    for p in range(2, total + 1):
        expected_phase_trans.add(f"PhaseTransition_{inj}_{p}")

expected_cured = {f"topic{inj}Cured" for inj in injuries}
expected_phase = set()
for inj in injuries + ["AlcoholPoisoning"]:
    for st in stages:
        expected_phase.add(f"topic{inj}Phase{st}")

for name in keys:
    keys[name] = sorted(set(keys[name]))

print("=== COUNTS ===")
for name, lst in keys.items():
    print(f"{name}: {len(lst)}")

print("\n=== MISSING Treat_* ===")
have_treat = set(keys["Treat_"])
missing_treat = sorted(expected_treat - have_treat)
print("missing", len(missing_treat))
for k in missing_treat:
    print(" ", k)

print("\n=== EXTRA Treat_* (not in expected 7x Before/After) ===")
extra_treat = sorted(have_treat - expected_treat)
for k in extra_treat:
    print(" ", k)

print("\n=== MISSING PhaseTransition ===")
have_pt = set(keys["PhaseTransition_"])
print(sorted(expected_phase_trans - have_pt))

print("\n=== EXTRA PhaseTransition ===")
print(sorted(have_pt - expected_phase_trans))

print("\n=== MISSING topic*Cured ===")
have_cured = set(keys["topicCured"])
print(sorted(expected_cured - have_cured))

print("\n=== Recovery_Complete in CP ===")
print(keys["Recovery_Complete_"])

inj_with_treat = set()
for k in keys["Treat_"]:
    m = re.match(r"Treat_(\w+)_(Before|After)", k)
    if m:
        inj_with_treat.add(m.group(1))
print("\nInjuries WITHOUT Treat_*:", sorted(set(injuries) - inj_with_treat))

# Phase topics C# creates vs CP
cs_phased = [
    "buffConcussion", "buffFracturedBone", "buffTornMuscles", "buffSprainedAnkle",
    "buffBruisedRibs", "buffDeepCuts", "buffBurnWounds", "buffInfectedWound",
    "buffBackStrain", "buffShrapnelWounds", "buffCold",
]
from pathlib import Path
# import TopicIds logic
def phase_topic(injury_id, phase):
    injury_name = injury_id.replace("buff", "")
    stage = {1: "Acute", 2: "Healing", 3: "Recovery"}[phase]
    return f"topic{injury_name}Phase{stage}"

cs_phase_topics = {phase_topic(i, p) for i in cs_phased for p in (1, 2, 3)}
cp_phase = set(keys["topicPhase"])
print("\n=== MISSING topic*Phase* (C# phased injuries) ===")
print(sorted(cs_phase_topics - cp_phase))
print("\n=== CP topicPhase not in C# phased set ===")
print(sorted(cp_phase - cs_phase_topics)[:30])

from pathlib import Path

p = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\events.json"
)
t = p.read_text(encoding="utf-8")
for needle in ['"acceptWalk"', "eventHarveyMedicalCheck/Friendship", '"58/f Harvey']:
    i = t.find(needle)
    print("===", needle, "===")
    print(t[i : i + 900])
    print()

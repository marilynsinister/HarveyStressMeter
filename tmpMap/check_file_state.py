from pathlib import Path

p = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\events.json"
)
text = p.read_text(encoding="utf-8")
print("corruption /,,:", "/,," in text)
print("count HarveyMod_FirstTreatment keys:", text.count('"HarveyMod_FirstTreatment/'))
for needle in [
    "HarveyMod_FirstTreatment/",
    "eventHarveyCheckHealthFarmer/",
    "eventHarveyTraumaExam/",
]:
    idx = text.find(f'"{needle}')
    if idx >= 0:
        snippet = text[idx : idx + 200]
        print("---", needle)
        print(repr(snippet[:180]))

from pathlib import Path
import re

p = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\events.json"
)
text = p.read_text(encoding="utf-8")
for m in re.finditer(r'\\"/\n    ,\n', text):
    print("CORRUPT at", m.start())
    print(repr(text[m.start() - 60 : m.start() + 80]))

print("total corrupt:", len(list(re.finditer(r'\\"/\n    ,\n', text))))

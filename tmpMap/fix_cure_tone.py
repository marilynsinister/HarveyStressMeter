# -*- coding: utf-8 -*-
from pathlib import Path

cure_path = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\dialoguesHarveyCure.json"
)
text = cure_path.read_text(encoding="utf-8")
marker = (
    '\n        {\n            "Action": "EditData",\n'
    '            "Target": "Characters/Dialogue/Harvey",\n            "When":'
)
idx = text.find(marker)
if idx == -1:
    raise SystemExit("cure marker not found")
head, tail = text[:idx], text[idx:]

replacements = [
    ("не отпущу тебя без контроля", "завершу осмотр и дам чёткие рекомендации"),
    ("не отпущу", "не завершу осмотр"),
    ("не позволю", "не рекомендую"),
    ("Ты под моей защитой", "Вы под медицинским наблюдением"),
    ("ты под моей защитой", "вы под наблюдением"),
    ("Ты под моим присмотром", "Вы под наблюдением"),
    ("ты под моим присмотром", "вы под наблюдением"),
    ("слишком хрупкая", "организм ослаблен"),
    ("слишком худая", "недостаточно питания"),
    ("никуда без меня не пойдёшь", "сегодня — только покой"),
    ("я всё контролирую", "я слежу за показателями"),
    ("отпущу только если", "отпущу после осмотра, если"),
]
for old, new in replacements:
    head = head.replace(old, new)

cure_path.write_text(head + tail, encoding="utf-8")
print("cure OK")

from pathlib import Path

p = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\events.json"
)
text = p.read_text(encoding="utf-8")
old = (
    '    speak Harvey \\"Наконец-то... Я так волновался за тебя.$0#$b#Садись сюда. '
    'Нужно провести полное обследование.$u\\"/\n    ,\n        pause 500/'
)
new = (
    '    speak Harvey \\"Наконец-то... Я так волновался за тебя.$0#$b#Садись сюда. '
    'Нужно провести полное обследование.$u\\"/\n    pause 500/'
)
if old not in text:
    print("pattern not found, checking...")
    idx = text.find("волновался за тебя")
    print(repr(text[idx - 20 : idx + 160]))
else:
    p.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")
    print("fixed")

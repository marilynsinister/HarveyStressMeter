#!/usr/bin/env python3
"""Replace intimate Harvey terms in CP JSON (JSONC-safe raw text)."""
from __future__ import annotations

import re
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
REPORT = Path(r"C:\Users\Admin\HarveyOverhaulInjury\docs\harvey-relationship-visits-audit\intimate-lines-replacements.md")

EVENT_FILES = [CP / "events.json", CP / "eventsCare.json", CP / "eventsMineRescue.json"]
DIALOGUE_FILES = [
    CP / "dialoguesHarvey.json",
    CP / "dialoguesHarveyInjury.json",
    CP / "dialoguesHarveyCare.json",
    CP / "dialoguesHarveyCure.json",
    CP / "dialoguesHarveyCureStress.json",
    CP / "dialoguesHarveyStress.json",
    CP / "dialoguesHarveyPregnant.json",
]

DATING_MARRIED = re.compile(r"PLAYER_NPC_RELATIONSHIP\s+Current\s+Harvey\s+(?:Dating|Married)", re.I)
EVENT_SPLIT = re.compile(r'(end","|","|^\s*")(?=[A-Za-z0-9_]+/)')

KEEP_KEY = re.compile(
    r'"(?:dating_|married_|marriage|AcceptBouquet|RejectBouquet_AlreadyAccepted|FlowerDance_Accept_Spouse)',
    re.I,
)
WHEN_DATING = re.compile(r'"Relationship:Harvey"\s*:\s*"(?:Dating|Married)"')
WHEN_MARRIED_TARGET = re.compile(r'"Target"\s*:\s*"Characters/Dialogue/MarriageDialogueHarvey"')

REPLACEMENTS: list[tuple[re.Pattern, str]] = [
    (re.compile(r"С днём рождения, моя дорогая!", re.I), "С днём рождения, @!"),
    (re.compile(r"Плановый осмотр, дорогая!", re.I), "Плановый осмотр, @!"),
    (re.compile(r"Дорогая, ты на грани", re.I), "@, ты на грани"),
    (re.compile(r"кто-то, кто очень тебя любит", re.I), "человек, которому ты очень дорога"),
    (re.compile(r'textAboveHead Harvey \\"Я люблю тебя\.\.\.\\"', re.I),
     'textAboveHead Harvey \\"Ты мне очень дорога...\\"'),
    (re.compile(r"Я люблю тебя\.\.\.", re.I), "Ты мне очень дорога..."),
    (re.compile(r"Я люблю тебя", re.I), "Ты мне очень дорога"),
    (re.compile(r"Хорошая девочка\. Такой я и хочу тебя видеть — уверенной", re.I),
     "Вот так, у тебя получается. Такой я и хочу тебя видеть — уверенной"),
    (re.compile(r"Хорошая девочка\. Сердцебиение нормализуется", re.I),
     "Вот так, у тебя получается. Сердцебиение нормализуется"),
    (re.compile(r"Хорошая девочка", re.I), "Вот так, у тебя получается"),
    (re.compile(r"Смотри на меня, девочка моя", re.I), "Смотри на меня, @"),
    (re.compile(r"девочка моя", re.I), "@"),
    (re.compile(r"моя дорогая", re.I), "ты"),
    (re.compile(r"\bДорогая,", re.I), "@,"),
    (re.compile(r"Привет, солнышко\.\.\.", re.I), "Привет, @. Рад тебя видеть."),
    (re.compile(r"Доверься мне, солнышко", re.I), "Доверься мне, @"),
    (re.compile(r"Иди сюда, солнышко", re.I), "Иди сюда, @"),
    (re.compile(r"Проснись, солнышко", re.I), "Проснись, @"),
    (re.compile(r"Привет, солнышко", re.I), "Привет, @. Рад тебя видеть."),
    (re.compile(r"моя любовь к тебе", re.I), "моё доверие к тебе"),
    (re.compile(r"моя хорошая\?", re.I), "ты в порядке?"),
    (re.compile(r"Как моя малышка\?", re.I), "Как ты?"),
    (re.compile(r"Малышка,", re.I), "@,"),
    (re.compile(r"\bмалышка\b", re.I), "@"),
    (re.compile(r"\bсолнышко\b", re.I), "@"),
    (re.compile(r"\bлюбимая\b", re.I), "@"),
    (re.compile(r"Моё сердце - твой щит от грозы", re.I), "Я рядом — гроза тебе не страшна"),
    (re.compile(r"свернувшись калачиком под моим боком", re.I), "такой маленькой на фоне всей долины"),
]


def apply(text: str) -> tuple[str, int]:
    n = 0
    out = text
    for pat, repl in REPLACEMENTS:
        out2, c = pat.subn(repl, out)
        if c:
            n += c
            out = out2
    return out, n


def process_events(path: Path) -> list[tuple[str, str, int]]:
    text = path.read_text(encoding="utf-8")
    log: list[tuple[str, str, int]] = []
    parts = re.split(r'(end","|",")(?=[A-Za-z][A-Za-z0-9_.]*(?:/|$))', text)
    rebuilt: list[str] = []
    i = 0
    while i < len(parts):
        sep = parts[i] if i < len(parts) and parts[i] in ('end","', '","') else ""
        if sep:
            i += 1
        if i >= len(parts):
            if sep:
                rebuilt.append(sep)
            break
        chunk = parts[i]
        i += 1
        m = re.match(r'([A-Za-z][A-Za-z0-9_.]*(?:/[^"]*)?)":\s*"', chunk)
        if not m:
            rebuilt.append(sep + chunk)
            continue
        key = m.group(1)
        body = chunk[m.end() - 1:]  # includes opening quote content from split artifact
        # chunk after split: key": "script...  OR from first segment includes prefix
        if chunk.startswith('"') and not m:
            rebuilt.append(sep + chunk)
            continue
        script_start = chunk.find('": "')
        if script_start == -1:
            rebuilt.append(sep + chunk)
            continue
        prefix = chunk[: script_start + 4]
        script = chunk[script_start + 4 :]
        if DATING_MARRIED.search(key):
            rebuilt.append(sep + chunk)
            continue
        new_script, count = apply(script)
        if count:
            event_id = key.split("/")[0]
            log.append((path.name, event_id, count))
            rebuilt.append(sep + prefix + new_script)
        else:
            rebuilt.append(sep + chunk)
    new_text = "".join(rebuilt)
    if new_text != text:
        path.write_text(new_text, encoding="utf-8")
    return log


def process_dialogue(path: Path) -> list[tuple[str, str, int]]:
    text = path.read_text(encoding="utf-8")
    log: list[tuple[str, str, int]] = []
    blocks = re.split(r'(\{\s*\n\s*"Action":\s*"EditData")', text)
    out_parts = [blocks[0]]
    idx = 1
    while idx < len(blocks):
        marker = blocks[idx]
        block = blocks[idx + 1] if idx + 1 < len(blocks) else ""
        idx += 2
        full = marker + block
        if WHEN_MARRIED_TARGET.search(full):
            out_parts.append(full)
            continue
        if WHEN_DATING.search(full):
            out_parts.append(full)
            continue
        if '"Target"' in full and "Harvey" not in full:
            out_parts.append(full)
            continue

        def repl_entry(m: re.Match) -> str:
            key = m.group(1)
            val = m.group(2)
            if KEEP_KEY.search(f'"{key}"'):
                return m.group(0)
            new_val, count = apply(val)
            if count:
                log.append((path.name, key, count))
            return f'"{key}": "{new_val}"'

        new_block = re.sub(
            r'"([^"]+)":\s*"((?:[^"\\]|\\.)*)"',
            repl_entry,
            full,
            flags=re.S,
        )
        out_parts.append(new_block)
    new_text = "".join(out_parts)
    if new_text != text:
        path.write_text(new_text, encoding="utf-8")
    return log


def main() -> None:
    all_log: list[tuple[str, str, int]] = []
    for p in EVENT_FILES:
        if p.exists():
            all_log.extend(process_events(p))
    for p in DIALOGUE_FILES:
        if p.exists():
            all_log.extend(process_dialogue(p))

    lines = [
        "# Замены интимных обращений Харви (pre-dating)\n",
        "Правило: оставлены без изменений блоки с `PLAYER_NPC_RELATIONSHIP ... Dating/Married` "
        "и dialogue-секции с `When: Relationship:Harvey Dating/Married`, а также `dating_*`, `marriage_*`, `married_*`.\n",
        f"Затронуто записей: **{len(all_log)}** | Всего замен фраз: **{sum(x[2] for x in all_log)}**\n",
        "| Файл | Event ID / Key | Замен |",
        "|---|---|---|",
    ]
    for fname, ident, count in sorted(all_log):
        lines.append(f"| `{fname}` | `{ident}` | {count} |")
    REPORT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Entries: {len(all_log)}, replacements: {sum(x[2] for x in all_log)}")
    print(REPORT)


if __name__ == "__main__":
    main()

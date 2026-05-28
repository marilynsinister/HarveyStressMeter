#!/usr/bin/env python3
"""Fix dialogue blocks: When at end of Change object supported."""
from __future__ import annotations

import re
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
FILES = [
    CP / "dialoguesHarvey.json",
    CP / "dialoguesHarveyInjury.json",
    CP / "dialoguesHarveyCare.json",
    CP / "dialoguesHarveyCure.json",
    CP / "dialoguesHarveyCureStress.json",
    CP / "dialoguesHarveyStress.json",
]

KEEP_KEY = re.compile(
    r'"(?:dating_|married_|marriage|AcceptBouquet|RejectBouquet_AlreadyAccepted|FlowerDance_Accept_Spouse|married_Harvey)',
    re.I,
)
SKIP_BLOCK = re.compile(
    r'"Relationship:Harvey"\s*:\s*"(?:Dating|Married)"|"Target"\s*:\s*"Characters/Dialogue/MarriageDialogueHarvey"',
    re.I,
)

REPLACEMENTS: list[tuple[re.Pattern, str]] = [
    (re.compile(r"С днём рождения, моя дорогая!", re.I), "С днём рождения, @!"),
    (re.compile(r"самая важная для меня девочка", re.I), "самый важный для меня человек"),
    (re.compile(r"Сегодня ты только моя,", re.I), "Сегодня день только для тебя,"),
    (re.compile(r"моя хорошая\?", re.I), "ты в порядке?"),
    (re.compile(r"Как моя малышка\?", re.I), "Как ты?"),
    (re.compile(r"моя маленькая фея", re.I), "@"),
    (re.compile(r"моя лесная фея", re.I), "@"),
    (re.compile(r"моя фея", re.I), "@"),
    (re.compile(r"котёнок", re.I), "@"),
    (re.compile(r"девочка моя", re.I), "@"),
    (re.compile(r"моя дорогая", re.I), "ты"),
    (re.compile(r"Привет, солнышко\.\.\.", re.I), "Привет, @. Рад тебя видеть."),
    (re.compile(r"\bсолнышко\b", re.I), "@"),
    (re.compile(r"\bмалышка\b", re.I), "@"),
    (re.compile(r"\bлюбимая\b", re.I), "@"),
    (re.compile(r"Потанцуем, @\?", re.I), "Потанцуем?"),
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


def process_file(path: Path) -> int:
    text = path.read_text(encoding="utf-8")
    parts = re.split(r'(\{\s*\n\s*"Action":\s*"EditData")', text)
    out = [parts[0]]
    total = 0
    i = 1
    while i < len(parts):
        head = parts[i]
        body = parts[i + 1] if i + 1 < len(parts) else ""
        block = head + body
        i += 2
        if SKIP_BLOCK.search(block):
            out.append(block)
            continue
        if '"Target"' in block and "Harvey" not in block:
            out.append(block)
            continue

        def repl(m: re.Match) -> str:
            nonlocal total
            key, val = m.group(1), m.group(2)
            if KEEP_KEY.search(f'"{key}"'):
                return m.group(0)
            new_val, c = apply(val)
            total += c
            return f'"{key}": "{new_val}"'

        new_block = re.sub(r'"([^"]+)":\s*"((?:[^"\\]|\\.)*)"', repl, block, flags=re.S)
        out.append(new_block)
    new_text = "".join(out)
    if new_text != text:
        path.write_text(new_text, encoding="utf-8")
    return total


def main() -> None:
    for p in FILES:
        if p.exists():
            n = process_file(p)
            print(f"{p.name}: {n} replacements")


if __name__ == "__main__":
    main()

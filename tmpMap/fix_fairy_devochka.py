#!/usr/bin/env python3
"""Remove fairy metaphors everywhere; neutralize 'девочка' outside Dating/Married blocks."""
from __future__ import annotations

import re
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")

HARVEY_FILES = [
    CP / "dialoguesHarvey.json",
    CP / "dialoguesHarveyInjury.json",
    CP / "dialoguesHarveyCare.json",
    CP / "dialoguesHarveyCure.json",
    CP / "dialoguesHarveyCureStress.json",
    CP / "dialoguesHarveyStress.json",
    CP / "dialoguesHarveyPregnant.json",
]
NPC_FILES = [CP / "dialoguesNpc.json"]

WHEN_DATING_MARRIED = re.compile(
    r'"Relationship:Harvey"\s*:\s*"(?:Dating|Married)"|"Target"\s*:\s*"Characters/Dialogue/MarriageDialogueHarvey"',
    re.I,
)
KEEP_KEY = re.compile(
    r'"(?:dating_|married_|marriage|AcceptBouquet|RejectBouquet_AlreadyAccepted|FlowerDance_Accept_Spouse|married_Harvey)',
    re.I,
)

# Longest-first fairy removal (all blocks, including Dating/Married)
FAIRY_REPLACEMENTS: list[tuple[re.Pattern, str]] = [
    (re.compile(r"словно настоящая лесная фея на фоне неба", re.I), "невероятно прекрасна на фоне неба"),
    (re.compile(r"слова первого романа — одинокая фея и заботливый врач", re.I), "слова первого романа — хрупкая девушка и заботливый врач"),
    (re.compile(r"«Маленькая фея и строгий доктор»", re.I), "«Хрупкая девушка и строгий доктор»"),
    (re.compile(r"С днём рождения, моя маленькая лесная фея\.", re.I), "С днём рождения, @."),
    (re.compile(r"С днём рождения, маленькая лесная фея\.", re.I), "С днём рождения, @."),
    (re.compile(r"С днём рождения, маленькая фея\.", re.I), "С днём рождения, @."),
    (re.compile(r"С днём рождения, лесная фея\.", re.I), "С днём рождения, @."),
    (re.compile(r"С днём всех влюблённых, моя фея\.", re.I), "С днём всех влюблённых, @."),
    (re.compile(r"Ты — настоящая лесная фея в нашем доме", re.I), "Ты наполняешь наш дом особым уютом"),
    (re.compile(r"Ты для меня как редкая лесная фея", re.I), "Ты для меня очень дорога"),
    (re.compile(r"Ты похожа на лесную фею", re.I), "Ты очень бледная и худая"),
    (re.compile(r"словно у испуганной лесной феи", re.I), "испуганы"),
    (re.compile(r"словно у лесной феи", re.I), "очень хрупкие"),
    (re.compile(r"словно настоящая лесная фея среди", re.I), "очень хрупкая среди"),
    (re.compile(r"словно настоящая лесная фея", re.I), "очень хрупкая и светлая"),
    (re.compile(r"как настоящая лесная фея в", re.I), "очень хрупкая в"),
    (re.compile(r"как настоящая лесная фея", re.I), "очень хрупкая"),
    (re.compile(r"Ты словно лесная фея", re.I), "Ты выглядишь очень хрупкой"),
    (re.compile(r"словно лесная фея среди", re.I), "очень хрупкая среди"),
    (re.compile(r"словно лесная фея, которая", re.I), "особенная девушка, которая"),
    (re.compile(r"словно лесная фея на пляже", re.I), "с таким уютом на пляже"),
    (re.compile(r"словно лесная фея готовит", re.I), "с такой заботой готовишь"),
    (re.compile(r"словно лесная фея\. Мне", re.I), "очень нежная. Мне"),
    (re.compile(r"словно лесная фея\.", re.I), "очень нежная."),
    (re.compile(r"словно лесная фея,", re.I), "очень нежная,"),
    (re.compile(r"словно лесная фея ", re.I), "очень нежная "),
    (re.compile(r"как фарфоровая фея", re.I), "очень хрупкая"),
    (re.compile(r"для такой хрупкой и светлой феи, как ты", re.I), "для такого хрупкого человека, как ты"),
    (re.compile(r"для такой хрупкой феи, как ты", re.I), "для такого хрупкого человека, как ты"),
    (re.compile(r"для такой нежной феи, как ты", re.I), "для такого нежного человека, как ты"),
    (re.compile(r"даже феям нужна защита", re.I), "даже самым хрупким нужна защита"),
    (re.compile(r"даже лесные феи нуждаются", re.I), "даже самым хрупким нужен"),
    (re.compile(r"даже феи нуждаются", re.I), "даже самым хрупким нужен"),
    (re.compile(r"мудростью лесной феи", re.I), "мудростью и терпением"),
    (re.compile(r"моя лесная фея нуждается", re.I), "ты нуждаешься"),
    (re.compile(r"Моя лесная фея\.", re.I), "@."),
    (re.compile(r"Моя хрупкая фея\.", re.I), "@."),
    (re.compile(r"Моя лесная фея,", re.I), "@,"),
    (re.compile(r"Моя хрупкая фея,", re.I), "@,"),
    (re.compile(r"моя лесная фея", re.I), "@"),
    (re.compile(r"моя хрупкая фея", re.I), "@"),
    (re.compile(r"моя маленькая лесная фея", re.I), "@"),
    (re.compile(r"маленькая лесная фея", re.I), "@"),
    (re.compile(r"маленькая фея", re.I), "@"),
    (re.compile(r"моя маленькая фея", re.I), "@"),
    (re.compile(r"моя фея", re.I), "@"),
    (re.compile(r"лесной феи", re.I), "хрупкой девушки"),
    (re.compile(r"лесную фею", re.I), "хрупкую девушку"),
    (re.compile(r"лесной фее", re.I), "хрупкой девушке"),
    (re.compile(r"лесной феей", re.I), "хрупкой девушкой"),
    (re.compile(r"лесная фея", re.I), "хрупкая девушка"),
    (re.compile(r"лесную фею", re.I), "хрупкую девушку"),
    (re.compile(r"лесной феи", re.I), "хрупкой девушки"),
    (re.compile(r"хрупкой феи", re.I), "хрупкого человека"),
    (re.compile(r"нежной феи", re.I), "нежного человека"),
    (re.compile(r"светлой феи", re.I), "хрупкого человека"),
    (re.compile(r"как настоящая лесная принцесса", re.I), "полноценным отдыхом"),
    (re.compile(r", маленькая фея\?", re.I), ", @?"),
]

DEVOCHKA_REPLACEMENTS: list[tuple[re.Pattern, str]] = [
    (re.compile(r"Моя хрупкая девочка,", re.I), "@,"),
    (re.compile(r"моя хрупкая девочка", re.I), "@"),
    (re.compile(r"девочка моя", re.I), "@"),
    (re.compile(r"моя девочка", re.I), "@"),
    (re.compile(r"Моя храбрая девочка", re.I), "Ты так смело"),
    (re.compile(r"своей девочке", re.I), "тебе"),
    (re.compile(r"за такой хрупкой девочки, как ты", re.I), "за такого хрупкого человека, как ты"),
    (re.compile(r"для такой хрупкой девочки, как ты", re.I), "для такого хрупкого человека, как ты"),
    (re.compile(r"для такой нежной девочки", re.I), "для такого хрупкого человека"),
    (re.compile(r"бледной девочки, как ты", re.I), "хрупкого человека, как ты"),
    (re.compile(r"хрупкой девочки, как ты", re.I), "хрупкого человека, как ты"),
    (re.compile(r"Девочка,", re.I), "@,"),
    (re.compile(r", девочка\.", re.I), ", @."),
    (re.compile(r", девочка\?", re.I), ", @?"),
    (re.compile(r"девочка\?", re.I), "@?"),
]


def apply_chain(text: str, replacements: list[tuple[re.Pattern, str]]) -> tuple[str, int]:
    total = 0
    out = text
    for pat, repl in replacements:
        out2, c = pat.subn(repl, out)
        if c:
            total += c
            out = out2
    return out, total


def process_file(path: Path, *, devochka: bool) -> tuple[int, int]:
    text = path.read_text(encoding="utf-8")
    fairy_total = 0
    dev_total = 0

    parts = re.split(r'(\{\s*\n\s*"Action":\s*"EditData")', text)
    out_parts = [parts[0]]
    idx = 1
    while idx < len(parts):
        head = parts[idx]
        body = parts[idx + 1] if idx + 1 < len(parts) else ""
        block = head + body
        idx += 2

        skip_devochka = WHEN_DATING_MARRIED.search(block) is not None

        def repl_entry(m: re.Match) -> str:
            nonlocal fairy_total, dev_total
            key, val = m.group(1), m.group(2)
            new_val, c = apply_chain(val, FAIRY_REPLACEMENTS)
            fairy_total += c
            if devochka and not skip_devochka and not KEEP_KEY.search(f'"{key}"'):
                new_val, c2 = apply_chain(new_val, DEVOCHKA_REPLACEMENTS)
                dev_total += c2
            return f'"{key}": "{new_val}"'

        if '"Target"' in block and "Harvey" not in block and path.name != "dialoguesNpc.json":
            new_block = block
        else:
            new_block = re.sub(
                r'"([^"]+)":\s*"((?:[^"\\]|\\.)*)"',
                repl_entry,
                block,
                flags=re.S,
            )
        out_parts.append(new_block)

    new_text = "".join(out_parts)
    if new_text != text:
        path.write_text(new_text, encoding="utf-8")
    return fairy_total, dev_total


def main() -> None:
    f_all = d_all = 0
    for p in HARVEY_FILES + NPC_FILES:
        if not p.exists():
            continue
        f, d = process_file(p, devochka=p.name != "dialoguesNpc.json")
        print(f"{p.name}: fairy={f}, devochka={d}")
        f_all += f
        d_all += d
    print(f"TOTAL fairy={f_all}, devochka={d_all}")


if __name__ == "__main__":
    main()

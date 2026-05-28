#!/usr/bin/env python3
"""Find intimate Harvey phrases outside Dating/Married gates."""
from __future__ import annotations

import re
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")

PATTERNS = [
    r"солнышко",
    r"моя дорогая",
    r"\bмалышка\b",
    r"хорошая девочка",
    r"\bлюбимая\b",
    r"я люблю тебя",
    r"кто-то, кто очень тебя любит",
    r"девочка моя",
    r"моя маленькая",
    r"\bкотёнок\b",
    r"\bкотенок\b",
    r"моя хорошая",
    r"\bСолнце,",
    r"Ты - моя",
    r"Ты — моя",
]
COMBINED = re.compile("|".join(f"({p})" for p in PATTERNS), re.I)

DATING_MARRIED_KEY = re.compile(
    r"PLAYER_NPC_RELATIONSHIP\s+Current\s+Harvey\s+(?:Dating|Married)", re.I
)
WHEN_DATING = re.compile(r'"Relationship:Harvey"\s*:\s*"(?:Dating|Married)"')
KEEP_KEY = re.compile(
    r'"(?:dating_|married_|marriage|AcceptBouquet|RejectBouquet_AlreadyAccepted|FlowerDance_Accept_Spouse)',
    re.I,
)

EVENT_FILES = ["events.json", "eventsCare.json", "eventsMineRescue.json"]
DIALOGUE_FILES = sorted(
    p.name
    for p in CP.glob("dialoguesHarvey*.json")
)


def audit_events(path: Path) -> list[tuple[str, str, str, str]]:
    text = path.read_text(encoding="utf-8")
    issues: list[tuple[str, str, str, str]] = []
    for m in re.finditer(r'"([A-Za-z][A-Za-z0-9_.]*(?:/[^"]*)?)":\s*"', text):
        key = m.group(1)
        start = m.end()
        end = start
        while end < len(text):
            if text[end] == '"' and text[end - 1] != "\\":
                break
            end += 1
        val = text[start:end]
        if not COMBINED.search(val):
            continue
        if DATING_MARRIED_KEY.search(key):
            continue
        for pm in COMBINED.finditer(val):
            issues.append((path.name, key.split("/")[0], pm.group(0), key))
    return issues


def audit_dialogue(path: Path) -> list[tuple[str, str, str, str]]:
    text = path.read_text(encoding="utf-8")
    blocks = re.split(r'(\{\s*\n\s*"Action":\s*"EditData")', text)
    issues: list[tuple[str, str, str, str]] = []
    idx = 1
    while idx < len(blocks):
        full = blocks[idx] + (blocks[idx + 1] if idx + 1 < len(blocks) else "")
        idx += 2
        gated = bool(
            WHEN_DATING.search(full)
            or ('"Target"' in full and "MarriageDialogueHarvey" in full)
        )
        for m in re.finditer(r'"([^"]+)":\s*"((?:[^"\\]|\\.)*)"', full, re.S):
            key, val = m.group(1), m.group(2)
            if not COMBINED.search(val):
                continue
            if gated or KEEP_KEY.search(f'"{key}"'):
                continue
            for pm in COMBINED.finditer(val):
                issues.append((path.name, key, pm.group(0), "no When Dating/Married"))
    return issues


def main() -> None:
    all_issues: list[tuple[str, str, str, str]] = []
    for name in EVENT_FILES:
        p = CP / name
        if p.exists():
            all_issues.extend(audit_events(p))
    for name in DIALOGUE_FILES:
        all_issues.extend(audit_dialogue(CP / name))

    print(f"VIOLATIONS: {len(all_issues)}")
    for row in all_issues:
        print(" | ".join(row))


if __name__ == "__main__":
    main()

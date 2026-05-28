#!/usr/bin/env python3
import re
from pathlib import Path

EVENTS = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\events.json"
)
text = EVENTS.read_text(encoding="utf-8")
KEY_RE = re.compile(
    r'"(HarveyOverhaul(?:Story|Romance)\.[^"]*Friendship Harvey (\d+)[^"]*)": "'
)
SPEAK_RE = re.compile(r'speak Harvey \\"((?:[^"\\]|\\.)*?)\\"', re.DOTALL)
FORMAL = re.compile(
    r"(Вы | вы |вас|вам|ваш|Ваш|пейте|поешьте|Садитесь|Давайте|Стойте|"
    r"Не двигайтесь|слушайте|Позвольте|покажите|Можете |Не хотите|"
    r"встанете|держать вас|слушайте|обязаны|захотите)",
    re.I,
)


def find_string_end(t: str, start: int) -> int:
    i = start
    while i < len(t):
        if t[i] == "\\":
            i += 2
            continue
        if t[i] == '"':
            return i
        i += 1
    raise ValueError("unterminated")


issues = []
for m in KEY_RE.finditer(text):
    fp = int(m.group(2))
    if fp < 750:
        continue
    eid = m.group(1).split("/")[0]
    body = text[m.end() : find_string_end(text, m.end())]
    for sm in SPEAK_RE.finditer(body):
        hit = FORMAL.search(sm.group(1))
        if hit:
            issues.append((eid, fp, hit.group(0), sm.group(1)[:100]))

if issues:
    print(f"Found {len(issues)} formal hits in speak Harvey (FP>=750):")
    for row in issues:
        print(f"  {row[0]} ({row[1]} FP) [{row[2]}]: {row[3]}...")
else:
    print("OK: no formal pronouns in speak Harvey for story/romance events FP>=750")

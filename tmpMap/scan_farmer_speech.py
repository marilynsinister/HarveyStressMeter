import re
from pathlib import Path

path = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\events.json")
text = path.read_text(encoding="utf-8")
parts = re.split(r'("HarveyOverhaulStory\.E[0-9][^"]+")', text)
events = {}
for i in range(1, len(parts), 2):
    key = parts[i].strip('"')
    if not re.match(r"HarveyOverhaulStory\.E[0-9]", key):
        continue
    body = parts[i + 1] if i + 1 < len(parts) else ""
    events[key.split("/")[0]] = body

issues = []
for eid, body in sorted(events.items()):
    if re.search(r"speak farmer", body, re.I):
        issues.append((eid, "speak farmer"))
    for m in re.findall(r'message \\"([^\\"]+)\\"', body):
        low = m.lower()
        if re.search(r"^(я |мне |спасибо\.|нет,|да,|прости|извини|не могу)", low):
            issues.append((eid, f"farmer speech: {m[:90]}"))
        if any(w in low for w in ["говоришь", "сказала", "шепчешь", "ответила", "призна"]):
            issues.append((eid, f"speech verb: {m[:90]}"))
    for qq in re.findall(r"quickQuestion ([^/]+)/", body):
        if qq.startswith('"'):
            issues.append((eid, f"QQ quoted prompt: {qq[:90]}"))
        for o in qq.split("#"):
            o = o.strip()
            if not o or o.startswith('"'):
                continue
            if len(o.split()) > 7:
                issues.append((eid, f"long option ({len(o.split())}w): {o[:90]}"))

    # Harvey line ending with ? not followed by quickQuestion within 500 chars
    for m in re.finditer(r'speak Harvey \\"([^\\"]*\?)\\"', body):
        q = m.group(1)
        after = body[m.end() : m.end() + 800]
        if "quickQuestion" not in after and "?" in q:
            if any(w in q.lower() for w in ["вы ", "теб", "вам", "хотите", "что ", "как ", "если", "можете"]):
                issues.append((eid, f"unanswered Q: {q[:90]}"))

for eid, issue in issues:
    print(f"{eid}: {issue}")
print("---")
print("Events:", len(events))

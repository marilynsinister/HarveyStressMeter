from pathlib import Path
import re

CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
)

def find_json_string_end(text, value_start):
    i = value_start + 1
    while i < len(text):
        if text[i] == "\\":
            i += 2
            continue
        if text[i] == '"':
            return i + 1
        i += 1
    return -1

for fn in ["events.json", "eventsCare.json"]:
    text = (CP / fn).read_text(encoding="utf-8")
    key_re = re.compile(r'"((?:[^"\\]|\\.)+)"\s*:\s*"')
    for m in key_re.finditer(text):
        fk = m.group(1)
        if "quickQuestion" not in text[m.start():m.start()+50000]:
            continue
        pos = m.end() - 1
        end = find_json_string_end(text, pos)
        if end < 0:
            continue
        raw = text[pos+1:end-1]
        if "quickQuestion" not in raw:
            continue
        # unformatted: quickQuestion on same line as prior cmd (no newline before quickQuestion in raw)
        if re.search(r'[^/\n]\s*\n\s*quickQuestion', raw):
            ok = True
        elif re.search(r'/quickQuestion|/\s*\n\s*[^q].*\n\s*quickQuestion', raw):
            ok = True
        else:
            ok = not re.search(r'/\s*\n\s*quickQuestion', raw) and not raw.strip().startswith("\n")
        # simpler: line contains quickQuestion AND another command ending with /
        bad = []
        for line in raw.split("\n"):
            if "quickQuestion" not in line:
                continue
            stripped = line.strip()
            if stripped.count("/") > 1 and not stripped.startswith("quickQuestion"):
                bad.append("multi-cmd line")
            if not stripped.startswith("quickQuestion") and "quickQuestion" in line:
                bad.append("qq not at line start")
        eid = fk.split("/")[0]
        if bad:
            print(f"CHECK {eid} ({fn}): {bad}")

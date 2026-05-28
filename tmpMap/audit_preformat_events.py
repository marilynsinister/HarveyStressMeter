#!/usr/bin/env python3
"""Pre-format safety audit for HarveyOverhaul CP event scripts."""
import json
import re
import sys
from pathlib import Path

CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
)

EVENT_FILES = {
    "events.json": True,
    "eventsCare.json": True,
    "eventsMineRescue.json": True,
    "events_for_mode_new_formatted.json": False,
}

EVENT_KEY_RE = re.compile(
    r'"((?:[A-Za-z][A-Za-z0-9_.]*)(?:/[^"]*)?)"\s*:\s*(?:"((?:\\.|[^"\\])*)"|(\[))',
    re.S,
)
TARGET_RE = re.compile(r'"Target"\s*:\s*"(Data/Events/[^"]+)"')
FORK_ONLY_KEYS = {
    "acceptWalk",
    "declineFood",
    "refuseCheckup",
    "irregularEating",
    "HarveySkullPromise",
    "leaveHospital",
}
SKIP_KEYS = {
    "Action", "Target", "FromFile", "LogName", "When", "Fields", "Entries",
    "Value", "Format", "Schema", "Changes", "DynamicTokens", "Priority",
    "PatchMode", "Update", "Include", "Text", "Name",
}

MUSIC_TOKENS = {
    "none", "continue", "ocean", "rain", "spring_day_ambient", "nightTime",
    "night_market", "Hospital_Ambient", "woodsTheme", "sam_acoustic1",
}


def strip_json_comments(text: str) -> str:
    out = []
    for line in text.splitlines():
        if line.strip().startswith("//"):
            continue
        if "//" in line:
            in_str = False
            esc = False
            cut = len(line)
            for i, ch in enumerate(line):
                if esc:
                    esc = False
                    continue
                if ch == "\\":
                    esc = True
                    continue
                if ch == '"':
                    in_str = not in_str
                    continue
                if not in_str and line[i : i + 2] == "//":
                    cut = i
                    break
            line = line[:cut]
        out.append(line)
    return "\n".join(out)


def script_from_array(text: str, start_pos: int) -> tuple[str, int]:
    depth = 0
    i = start_pos
    while i < len(text):
        if text[i] == "[":
            depth += 1
        elif text[i] == "]":
            depth -= 1
            if depth == 0:
                arr_text = text[start_pos : i + 1]
                try:
                    parts = json.loads(arr_text)
                    return "/".join(str(p) for p in parts), i + 1
                except json.JSONDecodeError:
                    return arr_text, i + 1
        i += 1
    return "", start_pos


def normalize_script(raw: str) -> str:
    s = raw.replace("\\n", "\n").replace('\\"', '"')
    s = re.sub(r"\s*\n\s*", "", s)  # join multiline physical lines
    s = s.replace("\\\\", "\\")  # JSON escaped backslashes in quickQuestion
    return s.strip()


def split_commands(script: str) -> list[str]:
    """Split event script on / outside quotes."""
    parts = []
    buf = []
    in_quote = False
    esc = False
    for ch in script:
        if esc:
            buf.append(ch)
            esc = False
            continue
        if ch == "\\":
            buf.append(ch)
            esc = True
            continue
        if ch == '"':
            in_quote = not in_quote
            buf.append(ch)
            continue
        if ch == "/" and not in_quote:
            part = "".join(buf).strip()
            if part:
                parts.append(part)
            buf = []
            continue
        buf.append(ch)
    tail = "".join(buf).strip()
    if tail:
        parts.append(tail)
    return parts


def quote_balance_issues(script: str) -> list[str]:
    issues = []
    # raw quote count outside escapes
    n = 0
    esc = False
    for ch in script:
        if esc:
            esc = False
            continue
        if ch == "\\":
            esc = True
            continue
        if ch == '"':
            n += 1
    if n % 2 != 0:
        issues.append(f"нечётное число кавычек ({n})")

    # suspicious unclosed speak/message patterns (heuristic)
    for m in re.finditer(r"\b(speak|message|question|quickQuestion)\b([^/]{0,200})", script, re.I):
        chunk = m.group(0)
        q = chunk.count('"') - chunk.count('\\"')
        if q % 2 != 0:
            issues.append(f"возможно незакрытая кавычка около {m.group(1)}")

    # slash inside quoted dialogue (formatting trap)
    if re.search(r'speak\s+\w+\s+"[^"]*\d/\d[^"]*"', script):
        issues.append('слэш внутри текста speak (например "110/70") — опасно резать по /')

    return issues


def check_header(cmds: list[str]) -> list[str]:
    issues = []
    if len(cmds) < 3:
        return [f"меньше 3 команд ({len(cmds)})"]

    c0 = cmds[0].split()[0] if cmds[0].split() else cmds[0]
    if c0.lower() not in MUSIC_TOKENS and not re.match(r"^[A-Za-z0-9_]+$", c0):
        issues.append(f"команда 1 (музыка): неожиданное «{cmds[0][:60]}»")
    elif c0.lower() not in MUSIC_TOKENS and c0 not in ("spring_day_ambient",):
        # allow unknown music ids but flag if doesn't look like music
        if " " in cmds[0] and not cmds[0].startswith(("none", "continue")):
            issues.append(f"команда 1 (музыка): «{cmds[0][:60]}»")

    # viewport: two numbers
    vp = cmds[1].strip()
    if not re.match(r"^-?\d+\s+-?\d+$", vp):
        issues.append(f"команда 2 (viewport): не координаты «{cmds[1][:60]}»")

    # character setup: farmer ...
    c2 = cmds[2].strip()
    if not re.match(r"^farmer\s+-?\d+\s+-?\d+\s+[0-3]\b", c2, re.I):
        if not re.match(r"^[A-Za-z]+\s+-?\d+\s+-?\d+\s+[0-3]", c2):
            issues.append(f"команда 3 (персонажи): не farmer/NPC setup «{cmds[2][:70]}»")

    return issues


def dialogue_risk(script: str, cmds: list[str]) -> dict:
    has_speak = bool(re.search(r"\bspeak\b", script, re.I))
    has_message = bool(re.search(r"\bmessage\b", script, re.I))
    has_quick = bool(re.search(r"\bquickQuestion\b", script, re.I))
    has_question = bool(re.search(r"\bquestion\b", script, re.I))
    has_fork = bool(re.search(r"\bfork\b", script, re.I))
    has_choice = bool(re.search(r"\bchoice\b", script, re.I))

    break_count = len(re.findall(r"\(break\)", script, re.I))
    backslash_chain = bool(re.search(r"\(break\)[^/]*\\\\", script))
    qq_inline = bool(re.search(r"quickQuestion[^/]{20,}\(break\)", script, re.I))
    qq_multiline_array = "(break)" in script and script.count("/") > 20

    # setSkipActions uses # — naive split dangerous
    has_skip_actions = bool(re.search(r"\bsetSkipActions\b", script, re.I))

    # slash inside quickQuestion branch joined by \\
    branch_speaks = re.findall(r"\(break\)(.*?)(?=\(break\)|/quickQuestion|/question|/end\b|$)", script, re.I | re.S)

    risks = []
    if has_quick:
        risks.append("quickQuestion")
    if break_count:
        risks.append(f"(break) x{break_count}")
    if backslash_chain or qq_inline:
        risks.append("ветки через \\\\ в одной строке")
    if has_question:
        risks.append("question")
    if has_fork:
        risks.append("fork")
    if has_choice:
        risks.append("choice")
    if has_skip_actions:
        risks.append("setSkipActions (#-разделитель)")
    if re.search(r"quickQuestion\s+null#", script, re.I):
        risks.append("quickQuestion null#…")

    # Text commands with quotes count
    quoted_cmds = len(re.findall(r'\b(speak|message)\s+\S+\s+"', script, re.I))
    quoted_cmds += len(re.findall(r'\bmessage\s+"', script, re.I))

    return {
        "has_speak": has_speak,
        "has_message": has_message,
        "has_quick": has_quick,
        "has_question": has_question,
        "has_fork": has_fork,
        "break_count": break_count,
        "risks": risks,
        "quoted_cmds": quoted_cmds,
        "branch_speaks": len(branch_speaks),
    }


def risk_score(row: dict) -> int:
    score = 0
    score += len(row["header_issues"]) * 3
    score += len(row["quote_issues"]) * 4
    if row["dialogue"]["has_quick"]:
        score += 3
    score += row["dialogue"]["break_count"] * 2
    if "ветки через \\\\ в одной строке" in row["dialogue"]["risks"]:
        score += 4
    if row["dialogue"]["has_fork"] or row["dialogue"]["has_question"]:
        score += 2
    if row["is_fork"]:
        score += 2
    if row["format"] == "single_long_line":
        score += 1
    if row["dialogue"]["quoted_cmds"] > 10:
        score += 1
    return score


def parse_events_file(fn: str, included: bool) -> list[dict]:
    path = CP / fn
    text = strip_json_comments(path.read_text(encoding="utf-8"))
    targets = [(m.start(), m.group(1)) for m in TARGET_RE.finditer(text)]
    rows = []

    for idx, (pos, target) in enumerate(targets):
        end = targets[idx + 1][0] if idx + 1 < len(targets) else len(text)
        chunk = text[pos:end]
        location = target.replace("Data/Events/", "")

        entries_m = re.search(r'"Entries"\s*:\s*\{', chunk)
        if not entries_m:
            continue
        entries_start = entries_m.end()
        entries_end_m = re.search(
            r'\n\s{4,8}\}(?:\s*,\s*\n|\s*\n\s*\})', chunk[entries_start:]
        )
        entries_text = (
            chunk[entries_start : entries_start + entries_end_m.start()]
            if entries_end_m
            else chunk[entries_start:]
        )

        for m in EVENT_KEY_RE.finditer(entries_text):
            full_key = m.group(1)
            base = full_key.split("/")[0]
            if base in SKIP_KEYS:
                continue
            is_fork = base in FORK_ONLY_KEYS

            if m.group(3) == "[":
                raw, _ = script_from_array(entries_text, m.end() - 1)
                fmt = "json_array"
            else:
                raw = m.group(2) or ""
                fmt = "multiline" if "\n" in raw else "single_long_line"

            script = normalize_script(raw)
            cmds = split_commands(script)
            header_issues = [] if is_fork else check_header(cmds)
            quote_issues = quote_balance_issues(script)
            dlg = dialogue_risk(script, cmds)

            rows.append(
                {
                    "file": fn,
                    "included": included,
                    "location": location,
                    "event_id": base,
                    "full_key": full_key,
                    "is_fork": is_fork,
                    "format": fmt,
                    "cmd_count": len(cmds),
                    "header_first3": cmds[:3] if cmds else [],
                    "header_issues": header_issues,
                    "quote_issues": quote_issues,
                    "dialogue": dlg,
                }
            )
    return rows


def classify_tier(row: dict) -> tuple[str, list[str]]:
    if row["is_fork"]:
        return "HIGH", ["fork-subevent (нет стандартного header)"]

    flags: list[str] = []
    cmds = row["header_first3"]
    music_ok = MUSIC_TOKENS | {
        "Hospital_Ambient", "kindadumbautumn", "night_market", "ocean", "rain",
        "spring_day_ambient", "woodsTheme", "aerobics", "EarthMine", "sam_acoustic1",
        "nightTime", "Hospital_Ambient",
    }

    if cmds:
        c0 = cmds[0].split()[0]
        if c0 not in music_ok:
            flags.append(f"cmd1 не музыка: «{c0}»")
    if len(cmds) >= 3:
        c2 = cmds[2]
        if not re.match(r"^farmer\s", c2, re.I):
            flags.append(f"cmd3 farmer ne pervyj: «{c2[:50]}»")

    dlg = row["dialogue"]
    if dlg["has_quick"] and dlg["break_count"]:
        flags.append(f"quickQuestion + (break)x{dlg['break_count']}")
    elif dlg["has_quick"]:
        flags.append("quickQuestion")

    if dlg["has_question"]:
        flags.append("question")
    if dlg["has_fork"]:
        flags.append("fork")
    if any("setSkipActions" in x for x in dlg["risks"]):
        flags.append("setSkipActions (#)")

    if row["format"] == "json_array":
        flags.append("JSON-array (ветки отдельными элементами)")

    if row["format"] == "single_long_line":
        flags.append("уже one-liner")

    # slash inside dialogue — real formatting trap
    script = row.get("_script", "")
    if re.search(r'speak\s+\w+\s+"[^"]*\d/\d[^"]*"', script):
        flags.append('слэш в тексте speak ("80/50", "110/70")')

    if re.search(r'\bmessage\s+Harvey\b', script, re.I):
        flags.append("message Harvey (не speak) — нестандартный синтаксис")

    if not flags:
        return "SAFE", ["OK"]

    high_markers = (
        "cmd1 не музыка",
        "cmd3 без farmer",
        "quickQuestion + (break)",
        "fork-subevent",
        "JSON-array",
        'слэш в тексте speak',
        "message Harvey",
    )
    tier = "HIGH" if any(any(m in f for m in high_markers) for f in flags) else "MED"
    return tier, flags


def attach_scripts(rows: list[dict]) -> None:
    """Re-parse to attach normalized script for slash checks."""
    for fn in EVENT_FILES:
        path = CP / fn
        text = strip_json_comments(path.read_text(encoding="utf-8"))
        targets = [(m.start(), m.group(1)) for m in TARGET_RE.finditer(text)]
        for idx, (pos, target) in enumerate(targets):
            end = targets[idx + 1][0] if idx + 1 < len(targets) else len(text)
            chunk = text[pos:end]
            entries_m = re.search(r'"Entries"\s*:\s*\{', chunk)
            if not entries_m:
                continue
            entries_start = entries_m.end()
            entries_end_m = re.search(
                r'\n\s{4,8}\}(?:\s*,\s*\n|\s*\n\s*\})', chunk[entries_start:]
            )
            entries_text = (
                chunk[entries_start : entries_start + entries_end_m.start()]
                if entries_end_m
                else chunk[entries_start:]
            )
            for m in EVENT_KEY_RE.finditer(entries_text):
                base = m.group(1).split("/")[0]
                if m.group(3) == "[":
                    raw, _ = script_from_array(entries_text, m.end() - 1)
                else:
                    raw = m.group(2) or ""
                script = normalize_script(raw)
                for row in rows:
                    if row["file"] == fn and row["event_id"] == base:
                        row["_script"] = script


def main() -> None:
    all_rows = []
    for fn, included in EVENT_FILES.items():
        all_rows.extend(parse_events_file(fn, included))

    attach_scripts(all_rows)

    for row in all_rows:
        row["tier"], row["flags"] = classify_tier(row)
        row["score"] = risk_score(row)

    by_tier = {"HIGH": [], "MED": [], "SAFE": []}
    for row in all_rows:
        by_tier[row["tier"]].append(row)

    print(f"TOTAL SCRIPTS: {len(all_rows)}")
    for tier in ("HIGH", "MED", "SAFE"):
        print(f"\n=== {tier} ({len(by_tier[tier])}) ===")
        for r in sorted(by_tier[tier], key=lambda x: (x["file"], x["event_id"])):
            inc = "active" if r["included"] else "NOT linked"
            print(
                f"  [{inc}] {r['event_id']} @ {r['location']} ({r['file']})"
            )
            print(f"           {', '.join(r['flags'])}")
            if r["header_first3"] and not r["is_fork"]:
                print(
                    f"           first3: {' | '.join(x[:45] for x in r['header_first3'])}"
                )


if __name__ == "__main__":
    main()

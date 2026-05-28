#!/usr/bin/env python3
"""Validate HarveyOverhaul CP JSON files after event formatting."""
from __future__ import annotations

import json
import re
from pathlib import Path

CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
)

MODIFIED = ["events.json", "eventsCare.json"]
ALL_EVENT_FILES = [
    "events.json",
    "eventsCare.json",
    "eventsMineRescue.json",
    "events_for_mode_new_formatted.json",
]

TARGET_RE = re.compile(r'"Target"\s*:\s*"(Data/Events[^"]*)"')
JSONC_LINE = re.compile(r"^\s*//")
JSONC_INLINE = re.compile(r"(?<!:)//")
JSONC_BLOCK = re.compile(r"/\*[\s\S]*?\*/")
TRAILING_COMMA = re.compile(r",\s*([}\]])")


def check_jsonc(text: str, path: str) -> list[str]:
    issues = []
    for i, line in enumerate(text.splitlines(), 1):
        if JSONC_LINE.match(line):
            issues.append(f"{path}:{i}: line comment //")
            continue
        if "//" in line:
            in_str = False
            esc = False
            for j, ch in enumerate(line):
                if esc:
                    esc = False
                    continue
                if ch == "\\":
                    esc = True
                    continue
                if ch == '"':
                    in_str = not in_str
                    continue
                if not in_str and line[j : j + 2] == "//":
                    issues.append(f"{path}:{i}: inline comment //")
                    break
    for m in JSONC_BLOCK.finditer(text):
        line = text.count("\n", 0, m.start()) + 1
        issues.append(f"{path}:{line}: block comment /* */")
    return issues


def check_trailing_commas(text: str, path: str) -> list[str]:
    issues = []
    for i, line in enumerate(text.splitlines(), 1):
        stripped = line.strip()
        if TRAILING_COMMA.search(stripped):
            issues.append(f"{path}:{i}: possible trailing comma: {stripped[:80]}")
    return issues


def parse_json_strict(text: str, path: str) -> tuple[dict | None, list[str]]:
    issues = []
    try:
        return json.loads(text), issues
    except json.JSONDecodeError as e:
        issues.append(f"{path}:{e.lineno}:{e.colno}: JSON parse error: {e.msg}")
        return None, issues


def walk_event_entries(obj, path: str, file_path: str, issues: list[str]) -> None:
    if isinstance(obj, dict):
        target = obj.get("Target", "")
        if target.startswith("Data/Events") and "Entries" in obj:
            entries = obj["Entries"]
            if not isinstance(entries, dict):
                issues.append(f"{file_path}: Changes[].Entries @ {target}: not an object")
            else:
                for key, val in entries.items():
                    loc = f'{file_path} → Target "{target}" → Entries["{key[:60]}..."]' if len(key) > 60 else f'{file_path} → Target "{target}" → Entries["{key}"]'
                    if isinstance(val, list):
                        issues.append(f"{loc}: value is array, expected string")
                    elif not isinstance(val, str):
                        issues.append(f"{loc}: value type {type(val).__name__}, expected string")
                    else:
                        issues.extend(check_script_quotes(val, loc))
        for v in obj.values():
            walk_event_entries(v, path, file_path, issues)
    elif isinstance(obj, list):
        for item in obj:
            walk_event_entries(item, path, file_path, issues)


def check_script_quotes(script: str, loc: str) -> list[str]:
    """Validate JSON-style escapes inside decoded script string."""
    issues = []
    i = 0
    while i < len(script):
        if script[i] == "\\":
            if i + 1 >= len(script):
                issues.append(f"{loc}: dangling backslash at end")
                break
            nxt = script[i + 1]
            if nxt == '"':
                i += 2
                continue
            if nxt == "\\":
                i += 2
                continue
            if nxt == "/":
                i += 2
                continue
            if nxt == "n":
                i += 2
                continue
            # other escapes allowed in JSON strings
            i += 2
            continue
        if script[i] == '"':
            issues.append(f"{loc}: unescaped double quote inside script at pos {i}")
            break
        i += 1
    return issues


def reencode_roundtrip(file_path: Path, text: str) -> list[str]:
    """Ensure file text round-trips through JSON string encoding for event values."""
    issues = []
    data, parse_issues = parse_json_strict(text, str(file_path))
    if parse_issues or data is None:
        return parse_issues
    # Re-read raw event string bodies and compare to json.loads extraction
    key_re = re.compile(r'"((?:[^"\\]|\\.)+)"\s*:\s*(?:"|\[)')
    for m in key_re.finditer(text):
        full_key = m.group(1)
        if m.group(0).endswith("["):
            continue
        # only check event-like keys in Data/Events sections
        if not any(
            full_key.startswith(p)
            for p in (
                "event", "HarveyMod", "HarveyOverhaulStory", "MyMod_",
                "acceptWalk", "declineFood", "refuseCheckup", "irregularEating",
                "HarveySkullPromise", "leaveHospital",
            )
        ) and not re.match(r"^\d+/", full_key):
            continue
    return issues


def find_json_string_end(text: str, value_start: int) -> int:
    i = value_start + 1
    while i < len(text):
        if text[i] == "\\":
            i += 2
            continue
        if text[i] == '"':
            return i + 1
        i += 1
    return -1


def raw_string_issues(text: str, file_path: str) -> list[str]:
    """Check raw file for corrupt patterns in JSON string literals."""
    issues = []
    if "/,," in text or re.search(r'\\"/\n\s*,\n', text):
        issues.append(f"{file_path}: corrupt pattern '/,,' or quote-comma in script")
    if re.search(r'\\"/\n\s*,\s*\n\s+[a-z]', text):
        issues.append(f"{file_path}: JSON string may close early before comma line")
    # odd number of unescaped quotes in each event value (raw JSON body)
    key_re = re.compile(
        r'"((?:event|HarveyMod|HarveyOverhaulStory|acceptWalk|declineFood|refuseCheckup|irregularEating|HarveySkullPromise|MyMod_)[^"]*)"\s*:\s*"'
    )
    for m in key_re.finditer(text):
        key = m.group(1)
        open_q = m.end() - 1
        close = find_json_string_end(text, open_q)
        if close < 0:
            issues.append(f'{file_path} → "{key[:50]}": unterminated string')
            continue
        body = text[open_q + 1 : close - 1]
        q = 0
        i = 0
        while i < len(body):
            if body[i] == "\\" and i + 1 < len(body):
                if body[i + 1] == '"':
                    q += 1
                i += 2
                continue
            i += 1
        if q % 2 != 0:
            issues.append(f'{file_path} → "{key[:50]}": odd escaped quote count ({q})')
    return issues


def main() -> None:
    print("=== VALIDATION: modified CP event files ===\n")
    all_issues: dict[str, list[str]] = {}

    for fn in MODIFIED:
        path = CP / fn
        text = path.read_text(encoding="utf-8")
        issues: list[str] = []

        issues.extend(check_jsonc(text, fn))
        issues.extend(check_trailing_commas(text, fn))
        issues.extend(raw_string_issues(text, fn))

        data, parse_issues = parse_json_strict(text, fn)
        issues.extend(parse_issues)

        if data is not None:
            walk_event_entries(data, fn, fn, issues)
            # count event entries
            n_entries = 0
            n_string = 0
            n_array = 0

            def count_entries(obj):
                nonlocal n_entries, n_string, n_array
                if isinstance(obj, dict):
                    t = obj.get("Target", "")
                    if t.startswith("Data/Events") and isinstance(obj.get("Entries"), dict):
                        for _, v in obj["Entries"].items():
                            n_entries += 1
                            if isinstance(v, str):
                                n_string += 1
                            elif isinstance(v, list):
                                n_array += 1
                    for v in obj.values():
                        count_entries(v)
                elif isinstance(obj, list):
                    for x in obj:
                        count_entries(x)

            count_entries(data)
            print(f"{fn}:")
            print(f"  strict JSON parse: OK")
            print(f"  Data/Events Entries: {n_entries} total, {n_string} strings, {n_array} arrays")
        else:
            print(f"{fn}:")
            print(f"  strict JSON parse: FAILED")

        all_issues[fn] = issues

    print("\n=== ISSUES ===")
    total = 0
    for fn, issues in all_issues.items():
        if issues:
            print(f"\n{fn} ({len(issues)} issue(s)):")
            for iss in issues:
                print(f"  - {iss}")
            total += len(issues)
        else:
            print(f"\n{fn}: no issues")

    print(f"\n=== SUMMARY ===")
    if total == 0:
        print("All modified files pass validation.")
    else:
        print(f"Total issues: {total}")

    # Also quick-check other event files for comparison
    print("\n=== Other event CP files (parse-only) ===")
    for fn in ALL_EVENT_FILES:
        if fn in MODIFIED:
            continue
        path = CP / fn
        text = path.read_text(encoding="utf-8")
        _, pi = parse_json_strict(text, fn)
        jsonc = check_jsonc(text, fn)
        status = "OK" if not pi else "PARSE FAIL"
        print(f"  {fn}: {status}" + (f", JSONC lines: {len(jsonc)}" if jsonc else ""))


if __name__ == "__main__":
    main()

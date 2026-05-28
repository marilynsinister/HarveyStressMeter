#!/usr/bin/env python3
"""Format CP event scripts that contain quickQuestion (conservative)."""
from __future__ import annotations

import re
from pathlib import Path

CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
)

FILES = ["events.json", "eventsCare.json"]
INDENT = "    "
# Only mod / Harvey event keys (skip vanilla numeric ids like "58/...")
KEY_PREFIX_OK = re.compile(
    r"^(?:event|HarveyMod|HarveyOverhaulStory|MyMod_|acceptWalk|declineFood|refuseCheckup|irregularEating|HarveySkullPromise|leaveHospital)"
)


def split_script_raw(raw: str) -> list[str]:
    collapsed = re.sub(r"/\s*\n\s*", "/", raw)
    collapsed = collapsed.strip()

    parts: list[str] = []
    buf: list[str] = []
    in_quote = False
    i = 0
    while i < len(collapsed):
        ch = collapsed[i]
        if ch == "\\" and i + 1 < len(collapsed):
            nxt = collapsed[i + 1]
            buf.append(ch)
            buf.append(nxt)
            if nxt == '"':
                in_quote = not in_quote
            i += 2
            continue
        if ch == "/" and not in_quote:
            part = "".join(buf).strip()
            if part:
                parts.append(part)
            buf = []
            i += 1
            continue
        buf.append(ch)
        i += 1
    tail = "".join(buf).strip()
    if tail:
        parts.append(tail)
    return parts


def slash_outside_quotes(cmd: str) -> bool:
    in_q = False
    i = 0
    while i < len(cmd):
        if cmd[i] == "\\" and i + 1 < len(cmd):
            if cmd[i + 1] == '"':
                in_q = not in_q
            i += 2
            continue
        if cmd[i] == "/" and not in_q:
            return True
        i += 1
    return False


def format_script_value(raw: str) -> str:
    parts = split_script_raw(raw)
    lines = [f"{INDENT}{p}/" for p in parts[:-1]]
    lines.append(f"{INDENT}{parts[-1]}")
    return "\n" + "\n".join(lines) + "\n"


def find_json_string_end(text: str, value_start: int) -> int:
    i = value_start + 1
    while i < len(text):
        if text[i] == "\\":
            i += 2
            continue
        if text[i] == '"':
            return i + 1
        i += 1
    raise ValueError("unterminated JSON string")


def locate_value_span(text: str, full_key: str) -> tuple[int, int, str]:
    needle = f'"{full_key}"'
    pos = text.find(needle)
    if pos < 0:
        raise KeyError(full_key)
    after = pos + len(needle)
    m = re.search(r":\s*\"", text[after:])
    if not m:
        raise ValueError(f"value not found for {full_key}")
    value_open = after + m.end() - 1
    value_end = find_json_string_end(text, value_open)
    raw_value = text[value_open + 1 : value_end - 1]
    return value_open + 1, value_end - 1, raw_value


def is_commented_key(text: str, key_start: int) -> bool:
    line_start = text.rfind("\n", 0, key_start) + 1
    prefix = text[line_start:key_start]
    return prefix.strip().startswith("//")


def key_allowed(full_key: str) -> bool:
    base = full_key.split("/")[0]
    return bool(KEY_PREFIX_OK.match(base))


def discover_qq_events(path: Path) -> list[tuple[str, str]]:
    text = path.read_text(encoding="utf-8")
    found: list[tuple[str, str]] = []
    key_re = re.compile(r'"((?:[^"\\]|\\.)+)"\s*:\s*"')
    for m in key_re.finditer(text):
        if is_commented_key(text, m.start()):
            continue
        full_key = m.group(1)
        if not key_allowed(full_key):
            continue
        try:
            _, _, raw = locate_value_span(text, full_key)
        except (KeyError, ValueError):
            continue
        if re.search(r"\bquickQuestion\b", raw, re.I):
            found.append((full_key, full_key.split("/")[0]))
    seen: set[str] = set()
    out: list[tuple[str, str]] = []
    for full_key, event_id in found:
        if full_key in seen:
            continue
        seen.add(full_key)
        out.append((full_key, event_id))
    return out


def validate_qq_commands(parts: list[str]) -> list[str]:
    issues = []
    for p in parts:
        if not re.match(r"quickQuestion\b", p, re.I):
            continue
        if "\n" in p:
            issues.append("quickQuestion contains newline")
        if slash_outside_quotes(p):
            issues.append("quickQuestion contains / outside quotes")
    return issues


def main() -> None:
    formatted: list[str] = []
    unchanged: list[str] = []
    skipped: list[str] = []

    for fn in FILES:
        path = CP / fn
        text = path.read_text(encoding="utf-8")
        replacements: list[tuple[int, int, str, str]] = []

        for full_key, event_id in discover_qq_events(path):
            label = f"{event_id} ({fn})"
            try:
                inner_start, inner_end, raw_value = locate_value_span(text, full_key)
            except (KeyError, ValueError) as e:
                skipped.append(f"{label}: {e}")
                continue

            parts = split_script_raw(raw_value)
            if not any(re.match(r"quickQuestion\b", p, re.I) for p in parts):
                skipped.append(f"{label}: quickQuestion not found after split")
                continue

            qq_issues = validate_qq_commands(parts)
            if qq_issues:
                skipped.append(f"{label}: {', '.join(qq_issues)}")
                continue

            new_inner = format_script_value(raw_value)
            if text[inner_start:inner_end] == new_inner:
                unchanged.append(label)
                continue

            if split_script_raw(raw_value) != split_script_raw(new_inner):
                skipped.append(f"{label}: command mismatch")
                continue

            replacements.append((inner_start, inner_end, new_inner, label))

        new_text = text
        for inner_start, inner_end, new_inner, label in sorted(
            replacements, key=lambda x: x[0], reverse=True
        ):
            new_text = new_text[:inner_start] + new_inner + new_text[inner_end:]
            formatted.append(label)

        if new_text != text:
            path.write_text(new_text, encoding="utf-8", newline="\n")

    print("FORMATTED:", len(formatted))
    for x in sorted(formatted):
        print(" ", x)
    print("UNCHANGED:", len(unchanged))
    for x in sorted(unchanged):
        print(" ", x)
    print("SKIPPED:", len(skipped))
    for x in sorted(skipped):
        print(" ", x)


if __name__ == "__main__":
    main()

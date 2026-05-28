#!/usr/bin/env python3
"""Format only whitelisted safe CP event scripts (quote-aware / split)."""
from __future__ import annotations

import re
from pathlib import Path

CP = Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code"
)

SAFE_EVENT_IDS = {
    "HarveyMod_FirstTreatment",
    "eventHarveyCheckHealthFarmer",
    "eventHarveyFirstDate",
    "eventHarveyLateNightCollapse",
    "eventHarveyMountainDate",
    "eventHarveyPropose",
    "eventHarveyRoomCheckup",
    "eventHarveyRoomCheckup2",
    "eventHarveyTraumaExam",
    "eventStayInHospital",
    "eventHarveyEmergencyCare",
    "eventHarveyMineInterception",
}

FILES = ["events.json", "eventsCare.json"]
INDENT = "    "


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
    after_key = pos + len(needle)
    colon = text.find('": "', after_key)
    if colon < 0:
        raise ValueError(f"value not found for {full_key}")
    value_open = colon + 3  # opening quote of JSON string value
    value_end = find_json_string_end(text, value_open)
    raw_value = text[value_open + 1 : value_end - 1]
    return value_open + 1, value_end - 1, raw_value


def verify_safe(raw: str) -> list[str]:
    issues = []
    if re.search(r"\bquickQuestion\b", raw, re.I):
        issues.append("quickQuestion")
    if re.search(r"\bquestion\b", raw, re.I):
        issues.append("question")
    if re.search(r"\bfork\b", raw, re.I):
        issues.append("fork")
    if "(break)" in raw:
        issues.append("(break)")
    if re.search(r"\bsetSkipActions\b", raw, re.I):
        issues.append("setSkipActions")
    if re.search(r"\bmessage\s+Harvey\b", raw, re.I):
        issues.append("message Harvey")

    in_q = False
    i = 0
    while i < len(raw):
        if raw[i] == "\\" and i + 1 < len(raw):
            if raw[i + 1] == '"':
                in_q = not in_q
            i += 2
            continue
        if raw[i] == "/" and in_q:
            issues.append("slash inside quotes")
            break
        i += 1
    return issues


def discover_events(path: Path) -> list[tuple[str, str]]:
    text = path.read_text(encoding="utf-8")
    found: list[tuple[str, str]] = []
    key_re = re.compile(r'"((?:[^"\\]|\\.)+)"\s*:\s*"')
    for m in key_re.finditer(text):
        full_key = m.group(1)
        event_id = full_key.split("/")[0]
        if event_id in SAFE_EVENT_IDS:
            found.append((full_key, event_id))
    # dedupe by full_key keep first
    seen = set()
    out = []
    for full_key, event_id in found:
        if full_key in seen:
            continue
        seen.add(full_key)
        out.append((full_key, event_id))
    return out


def main() -> None:
    formatted: list[str] = []
    skipped: list[str] = []
    unchanged: list[str] = []

    for fn in FILES:
        path = CP / fn
        text = path.read_text(encoding="utf-8")
        replacements: list[tuple[int, int, str, str]] = []

        for full_key, event_id in discover_events(path):
            label = f"{event_id} ({fn})"
            try:
                inner_start, inner_end, raw_value = locate_value_span(text, full_key)
            except (KeyError, ValueError) as e:
                skipped.append(f"{label}: {e}")
                continue

            issues = verify_safe(raw_value)
            if issues:
                skipped.append(f"{label}: {', '.join(issues)}")
                continue

            new_inner = format_script_value(raw_value)
            if text[inner_start:inner_end] == new_inner:
                unchanged.append(label)
                continue

            old_parts = split_script_raw(raw_value)
            new_parts = split_script_raw(new_inner)
            if old_parts != new_parts:
                skipped.append(f"{label}: command mismatch after format")
                continue

            replacements.append((inner_start, inner_end, new_inner, label))

        # apply from end to start on original text only
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

#!/usr/bin/env python3
"""Full HarveyOverhaul CP + InjuryCare C# event inventory."""
import json
import re
from collections import defaultdict
from datetime import date
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
CS = Path(r"C:\Users\Admin\HarveyOverhaulInjury")
OUT = CS / "docs" / "events-inventory"

EVENT_FILES = {
    "events.json": True,
    "eventsCare.json": True,
    "eventsMineRescue.json": True,
    "events_for_mode_new_formatted.json": False,
}

CMD_PATTERNS = [
    "addMail", "AddConversationTopic", "addConversationTopic", "changeLocation",
    "warp", "friendship", "friend", "setFlag", "end", "fork", "globalFade",
    "action", "removeBuff", "addBuff", "addQuest", "removeQuest", "switchEvent",
    "question", "cutscene", "mail", "removeConversationTopic", "quickQuestion",
]

BRIDGE_RE = re.compile(
    r"(?:AddConversationTopic|addConversationTopic|addMail|mail|AddMail|removeConversationTopic)\s+([A-Za-z0-9_]+)",
    re.I,
)
EVENT_KEY_RE = re.compile(
    r'"((?:[A-Za-z][A-Za-z0-9_.]*)(?:/[^"]*)?)"\s*:\s*(?:"((?:\\.|[^"\\])*)"|(\[))',
    re.S,
)
SKIP_KEYS = {
    "Action", "Target", "FromFile", "LogName", "When", "Fields", "Entries",
    "Value", "Format", "Schema", "Changes", "DynamicTokens", "Priority",
    "PatchMode", "Update", "Include", "Fields", "Text", "Name",
}
FORK_ONLY_KEYS = {
    "acceptWalk", "declineFood", "refuseCheckup", "irregularEating",
    "HarveySkullPromise", "leaveHospital",
}

# Topics/buffs set by C# launchers (constants — not always found by regex scan)
EXPLICIT_CS_TOPICS = {
    "topicDiagnosisComplete": "Managers/DialogueManager.cs (TryAddDiagnosisCompleteTopic)",
    "topicRescueOperation": "EventHandlers/RescueOperationLauncher.cs",
    "HarveyMod_CD_RescueOperation": "EventHandlers/RescueOperationLauncher.cs",
    "topicHarveyStormStress": "EventHandlers/StormComfortLauncher.cs",
    "HarveyMod_CD_StormComfort": "EventHandlers/StormComfortLauncher.cs",
    "topicHarveyMinorMineRescue": "EventHandlers/PassOutHandler.cs",
    "topicHarveyExhaustion": "EventHandlers/PassOutHandler.cs (fallback)",
}

EXPLICIT_CS_EVENTS = {
    "eventHarveyEmergencyCare": "EventHandlers/PassOutHandler.cs (QueueHospitalEvent)",
    "eventHarveyExhaustion": "EventHandlers/PassOutHandler.cs (QueueHospitalEvent)",
    "eventHarveyMinorMineRescue": "EventHandlers/PassOutHandler.cs, PlayerEventHandler.cs",
    "eventRescueOperation": "EventHandlers/RescueOperationLauncher.cs (topic gate)",
}
TARGET_RE = re.compile(r'"Target"\s*:\s*"(Data/Events/[^"]+)"')


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


def extract_commands(script: str) -> list[str]:
    found = []
    for cmd in CMD_PATTERNS:
        if re.search(rf"(?:^|/|\\|\()(?:{re.escape(cmd)})\b", script, re.I):
            found.append(cmd)
    return found


def script_from_array(text: str, start_pos: int) -> tuple[str, int]:
    """Parse JSON array event script starting at '['."""
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
                    inner = arr_text[1:-1]
                    return inner.replace('"', "").replace("\n", "/"), i + 1
        i += 1
    return "", start_pos


def parse_events_file(fn: str, included: bool) -> list[dict]:
    path = CP / fn
    if not path.exists():
        return []
    text = strip_json_comments(path.read_text(encoding="utf-8"))

    # split into chunks by Target to assign location
    targets = [(m.start(), m.group(1)) for m in TARGET_RE.finditer(text)]
    if not targets:
        return []

    rows = []
    for idx, (pos, target) in enumerate(targets):
        end = targets[idx + 1][0] if idx + 1 < len(targets) else len(text)
        chunk = text[pos:end]
        location = target.replace("Data/Events/", "")
        if not target.startswith("Data/Events/"):
            continue

        entries_m = re.search(r'"Entries"\s*:\s*\{', chunk)
        if not entries_m:
            continue
        entries_start = entries_m.end()
        # Heuristic: Entries value ends before next top-level key in Change object
        entries_end_m = re.search(r'\n\s{4,8}\}(?:\s*,\s*\n|\s*\n\s*\})', chunk[entries_start:])
        entries_text = chunk[entries_start : entries_start + entries_end_m.start()] if entries_end_m else chunk[entries_start:]

        for m in EVENT_KEY_RE.finditer(entries_text):
            full_key = m.group(1)
            base = full_key.split("/")[0]
            if base in SKIP_KEYS or base in FORK_ONLY_KEYS:
                continue
            if m.group(3) == "[":
                script, _ = script_from_array(entries_text, m.end() - 1)
            else:
                script = (m.group(2) or "").replace("\\n", "\n").replace('\\"', '"')

            event_id = full_key.split("/")[0]
            preconds = "/".join(full_key.split("/")[1:])
            bridge = sorted(set(BRIDGE_RE.findall(script)))
            rows.append({
                "file": fn,
                "included": included,
                "location": location,
                "event_id": event_id,
                "full_key": full_key,
                "preconditions": preconds,
                "commands": extract_commands(script),
                "bridge": bridge,
                "script": script,
                "is_fork": base in FORK_ONLY_KEYS,
            })
    return rows


def scan_cs() -> dict:
    """Return structured C# references."""
    data = {
        "event_literals": defaultdict(set),
        "events_seen": defaultdict(set),
        "start_event": defaultdict(set),
        "load_events": defaultdict(set),
        "topics_set": defaultdict(set),
        "mail_set": defaultdict(set),
        "play_event_refs": defaultdict(set),
    }
    topic_re = re.compile(r'AddTopic\(\s*"([^"]+)"|HasConversationTopic\(\s*"([^"]+)"|TryAdd\(\s*"([^"]+)"')
    mail_re = re.compile(r'addMailForTomorrow\(\s*"([^"]+)"|MailIds\.(\w+)')

    for cs in CS.rglob("*.cs"):
        if "obj" in cs.parts:
            continue
        rel = str(cs.relative_to(CS))
        text = cs.read_text(encoding="utf-8", errors="replace")

        for m in re.finditer(r'"eventHarvey[A-Za-z0-9_]+"|"event[A-Z][A-Za-z0-9_]*"|"PlayerKilled"', text):
            data["event_literals"][m.group(0).strip('"')].add(rel)

        for m in re.finditer(r'eventsSeen\.(?:Contains|Add)\(\s*"([^"]+)"', text):
            data["events_seen"][m.group(1)].add(rel)

        for m in re.finditer(r'startEvent\(', text):
            data["start_event"]["startEvent"].add(rel)

        for m in re.finditer(r'Load<[^>]+>\(\s*"Data/Events/([^"]+)"', text):
            data["load_events"][m.group(1)].add(rel)

        for m in re.finditer(r'TriggerEventByName\(\s*"([^"]+)"', text):
            data["event_literals"][m.group(1)].add(rel)

        for m in topic_re.finditer(text):
            tid = m.group(1) or m.group(2) or m.group(3)
            if tid:
                data["topics_set"][tid].add(rel)

        for m in mail_re.finditer(text):
            mid = m.group(1) or m.group(2)
            if mid:
                data["mail_set"][mid].add(rel)

    return data


def scan_triggers() -> dict:
    path = CP / "triggersCare.json"
    if not path.exists():
        return {}
    text = strip_json_comments(path.read_text(encoding="utf-8"))
    refs = defaultdict(list)
    for m in re.finditer(r'SpaceCore_PlayEvent\s+([A-Za-z0-9_]+)', text):
        refs[m.group(1)].append("triggersCare.json (SpaceCore_PlayEvent)")
    return refs


def summarize_script(row: dict) -> str:
    parts = []
    if row["bridge"]:
        parts.append("bridge: " + ", ".join(row["bridge"]))
    if row["commands"]:
        parts.append("cmds: " + ", ".join(row["commands"][:12]))
    s = row["script"]
    if "changeLocation" in s:
        m = re.search(r"changeLocation\s+(\w+)", s, re.I)
        if m:
            parts.append(f"→ {m.group(1)}")
    if "friendship Harvey" in s.lower():
        parts.append("friendship Harvey")
    if "/end" in s or s.rstrip().endswith("end"):
        parts.append("end")
    return "; ".join(parts) if parts else "—"


def trigger_source(row: dict, cs: dict, trig: dict) -> str:
    eid = row["event_id"]
    parts = []
    if eid in cs["event_literals"]:
        parts.extend(sorted(cs["event_literals"][eid]))
    if eid in EXPLICIT_CS_EVENTS:
        parts.append(EXPLICIT_CS_EVENTS[eid])
    if eid in trig:
        parts.extend(trig[eid])
    if row["preconditions"]:
        parts.append("vanilla location entry (CP preconditions)")
    elif not parts:
        parts.append("script-only / manual trigger unclear")
    return "; ".join(dict.fromkeys(parts))


def assess_risk(row: dict, cs: dict, all_by_id: dict, trig: dict) -> str:
    risks = []
    eid = row["event_id"]
    if not row["included"]:
        risks.append("NOT in content.json")
    if eid in all_by_id and len(all_by_id[eid]) > 1:
        locs = sorted({r["location"] for r in all_by_id[eid]})
        if len(locs) > 1 or len({r["file"] for r in all_by_id[eid]}) > 1:
            risks.append(f"duplicate across {', '.join(sorted({r['file'] for r in all_by_id[eid]}))}")
    if eid == "eventHarveyFirstMeeting":
        risks.append("дубль events.json + eventsCare.json + BusStop")

    cs_topics = set(cs["topics_set"]) | set(EXPLICIT_CS_TOPICS)
    has_cs_launch = eid in cs["event_literals"] or eid in trig or eid in EXPLICIT_CS_EVENTS

    if not has_cs_launch:
        if row["preconditions"]:
            req_topics = re.findall(
                r"PLAYER_HAS_CONVERSATION_TOPIC Current ([A-Za-z0-9_]+)", row["preconditions"]
            )
            unmet = [t for t in req_topics if t not in cs_topics]
            if unmet:
                risks.append(f"C# не выставляет topic: {', '.join(unmet[:3])}")
        else:
            risks.append("нет C# startEvent — только CP/trigger или недостижимо")

    if eid in ("eventHarveyMineRescue", "eventHarveyMineRescueDating"):
        risks.append("C# добавляет eventsSeen до vanilla — возможен double-mark")
    if "Random" in row["preconditions"]:
        risks.append("Random — может не сработать")
    if "Spiderbuttons.BETAS" in row["preconditions"]:
        risks.append("требует BETAS mod")
    return "; ".join(risks) if risks else "—"


def main():
    all_rows = []
    for fn, inc in EVENT_FILES.items():
        all_rows.extend(parse_events_file(fn, inc))

    cs = scan_cs()
    trig = scan_triggers()
    by_id = defaultdict(list)
    for r in all_rows:
        by_id[r["event_id"]].append(r)

    included_ids = {r["event_id"] for r in all_rows if r["included"]}
    cs_event_ids = set(cs["event_literals"]) | set(cs["events_seen"])

    OUT.mkdir(parents=True, exist_ok=True)

    included_rows = [r for r in all_rows if r["included"]]
    today = date.today().isoformat()

    # --- 00 summary table ---
    lines = [
        "# Сводная таблица событий HarveyOverhaul / InjuryCare\n",
        f"**Автоген из CP.** Актуализация {today} ({len(included_ids)} ID в активном CP, {len(included_rows)} записей с дублями ключей).\n",
        "Источники CP: `events.json`, `eventsCare.json`, `eventsMineRescue.json` (включены в `content.json`).\n",
        "Файл `events_for_mode_new_formatted.json` **не подключён** к content pack.\n",
        "## Таблица\n",
        "| Event ID | Location | Где объявлено | Где запускается | Условия (preconditions) | Что делает | Риски |",
        "|---|---|---|---|---|---|---|",
    ]
    for r in sorted(all_rows, key=lambda x: (x["included"] is False, x["location"], x["event_id"], x["file"])):
        if not r["included"] and r["file"] != "events_for_mode_new_formatted.json":
            continue
        pre = r["preconditions"] or "(нет — script trigger / SpaceCore / C#)"
        lines.append(
            f"| `{r['event_id']}` | {r['location']} | `{r['file']}` | {trigger_source(r, cs, trig)} | `{pre[:100]}` | {summarize_script(r)} | {assess_risk(r, cs, by_id, trig)} |"
        )
    (OUT / "00-summary-table.md").write_text("\n".join(lines), encoding="utf-8")

    # --- 01 CP catalog ---
    cp_lines = ["# Content Patcher: каталог Data/Events/*\n"]
    current_loc = None
    for r in sorted([x for x in all_rows if x["included"]], key=lambda x: (x["location"], x["event_id"])):
        if r["location"] != current_loc:
            current_loc = r["location"]
            cp_lines.append(f"\n## Data/Events/{current_loc}\n")
        cp_lines.append(f"### `{r['full_key']}`\n")
        cp_lines.append(f"- **Файл:** `{r['file']}`")
        cp_lines.append(f"- **Event ID:** `{r['event_id']}`")
        cp_lines.append(f"- **Preconditions:** `{r['preconditions'] or 'none'}`")
        cp_lines.append(f"- **Commands:** {', '.join(r['commands']) or '—'}")
        cp_lines.append(f"- **Bridge (mail/topic):** {', '.join(r['bridge']) or '—'}")
        preview = r["script"][:500].replace("\n", " ")
        cp_lines.append(f"- **Script preview:** `{preview}...`\n")
    (OUT / "01-cp-events-catalog.md").write_text("\n".join(cp_lines), encoding="utf-8")

    # --- 02 C# bridges ---
    cs_lines = [
        "# C# InjuryCare: запуск и мосты к CP\n",
        f"**Автоген {today}**\n",
        "## startEvent / eventsSeen / Load Data/Events\n",
        "| Механизм | Где | Детали |",
        "|---|---|---|",
        "| `TriggerEventByName` / `TryStartLocationEvent` | PassOutHandler.cs | Mine rescue; hospital pass-out; minor mine rescue |",
        "| `QueueHospitalEvent` | PassOutHandler.cs | `eventHarveyEmergencyCare`, `eventHarveyExhaustion` → Hospital |",
        "| `TryTriggerMinorMineRescue` | PassOutHandler.cs ← PlayerEventHandler | `eventHarveyMinorMineRescue` при injury без Severe |",
        "| `TriggerMineRescueEvents()` | GameEventHandler.OnDayStarted → PassOutHandler | Severe mine combat death + dating |",
        "| `StormComfortLauncher` | TimeEventHandler | Daily roll → `buffStressThunder` / `topicHarveyStormStress` |",
        "| `RescueOperationLauncher` | PlayerEventHandler | `topicRescueOperation` после E5 / storm comfort |",
        "",
        "## Topics, которые C# выставляет как мост к CP-событиям\n",
        "| Topic | Где выставляется | CP-события, ожидающие topic |",
        "|---|---|---|",
    ]
    topic_to_events = defaultdict(list)
    for r in all_rows:
        if not r["included"]:
            continue
        for t in re.findall(r"topic[A-Za-z0-9_]+", r["preconditions"]):
            topic_to_events[t].append(f"{r['event_id']} ({r['location']})")

    bridge_topics = [
        "topicPassedOutInTown", "topicFarmerExhausted", "topicMineInjuryRescue",
        "topicHarveyMandatoryCheckup", "topicFirstMeeting", "topicAgreedCheckup",
        "topicRescueOperation", "topicHarveySecondVisit", "topicHarveyFirstVisit",
        "topicDiagnosisComplete", "topicHarveyStormStress", "HarveyMod_CD_StormComfort",
        "HarveyMod_CD_RescueOperation", "topicHarveyMinorMineRescue",
    ]
    for t in sorted(set(list(cs["topics_set"]) + bridge_topics + list(EXPLICIT_CS_TOPICS))):
        cs_where = ", ".join(sorted(cs["topics_set"].get(t, []))) or EXPLICIT_CS_TOPICS.get(t, "—")
        cp_where = ", ".join(topic_to_events.get(t, [])) or "—"
        if cs_where != "—" or cp_where != "—":
            cs_lines.append(f"| `{t}` | {cs_where} | {cp_where} |")

    cs_lines.extend([
        "",
        "## Mail из C#",
        "| Mail | Где | CP-события |",
        "|---|---|---|",
    ])
    mail_to_events = defaultdict(list)
    for r in all_rows:
        if not r["included"]:
            continue
        for m in re.findall(r"mail[A-Za-z0-9_]+", r["preconditions"]):
            mail_to_events[m].append(r["event_id"])

    for m in sorted(set(list(cs["mail_set"]) + ["mailHarveySleepControl", "mailHarveyMineForbidden"])):
        cs_lines.append(f"| `{m}` | {', '.join(sorted(cs['mail_set'].get(m, []))) or '—'} | {', '.join(mail_to_events.get(m, [])) or '—'} |")

    (OUT / "02-csharp-bridges.md").write_text("\n".join(cs_lines), encoding="utf-8")

    # --- 03 gaps ---
    gap_lines = ["# Разрывы: C# ↔ CP\n"]
    gap_lines.append("## События упомянуты в C#, но отсутствуют в подключённых CP-файлах\n")
    missing = sorted(e for e in cs_event_ids if e.startswith("event") and e not in included_ids)
    if missing:
        for e in missing:
            gap_lines.append(f"- `{e}` — refs: {', '.join(sorted(cs['event_literals'].get(e, cs['events_seen'].get(e, []))))}")
    else:
        gap_lines.append("- **Нет** — все C# event ID найдены в CP.\n")

    gap_lines.append("\n## CP-события без C# и без SpaceCore trigger (только vanilla preconditions)\n")
    for r in sorted(all_rows, key=lambda x: x["event_id"]):
        if not r["included"]:
            continue
        eid = r["event_id"]
        if eid in cs["event_literals"] or eid in trig:
            continue
        gap_lines.append(f"- `{eid}` @ {r['location']} — pre: `{r['preconditions'][:80]}`")

    gap_lines.append("\n## CP-события с риском недостижимости\n")
    hard = []
    for r in all_rows:
        if not r["included"]:
            continue
        risk = assess_risk(r, cs, by_id, trig)
        if risk != "—":
            hard.append((r["event_id"], r["location"], risk))
    for eid, loc, risk in sorted(set(hard)):
        gap_lines.append(f"- `{eid}` @ {loc}: {risk}")

    gap_lines.append("\n## Файлы вне content.json\n")
    for r in all_rows:
        if r["included"]:
            continue
        gap_lines.append(f"- `{r['event_id']}` @ {r['location']} in `{r['file']}` — **не загружается**")

    gap_lines.append("\n## Дубликаты event ID\n")
    for eid, rows in sorted(by_id.items()):
        if len(rows) > 1 and any(x["included"] for x in rows):
            details = [f"{x['location']} ({x['file']})" for x in rows if x["included"]]
            if len(details) > 1:
                gap_lines.append(f"- `{eid}`: {', '.join(details)}")

    (OUT / "03-gaps-and-risks.md").write_text("\n".join(gap_lines), encoding="utf-8")

    # --- 06 locations index ---
    loc_map = defaultdict(set)
    for r in all_rows:
        if r["included"] and r["event_id"] not in FORK_ONLY_KEYS:
            loc_map[r["location"]].add(r["event_id"])
    loc_lines = [
        "# Индекс по локациям Data/Events/*\n",
        "Подключённые CP-файлы. Fork-подсобытия не включены.\n",
        f"**Автоген из CP** ({today}).\n",
        "",
        "| Location | Event IDs (count) |",
        "|---|---|",
    ]
    for loc in sorted(loc_map.keys()):
        ids = sorted(loc_map[loc])
        loc_lines.append(f"| **{loc}** | {', '.join(f'`{i}`' for i in ids)} ({len(ids)}) |")
    unique_ids = {r["event_id"] for r in all_rows if r["included"] and r["event_id"] not in FORK_ONLY_KEYS}
    loc_lines.extend([
        "",
        f"**Итого:** {len(unique_ids)} уникальных custom event ID в {len(loc_map)} локациях.",
        "",
        "## Файлы-источники",
        "",
        "| Файл | Локации |",
        "|---|---|",
        "| `events.json` | Farm, Hospital, SeedShop, Woods, Mountain, Custom_AdventurerSummit, Town, BusStop, HarveyRoom, Forest, Beach, ArchaeologyHouse, Desert, Mine |",
        "| `eventsCare.json` | Farm, Hospital, BusStop, SkullCave, Mine |",
        "| `eventsMineRescue.json` | Mine (+ `Data/Mail` mailHarveyAfterMineRescue) |",
        "",
        "## Не подключено (content.json)",
        "",
        "`events_for_mode_new_formatted.json` — Forest, Hospital, Farm (MyMod_* IDs).",
    ])
    (OUT / "06-locations-index.md").write_text("\n".join(loc_lines), encoding="utf-8")

    # --- 04 fork sub-events ---
    fork_lines = ["# Fork-подсобытия (не standalone entry points)\n"]
    fork_lines.append("Ключи в `Entries`, вызываемые через `fork` из родительских событий.\n")
    fork_lines.append("| Key | Location | Файл | Родитель (если известен) |")
    fork_lines.append("|---|---|---|---|")
    fork_parents = {
        "declineFood": "eventHarveyFirstMeeting",
        "refuseCheckup": "eventHarveyFirstMeeting",
        "irregularEating": "eventHarveyCheckup",
        "HarveySkullPromise": "eventHarveySkullCavePrevention",
        "acceptWalk": "eventHarveyFirstWalk",
    }
    for r in all_rows:
        if r["event_id"] in FORK_ONLY_KEYS:
            fork_lines.append(
                f"| `{r['full_key']}` | {r['location']} | `{r['file']}` | `{fork_parents.get(r['event_id'], '?')}` |"
            )
    (OUT / "04-fork-subevents.md").write_text("\n".join(fork_lines), encoding="utf-8")

    print(f"Parsed {len(all_rows)} event entries")
    print(f"Unique event IDs (included): {len(included_ids)}")
    print(f"Output: {OUT}")


if __name__ == "__main__":
    main()

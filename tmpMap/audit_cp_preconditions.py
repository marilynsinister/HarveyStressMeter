#!/usr/bin/env python3
"""Technical audit of Harvey CP event preconditions."""
import re
from collections import defaultdict
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
CS = Path(r"C:\Users\Admin\HarveyOverhaulInjury")
OUT = Path(r"C:\Users\Admin\HarveyOverhaulInjury\docs\harvey-relationship-visits-audit\cp-preconditions-audit-appendix.md")

EVENT_FILES = ["events.json", "eventsCare.json", "eventsMineRescue.json"]
ALL_CP_JSON = list(CP.glob("*.json"))
TARGET_RE = re.compile(r'"Target"\s*:\s*"(Data/Events/[^"]+)"')
EVENT_KEY_RE = re.compile(
    r'"((?:[A-Za-z][A-Za-z0-9_.]*)(?:/[^"]*)?)"\s*:\s*(?:"((?:\\.|[^"\\])*)"|(\[))',
    re.S,
)
FORK_ONLY = {
    "acceptWalk", "declineFood", "refuseCheckup", "irregularEating",
    "HarveySkullPromise", "leaveHospital",
}
SKIP = {"Action", "Target", "FromFile", "LogName", "When", "Entries", "Changes"}

TOPIC_ADD_RE = re.compile(
    r"(?:addConversationTopic|AddConversationTopic|removeConversationTopic|RemoveConversationTopic)\s+(?:Current\s+)?([A-Za-z0-9_]+)",
    re.I,
)
TOPIC_CHECK_RE = re.compile(
    r"(!?)PLAYER_HAS_CONVERSATION_TOPIC Current ([A-Za-z0-9_]+)",
)
SEEN_CHECK_RE = re.compile(
    r"(!?)PLAYER_HAS_SEEN_EVENT Current ([A-Za-z0-9_.]+)",
)
HAS_SEEN_LEGACY_RE = re.compile(r"(!?)HasSeenEvent ([A-Za-z0-9_.]+)")
MAIL_CHECK_RE = re.compile(r"PLAYER_HAS_MAIL Current ([A-Za-z0-9_]+)")
BUFF_CHECK_RE = re.compile(r"PLAYER_HAS_BUFF Current ([A-Za-z0-9_]+)")
RELATIONSHIP_RE = re.compile(r"PLAYER_NPC_RELATIONSHIP\s+Current Harvey\s+([^/]+)")

ROMANTIC_MARKERS = re.compile(
    r"\$(?:l|L)|солнышк|дорогая|любим|обним|поцел|женись|брак|муж|жена|сердце мо",
    re.I,
)
CRISIS_MARKERS = re.compile(
    r"кров|кома|капельниц|смерт|без сознан|критич|экстрен|коллапс|госпитал|операц|травм|рана|пульс нит",
    re.I,
)

# Vanilla / game events that exist outside CP
VANILLA_EVENT_IDS = {
    "PlayerKilled", "eventSeen", "eventHarveyFirstMeeting",
}


def strip_comments(text: str) -> str:
    out = []
    for line in text.splitlines():
        if line.strip().startswith("//"):
            continue
        if "//" in line:
            in_str = esc = False
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


def parse_events() -> list[dict]:
    rows = []
    for fn in EVENT_FILES:
        path = CP / fn
        if not path.exists():
            continue
        text = strip_comments(path.read_text(encoding="utf-8"))
        targets = [(m.start(), m.group(1)) for m in TARGET_RE.finditer(text)]
        for idx, (pos, target) in enumerate(targets):
            end = targets[idx + 1][0] if idx + 1 < len(targets) else len(text)
            chunk = text[pos:end]
            loc = target.replace("Data/Events/", "")
            em = re.search(r'"Entries"\s*:\s*\{', chunk)
            if not em:
                continue
            entries_text = chunk[em.end() :]
            for m in EVENT_KEY_RE.finditer(entries_text):
                full_key = m.group(1)
                base = full_key.split("/")[0]
                if base in SKIP or base in FORK_ONLY:
                    continue
                if not (
                    base.startswith("eventHarvey")
                    or base.startswith("HarveyMod_")
                    or base.startswith("HarveyOverhaulStory.")
                    or base == "eventStayInHospital"
                ):
                    continue
                if m.group(3) == "[":
                    continue
                script = (m.group(2) or "").replace("\\n", "\n").replace('\\"', '"')
                pre = "/".join(full_key.split("/")[1:])
                rows.append({
                    "file": fn,
                    "location": loc,
                    "event_id": base,
                    "full_key": full_key,
                    "preconditions": pre,
                    "script": script,
                })
    return rows


def scan_cp_topic_sources() -> dict[str, set[str]]:
    """topic -> where it's added/defined."""
    sources = defaultdict(set)
    for path in ALL_CP_JSON:
        rel = path.name
        text = strip_comments(path.read_text(encoding="utf-8"))
        for m in TOPIC_ADD_RE.finditer(text):
            sources[m.group(1)].add(f"{rel} (add/remove script)")
        if rel.startswith("dialogues"):
            for m in re.finditer(r'"((?:topic|Topic)[A-Za-z0-9_]+)"\s*:', text):
                sources[m.group(1)].add(f"{rel} (dialogue key)")
        if rel == "triggersCare.json":
            for m in re.finditer(r"AddConversationTopic\s+([A-Za-z0-9_]+)", text):
                sources[m.group(1)].add("triggersCare.json (trigger)")
    return sources


def scan_cs_topics() -> dict[str, set[str]]:
    sources = defaultdict(set)
    for cs in CS.rglob("*.cs"):
        if "obj" in cs.parts or "bin" in cs.parts:
            continue
        text = cs.read_text(encoding="utf-8", errors="replace")
        rel = str(cs.relative_to(CS))
        for m in re.finditer(r'AddTopic\(\s*"([^"]+)"|TryAdd\(\s*"([^"]+)"|HasConversationTopic\(\s*"([^"]+)"', text):
            tid = m.group(1) or m.group(2) or m.group(3)
            if tid:
                sources[tid].add(f"C# {rel}")
    return sources


def scan_cs_start_events() -> set[str]:
    ids = set()
    for cs in CS.rglob("*.cs"):
        if "obj" in cs.parts:
            continue
        text = cs.read_text(encoding="utf-8", errors="replace")
        for m in re.finditer(r'TriggerEventByName\(\s*"([^"]+)"|"eventHarvey[A-Za-z0-9_]+"', text):
            ids.add(m.group(1) or m.group(0).strip('"'))
    return ids


def extract_weather(pre: str) -> list[str]:
    out = []
    for m in re.finditer(r"Weather\s+(\w+)", pre, re.I):
        out.append(m.group(1).lower())
    return out


def extract_time(pre: str) -> str | None:
    m = re.search(r"Time (\d+ \d+)", pre)
    return m.group(1) if m else None


def script_location_hint(script: str) -> set[str]:
    hints = set()
    if re.search(r"changeLocation\s+Hospital", script, re.I):
        hints.add("Hospital")
    if re.search(r"changeLocation\s+Forest", script, re.I):
        hints.add("Forest")
    if re.search(r"Hospital_Ambient", script):
        hints.add("Hospital")
    if re.search(r"ocean/", script):
        hints.add("Beach")
    # tile heuristic for clinic interior
    if re.search(r"^\s*5 9/|farmer 2 5|farmer 5 10", script):
        hints.add("Hospital(interior coords)")
    return hints


def main():
    events = parse_events()
    event_ids = {e["event_id"] for e in events}
    cp_topics = scan_cp_topic_sources()
    cs_topics = scan_cs_topics()
    all_topic_sources = defaultdict(set)
    for d in (cp_topics, cs_topics):
        for k, v in d.items():
            all_topic_sources[k].update(v)

    topics_required = defaultdict(list)  # topic -> [(event, neg, cond)]
    topics_added_events = defaultdict(list)
    seen_required = defaultdict(list)
    mail_required = defaultdict(list)
    buff_required = defaultdict(list)

    for e in events:
        eid = e["event_id"]
        pre = e["preconditions"]
        script = e["script"]
        for neg, tid in TOPIC_CHECK_RE.findall(pre):
            topics_required[tid].append((eid, bool(neg), pre))
        for neg, ev in SEEN_CHECK_RE.findall(pre):
            seen_required[ev].append((eid, bool(neg), "PLAYER_HAS_SEEN_EVENT", pre))
        for neg, ev in HAS_SEEN_LEGACY_RE.findall(pre):
            seen_required[ev].append((eid, bool(neg), "HasSeenEvent", pre))
        for mid in MAIL_CHECK_RE.findall(pre):
            mail_required[mid].append(eid)
        for bid in BUFF_CHECK_RE.findall(pre):
            buff_required[bid].append(eid)
        for tid in TOPIC_ADD_RE.findall(script):
            if tid.lower() not in ("current",):
                topics_added_events[tid].append(eid)

    # Also topics added in fork scripts within same files - already in script

    issues = {
        "1_nonexistent_topics": [],
        "2_checked_never_added": [],
        "3_added_never_used": [],
        "4_nonexistent_seen_events": [],
        "5_seen_contradiction": [],
        "6_location_weather": [],
        "7_romantic_no_dating": [],
        "8_crisis_no_gate": [],
        "other_unpredictable": [],
    }

    fixes = []

    # 1 & 2: topics checked but never added (CP+C#), excluding dialogue-only existence
    for tid, refs in sorted(topics_required.items()):
        positive = [r for r in refs if not r[1]]
        if not positive:
            continue
        if tid not in all_topic_sources:
            issues["1_nonexistent_topics"].append({
                "topic": tid,
                "events": sorted({r[0] for r in positive}),
                "example": positive[0][2][:120],
            })
        elif tid not in cp_topics and tid not in cs_topics:
            issues["2_checked_never_added"].append({
                "topic": tid,
                "events": sorted({r[0] for r in positive}),
                "sources": sorted(all_topic_sources.get(tid, [])),
            })

    # Special: topic checked with wrong ID (legacy)
    legacy_wrong = [
        ("topicHarveyFirstVisit", "outcome topics Agree/Neutral/Refused used in SecondVisit"),
        ("topicHarveySecondVisit", "outcome topics used in FirstWalk — fixed?"),
    ]

    # 3: topics added in events but never required anywhere in CP preconditions/triggers
    all_checks = set(topics_required.keys())
    trigger_text = strip_comments((CP / "triggersCare.json").read_text(encoding="utf-8"))
    for tid in set(topics_added_events.keys()):
        if tid in ("Current",):
            continue
        used_in_pre = tid in all_checks or any(tid in e["preconditions"] for e in events)
        used_in_trigger = tid in trigger_text
        used_in_dialogue = tid in cp_topics and any("dialogue" in s for s in cp_topics[tid])
        if not used_in_pre and not used_in_trigger:
            # dialogue-only topics are OK (reactions)
            if not used_in_dialogue:
                issues["3_added_never_used"].append({
                    "topic": tid,
                    "added_by": sorted(set(topics_added_events[tid])),
                })

    # 4: seen event IDs that don't exist as CP event keys
    cs_events = scan_cs_start_events()
    all_event_ids = event_ids | VANILLA_EVENT_IDS | cs_events
    for ev, refs in sorted(seen_required.items()):
        positive = [r for r in refs if not r[1]]
        if not positive:
            continue
        if ev not in all_event_ids:
            issues["4_nonexistent_seen_events"].append({
                "event_ref": ev,
                "required_by": sorted({r[0] for r in positive}),
                "via": positive[0][2],
            })

    # 5: same event seen and !seen in one key
    for e in events:
        pre = e["preconditions"]
        pos = set()
        neg = set()
        for n, ev in SEEN_CHECK_RE.findall(pre):
            (neg if n else pos).add(ev)
        for n, ev in HAS_SEEN_LEGACY_RE.findall(pre):
            (neg if n else pos).add(ev)
        both = pos & neg
        if both:
            issues["5_seen_contradiction"].append({
                "event_id": e["event_id"],
                "conflict_ids": sorted(both),
                "key": e["full_key"][:180],
            })

    # 6: location/weather mismatch
    for e in events:
        pre = e["preconditions"]
        loc = e["location"]
        script = e["script"]
        hints = script_location_hint(script)
        if loc == "BusStop" and "Hospital(interior coords)" in hints:
            issues["6_location_weather"].append({
                "event_id": e["event_id"],
                "problem": f"Зарегистрировано на `{loc}`, скрипт — координаты клиники",
                "key": e["full_key"][:160],
            })
        if loc == "BusStop" and "Hospital" in hints and e["event_id"] != "eventHarveyCheckup":
            pass
        w_pre = extract_weather(pre)
        if "sunny" in w_pre and "storm" in script.lower()[:200]:
            issues["6_location_weather"].append({
                "event_id": e["event_id"],
                "problem": "Precondition Sunny, но сценарий про грозу",
                "key": e["full_key"][:160],
            })
        if "wind" in w_pre and loc not in ("BusStop", "Farm", "Town", "Forest", "Mountain"):
            issues["6_location_weather"].append({
                "event_id": e["event_id"],
                "problem": f"Weather Wind на локации `{loc}` — редко посещается",
                "key": e["full_key"][:160],
            })

    # Unpredictable: Random, no preconditions
    for e in events:
        pre = e["preconditions"]
        if not pre.strip():
            issues["other_unpredictable"].append({
                "event_id": e["event_id"],
                "location": e["location"],
                "problem": "Нет CP preconditions — только C#/ручной запуск",
            })
        if "Random" in pre:
            m = re.search(r"Random ([0-9.]+)", pre)
            pct = float(m.group(1)) * 100 if m else None
            issues["other_unpredictable"].append({
                "event_id": e["event_id"],
                "location": e["location"],
                "problem": f"Random {m.group(1) if m else '?'} (~{pct:.0f}%)" if pct else "Random gate",
            })
        if "Spiderbuttons.BETAS" in pre:
            issues["other_unpredictable"].append({
                "event_id": e["event_id"],
                "location": e["location"],
                "problem": "Требует мод BETAS",
            })

    # 7: romantic text without dating/married
    for e in events:
        pre = e["preconditions"]
        rel = RELATIONSHIP_RE.search(pre)
        has_dating = rel and ("Dating" in rel.group(1) or "Married" in rel.group(1))
        if has_dating:
            continue
        if e["event_id"] in (
            "eventHarveyPropose", "eventHarveyFirstDate", "eventHarveyMountainDate",
            "eventHarveyMorningCheckup", "eventHarveyCheckFarmerOutsideAfter22",
            "eventHarveyMineRescueDating",
        ):
            continue  # has dating gate or special
        script_sample = e["script"][:3000]
        if ROMANTIC_MARKERS.search(script_sample):
            hits = ROMANTIC_MARKERS.findall(script_sample)[:3]
            issues["7_romantic_no_dating"].append({
                "event_id": e["event_id"],
                "location": e["location"],
                "hearts": re.search(r"Friendship Harvey (\d+)", pre),
                "markers": hits,
                "problem": "Романтичный/партнёрский тон без Dating/Married gate",
            })

    # 8: medical crisis without injury/topic/buff/mail gate
    crisis_events = [
        "eventHarveyEmergencyCare", "eventHarveyExhaustion", "eventHarveyTreatmentCollapse",
        "eventStayInHospital", "eventHarveyTraumaExam", "eventHarveyLateNightCollapse",
        "HarveyMod_NightCrisis",
    ]
    for e in events:
        if e["event_id"] not in crisis_events and not CRISIS_MARKERS.search(e["script"][:2000]):
            continue
        pre = e["preconditions"]
        has_gate = any([
            TOPIC_CHECK_RE.search(pre),
            BUFF_CHECK_RE.search(pre),
            MAIL_CHECK_RE.search(pre),
            "PLAYER_HAS_SEEN_EVENT" in pre and "PlayerKilled" in pre,
            "HasSeenEvent" in pre,
            "topicDiagnosis" in pre,
        ])
        if not has_gate and not pre.strip():
            issues["8_crisis_no_gate"].append({
                "event_id": e["event_id"],
                "location": e["location"],
                "problem": "Медицинский/кризисный script без topic/buff/mail/seen gate в CP key",
            })
        elif e["event_id"] == "eventHarveyTraumaExam" and not TOPIC_CHECK_RE.search(pre):
            issues["8_crisis_no_gate"].append({
                "event_id": e["event_id"],
                "location": e["location"],
                "problem": "TraumaExam: только Friendship 2000, нет injury topic",
            })

    # Minimal fixes proposals
    if any(i["event_id"] == "eventHarveyCheckup" for i in issues["6_location_weather"]):
        fixes.append({
            "event": "eventHarveyCheckup",
            "fix": "Перенести ключ с `Data/Events/BusStop` на `Data/Events/Hospital` (тот же script).",
            "priority": "CRITICAL",
        })
    fixes.append({
        "event": "eventHarveyFirstMeeting",
        "fix": "Удалить дубль из events.json; добавить Town fallback (см. 02-first-meeting-reachability.md).",
        "priority": "HIGH",
    })
    for item in issues["8_crisis_no_gate"]:
        if item["event_id"] in ("eventHarveyEmergencyCare", "eventHarveyExhaustion", "eventHarveyTreatmentCollapse"):
            fixes.append({
                "event": item["event_id"],
                "fix": "Подключить triggersCare / C# bridge с topic или buff gate; или document as C#-only.",
                "priority": "HIGH",
            })
    for item in issues["other_unpredictable"]:
        if "Random" in item.get("problem", "") and item["event_id"].startswith("eventHarveyStormComfort"):
            fixes.append({
                "event": item["event_id"],
                "fix": "Добавить `!PLAYER_HAS_SEEN_EVENT` + HarveyMod_CD_* cooldown.",
                "priority": "MED",
            })
    if issues["7_romantic_no_dating"]:
        fixes.append({
            "event": "HarveyMod_FirstTreatment, HarveyOverhaulStory.E6+",
            "fix": "Сверить heart-gates с тоном; FirstTreatment поднять до 4–5♥ или смягчить текст.",
            "priority": "MED",
        })

    # Write markdown
    lines = [
        "# Технический аудит CP preconditions — события Харви\n\n",
        "Автоматический разбор `events.json`, `eventsCare.json`, `eventsMineRescue.json` + cross-ref с `triggersCare.json`, `dialoguesHarvey*.json`, C# InjuryCare.\n\n",
        "**Автоген appendix 2026-05-23** — CP после split gates / Story chain / C# topics.\n\n",
        f"Проверено событий: **{len(events)}** | Уникальных event ID: **{len(event_ids)}**\n\n",
        "---\n\n",
        "## Сводка по категориям\n\n",
        "| # | Категория | Найдено |\n",
        "|---|---|---|\n",
    ]
    cats = [
        ("1", "Несуществующие conversation topics (проверка без add в CP/C#)", len(issues["1_nonexistent_topics"])),
        ("2", "Topics проверяются, но не добавляются в CP/C#", len(issues["2_checked_never_added"])),
        ("3", "Topics добавляются, но не используются в preconditions/triggers", len(issues["3_added_never_used"])),
        ("4", "PLAYER_HAS_SEEN_EVENT на несуществующий ID", len(issues["4_nonexistent_seen_events"])),
        ("5", "seen + !seen одного ID в одном ключе", len(issues["5_seen_contradiction"])),
        ("6", "Противоречие локация/погода/сцена", len(issues["6_location_weather"])),
        ("7", "Dating-текст без Dating/Married gate", len(issues["7_romantic_no_dating"])),
        ("8", "Медкризис без injury/topic gate", len(issues["8_crisis_no_gate"])),
        ("+", "Непредсказуемость (Random, no preconditions, BETAS)", len(issues["other_unpredictable"])),
    ]
    for num, name, count in cats:
        lines.append(f"| {num} | {name} | **{count}** |\n")

    def section(title, items, formatter):
        lines.append(f"\n---\n\n## {title}\n\n")
        if not items:
            lines.append("*Проблем не найдено.*\n")
            return
        for item in items:
            lines.append(formatter(item) + "\n")

    section("1. Несуществующие conversation topics", issues["1_nonexistent_topics"],
            lambda x: f"- **`{x['topic']}`** — требуют: `{', '.join(x['events'])}`\n  - Пример условия: `{x['example']}`")

    section("2. Topics проверяются, но нигде не добавляются (CP/C#)", issues["2_checked_never_added"],
            lambda x: f"- **`{x['topic']}`** — gate для `{', '.join(x['events'])}`; источники: {x.get('sources') or 'нет'}")

    section("3. Topics добавляются, но не используются в gates", issues["3_added_never_used"][:40],
            lambda x: f"- **`{x['topic']}`** — добавляет `{', '.join(x['added_by'])}` (реакции в dialogue OK)")

    section("4. PLAYER_HAS_SEEN_EVENT / HasSeenEvent — ID не найден", issues["4_nonexistent_seen_events"],
            lambda x: f"- **`{x['event_ref']}`** ({x['via']}) — требуют: `{', '.join(x['required_by'])}`")

    section("5. Конфликт seen и !seen в одном ключе", issues["5_seen_contradiction"],
            lambda x: f"- **`{x['event_id']}`** — ID: `{', '.join(x['conflict_ids'])}`\n  - `{x['key']}`")

    section("6. Локация / погода vs сцена", issues["6_location_weather"],
            lambda x: f"- **`{x['event_id']}`** — {x['problem']}\n  - `{x['key']}`")

    section("7. Романтичный тон без Dating/Married", issues["7_romantic_no_dating"],
            lambda x: f"- **`{x['event_id']}`** @ `{x['location']}` — {x['problem']}")

    section("8. Медицинский кризис без injury/topic gate", issues["8_crisis_no_gate"],
            lambda x: f"- **`{x['event_id']}`** @ `{x['location']}` — {x['problem']}")

    section("9. Непредсказуемость (Random / orphan / BETAS)", issues["other_unpredictable"],
            lambda x: f"- **`{x['event_id']}`** @ `{x.get('location','?')}` — {x['problem']}")

    lines.append("\n---\n\n## Минимальные рекомендуемые правки\n\n")
    lines.append("| Приоритет | Event | Правка |\n|---|---|---|\n")
    for f in fixes:
        lines.append(f"| {f['priority']} | `{f['event']}` | {f['fix']} |\n")

    lines.append("\n---\n\n## Примечания\n\n")
    lines.append("- Topics с ключами только в `dialoguesHarvey*.json` считаются **валидными** (реакции на активный topic).\n")
    lines.append("- Orphan-события без preconditions могут быть **намеренно** C#-only (`PassOutHandler`, hospitalization).\n")
    lines.append("- `HarveyMod_CD_*` topics — cooldown Story; отсутствие в gate других веток — отдельный design debt.\n")
    lines.append("- См. также [harvey-events-audit.md](harvey-events-audit.md), [01-early-farm-visit-chain.md](01-early-farm-visit-chain.md).\n")

    OUT.write_text("".join(lines), encoding="utf-8")
    print(f"Wrote {OUT}")
    for k, v in issues.items():
        print(f"  {k}: {len(v)}")


if __name__ == "__main__":
    main()

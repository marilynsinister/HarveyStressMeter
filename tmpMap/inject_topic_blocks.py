#!/usr/bin/env python3
"""Inject topic dialogue blocks into 08-events-as-book.md next to each event."""
import json
import re
from collections import defaultdict
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
MD = Path(r"C:\Users\Admin\HarveyOverhaulInjury\docs\events-inventory\08-events-as-book.md")

EVENT_FILES = ["eventsCare.json", "eventsMineRescue.json", "events.json"]
DIALOGUE_GLOB = "dialoguesHarvey*.json"

PORTRAIT = {
    "$a": "*(строго)*", "$s": "*(грустно)*", "$h": "*(с улыбкой)*",
    "$l": "*(нежно)*", "$u": "*(серьёзно)*", "$8": "*(в панике)*",
    "$0": "", "$1": "", "$2": "", "$3": "", "$4": "", "$5": "",
}

DIALOGUE_SOURCE_PRIORITY = {
    "dialoguesHarveyCare.json": 100,
    "dialoguesHarveyInjury.json": 90,
    "dialoguesHarveyStress.json": 80,
    "dialoguesHarveyCure.json": 70,
    "dialoguesHarvey.json": 60,
    "dialoguesHarveyCureStress.json": 50,
    "dialoguesHarveyPregnant.json": 10,
}

VALID_TOPIC = re.compile(
    r"^(?:topic[A-Za-z0-9_]+|HarveyMod_CD_[A-Za-z0-9_]+|HarveyMineIntercept|HarveyAerobics)$"
)

EVENT_TOPICS_EXTRA = {
    "eventHarveyFirstMeeting": [
        ("topicAgreedCheckup", 5, "fork: согласие на осмотр"),
        ("topicRefusedCheckup", 3, "fork: отказ от осмотра"),
    ],
    "eventHarveyCheckup": [
        ("topicAfterCheckup", 5, "конец сцены (обе ветки)"),
        ("topicAgreedCheckup", None, "removeConversationTopic"),
    ],
    "eventHarveyCheckFarmerOutsideAfter22": [
        ("topicHarveyMandatoryCheckup", 1, "конец сцены"),
    ],
    "eventHarveyStormComfortFarm": [
        ("topicStressThunder", None, "removeConversationTopic (если был)"),
    ],
    "eventHarveyStormComfortForest": [("topicStressThunder", None, "remove")],
    "eventHarveyStormComfortTown": [("topicStressThunder", None, "remove")],
    "eventHarveyStormComfortMine": [("topicStressThunder", None, "remove")],
    "eventHarveyStormComfortMountain": [("topicStressThunder", None, "remove")],
    "eventHarveyStormComfortDesert": [("topicStressThunder", None, "remove")],
    "eventRescueOperation": [
        ("topicRescueComplete", 21, "конец сцены"),
        ("topicRescueOperation", None, "removeConversationTopic"),
    ],
    "HarveyMod_TreatmentPlanMeeting": [
        ("topicTreatmentAgreement", 30, "ветка «полный курс»"),
        ("topicIntensiveTreatment", 21, "ветка «сократить»"),
        ("topicTreatmentRefusal", 14, "ветка «сомнения»"),
        ("topicDiagnosisComplete", None, "removeConversationTopic"),
    ],
    "eventHarveyMineInterception": [("HarveyMineIntercept", 3, "конец сцены")],
    "eventHarveyExhaustion": [("topicHarveyExhaustion", 3, "eventsCare")],
    "HarveyMod_FirstTreatment": [("topicHarveyNeedsFirstTreatment", None, "removeConversationTopic")],
}

CD_TOPICS = {
    "HarveyOverhaulStory.E1_SlipperyPath": [("HarveyMod_CD_Global", 2), ("HarveyMod_CD_E1", 3)],
    "HarveyOverhaulStory.E3_ForestApothecary": [("HarveyMod_CD_Global", 2), ("HarveyMod_CD_E3", 3)],
    "HarveyOverhaulStory.E5_StormBeside": [("HarveyMod_CD_Global", 2), ("HarveyMod_CD_E5", 5)],
    "HarveyOverhaulStory.E6_SayItOutLoud": [("HarveyMod_CD_Global", 4), ("HarveyMod_CD_E6", 7)],
    "HarveyOverhaulStory.E7_TownSip_Sunny": [("HarveyMod_CD_Global", 2), ("HarveyMod_CD_E7", 2)],
    "HarveyOverhaulStory.E8_QuietShelf": [("HarveyMod_CD_Global", 2), ("HarveyMod_CD_E8", 2)],
}

ADD_TOPIC_RE = re.compile(
    r"(?:action\s+)?(?:add|Add)ConversationTopic\s+(?:Current\s+)?"
    r"((?:topic[A-Za-z0-9_]+|HarveyMod_CD_[A-Za-z0-9_]+|HarveyMineIntercept|HarveyAerobics))"
    r"\s+(\d+)",
    re.I,
)
REMOVE_TOPIC_RE = re.compile(
    r"(?:action\s+)?(?:remove|Remove)ConversationTopic\s+(?:Current\s+)?(topic[A-Za-z0-9_]+)",
    re.I,
)
EVENT_HEADER_RE = re.compile(
    r"^\*[A-Za-z][A-Za-z0-9_]* · `([^`]+)`\*",
    re.M,
)


def strip_json_comments(text: str) -> str:
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
    text = "\n".join(out)
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r",(\s*\n\s*[}\]])", r"\1", text)


def load_dialogues() -> dict[str, dict]:
    out: dict[str, dict] = {}
    for path in sorted(CP.glob(DIALOGUE_GLOB)):
        raw = json.loads(strip_json_comments(path.read_text(encoding="utf-8")))
        for change in raw.get("Changes", []):
            entries = change.get("Entries", {})
            if not isinstance(entries, dict):
                continue
            for key, val in entries.items():
                if not isinstance(val, str):
                    continue
                if not VALID_TOPIC.match(key):
                    continue
                actions = re.findall(r"\$t\s+(\S+)\s+\d+", val)
                text = clean_dialogue(val)
                pri = DIALOGUE_SOURCE_PRIORITY.get(path.name, 40)
                prev = out.get(key)
                if prev is None or pri > prev["priority"] or (pri == prev["priority"] and len(text) > len(prev["text"])):
                    out[key] = {"text": text, "actions": actions, "source": path.name, "priority": pri}
    return out


def clean_dialogue(s: str) -> str:
    s = re.sub(r"\$t\s+\S+\s+\d+", "", s)
    for k, v in PORTRAIT.items():
        s = s.replace(k, v)
    s = s.replace("#$b#", " ")
    s = s.replace("\\n", " ")
    s = re.sub(r"\s+", " ", s).strip()
    return s


def parse_event_topics() -> dict[str, list[tuple[str, int | None, str]]]:
    out: dict[str, list[tuple[str, int | None, str]]] = defaultdict(list)

    def add(eid: str, topic: str, days: int | None, branch: str = ""):
        if not VALID_TOPIC.match(topic):
            return
        key = (topic, days, branch)
        if key not in out[eid]:
            out[eid].append(key)

    for fname in EVENT_FILES:
        path = CP / fname
        if not path.exists():
            continue
        text = strip_json_comments(path.read_text(encoding="utf-8"))
        for m in re.finditer(r'"((?:HarveyOverhaulStory\.)?[A-Za-z0-9_.]+)(?:/[^"]*)?"\s*:\s*"', text):
            eid = m.group(1)
            start = m.end()
            end = text.find('",', start)
            if end == -1:
                continue
            script = text[start:end].replace("\\n", "\n").replace('\\"', '"')
            for tm in ADD_TOPIC_RE.finditer(script):
                topic, days_s = tm.group(1), tm.group(2)
                ctx = script[max(0, tm.start() - 120) : tm.start()]
                branch = "ветка quickQuestion" if "quickQuestion" in ctx or "(break)" in ctx else ""
                add(eid, topic, int(days_s), branch)
            for tm in REMOVE_TOPIC_RE.finditer(script):
                add(eid, tm.group(1), None, "removeConversationTopic")

    for eid, extras in EVENT_TOPICS_EXTRA.items():
        for item in extras:
            if item not in out[eid]:
                out[eid].append(item)
    for eid, cds in CD_TOPICS.items():
        for topic, days in cds:
            t = (topic, days, "cooldown после сцены")
            if t not in out[eid]:
                out[eid].append(t)
    return dict(out)


def format_topic_block(topics: list, dialogues: dict) -> str:
    if not topics:
        return ""
    # dedupe by topic id, prefer entry with branch note / longer duration info
    merged: dict[str, tuple[str, int | None, str]] = {}
    for topic, days, branch in topics:
        prev = merged.get(topic)
        if prev is None or (branch and not prev[2]) or (days is not None and prev[1] is None):
            merged[topic] = (topic, days, branch)
    topics = list(merged.values())
    lines = ["", "**Топики после сцены**", ""]
    seen = set()
    for topic, days, branch in topics:
        if topic in seen:
            continue
        seen.add(topic)
        if branch == "removeConversationTopic" or (days is None and branch and "remove" in branch.lower()):
            lines.append(f"- **`{topic}`** — снимается событием ({branch or 'remove'})")
            lines.append("")
            continue
        dur = f"{days} д" if days is not None else "?"
        note = f" ({branch})" if branch else ""
        lines.append(f"- **`{topic}`** — {dur}{note}")
        d = dialogues.get(topic)
        if d:
            lines.append(f"  - **Реплика Harvey:** {d['text']}")
            if d["actions"]:
                acts = ", ".join(f"`{a}`" for a in d["actions"])
                lines.append(f"  - **Действия в диалоге:** `$t` → {acts}")
            lines.append(f"  - *Источник:* `{d['source']}`")
        elif topic.startswith("HarveyMod_CD"):
            lines.append("  - *(cooldown — отдельной реплики нет, блокирует повтор Story-сцен)*")
        else:
            lines.append("  - *(нет ключа в dialoguesHarvey*.json — gate-only или end dialogue)*")
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def strip_old_topic_blocks(section: str) -> str:
    section = re.sub(r"\n\*\*Топики после сцены\*\*\n.*?(?=\n---\n|\Z)", "\n", section, flags=re.S)
    section = re.sub(
        r"\n\*\*После сцены[^\n]*\*\*[^\n]*\n.*?(?=\n---\n|\n\*\*Топики|\Z)",
        "\n",
        section,
        flags=re.S,
    )
    return section


def inject(body: str, event_topics: dict, dialogues: dict) -> str:
    start_m = re.search(r"\n## Часть I", body)
    if not start_m:
        return body
    prefix, work = body[: start_m.start()], body[start_m.start() :]

    headers = list(EVENT_HEADER_RE.finditer(work))
    if not headers:
        return body

    parts = []
    last = 0
    for i, m in enumerate(headers):
        eid = m.group(1)
        sec_start = m.start()
        sec_end = headers[i + 1].start() if i + 1 < len(headers) else len(work)

        section = work[sec_start:sec_end]
        topics = event_topics.get(eid, [])
        block = format_topic_block(topics, dialogues)
        if not block:
            continue

        section = strip_old_topic_blocks(section)
        dash = section.rfind("\n---\n")
        if dash == -1:
            section = section.rstrip() + "\n\n" + block
        else:
            section = section[:dash].rstrip() + "\n\n" + block + section[dash:]

        parts.append(work[last:sec_start])
        parts.append(section)
        last = sec_end

    parts.append(work[last:])
    return prefix + "".join(parts)


def main():
    dialogues = load_dialogues()
    event_topics = parse_event_topics()
    md = MD.read_text(encoding="utf-8")
    app_m = re.search(r"\n## Приложение A:", md)
    if app_m:
        body, appendix = md[: app_m.start()], md[app_m.start() :]
    else:
        body, appendix = md, ""
    body = inject(body, event_topics, dialogues)
    MD.write_text(body + appendix, encoding="utf-8")
    print(f"Dialogues loaded: {len(dialogues)}")
    print(f"Events with topics: {len(event_topics)}")
    print(f"Written: {MD}")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""C# ↔ CP ID synchronization audit."""
import json
import re
from collections import defaultdict
from datetime import date
from pathlib import Path

CS = Path(r"C:\Users\Admin\HarveyOverhaulInjury")
CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
OUT = CS / "docs" / "events-inventory" / "11-id-sync-audit.md"

ID_PATTERNS = [
    (r"\b(buff[A-Za-z0-9_]+)\b", "buff"),
    (r"\b(HarveyMod_[A-Za-z0-9_]+)\b", "HarveyMod"),
    (r"\b(topic[A-Za-z0-9_]+)\b", "topic"),
    (r"\b(mail[A-Za-z0-9_]+)\b", "mail"),
    (r"\b(eventHarvey[A-Za-z0-9_]+)\b", "event"),
    (r"\b(HarveyOverhaulStory\.[A-Za-z0-9_]+)\b", "event"),
    (r'"Id"\s*:\s*"\{\{ModId\}\}_([^"]+)"', "trigger"),
    (r'\{\{ModId\}\}_([A-Za-z0-9_]+)', "trigger"),
]

PHASE_TOPIC_RE = re.compile(r"topic[A-Za-z]+Phase(?:Acute|Healing|Recovery|Cast|Rest|Limited|Surgery|Rehab|Treatment|Observation)")
PHASE_TRANSITION_RE = re.compile(r"PhaseTransition_[A-Za-z0-9_]+")
CURED_TOPIC_RE = re.compile(r"topic[A-Za-z]+Cured|topicTreatmentCompleted")

BLACKLIST = {
    "buff", "buffId", "buffs", "topic", "topics", "mail", "mails", "eventHarvey",
    "topicDays", "topicId", "topicName", "mailId", "mailIds",
}

MAIL_STRINGS_CS = re.compile(
    r'(?:addMailForTomorrow|AddMail)\s*\(\s*"([^"]+)"'
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
    return re.sub(r",(\s*\n\s*[}\]])", r"\1", text)


def parse_phase_buffs_cs() -> dict[str, str]:
    text = (CS / "Managers" / "InjuryManager.cs").read_text(encoding="utf-8")
    buffs = {}
    for m in re.finditer(r'\[\d+\]\s*=\s*"(HarveyMod_[^"]+)"', text):
        buffs[m.group(1)] = "InjuryManager.GetPhaseBuffId"
    return buffs


def parse_injury_names_cs() -> list[str]:
    text = (CS / "Managers" / "InjuryManager.cs").read_text(encoding="utf-8")
    return sorted(set(re.findall(r'\["(buff[A-Za-z]+)"\]\s*=\s*new', text)))


def parse_phase_topics_cs() -> dict[str, str]:
    topics = {}
    for injury in parse_injury_names_cs():
        name = injury.replace("buff", "")
        for stage in ("Acute", "Healing", "Recovery"):
            tid = f"topic{name}Phase{stage}"
            topics[tid] = "InjuryManager.GetPhaseTopicId → InteractionHandler"
    return topics


def parse_completion_topics_cs() -> dict[str, str]:
    text = (CS / "EventHandlers" / "InteractionHandler.cs").read_text(encoding="utf-8")
    topics = {}
    for m in re.finditer(r'"(topic[A-Za-z]+Cured)"', text):
        topics[m.group(1)] = "InteractionHandler (completion)"
    topics["topicTreatmentCompleted"] = "TreatmentManager.AddTopic"
    return topics


def parse_triggers_cs() -> dict[str, str]:
    text = (CS / "Core" / "Constants.cs").read_text(encoding="utf-8")
    triggers = {}
    for m in re.finditer(r'=\s*"\{\{ModId\}\}_([^"]+)"', text):
        triggers[m.group(1)] = "Constants.Triggers → StateManager.AppliedTriggers"
    return triggers


def parse_constants_cs() -> dict[str, dict]:
    """Known IDs from Constants.cs grouped by kind."""
    text = (CS / "Core" / "Constants.cs").read_text(encoding="utf-8")
    out: dict[str, dict] = defaultdict(dict)
    blocks = {
        "CureBuffs": "buff",
        "InjuryBuffs": "HarveyMod/buff",
        "ConversationTopics": "topic",
        "MailIds": "mail",
        "Triggers": "trigger",
    }
    for block, kind in blocks.items():
        block_m = re.search(rf"class {block}\s*\{{(.*?)\n\s*\}}", text, re.S)
        if not block_m:
            continue
        for m in re.finditer(r'=\s*"([^"]+)"', block_m.group(1)):
            val = m.group(1).replace("{{ModId}}_", "")
            out[val] = {"kind": kind, "created": f"Constants.{block}"}
    return out


def parse_mail_created_cs() -> dict[str, str]:
    mails = {}
    for path in CS.rglob("*.cs"):
        rel = str(path.relative_to(CS))
        text = path.read_text(encoding="utf-8", errors="replace")
        for m in MAIL_STRINGS_CS.finditer(text):
            mails[m.group(1)] = rel
    return mails


def extract_cs_ids() -> dict[str, dict]:
    ids: dict[str, dict] = defaultdict(
        lambda: {"types": set(), "created": set(), "used": set()}
    )
    cs_files = list(CS.rglob("*.cs"))

    for path in cs_files:
        rel = str(path.relative_to(CS))
        text = path.read_text(encoding="utf-8", errors="replace")
        for pat, typ in ID_PATTERNS:
            for m in re.finditer(pat, text):
                iid = m.group(1) if m.lastindex else m.group(0)
                if iid in BLACKLIST or len(iid) < 4:
                    continue
                ids[iid]["types"].add(typ)
                ctx = text[max(0, m.start() - 150) : m.start()]
                if re.search(
                    r"(AddTopic|AddBuff|addMail|AddMail|AddConversationTopic|Apply\w+|CreateDebuffState|startEvent|addMailForTomorrow)",
                    ctx,
                ):
                    ids[iid]["created"].add(rel)
                ids[iid]["used"].add(rel)

    # Inject known dynamic / constant IDs
    for bid, src in parse_phase_buffs_cs().items():
        ids[bid]["types"].add("phase_buff")
        ids[bid]["created"].add(src)
        ids[bid]["used"].update({"Managers/InjuryManager.cs", "Managers/TreatmentManager.cs"})

    for tid, src in parse_phase_topics_cs().items():
        ids[tid]["types"].add("phase_topic")
        ids[tid]["created"].add(src)
        ids[tid]["used"].add("EventHandlers/InteractionHandler.cs")

    for tid, src in parse_completion_topics_cs().items():
        ids[tid]["types"].add("completion_topic")
        ids[tid]["created"].add(src)
        ids[tid]["used"].add("EventHandlers/InteractionHandler.cs")

    for tr, src in parse_triggers_cs().items():
        ids[tr]["types"].add("trigger")
        ids[tr]["created"].add(src)
        ids[tr]["used"].update({"Managers/InjuryManager.cs", "Managers/StateManager.cs"})

    for iid, meta in parse_constants_cs().items():
        ids[iid]["types"].add(meta["kind"])
        ids[iid]["used"].add("Core/Constants.cs")

    for mid, rel in parse_mail_created_cs().items():
        ids[mid]["types"].add("mail")
        ids[mid]["created"].add(rel)
        ids[mid]["used"].add(rel)

    return ids


def load_cp_json_entries():
    cats = {
        "buff": set(),
        "mail": set(),
        "dialogue": set(),
        "event": set(),
        "trigger": set(),
    }
    ref_topic: dict[str, set[str]] = defaultdict(set)
    ref_event: dict[str, set[str]] = defaultdict(set)
    ref_buff: dict[str, set[str]] = defaultdict(set)
    ref_mail: dict[str, set[str]] = defaultdict(set)
    raw_files: dict[str, str] = {}

    for path in CP.glob("*.json"):
        raw_files[path.name] = strip_json_comments(
            path.read_text(encoding="utf-8", errors="replace")
        )

    for name, text in raw_files.items():
        try:
            data = json.loads(text)
        except json.JSONDecodeError:
            data = None

        if data is not None:

            def walk(obj, fname):
                if isinstance(obj, dict):
                    if "Entries" in obj and isinstance(obj["Entries"], dict):
                        for k in obj["Entries"]:
                            classify_key(k, fname, cats)
                    if "Id" in obj and isinstance(obj.get("Id"), str) and "{{ModId}}" in obj["Id"]:
                        cats["trigger"].add(obj["Id"].replace("{{ModId}}_", ""))
                    for v in obj.values():
                        walk(v, fname)
                elif isinstance(obj, list):
                    for v in obj:
                        walk(v, fname)

            walk(data, name)

        for m in re.finditer(
            r'\b(buff[A-Za-z0-9_]+|HarveyMod_[A-Za-z0-9_]+|topic[A-Za-z0-9_]+|mail[A-Za-z0-9_]+|eventHarvey[A-Za-z0-9_]+|HarveyOverhaulStory\.[A-Za-z0-9_]+|PhaseTransition_[A-Za-z0-9_]+)\b',
            text,
        ):
            ref = m.group(1)
            if ref.startswith("topic") or ref.startswith("PhaseTransition"):
                ref_topic[ref].add(name)
            elif ref.startswith("event") or ref.startswith("HarveyOverhaul"):
                ref_event[ref].add(name)
            elif ref.startswith("mail"):
                ref_mail[ref].add(name)
            elif ref.startswith("buff") or ref.startswith("HarveyMod_"):
                ref_buff[ref].add(name)

        for m in re.finditer(r'"Id"\s*:\s*"\{\{ModId\}\}_([^"]+)"', text):
            cats["trigger"].add(m.group(1))

    return cats, raw_files, ref_topic, ref_event, ref_buff, ref_mail


def classify_key(key: str, fname: str, cats: dict):
    if key.startswith("buff") or key.startswith("HarveyMod_"):
        if fname.startswith("mail"):
            cats["mail"].add(key)
        else:
            cats["buff"].add(key)
    if key.startswith("mail"):
        cats["mail"].add(key)
    if key.startswith("topic") or key.startswith("PhaseTransition"):
        cats["dialogue"].add(key)
    if key.startswith("eventHarvey") or key.startswith("HarveyOverhaulStory."):
        cats["event"].add(key)


def dialogue_keys(raw_files: dict) -> set[str]:
    keys = set()
    for name, text in raw_files.items():
        if not name.startswith("dialogues"):
            continue
        for m in re.finditer(r'"((?:topic|PhaseTransition|eventSeen)[^"]+)"\s*:', text):
            keys.add(m.group(1))
    return keys


def infer_type(iid: str, info: dict) -> str:
    if info.get("types"):
        return "/".join(sorted(info["types"]))
    if iid.startswith("PhaseTransition"):
        return "phase_transition (CP-only key)"
    if iid.startswith(("buff", "HarveyMod_")):
        return "buff"
    if iid.startswith("topic"):
        return "topic"
    if iid.startswith("mail"):
        return "mail"
    if iid.startswith("event") or iid.startswith("HarveyOverhaul"):
        return "event"
    if iid.startswith("trigger"):
        return "trigger"
    return "?"


def cp_locations(iid, cp_cats, ref_topic, ref_event, ref_buff, ref_mail) -> list[str]:
    places = []
    if iid in cp_cats["buff"]:
        places.append("Data/Buffs")
    if iid in cp_cats["mail"]:
        places.append("Data/Mail")
    if iid in cp_cats["event"]:
        places.append("Data/Events")
    if iid in cp_cats["trigger"]:
        places.append("Data/TriggerActions")
    if iid in cp_cats["dialogue"]:
        places.append("Characters/Dialogue/Harvey")
    for ref_map, label in (
        (ref_buff, "ref:buff"),
        (ref_mail, "ref:mail"),
        (ref_topic, "ref:topic"),
        (ref_event, "ref:event"),
    ):
        for fn in sorted(ref_map.get(iid, [])):
            places.append(fn if label == "ref:topic" else f"{fn} ({label})")
    return sorted(set(places))


def status_row(iid, info, cp_cats, raw_files, dlg_keys, ref_topic, ref_event, ref_buff, ref_mail):
    typ = infer_type(iid, info)
    in_cs = bool(info.get("created") or info.get("used"))
    cp_places = cp_locations(iid, cp_cats, ref_topic, ref_event, ref_buff, ref_mail)
    in_cp = bool(cp_places)

    created = ", ".join(sorted(info.get("created", []))[:4]) or "—"
    if len(info.get("created", [])) > 4:
        created += "…"

    used_parts = []
    if info.get("used"):
        used_parts.append("C#: " + ", ".join(sorted(info["used"])[:3]))
    if cp_places:
        used_parts.append("CP: " + ", ".join(cp_places[:4]))
    used = "; ".join(used_parts) or "—"

    st = "OK"

    # C# creates but missing in CP
    if info.get("created"):
        if "phase_buff" in info.get("types", set()) and iid not in cp_cats["buff"]:
            st = "❌ phase buff — нет в CP Buffs"
        elif info.get("types") & {"mail"} and iid not in cp_cats["mail"]:
            st = "❌ C# mail — нет в CP Mail"
        elif iid.startswith("HarveyMod_") and "mail" in info.get("types", set()) and iid not in cp_cats["mail"]:
            st = "❌ C# mail (HarveyMod_*) — нет в CP Mail"
        elif "completion_topic" in info.get("types", set()) and iid not in dlg_keys:
            st = "⚠️ completion topic — нет диалога CP"
        elif "phase_topic" in info.get("types", set()) and iid not in dlg_keys:
            st = "⚠️ phase topic C# — нет ключа в dialogues"

    # CP expects but C# doesn't create
    if st == "OK" and in_cp and not info.get("created"):
        cp_only_refs = (
            ref_topic.get(iid, set())
            | ref_event.get(iid, set())
            | ref_buff.get(iid, set())
            | ref_mail.get(iid, set())
        )
        precond_files = {f for f in cp_only_refs if f.startswith(("events", "triggers"))}
        if precond_files and not in_cs:
            st = "⚠️ CP preconditions — C# не создаёт"
        elif iid.startswith("topic") and iid in dlg_keys and not in_cs:
            if not iid.startswith("PhaseTransition"):
                st = "⚠️ CP dialogue key — C# не создаёт topic"

    # Completion topics used in C# must have dialogue
    if st == "OK" and CURED_TOPIC_RE.match(iid) and info.get("created") and iid not in dlg_keys:
        st = "⚠️ completion topic — нет диалога CP"

    return (
        iid,
        typ,
        created,
        used,
        "да" if in_cp else "нет",
        "да" if in_cs else "нет",
        st,
    )


def main():
    cs_ids = extract_cs_ids()
    cp_cats, raw_files, ref_topic, ref_event, ref_buff, ref_mail = load_cp_json_entries()
    dlg_keys = dialogue_keys(raw_files)

    all_ids = set(cs_ids.keys())
    all_ids.update(cp_cats["buff"])
    all_ids.update(cp_cats["mail"])
    all_ids.update(cp_cats["event"])
    all_ids.update(cp_cats["dialogue"])
    all_ids.update(cp_cats["trigger"])
    all_ids.update(ref_topic.keys())
    all_ids.update(ref_event.keys())
    all_ids.update(ref_buff.keys())
    all_ids.update(ref_mail.keys())

    rows = []
    for iid in sorted(all_ids):
        if iid in BLACKLIST or len(iid) < 5:
            continue
        info = cs_ids.get(iid, {"types": set(), "created": set(), "used": set()})
        rows.append(
            status_row(
                iid, info, cp_cats, raw_files, dlg_keys,
                ref_topic, ref_event, ref_buff, ref_mail,
            )
        )

    phase_buffs = set(parse_phase_buffs_cs())
    missing_buffs = sorted(b for b in phase_buffs if b not in cp_cats["buff"])

    cs_phase_topics = set(parse_phase_topics_cs())
    cp_phase_format = {k for k in dlg_keys if PHASE_TOPIC_RE.match(k)}
    cp_phase_transition = {k for k in dlg_keys if PHASE_TRANSITION_RE.match(k)}
    phase_topic_in_dialogue = cs_phase_topics & dlg_keys
    phase_topic_missing = sorted(cs_phase_topics - dlg_keys)

    completion_cs = set(parse_completion_topics_cs())
    completion_no_dialogue = sorted(t for t in completion_cs if t not in dlg_keys)

    problems = [r for r in rows if not r[6].startswith("OK")]
    p0 = [r for r in problems if r[6].startswith("❌")]

    lines = [
        "# Синхронизация ID: C# InjuryCare ↔ Content Patcher",
        "",
        f"Аудит соответствия идентификаторов между SMAPI-модом и content pack. **Автоген {date.today().isoformat()}**.",
        "",
        f"- C# файлов: `{len(list(CS.rglob('*.cs')))}`",
        f"- CP JSON (`assets/Code/`): `{len(list(CP.glob('*.json')))}`",
        f"- Уникальных ID в таблице: **{len(rows)}**",
        f"- Проблемных строк: **{len(problems)}** (из них ❌: **{len(p0)}**)",
        "",
        "## Проверки",
        "",
        "| # | Правило | Результат |",
        "|---|---|---|",
        f"| 1 | Phase buff IDs из `GetPhaseBuffId` → `Data/Buffs` | {len(missing_buffs)} отсутствуют"
        + (f": `{', '.join(missing_buffs[:6])}{'…' if len(missing_buffs)>6 else ''}`" if missing_buffs else " — **все на месте**")
        + " |",
        f"| 2 | Phase topic формат `topic{{Injury}}Phase{{Acute|Healing|Recovery}}` | C#: {len(cs_phase_topics)}; ключи в dialogues: {len(phase_topic_in_dialogue)}/{len(cs_phase_topics)}; альт. `PhaseTransition_*`: {len(cp_phase_transition)} |",
        f"| 3 | C# mail (`addMailForTomorrow`) → CP Mail | ❌ см. таблицу проблем |",
        f"| 4 | CP preconditions / When / event script | ⚠️ см. «CP не создаёт C#» |",
        f"| 5 | Completion topics `topic*Cured`, `topicTreatmentCompleted` | без диалога: {len(completion_no_dialogue)} |",
        f"| 6 | Trigger IDs `{{ModId}}_trigger*` | C#: {len(parse_triggers_cs())}; CP TriggerActions: {len(cp_cats['trigger'])} |",
        "",
        "## Phase buff: отсутствуют в CP",
        "",
    ]
    if missing_buffs:
        for b in missing_buffs:
            lines.append(f"- `{b}`")
    else:
        lines.append("- *(все phase buff из InjuryManager найдены в buffsInjury/buffsCure)*")

    lines.extend([
        "",
        "## Phase topics: C# формат без ключа в dialogues",
        "",
    ])
    if phase_topic_missing:
        for t in phase_topic_missing[:20]:
            lines.append(f"- `{t}`")
        if len(phase_topic_missing) > 20:
            lines.append(f"- … и ещё {len(phase_topic_missing) - 20}")
    else:
        lines.append("- *(все 33 phase topic имеют ключи в dialoguesHarveyCure/Injury)*")

    lines.extend([
        "",
        "## Две схемы phase-диалогов в CP",
        "",
        "- **C# / Cure:** `topicDeepCutsPhaseAcute`, `topicDeepCutsPhaseHealing`, … — создаёт `GetPhaseTopicId`",
        "- **Injury (legacy):** `PhaseTransition_DeepCuts_2`, `PhaseTransition_DeepCuts_3` — **C# не создаёт**; вероятно мёртвые ключи или старая схема",
        "",
        f"- Ключей `topic*Phase*` в dialogues: **{len(cp_phase_format)}**",
        f"- Ключей `PhaseTransition_*`: **{len(cp_phase_transition)}**",
        "",
        "## Критичные разрывы (❌)",
        "",
    ])
    for r in p0:
        lines.append(f"- `{r[0]}` — {r[6]}")
    if not p0:
        lines.append("- *(нет строк со статусом ❌)*")

    lines.extend([
        "",
        "## Таблица проблем",
        "",
        "| ID | Тип | Где создаётся | Где используется | Есть в CP? | Есть в C#? | Статус |",
        "|---|---|---|---|---|---|---|",
    ])
    for r in problems:
        lines.append(f"| `{r[0]}` | {r[1]} | {r[2]} | {r[3]} | {r[4]} | {r[5]} | {r[6]} |")

    lines.extend([
        "",
        "## Полная таблица",
        "",
        "| ID | Тип | Где создаётся | Где используется | Есть в CP? | Есть в C#? | Статус |",
        "|---|---|---|---|---|---|---|",
    ])
    for r in rows:
        lines.append(f"| `{r[0]}` | {r[1]} | {r[2]} | {r[3]} | {r[4]} | {r[5]} | {r[6]} |")

    lines.extend([
        "",
        f"**Статус:** автоген {date.today().isoformat()}.",
        "",
    ])

    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {OUT} ({len(rows)} rows, {len(problems)} issues, {len(p0)} critical)")


if __name__ == "__main__":
    main()

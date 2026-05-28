#!/usr/bin/env python3
"""Generate readable book-style MD from HarveyOverhaul CP events."""
import json
import re
from collections import defaultdict
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
OUT = Path(r"C:\Users\Admin\HarveyOverhaulInjury\docs\events-inventory\08-events-as-book.md")

EVENT_FILES = ["eventsCare.json", "eventsMineRescue.json", "events.json"]
TARGET_RE = re.compile(r'"Target"\s*:\s*"(Data/Events/[^"]+)"')
EVENT_KEY_RE = re.compile(
    r'"((?:[A-Za-z][A-Za-z0-9_.]*)(?:/[^"]*)?)"\s*:\s*(?:"((?:\\.|[^"\\])*)"|(\[))',
    re.S,
)
SKIP = {
    "acceptWalk", "declineFood", "refuseCheckup", "irregularEating",
    "HarveySkullPromise", "leaveHospital",
}
PORTRAIT = {
    "$a": "*(строго)*", "$s": "*(грустно)*", "$h": "*(с улыбкой)*",
    "$l": "*(нежно)*", "$u": "*(серьёзно)*", "$8": "*(в панике)*",
    "$0": "", "$1": "", "$2": "", "$3": "", "$4": "", "$5": "",
}

CHAPTERS = [
    ("Часть I. Первая встреча", [
        "eventHarveyFirstMeeting", "eventHarveyCheckup",
        "eventHarveyFirstVisit", "eventHarveySecondVisit", "eventHarveyFirstWalk",
    ]),
    ("Часть II. История доверия (HarveyOverhaul Story)", [
        "HarveyOverhaulStory.E1_SlipperyPath", "HarveyOverhaulStory.E2_InsistentExam",
        "HarveyOverhaulStory.E3_ForestApothecary", "HarveyOverhaulStory.E4_PierBreath",
        "HarveyOverhaulStory.E5_StormBeside", "HarveyOverhaulStory.E6_SayItOutLoud",
        "HarveyOverhaulStory.E7_TownSip_Sunny", "HarveyOverhaulStory.E8_QuietShelf",
    ]),
    ("Часть III. Лечение и клиника", [
        "HarveyMod_FirstTreatment",
        "HarveyMod_NightCrisis_Dating", "HarveyMod_NightCrisis_PreDating",
        "HarveyMod_BirthdayHospital_Dating", "HarveyMod_BirthdayHospital_Friend",
        "HarveyMod_TreatmentPlanMeeting",
        "eventHarveyMedicalCheck", "eventHarveyMedicalCheck_Dating", "eventHarveyTraumaExam",
        "eventHarveyEmergencyCare", "eventHarveyExhaustion", "eventHarveyTreatmentCollapse",
        "eventStayInHospital",
    ]),
    ("Часть IV. Шахта и раны (InjuryCare)", [
        "eventHarveyMineRescueDating", "eventHarveyMineRescue", "eventHarveyMinorMineRescue",
        "eventHarveyMineInterception", "eventHarveySkullCavePrevention",
    ]),
    ("Часть V. Забота и ночные тревоги", [
        "eventHarveyCheckHealthFarmer", "eventHarveyCheckFarmerOutsideAfter22",
        "eventHarveyMorningCheckup", "eventHarveyLateNightCollapse",
    ]),
    ("Часть VI. Гроза и страх", [
        "eventHarveyStormComfortFarm", "eventHarveyStormComfortForest",
        "eventHarveyStormComfortTown", "eventHarveyStormComfortMine",
        "eventHarveyStormComfortMountain", "eventHarveyStormComfortDesert",
        "eventRescueOperation",
    ]),
    ("Часть VII. Сердце и свидания", [
        "eventHarveyFirstDate", "eventHarveyMountainDate", "eventHarveyPropose",
        "eventHarveyRoomCheckup", "eventHarveyRoomCheckup2",
    ]),
]

TITLES = {
    "eventHarveyFirstMeeting": "Автобусная остановка",
    "eventHarveyCheckup": "Первый осмотр",
    "eventHarveyFirstVisit": "Визит на ферму",
    "eventHarveySecondVisit": "Второй визит — травяной чай",
    "eventHarveyFirstWalk": "Прогулка в лес",
    "HarveyOverhaulStory.E1_SlipperyPath": "Скользкая дорожка",
    "HarveyOverhaulStory.E2_InsistentExam": "Настойчивый осмотр",
    "HarveyOverhaulStory.E3_ForestApothecary": "Лесная аптека",
    "HarveyOverhaulStory.E4_PierBreath": "Дыхание у пирса",
    "HarveyOverhaulStory.E5_StormBeside": "Рядом в грозу",
    "HarveyOverhaulStory.E6_SayItOutLoud": "Сказать вслух",
    "HarveyOverhaulStory.E7_TownSip_Sunny": "Глоток солнца в городе",
    "HarveyOverhaulStory.E8_QuietShelf": "Тихая полка",
    "HarveyMod_FirstTreatment": "Первое серьёзное лечение",
    "HarveyMod_NightCrisis_Dating": "Ночной кризис (dating/married)",
    "HarveyMod_NightCrisis_PreDating": "Ночной кризис (до dating)",
    "HarveyMod_BirthdayHospital_Dating": "День рождения в больнице (dating)",
    "HarveyMod_BirthdayHospital_Friend": "День рождения в больнице (друг)",
    "HarveyMod_TreatmentPlanMeeting": "План лечения",
    "eventHarveyMedicalCheck": "Медосмотр по напоминанию (pre-dating)",
    "eventHarveyMedicalCheck_Dating": "Медосмотр по напоминанию (dating)",
    "eventHarveyTraumaExam": "Осмотр старых шрамов",
    "eventHarveyEmergencyCare": "Экстренная помощь",
    "eventHarveyExhaustion": "Истощение",
    "eventHarveyTreatmentCollapse": "Коллапс на ферме",
    "eventStayInHospital": "Остаёшься в палате",
    "eventHarveyMineRescueDating": "Спасение из шахты (любовь)",
    "eventHarveyMineRescue": "Спасение из шахты",
    "eventHarveyMinorMineRescue": "Лёгкое спасение из шахты",
    "eventHarveyMineInterception": "Перехват у входа в шахту",
    "eventHarveySkullCavePrevention": "Пещера черепов",
    "eventHarveyCheckHealthFarmer": "Проверка после обморока",
    "eventHarveyCheckFarmerOutsideAfter22": "Ночная прогулка",
    "eventHarveyMorningCheckup": "Утренний осмотр",
    "eventHarveyLateNightCollapse": "Обморок в городе",
    "eventHarveyStormComfortFarm": "Укрытие на ферме",
    "eventHarveyStormComfortForest": "Укрытие в лесу",
    "eventHarveyStormComfortTown": "Укрытие в городе",
    "eventHarveyStormComfortMine": "Укрытие у шахты",
    "eventHarveyStormComfortMountain": "Укрытие на горе",
    "eventHarveyStormComfortDesert": "Укрытие в пустыне",
    "eventRescueOperation": "Операция спасения",
    "eventHarveyFirstDate": "Первое свидание",
    "eventHarveyMountainDate": "Свидание в горах",
    "eventHarveyPropose": "Предложение",
    "eventHarveyRoomCheckup": "Осмотр в комнате",
    "eventHarveyRoomCheckup2": "Неожиданный визит",
}

# Как событие реально попадает в игру (помимо CP-preconditions)
LAUNCH = {
    "eventHarveyMineRescueDating": {
        "type": "C# startEvent (+ дубль vanilla entry)",
        "detail": "`PassOutHandler.TriggerMineRescueEvents()` → утро после смерти в Mine → warp (17,7) → `startEvent`. CP-key с guards на seen/relationship при vanilla entry не проверяется при C#-запуске.",
    },
    "eventHarveyMineRescue": {
        "type": "C# startEvent (fallback)",
        "detail": "Тот же C#-путь, если `eventHarveyMineRescueDating` отсутствует в `Data/Events/Mine`. При текущем CP почти не используется.",
    },
    "eventHarveyMinorMineRescue": {
        "type": "C# startEvent",
        "detail": "C# выбирает minor при `!HasAnyBuff(Severe)`, но боевая смерть в шахте всегда даёт `buffBadlyHurt` → на практике **недостижимо**.",
    },
    "eventHarveyMineInterception": {
        "type": "SpaceCore PlayEvent",
        "detail": "`triggersCare.json` → `triggerHarveyMineWarning` при `LocationChanged` в Mine.",
    },
    "eventHarveySkullCavePrevention": {
        "type": "SpaceCore PlayEvent",
        "detail": "`triggerLocationReactionSkullCaveExit` (SkullCave) и `triggerHarveySkullCaveWarning` (Mine+SkullCave — условие битое).",
    },
    "eventHarveyEmergencyCare": {
        "type": "Script-only (нет launcher)",
        "detail": "Ключ без preconditions. PlayEvent был в **отключённом** `triggersInjury.json`. C# выставляет buff/topic без cutscene.",
    },
    "eventHarveyExhaustion": {
        "type": "Script-only (нет launcher)",
        "detail": "Ключ без preconditions. BETAS-триггер в отключённом `triggersInjury.json`. C# — `topicFarmerExhausted` без Hospital-сцены.",
    },
    "eventHarveyTreatmentCollapse": {
        "type": "Script-only (нет launcher)",
        "detail": "Ключ без preconditions. Нет C# `startEvent` / активного trigger.",
    },
    "eventStayInHospital": {
        "type": "Script-only (нет launcher)",
        "detail": "Ключ без preconditions. Госпитализацию делает C# `HospitalizationManager`, не это событие.",
    },
}

CS_CONDITIONS = {
    "eventHarveyMineRescueDating": [
        "Вчера: HP ≤ 0 в локации Mine (боевая смерть)",
        "Отношения с Харви: dating или married",
        "Severe-травма (`buffBadlyHurt` и др.)",
        "Событие ещё не в `eventsSeen` (иначе только topic `topicMineInjuryRescue`)",
    ],
    "eventHarveyMineRescue": [
        "Те же C#-условия, что у dating-версии",
        "Fallback, если dating-entry нет в content pack",
    ],
    "eventHarveyMinorMineRescue": [
        "C#: `!HasAnyBuff(Severe)` после mine pass-out",
        "Конфликт: mine combat death всегда даёт severe → minor не срабатывает",
    ],
}

TOPIC_RU = {
    "topicAgreedCheckup": "согласие на осмотр (fork первой встречи)",
    "topicFirstMeeting": "после первой встречи на автобусе",
    "topicHarveyFirstVisit": "после первого визита на ферму",
    "topicHarveySecondVisit": "после второго визита",
    "topicPassedOutInTown": "обморок в городе (C# PassOutHandler)",
    "topicHarveyMandatoryCheckup": "после ночной проверки / обморока",
    "topicMineInjuryRescue": "после mine rescue (событие или C# fallback)",
    "topicDiagnosisComplete": "завершена диагностика (HarveyMod)",
    "topicRescueOperation": "запуск операции спасения",
    "topicStressThunder": "стресс от грозы (бафф `buffStressThunder`)",
}

BUFF_RU = {
    "buffStressThunder": "стресс от грозы",
    "buffSurgicalWound": "хирургическая рана",
    "buffBruisedRibs": "ушиб рёбер",
    "buffSprainedAnkle": "растяжение",
    "buffBackStrain": "боль в спине",
    "buffDeepCuts": "глубокие порезы",
    "buffBurnWounds": "ожоги",
    "buffTornMuscles": "разрыв мышц",
    "buffConcussion": "сотрясение",
    "buffFracturedBone": "перелом",
    "buffShrapnelWounds": "осколочные раны",
    "buffInfectedWound": "инфекция",
}


def parse_spacecore_triggers() -> dict[str, list[dict]]:
    path = CP / "triggersCare.json"
    if not path.exists():
        return {}
    text = strip_comments(path.read_text(encoding="utf-8"))
    try:
        data = json.loads(text)
    except json.JSONDecodeError:
        return {}
    out: dict[str, list[dict]] = defaultdict(list)
    for change in data.get("Changes", []):
        for entry in change.get("Entries", {}).values():
            if not isinstance(entry, dict):
                continue
            trigger = entry.get("Trigger", "")
            condition = entry.get("Condition", "")
            for action in entry.get("Actions", []):
                m = re.search(r"SpaceCore_PlayEvent\s+(\w+)", str(action))
                if m:
                    out[m.group(1)].append({"trigger": trigger, "condition": condition})
    return dict(out)


def format_time_range(raw: str) -> str:
    parts = raw.strip().split()
    if len(parts) != 2:
        return raw

    def fmt(t: str) -> str:
        t = t.zfill(4)
        h, m = int(t[:2]), int(t[2:])
        if h >= 24:
            h -= 24
            return f"{h:02d}:{m:02d} (след. день)"
        return f"{h:02d}:{m:02d}"

    return f"{fmt(parts[0])}–{fmt(parts[1])}"


def describe_topics(raw: str) -> str:
    names = re.findall(r"topic[A-Za-z0-9_]+", raw)
    if not names:
        return raw.strip()
    bits = []
    for n in names:
        hint = TOPIC_RU.get(n, "")
        bits.append(f"`{n}`" + (f" ({hint})" if hint else ""))
    return ", ".join(bits)


def describe_buffs(raw: str) -> str:
    names = re.findall(r"buff[A-Za-z0-9_]+", raw)
    if not names:
        return raw.strip()
    bits = []
    for n in names:
        hint = BUFF_RU.get(n, "")
        bits.append(f"`{n}`" + (f" — {hint}" if hint else ""))
    return "; ".join(bits)


def parse_gsq_clause(clause: str) -> str | None:
    clause = clause.strip()
    if not clause:
        return None

    m = re.match(r"!PLAYER_HAS_MET Current (\w+)", clause)
    if m:
        return f"Ещё не знакомы с {m.group(1)}"

    m = re.match(r"PLAYER_HAS_CONVERSATION_TOPIC Current (.+)", clause)
    if m:
        return f"Активен разговорный топик: {describe_topics(m.group(1))}"

    m = re.match(r"!PLAYER_HAS_CONVERSATION_TOPIC Current (.+)", clause)
    if m:
        return f"Нет топика: {describe_topics(m.group(1))}"

    m = re.match(r"!PLAYER_HAS_SEEN_EVENT Current ([\w.]+)", clause)
    if m:
        return f"Не просмотрено событие `{m.group(1)}`"

    m = re.match(r"PLAYER_HAS_SEEN_EVENT Current ([\w.]+)", clause)
    if m:
        return f"Просмотрено событие `{m.group(1)}`"

    m = re.match(r"PLAYER_NPC_RELATIONSHIP\s+Current\s+(\w+)\s*(.*)", clause)
    if m:
        npc, rels = m.group(1), m.group(2).strip()
        if rels:
            return f"Отношения с {npc}: {' или '.join(rels.split())}"
        return f"Отношения с {npc}"

    m = re.match(r"PLAYER_HAS_BUFF Current (.+)", clause)
    if m:
        return f"Активен бафф: {describe_buffs(m.group(1))}"

    m = re.match(r"PLAYER_HAS_MAIL Current (\w+)", clause)
    if m:
        return f"Получено письмо `{m.group(1)}`"

    m = re.match(r"!PLAYER_HAS_MAIL Current (\w+) Received", clause)
    if m:
        return f"Письмо `{m.group(1)}` ещё не получено"

    m = re.match(r"DAYS_PLAYED (\d+)", clause)
    if m:
        return f"Прошло дней в сохранении ≥ {m.group(1)}"

    m = re.match(r"PLAYER_LOCATION_NAME Current (.+)", clause)
    if m:
        locs = m.group(1).split()
        if len(locs) > 1:
            return f"Локация игрока: {' и '.join(locs)} (в CP — часто ошибка OR)"
        return f"Локация игрока: {m.group(1)}"

    m = re.match(r"Spiderbuttons\.BETAS_NPC_IS_DATING Current (\w+)", clause)
    if m:
        return f"Мод BETAS: dating с {m.group(1)}"

    m = re.match(r"PLAYER_FRIENDSHIP_POINTS Current (\w+) (\d+)", clause)
    if m:
        hearts = int(m.group(2)) // 250
        return f"Дружба с {m.group(1)} ≥ {hearts} сердечек"

    m = re.match(r"TIME (\d+) (\d+)", clause, re.I)
    if m:
        return f"Время: {format_time_range(f'{m.group(1)} {m.group(2)}')}"

    m = re.match(r"RANDOM ([\d.]+)", clause, re.I)
    if m:
        return f"Случайность {float(m.group(1)) * 100:.0f}%"

    if clause.startswith("GameStateQuery "):
        inner = clause[len("GameStateQuery "):]
        parts = [parse_gsq_clause(p.strip()) for p in inner.split(",")]
        parts = [p for p in parts if p]
        return "; ".join(parts) if parts else inner

    return clause


def parse_precondition_segment(seg: str) -> str | None:
    seg = seg.strip()
    if not seg:
        return None

    if seg.startswith("GameStateQuery "):
        return parse_gsq_clause(seg)

    if seg.startswith("Time "):
        return f"Время суток: {format_time_range(seg[5:])}"

    if seg.startswith("Weather "):
        w = seg[8:].strip()
        weather = {"storm": "гроза", "sunny": "солнечно", "wind": "ветер"}.get(w.lower(), w)
        return f"Погода: {weather}"

    if seg.startswith("Friendship "):
        m = re.match(r"Friendship (\w+) (\d+)", seg)
        if m:
            hearts = int(m.group(2)) // 250
            return f"Дружба с {m.group(1)} ≥ {hearts} сердечек ({m.group(2)} pts)"
        return seg

    if seg.startswith("DayOfWeek "):
        days = seg[10:].split()
        ru = {"Mon": "пн", "Tue": "вт", "Wed": "ср", "Thu": "чт", "Fri": "пт", "Sat": "сб", "Sun": "вс"}
        return f"День недели: {', '.join(ru.get(d, d) for d in days)}"

    if seg == "!FestivalDay":
        return "Не день фестиваля"

    if seg.startswith("HasSeenEvent "):
        return f"Просмотрено событие `{seg.split()[1]}`"

    if seg.startswith("LocationName "):
        return f"Игрок в локации `{seg.split()[1]}`"

    if seg.startswith("Season "):
        return f"Сезон: {seg.split()[1]}"

    if seg.startswith("Day "):
        return f"Число месяца: {seg.split()[1]}"

    if seg.startswith("Random "):
        try:
            pct = float(seg.split()[1]) * 100
            return f"Случайный шанс {pct:.0f}% при входе в локацию"
        except (IndexError, ValueError):
            return seg

    return parse_gsq_clause(seg) or seg


def format_conditions(ev: dict, spacecore: dict[str, list[dict]]) -> list[str]:
    eid = ev["id"]
    loc = ev["location"]
    pre = ev["preconditions"]
    lines = ["**Условия срабатывания**", ""]

    launch = LAUNCH.get(eid)
    if launch:
        lines.append(f"- **Запуск:** {launch['type']}")
        lines.append(f"  {launch['detail']}")
    elif pre:
        lines.append(f"- **Запуск:** vanilla event entry — войти в `{loc}` при выполнении условий ниже")
    else:
        lines.append(f"- **Запуск:** только скриптовый вызов (`switchEvent` / PlayEvent / C#) — vanilla entry **нет**")

    if pre:
        lines.append("- **CP preconditions:**")
        for seg in pre.split("/"):
            desc = parse_precondition_segment(seg)
            if desc:
                lines.append(f"  - {desc}")
        lines.append(f"  - *Сырой ключ:* `{eid}/{pre}`")
    else:
        lines.append("- **CP preconditions:** *(нет — ключ `{eid}` без `/Time`/`/GameStateQuery`)*".format(eid=eid))

    if eid in CS_CONDITIONS:
        lines.append("- **Дополнительно (C# InjuryCare):**")
        for item in CS_CONDITIONS[eid]:
            lines.append(f"  - {item}")

    triggers = spacecore.get(eid, [])
    if triggers:
        lines.append("- **SpaceCore trigger (`triggersCare.json`):**")
        for tr in triggers:
            cond_bits = []
            for part in re.split(r",\s*", tr["condition"]):
                desc = parse_gsq_clause(part.strip()) or parse_precondition_segment(part.strip())
                if desc:
                    cond_bits.append(desc)
            lines.append(f"  - Trigger `{tr['trigger']}` → {', '.join(cond_bits) if cond_bits else tr['condition']}")

    lines.append("")
    return lines


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
                if not in_str and line[i:i+2] == "//":
                    cut = i
                    break
            line = line[:cut]
        out.append(line)
    text = "\n".join(out)
    text = re.sub(r",(\s*\n\s*[}\]])", r"\1", text)
    return text


def parse_all():
    events = {}
    for fn in EVENT_FILES:
        text = strip_comments((CP / fn).read_text(encoding="utf-8"))
        targets = [(m.start(), m.group(1)) for m in TARGET_RE.finditer(text)]
        for idx, (pos, target) in enumerate(targets):
            if not target.startswith("Data/Events/"):
                continue
            end = targets[idx + 1][0] if idx + 1 < len(targets) else len(text)
            chunk = text[pos:end]
            loc = target.replace("Data/Events/", "")
            em = re.search(r'"Entries"\s*:\s*\{', chunk)
            if not em:
                continue
            start = em.end()
            em_end = re.search(r'\n\s{4,8}\}(?:\s*,\s*\n|\s*\n\s*\})', chunk[start:])
            entries = chunk[start:start + em_end.start()] if em_end else chunk[start:]
            for m in EVENT_KEY_RE.finditer(entries):
                key = m.group(1)
                eid = key.split("/")[0]
                if eid in SKIP:
                    continue
                if m.group(3) == "[":
                    continue
                script = m.group(2).replace("\\n", "\n").replace('\\"', '"')
                pre = "/".join(key.split("/")[1:])
                if eid not in events or len(script) > len(events[eid]["script"]):
                    events[eid] = {
                        "id": eid, "location": loc, "file": fn,
                        "preconditions": pre, "full_key": key, "script": script,
                    }
    return events


def clean_text(t: str) -> str:
    t = t.replace("\\", "")
    for code, repl in PORTRAIT.items():
        if repl:
            t = t.replace("*" + code, " " + repl.strip())
        t = t.replace(code, repl)
    t = re.sub(r"\*([^*]+) \*\(", r"*\1* *(", t)
    t = t.replace("@", "фермер")
    t = re.sub(r"\$b#?", "\n\n", t)
    t = t.replace("#", "\n\n")
    t = re.sub(r"\$[0-9a-zA-Z]", "", t)  # leftover portrait codes
    t = re.sub(r"\$k", "", t)
    t = re.sub(r"\*\*\(", "*(", t)
    parts = [re.sub(r"[ \t]+", " ", p).strip() for p in t.split("\n")]
    return "\n".join(p for p in parts if p).strip()


def script_to_scenes(script: str) -> list[tuple[str, str, str]]:
    """Return list of (kind, speaker, text) — kind: narration|dialogue|stage|choice"""
    lines = []
    for part in script.split("/"):
        part = part.strip()
        if not part or part in ("none", "continue", "skippable"):
            continue
        if part.startswith("changeLocation"):
            loc = part.replace("changeLocation", "").strip()
            lines.append(("stage", "", f"*{loc}*"))
            continue
        if part.startswith("globalFade") or part.startswith("fade"):
            continue
        m = re.match(r'message\s+"((?:\\.|[^"\\])*)"', part, re.I)
        if m:
            lines.append(("narration", "", clean_text(m.group(1))))
            continue
        m = re.match(r'speak\s+(\w+)\s+"((?:\\.|[^"\\])*)"', part, re.I)
        if m:
            name = m.group(1)
            if name.lower() == "farmer":
                name = "Фермер"
            elif name.lower() == "harvey":
                name = "Харви"
            lines.append(("dialogue", name, clean_text(m.group(2))))
            continue
        m = re.match(r'end\s+dialogue\s+(\w+)\s+"((?:\\.|[^"\\])*)"', part, re.I)
        if m:
            lines.append(("dialogue", "Харви" if m.group(1).lower() == "harvey" else m.group(1),
                          clean_text(m.group(2)) + " *(конец сцены)*"))
            continue
        if part.startswith("quickQuestion") or part.startswith("question"):
            q = re.sub(r'^quickQuestion\s*', '', part)
            q = re.sub(r'^question\s+\w+\s*', '', q)
            opts = []
            for chunk in q.split("#"):
                chunk = chunk.strip().strip('"')
                if not chunk or chunk == "null":
                    continue
                if re.match(r"fork\d+", chunk, re.I):
                    continue
                opts.append(clean_text(chunk))
            if opts:
                lines.append(("choice", "", " / ".join(opts)))
            continue
        m = re.match(r'fork\d+\s+"([^"]*)"', part, re.I)
        if m:
            opts = [clean_text(o) for o in m.group(1).split("/") if o.strip()]
            if opts:
                lines.append(("choice", "", " / ".join(opts)))
            continue
        if part.startswith("(break)"):
            lines.append(("stage", "", "— *ветвление ответа* —"))
    return lines


def render_event(ev: dict, spacecore: dict[str, list[dict]]) -> str:
    eid = ev["id"]
    title = TITLES.get(eid, eid)
    scenes = script_to_scenes(ev["script"])
    out = [f"### {title}", "", f"*{ev['location']} · `{eid}`*", ""]
    out.extend(format_conditions(ev, spacecore))
    if not scenes:
        out.append("*(Текст сцены в data не извлечён — возможно, укороченный или технический скрипт.)*")
        out.append("")
        return "\n".join(out)

    for kind, speaker, text in scenes:
        if kind == "narration":
            out.append(text)
            out.append("")
        elif kind == "dialogue":
            for para in text.split("\n\n"):
                if para.strip():
                    out.append(f"**{speaker}:** {para.strip()}")
            out.append("")
        elif kind == "stage":
            out.append(text)
            out.append("")
        elif kind == "choice":
            out.append("**Выбор:**")
            for opt in text.split(" / "):
                opt = opt.strip().strip('"')
                if opt:
                    out.append(f"- {opt}")
            out.append("")
    out.append("---")
    out.append("")
    return "\n".join(out)


def main():
    events = parse_all()
    spacecore = parse_spacecore_triggers()
    lines = [
        "# Harvey и фермер: содержание событий",
        "",
        "*Читабельная версия сцен мода HarveyOverhaul. Имена и реплики — из Content Patcher; "
        "технические команды (`warp`, `fork`, `Random`) опущены. Перед каждой сценой — **условия срабатывания** "
        "(CP preconditions, C# InjuryCare, SpaceCore triggers).*",
        "",
        "---",
        "",
        "## Оглавление",
        "",
    ]

    chapter_num = 0
    toc_entries = []
    body = []

    for ch_title, eids in CHAPTERS:
        chapter_num += 1
        lines.append(f"{chapter_num}. [{ch_title}](#{slug(ch_title)})")
        body.append(f"## {ch_title}")
        body.append("")
        body.append(f"<a id=\"{slug(ch_title)}\"></a>")
        body.append("")

        scene_num = 0
        for eid in eids:
            if eid not in events:
                continue
            scene_num += 1
            t = TITLES.get(eid, eid)
            anchor = slug(f"{eid}-{t}")
            lines.append(f"   {chapter_num}.{scene_num} [{t}](#{anchor})")
            toc_entries.append((anchor, t))
            body.append(f"<a id=\"{anchor}\"></a>")
            body.append("")
            body.append(render_event(events[eid], spacecore))

    # Appendix: events not in chapters
    used = {eid for _, eids in CHAPTERS for eid in eids}
    rest = sorted(set(events) - used)
    if rest:
        lines.append(f"{chapter_num + 1}. [Приложение: прочие события](#appendix)")
        body.append("## Приложение: прочие события")
        body.append("")
        body.append("<a id=\"appendix\"></a>")
        body.append("")
        for eid in rest:
            body.append(render_event(events[eid], spacecore))

    lines.append("")
    lines.append("---")
    lines.append("")
    lines.extend(body)

    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {OUT} ({len(events)} events)")


def slug(s: str) -> str:
    s = s.lower()
    s = re.sub(r"[^a-z0-9а-яё]+", "-", s, flags=re.I)
    return s.strip("-")


if __name__ == "__main__":
    main()

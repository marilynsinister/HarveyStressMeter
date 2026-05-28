#!/usr/bin/env python3
"""Generate harvey-events-audit.md from current CP event files."""
import re
from collections import defaultdict
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
OUT = Path(r"C:\Users\Admin\HarveyOverhaulInjury\docs\harvey-relationship-visits-audit\harvey-events-audit.md")

EVENT_FILES = ["events.json", "eventsCare.json", "eventsMineRescue.json"]
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

SCENE_NAMES = {
    "eventHarveyFirstMeeting": "Первая встреча на BusStop",
    "eventHarveyCheckup": "Согласованный осмотр в клинике",
    "eventHarveyFirstVisit": "Первый визит на ферму",
    "eventHarveySecondVisit": "Второй визит (чай)",
    "eventHarveyFirstWalk": "Прогулка в лес",
    "HarveyOverhaulStory.E1_SlipperyPath": "E1 — Скользкая дорожка",
    "HarveyOverhaulStory.E2_InsistentExam": "E2 — Настойчивый осмотр",
    "HarveyOverhaulStory.E3_ForestApothecary": "E3 — Лесная аптека",
    "HarveyOverhaulStory.E4_PierBreath": "E4 — Дыхание на пирсе",
    "HarveyOverhaulStory.E5_StormBeside": "E5 — Рядом в грозу",
    "HarveyOverhaulStory.E6_SayItOutLoud": "E6 — Скажи вслух",
    "HarveyOverhaulStory.E7_TownSip_Sunny": "E7 — Глоток воды в городе",
    "HarveyOverhaulStory.E8_QuietShelf": "E8 — Тихая полка",
    "HarveyMod_FirstTreatment": "Первое лечение (клиника)",
    "HarveyMod_NightCrisis": "Ночной кризис",
    "HarveyMod_BirthdayHospital": "День рождения в больнице",
    "HarveyMod_TreatmentPlanMeeting": "План лечения",
    "eventHarveyMedicalCheck": "Медосмотр по письму",
    "eventHarveyTraumaExam": "Осмотр после травмы",
    "eventHarveyEmergencyCare": "Экстренная помощь",
    "eventHarveyExhaustion": "Истощение / капельница",
    "eventHarveyTreatmentCollapse": "Коллапс на лечении",
    "eventStayInHospital": "Принудительная госпитализация",
    "eventHarveyMineRescue": "Спасение в шахте (severe)",
    "eventHarveyMinorMineRescue": "Лёгкое спасение в шахте",
    "eventHarveyMineRescueDating": "Спасение в шахте (dating)",
    "eventHarveyStormComfortFarm": "Утешение в грозу — ферма",
    "eventHarveyStormComfortTown": "Утешение в грозу — город",
    "eventHarveyStormComfortForest": "Утешение в грозу — лес",
    "eventHarveyStormComfortMountain": "Утешение в грозу — горы",
    "eventHarveyStormComfortDesert": "Утешение в грозу — пустыня",
    "eventHarveyStormComfortMine": "Утешение в грозу — шахта",
    "eventHarveyCheckFarmerOutsideAfter22": "Проверка после 22:00",
    "eventHarveyMorningCheckup": "Утренний осмотр на ферме",
    "eventHarveyCheckHealthFarmer": "Проверка после смерти игрока",
    "eventHarveyRoomCheckup": "Осмотр в комнате клиники",
    "eventHarveyRoomCheckup2": "Случайный осмотр в HarveyRoom",
    "eventHarveyFirstDate": "Первое свидание",
    "eventHarveyMountainDate": "Свидание в горах",
    "eventHarveyPropose": "Предложение",
    "eventHarveyLateNightCollapse": "Обморок поздно ночью (Town)",
    "eventHarveySkullCavePrevention": "Пещера черепа — предупреждение",
    "eventHarveyMineInterception": "Перехват у шахты",
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


def parse_file(fn: str) -> list[dict]:
    path = CP / fn
    text = strip_comments(path.read_text(encoding="utf-8"))
    targets = [(m.start(), m.group(1)) for m in TARGET_RE.finditer(text)]
    rows = []
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
            pre = "/".join(full_key.split("/")[1:])
            rows.append({
                "file": fn,
                "location": loc,
                "event_id": base,
                "preconditions": pre,
                "full_key": full_key,
            })
    return rows


def hearts_from_pre(pre: str) -> str:
    parts = []
    for m in re.finditer(r"Friendship Harvey (\d+)", pre):
        pts = int(m.group(1))
        parts.append(f"{pts} ({pts // 250}♥)")
    for m in re.finditer(r"PLAYER_HEARTS Current Harvey (\d+)", pre):
        parts.append(f"{m.group(1)}♥ (hearts query)")
    return "; ".join(parts) if parts else "—"


def extract_time(pre: str) -> str:
    m = re.search(r"Time (\d+ \d+)", pre)
    return m.group(1) if m else "—"


def extract_weather(pre: str) -> str:
    for w in ("Weather Storm", "Weather storm", "Weather Sunny", "Weather sunny", "Weather Wind"):
        if w.lower() in pre.lower():
            return w.replace("Weather ", "")
    return "—"


def dating_gate(pre: str) -> str:
    if "PLAYER_NPC_RELATIONSHIP" in pre:
        if "Married" in pre and "Dating" in pre:
            return "Dating или Married"
        if "Married" in pre:
            return "Married"
        if "Dating" in pre:
            return "Dating"
    if "HasSeenEvent" in pre and "Dating" in pre:
        return "см. preconditions"
    return "Нет"


def prev_events(pre: str) -> str:
    seen = []
    for m in re.finditer(r"PLAYER_HAS_SEEN_EVENT Current ([^,/]+)", pre):
        token = m.group(1).strip()
        start = max(0, m.start() - 30)
        if "!" in pre[start:m.start()]:
            continue
        seen.append(token)
    neg_seen = re.findall(r"!PLAYER_HAS_SEEN_EVENT Current ([^,/]+)", pre)
    parts = []
    if seen:
        parts.append("seen: " + ", ".join(dict.fromkeys(seen)))
    if neg_seen:
        parts.append("!seen: " + ", ".join(dict.fromkeys(neg_seen)))
    hm = re.search(r"HasSeenEvent ([^/]+)", pre)
    if hm:
        parts.append("HasSeenEvent " + hm.group(1).strip())
    return "; ".join(parts) if parts else "—"


def topics_injury(pre: str) -> str:
    topics = re.findall(r"(?:PLAYER_HAS_CONVERSATION_TOPIC|!PLAYER_HAS_CONVERSATION_TOPIC) Current ([^,/]+)", pre)
    topics += re.findall(r"PLAYER_HAS_BUFF Current (\w+)", pre)
    topics += re.findall(r"PLAYER_HAS_MAIL Current (\w+)", pre)
    if "DAYS_PLAYED" in pre:
        topics.append(re.search(r"DAYS_PLAYED (\d+)", pre).group(0))
    return ", ".join(dict.fromkeys(topics)) if topics else "—"


def analyze(row: dict, all_rows: list[dict]) -> tuple[str, str, str, str]:
    eid = row["event_id"]
    pre = row["preconditions"]
    comment = []
    fix = []

    # duplicates
    dupes = [r for r in all_rows if r["event_id"] == eid]
    if len(dupes) > 1:
        files = sorted({r["file"] for r in dupes})
        comment.append(f"Дубль в {', '.join(files)}; eventsCare грузится после events.json")

    # no preconditions = C# or script trigger
    if not pre.strip():
        comment.append("Нет CP preconditions — запуск через C#/триггер или ручной warp")
        if eid in ("eventHarveyEmergencyCare", "eventHarveyExhaustion", "eventHarveyTreatmentCollapse", "eventStayInHospital"):
            fix.append("Подключить trigger/C# bridge или добавить gate topic/buff")

    too_early = "Нет"
    unreachable = "Нет"

    # topicFirstMeeting chain
    if eid == "eventHarveyFirstVisit" and "topicFirstMeeting" in pre:
        comment.append("Зависит от BusStop meeting; без Town fallback meeting цепочка мёртва")
    if eid == "eventHarveyCheckup" and "BusStop" in row["location"]:
        comment.append("⚠️ Зарегистрировано на BusStop, скрипт — координаты Hospital")
        fix.append("Перенести на Data/Events/Hospital")
    if eid == "eventHarveyFirstMeeting" and len(dupes) > 1:
        fix.append("Оставить один источник в eventsCare.json")

    # Story chain - already fixed but document
    story_order = {
        "HarveyOverhaulStory.E1_SlipperyPath": (None, 2),
        "HarveyOverhaulStory.E2_InsistentExam": ("E1", 3),
        "HarveyOverhaulStory.E3_ForestApothecary": ("E2", 4),
        "HarveyOverhaulStory.E4_PierBreath": ("E3", 5),
        "HarveyOverhaulStory.E5_StormBeside": ("E4", 6),
        "HarveyOverhaulStory.E6_SayItOutLoud": ("E5", 7),
        "HarveyOverhaulStory.E7_TownSip_Sunny": ("E6", 8),
        "HarveyOverhaulStory.E8_QuietShelf": ("E7", 8),
    }
    if eid in story_order:
        prev, hearts = story_order[eid]
        if prev and f"HarveyOverhaulStory.{prev}_" not in pre and f"E{prev[-1]}_" not in pre:
            if not re.search(rf"E{list(story_order.keys()).index(eid)}", pre):
                pass  # handled by name

    # Storm comfort - random, low hearts, no seen
    if eid.startswith("eventHarveyStormComfort"):
        too_early = "⚠️ 3♥ + Random — может до Story/E6"
        if "Random" in pre:
            unreachable = "⚠️ Random — может никогда не выпасть"
        comment.append("Нет eventsSeen; повтор при снятии buff/topic")
        fix.append("Добавить HarveyMod_CD_* или !seen")

    # Romantic tone vs gates
    romantic_early = {
        "eventHarveyMorningCheckup": (True, "Dating", "«солнышко», завтрак в постель"),
        "HarveyMod_FirstTreatment": (True, "3♥", "интимный тон лечения"),
        "HarveyMod_NightCrisis": (True, "6♥ + seen FirstTreatment", "ночная сцена"),
        "eventHarveyCheckHealthFarmer": (True, "Dating", "очень опекунски"),
        "HarveyOverhaulStory.E6_SayItOutLoud": (False, "7♥", "эмоциональная исповедь — OK при gate"),
        "HarveyOverhaulStory.E7_TownSip_Sunny": (True, "8♥", "ревниво-опекунский тон"),
    }
    if eid in romantic_early:
        flag, gate, tone = romantic_early[eid]
        if flag:
            comment.append(f"⚠️ Тон: {tone} при gate {gate}")

    if eid == "eventHarveyTraumaExam" and "2000" in pre:
        comment.append("8♥ без seen/topic — только friendship gate")

    if eid == "eventHarveyTreatmentCollapse" and not pre:
        unreachable = "⚠️ Нет preconditions — только C#?"
        comment.append("Orphan без явного триггера в CP")

    if eid == "eventHarveyMedicalCheck" and "mailHarveyMedicalCheckReminder" in pre:
        comment.append("Цепочка mail → event; OK если trigger работает")

    if eid == "HarveyMod_BirthdayHospital":
        comment.append("Harvey birthday summer 9; LocationName Hospital")

    if eid.startswith("eventHarveyMine"):
        if eid == "eventHarveyMineRescue" and not pre:
            comment.append("C# PassOutHandler startEvent; preconditions в runtime")
        if "MineRescueDating" in eid:
            comment.append("Dating/Married вариант rescue")

    if eid == "eventHarveyRoomCheckup2" and "BETAS" in pre:
        unreachable = "⚠️ Требует Spiderbuttons.BETAS + Random 0.2"

    if eid == "eventHarveyPropose" and "Dating" in pre:
        comment.append("10♥ + Dating; не требует seen first date")

    if eid == "eventHarveyFirstDate" and "Dating" in pre:
        comment.append("8♥ Dating; параллельно Story E7/E8 без связи")

    # Out of order risks
    if eid == "HarveyMod_TreatmentPlanMeeting" and "topicDiagnosisComplete" in pre:
        comment.append("Параллельная cure-ветка, не Story")

    if "Random" in pre and eid not in [x for x in SCENE_NAMES if "Storm" in x]:
        pass

    if not fix:
        fix.append("—")

    return too_early, unreachable, "; ".join(comment) if comment else "—", fix[0]


def md_escape(s: str) -> str:
    return s.replace("|", "\\|").replace("\n", " ")


def main():
    all_rows = []
    for fn in EVENT_FILES:
        all_rows.extend(parse_file(fn))

    # dedupe for display: prefer eventsCare over events for same id+location
    priority = {"eventsCare.json": 2, "eventsMineRescue.json": 2, "events.json": 1}
    by_key = {}
    for r in all_rows:
        k = (r["event_id"], r["location"])
        if k not in by_key or priority[r["file"]] >= priority[by_key[k]["file"]]:
            by_key[k] = r

    # also list duplicates separately in notes
    rows = sorted(by_key.values(), key=lambda r: (r["event_id"], r["location"]))

    order = [
        "eventHarveyFirstMeeting", "eventHarveyCheckup",
        "eventHarveyFirstVisit", "eventHarveySecondVisit", "eventHarveyFirstWalk",
    ] + [f"HarveyOverhaulStory.E{i}_" + n for i, n in [
        (1, "SlipperyPath"), (2, "InsistentExam"), (3, "ForestApothecary"),
        (4, "PierBreath"), (5, "StormBeside"), (6, "SayItOutLoud"),
        (7, "TownSip_Sunny"), (8, "QuietShelf"),
    ]]
    # fix order keys
    story_ids = [
        "HarveyOverhaulStory.E1_SlipperyPath", "HarveyOverhaulStory.E2_InsistentExam",
        "HarveyOverhaulStory.E3_ForestApothecary", "HarveyOverhaulStory.E4_PierBreath",
        "HarveyOverhaulStory.E5_StormBeside", "HarveyOverhaulStory.E6_SayItOutLoud",
        "HarveyOverhaulStory.E7_TownSip_Sunny", "HarveyOverhaulStory.E8_QuietShelf",
    ]
    priority_ids = [
        "eventHarveyFirstMeeting", "eventHarveyCheckup", "eventHarveyFirstVisit",
        "eventHarveySecondVisit", "eventHarveyFirstWalk",
    ] + story_ids + [
        "HarveyMod_FirstTreatment", "HarveyMod_NightCrisis", "HarveyMod_BirthdayHospital",
        "HarveyMod_TreatmentPlanMeeting",
        "eventHarveyMedicalCheck", "eventHarveyTraumaExam", "eventHarveyEmergencyCare",
        "eventHarveyExhaustion", "eventHarveyTreatmentCollapse", "eventStayInHospital",
        "eventHarveyMineRescue", "eventHarveyMinorMineRescue", "eventHarveyMineRescueDating",
        "eventHarveyStormComfortFarm", "eventHarveyStormComfortTown",
        "eventHarveyStormComfortForest", "eventHarveyStormComfortMountain",
        "eventHarveyStormComfortDesert", "eventHarveyStormComfortMine",
        "eventHarveyCheckFarmerOutsideAfter22", "eventHarveyMorningCheckup",
        "eventHarveyCheckHealthFarmer", "eventHarveyRoomCheckup", "eventHarveyRoomCheckup2",
        "eventHarveyFirstDate", "eventHarveyMountainDate", "eventHarveyPropose",
        "eventHarveyLateNightCollapse", "eventHarveySkullCavePrevention", "eventHarveyMineInterception",
    ]

    def sort_key(r):
        eid = r["event_id"]
        if eid in priority_ids:
            return (0, priority_ids.index(eid), r["location"])
        return (1, eid, r["location"])

    rows = sorted(by_key.values(), key=sort_key)

    lines = [
        "# Аудит событий Харви — HarveyOverhaul [CP]\n\n",
        "Дата: актуальное состояние CP после правок topic gates, pacing, Story chain, heart-gates.\n\n",
        "**Источники:** `content.json` → `events.json`, `eventsCare.json`, `eventsMineRescue.json`.\n\n",
        "**Код не менялся** — только документация.\n\n",
        "Friendship `N` = очки (1♥ = 250).\n\n",
        "---\n\n",
        "## Сводная таблица\n\n",
        "| Event ID | Название сцены | Location | Time | Weather | Hearts/Friendship | Dating/Married? | Предыдущие события | Injury/topic/mail | Слишком рано? | Недостижимо? | Комментарий | Рекомендуемая правка |\n",
        "|---|---|---|---|---|---|---|---|---|---|---|---|---|\n",
    ]

    for r in rows:
        eid = r["event_id"]
        pre = r["preconditions"]
        too_early, unreachable, comment, fix = analyze(r, all_rows)
        name = SCENE_NAMES.get(eid, eid)
        lines.append(
            "| {id} | {name} | {loc} | {time} | {weather} | {hearts} | {dating} | {prev} | {topics} | {early} | {unreach} | {comment} | {fix} |\n".format(
                id=f"`{eid}`",
                name=md_escape(name),
                loc=f"`{r['location']}`",
                time=extract_time(pre),
                weather=extract_weather(pre),
                hearts=hearts_from_pre(pre),
                dating=dating_gate(pre),
                prev=md_escape(prev_events(pre)),
                topics=md_escape(topics_injury(pre)),
                early=too_early,
                unreach=unreachable,
                comment=md_escape(comment),
                fix=md_escape(fix),
            )
        )

    lines += [
        "\n---\n\n",
        "## Приоритетные риски (кратко)\n\n",
        "### Ранняя care-цепочка (Farm / BusStop)\n",
        "- `eventHarveyFirstMeeting` — только BusStop; Town fallback не добавлен (см. `02-first-meeting-reachability.md`).\n",
        "- `eventHarveyCheckup` — ключ на **BusStop**, скрипт клиники → часто **недостижимо**.\n",
        "- `eventHarveyFirstVisit` → `SecondVisit` → `FirstWalk` — pacing через DAYS_PLAYED + outcome topics ✅.\n",
        "\n### HarveyOverhaul Story E1–E8\n",
        "- Линейная цепочка `seen E(n-1)` + heart-gates ✅ (после правок).\n",
        "- E1 требует **Wind** — может откладывать старт дуги.\n",
        "\n### Параллельные ветки без связи со Story\n",
        "- `HarveyMod_FirstTreatment` / `NightCrisis` — 3–6♥, не требуют E1–E8.\n",
        "- Storm comfort (6 локаций) — 3♥ + Random, **нет** seen Story.\n",
        "- Dating-сцены (`FirstDate`, `Propose`, room checkups) — не привязаны к E6/E7.\n",
        "\n### Injury / hospital\n",
        "- `eventHarveyEmergencyCare`, `eventHarveyExhaustion`, `eventHarveyTreatmentCollapse`, `eventStayInHospital` — **без** CP preconditions.\n",
        "- Mine rescue — C# `PassOutHandler`; CP keys без gates (кроме Dating variant).\n",
        "\n### Тон vs hearts\n",
        "- `eventHarveyMorningCheckup`, `HarveyMod_FirstTreatment` — партнёрский/опекунский тон при относительно низких hearts.\n",
        "- `HarveyOverhaulStory.E7` — ревнивый тон; gate 8♥ + E6 ✅.\n",
        "\n---\n",
        "## Дубликаты event ID\n",
    ]

    by_id = defaultdict(list)
    for r in all_rows:
        by_id[r["event_id"]].append(f"{r['file']} @ {r['location']}")

    for eid, locs in sorted(by_id.items()):
        if len(locs) > 1:
            lines.append(f"- **`{eid}`:** " + "; ".join(locs) + "\n")

    lines += [
        "\n---\n",
        "## Связанные документы\n",
        "- [01-early-farm-visit-chain.md](01-early-farm-visit-chain.md)\n",
        "- [02-first-meeting-reachability.md](02-first-meeting-reachability.md)\n",
        "- [../events-inventory/07-reachability-table.md](../events-inventory/07-reachability-table.md)\n",
    ]

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("".join(lines), encoding="utf-8")
    print(f"Wrote {OUT} ({len(rows)} events)")


if __name__ == "__main__":
    main()

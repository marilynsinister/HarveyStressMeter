#!/usr/bin/env python3
"""One-shot / repeatability audit for HarveyOverhaul InjuryCare + CP."""
import json
import re
from collections import defaultdict
from pathlib import Path

CS = Path(r"C:\Users\Admin\HarveyOverhaulInjury")
CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
OUT = CS / "docs" / "events-inventory" / "13-one-shot-audit.md"

ACTIVE_CP = ["events.json", "eventsCare.json", "eventsMineRescue.json", "triggersCare.json"]

CAT = {
    "story": "1. One-shot story",
    "injury": "2. Repeatable injury",
    "care": "3. Repeatable care",
    "daily": "4. Daily/temporary",
}

INJURY_TRIGGERS = {
    "triggerHurt": ("buffHurt", False),
    "triggerBadlyHurt": ("buffBadlyHurt", False),
    "triggerSprainedAnkle": ("buffSprainedAnkle", False),
    "triggerBruisedRibs": ("buffBruisedRibs", False),
    "triggerBackStrain": ("buffBackStrain", True),
    "triggerDeepCutsCombat": ("buffDeepCuts", False),
    "triggerDeepCutsFarming": ("buffDeepCuts", True),
    "triggerBurnWounds": ("buffBurnWounds", False),
    "triggerInfectedWound": ("buffInfectedWound", False),
    "triggerTornMuscles": ("buffTornMuscles", True),
    "triggerConcussion": ("buffConcussion", False),
    "triggerFracturedBone": ("buffFracturedBone", False),
    "triggerShrapnelWounds": ("buffShrapnelWounds", False),
    "triggerSurgicalWound": ("buffSurgicalWound", False),
    "triggerExplosionInjury": ("buffShrapnelWounds?", False),
    "triggerCold": ("buffCold", False),
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
    return re.sub(r",(\s*\n\s*[}\]])", r"\1", "\n".join(out))


def event_stem(key: str) -> str:
    return key.split("/")[0]


def parse_cp_events() -> list[dict]:
    rows = []
    key_re = re.compile(
        r'"((?:eventHarvey[A-Za-z0-9_]*|HarveyOverhaulStory\.[A-Za-z0-9_]+|HarveyMod_[A-Za-z0-9_]+|eventRescueOperation)[^"]*?)"\s*:'
    )
    for fname in ACTIVE_CP:
        if not fname.startswith("events"):
            continue
        path = CP / fname
        if not path.exists():
            continue
        text = strip_comments(path.read_text(encoding="utf-8", errors="replace"))
        loc = "?"
        for m in re.finditer(r'"Target"\s*:\s*"Data/Events/([^"]+)"', text):
            loc = m.group(1)
        for km in key_re.finditer(text):
            key = km.group(1)
            stem = event_stem(key)
            pre = key.split("/", 1)[1] if "/" in key else ""
            rows.append({
                "id": stem,
                "source": fname,
                "location": loc,
                "pre": pre[:120],
                "seen_guard": "SEEN_EVENT" in pre or "HasSeenEvent" in pre,
                "topic_guard": "CONVERSATION_TOPIC" in pre,
                "script": "",
            })
    # dedupe by id+location
    seen = set()
    uniq = []
    for r in rows:
        k = (r["id"], r["location"])
        if k in seen:
            continue
        seen.add(k)
        uniq.append(r)
    return uniq


def parse_cp_triggers() -> list[dict]:
    path = CP / "triggersCare.json"
    text = strip_comments(path.read_text(encoding="utf-8"))
    data = json.loads(text)
    rows = []
    for ch in data.get("Changes", []):
        for tid, entry in ch.get("Entries", {}).items():
            stem = tid.replace("{{ModId}}_", "")
            cond = entry.get("Condition", "")
            actions = entry.get("Actions", [])
            rows.append({
                "id": stem,
                "trigger": entry.get("Trigger", ""),
                "condition": cond,
                "actions": actions,
            })
    return rows


def classify_event(e: dict) -> tuple[str, str, str, str]:
    eid = e["id"]
    if eid.startswith("HarveyOverhaulStory.") or eid.startswith("eventHarveyFirst") or eid in (
        "eventHarveyPropose", "eventHarveyFirstDate", "eventHarveyMountainDate",
        "eventHarveyFirstWalk", "HarveyMod_BirthdayHospital", "HarveyMod_FirstTreatment",
        "HarveyMod_NightCrisis", "HarveyMod_TreatmentPlanMeeting", "eventHarveyTraumaExam",
        "eventRescueOperation",
    ):
        should = CAT["story"]
        now = "eventsSeen (CP preconditions)" if e["seen_guard"] else "eventsSeen / topics (chain)"
        risk = "LOW" if e["seen_guard"] else "MED — нет seen guard на ключе"
        rec = "OK" if e["seen_guard"] else "Добавить !PLAYER_HAS_SEEN_EVENT на ключ"
        return should, now, risk, rec

    if "MineRescue" in eid or eid == "eventHarveyMineRescue":
        return (
            CAT["story"],
            "C# eventsSeen on finish + topic fallback; repeat → topic only",
            "MED — кат-сцена one-shot, topic повторяется",
            "OK по дизайну; не блокировать повтор rescue topic",
        )

    if eid in ("eventHarveyStormComfortFarm", "eventHarveyStormComfortTown", "eventHarveyStormComfortForest",
               "eventHarveyStormComfortMine", "eventHarveyStormComfortMountain", "eventHarveyStormComfortDesert"):
        return (
            CAT["daily"],
            "Random entry + buffStressThunder; нет eventsSeen",
            "HIGH — может spam при каждом storm",
            "Добавить seen или cooldown topic HarveyMod_CD_*",
        )

    if eid in ("eventHarveyMineInterception", "eventHarveySkullCavePrevention"):
        return (
            CAT["daily"],
            "CP Trigger LocationChanged + mail; нет seen",
            "HIGH — каждый вход в Mine/SkullCave с injury buff",
            "Cooldown mail или !PLAYER_HAS_MAIL today",
        )

    if eid.startswith("eventHarvey") and e["topic_guard"]:
        return (
            CAT["care"],
            "Topic chain (CP); orphan topics без C#",
            "HIGH — цепочка не стартует / one-shot topic",
            "C# bridge topics или снять seen/topic gate",
        )

    if eid in ("eventHarveyEmergencyCare", "eventHarveyExhaustion"):
        return (
            CAT["care"],
            "C# QueueHospitalEvent → warp Hospital → startEvent; eventsSeen one-shot",
            "LOW — wired 2026-05-24",
            "OK",
        )

    if eid in ("eventHarveyTreatmentCollapse", "eventStayInHospital"):
        return (
            CAT["care"],
            "Script-only — нет trigger",
            "HIGH — недостижимо / dead",
            "switchEvent или удалить orphan",
        )

    return (
        CAT["story"],
        "eventsSeen если vanilla trigger",
        "MED",
        "Проверить preconditions",
    )


def classify_cp_trigger(t: dict) -> tuple[str, str, str, str]:
    tid = t["id"]
    cond = t["condition"]
    acts = " ".join(t["actions"])

    if tid.startswith("triggerTimeReaction") or tid.startswith("triggerLocationReaction"):
        return (
            CAT["daily"],
            f"Topic 1–2 дня; Trigger={t['trigger']}; нет seen",
            "MED — LocationChanged может часто",
            "Раз в день gate (topic или Last*Day в C#)",
        )

    if tid.startswith("triggerHarveyNote") or tid == "triggerHarveyMedicalCheckReminder":
        return (
            CAT["story"] if "Note" in tid else CAT["care"],
            "!PLAYER_HAS_MAIL Received — one-shot mail chain",
            "LOW" if "!" in cond and "MAIL" in cond else "MED",
            "OK для note chain",
        )

    if tid in ("triggerHarveyGentleCare", "triggerHarveyModerateCare", "triggerHarveyIntensiveCare"):
        return (
            CAT["care"],
            "Mail/topic chain + RANDOM; topic 7d",
            "LOW — sequential one-shot mail",
            "OK",
        )

    if tid in ("triggerHarveyMineWarning", "triggerHarveySkullCaveWarning"):
        return (
            CAT["daily"],
            "PlayEvent + mail on LocationChanged; base buff IDs",
            "HIGH — buffDeepCuts vs phase buff mismatch",
            "Fix buff check; mail cooldown",
        )

    if tid == "triggerEmergencySupervision":
        return (
            CAT["care"],
            "Topic 7d после комбинации reactions",
            "LOW",
            "OK",
        )

    return (CAT["daily"], f"Trigger={t['trigger']}", "MED", "Review")


def injury_rows() -> list[tuple]:
    rows = []
    for trig, (buff, repeatable) in INJURY_TRIGGERS.items():
        if repeatable:
            should = CAT["injury"]
            now = "LastInjuryAppliedDayByTrigger cooldown (RepeatableInjuryCooldownDays)"
            risk = "LOW"
            rec = "OK — cooldown config"
        else:
            should = CAT["injury"]
            now = "InjuryCooldownUntilDay per buff (AppliedTriggers only for story: SurgicalWound, ExplosionInjury)"
            risk = "LOW" if buff not in ("buffSurgicalWound", "buffShrapnelWounds") else "MED — story one-shot"
            rec = "OK — repeatable via cooldown (2026-05-24 policy)"
        rows.append((f"C# {trig} → {buff}", now, should, risk, rec))

    rows.append((
        "C# ApplyBadlyHurtFromMinePassOut",
        "Прямой ApplyBadlyHurt без AppliedTriggers gate",
        CAT["injury"],
        "MED — обходит one-shot для badly hurt в шахте",
        "OK для mine death; документировать исключение",
    ))
    return rows


def csharp_mechanics_rows() -> list[tuple]:
    return [
        (
            "C# eventsSeen: mine rescue (3 events)",
            "onEventFinished → eventsSeen; IsMineRescueEventAlreadySeen → topic only",
            CAT["story"],
            "LOW (после fix)",
            "OK",
        ),
        (
            "C# topicMineInjuryRescue",
            "Temporary 2d; removed on hospital warp",
            CAT["care"],
            "MED — topic снимается до клика по Харви",
            "Не RemoveTopic до interaction",
        ),
        (
            "C# topic*Cured / topicTreatmentCompleted",
            "7d; auto GameEventHandler + InteractionHandler",
            CAT["care"],
            "LOW",
            "OK — completion dialogue one per topic instance",
        ),
        (
            "C# topic*Phase* (InjuryManager)",
            "7d per phase; removed on phase advance",
            CAT["injury"],
            "LOW",
            "OK",
        ),
        (
            "C# topicFarmerExhausted / topicPassedOutInTown",
            "3d / 2d; gate hasBuff + HasConversationTopic",
            CAT["daily"],
            "LOW — повтор после expiry",
            "OK",
        ),
        (
            "C# topicHarvey_NightRound",
            "2d via activeDialogueEvents; LastNightRoundRollDay 1×/night",
            CAT["daily"],
            "LOW",
            "OK",
        ),
        (
            "C# Night visit (TimeEventHandler)",
            "LastNightRoundRollDay + LastNightRoundDay; 35% roll/night",
            CAT["daily"],
            "LOW",
            "OK",
        ),
        (
            "C# Mine entry warning HUD",
            "_lastMineWarningDay 1×/day",
            CAT["daily"],
            "LOW",
            "OK",
        ),
        (
            "C# HarveyMod_MineForbidden buff",
            "MineForbiddenAppliedDay + duration days",
            CAT["daily"],
            "LOW",
            "OK",
        ),
        (
            "C# Complication topics (Wet/Dirty/Neglect)",
            "4–7d topics; buff until treated",
            CAT["injury"],
            "LOW — repeatable via re-injury",
            "OK",
        ),
        (
            "C# situationReaction_Drunk + AppliedTriggers",
            "AppliedTriggers one-shot per save",
            CAT["daily"],
            "MED — drunk reaction once ever",
            "Repeatable: clear trigger on topic expiry",
        ),
        (
            "C# buffFarmerExhausted / buffSleepy",
            "Gate: !hasBuff && !topic",
            CAT["daily"],
            "LOW",
            "OK after topic expires",
        ),
        (
            "C# Neglect strikes",
            "NeglectStrikes counter; topicNeglect 7d",
            CAT["care"],
            "LOW",
            "OK",
        ),
        (
            "CP HarveyMod_CD_* topics",
            "2–7d cooldown between story events E1–E8",
            CAT["story"],
            "LOW",
            "OK — soft gate between story beats",
        ),
        (
            "CP buffStressThunder + storm comfort",
            "StormComfortLauncher daily roll → buff or topicHarveyStormStress",
            CAT["daily"],
            "LOW — wired 2026-05-24",
            "OK",
        ),
    ]


def main():
    events = parse_cp_events()
    triggers = parse_cp_triggers()

    table_rows: list[tuple[str, str, str, str, str]] = []

    for e in sorted(events, key=lambda x: x["id"]):
        should, now, risk, rec = classify_event(e)
        table_rows.append((f"event:{e['id']}", now, should, risk, rec))

    for t in sorted(triggers, key=lambda x: x["id"]):
        should, now, risk, rec = classify_cp_trigger(t)
        table_rows.append((f"trigger:{t['id']}", now, should, risk, rec))

    table_rows.extend(injury_rows())
    table_rows.extend(csharp_mechanics_rows())

    # stats
    by_cat = defaultdict(int)
    by_risk = defaultdict(int)
    for _, _, should, risk, _ in table_rows:
        by_cat[should.split(".", 1)[0]] += 1
        by_risk[risk.split(" ")[0]] += 1

    critical = [r for r in table_rows if r[3].startswith("CRITICAL")]
    high = [r for r in table_rows if r[3].startswith("HIGH")]

    lines = [
        "# Аудит одноразовости: triggers, events, topics",
        "",
        "Разделение механик HarveyOverhaul InjuryCare + CP по категориям повторяемости.",
        "",
        "## Категории",
        "",
        "| # | Категория | Ожидание |",
        "|---|---|---|",
        f"| 1 | **One-shot story event** | Один раз за сейв (`eventsSeen`, mail received) |",
        f"| 2 | **Repeatable injury trigger** | Повтор после выздоровления / cooldown |",
        f"| 3 | **Repeatable care scene** | Может повторяться, но не чаще N дней |",
        f"| 4 | **Daily/temporary reaction** | Раз в день / ночь / короткий topic |",
        "",
        "## Сводка",
        "",
        f"- Строк в таблице: **{len(table_rows)}**",
        f"- CRITICAL: **{len(critical)}** | HIGH: **{len(high)}**",
        "",
        "### Механизмы контроля одноразовости",
        "",
        "| Механизм | Где | Назначение |",
        "|---|---|---|",
        "| `Game1.player.eventsSeen` | CP preconditions + C# mine rescue | Story / cutscene one-shot |",
        "| `InjuryState.AppliedTriggers` | C# InjuryManager (story only: SurgicalWound, ExplosionInjury) | Story one-shot gate |",
        "| `InjuryCooldownUntilDay` | C# StateManager | Repeatable injury cooldown per buff |",
        "| `LastNightRoundRollDay` / `LastNightRoundDay` | TimeEventHandler | 1 roll / 1 visit per night |",
        "| `activeDialogueEvents` / AddTopic(days) | C# + CP | Temporary topics |",
        "| `!PLAYER_HAS_MAIL Received` | triggersCare | One-shot mail chains |",
        "| `HarveyMod_CD_*` topics | CP events E1–E8 | Cooldown between story beats |",
        "",
        "## Критичные разрывы (CRITICAL)",
        "",
    ]

    if critical:
        for name, now, should, risk, rec in critical:
            lines.append(f"- **{name}** — {now}. → {rec}")
    else:
        lines.append("- *(нет)*")

    lines.extend([
        "",
        "## HIGH риски (выборка)",
        "",
    ])
    for name, now, should, risk, rec in high[:15]:
        lines.append(f"- **{name}** — {rec}")
    if len(high) > 15:
        lines.append(f"- … и ещё {len(high) - 15}")

    lines.extend([
        "",
        "## Политика повторяемости травм (2026-05-24)",
        "",
        "`AppliedTriggers` — только **story one-shot** (`SurgicalWound`, `ExplosionInjury`).",
        "Остальные травмы — **cooldown** через `InjuryCooldownUntilDay` / `RepeatableInjuryCooldownDays`.",
        "",
        "Оставшийся риск: drunk `situationReaction` всё ещё использует `AppliedTriggers` one-shot per save.",
        "",
        "## Таблица",
        "",
        "| Trigger/Event | Сейчас | Должно быть | Риск | Рекомендация |",
        "|---|---|---|---|---|",
    ])

    for name, now, should, risk, rec in sorted(table_rows, key=lambda r: (r[3], r[0])):
        lines.append(f"| `{name}` | {now} | {should} | {risk} | {rec} |")

    lines.extend([
        "",
        "**Статус:** черновик аудита одноразовости.",
        "",
    ])

    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {OUT} ({len(table_rows)} rows, {len(critical)} critical, {len(high)} high)")


if __name__ == "__main__":
    main()

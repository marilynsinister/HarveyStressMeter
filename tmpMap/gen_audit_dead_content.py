#!/usr/bin/env python3
"""Generate docs/audit-dead-content.md — CP topics/mail with no callers."""
import json
import re
from collections import defaultdict
from pathlib import Path

CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
CS = Path(r"C:\Users\Admin\HarveyOverhaulInjury")
OUT = CS / "docs" / "audit-dead-content.md"

CALLER_FILES = [
    "events.json",
    "eventsCare.json",
    "eventsMineRescue.json",
    "triggersCare.json",
    "triggersDate.json",
    "triggersCure.json",
    "triggersInjury.json",
    "triggersStress.json",
    "triggersQuestsStress.json",
    "triggersCureStress.json",
]

DIALOGUE_FILES = [
    "dialoguesHarvey.json",
    "dialoguesHarveyCare.json",
    "dialoguesHarveyCure.json",
    "dialoguesHarveyCureStress.json",
    "dialoguesHarveyInjury.json",
    "dialoguesHarveyPregnant.json",
    "dialoguesHarveyStress.json",
    "dialoguesNpc.json",
]

MAIL_FILES = ["mail.json", "mailCare.json", "mailCure.json", "mailInjury.json", "mailStress.json"]

# --- Collect CP topic keys (dialogue) ---
topic_defined: dict[str, set[str]] = defaultdict(set)
for fn in DIALOGUE_FILES:
    p = CP / fn
    if not p.exists():
        continue
    text = p.read_text(encoding="utf-8")
    for m in re.finditer(r'"((?:topic[A-Za-z0-9_]+))"\s*:', text):
        topic_defined[m.group(1)].add(fn)

# --- Collect CP mail keys ---
mail_defined: dict[str, set[str]] = defaultdict(set)
for fn in MAIL_FILES:
    p = CP / fn
    if not p.exists():
        continue
    text = p.read_text(encoding="utf-8")
    for m in re.finditer(r'"((?:mail[A-Za-z0-9_]+|HarveyMod_[A-Za-z0-9_]+))"\s*:', text):
        mail_defined[m.group(1)].add(fn)

# --- Collect all callers from CP ---
cp_caller_text = ""
for fn in CALLER_FILES:
    p = CP / fn
    if p.exists():
        cp_caller_text += p.read_text(encoding="utf-8") + "\n"
for fn in DIALOGUE_FILES:
    p = CP / fn
    if p.exists():
        cp_caller_text += p.read_text(encoding="utf-8") + "\n"

topic_callers: dict[str, set[str]] = defaultdict(set)
mail_callers: dict[str, set[str]] = defaultdict(set)

for m in re.finditer(
    r"(?:addConversationTopic|AddConversationTopic|removeConversationTopic|RemoveConversationTopic)\s+([A-Za-z0-9_]+)",
    cp_caller_text,
    re.I,
):
    topic_callers[m.group(1)].add("CP events/triggers")
for m in re.finditer(r"PLAYER_HAS_CONVERSATION_TOPIC\s+Current\s+([A-Za-z0-9_]+)", cp_caller_text):
    topic_callers[m.group(1)].add("CP PLAYER_HAS_CONVERSATION_TOPIC")
for m in re.finditer(r"!PLAYER_HAS_CONVERSATION_TOPIC\s+Current\s+([A-Za-z0-9_]+)", cp_caller_text):
    topic_callers[m.group(1)].add("CP !PLAYER_HAS_CONVERSATION_TOPIC")
for m in re.finditer(r"#\$t\s+(topic[A-Za-z0-9_]+)", cp_caller_text):
    topic_callers[m.group(1)].add("CP dialogue #$t transition")

for m in re.finditer(r"(?:addMail|AddMail)\s+(?:Current\s+)?([A-Za-z0-9_]+)", cp_caller_text, re.I):
    mail_callers[m.group(1)].add("CP events/triggers")
for m in re.finditer(r"PLAYER_HAS_MAIL\s+Current\s+([A-Za-z0-9_]+)", cp_caller_text):
    mail_callers[m.group(1)].add("CP PLAYER_HAS_MAIL")

# --- C# callers ---
cs_text = "\n".join(p.read_text(encoding="utf-8", errors="ignore") for p in CS.rglob("*.cs"))
const_topics = set(re.findall(r'public const string \w+ = "(topic[^"]+)"', cs_text))
const_mails = set(re.findall(r'public const string \w+ = "(mail[^"]+|HarveyMod_[^"]+)"', cs_text))

for tid in set(re.findall(r'AddTopic\s*\(\s*"([^"]+)"', cs_text)) | const_topics:
    topic_callers[tid].add("C# AddTopic")
for tid in set(re.findall(r'RemoveTopic\s*\(\s*"([^"]+)"', cs_text)):
    topic_callers[tid].add("C# RemoveTopic")
for tid in set(re.findall(r'HasConversationTopic\s*\(\s*"([^"]+)"', cs_text)) | set(
    re.findall(r'HasTopic\s*\(\s*"([^"]+)"', cs_text)
):
    topic_callers[tid].add("C# HasTopic")
for tid in set(re.findall(r'activeDialogueEvents\.TryAdd\s*\(\s*"([^"]+)"', cs_text)):
    topic_callers[tid].add("C# TryAdd")
for tid in set(re.findall(r'TopicId\s*=\s*"(topic[^"]+)"', cs_text)):
    topic_callers[tid].add("C# KnownTraumas/Complications")

for mid in set(re.findall(r'addMailForTomorrow\s*\(\s*"([^"]+)"', cs_text)) | const_mails:
    mail_callers[mid].add("C# addMailForTomorrow")

# Dynamic C# patterns (not exact IDs)
CS_DYNAMIC_TOPIC = [
    ("topicTreatment*", "TreatmentManager.StartPhasedTreatment (Replace buff→topicTreatment)"),
    ("topic*PhaseAcute/Healing/Recovery", "InjuryManager.GetPhaseTopicId + InteractionHandler"),
    ("topic*Cured", "InteractionHandler.CompleteRecovery / GameEventHandler"),
    ("topicHealthDamageSevere/Critical", "InjuryManager при тяжёлых травмах"),
]

NOT_TOPIC_KEYS = {
    "Treat_", "PhaseTransition_", "Proximity_", "Treatment_Phase_", "RemoveStitches_",
    "Introduction", "Accept", "Reject", "Resort_", "Hospital_", "dating_Harvey", "married_Harvey",
    "eventSeen_", "eventHarvey", "FlowerDance_", "GreenRain_", "WinterStar_",
}

STRESS_INCLUDED = (CP.parent.parent / "content.json").read_text(encoding="utf-8")
STRESS_FILE_LOADED = "dialoguesHarveyStress.json" in STRESS_INCLUDED and "//" not in STRESS_INCLUDED.split("dialoguesHarveyStress.json")[0][-20:]
# simpler check
stress_loaded = bool(re.search(r'"FromFile":\s*"assets/Code/dialoguesHarveyStress\.json"', STRESS_INCLUDED) and not re.search(
    r'//\s*\{[^}]*dialoguesHarveyStress\.json', STRESS_INCLUDED, re.S
))


def files_str(s: set[str]) -> str:
    return ", ".join(sorted(s))


def topic_called(tid: str) -> tuple[bool, str]:
    if any(tid.startswith(p) for p in NOT_TOPIC_KEYS):
        return True, "dialogue key (не conversation topic)"

    callers = topic_callers.get(tid, set())
    if callers:
        return True, "; ".join(sorted(callers))

    if tid.endswith("Cured") and "completionTopic" in cs_text:
        return True, "C# dynamic topic*Cured"
    if tid.startswith("topicTreatment") and "topicTreatment" in cs_text:
        return True, "C# dynamic topicTreatment*"
    for phase in ("PhaseAcute", "PhaseHealing", "PhaseRecovery"):
        if tid.endswith(phase):
            return True, "C# GetPhaseTopicId"

    if "_memory_" in tid:
        base = re.sub(r"_memory_(oneday|oneweek|twoweeks|fourweeks)$", "", tid)
        if base in topic_callers or base in topic_defined:
            return False, "SDV memory key — базовый topic есть, memory topic никто не ставит"

    return False, ""


def mail_called(mid: str) -> tuple[bool, str]:
    callers = mail_callers.get(mid, set())
    if callers:
        return True, "; ".join(sorted(callers))
    if mid in cs_text:
        return True, "C# string reference (не addMailForTomorrow)"
    return False, ""


def classify_topic(tid: str, called: bool, where: set[str]) -> tuple[str, str]:
    """Returns (dead_or_future, action)"""
    fn = files_str(where)

    if called:
        return "—", "—"

    if "dialoguesHarveyStress.json" in where and not stress_loaded:
        return "мёртвый (файл не в Include)", "подключить dialoguesHarveyStress.json или удалить"

    if "Phase1Ready" in tid or "Phase2Ready" in tid or "RecoveryReady" in tid or "PhaseCast" in tid or "PhaseSurgery" in tid or "PhaseRehab" in tid or "PhaseObservation" in tid or "PhaseTreatment" in tid:
        return "legacy (старая фазовая cure-система CP)", "удалить или заменить на topic*PhaseAcute/Healing/Recovery"

    if tid == "topicSurgicalWoundHealed":
        return "legacy (C# ставит topicSurgicalWoundCured)", "переименовать в topicSurgicalWoundCured"

    if tid.startswith("topicHarvey") and "_memory_" in tid:
        return "задел (memory после снятия topic)", "подключить memory-триггер при RemoveTopic или удалить"

    if tid == "topicHarvey_EscalatedCare":
        return "задел (нет AddTopic)", "подключить в triggersCare или удалить"

    if tid in ("topicBoyfriendWorries", "topicProtectiveBoyfriend", "topicHusbandlyProtection", "topicWifelyWorries", "topicPreventiveCare", "topicHealthCheckup", "topicStartTreatment"):
        return "задел (relationship cure narrative)", "подключить триггер/событие или удалить"

    if tid in ("topicEatSomething", "topicSpeakToHarvey", "topicSpeakToSomebody", "topicHealthDamage", "topicForestRescue", "topicHarveyExhaustion"):
        return "legacy injury hooks", "подключить триггер или удалить"

    if "dialoguesNpc.json" in where:
        return "NPC-only topic (Harvey не ставит)", "NPC-триггер или удалить dialogue key"

    if tid in ("topicHarveyFirstVisitAgree", "topicHarveyFirstVisitNeutral", "topicHarveyFirstVisitRefused"):
        return "мёртвый? — проверить eventsCare", "подключить"  # should be called - bug in script?

    return "мёртвый / не подключён", "удалить или подключить вызов"


def classify_mail(mid: str, called: bool, where: set[str]) -> tuple[str, str]:
    if called:
        return "—", "—"

    if mid.startswith("mailHarveyStress"):
        if "mailStress.json" in where:
            return "задел (stress-триггеры закомментированы)", "подключить triggersStress или удалить"
        return "legacy", "удалить"

    if mid in ("HarveyMod_WetCare", "HarveyMod_WetStitchesCare", "mailHarvey_WetCare", "mailHarvey_WetStitchesCare"):
        return "задел (MailIds не используются в C#)", "подключить в ComplicationManager или удалить"

    if mid.startswith("HarveyMod_") and "mail.json" in where:
        narrative = {
            "HarveyMod_PsychologicalSupport", "HarveyMod_MonthlyCheckup", "HarveyMod_DangerWarning",
            "HarveyMod_DoctorWorries", "HarveyMod_WeatherWarning", "HarveyMod_WinterHealthTips",
            "HarveyMod_AnniversaryReflection", "HarveyMod_FamilySupport", "HarveyMod_MedicalRecognition",
            "HarveyMod_HealthyHolidays", "HarveyMod_FuturePlans", "HarveyMod_LoveConfession",
            "HarveyMod_MovingInNotice", "HarveyMod_TraumaAnxietyNotice", "HarveyMod_RecoveryReliefLetter",
            "HarveyMod_OverprotectiveNotice", "HarveyMod_ComfortLetter", "HarveyMod_ProtectionOffer",
            "HarveyMod_AlcoholWarning", "HarveyMod_ProfessionalSuccess", "HarveyMod_TreatmentPlanReady",
            "HarveyMod_TreatmentSeriesComplete", "HarveyMod_PerfectPatientAward", "HarveyMod_RelapseNotice",
            "HarveyMod_ViolationWarning", "HarveyMod_EscalationNotice", "HarveyMod_ExtendedTreatmentNotice",
            "HarveyMod_CriticalCareUnlocked", "HarveyMod_AdvancedTreatmentUnlocked", "HarveyMod_EmergencyHospitalization",
            "HarveyMod_PanicAttackComplete", "HarveyMod_FatigueTreatmentComplete", "HarveyMod_SleepTherapyComplete",
            "HarveyMod_DiagnosisComplete",
        }
        if mid in narrative:
            return "задел (narrative mail без триггера)", "подключить триггер/событие или удалить"

    if "mailCure.json" in where and "_Phase" in mid:
        return "legacy (старая phased cure почта)", "удалить или привязать к C# phase transition"

    if "mailInjury.json" in where and mid.endswith("Alert"):
        return "задел (injury alert mail)", "подключить C# OnInjuryApplied или удалить"

    if mid.startswith("mailHarvey") and "mailCare.json" in where:
        recovery = {"mailHarveyRecovery1", "mailHarveyRecovery2", "mailHarveyRecovery3", "mailHarveyRecoveryFinal",
                    "mailHarveyRecoveryFinalDating", "mailHarveyRecoveryFinal_Friendship", "mailHarveyPostTrauma",
                    "mailHarveyRestRequired", "mailHarveyStep1", "mailHarveyStep2", "mailHarveyStep3",
                    "mailHarveyStep1Dating", "mailHarveyStep2Dating", "mailHarveyStep3Dating", "mailHarveyNoteGirlfriend"}
        if mid in recovery:
            return "задел (care recovery chain)", "подключить triggersCare chain или удалить"

    return "мёртвый / не подключён", "удалить или подключить вызов"


def expected_topic(tid: str) -> str:
    mapping = {
        "topicBackStrainPhase1Ready": "CP phase transition (старый cure flow)",
        "topicHarveyGentleCare": "triggersCare.json → AddConversationTopic",
        "topicAfterCheckup": "eventsCare.json pregnancy checkup",
        "topicHarveyFirstVisitAgree": "eventsCare.json eventHarveyFirstVisit",
        "topicStressTired": "C# stress system / triggersStress (отключён)",
        "topicHarvey_EscalatedCare_memory_oneday": "SDV memory после topicHarvey_EscalatedCare",
        "topicSurgicalWoundHealed": "C# CompleteRecovery → topicSurgicalWoundCured",
    }
    if tid in mapping:
        return mapping[tid]
    if "Phase1Ready" in tid:
        return "Старый CP cure: смена фазы лечения"
    if tid.startswith("topicStress"):
        return "Stress-триггеры (triggersStress.json закомментирован)"
    if "_memory_" in tid:
        base = re.sub(r"_memory_.*$", "", tid)
        return f"Memory-реплика после снятия {base}"
    if "dialoguesNpc.json" in topic_defined.get(tid, set()):
        return "NPC reaction topic (не Harvey C#)"
    return "—"


def expected_mail(mid: str) -> str:
    if mid == "HarveyMod_WetCare":
        return "ComplicationManager при WetBandage (MailIds.WetCare — не вызывается)"
    if mid == "mailHarveyGentleCare":
        return "triggersCare (topicHarveyGentleCare chain) — но это mail, не topic"
    if mid.startswith("mailHarveyRecovery"):
        return "Care recovery chain после травмы"
    if mid.endswith("Alert"):
        return "C# InjuryManager при Apply* injury"
    return "—"


# Re-check topics that script missed from eventsCare
for extra in re.findall(
    r"(?:addConversationTopic|AddConversationTopic|action addConversationTopic)\s+(topic[A-Za-z0-9_]+)",
    (CP / "eventsCare.json").read_text(encoding="utf-8"),
    re.I,
):
    topic_callers[extra].add("CP eventsCare.json")

# Build rows
topic_rows = []
for tid in sorted(topic_defined.keys()):
    if any(tid.startswith(p) for p in NOT_TOPIC_KEYS):
        continue
    called, caller_detail = topic_called(tid)
    status, action = classify_topic(tid, called, topic_defined[tid])
    topic_rows.append({
        "id": tid,
        "type": "topic",
        "defined": files_str(topic_defined[tid]),
        "expected": expected_topic(tid),
        "called": "да" if called else "нет",
        "caller": caller_detail if called else "—",
        "status": status if not called else "активен",
        "action": action if not called else "—",
    })

mail_rows = []
for mid in sorted(mail_defined.keys()):
    called, caller_detail = mail_called(mid)
    status, action = classify_mail(mid, called, mail_defined[mid])
    mail_rows.append({
        "id": mid,
        "type": "mail",
        "defined": files_str(mail_defined[mid]),
        "expected": expected_mail(mid),
        "called": "да" if called else "нет",
        "caller": caller_detail if called else "—",
        "status": status if not called else "активен",
        "action": action if not called else "—",
    })

dead_topics = [r for r in topic_rows if r["called"] == "нет"]
dead_mails = [r for r in mail_rows if r["called"] == "нет"]
active_topics = [r for r in topic_rows if r["called"] == "да"]
active_mails = [r for r in mail_rows if r["called"] == "да"]

# Group dead topics
def topic_group(r):
    tid, defined = r["id"], r["defined"]
    if any(x in tid for x in ("Phase1Ready", "Phase2Ready", "RecoveryReady", "PhaseCast", "PhaseSurgery", "PhaseRehab", "PhaseObservation", "PhaseTreatment")):
        return "A"
    if "dialoguesHarveyStress.json" in defined:
        return "B"
    if "_memory_" in tid:
        return "C"
    if "dialoguesNpc.json" in defined and "dialoguesHarveyStress.json" not in defined:
        return "D"
    if tid in {"topicBoyfriendWorries", "topicProtectiveBoyfriend", "topicHusbandlyProtection", "topicWifelyWorries", "topicPreventiveCare", "topicHealthCheckup", "topicStartTreatment", "topicHarvey_EscalatedCare"}:
        return "E"
    return "F"

GROUP_LABELS = {
    "A": "A. Legacy phase-ready (dialoguesHarveyCure.json)",
    "B": "B. Stress (dialoguesHarveyStress.json — не в Include)",
    "C": "C. Memory topics (после снятия complication)",
    "D": "D. NPC-only topics",
    "E": "E. Cure narrative / relationship",
    "F": "F. Injury hooks / прочее",
}
groups = {GROUP_LABELS[k]: [] for k in GROUP_LABELS}
for r in dead_topics:
    groups[GROUP_LABELS[topic_group(r)]].append(r)

mail_groups = {
    "1. Narrative mail (mail.json)": [r for r in dead_mails if "mail.json" in r["defined"] and r["id"].startswith("HarveyMod_")],
    "2. Care recovery chain (mailCare.json)": [r for r in dead_mails if "mailCare.json" in r["defined"]],
    "3. Injury alert mail (mailInjury.json)": [r for r in dead_mails if "mailInjury.json" in r["defined"]],
    "4. Phased cure mail (mailCure.json)": [r for r in dead_mails if "mailCure.json" in r["defined"]],
    "5. Stress mail (mailStress.json)": [r for r in dead_mails if "mailStress.json" in r["defined"]],
}


def md_table(rows: list[dict], limit: int | None = None) -> str:
    if limit:
        rows = rows[:limit]
    lines = [
        "| ID | Тип | Где определён | Где должен вызываться | Найден вызов | Мёртвый/задел | Что сделать |",
        "|----|-----|---------------|----------------------|--------------|---------------|-------------|",
    ]
    for r in rows:
        lines.append(
            f"| `{r['id']}` | {r['type']} | {r['defined']} | {r['expected']} | **{r['called']}** | {r['status']} | {r['action']} |"
        )
    return "\n".join(lines)


lines = [
    "# Обратный аудит: мёртвый CP-контент (topics & mail)",
    "",
    "Дата: **2026-05-24** (актуализация; таблицы пересчитаны скриптом `tmpMap/gen_audit_dead_content.py`)  ",
    "Направление: **CP → вызовы** (противоположность audit-topics-cp-existence / audit-mail-cp-existence).",
    "",
    "## История обработки",
    "",
    "| Дата | Действие |",
    "|------|----------|",
    "| 2026-05-23 | CP JSON cleanup: ~36 legacy topic keys, 38 mail keys удалены; 3 topic подключены |",
    "| 2026-05-23 | C# sync: `TopicIds`, `MailIds`, completion list, CP dialogue/mail gaps |",
    "| 2026-05-24 | Пересчёт: legacy `*Phase*Ready` удалены → **0** в группе A; dead topics **64**, dead mail **79** |",
    "",
    "## Метод",
    "",
    "1. Собраны ключи `topic*` и `mail*` / `HarveyMod_*` из CP dialogue и mail JSON.",
    "2. Искали вызовы в:",
    "   - C#: `AddTopic`, `RemoveTopic`, `HasTopic`, `TryAdd`, `addMailForTomorrow`, `KnownTraumas`",
    "   - CP: `addConversationTopic`, `AddConversationTopic`, `addMail`/`AddMail`, `PLAYER_HAS_CONVERSATION_TOPIC`, `PLAYER_HAS_MAIL`, `#$t topic`",
    "   - Файлы: `events.json`, `eventsCare.json`, `eventsMineRescue.json`, `triggersCare.json` (+ закомментированные triggers*)",
    "3. Динамические C# ID (`topicTreatment*`, `topic*Phase*`, `topic*Cured`) считаются **вызываемыми**, даже если exact key отсутствует в grep CP.",
    "",
    "## Сводка",
    "",
    f"| | Topics | Mail |",
    f"|--|--------|------|",
    f"| Всего ключей в CP | {len(topic_rows)} | {len(mail_rows)} |",
    f"| С найденным вызовом | {len(active_topics)} | {len(active_mails)} |",
    f"| **Без вызова (мёртвые кандидаты)** | **{len(dead_topics)}** | **{len(dead_mails)}** |",
    "",
    "**Главные причины мёртвого контента:**",
    "",
    "1. **Legacy phased cure** — ~40 `topic*Phase*Ready` и phase-mail из старой CP-системы; C# использует `GetPhaseTopicId` → `PhaseAcute/Healing/Recovery`.",
    "2. **`dialoguesHarveyStress.json` закомментирован** в `content.json` — весь stress-topic блок не грузится.",
    "3. **Закомментированы triggers** (`triggersCure`, `triggersInjury`, `triggersStress`) — care/recovery/stress mail не отправляются.",
    "4. **Memory topics** (`*_memory_oneday/oneweek`) — SDV memory keys; C#/CP не ставят их при снятии complication topic.",
    "5. **Narrative mail в `mail.json`** — ~30 писем без триггеров (задел на будущее).",
    "",
    "---",
    "",
    "## Приоритет 1 — критичные несовпадения (актуально)",
    "",
    "| ID | Проблема | Статус |",
    "|----|----------|--------|",
    "| `topicSurgicalWoundHealed` | C# → `topicSurgicalWoundCured` | **✅ done** — legacy Healed удалён |",
    "| `HarveyMod_WetCare`, `HarveyMod_WetStitchesCare` | `MailIds` есть, send не wired | **⚠️ MEDIUM** — задел в CP |",
    "| `mailHarvey_Neglect` vs `HarveyMod_NeglectWarning` | Дубли ID | **✅ C#** шлёт `NeglectWarning`; `mailHarvey_Neglect` — dead CP |",
    "| Memory topics (16 keys) | Memory не ставятся | **kept intentionally** |",
    "| `topicHarveyMinorMineRescue` | C# add, dialogue нет | **⚠️ MEDIUM** — добавить CP key |",
    "",
    "---",
    "",
]

for gname, grows in groups.items():
    if not grows:
        continue
    lines += [f"## {gname} ({len(grows)})", "", md_table(grows), ""]

for gname, grows in mail_groups.items():
    if not grows:
        continue
    lines += [f"## Mail: {gname} ({len(grows)})", "", md_table(grows), ""]

lines += [
    "---",
    "",
    "## Полная таблица: topics без вызова",
    "",
    md_table(dead_topics),
    "",
    "---",
    "",
    "## Полная таблица: mail без вызова",
    "",
    md_table(dead_mails),
    "",
    "---",
    "",
    "## Активные topics (для справки, вызов найден)",
    "",
    f"Всего {len(active_topics)}. Примеры:",
    "",
]
for r in active_topics[:25]:
    lines.append(f"- `{r['id']}` ← {r['caller']}")
lines += ["", f"*(… и ещё {max(0, len(active_topics)-25)})*", ""]

lines += [
    "## Активные mail (для справки)",
    "",
]
for r in active_mails:
    lines.append(f"- `{r['id']}` ← {r['caller']}")

lines += [
    "",
    "## Закомментированная инфраструктура (content.json)",
    "",
    "| Include | Статус | Влияние |",
    "|---------|--------|---------|",
    "| `dialoguesHarveyStress.json` | **закомментирован** | ~30 stress topics мёртвы |",
    "| `triggersCure.json` | закомментирован | phase mail, cure triggers |",
    "| `triggersInjury.json` | закомментирован | injury alert mail |",
    "| `triggersStress.json` | закомментирован | stress mail chain |",
    "| `triggersQuestsStress.json` | закомментирован | quest stress |",
    "",
    "## Рекомендуемый порядок чистки",
    "",
    "1. **Удалить или архивировать** legacy `*Phase*Ready` topics + phase mail (группы A + mailCure phase).",
    "2. **Решить по stress**: включить Include + triggers **или** удалить `dialoguesHarveyStress.json` + `mailStress.json`.",
    "3. **Подключить** 3–5 high-value narrative mail (LateNight, MineWarning уже в triggersCare — OK).",
    "4. **Memory topics**: либо CP TriggerActions при RemoveTopic, либо удалить 14 keys.",
    "5. **Синхронизировать** `topicSurgicalWoundHealed` / `HarveyMod_WetCare` с C#.",
    "",
]

OUT.write_text("\n".join(lines), encoding="utf-8")
print(f"Wrote {OUT}")
print(f"Dead topics: {len(dead_topics)}, dead mail: {len(dead_mails)}")
for g, rows in groups.items():
    print(f"  {g}: {len(rows)}")

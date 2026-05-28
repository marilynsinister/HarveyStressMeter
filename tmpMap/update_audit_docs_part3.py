#!/usr/bin/env python3
"""Regenerate audit docs part 3."""
from pathlib import Path

ROOT = Path(r"C:\Users\Admin\HarveyOverhaulInjury")
DOCS = ROOT / "docs"
TODAY = "2026-05-24"

(DOCS / "audit-dynamic-id-risks.md").write_text(
    f"""# Аудит рисков динамических ID (C# ↔ CP)

Дата: {TODAY} (актуализация)  
Область: `HarveyOverhaulInjury` (C#) ↔ `HarveyOverhaul [CP]`.

## Как читать

- **CP** = ключ в `Characters/Dialogue/Harvey` или `Data/Mail`.
- **C# only** = ID в коде, CP-ключа нет.
- **Dead CP** = ключ в CP, C# не ставит topic.
- Риск: **HIGH** = игрок не видит текст; **MEDIUM** = частично; **LOW** = косметика.

---

## Сводка по шаблонам (актуально)

| Шаблон | Статус | Остаток |
|--------|--------|---------|
| `TopicIds.GetInjuryTopic` | ✅ 14/14 в CP | — |
| `TopicIds.GetTreatmentTopic` (11) | ✅ все в CP | — |
| `TopicIds.GetCuredTopic` | ✅ incl. Cold, Surgical | — |
| `TopicIds.GetPhaseTopicId` | ✅ alias keys в block1 | LOW: фазы 2–3 не AddTopic |
| `PhaseTransition_*` | ✅ OK | — |
| `MailIds.*` send | ✅ 7/7 в CP | MEDIUM: WetCare не wired |
| Event launcher topics | ✅ wired в C# | MEDIUM: minor rescue dialogue |

---

## Исправлено (2026-05-23 — 2026-05-24)

| Риск | Решение |
|------|---------|
| Neglect mail ID | C# → `HarveyMod_NeglectWarning` |
| 4 infection/reminder mail | CP entries + `MailIds` |
| `TopicIds` centralization | `Constants.cs` |
| `topicColdCured`, `topicSurgicalWoundCured` | CP keys + completion list |
| 11× `topicTreatment*` | CP block1 |
| Phase alias block1 | Duplicate keys PhaseHealing/Acute/Recovery |
| `topicHealthDamageSevere`, `topicTooCold`, `topicSurgicalWound` | CP injury |
| Legacy `topicStressRecoveryComplete` checks | Удалены из C# |
| AppliedTriggers one-shot all injuries | **Story one-shot** + **injury cooldown** |
| `topicDiagnosisComplete` orphan | C# `TryAddDiagnosisCompleteTopic` |
| `topicRescueOperation` orphan | `RescueOperationLauncher` |
| Storm `buffStressThunder` | `StormComfortLauncher` |
| Pass-out emergency events | `QueueHospitalEvent` в PassOutHandler |
| Minor mine rescue | `TryTriggerMinorMineRescue` |

---

## Остаётся открытым

| # | Проблема | Сторона | Риск |
|---|----------|---------|------|
| 1 | `MailIds.WetCare` / `WetStitchesCare` — send не wired | C# | MEDIUM |
| 2 | `topicHealthDamageCritical/Severe`, `PostOperativeCare` — не Remove при recovery | C# | MEDIUM |
| 3 | `topicHarvey_PainFlare` — buff/topic не в gameplay (только debug) | C# | MEDIUM |
| 4 | `topicHarveyMinorMineRescue` — нет CP dialogue key | CP | MEDIUM |
| 5 | Фазы 2–3 topic keys — C# не AddTopic (PhaseTransition only) | C# design | LOW |
| 6 | `mailHarvey_Neglect` legacy дубль в CP | CP cleanup | LOW |
| 7 | Memory topics (16) — не wired | CP/C# | LOW (задел) |
| 8 | Stress module (`dialoguesHarveyStress.json` off) | CP | LOW (задел) |

---

## Матрица HIGH-priority — статус

| # | Проблема | Статус |
|---|----------|--------|
| 1 | 11× `topicTreatment*` | ✅ CP |
| 2 | `topicSurgicalWound` base | ✅ CP |
| 3 | `topicSurgicalWoundCured` | ✅ CP (Healed удалён) |
| 4 | `topicColdCured` + Cold phases | ✅ CP |
| 5 | `topicHealthDamageSevere` | ✅ CP |
| 6 | Neglect mail ID | ✅ C# + CP |
| 7 | 4 infection/reminder mail | ✅ CP |
| 8 | WetCare MailIds | ⚠️ CP есть, C# не send |
| 9 | Phase alias block1 | ✅ CP |

---

## Рекомендуемая архитектура (без изменений)

1. **`TopicIds` / `ConversationTopics`** — единственное место Replace. ✅
2. **CP:** exact key для каждого C# ID. ✅ (кроме event-only)
3. **Mail:** все send через `MailIds`. ✅
4. **Генератор:** `tmpMap/final_validation_topics_mail.py` + SMAPI `injury_audit_content`.

**C# обновлён 2026-05-23; CP sync 2026-05-23; launchers 2026-05-24.**
""",
    encoding="utf-8",
)

(DOCS / "audit-topics-mail-final.md").write_text(
    f"""# Аудит топиков и писем HarveyOverhaul — сводка

Дата: {TODAY} (актуализация)  
Источники: `audit-topics-csharp.md`, `audit-mail-csharp.md`, `audit-topics-cp-existence.md`, `audit-mail-cp-existence.md`, `audit-dynamic-id-risks.md`, `audit-dead-content.md`, `audit-relationship-tone.md`, `audit-medical-texts.md`.

---

## Масштаб (актуально)

| Категория | Topics | Mail |
|-----------|--------|------|
| C# вызывает, CP отсутствует (HIGH) | **0** dialogue gaps | **0** |
| CP есть, вызова нет (мёртвый контент) | **64** | **79** |
| Тон / медицина (контент есть) | ~15 generic After | 1 (sleep gate) |
| Event-only topics (OK без dialogue) | 4 | — |

---

## 1. Критические ошибки — статус

### ✅ Закрыто (2026-05-23)

- 11× `topicTreatment*` — CP keys
- `topicSurgicalWound`, `topicSurgicalWoundCured`, `topicColdCured`, Cold phases
- `topicHealthDamageSevere`, `topicTooCold`
- 4 mail: `DirtyWoundInfection`, `WetBandageInfection`, `TreatmentUrgentReminder`, `TreatmentFinalWarning`
- Neglect mail: C# → `HarveyMod_NeglectWarning`
- Phase alias block1 (Cast/Observation/Treatment/Surgery/Rehab)
- Treat copy-paste P0 (Concussion, Infected, Fractured, Burn, Hurt)

### ✅ Закрыто C# (2026-05-24)

- `topicDiagnosisComplete` — `TryAddDiagnosisCompleteTopic`
- `topicRescueOperation` — `RescueOperationLauncher`
- Storm comfort — `StormComfortLauncher` (`buffStressThunder` / `topicHarveyStormStress`)
- Pass-out cutscenes — `eventHarveyEmergencyCare` / `eventHarveyExhaustion`
- Minor mine rescue — `TryTriggerMinorMineRescue`
- Repeatable injuries — cooldown вместо permanent AppliedTriggers
- Completion handler — Cold + Surgical cured

### ⚠️ Открыто (не HIGH)

| ID | Проблема | Приоритет |
|----|----------|-----------|
| `MailIds.WetCare` / `WetStitchesCare` | CP есть, C# не send | MEDIUM |
| `topicHarveyMinorMineRescue` | C# add, нет dialogue | MEDIUM |
| Health damage topics не снимаются | C# cleanup | MEDIUM |
| `mailHarveySleepControl` | C# без hearts gate | MEDIUM |
| Memory topics (16) | Задел | LOW |
| Stress module off | 27+22 ID | LOW |
| Narrative mail (~38) | Задел | LOW |

---

## 2. Чеклист синхронизации (актуальный)

### ID C# ↔ CP

- [x] Каждый gameplay `AddTopic` / `GetPhaseTopicId` / `*Cured` — exact CP dialogue key
- [x] Каждый `addMailForTomorrow` — exact CP mail key
- [x] 11 `topicTreatment*` в CP
- [x] Surgical/Cold/HealthDamage/TooCold keys
- [x] Neglect mail unified ID
- [x] Phase aliases block1
- [x] Все send через `MailIds`
- [ ] `topicHarveyMinorMineRescue` dialogue
- [ ] WetCare mail wired

### Тон / медицина

- [x] P0 Treat copy-paste (CP 2026-05-23)
- [x] P0 phase topics переписаны (block1)
- [x] Hearts gates injury/cure base (CP 2026-05-23)
- [ ] Treat After3–7 generic (не P0 травмы) — LOW
- [ ] C# sleep mail hearts gate — MEDIUM

---

## 3. Рекомендуемый порядок (оставшееся)

1. **MEDIUM:** Wire `HarveyMod_WetCare` / `WetStitchesCare` или удалить константы
2. **MEDIUM:** Dialogue `topicHarveyMinorMineRescue`
3. **MEDIUM:** RemoveTopic для health damage / post-op при recovery
4. **MEDIUM:** C# gate `mailHarveySleepControl` по отношениям
5. **LOW:** Stress Include decision; memory topics; narrative mail cleanup

---

## Связанные документы

| Документ | Фокус |
|----------|-------|
| [audit-dynamic-id-risks.md](./audit-dynamic-id-risks.md) | Шаблоны Replace, GetPhaseTopicId |
| [audit-topics-cp-existence.md](./audit-topics-cp-existence.md) | C# topic → CP dialogue |
| [audit-mail-cp-existence.md](./audit-mail-cp-existence.md) | C# mail → CP Mail |
| [audit-dead-content.md](./audit-dead-content.md) | CP → вызовы (обратный) |
| [audit-relationship-tone.md](./audit-relationship-tone.md) | Hearts / Dating |
| [audit-medical-texts.md](./audit-medical-texts.md) | Медицина по травмам |
| [events-audit.md](./events-audit.md) | CP events + C# launchers |
""",
    encoding="utf-8",
)

print("Wrote audit-dynamic-id-risks.md, audit-topics-mail-final.md")

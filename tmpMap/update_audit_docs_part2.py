#!/usr/bin/env python3
"""Regenerate audit docs part 2."""
from pathlib import Path

ROOT = Path(r"C:\Users\Admin\HarveyOverhaulInjury")
DOCS = ROOT / "docs"
TODAY = "2026-05-24"

(DOCS / "audit-topics-csharp.md").write_text(
    f"""# Аудит conversation topic ID в C# (HarveyOverhaul.InjuryCare)

Дата: {TODAY} (актуализация)  
Область: все `.cs` файлы проекта.  
`PLAYER_HAS_CONVERSATION_TOPIC` в C# **не найден** — условия событий в CP.

---

## Сводка

| Категория | Кол-во |
|-----------|--------|
| Базовые травмы | 14 |
| Сопутствующие | 3 |
| `topicTreatment*` | 11 |
| Фазовые `topic*Phase*` | 33 |
| `topic*Cured` | 14 |
| Осложнения `topicHarvey_*` | 6 |
| Обморок / шахта / FirstTreatment | 7 (+ minor rescue) |
| Event-only topics (без dialogue key) | 4 |
| Launchers (storm / rescue) | 3 |

---

## Ключевые изменения с 2026-05-23

| Область | Было | Стало |
|---------|------|-------|
| Динамические topic ID | Inline `Replace` | Класс `TopicIds` + `ConversationTopics` |
| Completion list | Без Cold/Surgical | `topicColdCured`, `topicSurgicalWoundCured` в `CheckAndHandleCompletionTopic` |
| Neglect mail | `mailHarvey_Neglect` | `MailIds.NeglectWarning` |
| AppliedTriggers | Все one-shot | **Story one-shot** (`SurgicalWound`, `ExplosionInjury`) + **injury cooldown** (`InjuryCooldownUntilDay`) для repeatable |
| `topicDiagnosisComplete` | Не ставился | `DialogueManager.TryAddDiagnosisCompleteTopic` при старте лечения eligible травм |
| `topicRescueOperation` | Orphan | `RescueOperationLauncher` после E5 storm |
| Storm comfort | `buffStressThunder` не ставился | `StormComfortLauncher` — buff или fallback `topicHarveyStormStress` |
| Minor mine rescue | Недостижим (всегда Severe) | `PassOutHandler.TryTriggerMinorMineRescue` — опасное состояние без Severe |
| Pass-out cutscenes | Только buff/topic | `QueueHospitalEvent` → `eventHarveyEmergencyCare` / `eventHarveyExhaustion` |
| Legacy checks | `topicStressRecoveryComplete` | **Удалены** |

---

## Event-only topics (dialogue key не требуется)

| Topic ID | Кто ставит | CP использование |
|----------|------------|------------------|
| `topicHarveyNeedsFirstTreatment` | C# `DialogueManager` | Precondition `HarveyMod_FirstTreatment` |
| `topicFirstTreatmentComplete` | CP event script | C# check-only |
| `topicDiagnosisComplete` | C# `TryAddDiagnosisCompleteTopic` | Precondition `HarveyMod_TreatmentPlanMeeting` |
| `topicRescueOperation` | C# `RescueOperationLauncher` | Precondition `eventRescueOperation` |
| `topicMineRescuePending` | C# `PassOutHandler` | Блокирует CP interception (1 д) |
| `topicHarveyStormStress` | C# `StormComfortLauncher` | Fallback gate storm comfort events |
| `topicHarveyMinorMineRescue` | C# после `eventHarveyMinorMineRescue` | **Нет dialogue key** — MEDIUM |

---

## Активные риски для CP-синхронизации

1. **`topicHealthDamageCritical/Severe`, `topicPostOperativeCare`** — add при травме, **нет remove** при recovery.
2. **`topicShrapnelWounds`** — topic duration 42 д ≠ сумма фаз 22 д.
3. **Фазовые topics 2–3** — C# не AddTopic при смене фазы (только PhaseTransition dialogue).
4. **`topicHarvey_AllergicRash`, `topicHarvey_PainFlare`** — только debug add; PainFlare buff не wired в gameplay.
5. **Ночной визит** — без dating gate.

---

## Префиксы диалогов (не conversation topics)

| Префикс | Где | CP |
|---------|-----|-----|
| `PhaseTransition_*` | `InteractionHandler.AdvanceToNextPhase` | ✓ injury JSON |
| `Treat_*` | `DialogueManager.PickTreatmentDialogue` | ✓ cure JSON |
| `Proximity_*` | `TreatmentManager.BuildCombinedDialogue` | ✓ cure JSON |

---

## Индекс файлов

| Файл | Роль |
|------|------|
| `Core/Constants.cs` | `ConversationTopics`, `TopicIds`, launchers |
| `Managers/DialogueManager.cs` | add/remove; FirstTreatment; DiagnosisComplete |
| `Managers/InjuryManager.cs` | базовые/health/post-op topics |
| `Managers/TreatmentManager.cs` | `topicTreatment*` |
| `EventHandlers/InteractionHandler.cs` | cured, phase1, completion |
| `EventHandlers/PassOutHandler.cs` | pass-out, mine rescue, hospital events |
| `EventHandlers/StormComfortLauncher.cs` | storm buff/topic |
| `EventHandlers/RescueOperationLauncher.cs` | rescue operation topic |
| `Managers/StateManager.cs` | cooldown vs story triggers |
""",
    encoding="utf-8",
)

(DOCS / "audit-topics-cp-existence.md").write_text(
    f"""# Аудит наличия CP-диалогов для C# conversation topics

Дата: {TODAY} (актуализация)  
Источник C#: [audit-topics-csharp.md](./audit-topics-csharp.md)  
CP: `HarveyOverhaul [CP]` → `Characters/Dialogue/Harvey`.

**Подключённые dialogue-файлы:** `dialoguesHarvey.json`, `dialoguesHarveyCare.json`, `dialoguesHarveyCure.json`, `dialoguesHarveyCureStress.json`, `dialoguesHarveyInjury.json`, `dialoguesHarveyPregnant.json`.  
**Не подключён:** `dialoguesHarveyStress.json`.

---

## Сводка

| Категория | C# ID | CP exact match | HIGH gaps |
|-----------|-------|----------------|-----------|
| Базовые травмы | 14 | **14** | 0 |
| Сопутствующие | 3 | **3** | 0 |
| `topicTreatment*` | 11 | **11** | 0 |
| Фазовые `topic*Phase*` | 33 | **33** | 0 (alias keys в block1) |
| `topic*Cured` | 14 | **14** | 0 |
| Осложнения | 6 | **6** | 0 |
| Обморок / env | 5 | **5** | 0 |
| Event-only (dialogue не нужен) | 4 | — | 0 |
| Dialogue gaps (MEDIUM) | 1 | 0 | `topicHarveyMinorMineRescue` |

**Итог:** все HIGH-priority topic ID, для которых C# ожидает реплику Харви при разговоре, **имеют CP keys** (добавлены 2026-05-23 + medical/tone правки).

---

## CP keys без dialogue (by design)

| Topic ID | Кто ставит | Почему OK |
|----------|------------|-----------|
| `topicHarveyNeedsFirstTreatment` | C# | Триггер CP event, не dialogue |
| `topicFirstTreatmentComplete` | CP event | C# check-only |
| `topicDiagnosisComplete` | C# | Триггер `HarveyMod_TreatmentPlanMeeting` |
| `topicRescueOperation` | C# launcher | Триггер `eventRescueOperation` |
| `topicMineRescuePending` | C# | Блокирующий (1 д), реплика не нужна |
| `topicHarveyStormStress` | C# launcher | Gate storm events; реплика в cutscene |

---

## Открытые задачи (MEDIUM / LOW)

| ID | Проблема | Действие |
|----|----------|----------|
| `topicHarveyMinorMineRescue` | C# add после minor rescue; **нет dialogue key** | Добавить в `dialoguesHarveyInjury.json` |
| `topicSurgicalWoundHealed` | Legacy key удалён из CP | C# использует `topicSurgicalWoundCured` ✓ |
| `Recovery_Complete_*` | C# filter, ключей нет | Добавить или убрать filter |
| `topicHarvey_ForcedHospitalization` | C# inline hosp, не AddTopic | OK как optional dialogue |

---

## Топ исправлений (статус)

1. ~~11× `topicTreatment*`~~ — **✅ done**
2. ~~Cold phase + cured~~ — **✅ done**
3. ~~Surgical base + cured~~ — **✅ done**
4. ~~HealthDamageSevere, TooCold, MineInjuryRescue, PainFlare~~ — **✅ done**
5. ~~Phase alias block1~~ — **✅ done**
6. **MEDIUM:** `topicHarveyMinorMineRescue` dialogue — **открыто**

---

## Методология

- Скрипт: `tmpMap/final_validation_topics_mail.py`
- Exact match ключей в Include-файлах
- Event-only topics: precondition в `events.json`, dialogue key не обязателен
- Последняя валидация: {TODAY} — **3 missing** = event-only topics без dialogue (ожидаемо)
""",
    encoding="utf-8",
)

print("Wrote audit-topics-csharp.md, audit-topics-cp-existence.md")

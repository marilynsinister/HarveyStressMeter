#!/usr/bin/env python3
"""Update manually maintained events-inventory docs (2026-05-24)."""
from __future__ import annotations

import re
from datetime import date
from pathlib import Path

ROOT = Path(r"C:\Users\Admin\HarveyOverhaulInjury")
INV = ROOT / "docs" / "events-inventory"
TODAY = date.today().isoformat()


def patch(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"patch miss in {path.name}: {old[:80]!r}...")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


def main() -> None:
    # --- README ---
    (INV / "README.md").write_text(
        f"""# Events Inventory — HarveyOverhaul / InjuryCare

Черновая инвентаризация всех CP-событий и C#-мостов мода InjuryCare + content pack HarveyOverhaul [CP].

| Файл | Содержание |
|---|---|
| [00-summary-table.md](00-summary-table.md) | Сводная таблица |
| [01-cp-events-catalog.md](01-cp-events-catalog.md) | Полный каталог CP с script preview |
| [02-csharp-bridges.md](02-csharp-bridges.md) | C# startEvent, topics, mail (авто) |
| [03-gaps-and-risks.md](03-gaps-and-risks.md) | Разрывы, дубликаты, недостижимые события |
| [04-fork-subevents.md](04-fork-subevents.md) | Fork-подсобытия (declineFood, refuseCheckup, …) |
| [05-csharp-inventory.md](05-csharp-inventory.md) | Детальный разбор C# startEvent, topics, mail |
| [06-locations-index.md](06-locations-index.md) | Индекс по локациям Data/Events/* |
| [07-reachability-table.md](07-reachability-table.md) | **Достижимость:** сводная таблица |
| [07-reachability-details.md](07-reachability-details.md) | **Достижимость:** детальный разбор (5 пунктов) |
| [08-events-as-book.md](08-events-as-book.md) | **Книга:** читабельное содержание всех сцен |
| [09-timing-audit.md](09-timing-audit.md) | **Тайминги:** аудит C#-обработчиков и цепочек |
| [10-relationship-narrative-audit.md](10-relationship-narrative-audit.md) | **Сюжет:** аудит тона и relationship gates |
| [11-id-sync-audit.md](11-id-sync-audit.md) | **ID sync:** buff/topic/mail/event/trigger C# ↔ CP |
| [12-cp-event-launch-safety.md](12-cp-event-launch-safety.md) | **Безопасность:** C# `startEvent` / mine rescue |
| [13-one-shot-audit.md](13-one-shot-audit.md) | **Одноразовость:** eventsSeen, AppliedTriggers, topics |
| [14-scenario-chains.md](14-scenario-chains.md) | **Сценарные цепочки:** 8 major flows step-by-step |
| [events-audit.md](../events-audit.md) | **Сводный аудит** CP + C# |
| [harvey-events-fix-report.md](../harvey-events-fix-report.md) | Отчёт о правках 2026-05-23 |

**Аудит обращений по уровню отношений (с правками CP):** [harvey-relationship-visits-audit](../harvey-relationship-visits-audit/) — gates, topics, визиты Харви, тон, контакт.

**Статус:** актуализация **{TODAY}**. **46** уникальных event ID в активном CP (49 записей с дублями ключей). C# launchers wired: hospital pass-out (`eventHarveyEmergencyCare` / `eventHarveyExhaustion`), minor mine rescue, storm comfort buff gate, `topicRescueOperation`, `topicDiagnosisComplete`.

**Автоген:** `00`–`04`, `06`, `08`, `11`, `13` — `python tmpMap/parse_events_inventory.py`, `generate_event_book.py`, `sync_id_audit.py`, `one_shot_audit.py`. Ручные секции — `python tmpMap/update_events_inventory_manual.py`.

**Отчёт о правках:** [harvey-events-fix-report.md](../harvey-events-fix-report.md) · **Сводный аудит:** [events-audit.md](../events-audit.md)

**CP sources (content.json):** `events.json`, `eventsCare.json`, `eventsMineRescue.json`  
**Не подключено:** `events_for_mode_new_formatted.json`
""",
        encoding="utf-8",
    )

    # --- 05 C# inventory ---
    (INV / "05-csharp-inventory.md").write_text(
        f"""# C# InjuryCare: запуск событий и мосты к CP

Мод **HarveyOverhaulInjury** (C#) напрямую запускает **mine rescue**, **hospital pass-out cutscenes** и **minor mine rescue**. Остальные CP-события — vanilla entry (локация + preconditions) или **SpaceCore PlayEvent** из `triggersCare.json`. Storm comfort и rescue operation используют C# **topic/buff gates**, CP играет cutscene при входе в локацию.

**Актуализация:** {TODAY}

---

## 1. Прямой запуск событий (`startEvent`)

| Метод | Файл | Когда | Event IDs | Location |
|---|---|---|---|---|
| `TriggerEventByName()` | `PassOutHandler.cs` | После warp в Mine (`OnPlayerWarped`) | `eventHarveyMineRescue`, `eventHarveyMineRescueDating` | Mine |
| `TryStartLocationEvent()` | `PassOutHandler.cs` | Minor rescue / hospital resume | `eventHarveyMinorMineRescue`, `eventHarveyEmergencyCare`, `eventHarveyExhaustion` | Mine / Hospital |
| `QueueHospitalEvent()` | `PassOutHandler.cs` | Critical pass-out / exhaustion вне шахты | `eventHarveyEmergencyCare`, `eventHarveyExhaustion` | Hospital (warp → startEvent) |
| `TryTriggerMinorMineRescue()` | `PassOutHandler.cs` ← `PlayerEventHandler` (вход в Mine) | Injury без Severe | `eventHarveyMinorMineRescue` | Mine |
| `TriggerMineRescueEvents()` | `PassOutHandler.cs` ← `GameEventHandler.OnDayStarted` | Утро после боевой смерти в шахте | severe dating rescue | телепорт → Mine |

**Цепочка mine rescue (severe):**

1. `OnUpdateTicked` / `TrackPassOut` — `NeedsMineRescueEvent`, `PassedOutInMineYesterday`, `ApplyBadlyHurtFromMinePassOut()`
2. `OnDayStarted` → `TriggerMineRescueEvents()` — `eventHarveyMineRescueDating` (dating/married)
3. Warp Mine (17,7) → `OnPlayerWarped` → `Load Data/Events/Mine` → `startEvent`
4. Fallback: `RunMineRescueFallback()` — topic + warp Hospital

**Цепочка hospital pass-out:**

1. `OnPlayerWarped` после сна — critical health ≤10 **вне шахты** или exhaustion (`WasExhausted`)
2. `QueueHospitalEvent` → warp Hospital → `TryStartLocationEvent` в `OnPlayerWarped`
3. Если `eventsSeen` — только fallback (topic/HUD); pending resume через `PendingHospitalPassOutEventId`

**Minor mine rescue:**

- `PlayerEventHandler` при входе в Mine с injury buff, **без** Severe → `TryTriggerMinorMineRescue()`
- Cooldown: `LastMinorMineRescueDay`; seen → skip cutscene
- Fallback topic: `topicHarveyMinorMineRescue`

**`eventsSeen`:** добавляется в `onEventFinished` (mine rescue fix 2025-05). Риск рассинхрона с CP `!PLAYER_HAS_SEEN_EVENT` снижен.

**`Load Data/Events/...`:** `Data/Events/Mine`, `Data/Events/Hospital` (pass-out cutscenes).

---

## 2. Topics как мост C# → CP

| Topic | Кто выставляет (C#) | CP-события, ожидающие topic | Примечание |
|---|---|---|---|
| `topicPassedOutInTown` | `PassOutHandler` | `eventHarveyCheckFarmerOutsideAfter22` | ✅ |
| `topicFarmerExhausted` | `PassOutHandler` fallback | — (cutscene через `QueueHospitalEvent`) | ✅ exhaustion wired |
| `topicMineInjuryRescue` | mine rescue event / fallback | forced hosp | ✅ |
| `topicHarveyMinorMineRescue` | minor rescue fallback | dialogues | ✅ |
| `topicDiagnosisComplete` | `DialogueManager.TryAddDiagnosisCompleteTopic` | `HarveyMod_TreatmentPlanMeeting` | ✅ wired 2026-05-24 |
| `topicRescueOperation` | `RescueOperationLauncher` после E5 / storm comfort | `eventRescueOperation` | ✅ wired 2026-05-24 |
| `topicHarveyStormStress` / `buffStressThunder` | `StormComfortLauncher` (daily roll) | `eventHarveyStormComfort*` | ✅ buff gate wired |
| `HarveyMod_CD_StormComfort` | после storm comfort event | cooldown | ✅ |
| `HarveyMod_CD_RescueOperation` | после rescue event | cooldown | ✅ |
| Injury topics `topicHurt`, … | `InjuryManager` | dialogues, triggersCare | dialogue bridge |

**Topics только из CP (C# не выставляет):**

- `topicFirstMeeting`, `topicAgreedCheckup` — BusStop care chain
- `topicHarveySecondVisit`, `topicHarveyFirstVisit` — Farm visits
- `HarveyMod_CD_*` (story E1–E8) — script bridge в CP events

---

## 3. Mail как мост C# → CP

| Mail | C# | CP consumer |
|---|---|---|
| `mailHarveySleepControl` | `PassOutHandler` | dialogues |
| `mailHarveyMineForbidden` | `GameEventHandler.OnDayEnding` | debuff (C#) |
| `HarveyMod_*` neglect/infection | `ComplicationManager` | mail entries + dialogues |
| `mailHarveyMedicalCheckReminder` | triggersCare (CP) | `eventHarveyMedicalCheck` |
| `mailHarveyAfterMineRescue` | CP script | post-rescue |

---

## 4. SpaceCore PlayEvent (CP triggers, не C#)

Из `assets/Code/triggersCare.json`:

| Trigger ID | Event | Условие |
|---|---|---|
| `triggerHarveySkullCaveWarning` | `eventHarveySkullCavePrevention` | dating + injury buffs (Condition битый: Mine+SkullCave) |
| `triggerHarveyMineWarning` | `eventHarveyMineInterception` | dating + base injury buffs + Mine |
| `triggerLocationReactionSkullCaveExit` | `eventHarveySkullCavePrevention` | SkullCave exit + dating |

---

## 5. C# launchers vs orphan CP scripts

| CP event | C# launcher / альтернатива |
|---|---|
| `eventHarveyEmergencyCare` | ✅ `QueueHospitalEvent` (critical pass-out) |
| `eventHarveyExhaustion` | ✅ `QueueHospitalEvent` (exhaustion pass-out) |
| `eventHarveyMinorMineRescue` | ✅ `TryTriggerMinorMineRescue` |
| `eventHarveyStormComfort*` | ✅ `StormComfortLauncher` → buff/topic → vanilla entry |
| `eventRescueOperation` | ✅ `RescueOperationLauncher` → topic → vanilla entry |
| `eventStayInHospital` | `HospitalizationManager` — dialogue block (orphan script) |
| `eventHarveyTreatmentCollapse` | orphan — нет caller |

---

## 6. Сводка: event ID в C#

```
eventHarveyMineRescue          PassOutHandler.cs (legacy fallback)
eventHarveyMineRescueDating    PassOutHandler.cs
eventHarveyMinorMineRescue     PassOutHandler.cs, PlayerEventHandler.cs
eventHarveyEmergencyCare       PassOutHandler.cs (QueueHospitalEvent)
eventHarveyExhaustion          PassOutHandler.cs (QueueHospitalEvent)
eventRescueOperation           RescueOperationLauncher.cs (topic only; CP plays event)
eventHarveyStormComfort*       StormComfortLauncher.cs (IsStormComfortEventId)
```

---

## 7. State flags (не events, но связаны)

| Flag / state | Файл | Назначение |
|---|---|---|
| `NeedsMineRescueEvent` | PassOutHandler | очередь severe mine rescue |
| `PendingHospitalPassOutEventId` | PassOutHandler | resume hospital cutscene |
| `PendingMinorMineRescueEventId` | PassOutHandler | resume minor rescue |
| `LastStormComfortRollDay` / `LastStormComfortEventDay` | StormComfortLauncher | 1 roll / 1 event per day |
| `LastMinorMineRescueDay` | PassOutHandler | minor rescue cooldown |
| `MineWarningDay` | GameEventHandler | mail forbidden chain |
""",
        encoding="utf-8",
    )

    # --- 07 reachability table ---
    rt = INV / "07-reachability-table.md"
    patch(
        rt,
        "Анализ для **активной** связки",
        f"**Актуализация {TODAY}.** Анализ для **активной** связки",
    )
    replacements = [
        (
            "| `eventHarveyMinorMineRescue` | Нет | C# minor если !Severe; шахтная смерть всегда `buffBadlyHurt` + dating gate | Severe vs minor | Minor без badly hurt или отдельный trigger |",
            "| `eventHarveyMinorMineRescue` | **Частично** | C# `TryTriggerMinorMineRescue` при входе в Mine с injury **без** Severe; **не** боевой death-rescue | Severe combat death → major rescue only; 1×/день cooldown | Документировать отличие minor vs severe path |",
        ),
        (
            "| `eventHarveyEmergencyCare` | **Нет** | Script-only; PlayEvent был в **отключённом** `triggersInjury.json` (BETAS PassedOut); C# даёт buff/topic **без** cutscene | Нет активного launcher | C# `PlayEvent` при badly hurt pass-out **или** включить trigger |",
            "| `eventHarveyEmergencyCare` | **Частично** | C# `QueueHospitalEvent` при critical pass-out (health≤10) **вне шахты** + dating/married; fallback если seen | Dating gate; one-shot cutscene (`eventsSeen`) | — |",
        ),
        (
            "| `eventHarveyExhaustion` | **Нет** | Аналогично — `triggersInjury` LocationChanged + exhausted; C# — topic/buff only | Нет активного launcher | C# PlayEvent при exhaustion pass-out |",
            "| `eventHarveyExhaustion` | **Частично** | C# `QueueHospitalEvent` при `WasExhausted` pass-out **вне шахты**; в шахте — только fallback topic | Exhaustion в Mine пропускает cutscene | — |",
        ),
        (
            "| `HarveyMod_TreatmentPlanMeeting` | **Нет** | Требует `topicDiagnosisComplete` | Topic **нигде не создаётся** (mail закомментирован) | Добавить topic в quest/mail/trigger при завершении диагностики |",
            "| `HarveyMod_TreatmentPlanMeeting` | **Частично** | Hospital + `topicDiagnosisComplete` — C# `TryAddDiagnosisCompleteTopic` при старте eligible лечения | Нужен активный treatment eligible injury | — |",
        ),
        (
            "| `eventHarveyStormComfortFarm` | **Нет** | Нужны storm + `buffStressThunder` + hearts 750 + Random 0.6 + Farm evening | **`buffStressThunder` не выставляется** (`triggersStress` отключён) | Включить stress triggers **или** buff при storm через CP/C# |",
            "| `eventHarveyStormComfortFarm` | **Частично** | C# `StormComfortLauncher` (daily roll) → `buffStressThunder` или `topicHarveyStormStress`; затем vanilla entry + Random | Dating не требуется; hearts ≥750; 1 roll/день | Random + location window |",
        ),
        (
            "| `eventHarveyStormComfortForest` | **Нет** | То же + Forest + Random 0.55 | Нет источника buff | То же |",
            "| `eventHarveyStormComfortForest` | **Частично** | То же (C# buff gate) + Forest + Random 0.55 | Random + storm day | — |",
        ),
        (
            "| `eventHarveyStormComfortTown` | **Нет** | Town + Random 0.3 | Нет buff | То же |",
            "| `eventHarveyStormComfortTown` | **Частично** | C# buff gate + Town + Random 0.3 | Random | — |",
        ),
        (
            "| `eventHarveyStormComfortMine` | **Нет** | Mine + Random 0.8 | Нет buff | То же |",
            "| `eventHarveyStormComfortMine` | **Частично** | C# buff gate + Mine + Random 0.8 | Random | — |",
        ),
        (
            "| `eventHarveyStormComfortMountain` | **Нет** | Custom_AdventurerSummit + Random 0.4 | Нет buff + SVE локация | То же |",
            "| `eventHarveyStormComfortMountain` | **Частично** | C# buff gate + SVE summit + Random 0.4 | SVE + Random | — |",
        ),
        (
            "| `eventHarveyStormComfortDesert` | **Нет** | Desert + Random 0.3 | Нет buff | То же |",
            "| `eventHarveyStormComfortDesert` | **Частично** | C# buff gate + Desert + Random 0.3 | Random | — |",
        ),
        (
            "| `eventRescueOperation` | **Нет** | Woods, storm, hearts 600, `topicRescueOperation` | Topic **нигде не создаётся** | Добавить topic в trigger/mail/quest перед событием |",
            "| `eventRescueOperation` | **Частично** | C# `RescueOperationLauncher` ставит `topicRescueOperation` после E5 / storm comfort; Woods + storm + hearts 600 | Нужен storm + topic window + !seen | — |",
        ),
        (
            "| InjuryCare / mine | 1 | 3 | 1 |\n| Care / pass-out chain | 3 | 4 | 5 |\n| Hospital mod | 3 | 1 | 1 |\n| Story E1–E8 | 3 | 5 | 0 |\n| Romance | 4 | 1 | 0 |\n| Storm comfort | 0 | 0 | 6 (+3 мёртвых файла) |\n| Прочее | 0 | 0 | 1 (+3 мёртвых) |",
            "| InjuryCare / mine | 1 | 4 | 0 |\n| Care / pass-out chain | 3 | 6 | 2 |\n| Hospital mod | 3 | 2 | 0 |\n| Story E1–E8 | 3 | 5 | 0 |\n| Romance | 4 | 1 | 0 |\n| Storm comfort | 0 | 6 | 0 (+3 мёртвых файла) |\n| Прочее | 0 | 1 | 0 (+3 мёртвых) |",
        ),
        (
            "**Критичные конфликты InjuryCare:** minor mine rescue недостижим; phase-buff vs CP trigger buff list; C# pass-out без PlayEvent для exhaustion/emergency; `topicMineInjuryRescue` снимается при forced hosp до использования в других CP-ветках.",
            "**Оставшиеся конфликты InjuryCare:** phase-buff vs CP trigger buff list (Mine/Skull interception); `topicMineInjuryRescue` снимается при forced hosp; morning checkup Dating-only vs Married after22; orphan `eventStayInHospital` / `eventHarveyTreatmentCollapse`.",
        ),
    ]
    for old, new in replacements:
        patch(rt, old, new)

    # --- 07 reachability details ---
    rd = INV / "07-reachability-details.md"
    patch(
        rd,
        "### `eventHarveyMinorMineRescue`\n\n1. **Условия:** C# minor если `!HasAnyBuff(Severe)`; warp Mine; без CP preconditions при C# start.\n2. **Источники:** C# только; script добавляет `topicMineInjuryRescue`.\n3. **Геймплей:** **нет** при стандартной шахтной смерти — всегда `buffBadlyHurt`.\n4. **Конфликты:** **Severe vs minor** — прямое противоречие дизайна InjuryCare.\n5. **Исправление:** minor при `buffHurt`/sprain без badly hurt **или** отдельный trigger (усталость в mine, не combat death).",
        "### `eventHarveyMinorMineRescue`\n\n1. **Условия:** C# `TryTriggerMinorMineRescue` — injury buff active, **нет** Severe, dating не требуется; warp Mine или start in-place.\n2. **Источники:** `PlayerEventHandler` при входе в Mine; `LastMinorMineRescueDay` cooldown; fallback `topicHarveyMinorMineRescue`.\n3. **Геймплей:** **да** при лёгкой травме + вход в шахту; **нет** при боевой смерти (severe path).\n4. **Конфликты:** отделён от severe rescue; seen → skip cutscene.\n5. **Исправление:** ✅ **2026-05-24** — wired в C#.",
    )
    patch(
        rd,
        "### `eventHarveyEmergencyCare` / `eventHarveyExhaustion`\n\n1. **Условия:** script-only (no `/` preconditions).\n2. **Источники:** **только** отключённый `triggersInjury.json` (BETAS PassedOut / exhausted LocationChanged); C# заменяет buff/topic без event.\n3. **Геймплей:** **нет** в текущей сборке.\n4. **Конфликты:** двойная реализация C# vs CP trigger; активна только C# без cutscene.\n5. **Исправление:** один launcher: C# `PlayEvent` после ApplyBadlyHurt / exhausted **или** включить triggersInjury без дублирования C# topics.",
        "### `eventHarveyEmergencyCare` / `eventHarveyExhaustion`\n\n1. **Условия:** script-only в CP; C# `QueueHospitalEvent` warp Hospital → `startEvent`.\n2. **Источники:** `PassOutHandler.OnPlayerWarped` — critical health ≤10 (не mine) или `WasExhausted` (не mine).\n3. **Геймплей:** **да** при dating/married pass-out вне шахты; one-shot (`eventsSeen`) + fallback topic.\n4. **Конфликты:** exhaustion **в шахте** — cutscene пропущен (mine pipeline).\n5. **Исправление:** ✅ **2026-05-24** — `QueueHospitalEvent` wired.",
    )
    patch(
        rd,
        "### `HarveyMod_TreatmentPlanMeeting`\n\n1. **Условия:** Hospital 09:00–17:00; `topicDiagnosisComplete`; hearts 500.\n2. **Источники:** topic **отсутствует** (mail `HarveyMod_DiagnosisComplete` закомментирован).\n3. **Геймплей:** **нет**.\n4. **Конфликты:** orphan consumer topic.\n5. **Исправление:** quest/mail/trigger ставит topic после stress diagnosis **или** удалить event.",
        "### `HarveyMod_TreatmentPlanMeeting`\n\n1. **Условия:** Hospital 09:00–17:00; `topicDiagnosisComplete`; hearts 500.\n2. **Источники:** C# `TryAddDiagnosisCompleteTopic` при старте eligible treatment (`TreatmentManager`).\n3. **Геймплей:** **да** после начала лечения phase-eligible травмы + визит в Hospital.\n4. **Конфликты:** topic снимается после meeting script.\n5. **Исправление:** ✅ **2026-05-24**.",
    )
    patch(
        rd,
        "## Storm comfort (`eventHarveyStormComfort*` ×6)\n\n1. **Условия:** storm weather + `buffStressThunder` + hearts 750 + Random 0.3–0.8 + location + time (Farm evening).\n2. **Источники:** buff — **нет активного producer** (`triggersStress` commented); topic `topicStressThunder` — только consumer в script (remove at end).\n3. **Геймплей:** **нет** без внешнего buff.\n4. **Конфликts:** stress subsystem отключён, events остались.\n5. **Исправление:** включить `triggersStress.json` **или** CP trigger `AddBuff buffStressThunder` при storm+hearts **или** убрать buff gate из events.",
        "## Storm comfort (`eventHarveyStormComfort*` ×6)\n\n1. **Условия:** storm + `buffStressThunder` (или legacy topic) + hearts 750 + Random + location/time.\n2. **Источники:** C# `StormComfortLauncher.TryDailyStormComfortRoll` (TimeEventHandler) — 1 roll/день, `buffStressThunder` или `topicHarveyStormStress`.\n3. **Геймплей:** **частично** — нужен успешный daily roll + storm + вход в локацию + Random.\n4. **Конфликты:** Random может не сработать; cooldown `HarveyMod_CD_StormComfort` после event.\n5. **Исправление:** ✅ **2026-05-24** — C# buff gate wired.",
    )
    patch(
        rd,
        "### `eventRescueOperation`\n\n1. **Условия:** Woods; storm; hearts 600; `topicRescueOperation`.\n2. **Источники:** topic **never set** in active content.\n3. **Геймплей:** **нет**.\n4. **Конфликты:** orphan topic consumer.\n5. **Исправление:** mail/quest adds topic before storm **или** удалить event.",
        "### `eventRescueOperation`\n\n1. **Условия:** Woods; storm; hearts 600; `topicRescueOperation`.\n2. **Источники:** C# `RescueOperationLauncher` после E5_StormBeside или storm comfort event.\n3. **Геймплей:** **частично** — нужен topic + storm + Woods + !seen.\n4. **Конфликты:** parallel trauma arc, не E1–E8; cooldown `HarveyMod_CD_RescueOperation`.\n5. **Исправление:** ✅ **2026-05-24**.",
    )
    patch(
        rd,
        "| C# pass-out vs disabled BETAS triggers | PassOutHandler vs triggersInjury | Cutscenes emergency/exhaustion/late collapse не играют |\n| Severe always on mine death | PassOutHandler + InjuryManager | Minor rescue мёртв |",
        "| C# pass-out vs disabled BETAS triggers | PassOutHandler vs triggersInjury | Emergency/exhaustion **wired**; late collapse Town — только random entry |\n| Severe vs minor mine path | PassOutHandler + PlayerEventHandler | Minor rescue **wired** для non-Severe injury |",
    )

    # --- 09 timing audit header + key line ---
    ta = INV / "09-timing-audit.md"
    text = ta.read_text(encoding="utf-8")
    text = text.replace(
        "Черновик аудита C#-обработчиков и их связи с CP-сценами. **Ничего не исправлено.**",
        f"Аудит C#-обработчиков и их связи с CP-сценами. **Актуализация {TODAY}** — добавлены `QueueHospitalEvent`, `StormComfortLauncher`, `RescueOperationLauncher`, minor mine rescue.",
        1,
    )
    text = text.replace(
        "CP events (`HarveyMod_FirstTreatment`, `eventHarveyEmergencyCare` и т.д.) **не вызываются** из C#. Только `StartTreatment` + delayed dialogue 1000 ms.",
        "CP events лечения по клику (`HarveyMod_FirstTreatment` и т.д.) **не вызываются** из C# — только `StartTreatment` + dialogue. **Исключение (2026-05-24):** pass-out cutscenes `eventHarveyEmergencyCare` / `eventHarveyExhaustion` через `QueueHospitalEvent`.",
        1,
    )
    if "| `HarveyMod_TreatmentUrgentReminder` | DayStarted phase neglect | ❌ **нет в CP** |" in text:
        text = text.replace(
            "| `HarveyMod_TreatmentUrgentReminder` | DayStarted phase neglect | ❌ **нет в CP** |",
            "| `HarveyMod_TreatmentUrgentReminder` | DayStarted phase neglect | ✅ CP 2026-05-23 |",
        )
        text = text.replace("| `HarveyMod_TreatmentFinalWarning` | DayStarted | ❌ |", "| `HarveyMod_TreatmentFinalWarning` | DayStarted | ✅ |")
        text = text.replace("| `HarveyMod_DirtyWoundInfection` | DayStarted complication | ❌ |", "| `HarveyMod_DirtyWoundInfection` | DayStarted complication | ✅ |")
        text = text.replace("| `HarveyMod_WetBandageInfection` | DayStarted | ❌ |", "| `HarveyMod_WetBandageInfection` | DayStarted | ✅ |")
        text = text.replace(
            "| `MailIds.Neglect` | DayStarted | ⚠️ mismatch с CP |",
            "| `MailIds.NeglectWarning` | DayStarted | ✅ unified 2026-05-23 |",
        )
    ta.write_text(text, encoding="utf-8")

    # --- 10 relationship audit date ---
    patch(
        INV / "10-relationship-narrative-audit.md",
        "**Актуализация после правок:** 2026-05-23 — split NightCrisis/Birthday/MedicalCheck, care chain, variant B тона, physical-contact audit.",
        f"**Актуализация:** {TODAY} (gates без изменений в этом проходе). Предыдущие правки 2026-05-23 — split NightCrisis/Birthday/MedicalCheck, care chain, variant B тона.",
    )

    # --- 12 launch safety intro ---
    ls = INV / "12-cp-event-launch-safety.md"
    patch(
        ls,
        "Аудит единственного C#-пути `location.startEvent(...)` — `PassOutHandler.TriggerEventByName`.",
        "Аудит C#-путей `location.startEvent(...)`: severe/minor **mine rescue**, **hospital pass-out** (`QueueHospitalEvent`), resume после save.",
    )
    if "## Обзор цепочки mine rescue" in ls.read_text(encoding="utf-8"):
        insert = f"""

---

## Обзор: hospital pass-out cutscenes ({TODAY})

```
OnPlayerWarped (после сна)
  → critical health ≤10 (не mine) OR WasExhausted (не mine)
  → QueueHospitalEvent(eventHarveyEmergencyCare | eventHarveyExhaustion)
      → warp Hospital, PendingHospitalPassOutEventId
  → OnPlayerWarped Hospital → TryStartLocationEvent
  → onEventFinished → eventsSeen (one-shot)
```

**Minor mine rescue:** `PlayerEventHandler` (Mine entry) → `TryTriggerMinorMineRescue` — отдельный pipeline, не severe DayStarted rescue.

"""
        text = ls.read_text(encoding="utf-8")
        if "## Обзор: hospital pass-out" not in text:
            text = text.replace("---\n\n## Checklist по вопросам", insert + "## Checklist по вопросам", 1)
            ls.write_text(text, encoding="utf-8")

    # --- 14 scenario chains ---
    sc = INV / "14-scenario-chains.md"
    patch(
        sc,
        "Пошаговые flow для крупных игровых сценариев. **Актуализация 2026-05-23** (C# mine rescue Dating-only, gates без изменений в этом файле).",
        f"Пошаговые flow для крупных игровых сценариев. **Актуализация {TODAY}** — hospital pass-out cutscenes, minor mine rescue, storm/rescue topic launchers.",
    )
    patch(
        sc,
        "| **Storm comfort** | `buffStressThunder` + storm | Random entry | **C# не ставит buff** | Unreachable |",
        "| **Storm comfort** | C# daily roll → `buffStressThunder` | Random entry + location | Random может fail | **Частично достижимо** |",
    )

    print(f"Updated manual events-inventory docs ({TODAY})")


if __name__ == "__main__":
    main()

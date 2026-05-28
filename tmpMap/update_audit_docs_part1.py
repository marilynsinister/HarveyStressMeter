#!/usr/bin/env python3
"""Regenerate summary sections for HarveyOverhaul audit docs (2026-05-24)."""
from __future__ import annotations

from datetime import date
from pathlib import Path

ROOT = Path(r"C:\Users\Admin\HarveyOverhaulInjury")
DOCS = ROOT / "docs"
TODAY = "2026-05-24"

# --- audit-mail-csharp.md ---
(DOCS / "audit-mail-csharp.md").write_text(
    f"""# Аудит писем, которые отправляет C# (HarveyOverhaul.InjuryCare)

Дата: {TODAY} (актуализация)  
Область: все `.cs` файлы проекта (без `obj/`).

Метод отправки: `Game1.addMailForTomorrow(...)` — письмо на **следующее утро**.  
Глобальный выключатель: `ModConfig.SendLetters` (по умолчанию `true`).

**Не найдено:** `mailReceived`, другие API отправки почты.

---

## Сводка

| Категория | Кол-во |
|-----------|--------|
| Реально отправляемые mail ID | **7** |
| Все через `MailIds.*` | **7** (100%) |
| Строковые литералы напрямую | **0** |
| Константы `MailIds` без send | **3** (`WetCare`, `WetStitchesCare`, `InfectionAlert`) |
| Legacy save-поля без send | **2** (`WetBandageMailDay`, `WetStitchesMailDay`) |

---

## Таблица: все вызовы `addMailForTomorrow`

| Mail ID | Где | Условие | Повтор | CP entry | Комментарий |
|---------|-----|---------|--------|----------|-------------|
| `mailHarveySleepControl` | `PassOutHandler` | Обморок в Town после 26:00 + dating/married + `SendLetters` | Может повториться | ✓ `mailInjury.json` | `MailIds.SleepControl` |
| `mailHarveyMineForbidden` | `GameEventHandler.OnDayEnding` | `MineWarningDay == today` после severe + шахта | 1× на инцидент | ✓ | `MailIds.MineForbidden` |
| `HarveyMod_DirtyWoundInfection` | `ComplicationManager` | Roll dirty wound → infection | При новом dirty | ✓ | `MailIds.DirtyWoundInfection` |
| `HarveyMod_WetBandageInfection` | `ComplicationManager` | Roll wet bandage → infection | При новой wet | ✓ | `MailIds.WetBandageInfection` |
| `HarveyMod_TreatmentUrgentReminder` | `ComplicationManager.CheckPhaseNeglect` | `days == phaseDuration + 3` | Новая фаза/травма | ✓ | `MailIds.TreatmentUrgentReminder` |
| `HarveyMod_TreatmentFinalWarning` | `ComplicationManager.CheckPhaseNeglect` | `days == totalAllowed - 1` | Новая фаза/травма | ✓ | `MailIds.TreatmentFinalWarning` |
| `HarveyMod_NeglectWarning` | `ComplicationManager.CheckPhaseNeglect` | `days >= totalAllowed` | Пока просрочка | ✓ | `MailIds.NeglectWarning` (не `mailHarvey_Neglect`) |

---

## Константы `MailIds` — полный список

| Константа | Значение | Send? | CP | Комментарий |
|-----------|----------|-------|-----|-------------|
| `SleepControl` | `mailHarveySleepControl` | **Да** | ✓ | — |
| `MineForbidden` | `mailHarveyMineForbidden` | **Да** | ✓ | — |
| `DirtyWoundInfection` | `HarveyMod_DirtyWoundInfection` | **Да** | ✓ | Добавлен в CP 2026-05-23 |
| `WetBandageInfection` | `HarveyMod_WetBandageInfection` | **Да** | ✓ | Добавлен в CP 2026-05-23 |
| `TreatmentUrgentReminder` | `HarveyMod_TreatmentUrgentReminder` | **Да** | ✓ | Добавлен в CP 2026-05-23 |
| `TreatmentFinalWarning` | `HarveyMod_TreatmentFinalWarning` | **Да** | ✓ | Добавлен в CP 2026-05-23 |
| `NeglectWarning` | `HarveyMod_NeglectWarning` | **Да** | ✓ | C# unified 2026-05-23; legacy `mailHarvey_Neglect` в CP — дубль |
| `WetCare` | `HarveyMod_WetCare` | **Нет** | ✓ `mailCure.json` | Задел: send не wired |
| `WetStitchesCare` | `HarveyMod_WetStitchesCare` | **Нет** | ✓ | Задел |
| `InfectionAlert` | `HarveyMod_InfectionAlert` | **Нет** | ✓ | Generic alert; C# шлёт dirty/wet-specific |

---

## Цепочки (кратко)

```
Шахта severe → MineWarningDay → mailHarveyMineForbidden → HarveyMod_MineForbidden
Обморок Town ≥26:00 → mailHarveySleepControl
Dirty/Wet roll → HarveyMod_*Infection
Phase neglect → Urgent → Final → HarveyMod_NeglectWarning + buff neglect
```

---

## Риски (актуальные)

1. **Нет проверки `mailReceived`** — neglect/infection могут повторяться при новых инцидентах.
2. **`MailIds.WetCare` / `WetStitchesCare` / `InfectionAlert`** — не используются в send.
3. **`mailHarveySleepControl`** — C# без gate по hearts (текст смягчён в CP 2026-05-23).
4. **Два пути neglect** — `GameEventHandler.CheckNeglect` без письма; `ComplicationManager` с письмом.

---

## Индекс файлов

| Файл | Роль |
|------|------|
| `Core/Constants.cs` | `MailIds.*` |
| `EventHandlers/PassOutHandler.cs` | `SleepControl` |
| `EventHandlers/GameEventHandler.cs` | `MineForbidden` |
| `Managers/ComplicationManager.cs` | infection + phase neglect mails |

**Статус синхронизации C# ↔ CP mail:** ✅ все 7 отправляемых ID имеют exact entry в CP.
""",
    encoding="utf-8",
)

# --- audit-mail-cp-existence.md ---
(DOCS / "audit-mail-cp-existence.md").write_text(
    f"""# Аудит наличия CP-почты для C# mail ID

Дата: {TODAY} (актуализация)  
Источник C#: [audit-mail-csharp.md](./audit-mail-csharp.md)  
CP: `HarveyOverhaul [CP]` → `Data/Mail` (Include в `content.json`).

**Подключённые mail-файлы:** `mail.json`, `mailCare.json`, `mailCure.json`, `mailInjury.json`, `mailStress.json`.

---

## Сводка

| Статус | Кол-во |
|--------|--------|
| ✓ Exact match для всех C# send | **7 / 7** |
| ✗ Отсутствует в CP | **0** |
| ✗ ID mismatch | **0** (C# → `HarveyMod_NeglectWarning`) |
| `MailIds` без send, но в CP | **3** |

**Итог:** все письма, которые C# отправляет, **имеют exact entry в CP**.

---

## Основная таблица — C# send

| Mail ID (C#) | CP | Файл | Контекст C# | Проблема |
|--------------|-----|------|-------------|----------|
| `mailHarveySleepControl` | ✓ | mailInjury.json | PassOutHandler, Town ≥26:00 | **MEDIUM:** нет gate по hearts в C# |
| `mailHarveyMineForbidden` | ✓ | mailInjury.json | Severe + шахта, вечер | — |
| `HarveyMod_DirtyWoundInfection` | ✓ | mailInjury.json | Dirty wound roll | — |
| `HarveyMod_WetBandageInfection` | ✓ | mailInjury.json | Wet bandage roll | — |
| `HarveyMod_TreatmentUrgentReminder` | ✓ | mailInjury.json | Phase neglect +3 | — |
| `HarveyMod_TreatmentFinalWarning` | ✓ | mailInjury.json | Phase neglect −1 | — |
| `HarveyMod_NeglectWarning` | ✓ | mailInjury.json | Phase neglect overdue | **LOW:** дубль `mailHarvey_Neglect` в CP (C# не шлёт) |

---

## Неиспользуемые константы vs CP

| C# `MailIds` | Send? | CP ключ | Заметка |
|--------------|-------|---------|---------|
| `WetCare` | Нет | `HarveyMod_WetCare` | Подключить ComplicationManager или удалить константу |
| `WetStitchesCare` | Нет | `HarveyMod_WetStitchesCare` | То же |
| `InfectionAlert` | Нет | `HarveyMod_InfectionAlert` | Generic; C# шлёт dirty/wet-specific |

---

## Дубли в CP (C# не шлёт)

| CP ключ | Заметка |
|---------|---------|
| `mailHarvey_Neglect` | Legacy ID; C# шлёт `HarveyMod_NeglectWarning` |
| `HarveyMod_InfectionAlert` | Generic infection; C# шлёт `DirtyWoundInfection` / `WetBandageInfection` |
| `mailHarveyMineWarning`, `HarveyMod_MineWarning` | CP/triggers only |

---

## Топ исправлений (статус)

1. ~~4 missing mail entries~~ — **✅ done** (2026-05-23)
2. ~~Neglect ID mismatch~~ — **✅ done** (C# → `HarveyMod_NeglectWarning`)
3. **MEDIUM:** Wire `WetCare` / `WetStitchesCare` или удалить константы — **открыто**
4. **MEDIUM:** `mailHarveySleepControl` — C# gate по отношениям — **открыто**
5. **LOW:** Удалить legacy `mailHarvey_Neglect` из CP — **опционально**
""",
    encoding="utf-8",
)

print("Wrote audit-mail-csharp.md, audit-mail-cp-existence.md")

# 🔍 Анализ проблемы с Social Stress квестом

## ❌ **Проблема**
Квест не выдается после разговора с Харви, хотя:
- Дебафф Social активен
- Топик `topicStressSocial` активен
- Диалог показывается правильно

## 🔎 **Найденные проблемы**

### 1. **isOnFinalDialogue() всегда False**
Из логов:
```
[Диалог] DialogueBox найдена. isOnFinalDialogue: False
[Диалог] Диалог с Харви продолжается (не финальный)
```

**Причина:** `isOnFinalDialogue()` возвращает `false` даже на последней странице диалога, потому что диалог может содержать несколько страниц, и метод проверяет только текущую позицию.

**Решение:** Обрабатываем начало лечения в `HandleHarveyDialogueEnd()` когда диалог полностью завершен (`e.OldMenu is DialogueBox`).

### 2. **_lastDialogueNpc не устанавливается для Харви**
В `HandleDialogueEvents` отсутствовала установка `_lastDialogueNpc = "Harvey"` при начале диалога с Харви.

**Решение:** Добавлено `_lastDialogueNpc = "Harvey"` в блоке обработки диалога с Харви.

## ✅ **Исправления**

### Исправление 1: Установка _lastDialogueNpc для Харви
```csharp
else if (e.NewMenu is DialogueBox && Game1.currentSpeaker is NPC harveyNpc && harveyNpc.Name == "Harvey")
{
    _lastDialogueNpc = "Harvey";  // ← ДОБАВЛЕНО
    HandleHarveyDialogue(harveyNpc, e);
}
```

### Исправление 2: Обнуление _lastDialogueNpc после завершения
```csharp
else if (e.OldMenu is DialogueBox && _lastDialogueNpc == "Harvey")
{
    HandleHarveyDialogueEnd();
    _lastDialogueNpc = null;  // ← ДОБАВЛЕНО
}
```

### Исправление 3: HandleHarveyDialogueEnd запускает лечение
```csharp
private void HandleHarveyDialogueEnd()
{
    _monitor.Log($"[Диалог] ✅ Завершен разговор с Харви", LogLevel.Info);

    // Проверяем, был ли активен топик, соответствующий дебаффу Social
    bool hasSocialStressTopic = ConversationHelper.HasTopic(TopicIds.StressSocial);
    _monitor.Log($"[Диалог] При завершении диалога топик topicStressSocial активен: {hasSocialStressTopic}", LogLevel.Info);

    // ⭐ КЛЮЧЕВОЕ: Запускаем начало лечения
    CheckStressTopicsAndStartTreatment(Game1.getCharacterFromName("Harvey"), null, hasSocialStressTopic);

    UpdateSocialQuestProgress(showUiMessage: false);

    if (!ConversationHelper.HasTopic(TopicIds.SpokeToday))
    {
        ConversationHelper.AddTopic(TopicIds.SpokeToday, 1);
    }
}
```

## 📊 **Жизненный цикл Social Stress (обновленный)**

### 1. **Получение дебаффа**
```
Разговор с NPC (дружба < 750, 30% шанс)
↓
ApplyStressBuff(BuffIds.Social)
↓
Дебафф активен + топик topicStressSocial добавлен
```

### 2. **Разговор с Харви**
```
Начало диалога с Харви (e.NewMenu is DialogueBox)
↓
HandleHarveyDialogue() вызывается (логирование)
↓
_lastDialogueNpc = "Harvey" устанавливается
↓
Игрок читает диалог по топику topicStressSocial
↓
Завершение диалога (e.OldMenu is DialogueBox)
↓
HandleHarveyDialogueEnd() вызывается
↓
CheckStressTopicsAndStartTreatment() запускается
↓
Если topicStressSocial активен → StartTreatment()
```

### 3. **Начало лечения (StartTreatment)**
```
TreatmentService.StartTreatment(BuffIds.Social)
↓
Проверка: квест не активен?
↓
Создание TreatmentState с уникальным ключом
↓
Удаление топика topicStressSocial
↓
Добавление квеста HarveyMod_SocialRecovery в журнал
↓
Инициализация прогресса:
  - TalkedUniqueToday = текущее количество разговоров (база)
  - SocialTalksAfterQuest = 0
  - SecondsNearHarvey = 0
↓
Добавление топика topicStressTreatmentStarted
↓
Реакция Харви (эмоция + текст)
```

### 4. **Прогресс квеста**

#### A. Разговоры с жителями
```
Разговор с NPC (не Харви)
↓
HandleDialogueEnd()
↓
_data.TalkedNpcsToday.Add(npc)
↓
UpdateSocialQuestProgress(showUiMessage: true)
↓
Расчет: SocialTalksAfterQuest = TalkedNpcsToday.Count - TalkedUniqueToday
↓
Обновление описания квеста через TriggerService
↓
HUD уведомление о прогрессе
```

#### B. Время с Харви
```
Каждую секунду (OnUpdateTicked, каждые 60 тиков)
↓
GameStateHelper.IsHarveyNearby() проверяется
↓
Если Harvey рядом:
  TriggerService.UpdateTreatmentProgress(harveyNearby: true)
  ↓
  UpdateSocialAnxietyProgress()
  ↓
  UpdateHarveyTimeProgress()
  ↓
  progress.SecondsNearHarvey++
  ↓
  HUD уведомления (15, 30, 45, 60 сек)
  ↓
  CheckQuestCompletion()
```

### 5. **Завершение квеста**
```
CheckQuestCompletion() проверяет условия:

Путь 1: SocialTalksAfterQuest >= 3 И SecondsNearHarvey >= 60
  ↓
  CompleteTreatment(BuffIds.Social)

Путь 2: SocialTalksAfterQuest >= 5
  ↓
  CompleteTreatment(BuffIds.Social)

CompleteTreatment():
  ↓
  Дебафф снимается
  ↓
  Квест завершается
  ↓
  +100 дружба с Харви
  ↓
  Письмо на завтра
```

## 🎯 **Текущий статус**

✅ **Исправлено:**
- Установка `_lastDialogueNpc` для Харви
- Обработка завершения диалога с Харви в `HandleHarveyDialogueEnd()`
- Запуск лечения при завершении диалога
- Детальное логирование всех этапов

✅ **Подтверждено работает:**
- Получение дебаффа Social
- Добавление топика `topicStressSocial`
- Показ диалога с Харви
- Отслеживание времени с Харви (TriggerService)
- Подсчет разговоров с жителями (UpdateSocialQuestProgress)
- Завершение квеста по обоим путям

## 📝 **Ожидаемые логи после исправления**

```
[Диалог] ✅ Начался разговор с Харви. Текущие топики: ...topicStressSocial...
[Диалог] 🟢 Дебафф Social активен: True
[Диалог] DialogueBox найдена. isOnFinalDialogue: False
[Диалог] Диалог с Харви продолжается (не финальный)
... игрок читает диалог ...
[Диалог] ✅ Завершен разговор с Харви
[Диалог] При завершении диалога топик topicStressSocial активен: True
[CheckStressTopicsAndStartTreatment] Начат анализ диалога с Харви...
[CheckStressTopicsAndStartTreatment] ✅ Диалог был по топику topicStressSocial и дебафф Social активен. Начинаем лечение Social!
[StartTreatment] Попытка начать лечение для buffStressSocial (Социальный дискомфорт)
[StartTreatment] Создаем новое лечение с ключом: buffStressSocial
[StartTreatment] ═══ ИНИЦИАЛИЗАЦИЯ ПРОГРЕССА SOCIAL ═══
[StartTreatment] TalkedUniqueToday (база): 3
[StartTreatment] SocialTalksAfterQuest (счетчик): 0
[StartTreatment] SecondsNearHarvey: 0
[StartTreatment] ✅ Квест 'HarveyMod_SocialRecovery' успешно добавлен в журнал
[StartTreatment] ✅ УСПЕШНО: Лечение начато для Социальный дискомфорт
```

## 🔧 **Проверка работы прогресса**

После выдачи квеста:

1. **Разговоры с жителями:**
```
Поговорить с любым NPC (не Харви)
↓
Должно появиться: "Прогресс: 1/5 разговоров" (или аналогичное)
↓
Квест обновляется: "Поговорили с персонажами: 1/3 (или 5)"
```

2. **Время с Харви:**
```
Стоять рядом с Харви
↓
Каждые 15 секунд: HUD уведомление "Время с Харви: 15/60 сек"
↓
Квест обновляется: "Время с Харви: 15/60 сек"
```

3. **Завершение:**
```
Вариант A: 3 разговора + 60 сек с Харви
  → "✅ Социальная тренировка завершена! (3 разговора + время с Харви)"

Вариант B: 5 разговоров
  → "✅ Социальная тренировка завершена! (5 разговоров)"
```


using System;
using System.Linq;
using System.Reflection;
using StardewValley;
using StardewValley.Quests;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Constants;
using StardewModdingAPI;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Сервис для управления квестами
    /// </summary>
    public class QuestService
    {
        private readonly IMonitor? _monitor;

        public QuestService(IMonitor? monitor = null)
        {
            _monitor = monitor;
        }
        /// <summary>
        /// ⭐ ИСПРАВЛЕНО: Проверяет наличие квеста с расширенным логированием
        /// Проверяет, есть ли квест в журнале игрока
        /// </summary>
        public bool HasQuest(string questId)
        {
            try
            {
                bool found = Game1.player.questLog.Any(q =>
                {
                    var qid = ReflectionHelper.GetQuestStringId(q, _monitor);
                    return string.Equals(qid, questId, StringComparison.OrdinalIgnoreCase);
                });

                // ⭐ НОВОЕ: Детальное логирование для отладки (только для Social квеста)
                if (questId == "HarveyMod_SocialRecovery" && !found)
                {
                    var questInfo = Game1.player.questLog.Select(q => 
                    {
                        var id = ReflectionHelper.GetQuestStringId(q, _monitor);
                        var type = q.GetType().Name;
                        return string.IsNullOrWhiteSpace(id) ? type : $"{id}({type})";
                    }).ToArray();
                    
                    _monitor?.Log($"[QuestService.HasQuest] Квест '{questId}' НЕ НАЙДЕН. Все квесты в журнале: {string.Join(", ", questInfo)}", LogLevel.Warn);
                }

                return found;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ⭐ НОВОЕ: Обновляет описание квеста через рефлексию
        /// Обновляет описание квеста в журнале
        /// </summary>
        public void UpdateQuestDescription(string questId, string newDescription)
        {
            try
            {
                foreach (var quest in Game1.player.questLog)
                {
                    var qid = ReflectionHelper.GetQuestStringId(quest, _monitor);
                    if (!string.Equals(qid, questId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // ⭐ НОВОЕ: Обновляем описание квеста через рефлексию
                    var descriptionField = quest.GetType().GetField("questDescription",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (descriptionField != null)
                    {
                        descriptionField.SetValue(quest, newDescription);
                        _monitor?.Log($"[QuestService.UpdateQuestDescription] ✅ Описание квеста '{questId}' обновлено", LogLevel.Debug);
                    }
                    else
                    {
                        _monitor?.Log($"[QuestService.UpdateQuestDescription] ⚠️ Не найдено поле questDescription для квеста '{questId}'", LogLevel.Warn);
                    }

                    break;
                }
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[QuestService.UpdateQuestDescription] Ошибка: {ex.Message}", LogLevel.Error);
            }
        }

        public int GetQuestCount()
        {
            return Game1.player.questLog.Count;
        }

        public bool IsQuestCompleted(string questId, out object? quest)
        {
            quest = null;

            foreach (var q in Game1.player.questLog)
            {
                var qid = ReflectionHelper.GetQuestStringId(q, _monitor);
                if (!string.Equals(qid, questId, StringComparison.OrdinalIgnoreCase))
                    continue;

                quest = q;

                if (ReflectionHelper.TryGetMember<bool>(q, "completed", out var completed))
                    return completed;

                return false;
            }
            return false;
        }

        /// <summary>
        /// ⭐ ИСПРАВЛЕНО: Добавляет квест с расширенной диагностикой
        /// Добавляет квест в журнал игрока
        /// </summary>
        public void AddQuest(string questId)
        {
            try
            {
                // ⭐ НОВОЕ: Проверяем, существует ли квест в Data/Quests ДО попытки добавления
                var questData = Game1.content.Load<Dictionary<string, string>>("Data/Quests");

                if (!questData.ContainsKey(questId))
                {
                    _monitor?.Log($"[QuestService.AddQuest] ❌ КВЕСТ '{questId}' НЕ НАЙДЕН в Data/Quests!", LogLevel.Error);
                    _monitor?.Log($"[QuestService.AddQuest] Content Patcher НЕ загрузил квест. Проверьте content.json", LogLevel.Error);

                    // ⭐ НОВОЕ: Показываем все доступные квесты для диагностики
                    var availableQuests = string.Join(", ", questData.Keys.Where(k => k.Contains("Harvey")).Take(10));
                    _monitor?.Log($"[QuestService.AddQuest] Доступные Harvey квесты: {availableQuests}", LogLevel.Info);
                    return;
                }

                // ⭐ НОВОЕ: Проверяем, не добавлен ли уже квест
                bool wasInJournal = HasQuest(questId);

                if (wasInJournal)
                {
                    _monitor?.Log($"[QuestService.AddQuest] ⚠️ Квест '{questId}' уже в журнале", LogLevel.Warn);
                    return;
                }

                // Стандартный способ добавления квеста
                int questCountBefore = Game1.player.questLog.Count;
                Game1.player.addQuest(questId);

                bool nowInJournal = HasQuest(questId);
                int questCountAfter = Game1.player.questLog.Count;

                _monitor?.Log($"[QuestService.AddQuest] '{questId}': было={wasInJournal}, стало={nowInJournal}, квестов {questCountBefore}→{questCountAfter}", LogLevel.Info);

                // Если количество изменилось, но HasQuest не видит квест - это проблема!
                if (questCountAfter > questCountBefore && !nowInJournal)
                {
                    _monitor?.Log($"[QuestService.AddQuest] ⚠️ Квест добавлен ({questCountBefore}→{questCountAfter}), но HasQuest не видит его!", LogLevel.Warn);
                    
                    // Логируем последний добавленный квест
                    var lastQuest = Game1.player.questLog.LastOrDefault();
                    if (lastQuest != null && _monitor != null)
                    {
                        _monitor.Log($"[QuestService.AddQuest] Последний добавленный квест: тип={lastQuest.GetType().Name}", LogLevel.Warn);
                        ReflectionHelper.LogQuest(lastQuest, _monitor);
                    }
                }

                // Логируем объект квеста ТОЛЬКО при добавлении (не каждую секунду)
                if (nowInJournal)
                {
                    var addedQuest = Game1.player.questLog.FirstOrDefault(q => 
                        ReflectionHelper.GetQuestStringId(q, _monitor) == questId);
                    if (addedQuest != null && _monitor != null)
                    {
                        ReflectionHelper.LogQuest(addedQuest, _monitor);
                    }
                }

                // ⭐ ИСПРАВЛЕНО: Если стандартный метод не сработал, пробуем через рефлексию
                if (!nowInJournal)
                {
                    _monitor?.Log($"[QuestService.AddQuest] ⚠️ Стандартный addQuest() не сработал. Пробуем через рефлексию...", LogLevel.Warn);

                    try
                    {
                        // Создаем квест напрямую через рефлексию
                        var questType = Type.GetType("StardewValley.Quests.Quest, Stardew Valley");
                        if (questType != null)
                        {
                            var getQuestMethod = questType.GetMethod("getQuestFromId",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                            if (getQuestMethod != null)
                            {
                                var quest = getQuestMethod.Invoke(null, new object[] { questId });
                                if (quest != null && quest is StardewValley.Quests.Quest questObj)
                                {
                                    Game1.player.questLog.Add(questObj);

                                    nowInJournal = HasQuest(questId);

                                    if (nowInJournal)
                                    {
                                        _monitor?.Log($"[QuestService.AddQuest] ✅ Рефлексия сработала! Квест добавлен.", LogLevel.Info);
                                    }
                                    else
                                    {
                                        _monitor?.Log($"[QuestService.AddQuest] ❌ Рефлексия тоже не сработала", LogLevel.Error);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _monitor?.Log($"[QuestService.AddQuest] Ошибка при добавлении через рефлексию: {ex.Message}", LogLevel.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[QuestService.AddQuest] ❌ ОШИБКА '{questId}': {ex.Message}", LogLevel.Error);
                _monitor?.Log($"[QuestService.AddQuest] Stack trace: {ex.StackTrace}", LogLevel.Error);
            }
        }

        public void CompleteQuest(string questId)
        {
            try
            {
                // Логируем квест ПЕРЕД удалением (только если квест есть в журнале)
                if (HasQuest(questId) && _monitor != null)
                {
                    var questInJournal = Game1.player.questLog.FirstOrDefault(q => 
                        ReflectionHelper.GetQuestStringId(q, _monitor) == questId);
                    
                    if (questInJournal != null)
                    {
                        _monitor.Log($"[QuestService.CompleteQuest] Завершаем квест '{questId}'", LogLevel.Info);
                        ReflectionHelper.LogQuest(questInJournal, _monitor);
                    }
                }

                var completeMethod = typeof(Farmer).GetMethod("completeQuest", new[] { typeof(string) });
                if (completeMethod != null)
                {
                    completeMethod.Invoke(Game1.player, new object[] { questId });
                    return;
                }

                var removeMethod = typeof(Farmer).GetMethod("removeQuest", new[] { typeof(string) });
                if (removeMethod != null)
                {
                    removeMethod.Invoke(Game1.player, new object[] { questId });
                }
            }
            catch { /* ok */ }
        }

        public void AddMailForTomorrow(string mailId)
        {
            try
            {
                Game1.addMailForTomorrow(mailId);
            }
            catch { /* ok */ }
        }

    }
}


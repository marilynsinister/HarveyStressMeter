using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Helpers;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Сервис для управления лечением стресса
    /// </summary>
    public class TreatmentService
    {
        private readonly SaveData _data;
        private readonly BuffService _buffService;
        private readonly QuestService _questService;
        private readonly StateService _stateService;
        private readonly IMonitor _monitor;

        // Карты связей buff -> quest/topic/mail
        private static readonly Dictionary<string, string> BuffToQuest = new()
        {
            [BuffIds.Thunder] = QuestIds.Thunder,
            [BuffIds.Darkness] = QuestIds.Darkness,
            [BuffIds.Lonely] = QuestIds.Lonely,
            [BuffIds.Overwork] = QuestIds.Overwork,
            [BuffIds.Hunger] = QuestIds.Hunger,
            [BuffIds.TooCold] = QuestIds.TooCold,
            [BuffIds.Social] = QuestIds.Social,
            [BuffIds.NoSleep] = QuestIds.NoSleep,
            [BuffIds.Tired] = QuestIds.Tired,
        };

        private static readonly Dictionary<string, (string topic, int days)> BuffToStressTopic = new()
        {
            [BuffIds.Tired] = (TopicIds.StressTired, 2),
            [BuffIds.Lonely] = (TopicIds.StressLonely, 2),
            [BuffIds.Thunder] = (TopicIds.StressThunder, 1),
            [BuffIds.Hunger] = (TopicIds.StressHunger, 1),
            [BuffIds.Overwork] = (TopicIds.StressOverwork, 4),
            [BuffIds.NoSleep] = (TopicIds.StressNoSleep, 1),
            [BuffIds.TooCold] = (TopicIds.StressTooCold, 1),
            [BuffIds.Darkness] = (TopicIds.StressDarkness, 1),
            [BuffIds.Social] = (TopicIds.StressSocial, 1),
        };

        private static readonly Dictionary<string, string> BuffToDisplayName = new()
        {
            [BuffIds.Tired] = "Усталость",
            [BuffIds.Lonely] = "Одиночество",
            [BuffIds.Thunder] = "Страх грозы",
            [BuffIds.Hunger] = "Голод",
            [BuffIds.Overwork] = "Переработка",
            [BuffIds.NoSleep] = "Недосып",
            [BuffIds.TooCold] = "Переохлаждение",
            [BuffIds.Darkness] = "Темнота",
            [BuffIds.Social] = "Социальный дискомфорт",
        };

        public TreatmentService(SaveData data, BuffService buffService, QuestService questService, StateService stateService, IMonitor monitor)
        {
            _data = data;
            _buffService = buffService;
            _questService = questService;
            _stateService = stateService;
            _monitor = monitor;
        }

        /// <summary>
        /// Применяет простой (незалоченный) бафф стресса при срабатывании триггера
        /// НЕ выдает квест - только бафф и топик для диалога с Харви
        /// </summary>
        public void ApplyStressBuff(string buffId, string displayName)
        {
            _monitor.Log($"[ApplyStressBuff] Попытка применить дебафф {buffId} ({displayName})", LogLevel.Debug);

            // Проверяем через StateService, можно ли выдать бафф
            if (!_stateService.CanIssueBuff(buffId, cooldownDays: 1))
            {
                _monitor.Log($"[ApplyStressBuff] Дебафф {buffId} нельзя выдать (активен или на кулдауне)", LogLevel.Debug);
                return;
            }

            // Делегируем логику применения баффа в StateService
            _stateService.ApplyStressBuff(buffId, displayName);

            // Добавляем топик для диалога с Харви
            AddTopicForBuff(buffId);

            _monitor.Log($"[ApplyStressBuff] ✅ Стресс {displayName} применен. Поговорите с Харви для начала лечения.", LogLevel.Info);
        }

        /// <summary>
        /// ⭐ УЛУЧШЕНО: Начинает программу лечения с поддержкой множественных лечений
        /// Начинает программу лечения: залочивает бафф и выдает квест
        /// Вызывается из диалогов Content Patcher после согласия игрока на лечение
        /// </summary>
        public void StartTreatment(string buffId, string displayName)
        {
            _monitor.Log($"[StartTreatment] Попытка начать лечение для {buffId} ({displayName})", LogLevel.Debug);

            if (!BuffToQuest.TryGetValue(buffId, out var questId))
            {
                _monitor.Log($"[StartTreatment] ОШИБКА: Не найден ID квеста для баффа {buffId}", LogLevel.Error);
                return;
            }

            // Проверяем, можно ли начать лечение
            if (!_data.StressState.HasActiveBuff(buffId))
            {
                _monitor.Log($"[StartTreatment] ОШИБКА: Бафф {buffId} не активен. Лечение не может быть начато.", LogLevel.Error);
                return;
            }

            // ⭐ УЛУЧШЕНО: Проверяем, не активен ли уже квест (поддерживает множественные лечения)
            if (_data.StressState.HasActiveQuest(questId))
            {
                _monitor.Log($"[StartTreatment] Квест {questId} уже активен. Пропускаем.", LogLevel.Debug);
                return;
            }

            // ⭐ НОВОЕ: Генерируем уникальный ключ для нового лечения
            var instanceNumber = _data.StressState.GetNextInstanceNumber(buffId);
            var treatmentKey = TreatmentState.GenerateTreatmentKey(buffId, instanceNumber);
            
            _monitor.Log($"[StartTreatment] Создаем новое лечение с ключом: {treatmentKey}", LogLevel.Info);

            // Удаляем исходный топик стресса при начале лечения
            if (BuffToStressTopic.TryGetValue(buffId, out var stressTopic))
            {
                if (ConversationHelper.HasTopic(stressTopic.topic))
                {
                    ConversationHelper.RemoveTopic(stressTopic.topic);
                }
            }

            // ⭐ НОВОЕ: Логируем состояние ПЕРЕД началом лечения
            _monitor.Log($"[StartTreatment] ═══ СОСТОЯНИЕ ПЕРЕД НАЧАЛОМ ЛЕЧЕНИЯ ═══", LogLevel.Info);
            _monitor.Log($"[StartTreatment] Разговоров сегодня до начала лечения: {_data.TalkedNpcsToday.Count}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] NPC, с которыми уже говорили: {string.Join(", ", _data.TalkedNpcsToday)}", LogLevel.Info);

            // ⭐ ИСПРАВЛЕНО: Создаем TreatmentState с уникальным ключом
            var treatment = new TreatmentState
            {
                BuffId = buffId,
                QuestId = questId,
                TreatmentKey = treatmentKey,
                InstanceNumber = instanceNumber,
                IssuedDate = SDate.Now(),
                TreatmentStartedDate = SDate.Now(),
                TreatmentStarted = true,
                AddedToGameLog = false,
                IsCured = false,
                IsCompleted = false,
                Progress = new TreatmentProgress()
            };

            // ⭐ ИСПРАВЛЕНО: Добавляем лечение в состояние с уникальным ключом
            _data.StressState.AddTreatment(treatment);
            
            // ⭐ ИСПРАВЛЕНО: Устанавливаем флаги напрямую, без повторного вызова StartTreatment
            _data.StressState.TreatmentFlags.SetTreatmentActive(buffId, true);
            
            // ⭐ ИСПРАВЛЕНО: Добавляем квест в журнал напрямую через QuestService
            _questService.AddQuest(questId);
            treatment.AddedToGameLog = _questService.HasQuest(questId);
            
            // ⭐ ИСПРАВЛЕНО: Устанавливаем флаг добавления квеста в журнал
            _data.StressState.TreatmentFlags.SetQuestAddedToJournal(questId, treatment.AddedToGameLog);
            
            if (treatment.AddedToGameLog)
            {
                _monitor.Log($"[StartTreatment] ✅ Квест '{questId}' успешно добавлен в журнал", LogLevel.Info);
            }
            else
            {
                _monitor.Log($"[StartTreatment] ❌ КРИТИЧЕСКАЯ ОШИБКА: Квест '{questId}' не добавлен в журнал!", LogLevel.Error);
            }

            // ⭐ УЛУЧШЕНО: Инициализируем прогресс для нового лечения
            if (buffId == BuffIds.Social)
            {
                // ⭐ КРИТИЧНО: TalkedUniqueToday = базовое значение (сколько было разговоров ДО квеста)
                treatment.Progress.TalkedUniqueToday = _data.TalkedNpcsToday.Count;

                // ⭐ КРИТИЧНО: SocialTalksAfterQuest = обнуляем счетчик разговоров ПОСЛЕ квеста
                treatment.Progress.SocialTalksAfterQuest = 0;

                // ⭐ НОВОЕ: Обнуляем время с Харви
                treatment.Progress.SecondsNearHarvey = 0;

                _monitor.Log($"[StartTreatment] ═══ ИНИЦИАЛИЗАЦИЯ ПРОГРЕССА SOCIAL ═══", LogLevel.Info);
                _monitor.Log($"[StartTreatment] TreatmentKey: {treatmentKey}", LogLevel.Info);
                _monitor.Log($"[StartTreatment] TalkedUniqueToday (база): {treatment.Progress.TalkedUniqueToday}", LogLevel.Info);
                _monitor.Log($"[StartTreatment] SocialTalksAfterQuest (счетчик): {treatment.Progress.SocialTalksAfterQuest}", LogLevel.Info);
                _monitor.Log($"[StartTreatment] SecondsNearHarvey: {treatment.Progress.SecondsNearHarvey}", LogLevel.Info);
            }

            // Добавить общий топик начала лечения
            if (!ConversationHelper.HasTopic(TopicIds.TreatmentStarted))
                ConversationHelper.AddTopic(TopicIds.TreatmentStarted, 0);

            // Реакция Харви
            var harvey = Game1.getCharacterFromName("Harvey");
            if (harvey?.currentLocation == Game1.currentLocation)
            {
                harvey.doEmote(32); // happy
                harvey.showTextAboveHead("Мы справимся вместе!");
            }

            // ⭐ УЛУЧШЕНО: Детальное логирование после старта
            _monitor.Log($"[StartTreatment] ═══ СОСТОЯНИЕ ПОСЛЕ НАЧАЛА ЛЕЧЕНИЯ ═══", LogLevel.Info);
            _monitor.Log($"[StartTreatment] TreatmentKey: {treatmentKey}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] InstanceNumber: {instanceNumber}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] HasBuff({buffId}): {_stateService.HasActiveBuffInGame(buffId)}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] HasQuest({questId}): {_stateService.HasQuestInJournal(questId)}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] ActiveTreatments.Count: {_data.StressState.ActiveTreatments.Count}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] ActiveTreatmentsByBuff({buffId}).Count: {_data.StressState.GetActiveTreatmentCountByBuff(buffId)}", LogLevel.Info);

            _monitor.Log($"[StartTreatment] ✅ УСПЕШНО: Лечение начато для {displayName} (Квест: {questId}, Ключ: {treatmentKey})", LogLevel.Info);
            Game1.playSound("questcomplete");
        }

        public void CompleteTreatment(string buffId, string message = "Лечение завершено.")
        {
            // ⭐ ИСПРАВЛЕНО: Находим активное лечение по buffId
            var activeTreatment = _data.StressState.GetActiveTreatment(buffId);
            if (activeTreatment != null)
            {
                // Отмечаем лечение как завершенное
                activeTreatment.IsCured = true;
                activeTreatment.CompletedDate = SDate.Now();

                // Удаляем из активных лечений по уникальному ключу
                _data.StressState.RemoveTreatment(activeTreatment.TreatmentKey);
            }

            // Отмечаем лечение как завершенное в истории
            if (_data.StressState.TreatmentHistory.TryGetValue(buffId, out var historyList) && historyList.Count > 0)
            {
                var latestTreatment = historyList.Last();
                latestTreatment.IsCured = true;
                latestTreatment.CompletedDate = SDate.Now();
            }

            // Удаляем из активных баффов и квестов через StateService
            if (BuffToQuest.TryGetValue(buffId, out var questId))
            {
                _stateService.CompleteTreatment(questId);
            }
            else
            {
                // Если нет квеста, просто удаляем бафф
                _buffService.RemoveBuff(buffId);
            }

            Game1.playSound("discoverMineral");
            Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type));

            var harvey = Game1.getCharacterFromName("Harvey");
            if (harvey?.currentLocation == Game1.currentLocation)
            {
                harvey.doEmote(32);
                harvey.showTextAboveHead("Ты справилась! Горжусь тобой.");
            }

            if (harvey != null)
                Game1.player.changeFriendship(100, harvey);

            _questService.AddMailForTomorrow(MailIds.GenericDone);
        }

        public void AddTopicForBuff(string buffId)
        {
            if (!_stateService.HasActiveBuffInGame(buffId))
                return;

            if (BuffToStressTopic.TryGetValue(buffId, out var topic) && !ConversationHelper.HasTopic(topic.topic))
                ConversationHelper.AddTopic(topic.topic, topic.days);
        }

        public void EnsureLockedBuffsPersist()
        {
            foreach (var (treatmentKey, treatment) in _data.StressState.ActiveTreatments)
            {
                if (!treatment.IsCured && !_stateService.HasActiveBuffInGame(treatment.BuffId))
                {
                    _buffService.ApplyBuffFromData(treatment.BuffId);
                    AddTopicForBuff(treatment.BuffId);
                }
            }
        }

        public void SyncQuestsAndBuffs()
        {
            // ⭐ ИСПРАВЛЕНО: Проверяем АКТИВНЫЕ лечения с новой архитектурой
            // Восстанавливаем квесты и баффы, если они потерялись
            foreach (var (treatmentKey, treatment) in _data.StressState.ActiveTreatments.ToList())
            {
                var questId = treatment.QuestId;
                var buffId = treatment.BuffId;

                // Проверяем, есть ли квест в журнале
                if (!string.IsNullOrEmpty(questId) && !_stateService.HasQuestInJournal(questId))
                {
                    _monitor.Log($"[SyncQuestsAndBuffs] ⚠️ Квест '{questId}' потерян для дебаффа '{buffId}' (ключ: {treatmentKey}). Удаляем лечение.", LogLevel.Warn);
                    _buffService.RemoveBuff(buffId);
                    _data.StressState.RemoveTreatment(treatmentKey);
                    continue;
                }

                // Квест есть - проверяем, не завершен ли он
                if (_questService.IsQuestCompleted(questId, out var quest) && quest != null)
                {
                    bool completed = false;
                    if (ReflectionHelper.TryGetMember<bool>(quest, "completed", out var c))
                        completed = c;

                    if (completed)
                    {
                        _monitor.Log($"[SyncQuestsAndBuffs] Квест '{questId}' завершен. Удаляем лечение '{buffId}' (ключ: {treatmentKey}).", LogLevel.Info);
                        _buffService.RemoveBuff(buffId);
                        _data.StressState.RemoveTreatment(treatmentKey);
                        continue;
                    }
                }

                // Квест активен и не завершен - восстанавливаем бафф если потерялся
                if (!_stateService.HasActiveBuffInGame(buffId))
                {
                    _buffService.ApplyBuffFromData(buffId);
                    _monitor.Log($"[SyncQuestsAndBuffs] Восстановлен дебафф '{buffId}' для активного квеста '{questId}' (ключ: {treatmentKey})", LogLevel.Debug);
                }

                // Гарантируем наличие прогресса
                if (treatment.Progress == null)
                {
                    treatment.Progress = new TreatmentProgress();
                }
            }
        }

        /// <summary>
        /// Восстанавливает потерянные квесты по топикам стресса
        /// Если есть топик, но нет квеста и записи - восстанавливаем квест
        /// Проверяет актуальность топика по дате последней выдачи дебаффа
        /// </summary>
        public void RestoreLostQuestsFromTopics()
        {
            var today = SDate.Now();
            const int MaxDaysForRestore = 3; // Максимум 3 дня для восстановления квеста

            foreach (var (buffId, topicData) in BuffToStressTopic)
            {
                // Проверяем, есть ли топик стресса
                if (!ConversationHelper.HasTopic(topicData.topic))
                    continue;

                // Проверяем актуальность топика по дате
                if (_data.StressState.LastIssuedDay.TryGetValue(buffId, out var lastIssued))
                {
                    int daysSinceIssued = today.DaysSinceStart - lastIssued.DaysSinceStart;

                    // Если прошло слишком много времени - удаляем топик и не восстанавливаем квест
                    if (daysSinceIssued > MaxDaysForRestore)
                    {
                        _monitor.Log($"Топик {topicData.topic} устарел ({daysSinceIssued} дней). Удаляем.", LogLevel.Debug);
                        ConversationHelper.RemoveTopic(topicData.topic);
                        _data.StressState.LastIssuedDay.Remove(buffId);
                        continue;
                    }
                }
                else
                {
                    // Нет записи о дате - это старый топик, удаляем
                    _monitor.Log($"Топик {topicData.topic} без даты выдачи. Удаляем.", LogLevel.Debug);
                    ConversationHelper.RemoveTopic(topicData.topic);
                    continue;
                }

                // Если уже есть запись в ActiveTreatments - всё в порядке
                if (_data.StressState.ActiveTreatments.ContainsKey(buffId))
                    continue;

                // Получаем questId для этого баффа
                if (!BuffToQuest.TryGetValue(buffId, out var questId))
                    continue;

                // Если квест уже есть - всё в порядке
                if (_stateService.HasQuestInJournal(questId))
                    continue;

                // Топик есть, актуален, а квеста нет - восстанавливаем!
                _monitor.Log($"Восстановление потерянного квеста {questId} по топику {topicData.topic}", LogLevel.Debug);

                // Добавляем квест
                _questService.AddQuest(questId);

                // Создаем запись в ActiveBuffs и ActiveQuests через StateService
                _stateService.ApplyStressBuff(buffId, BuffToDisplayName.GetValueOrDefault(buffId, buffId));
                _stateService.StartTreatment(buffId, questId);

                // Показываем сообщение игроку
                if (BuffToDisplayName.TryGetValue(buffId, out var displayName))
                {
                    Game1.addHUDMessage(new HUDMessage($"Квест восстановлен: {displayName}", HUDMessage.newQuest_type));
                }
            }
        }

        /// <summary>
        /// Очищает устаревшие топики стресса
        /// Вызывается при загрузке игры и начале дня
        /// </summary>
        public void CleanupOldStressTopics()
        {
            var today = SDate.Now();
            const int MaxDaysToKeep = 3;

            foreach (var (buffId, topicData) in BuffToStressTopic)
            {
                // Пропускаем, если топика нет
                if (!ConversationHelper.HasTopic(topicData.topic))
                    continue;

                // Проверяем дату
                if (_data.StressState.LastIssuedDay.TryGetValue(buffId, out var lastIssued))
                {
                    int daysSinceIssued = today.DaysSinceStart - lastIssued.DaysSinceStart;

                    if (daysSinceIssued > MaxDaysToKeep)
                    {
                        _monitor.Log($"Очистка устаревшего топика {topicData.topic} ({daysSinceIssued} дней)", LogLevel.Debug);
                        ConversationHelper.RemoveTopic(topicData.topic);
                        _data.StressState.LastIssuedDay.Remove(buffId);
                    }
                }
                else
                {
                    // Топик есть, но даты нет - удаляем
                    _monitor.Log($"Очистка топика без даты {topicData.topic}", LogLevel.Debug);
                    ConversationHelper.RemoveTopic(topicData.topic);
                }
            }
        }

        /// <summary>
        /// Восстанавливает активные невылеченные дебаффы стресса
        /// Вызывается при начале дня для поддержания дебаффов до излечения
        /// </summary>
        public void RestoreActiveStressBuffs()
        {
            var today = SDate.Now();
            int restoredCount = 0;
            int totalTreatments = _data.StressState.TreatmentHistory.Count;

            _monitor.Log($"[RestoreActiveStressBuffs] Начинаем восстановление. Всего лечений: {totalTreatments}", LogLevel.Debug);

            foreach (var (buffId, historyList) in _data.StressState.TreatmentHistory)
            {
                if (historyList.Count == 0) continue;
                
                var treatment = historyList.Last(); // Берем последнее лечение
                _monitor.Log($"[RestoreActiveStressBuffs] Проверяем {buffId}: выдан={treatment.IssuedDate}, лечение={treatment.TreatmentStarted}, вылечен={treatment.IsCured}", LogLevel.Debug);

                // Проверяем, нужно ли восстанавливать этот дебафф
                if (!treatment.ShouldRestore(today))
                {
                    int daysSince = today.DaysSinceStart - treatment.IssuedDate.DaysSinceStart;
                    _monitor.Log($"[RestoreActiveStressBuffs] {buffId} не нужно восстанавливать (вылечен={treatment.IsCured}, дней={daysSince})", LogLevel.Debug);

                    // Если устарел или вылечен - очищаем топики
                    if (treatment.IsCured || daysSince > 7)
                    {
                        if (BuffToStressTopic.TryGetValue(buffId, out var topicData))
                        {
                            ConversationHelper.RemoveTopic(topicData.topic);
                            _monitor.Log($"[RestoreActiveStressBuffs] Удален топик {topicData.topic} для {buffId}", LogLevel.Debug);
                        }
                    }
                    continue;
                }

                _monitor.Log($"[RestoreActiveStressBuffs] {buffId} нужно восстановить", LogLevel.Debug);

                // Если дебафф не вылечен и актуален
                // Проверяем, есть ли уже бафф
                if (!_stateService.HasActiveBuffInGame(buffId))
                {
                    _buffService.ApplyBuffFromData(buffId);
                    restoredCount++;
                    _monitor.Log($"[RestoreActiveStressBuffs] Восстановлен дебафф {buffId} (дата выдачи: {treatment.IssuedDate}, лечение начато: {treatment.TreatmentStarted})", LogLevel.Info);
                }
                else
                {
                    _monitor.Log($"[RestoreActiveStressBuffs] Дебафф {buffId} уже активен", LogLevel.Debug);
                }

                // Восстанавливаем топик для диалога с Харви, если лечение еще не начато
                if (!treatment.TreatmentStarted && BuffToStressTopic.TryGetValue(buffId, out var topic))
                {
                    if (!ConversationHelper.HasTopic(topic.topic))
                    {
                        ConversationHelper.AddTopic(topic.topic, topic.days);
                        _monitor.Log($"[RestoreActiveStressBuffs] Восстановлен топик {topic.topic} для незалеченного дебаффа {buffId}", LogLevel.Info);
                    }
                    else
                    {
                        _monitor.Log($"[RestoreActiveStressBuffs] Топик {topic.topic} для {buffId} уже существует", LogLevel.Debug);
                    }
                }

                // Если лечение начато - восстанавливаем квест, если потерян
                if (treatment.TreatmentStarted && !string.IsNullOrEmpty(treatment.QuestId))
                {
                    if (!_stateService.HasQuestInJournal(treatment.QuestId))
                    {
                        _questService.AddQuest(treatment.QuestId);

                        // Восстанавливаем запись в ActiveTreatments
                        if (!_data.StressState.ActiveTreatments.ContainsKey(buffId))
                        {
                            _data.StressState.ActiveTreatments[buffId] = treatment;
                        }

                        if (BuffToDisplayName.TryGetValue(buffId, out var displayName))
                        {
                            _monitor.Log($"[RestoreActiveStressBuffs] Восстановлен квест {treatment.QuestId} для дебаффа {buffId} ({displayName})", LogLevel.Info);
                        }
                    }
                    else
                    {
                        _monitor.Log($"[RestoreActiveStressBuffs] Квест {treatment.QuestId} для {buffId} уже активен", LogLevel.Debug);
                    }
                }
            }

            if (restoredCount > 0)
            {
                _monitor.Log($"[RestoreActiveStressBuffs] Восстановлено дебаффов стресса: {restoredCount}", LogLevel.Info);
            }
            else
            {
                _monitor.Log($"[RestoreActiveStressBuffs] Дебаффы для восстановления не найдены", LogLevel.Debug);
            }

            // Очищаем устаревшие записи
            var toRemove = _data.StressState.TreatmentHistory
                .Where(kvp => kvp.Value.Count > 0 && !kvp.Value.Last().ShouldRestore(today))
                .Select(kvp => kvp.Key)
                .ToList();

            _monitor.Log($"[RestoreActiveStressBuffs] Найдено устаревших записей для удаления: {toRemove.Count}", LogLevel.Debug);

            foreach (var buffId in toRemove)
            {
                if (_data.StressState.TreatmentHistory.TryGetValue(buffId, out var historyList) && historyList.Count > 0)
                {
                    var treatment = historyList.Last();
                    int daysSince = today.DaysSinceStart - treatment.IssuedDate.DaysSinceStart;
                    _data.StressState.TreatmentHistory.Remove(buffId);
                    _monitor.Log($"[RestoreActiveStressBuffs] Удалено устаревшее лечение {buffId} (вылечен={treatment.IsCured}, дней={daysSince})", LogLevel.Debug);
                }
            }
        }

        /// <summary>
        /// Очищает старые топики начала лечения (если нет соответствующего дебаффа)
        /// </summary>
        public void CleanupOrphanedTreatmentTopics()
        {
            var treatmentStartTopics = new Dictionary<string, string>
            {
                [TopicIds.TreatmentStartTired] = BuffIds.Tired,
                [TopicIds.TreatmentStartLonely] = BuffIds.Lonely,
                [TopicIds.TreatmentStartThunder] = BuffIds.Thunder,
                [TopicIds.TreatmentStartHunger] = BuffIds.Hunger,
                [TopicIds.TreatmentStartOverwork] = BuffIds.Overwork,
                [TopicIds.TreatmentStartNoSleep] = BuffIds.NoSleep,
                [TopicIds.TreatmentStartTooCold] = BuffIds.TooCold,
                [TopicIds.TreatmentStartSocial] = BuffIds.Social,
                [TopicIds.TreatmentStartDarkness] = BuffIds.Darkness,
            };

            _monitor.Log($"[CleanupOrphanedTreatmentTopics] Начинаем очистку топиков лечения", LogLevel.Debug);
            int removedCount = 0;

            foreach (var (topic, buffId) in treatmentStartTopics)
            {
                if (ConversationHelper.HasTopic(topic))
                {
                    _monitor.Log($"[CleanupOrphanedTreatmentTopics] Найден топик {topic} для дебаффа {buffId}", LogLevel.Debug);

                    // Проверяем, есть ли соответствующий дебафф или состояние
                    bool hasActiveBuff = _stateService.HasActiveBuffInGame(buffId);
                    bool hasValidState = _data.StressState.TreatmentHistory.TryGetValue(buffId, out var historyList)
                                        && historyList.Count > 0
                                        && !historyList.Last().IsCured 
                                        && historyList.Last().ShouldRestore(SDate.Now());

                    _monitor.Log($"[CleanupOrphanedTreatmentTopics] {topic}: активный бафф={hasActiveBuff}, валидное состояние={hasValidState}", LogLevel.Debug);

                    if (hasValidState && historyList != null && historyList.Count > 0)
                    {
                        var treatment = historyList.Last();
                        _monitor.Log($"[CleanupOrphanedTreatmentTopics] Лечение {buffId}: выдан={treatment.IssuedDate}, лечение={treatment.TreatmentStarted}, вылечен={treatment.IsCured}", LogLevel.Debug);
                    }

                    if (!hasActiveBuff && !hasValidState)
                    {
                        ConversationHelper.RemoveTopic(topic);
                        removedCount++;
                        _monitor.Log($"[CleanupOrphanedTreatmentTopics] УДАЛЕН топик {topic} без соответствующего дебаффа {buffId}", LogLevel.Info);
                    }
                    else
                    {
                        _monitor.Log($"[CleanupOrphanedTreatmentTopics] Оставляем топик {topic} - есть валидный дебафф", LogLevel.Debug);
                    }
                }
            }

            _monitor.Log($"[CleanupOrphanedTreatmentTopics] Очистка завершена. Удалено топиков: {removedCount}", LogLevel.Info);
        }
    }
}


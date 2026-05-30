using System;
using System.Collections.Generic;
using System.Linq;
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
        private GameDataService? _gameDataService;
        private StressLoadService? _stressLoadService;
        private TreatmentEpisodeService? _episodeService;
        private EpisodeQuestProgressService? _episodeQuestProgressService;
        private DarknessService? _darknessService;

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
        /// Устанавливает GameDataService для получения данных из JSON
        /// </summary>
        public void SetGameDataService(GameDataService gameDataService)
        {
            _gameDataService = gameDataService;
            _monitor.Log("[TreatmentService] GameDataService установлен", LogLevel.Debug);
        }

        public void SetStressLoadService(StressLoadService stressLoadService)
        {
            _stressLoadService = stressLoadService;
            _monitor.Log("[TreatmentService] StressLoadService установлен", LogLevel.Debug);
        }

        public void SetTreatmentEpisodeService(TreatmentEpisodeService episodeService)
        {
            _episodeService = episodeService;
            _monitor.Log("[TreatmentService] TreatmentEpisodeService установлен", LogLevel.Debug);
        }

        public void SetEpisodeQuestProgressService(EpisodeQuestProgressService episodeQuestProgressService)
        {
            _episodeQuestProgressService = episodeQuestProgressService;
            _monitor.Log("[TreatmentService] EpisodeQuestProgressService установлен", LogLevel.Debug);
        }

        public void SetDarknessService(DarknessService darknessService)
        {
            _darknessService = darknessService;
            _monitor.Log("[TreatmentService] DarknessService установлен", LogLevel.Debug);
        }

        /// <summary>
        /// Применяет простой (незалоченный) бафф стресса при срабатывании триггера
        /// НЕ выдает квест - только бафф и топик для диалога с Харви
        /// </summary>
        public void ApplyStressBuff(string buffId, string displayName)
        {
            _monitor.Log($"[ApplyStressBuff] Попытка применить дебафф {buffId} ({displayName})", LogLevel.Debug);

            // ⭐ ИСПРАВЛЕНО: Разный кулдаун для разных типов стресса
            // Социальная тревожность имеет более длинный кулдаун, так как срабатывает чаще
            int cooldownDays = buffId == BuffIds.Social ? 7 : 1;
            
            // Проверяем через StateService, можно ли выдать бафф
            if (!_stateService.CanIssueBuff(buffId, cooldownDays: cooldownDays))
            {
                _monitor.Log($"[ApplyStressBuff] Дебафф {buffId} нельзя выдать (активен или на кулдауне {cooldownDays} дней)", LogLevel.Debug);
                return;
            }

            // Делегируем логику применения баффа в StateService
            _stateService.ApplyStressBuff(buffId, displayName);

            // Добавляем топик для диалога с Харви
            AddTopicForBuff(buffId);

            _stressLoadService?.AddCauseFromBuff(buffId);

            _monitor.Log($"[ApplyStressBuff] ✅ Стресс {displayName} применен. Поговорите с Харви для начала лечения.", LogLevel.Info);
        }

        /// <summary>
        /// Начинает TreatmentEpisode — единое назначение Харви по общему состоянию.
        /// </summary>
        public bool StartTreatmentEpisode(string episodeId)
        {
            if (_episodeService == null)
            {
                _monitor.Log("[StartTreatmentEpisode] TreatmentEpisodeService не установлен", LogLevel.Error);
                return false;
            }

            return _episodeService.StartTreatmentEpisode(episodeId);
        }

        /// <summary>
        /// Legacy: начинает программу лечения по одному buffId.
        /// Episode mode использует <see cref="StartTreatmentEpisode"/>.
        /// </summary>
        public void StartTreatment(
            string buffId,
            string displayName,
            string? questIdOverride = null,
            string? episodeId = null)
        {
            if (GameStateHelper.IsEventActive())
            {
                _monitor.Log($"[StartTreatment] Пропуск: активно событие ({buffId})", LogLevel.Debug);
                return;
            }

            if (buffId == BuffIds.Darkness && DarknessLegacyHelper.UsesLevelSystem(_data, _stateService))
            {
                _monitor.Log("[StartTreatment] Старт уровневой терапии страха темноты", LogLevel.Info);
                if (!_data.Darkness.IsTherapyActive && !_data.Darkness.IsCured)
                {
                    if (_darknessService == null)
                    {
                        _monitor.Log("[StartTreatment] DarknessService не установлен — fallback topicDarknessTherapyStart", LogLevel.Error);
                        if (!ConversationHelper.HasTopic("topicDarknessTherapyStart"))
                            ConversationHelper.AddTopic("topicDarknessTherapyStart", 7);
                    }
                    else
                    {
                        _darknessService.StartTherapy();
                    }
                }

                return;
            }

            _monitor.Log($"[StartTreatment] Попытка начать лечение для {buffId} ({displayName})", LogLevel.Debug);
            if (!string.IsNullOrEmpty(episodeId))
                _monitor.Log($"[StartTreatment] Episode mode: {episodeId}", LogLevel.Debug);

            string? questId = null;
            QuestData? questData = null;

            if (!string.IsNullOrEmpty(questIdOverride))
            {
                questId = questIdOverride;
                questData = _gameDataService?.GetQuestData(questIdOverride);
                _monitor.Log($"[StartTreatment] Episode quest override: {questId}", LogLevel.Debug);
            }
            else if (_gameDataService != null)
            {
                // Пробуем получить квест из новой системы
                var quests = _gameDataService.GetQuestsForBuff(buffId);
                questData = quests.FirstOrDefault();
                
                if (questData != null)
                {
                    questId = questData.Id;
                    _monitor.Log($"[StartTreatment] ✅ Квест найден через GameDataService: {questId}", LogLevel.Debug);
                }
                else if (BuffToQuest.TryGetValue(buffId, out var fallbackQuestId))
                {
                    questId = fallbackQuestId;
                    _monitor.Log($"[StartTreatment] Квест найден через BuffToQuest (fallback): {questId}", LogLevel.Debug);
                }
                else
                {
                    _monitor.Log($"[StartTreatment] ОШИБКА: Не найден ID квеста для баффа {buffId}", LogLevel.Error);
                    return;
                }
            }
            else if (!BuffToQuest.TryGetValue(buffId, out questId))
            {
                _monitor.Log($"[StartTreatment] ОШИБКА: Не найден ID квеста для баффа {buffId} (GameDataService не установлен)", LogLevel.Error);
                return;
            }
            
            // Проверка что questId не null
            if (questId == null)
            {
                _monitor.Log($"[StartTreatment] ОШИБКА: questId оказался null для баффа {buffId}", LogLevel.Error);
                return;
            }

            // Проверяем, можно ли начать лечение
            if (!CanStartTreatmentForBuff(buffId, episodeId))
            {
                _monitor.Log($"[StartTreatment] ОШИБКА: Бафф {buffId} не активен и episode causes отсутствуют.", LogLevel.Error);
                return;
            }

            // ⭐ УЛУЧШЕНО: Проверяем, не активен ли уже квест (поддерживает множественные лечения)
            if (_data.StressState.HasActiveQuest(questId))
            {
                _monitor.Log($"[StartTreatment] Квест {questId} уже активен. Пропускаем.", LogLevel.Info);
                _monitor.Log($"[StartTreatment] ═══ СТЕК ВЫЗОВОВ ═══", LogLevel.Info);
                _monitor.Log($"[StartTreatment] {Environment.StackTrace}", LogLevel.Info);
                return;
            }

            // ⭐ ИСПРАВЛЕНО: Используем существующее лечение (созданное при ApplyStressBuff), а не дублируем
            var existingTreatment = _data.StressState.GetActiveTreatmentsByBuff(buffId)
                .FirstOrDefault(t => !t.IsCured && !t.TreatmentStarted);

            TreatmentState treatment;
            string treatmentKey;

            if (existingTreatment != null)
            {
                treatment = existingTreatment;
                treatmentKey = treatment.TreatmentKey;
                treatment.EnsureTreatmentKey();
                _monitor.Log($"[StartTreatment] Продолжаем существующее лечение: {treatmentKey}", LogLevel.Info);
            }
            else
            {
                var instanceNumber = _data.StressState.GetNextInstanceNumber(buffId);
                treatmentKey = TreatmentState.GenerateTreatmentKey(buffId, instanceNumber);
                _monitor.Log($"[StartTreatment] Создаем новое лечение с ключом: {treatmentKey}", LogLevel.Info);

                treatment = new TreatmentState
                {
                    BuffId = buffId,
                    TreatmentKey = treatmentKey,
                    InstanceNumber = instanceNumber,
                    IssuedDate = SDate.Now(),
                    IsCured = false,
                    IsCompleted = false,
                    Progress = new TreatmentProgress()
                };

                _data.StressState.AddTreatment(treatment);
            }

            treatment.QuestId = questId;
            treatment.TreatmentStartedDate = SDate.Now();
            treatment.TreatmentStarted = true;
            treatment.AddedToGameLog = false;
            treatment.Progress ??= new TreatmentProgress();

            // Удаляем исходный топик стресса при начале лечения
            if (BuffToStressTopic.TryGetValue(buffId, out var stressTopic))
            {
                if (ConversationHelper.HasTopic(stressTopic.topic))
                    ConversationHelper.RemoveTopic(stressTopic.topic);
            }

            RemoveLegacyTreatmentStartTopic(buffId);

            if (TreatmentTopics.FollowupByBuff.TryGetValue(buffId, out var followupTopic))
            {
                ConversationHelper.AddTopic(followupTopic, 2);
                _monitor.Log($"[StartTreatment] Followup topic {followupTopic} for aftercare dialogue", LogLevel.Debug);
            }

            // ⭐ НОВОЕ: Логируем состояние ПЕРЕД началом лечения
            _monitor.Log($"[StartTreatment] ═══ СОСТОЯНИЕ ПЕРЕД НАЧАЛОМ ЛЕЧЕНИЯ ═══", LogLevel.Info);
            _monitor.Log($"[StartTreatment] Разговоров сегодня до начала лечения: {_data.TalkedNpcsToday.Count}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] NPC, с которыми уже говорили: {string.Join(", ", _data.TalkedNpcsToday)}", LogLevel.Info);

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

            if (_stressLoadService != null
                && !string.IsNullOrEmpty(episodeId)
                && (_data.ActiveTreatmentEpisode == null
                    || !string.Equals(_data.ActiveTreatmentEpisode.EpisodeId, episodeId, StringComparison.Ordinal)))
            {
                _stressLoadService.SetActiveTreatmentEpisode(episodeId);
            }

            // ⭐ НОВОЕ: Отправляем письмо о начале лечения
            if (_gameDataService != null)
            {
                var mailData = _gameDataService.GetMailForBuff(buffId);
                if (mailData != null)
                {
                    _questService.AddMailForTomorrow(mailData.Id);
                    _monitor.Log($"[StartTreatment] ✅ Письмо '{mailData.Id}' добавлено на завтра", LogLevel.Info);
                }
                else
                {
                    _monitor.Log($"[StartTreatment] ⚠️ Письмо для {buffId} не найдено в GameDataService", LogLevel.Warn);
                }
            }

            // ⭐ УЛУЧШЕНО: Инициализируем прогресс для нового лечения
            treatment.Progress.BeginQuestObjectivesGracePeriod();

            if (string.Equals(questId, QuestIds.SocialShutdown, StringComparison.Ordinal))
            {
                treatment.Progress.SecondsNearHarvey = 0;
                treatment.Progress.SocialShutdownTrustedTalk = false;
                treatment.Progress.SocialShutdownUnfamiliarNpcs.Clear();

                _questService.UpdateQuest(
                    QuestIds.SocialShutdown,
                    description:
                        "Харви видит, что ты закрылась от людей. Сегодня нужен мягкий контакт, а не изоляция.\n\n" +
                        treatment.Progress.GetSocialShutdownProgressText(),
                    objective: TriggerService.BuildSocialShutdownObjective(treatment.Progress));

                _monitor.Log("[StartTreatment] Инициализация прогресса SocialShutdown", LogLevel.Info);
            }
            else if (buffId == BuffIds.Social)
            {
                treatment.Progress.TalkedUniqueToday = _data.TalkedNpcsToday.Count;
                treatment.Progress.SocialTalksAfterQuest = 0;
                treatment.Progress.SecondsNearHarvey = 0;
                treatment.Progress.SocialShutdownTrustedTalk = false;
                treatment.Progress.SocialShutdownUnfamiliarNpcs.Clear();

                _monitor.Log($"[StartTreatment] ═══ ИНИЦИАЛИЗАЦИЯ ПРОГРЕССА SOCIAL ═══", LogLevel.Info);
                _monitor.Log($"[StartTreatment] TreatmentKey: {treatmentKey}", LogLevel.Info);
                _monitor.Log($"[StartTreatment] TalkedUniqueToday (база): {treatment.Progress.TalkedUniqueToday}", LogLevel.Info);
                _monitor.Log($"[StartTreatment] SocialTalksAfterQuest (счетчик): {treatment.Progress.SocialTalksAfterQuest}", LogLevel.Info);
                _monitor.Log($"[StartTreatment] SecondsNearHarvey: {treatment.Progress.SecondsNearHarvey}", LogLevel.Info);
            }
            else if (!string.IsNullOrEmpty(episodeId))
            {
                _episodeQuestProgressService?.InitializeEpisodeQuest(episodeId, treatment.Progress);
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
            _monitor.Log($"[StartTreatment] InstanceNumber: {treatment.InstanceNumber}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] HasBuffInGame({buffId}): {_stateService.HasBuffInGame(buffId)}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] HasActiveTreatmentState({buffId}): {_stateService.HasActiveTreatmentState(buffId)}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] HasActiveQuestState({questId}): {_stateService.HasActiveQuestState(questId)}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] HasQuestInGameJournal({questId}): {_stateService.HasQuestInGameJournal(questId)}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] ActiveTreatments.Count: {_data.StressState.ActiveTreatments.Count}", LogLevel.Info);
            _monitor.Log($"[StartTreatment] ActiveTreatmentsByBuff({buffId}).Count: {_data.StressState.GetActiveTreatmentCountByBuff(buffId)}", LogLevel.Info);

            _monitor.Log($"[StartTreatment] ✅ УСПЕШНО: Лечение начато для {displayName} (Квест: {questId}, Ключ: {treatmentKey})", LogLevel.Info);
            Game1.playSound("newRecipe");
        }

        /// <summary>
        /// Условия назначения выполнены — квест и дебафф остаются до финального разговора с Харви.
        /// </summary>
        public void MarkTreatmentReadyForReview(string buffId, string? optionalMessage = null)
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode?.IsActiveEpisode() == true && _episodeService != null)
            {
                var primaryBuff = episode.PrimaryCauseId != null
                    && StressCauses.CauseToBuff.TryGetValue(episode.PrimaryCauseId, out var mappedBuff)
                    ? mappedBuff
                    : TreatmentEpisodeDefinitions.ResolvePrimaryBuffId(episode.EpisodeId, episode.RelatedCauseIds);

                if (string.IsNullOrEmpty(primaryBuff) || primaryBuff == buffId)
                {
                    _episodeService.MarkTreatmentEpisodeReadyForReview(episode.EpisodeId, optionalMessage);
                    return;
                }
            }

            MarkTreatmentReadyForReviewLegacy(buffId, optionalMessage);
        }

        private void MarkTreatmentReadyForReviewLegacy(string buffId, string? optionalMessage = null)
        {
            var activeTreatment = _data.StressState.GetActiveTreatment(buffId);
            if (activeTreatment == null)
            {
                _monitor.Log($"[MarkTreatmentReadyForReview] ⚠️ Активное лечение для buffId '{buffId}' не найдено", LogLevel.Warn);
                return;
            }

            if (activeTreatment.IsCured || activeTreatment.IsCompleted)
                return;

            if (activeTreatment.AwaitingHarveyReview)
                return;

            activeTreatment.ObjectivesCompleted = true;
            activeTreatment.AwaitingHarveyReview = true;
            activeTreatment.ReadyForReviewDate = SDate.Now();

            var questId = activeTreatment.QuestId;
            if (string.IsNullOrEmpty(questId) && BuffToQuest.TryGetValue(buffId, out var mappedQuestId))
                questId = mappedQuestId;

            if (!string.IsNullOrEmpty(questId))
            {
                _questService.UpdateQuest(questId, objective: StressQuestCopy.ReadyForReviewObjective);
            }

            if (TreatmentTopics.GetReadyForReviewTopic(buffId) is { } reviewTopic)
                ConversationHelper.AddTopic(reviewTopic, 2);

            var hudMessage = optionalMessage ?? StressQuestCopy.ReadyForReviewHud;
            Game1.addHUDMessage(new HUDMessage(hudMessage, HUDMessage.newQuest_type));

            _monitor.Log(
                $"[MarkTreatmentReadyForReview] buffId={buffId}, questId={questId ?? "(empty)"}, treatmentKey={activeTreatment.TreatmentKey}, AwaitingHarveyReview=true",
                LogLevel.Info);
        }

        /// <summary>Review по episode id (Gotoro flashback и др.).</summary>
        public void MarkTreatmentReadyForReviewByEpisode(string episodeId, string? optionalMessage = null)
        {
            if (_episodeService != null)
            {
                _episodeService.MarkTreatmentEpisodeReadyForReview(episodeId, optionalMessage);
                return;
            }

            if (!TreatmentEpisodeDefinitions.TryGet(episodeId, out _))
            {
                _monitor.Log($"[MarkTreatmentReadyForReviewByEpisode] Unknown episode {episodeId}", LogLevel.Warn);
                return;
            }

            var causeIds = _stressLoadService?.GetActiveCauses().Keys ?? Enumerable.Empty<string>();
            var buffId = TreatmentEpisodeDefinitions.ResolvePrimaryBuffId(episodeId, causeIds);
            MarkTreatmentReadyForReviewLegacy(buffId, optionalMessage);
        }

        public void CompleteTreatment(string buffId, string message = "Лечение завершено.")
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode?.AwaitingHarveyReview == true && _episodeService != null)
            {
                _episodeService.CompleteTreatmentEpisode(episode.EpisodeId, message);
                return;
            }

            CompleteTreatmentLegacy(buffId, message);
        }

        private void CompleteTreatmentLegacy(string buffId, string message = "Лечение завершено.")
        {
            var activeTreatment = _data.StressState.GetActiveTreatment(buffId);
            if (activeTreatment == null)
            {
                _monitor.Log($"[CompleteTreatment] ⚠️ Активное лечение для buffId '{buffId}' не найдено — завершение пропущено", LogLevel.Warn);
                return;
            }

            activeTreatment.ObjectivesCompleted = false;
            activeTreatment.AwaitingHarveyReview = false;
            activeTreatment.ReadyForReviewDate = null;

            var questId = activeTreatment.QuestId;
            if (string.IsNullOrEmpty(questId) && !BuffToQuest.TryGetValue(buffId, out questId))
            {
                _monitor.Log($"[CompleteTreatment] ⚠️ QuestId не найден для buffId '{buffId}' — завершение пропущено", LogLevel.Warn);
                return;
            }

            _stateService.CompleteTreatment(questId);

            if (_data.StressState.HasActiveQuest(questId))
            {
                _monitor.Log($"[CompleteTreatment] ⚠️ StateService не завершил лечение для questId '{questId}' — wrapper и HUD пропущены", LogLevel.Warn);
                return;
            }

            ApplyEpisodeCompletionStressReduction(buffId);
            ApplyCompletionRewards(buffId, message);

            _monitor.Log($"[CompleteTreatment] Legacy завершение для {buffId}", LogLevel.Info);
        }

        /// <summary>Friendship/mail/Harvey emote после завершения лечения.</summary>
        public void ApplyCompletionRewards(
            string buffId,
            string message,
            bool removeReviewTopic = true,
            bool playSoundAndHud = true)
        {
            if (removeReviewTopic
                && TreatmentTopics.GetReadyForReviewTopic(buffId) is { } reviewTopic
                && ConversationHelper.HasTopic(reviewTopic))
            {
                ConversationHelper.RemoveTopic(reviewTopic);
            }

            if (BuffToStressTopic.TryGetValue(buffId, out var stressTopic)
                && ConversationHelper.HasTopic(stressTopic.topic))
            {
                ConversationHelper.RemoveTopic(stressTopic.topic);
            }

            RemoveLegacyTreatmentStartTopic(buffId);
            RemoveTreatmentFollowupTopic(buffId);

            int immunityDays = GetImmunityDaysForDebuff(buffId);
            if (immunityDays > 0)
            {
                _stateService.SetImmunity(buffId, immunityDays);
                _monitor.Log(
                    $"[ApplyCompletionRewards] Иммунитет {immunityDays} дней для {buffId}",
                    LogLevel.Debug);
            }

            if (playSoundAndHud)
            {
                Game1.playSound("discoverMineral");
                Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type));
            }

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

        private bool CanStartTreatmentForBuff(string buffId, string? episodeId)
        {
            if (_data.StressState.HasActiveBuff(buffId) || _buffService.HasBuff(buffId))
                return true;

            if (string.IsNullOrEmpty(episodeId)
                || !TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition)
                || _stressLoadService == null)
            {
                return false;
            }

            if (definition.RelatedCauses.Any(causeId =>
                    _stressLoadService.GetActiveCauses().TryGetValue(causeId, out var cause) && cause.IsActive))
            {
                return true;
            }

            return definition.EpisodeId == StressEpisodes.GotoroFlashback
                   && _data.StressLoad.GotoroFlashbackActive;
        }

        private void ApplyEpisodeCompletionStressReduction(string buffId)
        {
            if (_stressLoadService == null)
                return;

            if (_data.ActiveTreatmentEpisode != null)
                return;

            var episodeId = _stressLoadService.GetActiveTreatmentEpisodeId();
            if (!string.IsNullOrEmpty(episodeId)
                && TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition))
            {
                var totalReduction = 0;
                foreach (var relatedCauseId in definition.RelatedCauses)
                {
                    if (!_stressLoadService.GetActiveCauses().ContainsKey(relatedCauseId))
                        continue;

                    totalReduction += StressCauses.GetBaseWeight(relatedCauseId);
                    _stressLoadService.RemoveCause(relatedCauseId);
                }

                _stressLoadService.ReduceStressByTreatment(
                    Math.Max(totalReduction, 10),
                    episodeId);

                _monitor.Log(
                    $"[CompleteTreatment] Episode {episodeId} cleared related causes, load -{totalReduction}",
                    LogLevel.Debug);
                return;
            }

            if (StressCauses.TryGetCauseForBuff(buffId, out var causeId))
            {
                _stressLoadService.RemoveCause(causeId);
                _stressLoadService.ReduceStressByTreatment(
                    StressCauses.GetBaseWeight(causeId),
                    _stressLoadService.GetActiveTreatmentEpisodeId());
            }
        }

        public void AddTopicForBuff(string buffId)
        {
            if (!_stateService.HasActiveTreatmentState(buffId))
                return;

            if (BuffToStressTopic.TryGetValue(buffId, out var topic))
            {
                // ⭐ ИСПРАВЛЕНО: Обновляем топик даже если он уже есть (для повторного применения баффа)
                // Это решает проблему, когда после первого лечения топик не удалился полностью
                if (ConversationHelper.HasTopic(topic.topic))
                {
                    ConversationHelper.RemoveTopic(topic.topic);
                    _monitor.Log($"[AddTopicForBuff] Удален старый топик {topic.topic} для повторного применения баффа {buffId}", LogLevel.Debug);
                }
                
                ConversationHelper.AddTopic(topic.topic, topic.days);
                _monitor.Log($"[AddTopicForBuff] Добавлен топик {topic.topic} для баффа {buffId} ({topic.days} дней)", LogLevel.Debug);
            }
        }

        public void EnsureLockedBuffsPersist()
        {
            foreach (var (treatmentKey, treatment) in _data.StressState.ActiveTreatments)
            {
                if (!treatment.IsCured && !_stateService.HasBuffInGame(treatment.BuffId))
                {
                    if (!_buffService.ApplyBuffFromData(treatment.BuffId))
                    {
                        _monitor.Log($"[EnsureLockedBuffsPersist] ⚠️ Не удалось восстановить бафф '{treatment.BuffId}' для лечения {treatmentKey}", LogLevel.Warn);
                    }
                    AddTopicForBuff(treatment.BuffId);
                }
            }
        }

        public void SyncQuestsAndBuffs()
        {
            int removedCount = 0;
            int restoredCount = 0;
            int fixedProgressCount = 0;

            foreach (var (treatmentKey, treatment) in _data.StressState.ActiveTreatments.ToList())
            {
                var questId = treatment.QuestId;
                var buffId = treatment.BuffId;

                // Проверяем, есть ли квест в журнале
                if (!string.IsNullOrEmpty(questId) && !_questService.HasQuest(questId))
                {
                    _buffService.RemoveBuff(buffId);
                    _data.StressState.RemoveTreatment(treatmentKey);
                    removedCount++;
                    _monitor.Log($"[SyncQuestsAndBuffs] Удалено лечение {treatmentKey}: квест {questId} не найден в журнале", LogLevel.Info);
                    continue;
                }

                // Квест завершён в журнале без mod state — переводим в ожидание разговора с Харви
                if (!string.IsNullOrEmpty(questId) && _questService.HasQuest(questId))
                {
                    var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == questId);
                    if (quest?.completed.Value == true && !treatment.AwaitingHarveyReview)
                    {
                        MarkTreatmentReadyForReview(buffId);
                        continue;
                    }
                }

                // Восстанавливаем бафф если потерялся
                if (!_stateService.HasBuffInGame(buffId))
                {
                    if (_buffService.ApplyBuffFromData(buffId))
                    {
                        restoredCount++;
                        _monitor.Log($"[SyncQuestsAndBuffs] Восстановлен дебафф {buffId} для лечения {treatmentKey}", LogLevel.Info);
                    }
                    else
                    {
                        _monitor.Log($"[SyncQuestsAndBuffs] ⚠️ Не удалось восстановить дебафф {buffId} для лечения {treatmentKey}", LogLevel.Warn);
                    }
                }

                // Гарантируем наличие прогресса
                if (treatment.Progress == null)
                {
                    treatment.Progress = new TreatmentProgress();
                    fixedProgressCount++;
                    _monitor.Log($"[SyncQuestsAndBuffs] Инициализирован прогресс для лечения {treatmentKey}", LogLevel.Info);
                }
            }

            if (removedCount > 0 || restoredCount > 0 || fixedProgressCount > 0)
            {
                _monitor.Log($"[SyncQuestsAndBuffs] Синхронизация завершена: удалено={removedCount}, восстановлено={restoredCount}, исправлено={fixedProgressCount}", LogLevel.Info);
            }
        }

        public void RestoreLostQuestsFromTopics()
        {
            int restoredCount = 0;
            foreach (var (buffId, topicData) in BuffToStressTopic)
            {
                if (!ConversationHelper.HasTopic(topicData.topic))
                    continue;

                if (_data.StressState.HasActiveBuff(buffId))
                    continue;

                if (!BuffToQuest.TryGetValue(buffId, out var questId))
                    continue;

                if (_questService.HasQuest(questId))
                    continue;

                _questService.AddQuest(questId);
                _stateService.ApplyStressBuff(buffId, BuffToDisplayName.GetValueOrDefault(buffId, buffId));
                _stateService.StartTreatment(buffId, questId);
                restoredCount++;
                _monitor.Log($"[RestoreLostQuestsFromTopics] Восстановлен квест для {BuffToDisplayName.GetValueOrDefault(buffId, buffId)} из топика {topicData.topic}", LogLevel.Info);
            }

            if (restoredCount > 0)
                _monitor.Log($"[RestoreLostQuestsFromTopics] Восстановлено квестов: {restoredCount}", LogLevel.Info);
        }

        public void CleanupOldStressTopics()
        {
            var today = SDate.Now();
            const int MaxDaysToKeep = 3;
            int removedCount = 0;

            foreach (var (buffId, topicData) in BuffToStressTopic)
            {
                if (!ConversationHelper.HasTopic(topicData.topic))
                    continue;

                if (_data.StressState.LastIssuedDay.TryGetValue(buffId, out var lastIssued))
                {
                    int daysSinceIssued = today.DaysSinceStart - lastIssued.DaysSinceStart;
                    if (daysSinceIssued > MaxDaysToKeep)
                    {
                        ConversationHelper.RemoveTopic(topicData.topic);
                        _data.StressState.LastIssuedDay.Remove(buffId);
                        removedCount++;
                        _monitor.Log($"[CleanupOldStressTopics] Удален старый топик {topicData.topic} для {BuffToDisplayName.GetValueOrDefault(buffId, buffId)} ({daysSinceIssued} дней)", LogLevel.Info);
                    }
                }
                else
                {
                    ConversationHelper.RemoveTopic(topicData.topic);
                    removedCount++;
                    _monitor.Log($"[CleanupOldStressTopics] Удален топик {topicData.topic} для {BuffToDisplayName.GetValueOrDefault(buffId, buffId)} (нет даты выдачи)", LogLevel.Info);
                }
            }

            if (removedCount > 0)
                _monitor.Log($"[CleanupOldStressTopics] Удалено старых топиков: {removedCount}", LogLevel.Info);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Восстанавливает потерянные баффы для активных лечений
        /// Используется только при ручном вызове через консольную команду
        /// </summary>
        public int RestoreMissingBuffsForActiveTreatments()
        {
            int restoredCount = 0;

            _monitor.Log("[RestoreMissingBuffs] Проверка активных лечений...", LogLevel.Info);

            foreach (var (treatmentKey, treatment) in _data.StressState.ActiveTreatments)
            {
                // Пропускаем завершенные лечения
                if (treatment.IsCured || treatment.IsCompleted)
                {
                    _monitor.Log($"[RestoreMissingBuffs] Пропускаем завершенное лечение: {treatmentKey}", LogLevel.Debug);
                    continue;
                }

                var buffId = treatment.BuffId;

                // Проверяем, есть ли бафф в игре
                if (!_stateService.HasBuffInGame(buffId))
                {
                    _monitor.Log($"[RestoreMissingBuffs] Восстанавливаем потерянный бафф '{buffId}' для лечения {treatmentKey}", LogLevel.Info);
                    if (_buffService.ApplyBuffFromData(buffId))
                    {
                        restoredCount++;
                    }
                    else
                    {
                        _monitor.Log($"[RestoreMissingBuffs] ⚠️ Не удалось восстановить бафф '{buffId}' для лечения {treatmentKey}", LogLevel.Warn);
                    }
                }
            }

            if (restoredCount == 0)
            {
                _monitor.Log("[RestoreMissingBuffs] Все баффы на месте, восстановление не требуется", LogLevel.Info);
            }
            else
            {
                _monitor.Log($"[RestoreMissingBuffs] ✅ Восстановлено баффов: {restoredCount}", LogLevel.Info);
            }

            return restoredCount;
        }

        public void RestoreActiveStressBuffs()
        {
            foreach (var (buffId, historyList) in _data.StressState.TreatmentHistory)
            {
                if (historyList.Count == 0) continue;

                var treatment = historyList.Last();
                var today = SDate.Now();
                int daysSince = today.DaysSinceStart - treatment.IssuedDate.DaysSinceStart;

                // Если устарел или вылечен - очищаем топики
                if (treatment.IsCured || daysSince > 7)
                {
                    if (BuffToStressTopic.TryGetValue(buffId, out var topicData))
                    {
                        ConversationHelper.RemoveTopic(topicData.topic);
                    }
                    continue;
                }

                // Восстанавливаем бафф если его нет
                if (!_stateService.HasBuffInGame(buffId))
                {
                    if (!_buffService.ApplyBuffFromData(buffId))
                    {
                        _monitor.Log($"[RestoreActiveStressBuffs] ⚠️ Не удалось восстановить бафф '{buffId}' для лечения {treatment.TreatmentKey}", LogLevel.Warn);
                    }
                }

                // Восстанавливаем топик если лечение не начато
                if (!treatment.TreatmentStarted && BuffToStressTopic.TryGetValue(buffId, out var topic))
                {
                    if (!ConversationHelper.HasTopic(topic.topic))
                    {
                        ConversationHelper.AddTopic(topic.topic, topic.days);
                    }
                }

                // Восстанавливаем квест если лечение начато
                if (treatment.TreatmentStarted && !string.IsNullOrEmpty(treatment.QuestId))
                {
                    if (!_questService.HasQuest(treatment.QuestId))
                        _questService.AddQuest(treatment.QuestId);

                    treatment.EnsureTreatmentKey();
                    if (_data.StressState.GetTreatmentByKey(treatment.TreatmentKey) == null)
                        _data.StressState.RestoreActiveTreatment(treatment);
                }
            }
        }

        /// <summary>
        /// ⭐ НОВОЕ: Получает количество дней иммунитета для дебаффа после лечения
        /// </summary>
        private int GetImmunityDaysForDebuff(string buffId)
        {
            // Social имеет иммунитет на 3 дня после лечения
            if (buffId == BuffIds.Social)
                return 3;
            
            // Остальные дебаффы не имеют иммунитета (достаточно кулдауна)
            return 0;
        }

        public void CleanupOrphanedTreatmentTopics()
        {
            int removedCount = 0;

            foreach (var topicId in TreatmentTopics.UnimplementedDisabledTopicIds)
            {
                if (!ConversationHelper.HasTopic(topicId))
                    continue;

                ConversationHelper.RemoveTopic(topicId);
                removedCount++;
                _monitor.Log($"[CleanupOrphanedTreatmentTopics] Удален disabled topic {topicId}", LogLevel.Info);
            }

            foreach (var (buffId, legacyTopic) in TreatmentTopics.LegacyStartByBuff)
            {
                if (!ConversationHelper.HasTopic(legacyTopic))
                    continue;

                bool treatmentStarted = _data.StressState.GetActiveTreatmentsByBuff(buffId)
                    .Any(t => t.TreatmentStarted && !t.IsCured);

                if (treatmentStarted || !_stateService.HasActiveTreatmentState(buffId))
                {
                    ConversationHelper.RemoveTopic(legacyTopic);
                    removedCount++;
                    _monitor.Log($"[CleanupOrphanedTreatmentTopics] Удален legacy start topic {legacyTopic}", LogLevel.Info);
                }
            }

            foreach (var (buffId, followupTopic) in TreatmentTopics.FollowupByBuff)
            {
                if (!ConversationHelper.HasTopic(followupTopic))
                    continue;

                bool hasActiveTreatment = _data.StressState.GetActiveTreatmentsByBuff(buffId)
                    .Any(t => t.TreatmentStarted && !t.IsCured);

                if (!hasActiveTreatment)
                {
                    ConversationHelper.RemoveTopic(followupTopic);
                    removedCount++;
                    _monitor.Log($"[CleanupOrphanedTreatmentTopics] Удален orphaned followup {followupTopic}", LogLevel.Info);
                }
            }

            if (removedCount > 0)
                _monitor.Log($"[CleanupOrphanedTreatmentTopics] Удалено топиков: {removedCount}", LogLevel.Info);
        }

        private static void RemoveLegacyTreatmentStartTopic(string buffId)
        {
            if (TreatmentTopics.LegacyStartByBuff.TryGetValue(buffId, out var legacyTopic)
                && ConversationHelper.HasTopic(legacyTopic))
            {
                ConversationHelper.RemoveTopic(legacyTopic);
            }
        }

        private static void RemoveTreatmentFollowupTopic(string buffId)
        {
            if (TreatmentTopics.FollowupByBuff.TryGetValue(buffId, out var followupTopic)
                && ConversationHelper.HasTopic(followupTopic))
            {
                ConversationHelper.RemoveTopic(followupTopic);
            }
        }
    }
}


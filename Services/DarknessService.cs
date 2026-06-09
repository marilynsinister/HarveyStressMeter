using System;
using System.Linq;
using System.Text;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using HarveyStressMeter.Models;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Сервис для управления дебаффом "Боязнь темноты" с системой уровней
    /// </summary>
    public class DarknessService
    {
        private readonly SaveData _data;
        private readonly BuffService _buffService;
        private readonly StateService _stateService;
        private readonly QuestService _questService;
        private StressLoadService? _stressLoadService;
        private DarknessRemissionService? _remissionService;
        private readonly IMonitor _monitor;

        private bool _worryLetterSent;

        private float _step1ElapsedMs;
        private bool _step1WasProgressing;
        private bool _step1WasAwayFromHome;
        private int _step1LastHudSecondsDisplayed = -1;
        private bool _step1EveningCreditedHudShown;
        private bool _step1AllEveningsHudShown;
        private bool _step1HadReadyTopicAtTalkStart;

        private int Step1EveningsRequired
            => DarknessRemissionHelper.GetStep1EveningsRequired(_data.Darkness);

        public DarknessService(
            SaveData data,
            BuffService buffService,
            StateService stateService,
            QuestService questService,
            IMonitor monitor)
        {
            _data = data;
            _buffService = buffService;
            _stateService = stateService;
            _questService = questService;
            _monitor = monitor;
        }

        public void SetStressLoadService(StressLoadService stressLoadService)
            => _stressLoadService = stressLoadService;

        public void SetRemissionService(DarknessRemissionService remissionService)
            => _remissionService = remissionService;

        /// <summary>
        /// Проверить и применить дебафф темноты при входе в локацию
        /// </summary>
        public void CheckAndApplyDarknessBuff(GameLocation location)
        {
            // Базовые проверки
            if (Game1.stats.DaysPlayed < 3) return;
            if (Game1.timeOfDay < 2200 || Game1.timeOfDay > 2600) return;
            if (_stateService.HasImmunity(BuffIds.Darkness)) return;

            var locationName = location.NameOrUniqueName;

            if (_data.Darkness.DarknessRemissionActive)
            {
                if (DarknessRemissionHelper.IsDarknessEnvironmentalTrigger(locationName, Game1.timeOfDay))
                    _remissionService?.OnDarknessEnvironmentalTrigger(location);
                return;
            }

            // Полная ремиссия / излечение без активного страха
            if (_data.Darkness.IsCured && _data.Darkness.HasOvercomeBonus && _data.Darkness.FearLevel <= 0)
            {
                _monitor.Log("[DarknessService] Ремиссия/излечение — классический дебафф не вешаем", LogLevel.Debug);
                return;
            }

            bool isDarkLocation = locationName is "Backwoods" or "Forest" or "Mountain";
            if (!isDarkLocation) return;

            RegisterDarknessEpisode();
            ApplyFearLevelBuff();
        }

        /// <summary>
        /// Зарегистрировать эпизод страха темноты
        /// </summary>
        private void RegisterDarknessEpisode()
        {
            var currentDate = SDate.Now();
            _data.Darkness.RegisterEpisode(currentDate);
            
            _monitor.Log($"[DarknessService] Эпизод страха зарегистрирован. Всего: {_data.Darkness.DarknessEpisodesCount}, за неделю: {_data.Darkness.EpisodesThisWeek}", LogLevel.Info);

            // Если это первый эпизод, повышаем уровень до 1
            if (_data.Darkness.FearLevel == 0)
            {
                _data.Darkness.IncreaseFearLevel(currentDate);
                _monitor.Log("[DarknessService] Первый эпизод страха! Уровень повышен до 1", LogLevel.Info);
                
                // Добавляем топик для диалога с Харви
                ConversationHelper.AddTopic(TopicIds.StressDarkness, 7); // 7 дней на разговор
            }
        }

        /// <summary>
        /// Применить бафф в зависимости от текущего уровня страха
        /// </summary>
        private void ApplyFearLevelBuff()
        {
            EnsureFearLevelBuff(showMessages: true);
            SyncStressLoadFromDarkness();
        }

        /// <summary>Legacy buffStressDarkness → уровневая система (идемпотентно, через SyncDarknessState).</summary>
        public void MigrateLegacyDarknessPath()
            => SyncDarknessState("MigrateLegacyDarknessPath");

        private void SyncStressLoadFromDarkness()
            => _stressLoadService?.SyncFromGameState();

        private void TryMigrateCuredSaveToRemission()
        {
            var d = _data.Darkness;
            if (!d.IsCured || !d.HasOvercomeBonus || d.DarknessRemissionActive || d.IsTherapyActive)
                return;

            if (d.DarknessRemissionStartDay >= 0)
                return;

            _remissionService?.StartRemission(showHud: false);
            _monitor.Log("[DarknessService] Миграция IsCured → активная ремиссия", LogLevel.Info);
        }

        /// <summary>Единый путь синхронизации save ↔ игра (баффы, квесты, legacy). Не меняет TherapyStage.</summary>
        public void SyncDarknessState(string reason)
        {
            if (!Context.IsWorldReady)
            {
                _monitor.Log($"[DarknessService] SyncDarknessState({reason}): мир не готов, пропуск", LogLevel.Debug);
                return;
            }

            _monitor.Log($"[DarknessService] SyncDarknessState reason={reason}", LogLevel.Debug);

            TryMigrateCuredSaveToRemission();
            TryMigrateLegacyBuffToLevelSystem();

            var removedTreatments = DarknessLegacyHelper.RemoveErroneousLevelDarknessTreatments(_data, _monitor);
            if (removedTreatments > 0)
            {
                _monitor.Log(
                    $"[DarknessService] Удалено {removedTreatments} ошибочных ActiveTreatment для level-buff темноты",
                    LogLevel.Warn);
            }

            CleanupStaleDarknessTopics();

            if (_data.Darkness.IsCured)
            {
                RemoveAllFearBuffs();
                _buffService.RemoveBuff(BuffIds.Darkness);
                RemoveLegacyDarknessRecoveryArtifacts("cured");

                if (_data.Darkness.HasOvercomeBonus)
                    EnsureOvercomeBonus();

                if (_data.Darkness.DarknessRemissionActive)
                    RemoveAllFearBuffs();

                LogDarknessSyncSnapshot(reason);
                SyncStressLoadFromDarkness();
                return;
            }

            if (DarknessLegacyHelper.UsesLevelSystem(_data, _stateService))
                RemoveLegacyDarknessRecoveryArtifacts("level-system");

            if (_data.Darkness.IsTherapyActive)
            {
                if (_data.Darkness.TherapyStage == 1)
                {
                    TryMigrateLegacyStep1Progress();
                    TryMigrateStep1ProgressUnitsToSeconds();
                    EnsureStep1CalendarDay(refreshJournalOnDayChange: true);
                    RefreshTherapyQuestJournal(1);
                }

                SyncTherapyQuestsFromSavedStage();
            }

            if (_data.Darkness.FearLevel > 0)
                EnsureFearLevelBuff(showMessages: false);
            else if (!_data.Darkness.IsTherapyActive)
                RemoveAllFearBuffs();

            if (_data.Darkness.HasOvercomeBonus)
                EnsureOvercomeBonus();

            RestoreStressDarknessTopicIfNeeded();

            LogDarknessSyncSnapshot(reason);
            SyncStressLoadFromDarkness();
        }

        private void TryMigrateLegacyBuffToLevelSystem()
        {
            if (DarknessLegacyHelper.UsesLevelSystem(_data, _stateService))
                return;

            if (!_stateService.HasBuffInGame(BuffIds.Darkness) || _data.Darkness.FearLevel != 0)
                return;

            _data.Darkness.IncreaseFearLevel(SDate.Now());
            _buffService.RemoveBuff(BuffIds.Darkness);
            ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
            _monitor.Log("[DarknessService] Миграция legacy buffStressDarkness → уровень 1", LogLevel.Info);
        }

        private void RemoveLegacyDarknessRecoveryArtifacts(string context)
        {
            if (_stateService.HasBuffInGame(BuffIds.Darkness))
                _buffService.RemoveBuff(BuffIds.Darkness);

            if (!_questService.HasQuest(QuestIds.Darkness) && !_stateService.HasActiveQuestState(QuestIds.Darkness))
                return;

            _questService.CompleteQuest(QuestIds.Darkness);
            Game1.player.removeQuest(QuestIds.Darkness);
            _buffService.RemoveBuff(BuffIds.LightAndSafe);
            _monitor.Log(
                $"[DarknessService] Снят legacy HarveyMod_DarknessRecovery / buffLightAndSafe ({context})",
                LogLevel.Info);
        }

        /// <summary>Только устаревшие топики; терапию по отсутствию квеста не сбрасываем.</summary>
        public void CleanupStaleDarknessState()
            => CleanupStaleDarknessTopics();

        private void CleanupStaleDarknessTopics()
        {
            if (!_data.Darkness.IsTherapyActive && ConversationHelper.HasTopic("topicDarknessTherapyStart"))
            {
                ConversationHelper.RemoveTopic("topicDarknessTherapyStart");
                _monitor.Log("[DarknessService] Удалён устаревший topicDarknessTherapyStart (терапия не активна)", LogLevel.Info);
            }

            if (!_data.Darkness.IsTherapyActive || _data.Darkness.CompletedStep1)
            {
                if (ConversationHelper.HasTopic(DarknessLegacyHelper.Step1ReadyForHarveyTopic))
                {
                    ConversationHelper.RemoveTopic(DarknessLegacyHelper.Step1ReadyForHarveyTopic);
                    _monitor.Log(
                        "[DarknessService] Удалён устаревший topicDarknessStep1ReadyForHarvey",
                        LogLevel.Debug);
                }
            }
        }

        private void SyncTherapyQuestsFromSavedStage()
        {
            int stage = Math.Clamp(_data.Darkness.TherapyStage, 1, 3);
            if (_data.Darkness.TherapyStage < 1)
            {
                _monitor.Log(
                    "[DarknessService] IsTherapyActive при TherapyStage<1 — квест не восстанавливаем (ожидается StartTherapy)",
                    LogLevel.Warn);
                return;
            }

            string expectedQuestId = GetStepQuestId(stage);
            RemoveStaleStepQuests(stage);

            if (!_questService.HasQuest(expectedQuestId))
            {
                _questService.AddQuest(expectedQuestId);
                _monitor.Log(
                    $"[DarknessService] Восстановлен квест {expectedQuestId} для TherapyStage={stage} (save IsTherapyActive=true)",
                    LogLevel.Warn);
            }

            RefreshTherapyQuestJournal(stage);
        }

        private static string GetStepQuestId(int stage) => stage switch
        {
            1 => QuestIds.DarknessStep1,
            2 => QuestIds.DarknessStep2,
            3 => QuestIds.DarknessStep3,
            _ => QuestIds.DarknessStep1,
        };

        private void RemoveStaleStepQuests(int activeStage)
        {
            foreach (var questId in new[] { QuestIds.DarknessStep1, QuestIds.DarknessStep2, QuestIds.DarknessStep3 })
            {
                if (GetStepQuestId(activeStage) == questId || !_questService.HasQuest(questId))
                    continue;

                Game1.player.removeQuest(questId);
                _monitor.Log(
                    $"[DarknessService] Удалён лишний step-квест {questId} (активный этап {activeStage})",
                    LogLevel.Debug);
            }
        }

        private void EnsureFearLevelBuff(bool showMessages)
        {
            if (_data.Darkness.FearLevel <= 0)
                return;

            string buffId = GetCurrentBuffId();
            RemoveOtherFearLevelBuffs(buffId);

            if (_stateService.HasBuffInGame(buffId))
                return;

            var effects = GetFearLevelEffects(_data.Darkness.FearLevel);
            _buffService.ApplyBuff(buffId, GetFearLevelDisplayName(), effects, Buff.ENDLESS);
            _monitor.Log(
                $"[DarknessService] Синхронизирован бафф {buffId} (уровень {_data.Darkness.FearLevel})",
                LogLevel.Info);

            if (showMessages)
                ShowFearLevelMessage();
        }

        private void EnsureOvercomeBonus()
        {
            if (_stateService.HasBuffInGame(BuffIds.DarknessOvercome))
                return;

            ApplyOvercomeBonus();
        }

        private void RestoreStressDarknessTopicIfNeeded()
        {
            if (_data.Darkness.IsTherapyActive || _data.Darkness.FearLevel <= 0 || _data.Darkness.IsCured)
                return;

            if (!ConversationHelper.HasTopic(TopicIds.StressDarkness))
            {
                ConversationHelper.AddTopic(TopicIds.StressDarkness, 7);
                _monitor.Log(
                    $"[DarknessService] Восстановлен топик {TopicIds.StressDarkness} (FearLevel={_data.Darkness.FearLevel})",
                    LogLevel.Debug);
            }
        }

        private void LogDarknessSyncSnapshot(string reason)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[DarknessService] SyncDarknessState snapshot reason={reason}");
            sb.AppendLine($"  FearLevel={_data.Darkness.FearLevel} IsTherapyActive={_data.Darkness.IsTherapyActive} TherapyStage={_data.Darkness.TherapyStage}");
            sb.AppendLine($"  IsCured={_data.Darkness.IsCured} HasOvercomeBonus={_data.Darkness.HasOvercomeBonus}");
            sb.AppendLine(
                $"  Step1 evenings={_data.Darkness.SafeDarknessEveningsCompleted}/{DarknessLegacyHelper.Step1EveningsRequired} " +
                $"todaySec={_data.Darkness.SafeDarknessProgressToday}/{DarknessLegacyHelper.Step1SecondsPerEvening} " +
                $"todayCompleted={_data.Darkness.DarknessTherapyTodayCompleted} lastDay={_data.Darkness.DarknessTherapyLastCompletedDay} " +
                $"LastSafeDarknessDate={_data.Darkness.LastSafeDarknessDate?.ToString() ?? "null"} " +
                $"legacyMinutes={_data.Darkness.SafeDarknessMinutes}");
            sb.AppendLine($"  SafeZonesVisited=[{string.Join(", ", _data.Darkness.SafeZonesVisited)}] MountainNightSeconds={_data.Darkness.MountainNightSeconds}");
            sb.Append("  buffs: ");
            AppendBuffFlag(sb, BuffIds.DarknessLevel1);
            AppendBuffFlag(sb, BuffIds.DarknessLevel2);
            AppendBuffFlag(sb, BuffIds.DarknessLevel3);
            AppendBuffFlag(sb, BuffIds.Darkness);
            AppendBuffFlag(sb, BuffIds.DarknessOvercome);
            AppendBuffFlag(sb, BuffIds.HarveyLantern);
            AppendBuffFlag(sb, BuffIds.LightAndSafe);
            sb.AppendLine();
            sb.Append("  quests: ");
            AppendQuestFlag(sb, QuestIds.Darkness);
            AppendQuestFlag(sb, QuestIds.DarknessStep1);
            AppendQuestFlag(sb, QuestIds.DarknessStep2);
            AppendQuestFlag(sb, QuestIds.DarknessStep3);
            if (_data.Darkness.IsTherapyActive && _data.Darkness.TherapyStage >= 1)
            {
                var objective = DarknessLegacyHelper.GetStepQuestCurrentObjective(_data.Darkness.TherapyStage);
                sb.AppendLine();
                sb.Append($"  currentObjective: {objective ?? "(n/a)"}");
            }

            _monitor.Log(sb.ToString(), LogLevel.Info);
        }

        private void AppendBuffFlag(StringBuilder sb, string buffId)
            => sb.Append(_stateService.HasBuffInGame(buffId) ? $"{buffId}=Y " : $"{buffId}=N ");

        private void AppendQuestFlag(StringBuilder sb, string questId)
        {
            sb.Append(_questService.HasQuest(questId) ? $"{questId}=Y " : $"{questId}=N ");
        }

        /// <summary>
        /// Обновить состояние страха темноты (вызывается каждый день)
        /// </summary>
        public void UpdateDailyFearState()
        {
            var currentDate = SDate.Now();

            SyncDarknessState("UpdateDailyFearState:pre");

            _data.Darkness.UpdateIgnoredDays(currentDate);

            _monitor.Log(
                $"[DarknessService] Ежедневное обновление: Уровень={_data.Darkness.FearLevel}, Игнорируется={_data.Darkness.DaysIgnored} дней",
                LogLevel.Info);

            if (_data.Darkness.ShouldIncreaseFearLevel(currentDate))
                IncreaseFearLevel();

            if (_data.Darkness.CanDecreaseFearNaturally(currentDate))
                DecreaseFearLevel();

            SyncDarknessState("UpdateDailyFearState:post");
        }

        /// <summary>
        /// Повысить уровень страха
        /// </summary>
        private void IncreaseFearLevel()
        {
            int oldLevel = _data.Darkness.FearLevel;
            _data.Darkness.IncreaseFearLevel(SDate.Now());
            int newLevel = _data.Darkness.FearLevel;
            
            _monitor.Log($"[DarknessService] ⚠️ Уровень страха повышен: {oldLevel} → {newLevel}", LogLevel.Warn);
            
            // Удаляем старый топик и добавляем новый
            ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
            
            if (newLevel == 2)
            {
                ConversationHelper.AddTopic("topicStressDarknessSerious", 7);
                Game1.addHUDMessage(new HUDMessage(
                    "Темнота снова давит сильнее… Не оставайся с этим одна. Дом и свет — по плану Харви.",
                    HUDMessage.error_type));
            }
            else if (newLevel == 3)
            {
                ConversationHelper.AddTopic("topicStressDarknessPhobia", 0); // Не истекает, пока не начнется лечение
                Game1.addHUDMessage(new HUDMessage(
                    "Это уже похоже на фобию… Приди к Харви. Не геройствуй и не проверяй себя темнотой одна.",
                    HUDMessage.error_type));
                
                // Отправляем письмо от Харви
                SendHarveyWorryLetter();
            }
            
            // Применяем новый бафф
            ApplyFearLevelBuff();
        }

        /// <summary>
        /// Снизить уровень страха естественным путем
        /// </summary>
        private void DecreaseFearLevel()
        {
            int oldLevel = _data.Darkness.FearLevel;
            _data.Darkness.DecreaseFearLevel();
            int newLevel = _data.Darkness.FearLevel;
            
            _monitor.Log($"[DarknessService] ✅ Уровень страха снижен естественным путем: {oldLevel} → {newLevel}", LogLevel.Info);
            
            if (newLevel == 0)
            {
                // Полностью снят
                RemoveAllFearBuffs();
                ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
                Game1.addHUDMessage(new HUDMessage(
                    "Сегодня темнота отступила. Если снова накроет — ты знаешь план: дом, свет, не одна.",
                    HUDMessage.achievement_type));
            }
        }

        /// <summary>
        /// Начать терапию страха темноты
        /// </summary>
        public void StartTherapy()
        {
            if (_data.Darkness.IsTherapyActive)
            {
                _monitor.Log("[DarknessService] Терапия уже активна", LogLevel.Warn);
                return;
            }

            _data.Darkness.IsTherapyActive = true;
            _data.Darkness.TherapyStartDate = SDate.Now();
            _data.Darkness.TherapyStage = 1; // Начинаем с Шага 1
            
            _monitor.Log($"[DarknessService] ✅ Терапия начата! Текущий уровень страха: {_data.Darkness.FearLevel}", LogLevel.Info);
            
            // Удаляем топики игнорирования и согласия на терапию
            ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
            ConversationHelper.RemoveTopic("topicStressDarknessSerious");
            ConversationHelper.RemoveTopic("topicStressDarknessPhobia");
            ConversationHelper.RemoveTopic("topicDarknessTherapyStart");
            ConversationHelper.RemoveTopic("topicStressDarknessLevel2");
            ConversationHelper.RemoveTopic("topicStressDarknessLevel3");
            
            // Сбрасываем счетчик игнорирования
            _data.Darkness.IgnoredSinceDate = null;
            _data.Darkness.DaysIgnored = 0;

            if (!_questService.HasQuest(QuestIds.DarknessStep1))
                _questService.AddQuest(QuestIds.DarknessStep1);
            
            Game1.playSound("newRecipe");
            Game1.addHUDMessage(new HUDMessage(
                DarknessTherapyCopy.StartTherapyHud(),
                HUDMessage.newQuest_type));
            ResetStep1HudFlags();
            RefreshTherapyQuestJournal(1);
            SyncDarknessState("StartTherapy");
        }

        /// <summary>
        /// Завершить терапию полностью
        /// </summary>
        public void CompleteTherapy()
        {
            _data.Darkness.IsCured = true;
            _data.Darkness.HasOvercomeBonus = true;
            _data.Darkness.TherapyCompletedDate = SDate.Now();
            _data.Darkness.FearLevel = 0;
            _data.Darkness.IsTherapyActive = false;
            
            _monitor.Log("[DarknessService] ✅✅✅ Терапия полностью завершена! Игрок излечен!", LogLevel.Info);
            
            // Удаляем все дебаффы страха
            RemoveAllFearBuffs();
            
            // Применяем перманентный бонус "Преодоление"
            ApplyOvercomeBonus();
            
            Game1.playSound("yoba");
            Game1.addHUDMessage(new HUDMessage(
                "Ты прошла всю программу. Я горжусь — и всё равно хочу слышать, как ты себя чувствуешь.",
                HUDMessage.achievement_type));

            _data.Darkness.DarknessRelapseTreatmentActive = false;
            _remissionService?.StartRemission();
            
            // +200 дружбы с Харви
            var harvey = Game1.getCharacterFromName("Harvey");
            if (harvey != null)
            {
                Game1.player.changeFriendship(200, harvey);
            }
        }

        /// <summary>Рецидив: укороченное лечение (1–3 вечера) без полной 3-шаговой программы.</summary>
        public void BeginRelapseTreatment(int eveningsRequired)
        {
            var d = _data.Darkness;
            eveningsRequired = Math.Clamp(eveningsRequired, 1, DarknessLegacyHelper.Step1EveningsRequired);

            d.IsCured = false;
            d.HasOvercomeBonus = false;
            d.IsTherapyActive = true;
            d.TherapyStage = 1;
            d.DarknessRelapseTreatmentActive = true;
            d.DarknessTherapyEveningsRequired = eveningsRequired;
            d.CompletedStep1 = false;
            d.CompletedStep2 = false;
            d.CompletedStep3 = false;
            d.FearLevel = 1;
            d.SafeDarknessEveningsCompleted = 0;
            d.SafeDarknessProgressToday = 0;
            d.DarknessTherapyTodayCompleted = false;
            d.DarknessTherapyLastCompletedDay = -1;
            d.LastSafeDarknessDate = SDate.Now();

            RemoveAllFearBuffs();
            EnsureFearLevelBuff(showMessages: true);
            ResetStep1HudFlags();

            if (!_questService.HasQuest(QuestIds.DarknessStep1))
                _questService.AddQuest(QuestIds.DarknessStep1);

            RefreshTherapyQuestJournal(1);
            SyncDarknessState("BeginRelapseTreatment");

            _monitor.Log(
                $"[DarknessService] Рецидив: начато лечение на {eveningsRequired} вечер(а/ов)",
                LogLevel.Warn);
        }

        /// <summary>Завершение укороченного лечения после рецидива → снова ремиссия.</summary>
        public void CompleteRelapseTreatmentAfterHarveyTalk()
        {
            var d = _data.Darkness;
            if (!d.DarknessRelapseTreatmentActive)
                return;

            d.DarknessRelapseTreatmentActive = false;
            d.IsTherapyActive = false;
            d.TherapyStage = 0;
            d.FearLevel = 0;
            d.IsCured = true;
            d.HasOvercomeBonus = true;
            d.TherapyCompletedDate = SDate.Now();

            RemoveAllFearBuffs();
            ApplyOvercomeBonus();

            var quest1 = Game1.player.questLog.FirstOrDefault(q => q.id.Value == QuestIds.DarknessStep1);
            if (quest1 != null)
            {
                quest1.questComplete();
                Game1.player.removeQuest(QuestIds.DarknessStep1);
            }

            ConversationHelper.RemoveTopic(DarknessLegacyHelper.Step1ReadyForHarveyTopic);
            _remissionService?.StartRemission();

            Game1.playSound("questcomplete");
            Game1.addHUDMessage(new HUDMessage(
                "Откат пройден. Ремиссия снова активна — береги себя ночью.",
                HUDMessage.achievement_type));

            _monitor.Log("[DarknessService] Рецидивное лечение завершено → ремиссия", LogLevel.Info);
            SyncDarknessState("CompleteRelapseTreatment");
        }

        /// <summary>
        /// Проверить, можно ли выходить из дома ночью (для Уровня 3)
        /// </summary>
        public bool CanLeaveHouseAtNight()
        {
            // Если уровень страха 3 и время после 22:00
            if (_data.Darkness.FearLevel >= 3 && Game1.timeOfDay >= 2200)
            {
                // Проверяем, не активна ли терапия на Шаге 2 или 3
                if (_data.Darkness.IsTherapyActive && _data.Darkness.TherapyStage >= 2)
                {
                    return true; // Во время терапии можно выходить
                }
                
                return false; // Фобия блокирует выход
            }
            
            return true;
        }

        /// <summary>
        /// Показать сообщение о блокировке выхода
        /// </summary>
        public void ShowCannotLeaveMessage()
        {
            Game1.drawObjectDialogue(
                "Ноги не идут за порог… Слишком темно.$s#$b#Харви говорил: сначала дом и свет. Не геройствуй сейчас.");
            Game1.playSound("cancel");
        }

        // ===== МЕХАНИКИ ШАГОВ ТЕРАПИИ =====

        /// <summary>
        /// Обновить прогресс терапии (~раз в секунду из GameLogicHandler).
        /// </summary>
        public void UpdateTherapyProgress()
        {
            var elapsed = Game1.currentGameTime?.ElapsedGameTime ?? TimeSpan.FromSeconds(1);
            _remissionService?.UpdatePerSecond(elapsed);

            if (!_data.Darkness.IsTherapyActive) return;

            switch (_data.Darkness.TherapyStage)
            {
                case 1:
                    UpdateStep1Progress(elapsed);
                    break;
                case 2:
                    UpdateStep2Progress();
                    break;
                case 3:
                    UpdateStep3Progress();
                    break;
            }
        }

        /// <summary>
        /// Шаг 1: 3 вечера × 60 минут дома при свете (20:00–00:00). Таймер в игровых секундах (ElapsedGameTime).
        /// </summary>
        private void UpdateStep1Progress(TimeSpan elapsed)
        {
            if (!_data.Darkness.IsTherapyActive || _data.Darkness.TherapyStage != 1)
            {
                ClearStep1SessionState();
                return;
            }

            TryMigrateLegacyStep1Progress();
            TryMigrateStep1ProgressUnitsToSeconds();
            EnsureStep1CalendarDay(refreshJournalOnDayChange: true);

            if (IsStep1AwaitingHarveyTalk())
            {
                RefreshTherapyQuestJournal(1);
                return;
            }

            bool evening = Game1.timeOfDay >= 2000 && Game1.timeOfDay <= 2400;
            bool atHome = GameStateHelper.IsPlayerHomeLocation();
            bool noEvent = !GameStateHelper.IsEventActive();
            bool sessionClear = !IsStep1SessionBlocked();
            bool canCountToday = !IsStep1TodayCredited() && CanCreditAnotherEveningToday();
            bool isProgressing = evening && atHome && noEvent && sessionClear && canCountToday;

            HandleStep1HudState(isProgressing, atHome, evening);

            if (isProgressing)
            {
                _step1ElapsedMs += (float)elapsed.TotalMilliseconds;
                while (_step1ElapsedMs >= 1000f)
                {
                    _step1ElapsedMs -= 1000f;
                    IncrementStep1ProgressSecond();
                }
            }

            _step1WasProgressing = isProgressing;
        }

        private static bool IsStep1SessionBlocked()
        {
            if (Game1.activeClickableMenu != null)
                return true;

            return Game1.player?.isInBed?.Value == true;
        }

        private bool IsStep1TodayCredited()
            => _data.Darkness.DarknessTherapyTodayCompleted;

        private bool IsStep1TimerComplete()
            => _data.Darkness.SafeDarknessProgressToday >= DarknessLegacyHelper.Step1SecondsPerEvening;

        private bool CanCreditAnotherEveningToday()
        {
            if (IsStep1TodayCredited())
                return false;

            int todayDay = SDate.Now().DaysSinceStart;
            return _data.Darkness.DarknessTherapyLastCompletedDay != todayDay;
        }

        private bool IsStep1AwaitingHarveyTalk()
            => _data.Darkness.IsTherapyActive
               && _data.Darkness.TherapyStage == 1
               && !_data.Darkness.CompletedStep1
               && _data.Darkness.SafeDarknessEveningsCompleted >= Step1EveningsRequired;

        public void CaptureStep1ReadyTopicAtHarveyTalkStart()
            => _step1HadReadyTopicAtTalkStart = ConversationHelper.HasTopic(DarknessLegacyHelper.Step1ReadyForHarveyTopic);

        /// <summary>После разговора с Харви при 3/3 вечерах — закрыть шаг 1 (не автоматически при зачёте).</summary>
        public bool TryFinalizeStep1AfterHarveyTalk()
        {
            if (!IsStep1AwaitingHarveyTalk())
                return false;

            bool topicNow = ConversationHelper.HasTopic(DarknessLegacyHelper.Step1ReadyForHarveyTopic);
            if (!_step1HadReadyTopicAtTalkStart && !topicNow)
                return false;

            ConversationHelper.RemoveTopic(DarknessLegacyHelper.Step1ReadyForHarveyTopic);

            if (_data.Darkness.DarknessRelapseTreatmentActive)
            {
                _monitor.Log("[DarknessStep1] Финальный разговор с Харви — завершение рецидивного лечения", LogLevel.Info);
                CompleteRelapseTreatmentAfterHarveyTalk();
                return true;
            }

            _monitor.Log("[DarknessStep1] Финальный разговор с Харви — завершение шага 1", LogLevel.Info);
            CompleteStep1();
            return true;
        }

        private void TryMigrateLegacyStep1Progress()
        {
            var d = _data.Darkness;
            if (d.SafeDarknessEveningsCompleted > 0 || d.SafeDarknessProgressToday > 0 || d.SafeDarknessMinutes <= 0)
                return;

            int total = d.SafeDarknessMinutes;
            int oldMinutesPerEvening = 5;
            d.SafeDarknessEveningsCompleted = Math.Min(
                DarknessLegacyHelper.Step1EveningsRequired,
                total / oldMinutesPerEvening);
            int remainderMin = total % oldMinutesPerEvening;
            d.SafeDarknessProgressToday = remainderMin * 60;
            if (total >= DarknessLegacyHelper.Step1EveningsRequired * oldMinutesPerEvening)
            {
                d.SafeDarknessEveningsCompleted = DarknessLegacyHelper.Step1EveningsRequired;
                d.SafeDarknessProgressToday = 0;
            }

            d.SafeDarknessMinutes = 0;
            d.LastSafeDarknessDate ??= SDate.Now();
            _monitor.Log(
                $"[DarknessStep1] Миграция legacy SafeDarknessMinutes → вечера {d.SafeDarknessEveningsCompleted}/{DarknessLegacyHelper.Step1EveningsRequired}, " +
                $"сегодня {d.SafeDarknessProgressToday} сек",
                LogLevel.Info);
        }

        /// <summary>Старые сейвы: прогресс «сегодня» в минутах (0–5 или 0–60) → секунды.</summary>
        private void TryMigrateStep1ProgressUnitsToSeconds()
        {
            var d = _data.Darkness;
            int sec = d.SafeDarknessProgressToday;
            if (sec <= 0 || sec >= DarknessLegacyHelper.Step1SecondsPerEvening)
                return;

            if (sec <= DarknessLegacyHelper.Step1MinutesPerEvening)
            {
                d.SafeDarknessProgressToday = sec * 60;
                _monitor.Log(
                    $"[DarknessStep1] Миграция today {sec} мин → {d.SafeDarknessProgressToday} сек",
                    LogLevel.Debug);
            }
        }

        private void EnsureStep1CalendarDay(bool refreshJournalOnDayChange = false)
        {
            var today = SDate.Now();
            var tracked = _data.Darkness.LastSafeDarknessDate;

            if (tracked == null)
            {
                _data.Darkness.LastSafeDarknessDate = today;
                return;
            }

            if (tracked.DaysSinceStart == today.DaysSinceStart)
                return;

            _data.Darkness.SafeDarknessProgressToday = 0;
            _data.Darkness.DarknessTherapyTodayCompleted = false;
            _data.Darkness.LastSafeDarknessDate = today;
            ResetStep1HudFlags();
            _monitor.Log("[DarknessStep1] Новый игровой день — сброс secondsToday и DarknessEveningCountedToday", LogLevel.Debug);

            if (refreshJournalOnDayChange)
            {
                RefreshTherapyQuestJournal(1);
                _monitor.Log("[DarknessStep1] Quest journal refreshed after day change", LogLevel.Debug);
            }
        }

        private void IncrementStep1ProgressSecond()
        {
            if (IsStep1TodayCredited())
                return;

            if (IsStep1TimerComplete())
            {
                CompleteDarknessTherapyEvening();
                return;
            }

            int after = _data.Darkness.SafeDarknessProgressToday + 1;
            _data.Darkness.SafeDarknessProgressToday = after;

            _monitor.Log(
                $"[DarknessStep1] timer +1 → {after}/{DarknessLegacyHelper.Step1SecondsPerEvening} sec",
                LogLevel.Trace);

            RefreshTherapyQuestJournal(1);

            int displaySeconds = GetStep1DisplaySecondsToday();
            if (displaySeconds != _step1LastHudSecondsDisplayed)
            {
                _step1LastHudSecondsDisplayed = displaySeconds;
                if (displaySeconds > 0 && displaySeconds % 900 == 0)
                    ShowStep1TimerHud(atHome: true);
            }

            if (after >= DarknessLegacyHelper.Step1SecondsPerEvening)
            {
                _monitor.Log(
                    $"[DarknessStep1] daily timer reached {after}/{DarknessLegacyHelper.Step1SecondsPerEvening} sec — crediting evening",
                    LogLevel.Info);
                CompleteDarknessTherapyEvening();
            }
        }

        /// <summary>
        /// Зачесть один вечер терапии (не более одного раза за игровой день).
        /// </summary>
        private void CompleteDarknessTherapyEvening()
        {
            int todayDay = SDate.Now().DaysSinceStart;
            if (IsStep1TodayCredited())
            {
                _monitor.Log(
                    $"[DarknessStep1] Повторный зачёт вечера заблокирован — DarknessEveningCountedToday=true (day={todayDay})",
                    LogLevel.Debug);
                RefreshTherapyQuestJournal(1);
                return;
            }

            if (_data.Darkness.DarknessTherapyLastCompletedDay == todayDay)
            {
                _monitor.Log(
                    $"[DarknessStep1] Повторный зачёт вечера заблокирован — DarknessLastCountedDay={todayDay}",
                    LogLevel.Debug);
                RefreshTherapyQuestJournal(1);
                return;
            }

            _data.Darkness.DarknessTherapyTodayCompleted = true;
            _data.Darkness.DarknessTherapyLastCompletedDay = todayDay;
            _data.Darkness.SafeDarknessEveningsCompleted++;
            _data.Darkness.SafeDarknessProgressToday = DarknessLegacyHelper.Step1SecondsPerEvening;
            _data.Darkness.LastSafeDarknessDate = SDate.Now();

            int evenings = _data.Darkness.SafeDarknessEveningsCompleted;
            RefreshTherapyQuestJournal(1);
            LogStep1ProgressChange(force: true);

            _monitor.Log(
                $"[DarknessStep1] ✅ Вечер зачтён: evenings={evenings}/{Step1EveningsRequired}, day={todayDay}",
                LogLevel.Info);

            if (!_step1EveningCreditedHudShown)
            {
                _step1EveningCreditedHudShown = true;
                Game1.playSound("newArtifact");
                Game1.addHUDMessage(new HUDMessage(
                    DarknessTherapyCopy.EveningCreditedHud(evenings, Step1EveningsRequired),
                    HUDMessage.achievement_type));
            }

            if (evenings >= Step1EveningsRequired)
                MarkStep1ReadyForHarveyTalk();
        }

        private void MarkStep1ReadyForHarveyTalk()
        {
            if (!ConversationHelper.HasTopic(DarknessLegacyHelper.Step1ReadyForHarveyTopic))
                ConversationHelper.AddTopic(DarknessLegacyHelper.Step1ReadyForHarveyTopic, 7);

            RefreshTherapyQuestJournal(1);

            _monitor.Log(
                $"[DarknessStep1] Терапия готова к разговору с Харви — evenings={_data.Darkness.SafeDarknessEveningsCompleted}/{Step1EveningsRequired}, topic={DarknessLegacyHelper.Step1ReadyForHarveyTopic}",
                LogLevel.Info);

            if (!_step1AllEveningsHudShown)
            {
                _step1AllEveningsHudShown = true;
                Game1.playSound("questcomplete");
                Game1.addHUDMessage(new HUDMessage(
                    DarknessTherapyCopy.AllEveningsCreditedHud(Step1EveningsRequired),
                    HUDMessage.achievement_type));
            }
        }

        private void HandleStep1HudState(bool isProgressing, bool atHome, bool evening)
        {
            if (!evening || IsStep1AwaitingHarveyTalk())
                return;

            if (IsStep1TodayCredited())
                return;

            if (isProgressing)
            {
                if (!_step1WasProgressing)
                    ShowStep1TimerHud(atHome: true);
                else
                    TryRefreshStep1TimerHudPeriodic(atHome: true);
                _step1WasAwayFromHome = false;
                return;
            }

            if (evening && !atHome)
            {
                if (!_step1WasAwayFromHome)
                {
                    ShowStep1TimerHud(atHome: false);
                    _step1WasAwayFromHome = true;
                }
            }
        }

        private void ShowStep1TimerHud(bool atHome)
        {
            int seconds = GetStep1DisplaySecondsToday();
            string line = atHome
                ? DarknessTherapyCopy.TimerHudAtHome(seconds)
                : DarknessTherapyCopy.TimerHudAwayFromHome(seconds);

            Game1.addHUDMessage(new HUDMessage(line, HUDMessage.newQuest_type));

            if (atHome && seconds == 0 && !_step1WasProgressing)
            {
                Game1.addHUDMessage(new HUDMessage(
                    DarknessTherapyCopy.TimerHintAtHome(),
                    HUDMessage.newQuest_type));
            }
        }

        private void TryRefreshStep1TimerHudPeriodic(bool atHome)
        {
            int seconds = GetStep1DisplaySecondsToday();
            if (seconds <= 0 || seconds == _step1LastHudSecondsDisplayed)
                return;

            if (seconds % 600 != 0)
                return;

            _step1LastHudSecondsDisplayed = seconds;
            ShowStep1TimerHud(atHome);
        }

        private int GetStep1DisplaySecondsToday()
            => Math.Min(
                _data.Darkness.SafeDarknessProgressToday,
                DarknessLegacyHelper.Step1SecondsPerEvening);

        private int GetStep1DisplayMinutesToday()
            => GetStep1DisplaySecondsToday() / 60;

        private void LogStep1ProgressChange(bool force = false)
        {
            int todaySec = _data.Darkness.SafeDarknessProgressToday;
            string location = Game1.player?.currentLocation?.NameOrUniqueName ?? "?";
            _monitor.Log(
                $"[DarknessStep1] today={todaySec}/{DarknessLegacyHelper.Step1SecondsPerEvening}s " +
                $"({GetStep1DisplayMinutesToday()}/{DarknessLegacyHelper.Step1MinutesPerEvening} min), " +
                $"evenings={_data.Darkness.SafeDarknessEveningsCompleted}/{Step1EveningsRequired}, " +
                $"todayCompleted={IsStep1TodayCredited()}, lastDay={_data.Darkness.DarknessTherapyLastCompletedDay}, " +
                $"location={location}, time={Game1.timeOfDay}",
                force ? LogLevel.Info : LogLevel.Debug);
        }

        private void ResetStep1HudFlags()
        {
            _step1LastHudSecondsDisplayed = -1;
            _step1EveningCreditedHudShown = false;
            _step1AllEveningsHudShown = false;
            _step1WasAwayFromHome = false;
            _step1WasProgressing = false;
        }

        private void ClearStep1SessionState()
        {
            _step1WasProgressing = false;
        }

        /// <summary>
        /// Завершить Шаг 1 и перейти к Шагу 2
        /// </summary>
        private void CompleteStep1()
        {
            RefreshTherapyQuestJournal(1);

            _data.Darkness.CompletedStep1 = true;
            _data.Darkness.TherapyStage = 2;

            ClearStep1SessionState();
            ResetStep1HudFlags();
            ConversationHelper.RemoveTopic(DarknessLegacyHelper.Step1ReadyForHarveyTopic);
            
            _monitor.Log("[DarknessService] ✅ Шаг 1 завершен! Переход к Шагу 2", LogLevel.Info);
            
            // Завершаем квест Шага 1
            var quest1 = Game1.player.questLog.FirstOrDefault(q => q.id.Value == QuestIds.DarknessStep1);
            if (quest1 != null)
            {
                quest1.questComplete();
                Game1.player.removeQuest(QuestIds.DarknessStep1);
            }
            
            Game1.playSound("questcomplete");
            
            // Добавляем топик и квест Шага 2
            ConversationHelper.AddTopic("topicDarknessStep1Complete", 2);
            
            // Добавляем квест Шага 2 с задержкой (чтобы игрок сначала увидел сообщение)
            Game1.player.addQuest(QuestIds.DarknessStep2);
            RefreshTherapyQuestJournal(2);
            _monitor.Log("[DarknessService] Добавлен квест Шага 2", LogLevel.Info);
            SyncDarknessState("CompleteStep1");
        }

        /// <summary>
        /// Шаг 2: Контролируемая прогулка (посетить безопасные зоны)
        /// </summary>
        private void UpdateStep2Progress()
        {
            // Прогресс обрабатывается в HandleLocationVisit()
            // Здесь просто проверяем периодические сообщения поддержки
            
            bool isEarlyNight = Game1.timeOfDay >= 2000 && Game1.timeOfDay <= 2200;
            bool isOutside = Game1.player.currentLocation?.IsOutdoors == true;
            
            if (isEarlyNight && isOutside)
            {
                // Периодические сообщения поддержки (5% шанс каждую секунду)
                if (Game1.random.NextDouble() < 0.05)
                {
                    var messages = new[]
                    {
                        "Харви сказал: маленький шаг — и сразу домой. Не спорь со страхом допоздна.",
                        "Ты не одна: он знает, что ты вышла. Вернись вовремя — он проверит.",
                        "Свет фонарей в городе — не проверка на смелость. Это лечение, шаг 2/3 пути."
                    };
                    Game1.addHUDMessage(new HUDMessage(messages[Game1.random.Next(messages.Length)], HUDMessage.newQuest_type));
                }
            }
        }

        /// <summary>
        /// Обработать посещение локации для Шага 2
        /// </summary>
        public void HandleLocationVisit(string locationName)
        {
            if (!_data.Darkness.IsTherapyActive || _data.Darkness.TherapyStage != 2) return;

            // Проверяем время (20:00-22:00)
            if (Game1.timeOfDay < 2000 || Game1.timeOfDay > 2200) return;

            // Список безопасных зон
            var safeZones = new[] { "BusStop", "Town" };
            
            if (safeZones.Contains(locationName) && !_data.Darkness.SafeZonesVisited.Contains(locationName))
            {
                _data.Darkness.SafeZonesVisited.Add(locationName);
                
                string zoneName = locationName == "BusStop" ? "Автобусная остановка" : "Город";
                Game1.addHUDMessage(new HUDMessage($"✓ Зона посещена: {zoneName} ({_data.Darkness.SafeZonesVisited.Count}/2)", HUDMessage.achievement_type));
                Game1.playSound("coin");
                
                _monitor.Log($"[DarknessService] Шаг 2: Посещена зона {locationName} ({_data.Darkness.SafeZonesVisited.Count}/2)", LogLevel.Info);
                RefreshTherapyQuestJournal(2);

                // Проверяем завершение
                if (_data.Darkness.SafeZonesVisited.Count >= 2)
                {
                    CompleteStep2();
                }
            }
        }

        /// <summary>
        /// Завершить Шаг 2 и перейти к Шагу 3
        /// </summary>
        private void CompleteStep2()
        {
            _data.Darkness.CompletedStep2 = true;
            _data.Darkness.TherapyStage = 3;
            _data.Darkness.HasReceivedLantern = true; // "Получен" виртуальный фонарь
            
            // Снижаем уровень страха еще больше
            if (_data.Darkness.FearLevel > 0)
            {
                _data.Darkness.FearLevel = 1; // Финальный уровень перед излечением
                ApplyFearLevelBuff();
            }

            _monitor.Log("[DarknessService] ✅ Шаг 2 завершен! Переход к Шагу 3 (финал)", LogLevel.Info);
            
            // Завершаем квест Шага 2
            var quest2 = Game1.player.questLog.FirstOrDefault(q => q.id.Value == QuestIds.DarknessStep2);
            if (quest2 != null)
            {
                quest2.questComplete();
                Game1.player.removeQuest(QuestIds.DarknessStep2);
            }
            
            Game1.playSound("questcomplete");
            Game1.addHUDMessage(new HUDMessage(
                "Шаг 2 выполнен. Поговори с Харви — потом финал в горах. Не пропускай разговор.",
                HUDMessage.achievement_type));
            
            ConversationHelper.AddTopic("topicDarknessStep2Complete", 2);
            ConversationHelper.AddTopic("topicDarknessLanternReceived", 0);
            
            // Добавляем квест Шага 3
            Game1.player.addQuest(QuestIds.DarknessStep3);
            RefreshTherapyQuestJournal(3);
            _monitor.Log("[DarknessService] Добавлен квест Шага 3 (финал)", LogLevel.Info);
            SyncDarknessState("CompleteStep2");
        }

        /// <summary>
        /// Шаг 3: Ночь в горах (финальное испытание)
        /// </summary>
        private void UpdateStep3Progress()
        {
            // Проверяем условия
            bool isLateNight = Game1.timeOfDay >= 2200 && Game1.timeOfDay <= 2600;
            bool inMountains = Game1.player.currentLocation?.NameOrUniqueName == "Mountain";

            if (isLateNight && inMountains)
            {
                // Применяем бафф "Свет надежды" (виртуальный фонарь)
                if (!_stateService.HasBuffInGame(BuffIds.HarveyLantern))
                {
                    var effects = new StardewValley.Buffs.BuffEffects();
                    effects.Defense.Add(2);
                    _buffService.ApplyBuff(BuffIds.HarveyLantern, "Свет надежды", effects, -2);
                    Game1.addHUDMessage(new HUDMessage(
                        "«Свет надежды» — Харви рядом мысленно. Держись плана: две минуты, не больше.",
                        HUDMessage.newQuest_type));
                    _monitor.Log("[DarknessService] Шаг 3: Бафф 'Свет надежды' применен", LogLevel.Debug);
                }

                // Увеличиваем счетчик каждую секунду
                if (Game1.ticks % 60 == 0)
                {
                    _data.Darkness.MountainNightSeconds++;
                    
                    // Периодические сообщения
                    if (_data.Darkness.MountainNightSeconds % 30 == 0)
                    {
                        var messages = new[]
                        {
                            "Сердце колотится… Дыши. Харви сказал: коротко, по таймеру, потом домой.",
                            "Свет надежды — не игра в героиню. Он ждёт отчёта, не подвига.",
                            "Ещё немного. Ты не обязана победить страх навсегда за одну ночь."
                        };
                        Game1.addHUDMessage(new HUDMessage(messages[Game1.random.Next(messages.Length)], HUDMessage.newQuest_type));
                    }

                    if (_data.Darkness.MountainNightSeconds % 20 == 0)
                    {
                        RefreshTherapyQuestJournal(3);
                        Game1.addHUDMessage(new HUDMessage($"Прогресс: {_data.Darkness.MountainNightSeconds}/120 сек", HUDMessage.newQuest_type));
                        _monitor.Log($"[DarknessService] Шаг 3: Прогресс {_data.Darkness.MountainNightSeconds}/120 сек", LogLevel.Info);
                    }

                    // Проверяем завершение
                    if (_data.Darkness.MountainNightSeconds >= 120)
                    {
                        CompleteStep3();
                    }
                }
            }
            else
            {
                // Убираем бафф если покинули горы
                if (_stateService.HasBuffInGame(BuffIds.HarveyLantern))
                {
                    _buffService.RemoveBuff(BuffIds.HarveyLantern);
                }
            }
        }

        /// <summary>
        /// Завершить Шаг 3 - ПОЛНОЕ ИЗЛЕЧЕНИЕ!
        /// </summary>
        private void CompleteStep3()
        {
            _data.Darkness.CompletedStep3 = true;
            
            // Завершаем квест Шага 3
            var quest3 = Game1.player.questLog.FirstOrDefault(q => q.id.Value == QuestIds.DarknessStep3);
            if (quest3 != null)
            {
                quest3.questComplete();
                Game1.player.removeQuest(QuestIds.DarknessStep3);
            }
            
            // Снимаем бафф фонаря
            _buffService.RemoveBuff(BuffIds.HarveyLantern);
            
            // Завершаем терапию полностью
            CompleteTherapy();
            
            // Добавляем топик полного излечения
            ConversationHelper.AddTopic("topicDarknessFullyCured", 3);
        }

        // ===== Вспомогательные методы =====

        private string GetCurrentBuffId()
        {
            return _data.Darkness.FearLevel switch
            {
                1 => BuffIds.DarknessLevel1,
                2 => BuffIds.DarknessLevel2,
                3 => BuffIds.DarknessLevel3,
                _ => BuffIds.Darkness,
            };
        }

        private string GetFearLevelDisplayName()
        {
            return _data.Darkness.FearLevel switch
            {
                1 => "Боязнь темноты (легкая)",
                2 => "Боязнь темноты (сильная)",
                3 => "Фобия темноты",
                _ => "Боязнь темноты"
            };
        }

        private StardewValley.Buffs.BuffEffects GetFearLevelEffects(int level)
        {
            var effects = new StardewValley.Buffs.BuffEffects();
            
            switch (level)
            {
                case 1:
                    effects.Defense.Add(-1);
                    break;
                case 2:
                    effects.Defense.Add(-2);
                    break;
                case 3:
                    effects.Defense.Add(-3);
                    break;
            }
            
            return effects;
        }

        private void RemoveOtherFearLevelBuffs(string currentBuffId)
        {
            var allBuffIds = new[] { BuffIds.DarknessLevel1, BuffIds.DarknessLevel2, BuffIds.DarknessLevel3, BuffIds.Darkness };
            
            foreach (var buffId in allBuffIds)
            {
                if (buffId != currentBuffId && _stateService.HasBuffInGame(buffId))
                {
                    _buffService.RemoveBuff(buffId);
                }
            }
        }

        private void RemoveAllFearBuffs()
        {
            var allBuffIds = new[] { BuffIds.DarknessLevel1, BuffIds.DarknessLevel2, BuffIds.DarknessLevel3, BuffIds.Darkness };
            
            foreach (var buffId in allBuffIds)
            {
                _buffService.RemoveBuff(buffId);
            }
        }

        private void ShowFearLevelMessage()
        {
            switch (_data.Darkness.FearLevel)
            {
                case 1:
                    if (Game1.random.NextDouble() < 0.3) // 30% шанс
                        Game1.addHUDMessage(new HUDMessage(
                            "Темнота цепляет… Если страшно — дом и свет. Не проверяй себя специально.",
                            HUDMessage.error_type));
                    break;
                    
                case 2:
                    if (Game1.random.NextDouble() < 0.5) // 50% шанс
                    {
                        var messages = new[]
                        {
                            "Тени длиннее обычного… Харви бы сказал: дом, свет, не одна.",
                            "Сердце учащённо… После восьми — час при свете, если есть план.",
                            "Каждый шорох бьёт по нервам… Не уходи в ночь из упрямства."
                        };
                        Game1.addHUDMessage(new HUDMessage(messages[Game1.random.Next(messages.Length)], HUDMessage.error_type));
                    }
                    break;
                    
                case 3:
                    if (Game1.random.NextDouble() < 0.7) // 70% шанс
                    {
                        var messages = new[]
                        {
                            "Паника поднимается… Дом. Свет. Не геройствуй — Харви лечит, а не судит.",
                            "Темнота давит со всех сторон… Вернись туда, где светло и безопасно.",
                            "Сердце колотится… Ты не обязана победить страх в одиночку за ночь."
                        };
                        Game1.addHUDMessage(new HUDMessage(messages[Game1.random.Next(messages.Length)], HUDMessage.error_type));
                    }
                    break;
            }
        }

        private void SendHarveyWorryLetter()
        {
            if (_worryLetterSent || ConversationHelper.HasTopic("topicHarveyDarknessWorryLetterSent"))
                return;

            _questService.AddMailForTomorrow(MailIds.DarknessWorry);
            ConversationHelper.AddTopic("topicHarveyDarknessWorryLetterSent", 14);
            _worryLetterSent = true;
            _monitor.Log("[DarknessService] Письмо от Харви о беспокойстве (фobия) запланировано на завтра", LogLevel.Info);
        }

        public void RefreshTherapyQuestJournal(int stage)
        {
            var objective = stage switch
            {
                1 => BuildStep1ObjectiveForInstance(),
                2 => BuildStep2ObjectiveForInstance(),
                3 => BuildStep3ObjectiveForInstance(),
                _ => null,
            };

            if (objective == null)
                return;

            var questId = GetStepQuestId(stage);

            if (string.IsNullOrEmpty(questId))
                return;

            _questService.UpdateQuest(questId, objective: objective);
            _monitor.Log(
                $"[DarknessStep1] Quest journal updated: questId={questId}, objective=\"{objective.Replace('\n', '|')}\"",
                LogLevel.Trace);
        }

        private string BuildStep1ObjectiveForInstance()
        {
            var d = _data.Darkness;
            int evenings = d.SafeDarknessEveningsCompleted;
            int secondsToday = GetStep1DisplaySecondsToday();

            int required = Step1EveningsRequired;

            if (IsStep1AwaitingHarveyTalk())
                return DarknessTherapyCopy.Step1QuestObjectiveAwaitingHarvey(required);

            if (IsStep1TodayCredited())
                return DarknessTherapyCopy.Step1QuestObjectiveTodayCredited(evenings, required);

            return DarknessTherapyCopy.Step1QuestObjectiveInProgress(evenings, required, secondsToday);
        }

        private string BuildStep2ObjectiveForInstance()
        {
            var visited = _data.Darkness.SafeZonesVisited;
            var bus = visited.Contains("BusStop") ? "✅" : "⬜";
            var town = visited.Contains("Town") ? "✅" : "⬜";
            return
                $"Шаг 2 (20:00–22:00): {bus} Автобусная остановка, {town} Город ({visited.Count}/2).\n" +
                "Короткий выход, без геройства. Потом — разговор с Харви.";
        }

        private string BuildStep3ObjectiveForInstance()
            => $"Шаг 3: горы ночью {_data.Darkness.MountainNightSeconds}/120 сек (22:00–02:00).\n" +
               "С «светом надежды» Харви. После — обязательно поговорить с ним.";

        private void ApplyOvercomeBonus()
        {
            // Применяем перманентный бафф "Преодоление": +1 Defense ночью
            var effects = new StardewValley.Buffs.BuffEffects();
            effects.Defense.Add(1);
            _buffService.ApplyBuff(BuffIds.DarknessOvercome, "Преодоление", effects, -2);

            _monitor.Log("[DarknessService] ✅ Применен перманентный бонус 'Преодоление' (Defense +1 ночью)", LogLevel.Info);
        }

        /// <summary>Восстановление баффа/квестов темноты (делегирует в SyncDarknessState).</summary>
        public void RestoreFearBuff()
            => SyncDarknessState("RestoreFearBuff");

        /// <summary>DEV/MCP: установить FearLevel 1–3, синхронизировать level-buff и топики Харви.</summary>
        public bool ApplyDebugFearLevel(int level)
        {
            if (level is < 1 or > 3)
                return false;

            if (!Context.IsWorldReady)
            {
                _monitor.Log("[DarknessService] ApplyDebugFearLevel: world not ready", LogLevel.Warn);
                return false;
            }

            _data.Darkness.FearLevel = level;
            _data.Darkness.IsCured = false;
            _data.Darkness.HasOvercomeBonus = false;
            _data.Darkness.TherapyCompletedDate = null;

            _buffService.RemoveBuff(BuffIds.Darkness);
            _buffService.RemoveBuff(BuffIds.DarknessOvercome);
            RemoveAllFearBuffs();
            ApplyDebugHarveyTopicsForLevel(level);
            EnsureFearLevelBuff(showMessages: false);
            SyncStressLoadFromDarkness();

            _monitor.Log($"[DarknessService] ApplyDebugFearLevel: set to {level}", LogLevel.Info);
            return true;
        }

        /// <summary>DEV/MCP: прогресс шага 1 — вечера и минуты сегодня (конвертируются в секунды).</summary>
        public bool ApplyDebugStep1Progress(int evenings, int todayMinutes)
        {
            if (!Context.IsWorldReady)
            {
                _monitor.Log("[DarknessService] ApplyDebugStep1Progress: world not ready", LogLevel.Warn);
                return false;
            }

            if (!_data.Darkness.IsTherapyActive || _data.Darkness.TherapyStage != 1)
            {
                _monitor.Log(
                    "[DarknessService] ApplyDebugStep1Progress: therapy not active at stage 1 — call stress_darkness_start_therapy first",
                    LogLevel.Warn);
                return false;
            }

            var d = _data.Darkness;
            d.SafeDarknessMinutes = 0;
            d.SafeDarknessEveningsCompleted = Math.Clamp(evenings, 0, DarknessLegacyHelper.Step1EveningsRequired);
            d.SafeDarknessProgressToday = Math.Clamp(todayMinutes, 0, DarknessLegacyHelper.Step1MinutesPerEvening) * 60;
            d.DarknessTherapyTodayCompleted = todayMinutes >= DarknessLegacyHelper.Step1MinutesPerEvening;
            d.DarknessTherapyLastCompletedDay = d.DarknessTherapyTodayCompleted
                ? SDate.Now().DaysSinceStart
                : -1;
            d.LastSafeDarknessDate = SDate.Now();
            ResetStep1HudFlags();

            if (d.SafeDarknessEveningsCompleted >= Step1EveningsRequired)
                MarkStep1ReadyForHarveyTalk();
            else
                RefreshTherapyQuestJournal(1);

            _monitor.Log(
                $"[DarknessService] ApplyDebugStep1Progress: evenings={d.SafeDarknessEveningsCompleted}/{DarknessLegacyHelper.Step1EveningsRequired}, " +
                $"today={GetStep1DisplayMinutesToday()}/{DarknessLegacyHelper.Step1MinutesPerEvening} min, todayCompleted={d.DarknessTherapyTodayCompleted}",
                LogLevel.Info);
            return true;
        }

        public string BuildStep1DebugStatusLine()
        {
            var d = _data.Darkness;
            bool awaitingHarvey = IsStep1AwaitingHarveyTalk();
            var questId = QuestIds.DarknessStep1;
            var objective = DarknessLegacyHelper.GetStepQuestCurrentObjective(1)?.Replace('\n', '|') ?? "(n/a)";

            return
                $"questId={questId} activeEpisode={(d.IsTherapyActive ? "DarknessStep1" : "(none)")} " +
                $"active={d.IsTherapyActive} stage={d.TherapyStage} " +
                $"secondsToday={GetStep1DisplaySecondsToday()}/{DarknessLegacyHelper.Step1SecondsPerEvening} " +
                $"completedEvenings={d.SafeDarknessEveningsCompleted}/{Step1EveningsRequired} " +
                $"eveningCountedToday={IsStep1TodayCredited()} lastCountedDay={d.DarknessTherapyLastCompletedDay} " +
                $"awaitingHarveyReview={awaitingHarvey} topicReady={ConversationHelper.HasTopic(DarknessLegacyHelper.Step1ReadyForHarveyTopic)} " +
                $"objective={objective}";
        }

        public bool ApplyDebugAddStep1Minutes(int minutes)
        {
            if (!Context.IsWorldReady || !_data.Darkness.IsTherapyActive || _data.Darkness.TherapyStage != 1)
                return false;

            var before = BuildStep1DebugStatusLine();
            if (minutes <= 0)
                return false;

            TryMigrateStep1ProgressUnitsToSeconds();
            EnsureStep1CalendarDay();

            if (IsStep1AwaitingHarveyTalk())
            {
                _monitor.Log($"[DarknessService] add_minutes skipped (awaiting Harvey). before: {before}", LogLevel.Warn);
                return false;
            }

            int addSec = minutes * 60;
            int target = Math.Min(
                DarknessLegacyHelper.Step1SecondsPerEvening,
                _data.Darkness.SafeDarknessProgressToday + addSec);

            while (_data.Darkness.SafeDarknessProgressToday < target)
                IncrementStep1ProgressSecond();

            if (_data.Darkness.SafeDarknessProgressToday >= DarknessLegacyHelper.Step1SecondsPerEvening
                && !IsStep1TodayCredited()
                && CanCreditAnotherEveningToday())
                CompleteDarknessTherapyEvening();

            var after = BuildStep1DebugStatusLine();
            _monitor.Log($"[DarknessService] add_minutes +{minutes}: before [{before}] after [{after}]", LogLevel.Info);
            return true;
        }

        public bool ApplyDebugCompleteEvening()
        {
            if (!Context.IsWorldReady || !_data.Darkness.IsTherapyActive || _data.Darkness.TherapyStage != 1)
                return false;

            var before = BuildStep1DebugStatusLine();
            if (IsStep1AwaitingHarveyTalk())
                return false;

            _data.Darkness.SafeDarknessProgressToday = DarknessLegacyHelper.Step1SecondsPerEvening;
            CompleteDarknessTherapyEvening();
            var after = BuildStep1DebugStatusLine();
            _monitor.Log($"[DarknessService] complete_evening: before [{before}] after [{after}]", LogLevel.Info);
            return true;
        }

        public bool ApplyDebugResetStep1Therapy()
        {
            if (!Context.IsWorldReady)
                return false;

            var before = BuildStep1DebugStatusLine();
            var d = _data.Darkness;
            d.SafeDarknessEveningsCompleted = 0;
            d.SafeDarknessProgressToday = 0;
            d.SafeDarknessMinutes = 0;
            d.DarknessTherapyTodayCompleted = false;
            d.DarknessTherapyLastCompletedDay = -1;
            d.LastSafeDarknessDate = SDate.Now();
            d.CompletedStep1 = false;
            ConversationHelper.RemoveTopic(DarknessLegacyHelper.Step1ReadyForHarveyTopic);
            ResetStep1HudFlags();
            _step1ElapsedMs = 0;

            if (d.IsTherapyActive && d.TherapyStage == 1)
                RefreshTherapyQuestJournal(1);

            var after = BuildStep1DebugStatusLine();
            _monitor.Log($"[DarknessService] reset_therapy: before [{before}] after [{after}]", LogLevel.Info);
            return true;
        }

        /// <summary>DEV/MCP: миграция legacy SafeDarknessMinutes → evenings/today.</summary>
        public bool ApplyDebugStep1ProgressLegacy(int legacyMinutes)
        {
            if (!Context.IsWorldReady)
            {
                _monitor.Log("[DarknessService] ApplyDebugStep1ProgressLegacy: world not ready", LogLevel.Warn);
                return false;
            }

            if (!_data.Darkness.IsTherapyActive || _data.Darkness.TherapyStage != 1)
                return false;

            var d = _data.Darkness;
            d.SafeDarknessMinutes = Math.Max(0, legacyMinutes);
            d.SafeDarknessEveningsCompleted = 0;
            d.SafeDarknessProgressToday = 0;
            TryMigrateLegacyStep1Progress();
            RefreshTherapyQuestJournal(1);
            return true;
        }

        public string BuildDebugSnapshot()
            => DarknessDebugReporter.BuildMcpSnapshot(_data, _stateService, _questService);

        private static void ApplyDebugHarveyTopicsForLevel(int level)
        {
            ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
            ConversationHelper.RemoveTopic("topicStressDarknessLevel2");
            ConversationHelper.RemoveTopic("topicStressDarknessLevel3");
            ConversationHelper.RemoveTopic("topicStressDarknessSerious");
            ConversationHelper.RemoveTopic("topicStressDarknessPhobia");

            switch (level)
            {
                case 1:
                    ConversationHelper.AddTopic(TopicIds.StressDarkness, 7);
                    break;
                case 2:
                    ConversationHelper.AddTopic("topicStressDarknessLevel2", 7);
                    break;
                case 3:
                    ConversationHelper.AddTopic("topicStressDarknessLevel3", 0);
                    break;
            }
        }
    }
}


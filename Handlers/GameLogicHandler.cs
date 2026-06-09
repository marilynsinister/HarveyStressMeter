using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using HarveyStressMeter.Services;
using HarveyStressMeter.Models;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using StardewModdingAPI.Utilities;

namespace HarveyStressMeter.Handlers
{
    /// <summary>
    /// Handles game logic: stress triggers, dialogues, quests
    /// Follows Single Responsibility Principle - only game mechanics
    /// </summary>
    public class GameLogicHandler
    {
        private readonly SaveData _data;
        private readonly IMonitor _monitor;
        private readonly TreatmentService _treatmentService;
        private readonly TriggerService _triggerService;
        private readonly BuffService _buffService;
        private readonly StateService _stateService;
        private readonly DarknessService _darknessService;
        private readonly DarknessRemissionService _darknessRemissionService;
        private readonly StressDialogueService _stressDialogueService;
        private readonly StressTreatmentReviewService _stressTreatmentReviewService;
        private readonly StressLoadService _stressLoadService;
        private readonly ThunderFlashbackService _thunderFlashbackService;
        private readonly HarveyFlashbackRescueService _harveyFlashbackRescueService;
        private readonly HarveyCareTrustService _harveyCareTrustService;
        private readonly HarveySafePersonAuraService _harveySafePersonAuraService;
        private readonly SocialExposureService _socialExposureService;
        private readonly StressGameplayEffectService _stressGameplayEffectService;
        private readonly EpisodeQuestProgressService? _episodeQuestProgressService;
        private SocialAnxietyTherapyService? _socialAnxietyTherapyService;

        private string? _lastDialogueNpc;
        /// <summary>Один stress debuff за MenuChanged/DialogueBox cycle с Харви.</summary>
        private bool _harveyStressDialogueCycleHandled;
        /// <summary>topicDarknessTherapyStart уже был до текущего разговора (не CP #$t в этом диалоге).</summary>
        private bool _hadDarknessTherapyTopicAtTalkStart;

        // ⭐ ОПТИМИЗАЦИЯ: Кэширование и интервальные проверки
        private bool _lastHarveyNearby = false;
        private int _harveyCheckCounter = 0;
        private const int HARVEY_CHECK_INTERVAL = 1; // Каждые 1 секунды

        private int _progressUpdateCounter = 0;
        private const int PROGRESS_UPDATE_INTERVAL = 1; // Каждые 1 секунд

        private int _tiredCheckCounter = 0;
        private const int TIRED_CHECK_INTERVAL = 10; // Каждые 10 секунд

        private bool _hasAnyStressBuffCached = false;
        private int _stressBuffCacheCounter = 0;
        private const int STRESS_BUFF_CACHE_INTERVAL = 5; // Каждые 5 секунд

        // Static mapping to avoid recreating dictionary on each call (DRY principle)
        private static readonly Dictionary<string, (string buffId, string questId, string displayName, bool isTreatmentTopic)> TopicMapping = new()
        {
            [TopicIds.StressTired] = (BuffIds.Tired, QuestIds.Tired, "Усталость", false),
            [TopicIds.StressLonely] = (BuffIds.Lonely, QuestIds.Lonely, "Одиночество", false),
            [TopicIds.StressThunder] = (BuffIds.Thunder, QuestIds.Thunder, "Страх грозы", false),
            [TopicIds.StressHunger] = (BuffIds.Hunger, QuestIds.Hunger, "Голод", false),
            [TopicIds.StressOverwork] = (BuffIds.Overwork, QuestIds.Overwork, "Переработка", false),
            [TopicIds.StressNoSleep] = (BuffIds.NoSleep, QuestIds.NoSleep, "Недосып", false),
            [TopicIds.StressTooCold] = (BuffIds.TooCold, QuestIds.TooCold, "Переохлаждение", false),
            [TopicIds.StressSocial] = (BuffIds.Social, QuestIds.Social, "Социальный дискомфорт", false),
            [TopicIds.StressDarkness] = (BuffIds.Darkness, QuestIds.Darkness, "Темнота", false),

            [TopicIds.TreatmentFollowupTired] = (BuffIds.Tired, QuestIds.Tired, "Усталость", true),
            [TopicIds.TreatmentFollowupLonely] = (BuffIds.Lonely, QuestIds.Lonely, "Одиночество", true),
            [TopicIds.TreatmentFollowupThunder] = (BuffIds.Thunder, QuestIds.Thunder, "Страх грозы", true),
            [TopicIds.TreatmentFollowupHunger] = (BuffIds.Hunger, QuestIds.Hunger, "Голод", true),
            [TopicIds.TreatmentFollowupOverwork] = (BuffIds.Overwork, QuestIds.Overwork, "Переработка", true),
            [TopicIds.TreatmentFollowupNoSleep] = (BuffIds.NoSleep, QuestIds.NoSleep, "Недосып", true),
            [TopicIds.TreatmentFollowupTooCold] = (BuffIds.TooCold, QuestIds.TooCold, "Переохлаждение", true),
            [TopicIds.TreatmentFollowupSocial] = (BuffIds.Social, QuestIds.Social, "Социальный дискомфорт", true),
            [TopicIds.TreatmentFollowupDarkness] = (BuffIds.Darkness, QuestIds.Darkness, "Темнота", true),
        };

        public GameLogicHandler(
            SaveData data,
            IMonitor monitor,
            TreatmentService treatmentService,
            TriggerService triggerService,
            BuffService buffService,
            StateService stateService,
            DarknessService darknessService,
            DarknessRemissionService darknessRemissionService,
            StressDialogueService stressDialogueService,
            StressTreatmentReviewService stressTreatmentReviewService,
            StressLoadService stressLoadService,
            ThunderFlashbackService thunderFlashbackService,
            HarveyFlashbackRescueService harveyFlashbackRescueService,
            HarveyCareTrustService harveyCareTrustService,
            HarveySafePersonAuraService harveySafePersonAuraService,
            SocialExposureService socialExposureService,
            StressGameplayEffectService stressGameplayEffectService,
            EpisodeQuestProgressService? episodeQuestProgressService = null)
        {
            _data = data;
            _monitor = monitor;
            _treatmentService = treatmentService;
            _triggerService = triggerService;
            _buffService = buffService;
            _stateService = stateService;
            _darknessService = darknessService;
            _darknessRemissionService = darknessRemissionService;
            _stressDialogueService = stressDialogueService;
            _stressTreatmentReviewService = stressTreatmentReviewService;
            _stressLoadService = stressLoadService;
            _thunderFlashbackService = thunderFlashbackService;
            _harveyFlashbackRescueService = harveyFlashbackRescueService;
            _harveyCareTrustService = harveyCareTrustService;
            _harveySafePersonAuraService = harveySafePersonAuraService;
            _socialExposureService = socialExposureService;
            _stressGameplayEffectService = stressGameplayEffectService;
            _episodeQuestProgressService = episodeQuestProgressService;
        }

        /// <summary>Сбрасывает RAM-состояние programmatic stress-диалога (pending auto-start).</summary>
        public void ClearStressDialoguePending()
        {
            _stressDialogueService.ClearPendingTreatment();
            _stressTreatmentReviewService.ClearPendingReview();
        }

        public void SetSocialAnxietyTherapyService(SocialAnxietyTherapyService socialAnxietyTherapyService)
            => _socialAnxietyTherapyService = socialAnxietyTherapyService;

        public void RepairStuckSocialTreatments()
            => _triggerService.RepairStuckSocialTreatments();

        public void MigrateSocialExposure()
            => _socialExposureService.MigrateLegacyExposure();

        public void ResetDailyData()
        {
            // ⭐ НОВОЕ: Проверяем топики вчерашнего дня ПЕРЕД очисткой
            // Это нужно для подсчета дней без разговоров/еды
            UpdateConsecutiveDaysCounters();

            _data.TalkedNpcsToday.Clear();
            _data.OverworkBreaksToday = 0;
            _data.OverworkBreakSeconds = 0;
            _data.OverworkBreakActive = false;
            _data.TalkedToHarveyToday = false;

            _socialExposureService.ResetDaily();

            ResetDailyQuestCounters();
            ApplyPendingNoSleepLateObjective();

            _thunderFlashbackService.ResetDailyState();
            _harveyFlashbackRescueService.ResetDailyState();
            _harveyCareTrustService.OnDayStarted();
        }

        public void UpdateDailyDarknessState()
            => _darknessService.UpdateDailyFearState();

        public void CheckDayStartedStressTriggers()
        {
            // ⭐ ИСПРАВЛЕНО: Tired проверка убрана из начала дня
            // В начале дня stamina всегда полная, поэтому проверка Stamina <= 10 никогда не сработает
            // Проверка перенесена в CheckTiredStressTrigger() - вызывается в течение дня

            // Thunder - lightning
            if (Game1.stats.DaysPlayed >= 2
                && Game1.isLightning
                && !_stateService.HasActiveTreatmentState(BuffIds.Thunder)
                && !_stateService.HasImmunity(BuffIds.Thunder))
            {
                _treatmentService.ApplyStressBuff(BuffIds.Thunder, "Страх грозы");
            }

            // TooCold — проверяется при смене времени (вечер) и warp (см. TryApplyTooColdStressTrigger)

            // ⭐ НОВОЕ: Lonely - несколько дней без разговоров
            if (Game1.stats.DaysPlayed >= 3
                && _data.DaysWithoutTalking >= 3
                && !_stateService.HasActiveTreatmentState(BuffIds.Lonely)
                && !_stateService.HasImmunity(BuffIds.Lonely))
            {
                _treatmentService.ApplyStressBuff(BuffIds.Lonely, "Одиночество");
                _monitor.Log($"[Lonely Stress] Триггер активирован: {_data.DaysWithoutTalking} дней без разговоров", LogLevel.Info);
            }

            // ⭐ НОВОЕ: Hunger - несколько дней без еды
            if (Game1.stats.DaysPlayed >= 3
                && _data.DaysWithoutEating >= 2
                && !_stateService.HasActiveTreatmentState(BuffIds.Hunger)
                && !_stateService.HasImmunity(BuffIds.Hunger))
            {
                _treatmentService.ApplyStressBuff(BuffIds.Hunger, "Слабость от голода");
                _monitor.Log($"[Hunger Stress] Триггер активирован: {_data.DaysWithoutEating} дней без еды", LogLevel.Info);
            }

            // ⭐ НОВОЕ: NoSleep - несколько дней позднего сна
            if (Game1.stats.DaysPlayed >= 3
                && _data.DaysWithLateSleep >= 3
                && !_stateService.HasActiveTreatmentState(BuffIds.NoSleep)
                && !_stateService.HasImmunity(BuffIds.NoSleep))
            {
                _treatmentService.ApplyStressBuff(BuffIds.NoSleep, "Недосып");
                _monitor.Log($"[NoSleep Stress] Триггер активирован: {_data.DaysWithLateSleep} дней позднего сна", LogLevel.Info);
            }
        }

        public void ProcessGameTick(bool _)
        {
            if (!Context.IsWorldReady)
                return;

            var harveyNearby = _harveySafePersonAuraService.IsHarveyWithinCareAuraRange();

            // ⭐ ОПТИМИЗАЦИЯ: Интервальные счетчики
            _harveyCheckCounter++;
            _progressUpdateCounter++;
            _tiredCheckCounter++;
            _stressBuffCacheCounter++;

            // === КАЖДЫЕ 3 СЕКУНДЫ: Harvey proximity (66% ⬇️) ===
            if (_harveyCheckCounter >= HARVEY_CHECK_INTERVAL)
            {
                _harveyCheckCounter = 0;
                if (_buffService.HasBuff(BuffIds.CareAura))
                    _buffService.RemoveBuff(BuffIds.CareAura);
                _lastHarveyNearby = harveyNearby;
            }
            else
            {
                // Используем кэшированное значение для других проверок
                harveyNearby = _lastHarveyNearby;
            }

            // === КАЖДУЮ СЕКУНДУ: прогресс лечения + manual triggers ===
            if (_progressUpdateCounter >= PROGRESS_UPDATE_INTERVAL)
            {
                _progressUpdateCounter = 0;

                // Обновляем прогресс терапии темноты
                _darknessService.UpdateTherapyProgress();

                // Обновляем прогресс лечений (самый важный процесс)
                if (_data.StressState.ActiveTreatments.Count > 0)
                {
                    _triggerService.UpdateTreatmentProgress(harveyNearby);
                    _treatmentService.EnsureLockedBuffsPersist();
                }

                _episodeQuestProgressService?.UpdateActiveEpisode(harveyNearby);

                // Manual triggers (Tired/Thunder/Overwork/Social complete paths) — раз в секунду, как в backup
                if (ShouldRunManualTriggers())
                    _triggerService.CheckManualTriggers();

                // Thunder calming buff (только если квест активен)
                if (_stateService.HasActiveQuestState(QuestIds.Thunder))
                {
                    ApplyThunderCalmingBuff(harveyNearby);
                }

                // Natural buff removal (только если есть активные баффы)
                if (_data.StressState.ActiveTreatments.Count > 0 || GetHasAnyStressBuff())
                {
                    NaturalBuffRemoval(harveyNearby);
                }

                if (_thunderFlashbackService.State.IsActive)
                    _thunderFlashbackService.UpdateActiveFlashback(1);

                if (Game1.isLightning)
                    _thunderFlashbackService.UpdateRelapseMonitoring(Game1.timeOfDay);

                _harveyFlashbackRescueService.Update(1);

                _socialExposureService.UpdateRecovery(harveyNearby);

                _stressGameplayEffectService.UpdateEffects();
            }

            // === КАЖДЫЕ 10 СЕКУНД: Медленные проверки (90% ⬇️) ===
            if (_tiredCheckCounter >= TIRED_CHECK_INTERVAL)
            {
                _tiredCheckCounter = 0;

                // Пересчёт StressLoad (гроза, комбо-модификаторы)
                if (GetHasAnyStressBuff())
                    _stressLoadService.SyncFromGameState();

                // Проверяем усталость в течение дня
                if (!_stateService.HasActiveTreatmentState(BuffIds.Tired))
                {
                    CheckTiredStressTrigger();
                }
            }
        }

        /// <summary>
        /// Manual triggers не должны срабатывать во время event script или с открытым меню.
        /// </summary>
        private static bool ShouldRunManualTriggers()
        {
            if (GameStateHelper.IsEventActive())
                return false;
            if (Game1.activeClickableMenu != null)
                return false;
            return true;
        }

        /// <summary>
        /// ⭐ ОПТИМИЗАЦИЯ: Кэшированная проверка наличия стрессовых баффов
        /// Обновляется каждые 5 секунд вместо каждого вызова
        /// </summary>
        private bool GetHasAnyStressBuff()
        {
            // Обновляем кэш каждые N секунд
            if (_stressBuffCacheCounter >= STRESS_BUFF_CACHE_INTERVAL)
            {
                _stressBuffCacheCounter = 0;
                _hasAnyStressBuffCached = HasAnyStressBuff();
            }

            return _hasAnyStressBuffCached;
        }

        /// <summary>
        /// Сбрасывает счетчики оптимизации (вызывается при смене дня)
        /// </summary>
        public void ResetOptimizationCounters()
        {
            _harveyCheckCounter = 0;
            _progressUpdateCounter = 0;
            _tiredCheckCounter = 0;
            _stressBuffCacheCounter = 0;
            _hasAnyStressBuffCached = false;
            _lastHarveyNearby = false;
        }

        /// <summary>
        /// Быстрая проверка наличия любого стрессового баффа
        /// </summary>
        private bool HasAnyStressBuff()
        {
            return _stateService.HasBuffInGame(BuffIds.Tired)
                || _stateService.HasBuffInGame(BuffIds.Lonely)
                || _stateService.HasBuffInGame(BuffIds.Thunder)
                || _stateService.HasBuffInGame(BuffIds.Hunger)
                || _stateService.HasBuffInGame(BuffIds.TooCold)
                || _stateService.HasBuffInGame(BuffIds.Social)
                || DarknessLegacyHelper.HasAnyDarknessStressBuffInGame(_stateService);
        }

        public void HandleMenuChanged(MenuChangedEventArgs e)
        {
            HandleDialogueEvents(e);
        }

        public void HandleWarped(WarpedEventArgs e)
        {
            if (e.NewLocation == null) return;

            TryBlockPhobiaHouseExit(e);

            // Используем новую систему уровней страха темноты
            _darknessService.CheckAndApplyDarknessBuff(e.NewLocation);
            
            // Обрабатываем посещение локации для терапии (Шаг 2)
            _darknessService.HandleLocationVisit(e.NewLocation.NameOrUniqueName);
            
            ApplyQuestLocationBuffs(e.NewLocation);
            TryApplyTooColdStressTrigger();

            _thunderFlashbackService.OnLocationChanged(
                e.OldLocation?.NameOrUniqueName,
                e.NewLocation.NameOrUniqueName);

            _harveyFlashbackRescueService.OnLocationChanged(
                e.OldLocation?.NameOrUniqueName,
                e.NewLocation.NameOrUniqueName);

            _episodeQuestProgressService?.OnPlayerWarped(e.NewLocation.NameOrUniqueName);
            _stressLoadService.SyncFromGameState();
        }

        /// <summary>Фobия (уровень 3): блок выхода из дома ночью, кроме терапии шагов 2–3.</summary>
        private void TryBlockPhobiaHouseExit(WarpedEventArgs e)
        {
            if (e.OldLocation is not StardewValley.Locations.FarmHouse)
                return;

            if (e.NewLocation is StardewValley.Locations.FarmHouse)
                return;

            if (Game1.eventUp || Game1.CurrentEvent != null)
                return;

            if (_darknessService.CanLeaveHouseAtNight())
                return;

            // Возврат к двери фермерского дома (ванильная точка выхода)
            Game1.warpFarmer("FarmHouse", 3, 24, 0);
            _darknessService.ShowCannotLeaveMessage();
            _monitor.Log("[DarknessTherapy] Выход из дома заблокирован (фobия, уровень 3)", LogLevel.Debug);
        }

        public void HandleTimeChanged(TimeChangedEventArgs e)
        {
            if (e.NewTime / 100 != e.OldTime / 100)
                _stressLoadService.ApplyHourlyDecay();

            // Obmorok from tiredness (at 2:00 am = 2600 in 26-hour time)
            if (e.NewTime == 2600 && Game1.player.Stamina >= 0 && Game1.player.Stamina <= 5)
            {
                if (Game1.stats.DaysPlayed >= 1
                    && !_stateService.HasActiveTreatmentState(BuffIds.Overwork)
                    && !_stateService.HasImmunity(BuffIds.Overwork))
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Overwork, "Переработка");
                }
            }

            if (e.NewTime >= 2100 && e.OldTime < 2100)
                TryApplyTooColdStressTrigger();

            // Lightning check every 10 minutes during storm
            if (Game1.isLightning && e.NewTime % 100 == 0)
            {
                CheckLightningStressTrigger();
                _thunderFlashbackService.OnTimeChanged(e.OldTime, e.NewTime);
            }

            _harveySafePersonAuraService.OnTimeChanged(e.OldTime, e.NewTime);
        }

        public void CheckDayEndingQuestCompletion()
        {
            _episodeQuestProgressService?.OnDayEnding(Game1.timeOfDay);

            if (!_stateService.HasActiveQuestState(QuestIds.NoSleep))
                return;

            var noSleepTreatment = GetTreatmentByQuest(QuestIds.NoSleep);
            if (noSleepTreatment?.AwaitingHarveyReview == true)
                return;

            int bedtime = Game1.timeOfDay;
            if (bedtime >= 600 && bedtime <= 2200)
            {
                _treatmentService.UpdateTreatmentObjective(BuffIds.NoSleep, LegacyTreatmentObjectives.NoSleepDone);
                _treatmentService.MarkTreatmentReadyForReview(BuffIds.NoSleep);
                Game1.playSound("questcomplete");
            }
            else if (bedtime > 2200)
            {
                _data.NoSleepLateObjectivePending = true;
            }

            // Darkness — legacy HarveyMod_DarknessRecovery отключён при уровневой системе
        }

        /// <summary>
        /// ⭐ НОВОЕ: Отслеживает паттерн позднего отхода ко сну
        /// Вызывается в конце дня перед сохранением
        /// </summary>
        public void CheckLateSleepPattern()
        {
            int currentTime = Game1.timeOfDay;
            bool wentToSleepLate = false;

            // Поздний сон: после полуночи до 2:00 (2400–2600 в 26-hour time)
            if (GameStateHelper.IsAfterMidnightUntilTwoAm())
            {
                wentToSleepLate = true;
                _monitor.Log($"[LateSleep] Игрок лег спать поздно: {currentTime}", LogLevel.Info);
            }
            // ⭐ ИСПРАВЛЕНО: Проверяем критически низкую выносливость (0-5) как признак обморока от усталости
            else if (Game1.player.Stamina >= 0 && Game1.player.Stamina <= 5)
            {
                wentToSleepLate = true;
                _monitor.Log($"[LateSleep] Игрок упал от усталости (stamina={Game1.player.Stamina})", LogLevel.Info);
            }
            
            if (wentToSleepLate)
            {
                _data.DaysWithLateSleep++;
                _monitor.Log($"[LateSleep] Счетчик: {_data.DaysWithLateSleep} дней позднего сна подряд", LogLevel.Info);
            }
            else
            {
                // Лег спать вовремя - сбрасываем счетчик
                if (_data.DaysWithLateSleep > 0)
                {
                    _monitor.Log($"[LateSleep] Игрок лег спать вовремя ({currentTime}) - счетчик сброшен", LogLevel.Info);
                }
                _data.DaysWithLateSleep = 0;
            }
        }

        private void HandleDialogueEvents(MenuChangedEventArgs e)
        {
            // Во время scripted event реплики NPC (speak Harvey/...) — не разговор игрока с NPC.
            // Иначе каждая строка события перехватывается programmatic stress-диалогом.
            if (GameStateHelper.IsEventActive())
                return;

            if (e.NewMenu is DialogueBox && Game1.currentSpeaker is NPC npc && npc.Name != "Harvey")
            {
                _lastDialogueNpc = npc.Name;
                CheckSocialStressTrigger(npc);
            }
            else if (e.NewMenu is DialogueBox && Game1.currentSpeaker is NPC harveyNpc && harveyNpc.Name == "Harvey")
            {
                _lastDialogueNpc = "Harvey";
                HandleHarveyDialogue(harveyNpc, e);
            }
            else if (e.OldMenu is DialogueBox && _lastDialogueNpc != null && _lastDialogueNpc != "Harvey")
            {
                HandleDialogueEnd();
            }
            else if (e.OldMenu is DialogueBox && _lastDialogueNpc == "Harvey")
            {
                if (_stressDialogueService.TryShowDeferredStressDialogue())
                    return;

                HandleHarveyDialogueEnd();
                _lastDialogueNpc = null;
            }
        }

        private void HandleHarveyDialogue(NPC harveyNpc, MenuChangedEventArgs e)
        {
            _hadDarknessTherapyTopicAtTalkStart = ConversationHelper.HasTopic("topicDarknessTherapyStart");
            _darknessService.CaptureStep1ReadyTopicAtHarveyTalkStart();

            if (!CanRunStressDialoguePipeline(harveyNpc))
                return;

            if (_stressDialogueService.IsShowingStressDialogue
                || _stressDialogueService.HasDeferredStressDialogue)
            {
                _monitor.Log("[Диалог] Programmatic/deferred stress dialogue — fallback topics skipped", LogLevel.Debug);
                return;
            }

            if (_harveyStressDialogueCycleHandled)
            {
                _monitor.Log("[Диалог] Harvey stress dialogue cycle already handled — skip", LogLevel.Debug);
                return;
            }

            _monitor.Log($"[Диалог] Начался разговор с Харви. Текущие топики: {string.Join(", ", Game1.player.activeDialogueEvents.Keys.Where(k => k.Contains("Stress")))}", LogLevel.Info);
            _monitor.Log($"[Диалог] Дебафф Social активен: {_stateService.HasActiveTreatmentState(BuffIds.Social)}", LogLevel.Info);

            if (_stressDialogueService.ShouldShowStressDialogue(out var buffId, out var dialogueText))
            {
                _monitor.Log($"[Диалог] Programmatic stress dialogue selected: buffId={buffId}", LogLevel.Debug);
                _monitor.Log($"[Диалог] Обнаружен активный дебафф {buffId} без лечения.", LogLevel.Info);

                _stressDialogueService.ShowStressDialogue(buffId!, dialogueText!);
                if (string.Equals(buffId, BuffIds.Social, StringComparison.Ordinal)
                    && _socialAnxietyTherapyService?.IsReadyToComplete == true)
                {
                    _socialAnxietyTherapyService.OnFollowupDialogueStarted();
                }

                _lastDialogueNpc = "Harvey";
                _harveyStressDialogueCycleHandled = true;
                return;
            }

            if (_stressTreatmentReviewService.TryArmReviewCompletionOnHarveyTalk(out var reviewBuffId))
            {
                if (string.Equals(reviewBuffId, BuffIds.Social, StringComparison.Ordinal))
                {
                    _monitor.Log(
                        "[Диалог] Social review — CP topic path skipped (programmatic follow-up only)",
                        LogLevel.Debug);
                }
                else
                {
                    _monitor.Log(
                        $"[Диалог] Treatment review via CP topic (buff={reviewBuffId}) — vanilla dialogue, completion after close",
                        LogLevel.Info);
                }

                _lastDialogueNpc = "Harvey";
                _harveyStressDialogueCycleHandled = true;
                return;
            }

            _monitor.Log("[Диалог] No programmatic stress dialogue; fallback topics allowed", LogLevel.Debug);

            CheckAllStressDebuffsAndAddTopics();
            CheckDarknessDebuffsAndAddTopics();
            _harveyStressDialogueCycleHandled = true;
        }

        /// <summary>
        /// Fallback: добавляет topicStressXXX только для одного primary untreated debuff по приоритету.
        /// </summary>
        private void CheckAllStressDebuffsAndAddTopics()
        {
            if (!CanRunStressDialoguePipeline())
                return;

            if (_stressDialogueService.IsShowingStressDialogue)
            {
                _monitor.Log("[HandleHarveyDialogue] Fallback topics skipped: programmatic stress dialogue is active", LogLevel.Debug);
                return;
            }

            var untreated = StressDebuffSelector.GetUntreatedDebuffs(_stateService, _data);
            if (untreated.Count == 0)
            {
                _monitor.Log("[HandleHarveyDialogue] Fallback topics skipped: no untreated stress debuffs", LogLevel.Debug);
                return;
            }

            var primary = untreated[0];

            if (untreated.Count > 1)
            {
                _monitor.Log(
                    $"[StressDialogue] Multiple active stress debuffs found: {string.Join(", ", untreated)}. Fallback topic only for: {primary}",
                    LogLevel.Debug);
            }

            if (_stateService.WasTreatmentOfferShownToday(primary))
            {
                _monitor.Log(
                    $"[HandleHarveyDialogue] Fallback topic skipped: programmatic offer already shown today for {primary}",
                    LogLevel.Debug);
                return;
            }

            if (!_stateService.HasActiveTreatmentState(primary))
                return;

            _treatmentService.AddTopicForBuff(primary);
        }

        private void HandleHarveyDialogueEnd()
        {
            _harveyStressDialogueCycleHandled = false;

            _monitor.Log($"[Диалог] Завершен разговор с Харви", LogLevel.Info);

            // Защита: scripted event не должен доходить сюда (HandleDialogueEvents пропускает event).
            if (GameStateHelper.IsEventActive())
            {
                _monitor.Log($"[Диалог] Разговор во время события - не засчитывается", LogLevel.Debug);
                return;
            }

            // Auto-start treatment/quest after programmatic stress start dialogue closes.
            _stressDialogueService.CheckAndStartTreatmentAfterDialogue();

            // CP level 2/3: #$t topicDarknessTherapyStart добавляет топик в конце диалога — старт только тогда.
            TryStartDarknessTherapyFromCpDialogue();

            _hadDarknessTherapyTopicAtTalkStart = false;

            if (_darknessService.TryFinalizeStep1AfterHarveyTalk())
            {
                _monitor.Log("[DarknessTherapy] Шаг 1 закрыт после разговора с Харви (3/3 вечера)", LogLevel.Info);
            }

            _darknessRemissionService.OnHarveyTalkEnded();

            // Финальное завершение лечения после programmatic review-диалога.
            _stressTreatmentReviewService.OnReviewDialogueClosed();

            _socialAnxietyTherapyService?.OnQuestCompletedIfTreatmentFinished();

            _harveyCareTrustService.TryAwardSupportiveTalk();

            UpdateSocialQuestProgress(showUiMessage: false);
            if (_lastDialogueNpc == "Harvey")
                _triggerService.RegisterSocialShutdownNpcTalk("Harvey");

            if (!ConversationHelper.HasTopic(TopicIds.SpokeToday))
            {
                ConversationHelper.AddTopic(TopicIds.SpokeToday, 1);
            }
        }

        private void HandleDialogueEnd()
        {
            // ⭐ НОВОЕ: Не засчитываем разговоры во время событий
            if (GameStateHelper.IsEventActive())
            {
                _monitor.Log($"[Диалог] Разговор во время события - не засчитывается", LogLevel.Debug);
                _lastDialogueNpc = null;
                return;
            }

            if (_lastDialogueNpc != "Harvey")
            {
                if (_lastDialogueNpc != null)
                {
                    _data.TalkedNpcsToday.Add(_lastDialogueNpc);
                    _monitor.Log($"[Диалог] Завершен разговор с {_lastDialogueNpc}. Всего разговоров сегодня: {_data.TalkedNpcsToday.Count}", LogLevel.Info);
                    _triggerService.RegisterSocialShutdownNpcTalk(_lastDialogueNpc);
                }

                UpdateLonelyQuestProgress();
                UpdateSocialQuestProgress(showUiMessage: true);
            }
            else
            {
                _monitor.Log($"[Диалог] Завершен разговор с Харви (не учитывается в счетчике)", LogLevel.Debug);
                UpdateSocialQuestProgress(showUiMessage: false);
            }

            _lastDialogueNpc = null;

            if (!ConversationHelper.HasTopic(TopicIds.SpokeToday))
            {
                ConversationHelper.AddTopic(TopicIds.SpokeToday, 1);
            }
        }

        private void CheckSocialStressTrigger(NPC npc)
            => _socialExposureService.OnNpcConversationStarted(npc);

        private void UpdateLonelyQuestProgress()
        {
            var lonelyTreatment = GetTreatmentByQuest(QuestIds.Lonely);
            if (!_stateService.HasActiveQuestState(QuestIds.Lonely) || lonelyTreatment?.Progress == null)
                return;

            if (lonelyTreatment.AwaitingHarveyReview)
                return;

            lonelyTreatment.Progress.TalkedUniqueToday = _data.TalkedNpcsToday.Count;
            int talked = lonelyTreatment.Progress.TalkedUniqueToday;

            _treatmentService.UpdateTreatmentObjective(BuffIds.Lonely, LegacyTreatmentObjectives.Lonely(talked));

            Game1.addHUDMessage(new HUDMessage($"+1 общение ({talked}/3)", 2));

            if (talked >= 3)
            {
                Game1.playSound("questcomplete");
                _treatmentService.MarkTreatmentReadyForReview(BuffIds.Lonely);
            }
        }

        private void UpdateSocialQuestProgress(bool showUiMessage = false)
        {
            if (!_data.StressState.HasActiveQuest(QuestIds.Social)) return;

            var socialTreatment = GetTreatmentByQuest(QuestIds.Social);
            if (socialTreatment?.Progress == null) return;

            int baseConversations = socialTreatment.Progress.TalkedUniqueToday;
            int currentTotal = _data.TalkedNpcsToday.Count;
            int conversationsAfterQuest = Math.Max(0, currentTotal - baseConversations);

            if (socialTreatment.Progress.SocialTalksAfterQuest != conversationsAfterQuest)
            {
                socialTreatment.Progress.SocialTalksAfterQuest = conversationsAfterQuest;
                _triggerService.UpdateQuestDescription(socialTreatment.Progress);

                // Показываем сообщение о прогрессе только если запрошено (при завершении диалогов)
                if (showUiMessage)
                {
                    string progressText = socialTreatment.Progress.GetSocialProgressText();
                    Game1.addHUDMessage(new HUDMessage(progressText, HUDMessage.newQuest_type));
                }
            }
        }

        private void ApplyThunderCalmingBuff(bool harveyNearby)
        {
            bool shouldApply = harveyNearby
                && Game1.player.currentLocation?.NameOrUniqueName == "Hospital"
                && (Game1.isLightning || Game1.isRaining);

            if (shouldApply)
            {
                if (!_buffService.HasBuff(BuffIds.CalmingAtHospital))
                {
                    _buffService.ApplyBuff(BuffIds.CalmingAtHospital, "Успокоение с Харви",
                        new StardewValley.Buffs.BuffEffects { }, -2);
                }
            }
            else if (_buffService.HasBuff(BuffIds.CalmingAtHospital))
            {
                _buffService.RemoveBuff(BuffIds.CalmingAtHospital);
            }
        }

        /// <summary>
        /// ⭐ УЛУЧШЕНО: Обрабатывает потребление еды игроком
        /// Вызывается из FoodConsumptionPatch после Farmer.doneEating
        /// </summary>
        public void OnFoodConsumed(StardewValley.Object consumed)
        {
            _monitor.Log($"[FoodConsumption] Игрок съел: {consumed.DisplayName ?? consumed.Name} ({consumed.QualifiedItemId})", LogLevel.Debug);

            // Топик «ел сегодня» — только для счётчика дней без еды, не блокирует квесты
            if (!ConversationHelper.HasTopic(TopicIds.AteToday))
                ConversationHelper.AddTopic(TopicIds.AteToday, 1);

            var activeHunger = GetTreatmentByQuest(QuestIds.Hunger)
                ?? _data.StressState.GetActiveTreatment(BuffIds.Hunger);

            // Hunger — квест активен (лечение начато)
            if (_stateService.HasActiveQuestState(QuestIds.Hunger))
            {
                if (activeHunger?.Progress != null)
                    activeHunger.Progress.AteAnyFood = true;

                _treatmentService.UpdateTreatmentObjective(BuffIds.Hunger, LegacyTreatmentObjectives.HungerDone);
                Game1.playSound("questcomplete");
                _treatmentService.MarkTreatmentReadyForReview(BuffIds.Hunger);
            }
            else if (activeHunger != null && !activeHunger.TreatmentStarted)
            {
                // M-02: естественное решение до разговора с Harvey — полная очистка state
                _monitor.Log("[FoodConsumption] Hunger resolved naturally before treatment started", LogLevel.Info);

                _buffService.RemoveBuff(BuffIds.Hunger);
                ConversationHelper.RemoveTopic(TopicIds.StressHunger);
                RemoveTreatmentTopicArtifacts(BuffIds.Hunger);

                activeHunger.IsCured = true;
                activeHunger.CompletedDate = SDate.Now();

                if (_data.StressState.RemoveTreatment(activeHunger.TreatmentKey))
                {
                    _monitor.Log($"[FoodConsumption] ActiveTreatment removed: {activeHunger.TreatmentKey}", LogLevel.Debug);
                }
                else
                {
                    _monitor.Log($"[FoodConsumption] Failed to remove ActiveTreatment: {activeHunger.TreatmentKey}", LogLevel.Warn);
                }

                Game1.addHUDMessage(new HUDMessage("Голод утолён", HUDMessage.newQuest_type));
            }
            else if (_stateService.HasBuffInGame(BuffIds.Hunger))
            {
                // Edge case: game buff без TreatmentState
                _buffService.RemoveBuff(BuffIds.Hunger);
                Game1.addHUDMessage(new HUDMessage("Голод утолён", HUDMessage.newQuest_type));
            }

            // TooCold — только горячий напиток (C-08)
            if (_stateService.HasActiveQuestState(QuestIds.TooCold))
            {
                if (HotDrinkHelper.IsHotDrinkOrWarmingFood(consumed))
                {
                    _treatmentService.UpdateTreatmentObjective(BuffIds.TooCold, LegacyTreatmentObjectives.TooColdHotDrink);
                    Game1.playSound("questcomplete");
                    _treatmentService.MarkTreatmentReadyForReview(BuffIds.TooCold);
                }
                else
                {
                    _monitor.Log(
                        $"[FoodConsumption] TooCold: {consumed.QualifiedItemId} is not a hot drink — quest not completed",
                        LogLevel.Debug);
                    Game1.addHUDMessage(new HUDMessage("Нужно что-то горячее: чай или кофе", HUDMessage.newQuest_type));
                }
            }

            _episodeQuestProgressService?.OnFoodConsumed();

            if (HotDrinkHelper.IsHotDrinkOrWarmingFood(consumed))
                _episodeQuestProgressService?.OnHotDrinkConsumed();
        }

        private void NaturalBuffRemoval(bool harveyNearby)
        {
            // Tired - rest at home late evening
            if (CanNaturallyRemoveDebuff(BuffIds.Tired)
                && Game1.player.currentLocation is StardewValley.Locations.FarmHouse
                && GameStateHelper.IsEveningNight(2200, 2600))
            {
                NaturallyResolveBeforeTreatment(BuffIds.Tired, TopicIds.StressTired);
            }

            // Lonely - removal when talking to Harvey
            if (CanNaturallyRemoveDebuff(BuffIds.Lonely) && harveyNearby)
            {
                NaturallyResolveBeforeTreatment(
                    BuffIds.Lonely,
                    TopicIds.StressLonely,
                    () => Game1.getCharacterFromName("Harvey")?.showTextAboveHead("Я всегда рядом."));
            }

            // Thunder - removal indoors with Harvey
            if (CanNaturallyRemoveDebuff(BuffIds.Thunder)
                && harveyNearby
                && Game1.player.currentLocation?.NameOrUniqueName == "Hospital"
                && (Game1.isLightning || Game1.isRaining))
            {
                NaturallyResolveBeforeTreatment(BuffIds.Thunder, TopicIds.StressThunder);
            }

            // TooCold - removal in warm zones
            if (CanNaturallyRemoveDebuff(BuffIds.TooCold)
                && GameStateHelper.IsInWarmZone())
            {
                NaturallyResolveBeforeTreatment(BuffIds.TooCold, TopicIds.StressTooCold);
            }

            // Hunger — OnFoodConsumed (M-02). Darkness — DarknessService therapy (не менять).
        }

        /// <summary>
        /// Natural removal только до StartTreatment: реальный game buff + лечение не начато.
        /// </summary>
        private bool CanNaturallyRemoveDebuff(string buffId)
        {
            if (!_stateService.HasBuffInGame(buffId))
                return false;

            var activeTreatment = _data.StressState.GetActiveTreatment(buffId);
            return activeTreatment == null || !activeTreatment.TreatmentStarted;
        }

        /// <summary>
        /// Снимает debuff естественно до начала лечения: buff, topics, ActiveTreatment, history.
        /// </summary>
        private void NaturallyResolveBeforeTreatment(
            string buffId,
            string stressTopic,
            Action? afterRemove = null)
        {
            var activeTreatment = _data.StressState.GetActiveTreatment(buffId);

            _monitor.Log($"[NaturalBuffRemoval] {buffId} naturally resolved before treatment started", LogLevel.Info);

            _buffService.RemoveBuff(buffId);
            ConversationHelper.RemoveTopic(stressTopic);
            RemoveTreatmentTopicArtifacts(buffId);

            if (StressCauses.TryGetCauseForBuff(buffId, out var causeId))
                _stressLoadService.RemoveCause(causeId);

            if (activeTreatment != null)
            {
                activeTreatment.IsCured = true;
                activeTreatment.CompletedDate = SDate.Now();

                if (!_data.StressState.RemoveTreatment(activeTreatment.TreatmentKey))
                {
                    _monitor.Log(
                        $"[NaturalBuffRemoval] Failed to remove ActiveTreatment: {activeTreatment.TreatmentKey}",
                        LogLevel.Warn);
                }
            }

            afterRemove?.Invoke();
        }

        private static void RemoveTreatmentTopicArtifacts(string buffId)
        {
            if (TreatmentTopics.LegacyStartByBuff.TryGetValue(buffId, out var legacyTopic))
                ConversationHelper.RemoveTopic(legacyTopic);

            if (TreatmentTopics.FollowupByBuff.TryGetValue(buffId, out var followupTopic))
                ConversationHelper.RemoveTopic(followupTopic);
        }

        // УСТАРЕВШИЙ МЕТОД - Теперь используется DarknessService.CheckAndApplyDarknessBuff
        // Оставлен для обратной совместимости, но больше не вызывается
        [Obsolete("Используйте DarknessService.CheckAndApplyDarknessBuff")]
        private void CheckDarknessDebuff(GameLocation newLocation)
        {
            // Старая логика закомментирована - используйте DarknessService
            /*
            if (Game1.stats.DaysPlayed >= 3
                && Game1.timeOfDay >= 2200 && Game1.timeOfDay <= 2600
                && !_stateService.HasActiveTreatmentState(BuffIds.Darkness)
                && !_stateService.HasImmunity(BuffIds.Darkness))
            {
                var n = newLocation.NameOrUniqueName;
                if (n == "Backwoods" || n == "Forest" || n == "Mountain")
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Darkness, "Темнота");
                }
            }
            */
        }

        private void ApplyQuestLocationBuffs(GameLocation newLocation)
        {
            // Tired quest - resting at home buff
            if (_stateService.HasActiveQuestState(QuestIds.Tired)
                && newLocation is StardewValley.Locations.FarmHouse
                && !GameStateHelper.HasHeavyTools(Game1.player)
                && !_buffService.HasBuff(BuffIds.RestingAtHome))
            {
                _buffService.ApplyBuff(BuffIds.RestingAtHome, "Отдых дома",
                    new StardewValley.Buffs.BuffEffects { }, -2);
            }

            ManageOverworkBreaks(newLocation);

            // Legacy Darkness (HarveyMod_DarknessRecovery) — только без уровневой системы
            if (!DarknessLegacyHelper.UsesLevelSystem(_data, _stateService)
                && _stateService.HasActiveQuestState(QuestIds.Darkness)
                && GameStateHelper.IsEveningNight(2000, 2600)
                && !_buffService.HasBuff(BuffIds.LightAndSafe)
                && newLocation is StardewValley.Locations.FarmHouse)
            {
                _buffService.ApplyBuff(BuffIds.LightAndSafe, "Свет и безопасность",
                    new StardewValley.Buffs.BuffEffects { }, -2);
            }
        }

        private void ManageOverworkBreaks(GameLocation newLocation)
        {
            if (!_stateService.HasActiveQuestState(QuestIds.Overwork)) return;

            bool restZone = GameStateHelper.IsInRestZone();

            if (restZone && _data.OverworkBreaksToday < 3 && !_buffService.HasBuff(BuffIds.OverworkBreak))
            {
                _buffService.ApplyBuff(BuffIds.OverworkBreak, "Перерыв",
                    new StardewValley.Buffs.BuffEffects { }, -2);
                ConversationHelper.AddTopic(TopicIds.OverworkBreakActive, 1);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakInterrupted);
                Game1.playSound("sipTea");
            }
            else if (!restZone && _buffService.HasBuff(BuffIds.OverworkBreak))
            {
                _buffService.RemoveBuff(BuffIds.OverworkBreak);
                _data.OverworkBreakSeconds = 0;
                _data.OverworkBreakActive = false;
                ConversationHelper.AddTopic(TopicIds.OverworkBreakInterrupted, 0);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakActive);
                Game1.playSound("cancel");
            }
        }

        private void CheckLightningStressTrigger()
        {
            if (Game1.stats.DaysPlayed < 2) return;
            if (_stateService.HasActiveTreatmentState(BuffIds.Thunder)) return;
            if (_stateService.HasImmunity(BuffIds.Thunder)) return;

            if (Game1.random.NextDouble() < 0.3)
            {
                _treatmentService.ApplyStressBuff(BuffIds.Thunder, "Страх грозы");
            }
        }

        /// <summary>
        /// Выдача debuff TooCold: вечер/ночь (21:00–2:00), холодная погода, холодные локации.
        /// Вызывается при warp и при переходе времени за 21:00 (не на DayStarted).
        /// </summary>
        private void TryApplyTooColdStressTrigger()
        {
            if (Game1.stats.DaysPlayed < 2) return;
            if (!GameStateHelper.IsEveningNight(2100, 2600)) return;
            if (!GameStateHelper.IsSeasonOneOf("spring", "fall", "winter")) return;
            if (!GameStateHelper.IsWeatherOneOf("Snow", "Rain", "Wind", "Storm")) return;
            if (_stateService.HasActiveTreatmentState(BuffIds.TooCold)) return;
            if (_stateService.HasImmunity(BuffIds.TooCold)) return;

            var loc = Game1.player.currentLocation?.NameOrUniqueName;
            if (loc is "Mountain" or "Forest" or "Railroad" or "Backwoods")
            {
                _treatmentService.ApplyStressBuff(BuffIds.TooCold, "Переохлаждение");
            }
        }

        /// <summary>
        /// ⭐ ИСПРАВЛЕНО: Проверяет усталость в течение дня
        /// Вызывается из ProcessGameTick, когда stamina падает до низкого уровня
        /// </summary>
        private void CheckTiredStressTrigger()
        {
            // Проверяем только раз в минуту (каждые 60 тиков) для оптимизации
            if (Game1.ticks % 60 != 0) return;

            // Базовые проверки
            if (Game1.stats.DaysPlayed < 1) return;
            if (_stateService.HasImmunity(BuffIds.Tired)) return;
            if (_stateService.HasActiveTreatmentState(BuffIds.Tired)) return;

            // Проверяем, что stamina низкая (<= 10) и игрок не в событии
            if (GameStateHelper.IsEventActive()) return;
            
            if (Game1.player.Stamina >= 0 && Game1.player.Stamina <= 10)
            {
                _treatmentService.ApplyStressBuff(BuffIds.Tired, "Усталость");
                _monitor.Log($"[Tired Stress] Триггер активирован: stamina={Game1.player.Stamina}/270", LogLevel.Info);
            }
        }

        /// <summary>
        /// ⭐ НОВОЕ: Обновляет счетчики дней без разговоров/еды в начале нового дня
        /// Вызывается ПЕРЕД очисткой топиков
        /// </summary>
        private void UpdateConsecutiveDaysCounters()
        {
            // Проверяем, разговаривал ли игрок вчера (топик SpokeToday НЕ истекает в конце дня)
            bool spokeYesterday = ConversationHelper.HasTopic(TopicIds.SpokeToday);
            
            if (spokeYesterday)
            {
                // Разговаривал - сбрасываем счетчик
                _data.DaysWithoutTalking = 0;
                _monitor.Log("[DaysCounter] Игрок разговаривал вчера - счетчик Lonely сброшен", LogLevel.Debug);
            }
            else
            {
                // Не разговаривал - увеличиваем счетчик
                _data.DaysWithoutTalking++;
                _monitor.Log($"[DaysCounter] Игрок НЕ разговаривал вчера - счетчик Lonely: {_data.DaysWithoutTalking} дней", LogLevel.Info);
            }

            // Проверяем, ел ли игрок вчера (топик AteToday НЕ истекает в конце дня)
            bool ateYesterday = ConversationHelper.HasTopic(TopicIds.AteToday);
            
            if (ateYesterday)
            {
                // Ел - сбрасываем счетчик
                _data.DaysWithoutEating = 0;
                _monitor.Log("[DaysCounter] Игрок ел вчера - счетчик Hunger сброшен", LogLevel.Debug);
            }
            else
            {
                // Не ел - увеличиваем счетчик
                _data.DaysWithoutEating++;
                _monitor.Log($"[DaysCounter] Игрок НЕ ел вчера - счетчик Hunger: {_data.DaysWithoutEating} дней", LogLevel.Info);
            }
        }

        private void ResetDailyQuestCounters()
        {
            if (_stateService.HasActiveQuestState(QuestIds.Overwork))
            {
                _data.OverworkBreaksToday = 0;
                _data.OverworkBreakSeconds = 0;
                _data.OverworkBreakActive = false;
                _buffService.RemoveBuff(BuffIds.OverworkBreak);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakActive);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakInterrupted);
                _treatmentService.UpdateTreatmentObjective(BuffIds.Overwork, LegacyTreatmentObjectives.OverworkDailyStart);
            }

            if (_stateService.HasActiveQuestState(QuestIds.Thunder))
            {
                var thunderTreatment = GetTreatmentByQuest(QuestIds.Thunder);
                if (thunderTreatment?.Progress != null)
                {
                    thunderTreatment.Progress.SecondsNearHarvey = 0;
                }
            }

            var lonelyTreatment = GetTreatmentByQuest(QuestIds.Lonely);
            if (lonelyTreatment?.Progress != null)
            {
                lonelyTreatment.Progress.TalkedUniqueToday = 0;
            }

            var socialTreatment = GetTreatmentByQuest(QuestIds.Social);
            if (socialTreatment?.Progress != null)
            {
                if (socialTreatment.AwaitingHarveyReview
                    || _data.SocialAnxietyTherapy.Phase >= SocialAnxietyTherapyPhase.ReadyToComplete)
                {
                    _monitor.Log(
                        "[SocialAnxiety] Daily reset skipped — awaiting Harvey follow-up",
                        LogLevel.Debug);
                }
                else
                {
                    socialTreatment.Progress.SocialTalksAfterQuest = 0;
                    socialTreatment.Progress.SecondsNearHarvey = 0;
                }
            }

            var socialShutdownTreatment = GetTreatmentByQuest(QuestIds.SocialShutdown);
            if (socialShutdownTreatment?.Progress != null)
            {
                socialShutdownTreatment.Progress.SecondsNearHarvey = 0;
                socialShutdownTreatment.Progress.SocialShutdownUnfamiliarNpcs.Clear();
            }

            ResetActiveEpisodeDailyCounters();
        }

        private void ResetActiveEpisodeDailyCounters()
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null || !episode.IsActiveEpisode())
                return;

            var treatment = GetTreatmentByQuest(episode.QuestId);
            if (treatment?.Progress == null)
                return;

            switch (episode.EpisodeId)
            {
                case StressEpisodes.Burnout:
                    treatment.Progress.BurnoutAvoidedMinesToday = true;
                    break;
                case StressEpisodes.PhysicalExhaustion:
                    treatment.Progress.WarmSeconds = 0;
                    treatment.Progress.TiredRestSeconds = 0;
                    break;
                case StressEpisodes.AnxietySpike:
                    treatment.Progress.AnxietySafeSeconds = 0;
                    break;
            }

            _episodeQuestProgressService?.UpdateQuestJournal(episode.EpisodeId, treatment.Progress);
        }

        private void ApplyPendingNoSleepLateObjective()
        {
            if (!_data.NoSleepLateObjectivePending)
                return;

            if (!_stateService.HasActiveQuestState(QuestIds.NoSleep))
            {
                _data.NoSleepLateObjectivePending = false;
                return;
            }

            _treatmentService.UpdateTreatmentObjective(BuffIds.NoSleep, LegacyTreatmentObjectives.NoSleepLateFailed);
            _data.NoSleepLateObjectivePending = false;
        }

        private TreatmentState? GetTreatmentByQuest(string questId)
        {
            return _data.StressState.GetActiveTreatmentByQuest(questId);
        }

        // ===== МЕТОДЫ ДЛЯ ТЕРАПИИ СТРАХА ТЕМНОТЫ =====

        /// <summary>
        /// Проверить наличие дебаффов темноты и добавить соответствующие топики
        /// </summary>
        private void CheckDarknessDebuffsAndAddTopics()
        {
            if (!CanRunStressDialoguePipeline())
                return;

            if (_stressDialogueService.IsShowingStressDialogue)
            {
                _monitor.Log("[HandleHarveyDialogue] Darkness fallback topics skipped: programmatic stress dialogue is active", LogLevel.Debug);
                return;
            }

            // Проверяем, не идёт ли уже терапия
            if (_data.Darkness.IsTherapyActive) return;

            // Проверяем уровень 3 (фобия) - приоритет
            if (_stateService.HasBuffInGame(BuffIds.DarknessLevel3))
            {
                if (!ConversationHelper.HasTopic("topicStressDarknessLevel3"))
                {
                    ConversationHelper.AddTopic("topicStressDarknessLevel3", 0); // Не истекает
                    _monitor.Log("[DarknessTherapy] Добавлен топик для Уровня 3 (фобия)", LogLevel.Info);
                }
                return; // Не проверяем дальше
            }

            // Проверяем уровень 2 (сильный страх)
            if (_stateService.HasBuffInGame(BuffIds.DarknessLevel2))
            {
                if (!ConversationHelper.HasTopic("topicStressDarknessLevel2"))
                {
                    ConversationHelper.AddTopic("topicStressDarknessLevel2", 7); // 7 дней
                    _monitor.Log("[DarknessTherapy] Добавлен топик для Уровня 2 (сильный страх)", LogLevel.Info);
                }
                return;
            }

            // Проверяем уровень 1 (легкий страх) - старый топик уже должен быть
            if (_stateService.HasBuffInGame(BuffIds.DarknessLevel1) || _stateService.HasBuffInGame(BuffIds.Darkness))
            {
                if (!ConversationHelper.HasTopic(TopicIds.StressDarkness))
                {
                    ConversationHelper.AddTopic(TopicIds.StressDarkness, 7); // 7 дней
                    _monitor.Log("[DarknessTherapy] Добавлен топик для Уровня 1 (легкий страх)", LogLevel.Info);
                }
            }
        }

        /// <summary>
        /// CP: терапия стартует только если topicDarknessTherapyStart добавлен #$t в этом разговоре.
        /// Programmatic level 1 идёт через CheckAndStartTreatmentAfterDialogue.
        /// </summary>
        private void TryStartDarknessTherapyFromCpDialogue()
        {
            if (GameStateHelper.IsEventActive())
                return;

            if (_data.Darkness.IsTherapyActive)
                return;

            if (_hadDarknessTherapyTopicAtTalkStart)
                return;

            if (!ConversationHelper.HasTopic("topicDarknessTherapyStart"))
                return;

            _darknessService.StartTherapy();
            _monitor.Log("[DarknessTherapy] ✅ Терапия начата после CP-диалога (#$t topicDarknessTherapyStart)", LogLevel.Info);
        }

        private bool CanRunStressDialoguePipeline(
            NPC? harveyNpc = null,
            bool requireDialogueBox = true,
            [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            if (StressDialoguePipelineGuard.CanRun(
                    out var reason,
                    requireDialogueBox,
                    requireHarveySpeaker: true,
                    knownHarveyNpc: harveyNpc))
            {
                return true;
            }

            StressDialoguePipelineGuard.LogBlocked(_monitor, caller, reason);
            return false;
        }

    }
}

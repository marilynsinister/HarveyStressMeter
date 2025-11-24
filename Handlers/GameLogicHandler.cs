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

        private string? _lastDialogueNpc;

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

            [TopicIds.TreatmentStartTired] = (BuffIds.Tired, QuestIds.Tired, "Усталость", true),
            [TopicIds.TreatmentStartLonely] = (BuffIds.Lonely, QuestIds.Lonely, "Одиночество", true),
            [TopicIds.TreatmentStartThunder] = (BuffIds.Thunder, QuestIds.Thunder, "Страх грозы", true),
            [TopicIds.TreatmentStartHunger] = (BuffIds.Hunger, QuestIds.Hunger, "Голод", true),
            [TopicIds.TreatmentStartOverwork] = (BuffIds.Overwork, QuestIds.Overwork, "Переработка", true),
            [TopicIds.TreatmentStartNoSleep] = (BuffIds.NoSleep, QuestIds.NoSleep, "Недосып", true),
            [TopicIds.TreatmentStartTooCold] = (BuffIds.TooCold, QuestIds.TooCold, "Переохлаждение", true),
            [TopicIds.TreatmentStartSocial] = (BuffIds.Social, QuestIds.Social, "Социальный дискомфорт", true),
            [TopicIds.TreatmentStartDarkness] = (BuffIds.Darkness, QuestIds.Darkness, "Темнота", true),
        };

        public GameLogicHandler(
            SaveData data,
            IMonitor monitor,
            TreatmentService treatmentService,
            TriggerService triggerService,
            BuffService buffService,
            StateService stateService)
        {
            _data = data;
            _monitor = monitor;
            _treatmentService = treatmentService;
            _triggerService = triggerService;
            _buffService = buffService;
            _stateService = stateService;
        }

        public void ResetDailyData()
        {
            _data.TalkedNpcsToday.Clear();
            _data.OverworkBreaksToday = 0;
            _data.OverworkBreakSeconds = 0;
            _data.OverworkBreakActive = false;
            _data.TalkedToHarveyToday = false;

            ResetDailyQuestCounters();
        }

        public void CheckDayStartedStressTriggers()
        {
            // Tired - low stamina at day start
            if (Game1.stats.DaysPlayed >= 1
                && Game1.player.Stamina >= 0 && Game1.player.Stamina <= 10
                && !_stateService.HasActiveBuffInGame(BuffIds.Immunity)
                && !_stateService.HasActiveBuffInGame(BuffIds.Tired))
            {
                _treatmentService.ApplyStressBuff(BuffIds.Tired, "Усталость");
            }

            // Thunder - lightning
            if (Game1.stats.DaysPlayed >= 2
                && Game1.isLightning
                && !_stateService.HasActiveBuffInGame(BuffIds.Thunder)
                && !_stateService.HasActiveBuffInGame(BuffIds.Immunity))
            {
                _treatmentService.ApplyStressBuff(BuffIds.Thunder, "Страх грозы");
            }

            // TooCold - cold weather in cold locations
            if (Game1.stats.DaysPlayed >= 2
                && Game1.timeOfDay >= 2100 && Game1.timeOfDay <= 2600
                && GameStateHelper.IsSeasonOneOf("spring", "fall", "winter")
                && GameStateHelper.IsWeatherOneOf("Snow", "Rain", "Wind", "Storm")
                && !_stateService.HasActiveBuffInGame(BuffIds.TooCold)
                && !_stateService.HasActiveBuffInGame(BuffIds.Immunity))
            {
                var loc = Game1.player.currentLocation?.NameOrUniqueName;
                if (loc == "Mountain" || loc == "Forest" || loc == "Railroad" || loc == "Backwoods")
                {
                    _treatmentService.ApplyStressBuff(BuffIds.TooCold, "Переохлаждение");
                }
            }
        }

        public void ProcessGameTick(bool harveyNearby)
        {
            // Harvey's care aura
            if (harveyNearby)
            {
                _buffService.ApplyBuff(BuffIds.CareAura, "Рядом с Харви",
                    new StardewValley.Buffs.BuffEffects { Defense = { +1 }, MaxStamina = { +10 } }, 2000);
            }
            else
            {
                _buffService.RemoveBuff(BuffIds.CareAura);
            }

            // Thunder quest calming buff
            ApplyThunderCalmingBuff(harveyNearby);

            // Food consumption detection
            DetectFoodConsumption();

            // Natural buff removal
            NaturalBuffRemoval(harveyNearby);

            // Keep locked buffs persistent
            _treatmentService.EnsureLockedBuffsPersist();

            // ⭐ НОВОЕ: Обновление прогресса всех активных лечений (включая Social)
            _triggerService.UpdateTreatmentProgress(harveyNearby);
        }

        public void HandleMenuChanged(MenuChangedEventArgs e)
        {
            HandleDialogueEvents(e);
        }

        public void HandleWarped(WarpedEventArgs e)
        {
            if (e.NewLocation == null) return;

            CheckDarknessDebuff(e.NewLocation);
            ApplyQuestLocationBuffs(e.NewLocation);
        }

        public void HandleTimeChanged(TimeChangedEventArgs e)
        {
            // Obmorok from tiredness (at 2:00)
            if (e.NewTime == 200 && Game1.player.Stamina <= 0)
            {
                if (Game1.stats.DaysPlayed >= 1
                    && !_stateService.HasActiveBuffInGame(BuffIds.Overwork)
                    && !_stateService.HasActiveBuffInGame(BuffIds.Immunity))
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Overwork, "Переработка");
                }
            }

            // Lightning check every 10 minutes during storm
            if (Game1.isLightning && e.NewTime % 100 == 0)
            {
                CheckLightningStressTrigger();
            }
        }

        public void CheckDayEndingQuestCompletion()
        {
            // NoSleep - completion at early bedtime
            if (_stateService.HasQuestInJournal(QuestIds.NoSleep)
                && Game1.timeOfDay >= 600 && Game1.timeOfDay <= 2200)
            {
                _stateService.CompleteTreatment(QuestIds.NoSleep);
                ConversationHelper.AddTopic("topicStressTreatmentNoSleepCured", 2);
                Game1.playSound("questcomplete");
            }

            // Darkness - completion when spending evening in light
            if (_stateService.HasQuestInJournal(QuestIds.Darkness)
                && Game1.timeOfDay >= 2000 && Game1.timeOfDay <= 200
                && _stateService.HasActiveBuffInGame(BuffIds.LightAndSafe)
                && Game1.player.currentLocation is StardewValley.Locations.FarmHouse)
            {
                _buffService.RemoveBuff(BuffIds.LightAndSafe);
                ConversationHelper.AddTopic("topicStressTreatmentDarknessCured", 2);
                Game1.playSound("questcomplete");
                _stateService.CompleteTreatment(QuestIds.Darkness);
            }
        }

        private void HandleDialogueEvents(MenuChangedEventArgs e)
        {
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
                HandleHarveyDialogueEnd();
                _lastDialogueNpc = null;
            }
        }

        private void HandleHarveyDialogue(NPC harveyNpc, MenuChangedEventArgs e)
        {
            _monitor.Log($"[Диалог] Начался разговор с Харви. Текущие топики: {string.Join(", ", Game1.player.activeDialogueEvents.Keys.Where(k => k.Contains("Stress")))}", LogLevel.Info);
            _monitor.Log($"[Диалог] Дебафф Social активен: {_stateService.HasActiveBuffInGame(BuffIds.Social)}", LogLevel.Info);

            if (e.NewMenu is DialogueBox dialogueBox)
            {
                _monitor.Log($"[Диалог] DialogueBox найдена. isOnFinalDialogue: {dialogueBox.characterDialogue?.isOnFinalDialogue()}", LogLevel.Info);

                if (dialogueBox.characterDialogue?.isOnFinalDialogue() == true)
                {
                    _monitor.Log($"[Диалог] Достигнут финальный диалог с Харви!", LogLevel.Info);

                    // Проверяем, был ли активен топик, соответствующий дебаффу Social
                    bool hasSocialStressTopic = ConversationHelper.HasTopic(TopicIds.StressSocial);
                    _monitor.Log($"[Диалог] Топик topicStressSocial активен: {hasSocialStressTopic}", LogLevel.Info);

                    CheckStressTopicsAndStartTreatment(harveyNpc, dialogueBox.characterDialogue.TranslationKey, hasSocialStressTopic);
                }
                else
                {
                    _monitor.Log($"[Диалог] Диалог с Харви продолжается (не финальный)", LogLevel.Debug);
                }
            }
            else
            {
                _monitor.Log($"[Диалог] Это не DialogueBox, а {e.NewMenu?.GetType().Name}", LogLevel.Warn);
            }
        }

        private void HandleHarveyDialogueEnd()
        {
            _monitor.Log($"[Диалог] Завершен разговор с Харви", LogLevel.Info);

            // Проверяем, был ли активен топик, соответствующий дебаффу Social
            bool hasSocialStressTopic = ConversationHelper.HasTopic(TopicIds.StressSocial);
            _monitor.Log($"[Диалог] При завершении диалога топик topicStressSocial активен: {hasSocialStressTopic}", LogLevel.Info);

            CheckStressTopicsAndStartTreatment(Game1.getCharacterFromName("Harvey"), null, hasSocialStressTopic);

            UpdateSocialQuestProgress(showUiMessage: false);

            if (!ConversationHelper.HasTopic(TopicIds.SpokeToday))
            {
                ConversationHelper.AddTopic(TopicIds.SpokeToday, 1);
            }
        }

        private void HandleDialogueEnd()
        {
            if (_lastDialogueNpc != "Harvey")
            {
                if (_lastDialogueNpc != null)
                {
                    _data.TalkedNpcsToday.Add(_lastDialogueNpc);
                    _monitor.Log($"[Диалог] Завершен разговор с {_lastDialogueNpc}. Всего разговоров сегодня: {_data.TalkedNpcsToday.Count}", LogLevel.Info);
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
        {
            if (Game1.stats.DaysPlayed < 5) return;
            if (_stateService.HasActiveBuffInGame(BuffIds.Social)) return;
            if (_stateService.HasActiveBuffInGame(BuffIds.Immunity)) return;

            if (Game1.player.friendshipData.TryGetValue(npc.Name, out var friendship))
            {
                if (friendship.Points < 750 && Game1.random.NextDouble() < 0.3)
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Social, "Социальный дискомфорт");
                    _monitor.Log($"[Social Stress] Триггер активирован при разговоре с {npc.Name} (дружба: {friendship.Points}/750)", LogLevel.Info);
                }
            }
        }

        private void CheckStressTopicsAndStartTreatment(NPC harvey, string? dialogueKey = null, bool hasSocialStressTopic = false)
        {
            _monitor.Log($"[CheckStressTopicsAndStartTreatment] Начат анализ диалога с Харви. DialogueKey: '{dialogueKey}', hasSocialStressTopic: {hasSocialStressTopic}", LogLevel.Info);
            _monitor.Log($"[CheckStressTopicsAndStartTreatment] Активные дебаффы: Tired={_stateService.HasActiveBuffInGame(BuffIds.Tired)}, Lonely={_stateService.HasActiveBuffInGame(BuffIds.Lonely)}, Thunder={_stateService.HasActiveBuffInGame(BuffIds.Thunder)}, Social={_stateService.HasActiveBuffInGame(BuffIds.Social)}", LogLevel.Info);

            // First, try the original topic-based approach (for Content Patcher compatibility)
            if (!string.IsNullOrEmpty(dialogueKey))
            {
                string normalizedKey = dialogueKey;
                int colonIndex = dialogueKey.IndexOf(':');
                if (colonIndex >= 0 && colonIndex < dialogueKey.Length - 1)
                {
                    normalizedKey = dialogueKey.Substring(colonIndex + 1);
                }

                _monitor.Log($"[CheckStressTopicsAndStartTreatment] Исходный ключ: '{dialogueKey}', нормализованный: '{normalizedKey}'", LogLevel.Info);

                if (TopicMapping.TryGetValue(normalizedKey, out var mapping) && mapping.isTreatmentTopic)
                {
                    _monitor.Log($"[CheckStressTopicsAndStartTreatment] ✅ Найден маппинг для ключа '{normalizedKey}': {mapping.buffId}, isTreatmentTopic={mapping.isTreatmentTopic}", LogLevel.Info);

                    if (_stateService.HasActiveBuffInGame(mapping.buffId))
                    {
                        _monitor.Log($"[CheckStressTopicsAndStartTreatment] Дебафф {mapping.buffId} активен. Начинаем лечение через topic mapping...", LogLevel.Info);
                        _treatmentService.StartTreatment(mapping.buffId, mapping.displayName);
                    }
                    else
                    {
                        _monitor.Log($"[CheckStressTopicsAndStartTreatment] ❌ Дебафф {mapping.buffId} НЕ активен!", LogLevel.Error);
                    }
                    return;
                }
            }

            // Special handling for Social stress: only start treatment if the social stress topic was active during this dialogue
            if (hasSocialStressTopic && _stateService.HasActiveBuffInGame(BuffIds.Social))
            {
                _monitor.Log($"[CheckStressTopicsAndStartTreatment] ✅ Диалог был по топику topicStressSocial и дебафф Social активен. Начинаем лечение Social!", LogLevel.Info);
                _treatmentService.StartTreatment(BuffIds.Social, "Социальный дискомфорт");
                return;
            }

            // Fallback: Check for other active stress topics and start treatment
            // This handles the case where Content Patcher isn't used but other topics are set
            _monitor.Log($"[CheckStressTopicsAndStartTreatment] Topic mapping и Social stress check не сработали, проверяем другие активные топики стресса...", LogLevel.Info);

            foreach (var kvp in TopicMapping.Where(x => !x.Value.isTreatmentTopic && x.Key != TopicIds.StressSocial))
            {
                var topicId = kvp.Key;
                var mapping = kvp.Value;

                if (ConversationHelper.HasTopic(topicId))
                {
                    _monitor.Log($"[CheckStressTopicsAndStartTreatment] ✅ Найден активный топик стресса: {topicId} -> {mapping.buffId}", LogLevel.Info);

                    if (_stateService.HasActiveBuffInGame(mapping.buffId))
                    {
                        _monitor.Log($"[CheckStressTopicsAndStartTreatment] Дебафф {mapping.buffId} активен. Начинаем лечение через fallback логику...", LogLevel.Info);
                        _treatmentService.StartTreatment(mapping.buffId, mapping.displayName);
                        return;
                    }
                    else
                    {
                        _monitor.Log($"[CheckStressTopicsAndStartTreatment] Дебафф {mapping.buffId} не активен для топика {topicId}", LogLevel.Warn);
                    }
                }
            }

            _monitor.Log($"[CheckStressTopicsAndStartTreatment] Не найдено подходящих условий для начала лечения", LogLevel.Info);
        }

        private void UpdateLonelyQuestProgress()
        {
            var lonelyTreatment = GetTreatmentByQuest(QuestIds.Lonely);
            if (_stateService.HasQuestInJournal(QuestIds.Lonely) && lonelyTreatment?.Progress != null)
            {
                lonelyTreatment.Progress.TalkedUniqueToday = _data.TalkedNpcsToday.Count;

                Game1.addHUDMessage(new HUDMessage($"+1 общение ({lonelyTreatment.Progress.TalkedUniqueToday}/3)", 2));

                if (lonelyTreatment.Progress.TalkedUniqueToday >= 3)
                {
                    Game1.playSound("questcomplete");
                    _stateService.CompleteTreatment(QuestIds.Lonely);
                    ConversationHelper.AddTopic("topicStressTreatmentLonelyCured", 2);
                }
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
            if (_stateService.HasQuestInJournal(QuestIds.Thunder)
                && Game1.player.currentLocation?.NameOrUniqueName == "Hospital"
                && (Game1.isLightning || Game1.isRaining)
                && harveyNearby)
            {
                _buffService.ApplyBuff(BuffIds.CalmingAtHospital, "Успокоение с Харви",
                    new StardewValley.Buffs.BuffEffects { }, -2);
            }
        }

        private void DetectFoodConsumption()
        {
            // Note: Simplified food detection - stamina tracking moved to EventHandler
            bool inSpa = Game1.currentLocation is StardewValley.Locations.BathHousePool;

            if (!inSpa && Game1.player.Stamina >= 10f)
            {
                var hungerTreatment = GetTreatmentByQuest(QuestIds.Hunger);
                if (hungerTreatment?.Progress != null)
                {
                    hungerTreatment.Progress.AteAnyFood = true;
                }

                if (!ConversationHelper.HasTopic(TopicIds.AteToday))
                {
                    ConversationHelper.AddTopic(TopicIds.AteToday, 1);
                }

                if (_stateService.HasQuestInJournal(QuestIds.Hunger))
                {
                    Game1.playSound("questcomplete");
                    _stateService.CompleteTreatment(QuestIds.Hunger);
                    ConversationHelper.AddTopic("topicStressTreatmentHungerCured", 2);
                }

                if (_stateService.HasQuestInJournal(QuestIds.TooCold))
                {
                    Game1.playSound("questcomplete");
                    _stateService.CompleteTreatment(QuestIds.TooCold);
                    ConversationHelper.AddTopic("topicStressTreatmentTooColdCured", 2);
                }

                if (_stateService.HasActiveBuffInGame(BuffIds.Hunger) && !_data.StressState.IsTreatmentLocked(BuffIds.Hunger))
                {
                    _buffService.RemoveBuff(BuffIds.Hunger);
                    Game1.addHUDMessage(new HUDMessage("Голод утолён", HUDMessage.newQuest_type));
                }
            }
        }

        private void NaturalBuffRemoval(bool harveyNearby)
        {
            // Tired - rest at home late evening
            if (_stateService.HasActiveBuffInGame(BuffIds.Tired)
                && !_data.StressState.IsTreatmentLocked(BuffIds.Tired)
                && Game1.player.currentLocation is StardewValley.Locations.FarmHouse
                && Game1.timeOfDay >= 2200 && Game1.timeOfDay <= 200)
            {
                _buffService.RemoveBuff(BuffIds.Tired);
                ConversationHelper.RemoveTopic(TopicIds.StressTired);
            }

            // Lonely - removal when talking to Harvey
            if (_stateService.HasActiveBuffInGame(BuffIds.Lonely)
                && !_data.StressState.IsTreatmentLocked(BuffIds.Lonely)
                && harveyNearby)
            {
                _buffService.RemoveBuff(BuffIds.Lonely);
                ConversationHelper.RemoveTopic(TopicIds.StressLonely);
                Game1.getCharacterFromName("Harvey")?.showTextAboveHead("Я всегда рядом.");
            }

            // Thunder - removal indoors with Harvey
            if (_stateService.HasActiveBuffInGame(BuffIds.Thunder)
                && !_data.StressState.IsTreatmentLocked(BuffIds.Thunder)
                && harveyNearby
                && Game1.player.currentLocation?.NameOrUniqueName == "Hospital"
                && (Game1.isLightning || Game1.isRaining))
            {
                _buffService.RemoveBuff(BuffIds.Thunder);
                ConversationHelper.RemoveTopic(TopicIds.StressThunder);
            }

            // TooCold - removal in warm zones
            if (_stateService.HasActiveBuffInGame(BuffIds.TooCold)
                && !_data.StressState.IsTreatmentLocked(BuffIds.TooCold)
                && GameStateHelper.IsInWarmZone())
            {
                _buffService.RemoveBuff(BuffIds.TooCold);
                ConversationHelper.RemoveTopic(TopicIds.StressTooCold);
            }

            // Darkness - removal in light
            if (_stateService.HasActiveBuffInGame(BuffIds.Darkness)
                && !_data.StressState.IsTreatmentLocked(BuffIds.Darkness)
                && GameStateHelper.IsInWarmZone()
                && Game1.timeOfDay >= 2000 && Game1.timeOfDay <= 200)
            {
                _buffService.RemoveBuff(BuffIds.Darkness);
                ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
            }
        }

        private void CheckDarknessDebuff(GameLocation newLocation)
        {
            if (Game1.stats.DaysPlayed >= 3
                && Game1.timeOfDay >= 2200 && Game1.timeOfDay <= 2600
                && !_stateService.HasActiveBuffInGame(BuffIds.Darkness)
                && !_stateService.HasActiveBuffInGame(BuffIds.Immunity))
            {
                var n = newLocation.NameOrUniqueName;
                if (n == "Backwoods" || n == "Forest" || n == "Mountain")
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Darkness, "Темнота");
                }
            }
        }

        private void ApplyQuestLocationBuffs(GameLocation newLocation)
        {
            // Tired quest - resting at home buff
            if (_stateService.HasQuestInJournal(QuestIds.Tired)
                && newLocation is StardewValley.Locations.FarmHouse
                && !GameStateHelper.HasHeavyTools(Game1.player))
            {
                _buffService.ApplyBuff(BuffIds.RestingAtHome, "Отдых дома",
                    new StardewValley.Buffs.BuffEffects { }, -2);
            }

            ManageOverworkBreaks(newLocation);

            // Darkness quest - light and safety buff
            if (_stateService.HasQuestInJournal(QuestIds.Darkness)
                && Game1.timeOfDay >= 2000 && Game1.timeOfDay <= 200
                && !_stateService.HasActiveBuffInGame(BuffIds.LightAndSafe)
                && newLocation is StardewValley.Locations.FarmHouse)
            {
                _buffService.ApplyBuff(BuffIds.LightAndSafe, "Свет и безопасность",
                    new StardewValley.Buffs.BuffEffects { }, -2);
            }
        }

        private void ManageOverworkBreaks(GameLocation newLocation)
        {
            if (!_stateService.HasQuestInJournal(QuestIds.Overwork)) return;

            bool restZone = GameStateHelper.IsInRestZone();

            if (restZone && _data.OverworkBreaksToday < 3 && !_stateService.HasActiveBuffInGame(BuffIds.OverworkBreak))
            {
                _buffService.ApplyBuff(BuffIds.OverworkBreak, "Перерыв",
                    new StardewValley.Buffs.BuffEffects { }, -2);
                ConversationHelper.AddTopic(TopicIds.OverworkBreakActive, 1);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakInterrupted);
                Game1.playSound("sipTea");
            }
            else if (!restZone && _stateService.HasActiveBuffInGame(BuffIds.OverworkBreak))
            {
                _buffService.RemoveBuff(BuffIds.OverworkBreak);
                ConversationHelper.AddTopic(TopicIds.OverworkBreakInterrupted, 0);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakActive);
                Game1.playSound("cancel");
            }

            if (_data.OverworkBreaksToday >= 3)
            {
                _stateService.CompleteTreatment(QuestIds.Overwork);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakActive);
                ConversationHelper.AddTopic("topicStressTreatmentOverworkCured", 2);
                Game1.playSound("questcomplete");
            }
        }

        private void CheckLightningStressTrigger()
        {
            if (Game1.stats.DaysPlayed < 2) return;
            if (_stateService.HasActiveBuffInGame(BuffIds.Thunder)) return;
            if (_stateService.HasActiveBuffInGame(BuffIds.Immunity)) return;

            if (Game1.random.NextDouble() < 0.3)
            {
                _treatmentService.ApplyStressBuff(BuffIds.Thunder, "Страх грозы");
            }
        }

        private void ResetDailyQuestCounters()
        {
            if (_stateService.HasQuestInJournal(QuestIds.Overwork))
            {
                _data.OverworkBreaksToday = 0;
                _buffService.RemoveBuff(BuffIds.OverworkBreak);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakActive);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakInterrupted);
            }

            if (_stateService.HasQuestInJournal(QuestIds.Thunder))
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

            // ⭐ НОВОЕ: Для Social квеста НЕ сбрасываем TalkedUniqueToday!
            // Это базовое значение разговоров на момент получения квеста
            // Сбрасываем только счетчик разговоров ПОСЛЕ квеста и время с Харви
            var socialTreatment = GetTreatmentByQuest(QuestIds.Social);
            if (socialTreatment?.Progress != null)
            {
                socialTreatment.Progress.SocialTalksAfterQuest = 0;  // Сбрасываем счетчик после квеста
                socialTreatment.Progress.SecondsNearHarvey = 0;       // Сбрасываем время с Харви
                // TalkedUniqueToday НЕ трогаем - это база!
            }
        }

        private TreatmentState? GetTreatmentByQuest(string questId)
        {
            return _data.StressState.GetActiveTreatmentByQuest(questId);
        }

    }
}

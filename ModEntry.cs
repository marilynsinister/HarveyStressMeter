using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.UI;
using StardewUI.Framework;

namespace HarveyStressMeter
{
    public sealed class ModEntry : Mod
    {
        // Конфигурация и данные
        private ModConfig _config = null!;
        private SaveData _data = new();
        private readonly string SaveKey = "stress-data-v1";

        // Сервисы
        private BuffService _buffService = null!;
        private QuestService _questService = null!;
        private StateService _stateService = null!;
        private TreatmentService _treatmentService = null!;
        private TriggerService _triggerService = null!;
        private HandbookManager _handbookManager = null!;

        // UI
        private IViewEngine _viewEngine = null!;
        private Texture2D _handbookTex = null!;
        private Texture2D _iconsTex = null!;
        private Rectangle _handbookRect;
        private const int HandbookSize = 44;

        // Состояние
        private float _prevStamina;
        private string? _lastDialogueNpc;

        public override void Entry(IModHelper helper)
        {
            _config = helper.ReadConfig<ModConfig>();

            // Инициализация сервисов
            InitializeServices();

            // Подписка на события
            SubscribeToEvents(helper);

            // Консольные команды
            RegisterCommands();

            // Загрузка текстур
            LoadTextures(helper);
        }

        private void InitializeServices()
        {
            // Инициализация хелперов
            ConversationHelper.Initialize(Monitor);

            _buffService = new BuffService();
            _questService = new QuestService(Monitor);
            _stateService = new StateService(_data, Monitor, _buffService, _questService);
            _treatmentService = new TreatmentService(_data, _buffService, _questService, _stateService, Monitor);
            _triggerService = new TriggerService(_data, _buffService, _questService, _stateService, _treatmentService, Monitor);
        }

        private void SubscribeToEvents(IModHelper helper)
        {
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.ReturnedToTitle += (_, __) => _data = new SaveData();
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.DayEnding += OnDayEnding;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.ButtonsChanged += OnButtonsChanged;
            helper.Events.Player.Warped += OnWarped;

            // События SMAPI для специфичных триггеров
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
        }

        private void RegisterCommands()
        {
            RegisterBasicCommands();
            RegisterDebugCommands();
        }

        private void RegisterBasicCommands()
        {
            Helper.ConsoleCommands.Add("hs.handbook", "Открыть справочник Харви", (_, __) => OpenHandbook());
            Helper.ConsoleCommands.Add("harvey_fix_sync", "Resync quests and stress buffs.", (_, __) =>
            {
                _treatmentService.SyncQuestsAndBuffs();
                Monitor.Log($"Synced. Treatments={_data.StressState.ActiveTreatments.Count}, Buffs={Game1.player.buffs?.AppliedBuffs?.Count ?? 0}", LogLevel.Info);
            });
            Helper.ConsoleCommands.Add("hs.trigger", "Manually run check for quest completion triggers.", (_, __) =>
            {
                _triggerService.CheckManualTriggers();
                Monitor.Log("Manual triggers checked.", LogLevel.Info);
            });
            Helper.ConsoleCommands.Add("hs.restore", "Restore active stress buffs and quests.", (_, __) =>
            {
                _treatmentService.RestoreActiveStressBuffs();
                Monitor.Log("Active stress buffs restoration completed.", LogLevel.Info);
            });
            Helper.ConsoleCommands.Add("hs.cleanup-topics", "Clean up orphaned treatment topics.", (_, __) =>
            {
                _treatmentService.CleanupOrphanedTreatmentTopics();
                Monitor.Log("Orphaned treatment topics cleanup completed.", LogLevel.Info);
            });
        }

        private void RegisterDebugCommands()
        {
            Helper.ConsoleCommands.Add("hs.debug-quests", "Debug quest system - check quest data and availability.", (_, __) => DebugQuestSystem());
            Helper.ConsoleCommands.Add("hs.states", "Show all stress buff states.", (_, __) => ShowTreatmentStates());
            Helper.ConsoleCommands.Add("hs.debug", "Show full diagnostic info (topics, buffs, states).", (_, __) => ShowFullDiagnostic());
            Helper.ConsoleCommands.Add("hs.clear", "Clear all stress buffs, quests and topics (emergency reset).", (_, __) => ClearAllStressStates());
        }

        private void DebugQuestSystem()
        {
            Monitor.Log("=== QUEST DEBUG ===", LogLevel.Info);
            var questData = Game1.content.Load<Dictionary<string, string>>("Data/Quests");
            Monitor.Log($"Data/Quests.Count: {questData.Count}", LogLevel.Info);

            var harveyQuests = questData.Keys.Where(k => k.Contains("Harvey")).ToList();
            Monitor.Log($"Harvey quests in Data/Quests: {harveyQuests.Count}", LogLevel.Info);
            foreach (var quest in harveyQuests.Take(10))
            {
                Monitor.Log($"  • {quest}", LogLevel.Info);
            }

            Monitor.Log($"Player quests in journal: {Game1.player.questLog.Count}", LogLevel.Info);
            for (int i = 0; i < Math.Min(Game1.player.questLog.Count, 5); i++)
            {
                var quest = Game1.player.questLog[i];
                var id = ReflectionHelper.GetQuestStringId(quest, Monitor);
                var questType = quest.GetType().Name;
                
                // Пытаемся получить дополнительные поля для диагностики
                string additionalInfo = "";
                try
                {
                    if (ReflectionHelper.TryGetMember<int>(quest, "id", out var intId))
                        additionalInfo += $", int_id={intId}";
                    if (ReflectionHelper.TryGetMember<string>(quest, "questTitle", out var title))
                        additionalInfo += $", title={title}";
                }
                catch { }
                
                if (string.IsNullOrWhiteSpace(id))
                    Monitor.Log($"  • [{i}] Тип: {questType}{additionalInfo}", LogLevel.Info);
                else
                    Monitor.Log($"  • [{i}] ID: {id}, Тип: {questType}{additionalInfo}", LogLevel.Info);
            }

            Monitor.Log($"Social quest HarveyMod_SocialRecovery in Data/Quests: {questData.ContainsKey("HarveyMod_SocialRecovery")}", LogLevel.Info);
            Monitor.Log($"Social quest in journal: {_stateService.HasQuestInJournal("HarveyMod_SocialRecovery")}", LogLevel.Info);
        }

        private void ShowTreatmentStates()
        {
            Monitor.Log($"=== TreatmentHistory (всего: {_data.StressState.TreatmentHistory.Count}) ===", LogLevel.Info);
            foreach (var (buffId, historyList) in _data.StressState.TreatmentHistory)
            {
                if (historyList.Count > 0)
                {
                    var treatment = historyList.Last();
                    var displayName = treatment.TreatmentStarted ? "ЛЕЧЕНИЕ НАЧАТО" : "НЕ НАЧАТО";
                    var curedStatus = treatment.IsCured ? "ВЫЛЕЧЕН" : "НЕ ВЫЛЕЧЕН";
                    var daysSince = SDate.Now().DaysSinceStart - treatment.IssuedDate.DaysSinceStart;
                    Monitor.Log($"  {buffId}: выдан={treatment.IssuedDate} ({daysSince}д назад), лечение={displayName}, статус={curedStatus}", LogLevel.Info);
                }
            }
        }

        private void ShowFullDiagnostic()
        {
            Monitor.Log("=== ДИАГНОСТИКА СИСТЕМЫ СТРЕССА ===", LogLevel.Info);

            ShowActiveTopics();
            ShowActiveStressBuffs();
            ShowActiveTreatments();
            ShowTreatmentHistory();
            ShowConversationState();
            ShowTreatmentProgress();
            ShowDetailedAnalysis();

            Monitor.Log("\n=== КОНЕЦ ДИАГНОСТИКИ ===", LogLevel.Info);
        }

        private void ShowActiveTopics()
        {
            var topics = Game1.player.activeDialogueEvents;
            Monitor.Log($"\n📋 Активные топики:", LogLevel.Info);
            if (topics != null && topics.Count() > 0)
            {
                int foundCount = 0;
                foreach (var key in topics.Keys)
                {
                    if (key.Contains("Stress") || key.Contains("Treatment") || key.Contains("Harvey"))
                    {
                        topics.TryGetValue(key, out int days);
                        Monitor.Log($"  • {key} = {days} дней", LogLevel.Info);
                        foundCount++;
                    }
                }
                if (foundCount == 0)
                {
                    Monitor.Log("  Нет топиков, связанных со стрессом", LogLevel.Info);
                }
            }
            else
            {
                Monitor.Log("  Нет активных топиков", LogLevel.Info);
            }
        }

        private void ShowActiveStressBuffs()
        {
            Monitor.Log($"\n🔴 Активные баффы стресса:", LogLevel.Info);
            var stressBuffIds = new[] { BuffIds.Tired, BuffIds.Lonely, BuffIds.Thunder, BuffIds.Hunger,
                BuffIds.Overwork, BuffIds.NoSleep, BuffIds.TooCold, BuffIds.Social, BuffIds.Darkness };
            bool hasAnyBuff = false;
            foreach (var buffId in stressBuffIds)
            {
                if (_stateService.HasActiveBuffInGame(buffId))
                {
                    var isLocked = _data.StressState.IsTreatmentLocked(buffId) ? "🔒 ЗАЛОЧЕН" : "⚪ Свободный";
                    Monitor.Log($"  • {buffId} - {isLocked}", LogLevel.Info);
                    hasAnyBuff = true;
                }
            }
            if (!hasAnyBuff) Monitor.Log("  Нет активных баффов стресса", LogLevel.Info);
        }

        private void ShowActiveTreatments()
        {
            Monitor.Log($"\n🔒 Активные лечения (всего: {_data.StressState.GetActiveTreatmentsCount()}):", LogLevel.Info);
            if (_data.StressState.ActiveTreatments.Count > 0)
            {
                foreach (var kvp in _data.StressState.ActiveTreatments)
                {
                    var questId = kvp.Value.QuestId ?? "нет квеста";
                    Monitor.Log($"  • {kvp.Key} → квест: {questId}", LogLevel.Info);
                }
            }
            else
            {
                Monitor.Log("  Нет залоченных дебаффов", LogLevel.Info);
            }
        }

        private void ShowTreatmentHistory()
        {
            Monitor.Log($"\n📊 История лечений (всего: {_data.StressState.TreatmentHistory.Count}):", LogLevel.Info);
            if (_data.StressState.TreatmentHistory.Count > 0)
            {
                foreach (var (buffId, historyList) in _data.StressState.TreatmentHistory)
                {
                    if (historyList.Count > 0)
                    {
                        var treatment = historyList.Last();
                        var daysSince = SDate.Now().DaysSinceStart - treatment.IssuedDate.DaysSinceStart;
                        var status = treatment.IsCured ? "✅ Вылечен" :
                                    (treatment.TreatmentStarted ? "🔄 Лечение начато" : "⏳ Ожидание");
                        Monitor.Log($"  • {buffId}: {status}, выдан {treatment.IssuedDate} ({daysSince}д назад)", LogLevel.Info);
                    }
                }
            }
            else
            {
                Monitor.Log("  Нет сохраненных состояний", LogLevel.Info);
            }
        }

        private void ShowConversationState()
        {
            Monitor.Log($"\n💬 Состояние разговоров:", LogLevel.Info);
            Monitor.Log($"  • Разговоров сегодня: {_data.TalkedNpcsToday.Count}", LogLevel.Info);
            if (_data.TalkedNpcsToday.Count > 0)
            {
                foreach (var npc in _data.TalkedNpcsToday)
                {
                    Monitor.Log($"    - {npc}", LogLevel.Info);
                }
            }
        }

        private void ShowTreatmentProgress()
        {
            Monitor.Log($"\n🏥 Прогресс лечения:", LogLevel.Info);
            bool hasActiveTreatment = false;
            foreach (var (buffId, treatment) in _data.StressState.ActiveTreatments)
            {
                if (treatment.TreatmentStarted && !string.IsNullOrEmpty(treatment.QuestId))
                {
                    hasActiveTreatment = true;
                    var progress = treatment.Progress;
                    if (progress != null)
                    {
                        Monitor.Log($"  • {buffId}: разговоры={progress.TalkedUniqueToday}, время с Харви={progress.SecondsNearHarvey}с", LogLevel.Info);
                    }
                }
            }
            if (!hasActiveTreatment)
            {
                Monitor.Log("  Нет активного лечения", LogLevel.Info);
            }
        }

        private void ShowDetailedAnalysis()
        {
            Monitor.Log("\n🔍 ДЕТАЛЬНЫЙ АНАЛИЗ СОСТОЯНИЯ:", LogLevel.Info);
            ReflectionHelper.LogObjectFields(_data, Monitor, "SaveData_Debug");
            ReflectionHelper.LogObjectFields(_data.StressState, Monitor, "StressState_Debug");
        }

        private void ClearAllStressStates()
        {
            int removedBuffs = 0;
            int removedQuests = 0;
            int removedTopics = 0;

            // Удалить все баффы стресса
            var allBuffIds = new[]
            {
                BuffIds.Tired, BuffIds.Lonely, BuffIds.Thunder, BuffIds.Hunger,
                BuffIds.Overwork, BuffIds.NoSleep, BuffIds.TooCold, BuffIds.Social, BuffIds.Darkness
            };

            foreach (var buffId in allBuffIds)
            {
                if (_stateService.HasActiveBuffInGame(buffId))
                {
                    _buffService.RemoveBuff(buffId);
                    removedBuffs++;
                }
            }

            // Удалить все квесты
            var allQuestIds = new[]
            {
                QuestIds.Tired, QuestIds.Lonely, QuestIds.Thunder, QuestIds.Hunger,
                QuestIds.Overwork, QuestIds.NoSleep, QuestIds.TooCold, QuestIds.Social, QuestIds.Darkness
            };

            foreach (var questId in allQuestIds)
            {
                if (_stateService.HasQuestInJournal(questId))
                {
                    _questService.CompleteQuest(questId);
                    removedQuests++;
                }
            }

            // Удалить все топики
            var allTopics = new[]
            {
                TopicIds.StressTired, TopicIds.StressLonely, TopicIds.StressThunder, TopicIds.StressHunger,
                TopicIds.StressOverwork, TopicIds.StressNoSleep, TopicIds.StressTooCold, TopicIds.StressSocial,
                TopicIds.StressDarkness, TopicIds.LonelyPending, TopicIds.OverworkBreakActive,
                TopicIds.OverworkBreakInterrupted, TopicIds.AteToday, TopicIds.SpokeToday,
                TopicIds.TreatmentStartTired, TopicIds.TreatmentStartLonely, TopicIds.TreatmentStartThunder,
                TopicIds.TreatmentStartHunger, TopicIds.TreatmentStartOverwork, TopicIds.TreatmentStartNoSleep,
                TopicIds.TreatmentStartTooCold, TopicIds.TreatmentStartSocial, TopicIds.TreatmentStartDarkness,
                TopicIds.TreatmentStarted
            };

            foreach (var topic in allTopics)
            {
                if (ConversationHelper.HasTopic(topic))
                {
                    ConversationHelper.RemoveTopic(topic);
                    removedTopics++;
                }
            }

            // Очистить данные сохранения
            _data.StressState.ActiveTreatments.Clear();
            _data.StressState.LastIssuedDay.Clear();
            _data.StressState.TreatmentHistory.Clear();
            _data.TalkedNpcsToday.Clear();
            _data.OverworkBreaksToday = 0;
            _data.OverworkBreakSeconds = 0;
            _data.OverworkBreakActive = false;
            _data.TalkedToHarveyToday = false;

            SaveData();

            Monitor.Log($"Все состояния стресса очищены: {removedBuffs} баффов, {removedQuests} квестов, {removedTopics} топиков.", LogLevel.Info);
            Game1.addHUDMessage(new HUDMessage("Все состояния стресса очищены", HUDMessage.achievement_type));
        }

        private void LoadTextures(IModHelper helper)
        {
            try
            {
                _handbookTex = helper.ModContent.Load<Texture2D>("assets/sprites/handbook.png");
                _iconsTex = helper.ModContent.Load<Texture2D>("assets/sprites/stressIcons.png");
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to load textures: {ex.Message}", LogLevel.Warn);
            }
        }

        // ===== События загрузки =====

        private void OnSaveLoaded(object? s, SaveLoadedEventArgs e)
        {
            Monitor.Log("[OnSaveLoaded] Начинаем загрузку сохранения", LogLevel.Debug);

            _data = Helper.Data.ReadSaveData<SaveData>(SaveKey) ?? new SaveData();
            _data.LastDay = SDate.Now();

            // Инициализируем PlayerStressState если null
            if (_data.StressState == null)
            {
                _data.StressState = new PlayerStressState();
                Monitor.Log("[OnSaveLoaded] PlayerStressState инициализирован", LogLevel.Debug);
            }

            Monitor.Log($"[OnSaveLoaded] Загружены данные: активных лечений={_data.StressState.ActiveTreatments.Count}", LogLevel.Debug);

            // Логируем состояние данных сохранения для отладки
            ReflectionHelper.LogObjectFields(_data, Monitor, "SaveData");

            // Мигрируем старые данные в новую структуру
            _stateService.MigrateOldData();

            // Синхронизируем состояние с игрой (восстанавливаем пропавшие баффы/квесты)
            _stateService.SyncWithGame();

            // Очищаем топики лечения без соответствующих дебаффов
            _treatmentService.CleanupOrphanedTreatmentTopics();

            Monitor.Log($"[OnSaveLoaded] ✅ Загрузка завершена: активных лечений={_data.StressState.ActiveTreatments.Count}", LogLevel.Info);
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            _viewEngine = Helper.ModRegistry.GetApi<IViewEngine>("focustense.StardewUI")!;

            if (_viewEngine == null)
            {
                Monitor.Log("StardewUI не найден. Справочник будет отключён.", LogLevel.Warn);
                return;
            }

            _viewEngine.RegisterViews("Mods/marilynsinister.HarveyStressMeter/Views", "assets/views");
            _viewEngine.RegisterSprites("Mods/marilynsinister.HarveyStressMeter/Sprites", "assets/sprites");

            _handbookManager = new HandbookManager(_iconsTex);
        }

        // ===== События дня =====

        private void OnDayStarted(object? s, DayStartedEventArgs e)
        {
            Monitor.Log("[OnDayStarted] Начинаем новый день", LogLevel.Debug);
            _prevStamina = Game1.player.Stamina;
            _data.TalkedNpcsToday.Clear();
            _data.OverworkBreaksToday = 0;
            _data.OverworkBreakSeconds = 0;
            _data.OverworkBreakActive = false;
            _data.TalkedToHarveyToday = false;

            //_treatmentService.SyncQuestsAndBuffs();
            _stateService.SyncWithGame();
            // Очищаем топики лечения без соответствующих дебаффов
            _treatmentService.CleanupOrphanedTreatmentTopics();

            // Восстанавливаем активные невылеченные дебаффы
            _treatmentService.RestoreActiveStressBuffs();

            // Проверка триггеров стресса
            CheckDayStartedStressTriggers();

            // Логируем состояние игрока в начале дня для отладки
            ReflectionHelper.LogPlayer(Game1.player, Monitor);

            Monitor.Log("[OnDayStarted] Новый день инициализирован", LogLevel.Debug);

            // Одиночество - отложенное применение
            if (ConversationHelper.HasTopic(TopicIds.LonelyPending))
            {
                ConversationHelper.RemoveTopic(TopicIds.LonelyPending);
                _treatmentService.ApplyStressBuff(BuffIds.Lonely, "Одиночество");
            }



            // Сброс счетчиков квестов
            ResetDailyQuestCounters();

            SaveData();

             Monitor.Log($"[OnDayStarted] Новый день. Активных лечений: {_data.StressState.ActiveTreatments.Count}", LogLevel.Info);
        }

        private void CheckDayStartedStressTriggers()
        {
            // Tired - низкая стамина при начале дня
            if (Game1.stats.DaysPlayed >= 1
                && Game1.player.Stamina >= 0 && Game1.player.Stamina <= 10
                && !_stateService.HasActiveBuffInGame(BuffIds.Immunity)
                && !_stateService.HasActiveBuffInGame(BuffIds.Tired))
            {
                Monitor.Log($"[CheckDayStartedStressTriggers] Применяем дебафф Tired (стамина: {Game1.player.Stamina})", LogLevel.Info);
                _treatmentService.ApplyStressBuff(BuffIds.Tired, "Усталость");
            }

            // Thunder - гроза
            if (Game1.stats.DaysPlayed >= 2
                && Game1.isLightning
                && !_stateService.HasActiveBuffInGame(BuffIds.Thunder)
                && !_stateService.HasActiveBuffInGame(BuffIds.Immunity))
            {
                _treatmentService.ApplyStressBuff(BuffIds.Thunder, "Страх грозы");
            }

            // TooCold - холодная погода в холодных локациях
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

        private void RestoreStressBuffsFromTopics()
        {
            // Восстановить баффы стресса если есть соответствующие топики
            var topicToBuffMap = new Dictionary<string, string>
            {
                [TopicIds.StressTired] = BuffIds.Tired,
                [TopicIds.StressLonely] = BuffIds.Lonely,
                [TopicIds.StressThunder] = BuffIds.Thunder,
                [TopicIds.StressHunger] = BuffIds.Hunger,
                [TopicIds.StressOverwork] = BuffIds.Overwork,
                [TopicIds.StressNoSleep] = BuffIds.NoSleep,
                [TopicIds.StressTooCold] = BuffIds.TooCold,
                [TopicIds.StressSocial] = BuffIds.Social,
                [TopicIds.StressDarkness] = BuffIds.Darkness,
            };

            foreach (var (topic, buffId) in topicToBuffMap)
            {
                // Не восстанавливать баффы, которые заблокированы через квестовую систему
                if (ConversationHelper.HasTopic(topic)
                    && !_stateService.HasActiveBuffInGame(buffId)
                    && !_data.StressState.IsTreatmentLocked(buffId))
                {
                    _buffService.ApplyBuffFromData(buffId);
                }
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
                // Сброс счетчика минут при грозе
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
                // TalkedUniqueToday НЕ сбрасывается - это базовое значение при получении квеста
                // Сбрасываем только счетчик разговоров после квеста и время с Харви
                socialTreatment.Progress.SocialTalksAfterQuest = 0;
                socialTreatment.Progress.SecondsNearHarvey = 0;
            }
        }

        private void OnDayEnding(object? sender, DayEndingEventArgs e)
        {
            // Hunger - проверка факта еды
            if (Game1.stats.DaysPlayed >= 3
                && !ConversationHelper.HasTopic(TopicIds.AteToday)
                && !_stateService.HasActiveBuffInGame(BuffIds.Hunger)
                && !_stateService.HasActiveBuffInGame(BuffIds.Immunity))
            {
                _treatmentService.ApplyStressBuff(BuffIds.Hunger, "Голод");
            }

            // NoSleep - поздний отбой
            if (GameStateHelper.IsTimeBetween(2400, 2600)
                && !_stateService.HasActiveBuffInGame(BuffIds.NoSleep)
                && !_stateService.HasActiveBuffInGame(BuffIds.Immunity))
            {
                _treatmentService.ApplyStressBuff(BuffIds.NoSleep, "Недосып");
            }

            // Обновление счетчика раннего отбоя
            UpdateEarlySleepStreak();

            // Проверка завершения квестов NoSleep и Darkness
            CheckDayEndingQuestCompletion();

            // Логируем состояние данных в конце дня для отладки
            ReflectionHelper.LogObjectFields(_data, Monitor, "SaveData_EndOfDay");

            SaveData();
        }

        private void UpdateEarlySleepStreak()
        {
            var noSleepTreatment = GetTreatmentByQuest(QuestIds.NoSleep);
            if (noSleepTreatment?.Progress != null)
            {
                if (Game1.timeOfDay < 2400)
                    noSleepTreatment.Progress.EarlySleepStreak = Math.Min(3, noSleepTreatment.Progress.EarlySleepStreak + 1);
                else
                    noSleepTreatment.Progress.EarlySleepStreak = 0;
            }
        }

        private void CheckDayEndingQuestCompletion()
        {
            // NoSleep - завершение при раннем отбое
            if (_stateService.HasQuestInJournal(QuestIds.NoSleep)
                && Game1.timeOfDay >= 600 && Game1.timeOfDay <= 2200)
            {
                _stateService.CompleteTreatment(QuestIds.NoSleep);
                ConversationHelper.AddTopic("topicStressTreatmentNoSleepCured", 2);
                Game1.playSound("questcomplete");
            }

            // Darkness - завершение если провели вечер при свете
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

        // ===== Обновление каждый тик =====

        private void OnUpdateTicked(object? s, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // Manual триггеры - каждую секунду
            if (e.IsMultipleOf(60))
            {
                _triggerService.UpdateTreatmentProgress(GameStateHelper.IsHarveyNearby());
                _triggerService.CheckManualTriggers();
                ProcessGameTick();

                // Проверка топиков начала лечения
                CheckTreatmentStartTopics();
            }
        }

        private void ProcessGameTick()
        {
            bool harveyNearby = GameStateHelper.IsHarveyNearby();

            // Аура заботы Харви
            if (harveyNearby)
                _buffService.ApplyBuff(BuffIds.CareAura, "Рядом с Харви",
                    new StardewValley.Buffs.BuffEffects { Defense = { +1 }, MaxStamina = { +10 } }, 2000);
            else
                _buffService.RemoveBuff(BuffIds.CareAura);

            // Thunder quest - бафф успокоения
            ApplyThunderCalmingBuff(harveyNearby);

            // Детекция еды по стамине
            DetectFoodConsumption();

            // Естественное снятие баффов
            NaturalBuffRemoval(harveyNearby);

            // Обновление прогресса лечения НЕ здесь - оно уже вызывается каждую секунду в OnUpdateTicked
            // _triggerService.UpdateTreatmentProgress(harveyNearby);
            _treatmentService.EnsureLockedBuffsPersist();
            _treatmentService.SyncQuestsAndBuffs();
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
            float delta = Game1.player.Stamina - _prevStamina;
            bool inSpa = Game1.currentLocation is StardewValley.Locations.BathHousePool;

            if (!inSpa && delta >= 10f && delta <= 200f)
            {
                var hungerTreatment = GetTreatmentByQuest(QuestIds.Hunger);
                if (hungerTreatment?.Progress != null)
                {
                    hungerTreatment.Progress.AteAnyFood = true;
                }

                if (!ConversationHelper.HasTopic(TopicIds.AteToday))
                    ConversationHelper.AddTopic(TopicIds.AteToday, 1);

                // Проверка завершения квеста Hunger при употреблении еды
                if (_stateService.HasQuestInJournal(QuestIds.Hunger))
                {
                    Game1.playSound("questcomplete");
                    _stateService.CompleteTreatment(QuestIds.Hunger);
                    ConversationHelper.AddTopic("topicStressTreatmentHungerCured", 2);
                }

                // Проверка завершения квеста TooCold при употреблении горячих напитков
                // Кофе и эспрессо дают большое восстановление стамины (60+)
                if (_stateService.HasQuestInJournal(QuestIds.TooCold) && delta >= 60f)
                {
                    Game1.playSound("questcomplete");
                    _stateService.CompleteTreatment(QuestIds.TooCold);
                    ConversationHelper.AddTopic("topicStressTreatmentTooColdCured", 2);
                }

                // Естественное снятие голода
                if (_stateService.HasActiveBuffInGame(BuffIds.Hunger) && !_data.StressState.IsTreatmentLocked(BuffIds.Hunger))
                {
                    _buffService.RemoveBuff(BuffIds.Hunger);
                    Game1.addHUDMessage(new HUDMessage("Голод утолён", HUDMessage.newQuest_type));
                }
            }

            _prevStamina = Game1.player.Stamina;
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // Детекция обморока от усталости (срабатывает в 2:00 ночи)
            if (e.NewTime == 200 && Game1.player.Stamina <= 0)
            {
                if (Game1.stats.DaysPlayed >= 1
                    && !_stateService.HasActiveBuffInGame(BuffIds.Overwork)
                    && !_stateService.HasActiveBuffInGame(BuffIds.Immunity))
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Overwork, "Переработка");
                }
            }

            // Проверка молнии каждые 10 минут во время грозы
            if (Game1.isLightning && e.NewTime % 100 == 0)
            {
                CheckLightningStressTrigger();
            }
        }

        private void CheckLightningStressTrigger()
        {
            if (Game1.stats.DaysPlayed < 2) return;
            if (_stateService.HasActiveBuffInGame(BuffIds.Thunder)) return;
            if (_stateService.HasActiveBuffInGame(BuffIds.Immunity)) return;

            // 30% шанс при каждой проверке во время грозы
            if (Game1.random.NextDouble() < 0.3)
            {
                _treatmentService.ApplyStressBuff(BuffIds.Thunder, "Страх грозы");
            }
        }

        private void NaturalBuffRemoval(bool harveyNearby)
        {
            // Tired - отдых дома поздним вечером
            if (_stateService.HasActiveBuffInGame(BuffIds.Tired)
                && !_data.StressState.IsTreatmentLocked(BuffIds.Tired)
                && Game1.player.currentLocation is StardewValley.Locations.FarmHouse
                && Game1.timeOfDay >= 2200 && Game1.timeOfDay <= 200)
            {
                _buffService.RemoveBuff(BuffIds.Tired);
                ConversationHelper.RemoveTopic(TopicIds.StressTired);
            }

            // Lonely - снятие при разговоре с Харви
            if (_stateService.HasActiveBuffInGame(BuffIds.Lonely)
                && !_data.StressState.IsTreatmentLocked(BuffIds.Lonely)
                && harveyNearby)
            {
                _buffService.RemoveBuff(BuffIds.Lonely);
                ConversationHelper.RemoveTopic(TopicIds.StressLonely);
                Game1.getCharacterFromName("Harvey")?.showTextAboveHead("Я всегда рядом.");
            }

            // Thunder - снятие в помещении с Харви
            if (_stateService.HasActiveBuffInGame(BuffIds.Thunder)
                && !_data.StressState.IsTreatmentLocked(BuffIds.Thunder)
                && harveyNearby
                && Game1.player.currentLocation?.NameOrUniqueName == "Hospital"
                && (Game1.isLightning || Game1.isRaining))
            {
                _buffService.RemoveBuff(BuffIds.Thunder);
                ConversationHelper.RemoveTopic(TopicIds.StressThunder);
            }

            // TooCold - снятие в тепле
            if (_stateService.HasActiveBuffInGame(BuffIds.TooCold)
                && !_data.StressState.IsTreatmentLocked(BuffIds.TooCold)
                && GameStateHelper.IsInWarmZone())
            {
                _buffService.RemoveBuff(BuffIds.TooCold);
                ConversationHelper.RemoveTopic(TopicIds.StressTooCold);
            }

            // Darkness - снятие при свете
            if (_stateService.HasActiveBuffInGame(BuffIds.Darkness)
                && !_data.StressState.IsTreatmentLocked(BuffIds.Darkness)
                && GameStateHelper.IsInWarmZone()
                && Game1.timeOfDay >= 2000 && Game1.timeOfDay <= 200)
            {
                _buffService.RemoveBuff(BuffIds.Darkness);
                ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
            }
        }

        // ===== События перемещения =====

        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer) return;

            // Логируем информацию о перемещении для отладки
            Monitor.Log($"[OnWarped] Перемещение: {e.OldLocation?.NameOrUniqueName ?? "null"} → {e.NewLocation?.NameOrUniqueName ?? "null"}", LogLevel.Debug);
            
            if (e.NewLocation != null)
            {
                // Логируем новую локацию для отладки
                ReflectionHelper.LogLocation(e.NewLocation, Monitor);
                
                CheckDarknessDebuff(e.NewLocation);
                ApplyQuestLocationBuffs(e.NewLocation);
            }
        }

        private void CheckDarknessDebuff(GameLocation newLocation)
        {
            if (Game1.stats.DaysPlayed >= 3
                && Game1.timeOfDay >= 2200 && Game1.timeOfDay <= 2600
                && !_stateService.HasActiveBuffInGame(BuffIds.Darkness)
                && !_stateService.HasActiveBuffInGame(BuffIds.Immunity))
            {
                var n = newLocation?.NameOrUniqueName;
                if (n == "Backwoods" || n == "Forest" || n == "Mountain")
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Darkness, "Темнота");
                }
            }
        }

        private void ApplyQuestLocationBuffs(GameLocation newLocation)
        {
            // Tired quest - бафф отдыха дома
            if (_stateService.HasQuestInJournal(QuestIds.Tired)
                && newLocation is StardewValley.Locations.FarmHouse
                && !GameStateHelper.HasHeavyTools(Game1.player))
            {
                _buffService.ApplyBuff(BuffIds.RestingAtHome, "Отдых дома",
                    new StardewValley.Buffs.BuffEffects { }, -2);
            }

            // Overwork - управление перерывами
            ManageOverworkBreaks(newLocation);

            // Darkness - бафф света и безопасности
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

            // Проверка завершения квеста при 3 перерывах
            if (_data.OverworkBreaksToday >= 3)
            {
                _stateService.CompleteTreatment(QuestIds.Overwork);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakActive);
                ConversationHelper.AddTopic("topicStressTreatmentOverworkCured", 2);
                Game1.playSound("questcomplete");
            }
        }

        // ===== События UI =====

        private void OnMenuChanged(object? s, MenuChangedEventArgs e)
        {
            // Логируем информацию о диалогах для отладки
            if (e.NewMenu is DialogueBox && Game1.currentSpeaker is NPC npc)
            {
                Monitor.Log($"[OnMenuChanged] Начался диалог с {npc.Name}", LogLevel.Debug);
                ReflectionHelper.LogNPC(npc, Monitor);
            }

            HandleDialogueEvents(e);

            if (e.NewMenu is GameMenu gm)
            {
                // Можно добавить кнопку справочника
            }
        }

        // ===== ИСПРАВЛЕНИЯ В МЕТОДАХ ДИАЛОГОВ =====

        private void HandleDialogueEvents(MenuChangedEventArgs e)
        {
            if (e.NewMenu is DialogueBox && Game1.currentSpeaker is NPC npc && npc.Name != "Harvey")
            {
                _lastDialogueNpc = npc.Name;

                // Social stress trigger - разговор с NPC с низкой дружбой
                CheckSocialStressTrigger(npc);
            }
            else if (e.NewMenu is DialogueBox && Game1.currentSpeaker is NPC harveyNpc && harveyNpc.Name == "Harvey")
            {
                Monitor.Log($"[Диалог] Начался разговор с Харви. Текущие топики: {string.Join(", ", Game1.player.activeDialogueEvents.Keys.Where(k => k.Contains("Stress")))}", LogLevel.Info);
                Monitor.Log($"[Диалог] Дебафф Social активен: {_stateService.HasActiveBuffInGame(BuffIds.Social)}", LogLevel.Info);
            }
            else if (e.OldMenu is DialogueBox && _lastDialogueNpc != null)
            {
                // Харви не учитывается в счетчике разговоров (только для квестов Lonely и Social)
                if (_lastDialogueNpc != "Harvey")
                {
                    // ⭐ ИЗМЕНЕНО: Добавляем NPC в список ПЕРЕД обновлением прогресса
                    _data.TalkedNpcsToday.Add(_lastDialogueNpc);
                    Monitor.Log($"[Диалог] Завершен разговор с {_lastDialogueNpc}. Всего разговоров сегодня: {_data.TalkedNpcsToday.Count}", LogLevel.Info);

                    // ⭐ ИЗМЕНЕНО: Обновляем прогресс квестов ПОСЛЕ добавления в список
                    // Lonely quest - счетчик разговоров
                    UpdateLonelyQuestProgress();

                    // ⭐ НОВОЕ: Social quest - счетчик разговоров (главное исправление!)
                    UpdateSocialQuestProgress();
                }
                else
                {
                    // Харви не учитывается в счетчике
                    Monitor.Log($"[Диалог] Завершен разговор с Харви (не учитывается в счетчике)", LogLevel.Debug);
                }

                _lastDialogueNpc = null;

                if (!ConversationHelper.HasTopic(TopicIds.SpokeToday))
                    ConversationHelper.AddTopic(TopicIds.SpokeToday, 1);
            }
            else if (e.OldMenu is DialogueBox && Game1.currentSpeaker is NPC currentNpc && currentNpc.Name == "Harvey")
            {
                Monitor.Log($"[Диалог] Завершен разговор с Харви.", LogLevel.Info);
                Monitor.Log($"[Диалог] Текущие топики: {string.Join(", ", Game1.player.activeDialogueEvents.Keys.Where(k => k.Contains("Stress") || k.Contains("Treatment")))}", LogLevel.Info);
                Monitor.Log($"[Диалог] Дебафф Social после разговора: {_stateService.HasActiveBuffInGame(BuffIds.Social)}", LogLevel.Info);

                // ⭐ НОВОЕ: Обновляем прогресс социального квеста после разговора с Харви (для обновления UI)
                UpdateSocialQuestProgress();
            }
        }

        private void CheckSocialStressTrigger(NPC npc)
        {
            if (Game1.stats.DaysPlayed < 5) return;
            if (_stateService.HasActiveBuffInGame(BuffIds.Social)) return;
            if (_stateService.HasActiveBuffInGame(BuffIds.Immunity)) return;

            // Проверяем дружбу с NPC
            if (Game1.player.friendshipData.TryGetValue(npc.Name, out var friendship))
            {
                // Если дружба < 750 (менее 3 сердец) и случайность 30%
                // Социальный стресс от общения с малознакомыми людьми
                if (friendship.Points < 750 && Game1.random.NextDouble() < 0.3)
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Social, "Социальный дискомфорт");
                    Monitor.Log($"[Social Stress] Триггер активирован при разговоре с {npc.Name} (дружба: {friendship.Points}/750)", LogLevel.Info);
                }
            }
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
                    SaveData();
                }
            }
        }

        /// <summary>
        /// ⭐ ПОЛНОСТЬЮ ПЕРЕПИСАННЫЙ МЕТОД - исправлен подсчет разговоров
        /// Обновляет прогресс квеста социальной тревожности
        /// </summary>
        private void UpdateSocialQuestProgress()
        {
            // ⭐ ИЗМЕНЕНО: Используем новое состояние вместо проверки квеста в журнале
            if (!_data.StressState.HasActiveQuest(QuestIds.Social))
            {
                return; // Квест не активен
            }

            var socialTreatment = GetTreatmentByQuest(QuestIds.Social);
            if (socialTreatment == null)
            {
                Monitor.Log($"[UpdateSocialQuestProgress] ⚠️ Лечение для квеста Social не найдено", LogLevel.Warn);
                return;
            }

            if (socialTreatment.Progress == null)
            {
                Monitor.Log($"[UpdateSocialQuestProgress] ⚠️ Progress == null для квеста Social", LogLevel.Warn);
                return;
            }

            // ⭐ НОВОЕ: Правильный подсчет разговоров ПОСЛЕ получения квеста
            // TalkedUniqueToday = базовое значение при получении квеста (сколько было разговоров ДО квеста)
            // TalkedNpcsToday.Count = текущее общее количество разговоров сегодня
            int baseConversations = socialTreatment.Progress.TalkedUniqueToday;
            int currentTotal = _data.TalkedNpcsToday.Count;

            // ⭐ НОВОЕ: Считаем разницу - сколько разговоров было ПОСЛЕ получения квеста
            int conversationsAfterQuest = Math.Max(0, currentTotal - baseConversations);

            // ⭐ НОВОЕ: Проверяем, изменилось ли количество разговоров
            bool conversationsChanged = socialTreatment.Progress.SocialTalksAfterQuest != conversationsAfterQuest;

            if (conversationsChanged)
            {
                // ⭐ НОВОЕ: Обновляем ПРАВИЛЬНОЕ ПОЛЕ в прогрессе
                socialTreatment.Progress.SocialTalksAfterQuest = conversationsAfterQuest;

                // ⭐ НОВОЕ: Детальное логирование для отладки
                Monitor.Log($"[UpdateSocialQuestProgress] ═══ ОБНОВЛЕНИЕ ПРОГРЕССА ═══", LogLevel.Info);
                Monitor.Log($"[UpdateSocialQuestProgress] База при получении квеста: {baseConversations}", LogLevel.Info);
                Monitor.Log($"[UpdateSocialQuestProgress] Текущее общее количество: {currentTotal}", LogLevel.Info);
                Monitor.Log($"[UpdateSocialQuestProgress] Разговоров после квеста: {conversationsAfterQuest}", LogLevel.Info);
                Monitor.Log($"[UpdateSocialQuestProgress] Время с Харви: {socialTreatment.Progress.SecondsNearHarvey}/60 сек", LogLevel.Info);

                // ⭐ НОВОЕ: Обновляем описание квеста через TriggerService
                _triggerService?.UpdateQuestDescription(socialTreatment.Progress);

                // ⭐ НОВОЕ: HUD уведомление при каждом новом разговоре
                if (conversationsAfterQuest <= 5)
                {
                    Game1.addHUDMessage(new HUDMessage($"Прогресс: {conversationsAfterQuest}/5 разговоров", HUDMessage.newQuest_type));
                }

                // ⭐ НОВОЕ: Сохраняем изменения
                SaveData();
            }
        }

        private void OnRenderedActiveMenu(object? s, RenderedActiveMenuEventArgs e)
        {
            if (Game1.activeClickableMenu is not GameMenu gm) return;
            if (!IsInventoryPage(gm)) return;

            float ui = Game1.options.uiScale;
            int size = (int)(HandbookSize * ui);

            var anchor = TryGetTrashCanBounds(gm)
                ?? new Rectangle(
                    gm.xPositionOnScreen + gm.width - (int)(96 * ui),
                    gm.yPositionOnScreen + gm.height - (int)(96 * ui),
                    (int)(64 * ui), (int)(64 * ui));

            int x = anchor.X + (anchor.Width - size) / 2;
            int y = anchor.Bottom + (int)(18 * ui);
            _handbookRect = new Rectangle(x, y, size, size);

            e.SpriteBatch.Draw(_handbookTex, _handbookRect, Color.White);

            if (_handbookRect.Contains(Game1.getMouseX(), Game1.getMouseY()))
                IClickableMenu.drawHoverText(e.SpriteBatch, "Справочник Харви", Game1.smallFont);

            gm.drawMouse(e.SpriteBatch);
        }

        private void OnButtonPressed(object? s, ButtonPressedEventArgs e)
        {
            if (Game1.activeClickableMenu is not GameMenu gm) return;
            if (!IsInventoryPage(gm)) return;

            if (e.Button.IsUseToolButton() || e.Button == SButton.MouseLeft || e.Button == SButton.ControllerA)
            {
                if (_handbookRect.Contains(Game1.getMouseX(), Game1.getMouseY()))
                {
                    OpenHandbook();
                    Helper.Input.Suppress(SButton.MouseLeft);
                }
            }
        }

        private void OnButtonsChanged(object? s, ButtonsChangedEventArgs e)
        {
            if (!Context.IsPlayerFree) return;

            if (_config.OpenHandbook.JustPressed())
            {
                OpenHandbook();
                Helper.Input.SuppressActiveKeybinds(_config.OpenHandbook);
            }
        }

        // ===== Вспомогательные методы =====

        private void OpenHandbook()
        {
            if (_viewEngine == null || _handbookManager == null) return;
            var vm = _handbookManager.BuildViewModel(_data);
            Game1.activeClickableMenu = _viewEngine.CreateMenuFromAsset(
                $"Mods/{ModManifest.UniqueID}/Views/Handbook", vm);
        }

        private bool IsInventoryPage(GameMenu gm) => gm.currentTab == GameMenu.inventoryTab;

        private Rectangle? TryGetTrashCanBounds(GameMenu gm)
        {
            if (gm.pages[gm.currentTab] is InventoryPage inv)
            {
                var field = Helper.Reflection.GetField<ClickableTextureComponent>(inv, "trashCan", false);
                var trash = field?.GetValue();
                if (trash != null) return trash.bounds;
            }
            return null;
        }

        private void SaveData() => Helper.Data.WriteSaveData(SaveKey, _data);

        /// <summary>
        /// Проверяет топики начала лечения (устанавливаются из диалогов при согласии игрока)
        /// Проверяет наличие соответствующего дебаффа перед запуском лечения
        /// </summary>
        private void CheckTreatmentStartTopics()
        {
            var treatmentTopics = new Dictionary<string, (string buffId, string displayName)>
            {
                [TopicIds.TreatmentStartTired] = (BuffIds.Tired, "Усталость"),
                [TopicIds.TreatmentStartLonely] = (BuffIds.Lonely, "Одиночество"),
                [TopicIds.TreatmentStartThunder] = (BuffIds.Thunder, "Страх грозы"),
                [TopicIds.TreatmentStartHunger] = (BuffIds.Hunger, "Голод"),
                [TopicIds.TreatmentStartOverwork] = (BuffIds.Overwork, "Переработка"),
                [TopicIds.TreatmentStartNoSleep] = (BuffIds.NoSleep, "Недосып"),
                [TopicIds.TreatmentStartTooCold] = (BuffIds.TooCold, "Переохлаждение"),
                [TopicIds.TreatmentStartSocial] = (BuffIds.Social, "Социальный дискомфорт"),
                [TopicIds.TreatmentStartDarkness] = (BuffIds.Darkness, "Темнота"),
                [TopicIds.TreatmentStartCriticism] = (BuffIds.Criticism, "Самокритика"),
                [TopicIds.TreatmentStartBadDream] = (BuffIds.BadDream, "Кошмары"),
                [TopicIds.TreatmentStartPanic] = (BuffIds.Panic, "Паническая атака"),
                [TopicIds.TreatmentStartSleepDeprivation] = (BuffIds.SleepDeprivation, "Депривация сна"),
                [TopicIds.TreatmentStartAnxietyWave] = (BuffIds.AnxietyWave, "Волна тревоги"),
                [TopicIds.TreatmentStartMentalFatigue] = (BuffIds.MentalFatigue, "Ментальная усталость"),
                [TopicIds.TreatmentStartShadowParanoia] = (BuffIds.ShadowParanoia, "Страх теней"),
                [TopicIds.TreatmentStartFreezeResponse] = (BuffIds.FreezeResponse, "Реакция замирания"),
                [TopicIds.TreatmentStartIsolation] = (BuffIds.Isolation, "Изоляция"),
                [TopicIds.TreatmentStartBreakdown] = (BuffIds.Breakdown, "Эмоциональный срыв"),
                [TopicIds.TreatmentStartCollapse] = (BuffIds.Collapse, "Обморок"),
                [TopicIds.TreatmentStartNumbness] = (BuffIds.Numbness, "Эмоциональное онемение"),
                [TopicIds.TreatmentStartDespair] = (BuffIds.Despair, "Отчаяние"),
            };

            int foundTopics = 0;
            int startedTreatments = 0;

            foreach (var (topic, (buffId, displayName)) in treatmentTopics)
            {
                if (ConversationHelper.HasTopic(topic))
                {
                    foundTopics++;
                    Monitor.Log($"[Лечение] ═══ Найден топик начала лечения: {topic} → {displayName} ═══", LogLevel.Info);
                    Monitor.Log($"[Лечение] Текущее состояние ПЕРЕД началом:", LogLevel.Info);
                    Monitor.Log($"[Лечение]   - HasBuff({buffId}): {_stateService.HasActiveBuffInGame(buffId)}", LogLevel.Info);
                    Monitor.Log($"[Лечение]   - В ActiveTreatments: {_data.StressState.IsTreatmentLocked(buffId)}", LogLevel.Info);
                    Monitor.Log($"[Лечение]   - Всего разговоров сегодня: {_data.TalkedNpcsToday.Count}", LogLevel.Info);

                    // Удалить топик-триггер
                    ConversationHelper.RemoveTopic(topic);

                    // Проверяем, можно ли начать лечение
                    bool shouldStartTreatment = CanStartTreatment(buffId);
                    string reason = shouldStartTreatment ? 
                        (_stateService.HasActiveBuffInGame(buffId) ? "активный дебафф" : "состояние в сохранении") : 
                        "нет активного дебаффа или валидного состояния";

                    if (shouldStartTreatment)
                    {
                        Monitor.Log($"[Лечение] ✓ {displayName}: {reason}", LogLevel.Info);
                        
                        // Восстанавливаем дебафф, если его нет
                        if (!_stateService.HasActiveBuffInGame(buffId))
                        {
                            _buffService.ApplyBuffFromData(buffId);
                            Monitor.Log($"[Лечение] ✓ {displayName}: дебафф восстановлен из сохранения", LogLevel.Info);
                        }
                    }
                    else
                    {
                        Monitor.Log($"[Лечение] ✗ {displayName}: {reason}", LogLevel.Warn);
                    }

                    if (shouldStartTreatment)
                    {
                        // Запустить лечение
                        Monitor.Log($"[Лечение] ▶ Вызов StartTreatment для {displayName}...", LogLevel.Info);
                        _treatmentService.StartTreatment(buffId, displayName);
                        startedTreatments++;

                        // Проверяем состояние ПОСЛЕ
                        Monitor.Log($"[Лечение] Состояние ПОСЛЕ StartTreatment:", LogLevel.Info);
                        Monitor.Log($"[Лечение]   - HasBuff({buffId}): {_stateService.HasActiveBuffInGame(buffId)}", LogLevel.Info);
                        Monitor.Log($"[Лечение]   - В ActiveTreatments: {_data.StressState.IsTreatmentLocked(buffId)}", LogLevel.Info);
                        
                        // Логируем состояние данных после начала лечения
                        ReflectionHelper.LogObjectFields(_data.StressState, Monitor, "StressState_AfterTreatmentStart");
                        
                        Monitor.Log($"[Лечение] ✅ УСПЕШНО: Лечение начато (причина: {reason})", LogLevel.Info);
                    }
                    else
                    {
                        Monitor.Log($"[Лечение] ❌ ПРОПУСК: Дебафф {displayName} не активен", LogLevel.Warn);
                    }
                }
            }

            if (foundTopics > 0)
            {
                Monitor.Log($"[Лечение] Итог проверки: найдено топиков={foundTopics}, начато лечений={startedTreatments}", LogLevel.Info);
            }
        }

        #region Вспомогательные методы для работы с лечением

        /// <summary>
        /// Безопасно получает лечение по квесту с проверкой на null
        /// </summary>
        private TreatmentState? GetTreatmentByQuest(string questId)
        {
            return _data.StressState.GetActiveTreatmentByQuest(questId);
        }

        /// <summary>
        /// Безопасно получает лечение по баффу с проверкой на null
        /// </summary>
        private TreatmentState? GetTreatmentByBuff(string buffId)
        {
            return _data.StressState.GetActiveTreatment(buffId);
        }

        /// <summary>
        /// Проверяет, можно ли начать лечение для данного баффа
        /// </summary>
        private bool CanStartTreatment(string buffId)
        {
            // Проверяем активный дебафф
            if (_stateService.HasActiveBuffInGame(buffId))
                return true;

            // Проверяем валидное состояние в истории
            return _data.StressState.HasValidTreatmentInHistory(buffId, SDate.Now());
        }

        /// <summary>
        /// Обновляет прогресс лечения с проверкой на null
        /// </summary>
        private void UpdateTreatmentProgressSafe(string questId, Action<TreatmentProgress> updateAction)
        {
            var treatment = GetTreatmentByQuest(questId);
            if (treatment?.Progress != null)
            {
                updateAction(treatment.Progress);
            }
        }

        #endregion
    }
}



using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using HarmonyLib;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using HarveyStressMeter.Handlers;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Patches;

namespace HarveyStressMeter
{
    /// <summary>
    /// Main entry point for HarveyStressMeter mod
    /// Follows Single Responsibility Principle - only initialization and coordination
    /// </summary>
    public sealed class ModEntry : Mod
    {
        // Configuration and data
        private ModConfig _config = null!;
        private SaveData _data = new();
        private const string SaveKey = "stress-data-v1";

        // Services
        private BuffService _buffService = null!;
        private QuestService _questService = null!;
        private StateService _stateService = null!;
        private TreatmentService _treatmentService = null!;
        private TriggerService _triggerService = null!;
        private DarknessService _darknessService = null!;
        private StressDialogueService _stressDialogueService = null!;
        private GameDataService _gameDataService = null!;

        // Handlers (following SRP)
        private Handlers.EventHandler _eventHandler = null!;
        private GameLogicHandler _gameLogicHandler = null!;
        private ConsoleCommandHandler _consoleCommandHandler = null!;
        private UIHandler _uiHandler = null!;
        private IModHelper _helper = null!;
        private Harmony? _harmony;

        public override void Entry(IModHelper helper)
        {
            _helper = helper;
            _config = helper.ReadConfig<ModConfig>();

            // ⭐ НОВОЕ: Инициализация Harmony для патчей
            InitializeHarmony();

            // Initialize services (following DIP - depend on abstractions)
            InitializeServices();

            // Initialize handlers (following SRP - each handles one responsibility)
            InitializeHandlers();

            // Subscribe to events through EventHandler
            _eventHandler.SubscribeToEvents();

            // Register console commands through ConsoleCommandHandler
            _consoleCommandHandler.RegisterCommands();
        }

        /// <summary>
        /// ⭐ НОВОЕ: Инициализирует Harmony патчи для отслеживания игровых событий
        /// </summary>
        private void InitializeHarmony()
        {
            try
            {
                _harmony = new Harmony(ModManifest.UniqueID);
                Monitor.Log("✅ Harmony инициализирован", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"❌ Ошибка при инициализации Harmony: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }

        private void InitializeServices()
        {
            // Initialize helpers
            ConversationHelper.Initialize(Monitor);

            // Create services (low-level modules)
            _buffService = new BuffService(Monitor);
            _questService = new QuestService(Monitor);
            _stateService = new StateService(_data, Monitor, _buffService, _questService);
            _treatmentService = new TreatmentService(_data, _buffService, _questService, _stateService, Monitor);
            _triggerService = new TriggerService(_data, _buffService, _questService, _stateService, _treatmentService, Monitor);
            _darknessService = new DarknessService(_data, _buffService, _stateService, Monitor);
            _stressDialogueService = new StressDialogueService(Monitor, _helper, _stateService, _treatmentService);
            _gameDataService = new GameDataService(Monitor, _helper);
            
            // ⭐ НОВОЕ: Загружаем данные из JSON
            _stressDialogueService.LoadDialogues();
            _gameDataService.LoadAllData();
            
            // ⭐ НОВОЕ: Связываем GameDataService с TreatmentService
            _treatmentService.SetGameDataService(_gameDataService);
        }

        private void InitializeHandlers()
        {
            // Create handlers (high-level modules that depend on services)
            _uiHandler = new UIHandler(Monitor, _data, _helper);
            _gameLogicHandler = new GameLogicHandler(_data, Monitor, _treatmentService, _triggerService, _buffService, _stateService, _darknessService, _stressDialogueService);
            _eventHandler = new Handlers.EventHandler(Monitor, _helper, _data, _stateService, _gameLogicHandler, _uiHandler, _darknessService);
            _consoleCommandHandler = new ConsoleCommandHandler(Monitor, _helper, _data, _treatmentService, _triggerService, _stateService, _uiHandler);
            
            // Патч еды применяется после создания GameLogicHandler, когда callback уже известен
            FoodConsumptionPatch.Initialize(Monitor, () => _gameLogicHandler.OnFoodConsumed());
            if (_harmony != null)
                FoodConsumptionPatch.Apply(_harmony);
            else
                Monitor.Log("❌ Harmony не инициализирован — отслеживание еды отключено", LogLevel.Error);
        }
    }
}
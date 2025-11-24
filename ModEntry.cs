using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using HarveyStressMeter.Handlers;
using HarveyStressMeter.Helpers;

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

        // Handlers (following SRP)
        private Handlers.EventHandler _eventHandler = null!;
        private GameLogicHandler _gameLogicHandler = null!;
        private ConsoleCommandHandler _consoleCommandHandler = null!;
        private UIHandler _uiHandler = null!;
        private IModHelper _helper = null!;

        public override void Entry(IModHelper helper)
        {
            _helper = helper;
            _config = helper.ReadConfig<ModConfig>();

            // Initialize services (following DIP - depend on abstractions)
            InitializeServices();

            // Initialize handlers (following SRP - each handles one responsibility)
            InitializeHandlers();

            // Subscribe to events through EventHandler
            _eventHandler.SubscribeToEvents();

            // Register console commands through ConsoleCommandHandler
            _consoleCommandHandler.RegisterCommands();
        }

        private void InitializeServices()
        {
            // Initialize helpers
            ConversationHelper.Initialize(Monitor);

            // Create services (low-level modules)
            _buffService = new BuffService();
            _questService = new QuestService(Monitor);
            _stateService = new StateService(_data, Monitor, _buffService, _questService);
            _treatmentService = new TreatmentService(_data, _buffService, _questService, _stateService, Monitor);
            _triggerService = new TriggerService(_data, _buffService, _questService, _stateService, _treatmentService, Monitor);
        }

        private void InitializeHandlers()
        {
            // Create handlers (high-level modules that depend on services)
            _uiHandler = new UIHandler(Monitor, _data, _helper);
            _gameLogicHandler = new GameLogicHandler(_data, Monitor, _treatmentService, _triggerService, _buffService, _stateService);
            _eventHandler = new Handlers.EventHandler(Monitor, _helper, _data, _stateService, _gameLogicHandler, _uiHandler);
            _consoleCommandHandler = new ConsoleCommandHandler(Monitor, _helper, _data, _treatmentService, _triggerService, _stateService, _uiHandler);
        }
    }
}
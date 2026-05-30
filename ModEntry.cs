using System;
using System.Collections.Concurrent;
using System.Text.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using HarmonyLib;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using HarveyStressMeter.Handlers;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Patches;
using HarveyStressMeter.Testing;

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

        // Services
        private BuffService _buffService = null!;
        private QuestService _questService = null!;
        private StateService _stateService = null!;
        private TreatmentService _treatmentService = null!;
        private TriggerService _triggerService = null!;
        private EpisodeQuestProgressService _episodeQuestProgressService = null!;
        private DarknessService _darknessService = null!;
        private StressDialogueService _stressDialogueService = null!;
        private StressTreatmentReviewService _stressTreatmentReviewService = null!;
        private GameDataService _gameDataService = null!;
        private ModResetService _modResetService = null!;
        private StressLoadService _stressLoadService = null!;
        private TreatmentEpisodeService _treatmentEpisodeService = null!;
        private ThunderFlashbackService _thunderFlashbackService = null!;
        private HarveyFlashbackRescueService _harveyFlashbackRescueService = null!;
        private HarveyCareTrustService _harveyCareTrustService = null!;
        private HarveySafePersonAuraService _harveySafePersonAuraService = null!;
        private StressSystemsCoordinator _stressSystemsCoordinator = null!;
        private HarveyCareTrustDialogueService _harveyCareTrustDialogueService = null!;
        private StressGameplayEffectService _stressGameplayEffectService = null!;
        private StressMeterHudService _stressMeterHudService = null!;

        // Handlers (following SRP)
        private Handlers.EventHandler _eventHandler = null!;
        private GameLogicHandler _gameLogicHandler = null!;
        private ConsoleCommandHandler _consoleCommandHandler = null!;
        private UIHandler _uiHandler = null!;
        private IModHelper _helper = null!;
        private Harmony? _harmony;
        private StressMcpServer? _stressMcpServer;
        private StressMcpToolHandler? _stressMcpToolHandler;
        private readonly McpWaitService _mcpWaitService = new();
        private readonly McpWarpService _mcpWarpService = new();
        private readonly ConcurrentQueue<(string Tool, JsonElement? Args, TaskCompletionSource<string> Completion)> _mcpCommandQueue = new();

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

            _helper.Events.Content.AssetRequested += OnAssetRequested;

            // Register console commands through ConsoleCommandHandler
            _consoleCommandHandler.RegisterCommands();

            _stressMcpToolHandler = new StressMcpToolHandler(
                Monitor,
                _data,
                _treatmentService,
                _stateService,
                _buffService,
                _questService,
                _stressDialogueService,
                _modResetService,
                _harveyCareTrustService,
                _stressLoadService,
                _harveyFlashbackRescueService,
                _harveySafePersonAuraService,
                _stressMeterHudService,
                _treatmentEpisodeService,
                _gameLogicHandler,
                _darknessService);

            if (_config.EnableStressMcp)
            {
                helper.Events.GameLoop.UpdateTicked += OnProcessMcpCommandQueue;
                _stressMcpServer = new StressMcpServer(Monitor, ExecuteMcpTool);
                _stressMcpServer.Start(_config.StressMcpPort);
            }
        }

        private string ExecuteMcpTool(string toolName, JsonElement? arguments)
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _mcpCommandQueue.Enqueue((toolName, arguments, completion));

            var timeout = ResolveMcpToolTimeout(toolName, arguments);
            if (!completion.Task.Wait(timeout))
            {
                return toolName == "mcp_wait_seconds"
                    ? $"Error: timeout waiting for mcp_wait_seconds ({timeout.TotalSeconds:0}s)."
                    : "Error: timeout waiting for game thread (30s). Is the game running?";
            }

            return completion.Task.GetAwaiter().GetResult();
        }

        private static TimeSpan ResolveMcpToolTimeout(string toolName, JsonElement? arguments)
        {
            if (string.Equals(toolName, "mcp_warp", StringComparison.Ordinal))
                return TimeSpan.FromSeconds(20);

            if (!string.Equals(toolName, "mcp_wait_seconds", StringComparison.Ordinal))
                return TimeSpan.FromSeconds(30);

            if (arguments is { ValueKind: JsonValueKind.Object } args
                && args.TryGetProperty("seconds", out var el)
                && el.TryGetInt32(out var seconds)
                && seconds >= 0)
            {
                return TimeSpan.FromSeconds(Math.Max(30, seconds + 15));
            }

            return TimeSpan.FromSeconds(30);
        }

        private void OnProcessMcpCommandQueue(object? sender, StardewModdingAPI.Events.UpdateTickedEventArgs e)
        {
            _mcpWaitService.Tick();
            _mcpWarpService.Tick();

            while (_mcpCommandQueue.TryDequeue(out var item))
            {
                try
                {
                    if (_mcpWaitService.TryHandle(item.Tool, item.Args, item.Completion))
                        continue;

                    if (_mcpWarpService.TryHandle(item.Tool, item.Args, item.Completion))
                        continue;

                    item.Completion.TrySetResult(_stressMcpToolHandler!.Execute(item.Tool, item.Args));
                }
                catch (Exception ex)
                {
                    item.Completion.TrySetResult($"Error: {ex.Message}");
                }
            }
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
            _stressLoadService = new StressLoadService(_data, _buffService, _stateService, _config, Monitor);

            _harveyCareTrustService = new HarveyCareTrustService(_data, _config, Monitor);
            _stressLoadService.SetTrustService(_harveyCareTrustService);
            Monitor.Log("HarveyCareTrustService initialized", LogLevel.Info);

            var lightningFrightMessageService = new LightningFrightMessageService(Monitor, _config);
            lightningFrightMessageService.Load(_helper);
            _stressGameplayEffectService = new StressGameplayEffectService(
                _config,
                _stressLoadService,
                _buffService,
                _data,
                Monitor);
            _treatmentEpisodeService = new TreatmentEpisodeService(
                _data,
                _stressLoadService,
                _stateService,
                _questService,
                _buffService,
                Monitor);
            _treatmentService = new TreatmentService(_data, _buffService, _questService, _stateService, Monitor);
            _treatmentService.SetStressLoadService(_stressLoadService);
            _treatmentService.SetTreatmentEpisodeService(_treatmentEpisodeService);
            _treatmentEpisodeService.SetTreatmentService(_treatmentService);
            _treatmentEpisodeService.SetTrustService(_harveyCareTrustService);
            _episodeQuestProgressService = new EpisodeQuestProgressService(
                _data,
                _questService,
                _treatmentService,
                Monitor);
            _treatmentService.SetEpisodeQuestProgressService(_episodeQuestProgressService);
            _triggerService = new TriggerService(_data, _buffService, _questService, _stateService, _treatmentService, Monitor);
            _triggerService.SetEpisodeQuestProgressService(_episodeQuestProgressService);
            _darknessService = new DarknessService(_data, _buffService, _stateService, _questService, Monitor);
            _darknessService.SetStressLoadService(_stressLoadService);
            _treatmentService.SetDarknessService(_darknessService);
            _thunderFlashbackService = new ThunderFlashbackService(
                _data,
                _stressLoadService,
                _treatmentService,
                _buffService,
                _stateService,
                lightningFrightMessageService,
                _config,
                Monitor);
            _thunderFlashbackService.SetTrustService(_harveyCareTrustService);
            _harveySafePersonAuraService = new HarveySafePersonAuraService(
                _data,
                _config,
                _harveyCareTrustService,
                _stressLoadService,
                _thunderFlashbackService,
                Monitor);
            _thunderFlashbackService.SetSafeAuraService(_harveySafePersonAuraService);
            _thunderFlashbackService.SetEpisodeQuestProgressService(_episodeQuestProgressService);
            Monitor.Log("Safe aura wired", LogLevel.Info);
            _harveyFlashbackRescueService = new HarveyFlashbackRescueService(
                _data,
                _stressLoadService,
                _thunderFlashbackService,
                _treatmentService,
                _config,
                Monitor,
                _harveyCareTrustService);

            _stressSystemsCoordinator = new StressSystemsCoordinator(
                _data,
                _stressLoadService,
                _treatmentEpisodeService,
                _thunderFlashbackService,
                Monitor);
            Monitor.Log("StressSystemsCoordinator initialized", LogLevel.Info);

            _treatmentEpisodeService.SetCoordinator(_stressSystemsCoordinator);
            _thunderFlashbackService.SetCoordinator(_stressSystemsCoordinator);
            _harveyFlashbackRescueService.SetCoordinator(_stressSystemsCoordinator);
            Monitor.Log("Treatment episode service wired", LogLevel.Info);
            Monitor.Log("Rescue service wired", LogLevel.Info);

            _stressTreatmentReviewService = new StressTreatmentReviewService(
                Monitor,
                _stateService,
                _treatmentService,
                _treatmentEpisodeService);
            _stressDialogueService = new StressDialogueService(
                Monitor,
                _helper,
                _data,
                _stateService,
                _treatmentService,
                _treatmentEpisodeService,
                _stressTreatmentReviewService);
            _stressTreatmentReviewService.SetDialogueService(_stressDialogueService);
            _harveyCareTrustDialogueService = new HarveyCareTrustDialogueService(
                _data,
                _harveyCareTrustService,
                _stressDialogueService,
                Monitor);
            _stressDialogueService.SetTrustDialogueService(_harveyCareTrustDialogueService);
            _harveyFlashbackRescueService.SetTrustDialogueService(_harveyCareTrustDialogueService);
            _gameDataService = new GameDataService(Monitor, _helper);

            // ⭐ НОВОЕ: Загружаем данные из JSON
            _stressDialogueService.LoadDialogues();
            _gameDataService.LoadAllData();
            
            // ⭐ НОВОЕ: Связываем GameDataService с TreatmentService
            _treatmentService.SetGameDataService(_gameDataService);
            _modResetService = new ModResetService(_helper, _data, _buffService, _stressDialogueService);

            _stressMeterHudService = new StressMeterHudService(
                _helper,
                Monitor,
                _config,
                _data,
                _stressLoadService,
                _treatmentEpisodeService,
                _stateService);
            _stressMeterHudService.Subscribe();
        }

        private void InitializeHandlers()
        {
            // Create handlers (high-level modules that depend on services)
            _uiHandler = new UIHandler(Monitor, _data, _helper);
            _gameLogicHandler = new GameLogicHandler(_data, Monitor, _treatmentService, _triggerService, _buffService, _stateService, _darknessService, _stressDialogueService, _stressTreatmentReviewService, _stressLoadService, _thunderFlashbackService, _harveyFlashbackRescueService, _harveyCareTrustService, _harveySafePersonAuraService, _stressGameplayEffectService, _episodeQuestProgressService);
            _eventHandler = new Handlers.EventHandler(Monitor, _helper, _data, _stateService, _treatmentService, _gameLogicHandler, _uiHandler, _darknessService, _stressLoadService);
            _consoleCommandHandler = new ConsoleCommandHandler(Monitor, _helper, _data, _treatmentService, _triggerService, _stateService, _uiHandler, _modResetService, _stressDialogueService);

            if (_config.EnableDevTestCommands)
            {
                new StressDebugCommandHandler(
                    Monitor,
                    _helper,
                    _data,
                    _treatmentService,
                    _stateService,
                    _buffService,
                    _questService,
                    _stressDialogueService,
                    _modResetService,
                    _stressTreatmentReviewService,
                    _thunderFlashbackService,
                    _harveyFlashbackRescueService,
                    _harveyCareTrustService,
                    _harveySafePersonAuraService,
                    _stressSystemsCoordinator,
                    _stressLoadService,
                    _treatmentEpisodeService,
                    _stressGameplayEffectService,
                    _darknessService).RegisterCommands();
            }
            else
            {
                Monitor.Log("[DEV/TEST] hs.test.* / stress_* commands disabled (EnableDevTestCommands=false).", LogLevel.Trace);
            }
            
            // Патч еды применяется после создания GameLogicHandler, когда callback уже известен
            FoodConsumptionPatch.Initialize(Monitor, consumed => _gameLogicHandler.OnFoodConsumed(consumed));
            if (_harmony != null)
            {
                FoodConsumptionPatch.Apply(_harmony);
            }
            else
                Monitor.Log("❌ Harmony не инициализирован — отслеживание еды отключено", LogLevel.Error);
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (!e.NameWithoutLocale.IsEquivalentTo("Data/Mail"))
                return;

            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, string>().Data;
                foreach (var mail in _gameDataService.GetAllMails())
                {
                    if (string.IsNullOrEmpty(mail.Id) || data.ContainsKey(mail.Id))
                        continue;

                    data[mail.Id] = $"{mail.Subject}^^{mail.Text}";
                }
            });
        }
    }
}
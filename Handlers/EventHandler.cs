using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using HarveyStressMeter.Services;
using HarveyStressMeter.Models;
using HarveyStressMeter.Helpers;

namespace HarveyStressMeter.Handlers
{
    /// <summary>
    /// Handles all SMAPI events for the mod
    /// Follows Single Responsibility Principle - only handles events
    /// </summary>
    public class EventHandler
    {
        private readonly IMonitor _monitor;
        private readonly IModHelper _helper;
        private readonly SaveData _data;
        private readonly StateService _stateService;
        private readonly GameLogicHandler _gameLogicHandler;
        private readonly UIHandler _uiHandler;
        private readonly DarknessService _darknessService;

        public EventHandler(
            IMonitor monitor,
            IModHelper helper,
            SaveData data,
            StateService stateService,
            GameLogicHandler gameLogicHandler,
            UIHandler uiHandler,
            DarknessService darknessService)
        {
            _monitor = monitor;
            _helper = helper;
            _data = data;
            _stateService = stateService;
            _gameLogicHandler = gameLogicHandler;
            _uiHandler = uiHandler;
            _darknessService = darknessService;
        }

        public void SubscribeToEvents()
        {
            _helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            _helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            _helper.Events.GameLoop.DayStarted += OnDayStarted;
            _helper.Events.GameLoop.DayEnding += OnDayEnding;
            _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            _helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            _helper.Events.Display.MenuChanged += OnMenuChanged;
            _helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
            _helper.Events.Input.ButtonPressed += OnButtonPressed;
            _helper.Events.Input.ButtonsChanged += OnButtonsChanged;
            _helper.Events.Player.Warped += OnWarped;
            _helper.Events.GameLoop.TimeChanged += OnTimeChanged;
        }

        private void OnSaveLoaded(object? s, SaveLoadedEventArgs e)
        {
            _monitor.Log("[OnSaveLoaded] Loading save data", LogLevel.Debug);

            _data.LastDay = SDate.Now();
            _data.StressState ??= new PlayerStressState();

            _stateService.MigrateOldData();
            _stateService.SyncWithGame();

            _monitor.Log($"[OnSaveLoaded] ✅ Save loaded: active treatments={_data.StressState.ActiveTreatments.Count}", LogLevel.Info);
        }

        private void OnReturnedToTitle(object? s, ReturnedToTitleEventArgs e)
        {
            _data.StressState.ActiveTreatments.Clear();
        }

        private void OnDayStarted(object? s, DayStartedEventArgs e)
        {
            _monitor.Log("[OnDayStarted] Starting new day", LogLevel.Debug);
            
            _gameLogicHandler.ResetDailyData();
            
            // ⭐ ОПТИМИЗАЦИЯ: Сбрасываем счетчики оптимизации
            _gameLogicHandler.ResetOptimizationCounters();
            
            // ⭐ НОВОЕ: Очищаем истекшие иммунитеты
            _stateService.CleanupExpiredImmunities();
            
            _stateService.SyncWithGame();
            
            // ⭐ НОВОЕ: Восстанавливаем бафф страха темноты если он был активен
            _darknessService.RestoreFearBuff();
            
            _gameLogicHandler.CheckDayStartedStressTriggers();

            _monitor.Log($"[OnDayStarted] New day initialized: active treatments={_data.StressState.ActiveTreatments.Count}", LogLevel.Info);
        }

        private void OnDayEnding(object? s, DayEndingEventArgs e)
        {
            _gameLogicHandler.CheckDayEndingQuestCompletion();
            _gameLogicHandler.CheckLateSleepPattern();  // ⭐ НОВОЕ: Отслеживание позднего сна
            SaveData();
        }

        private void OnUpdateTicked(object? s, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // ⭐ НОВОЕ: Harmony патч обрабатывает еду, больше не нужна проверка isEating

            if (e.IsMultipleOf(60))
            {
                var harveyNearby = GameStateHelper.IsHarveyNearby();
                _gameLogicHandler.ProcessGameTick(harveyNearby);
            }
        }

        private void OnGameLaunched(object? s, GameLaunchedEventArgs e)
        {
            _uiHandler.Initialize();
        }

        private void OnMenuChanged(object? s, MenuChangedEventArgs e)
        {
            _gameLogicHandler.HandleMenuChanged(e);
        }

        private void OnRenderedActiveMenu(object? s, RenderedActiveMenuEventArgs e)
        {
            _uiHandler.HandleRenderedActiveMenu(e);
        }

        private void OnButtonPressed(object? s, ButtonPressedEventArgs e)
        {
            _uiHandler.HandleButtonPressed(e);
            
            // ⭐ УДАЛЕНО: Больше не нужна проверка еды через ButtonPressed
            // Harmony патч обрабатывает это автоматически
        }

        private void OnButtonsChanged(object? s, ButtonsChangedEventArgs e)
        {
            _uiHandler.HandleButtonsChanged(e);
        }

        private void OnWarped(object? s, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer) return;

            _gameLogicHandler.HandleWarped(e);
        }

        private void OnTimeChanged(object? s, TimeChangedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            _gameLogicHandler.HandleTimeChanged(e);
        }

        private void SaveData()
        {
            _helper.Data.WriteSaveData("stress-data-v1", _data);
        }
    }
}

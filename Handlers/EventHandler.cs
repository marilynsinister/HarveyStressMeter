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
using HarveyStressMeter.Constants;

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
        private readonly StressLoadService _stressLoadService;

        public EventHandler(
            IMonitor monitor,
            IModHelper helper,
            SaveData data,
            StateService stateService,
            GameLogicHandler gameLogicHandler,
            UIHandler uiHandler,
            DarknessService darknessService,
            StressLoadService stressLoadService)
        {
            _monitor = monitor;
            _helper = helper;
            _data = data;
            _stateService = stateService;
            _gameLogicHandler = gameLogicHandler;
            _uiHandler = uiHandler;
            _darknessService = darknessService;
            _stressLoadService = stressLoadService;
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
            var loaded = SaveDataHelper.ReadSaveData(_monitor);

            if (loaded != null)
            {
                SaveDataHelper.CopySaveDataIntoExistingInstance(_data, loaded);
                _monitor.Log(
                    $"[OnSaveLoaded] Mod save loaded from '{SaveDataHelper.SaveKey}': " +
                    $"active treatments={_data.StressState.ActiveTreatments.Count}, " +
                    $"DaysWithoutEating={_data.DaysWithoutEating}, FearLevel={_data.Darkness.FearLevel}",
                    LogLevel.Info);
            }
            else
            {
                SaveDataHelper.ResetSaveDataInPlace(_data);
                _monitor.Log(
                    $"[OnSaveLoaded] No mod save found for '{SaveDataHelper.SaveKey}' — initialized fresh mod state for this slot",
                    LogLevel.Info);
            }

            _stateService.MigrateOldData();
            _stateService.SyncWithGame();

            _gameLogicHandler.ClearStressDialoguePending();

            // Восстанавливаем все активные баффы после загрузки сохранения
            _stateService.RestoreAllActiveBuffs();
            _stressLoadService.SyncFromGameState();
        }

        private void OnReturnedToTitle(object? s, ReturnedToTitleEventArgs e)
        {
            _data.StressState.ClearActiveTreatments();
            _gameLogicHandler.ClearStressDialoguePending();
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
            
            // ⭐ НОВОЕ: Восстанавливаем все активные баффы в начале дня
            _stateService.RestoreAllActiveBuffs();
            
            // ⭐ НОВОЕ: Восстанавливаем бафф страха темноты если он был активен
            _darknessService.RestoreFearBuff();
            
            _gameLogicHandler.CheckDayStartedStressTriggers();
            _stressLoadService.SyncFromGameState();

            _monitor.Log($"[OnDayStarted] New day initialized: active treatments={_data.StressState.ActiveTreatments.Count}, stressLoad={_stressLoadService.GetCurrentStressLoad()} ({_stressLoadService.GetSeverity()})", LogLevel.Info);
        }

        private void OnDayEnding(object? s, DayEndingEventArgs e)
        {
            _gameLogicHandler.CheckDayEndingQuestCompletion();
            _gameLogicHandler.CheckLateSleepPattern();  // ⭐ НОВОЕ: Отслеживание позднего сна
            _stressLoadService.SyncFromGameState();
            _stressLoadService.DecayStress(GetEndOfDayStressDecay());
            _stressLoadService.NormalizeRecoveryOffsetAtDayEnd();
            SaveData();
        }

        private static int GetEndOfDayStressDecay() =>
            Game1.timeOfDay switch
            {
                _ when Game1.player.hasBuff(BuffIds.NoSleep) => 0,
                _ => 5,
            };

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
            SaveDataHelper.WriteSaveData(_data);
        }
    }
}

using System.Text;
using HarveyStressMeter.Handlers;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP save/reload for mod SaveData persistence tests (EnableStressMcp only).</summary>
    internal static class McpSaveTools
    {
        public static string SaveGame(SaveData data, StressLoadService stressLoadService, IMonitor monitor)
        {
            if (!Context.IsWorldReady)
                return "Error: load a save first.";

            var offsetBefore = stressLoadService.GetStressRecoveryOffset();
            var loadBefore = stressLoadService.GetCurrentStressLoad();
            var treatmentsBefore = data.StressState.ActiveTreatments.Count;

            SaveDataHelper.WriteSaveData(data);

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"saveKey: {SaveDataHelper.SaveKey}");
            sb.AppendLine($"StressRecoveryOffset: {offsetBefore}");
            sb.AppendLine($"CurrentStressLoad: {loadBefore}");
            sb.AppendLine($"ActiveTreatments: {treatmentsBefore}");
            sb.AppendLine($"RescueTriggeredToday: {data.HarveyFlashbackRescue?.HarveyRescueTriggeredToday ?? false}");
            sb.AppendLine("warning: writes mod SaveData to slot CustomData (same path as day-end save). Full SDV save file not written.");
            return sb.ToString().TrimEnd();
        }

        public static string ReloadSave(
            SaveData data,
            StateService stateService,
            DarknessService darknessService,
            StressLoadService stressLoadService,
            GameLogicHandler gameLogicHandler,
            IMonitor monitor)
        {
            if (!Context.IsWorldReady)
                return "Error: load a save first.";

            var loaded = SaveDataHelper.ReadSaveData(monitor);
            if (loaded == null)
                return "Error: no mod save data in current slot. Run mcp_save_game first.";

            var offsetBefore = stressLoadService.GetStressRecoveryOffset();
            var loadBefore = stressLoadService.GetCurrentStressLoad();
            var treatmentsBefore = data.StressState.ActiveTreatments.Count;

            SaveDataHelper.CopySaveDataIntoExistingInstance(data, loaded);
            stateService.MigrateOldData();
            stateService.SyncWithGame();
            gameLogicHandler.ClearStressDialoguePending();
            stateService.RestoreAllActiveBuffs();
            darknessService.SyncDarknessState("McpReloadSave");
            stressLoadService.SyncFromGameState();

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine("[before]");
            sb.AppendLine($"StressRecoveryOffset: {offsetBefore}");
            sb.AppendLine($"CurrentStressLoad: {loadBefore}");
            sb.AppendLine($"ActiveTreatments: {treatmentsBefore}");
            sb.AppendLine("[after]");
            sb.AppendLine($"StressRecoveryOffset: {stressLoadService.GetStressRecoveryOffset()}");
            sb.AppendLine($"CurrentStressLoad: {stressLoadService.GetCurrentStressLoad()}");
            sb.AppendLine($"ActiveTreatments: {data.StressState.ActiveTreatments.Count}");
            sb.AppendLine($"RescueTriggeredToday: {data.HarveyFlashbackRescue?.HarveyRescueTriggeredToday ?? false}");
            sb.AppendLine("warning: reloads mod SaveData only (simulates SaveLoaded path), not a full game restart.");
            return sb.ToString().TrimEnd();
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Разделение legacy buffStressDarkness и новой системы уровней.</summary>
    public static class DarknessLegacyHelper
    {
        public static readonly string[] LevelBuffIds =
        {
            BuffIds.DarknessLevel1,
            BuffIds.DarknessLevel2,
            BuffIds.DarknessLevel3,
        };

        public const int Step1EveningsRequired = 3;
        public const int Step1MinutesPerEvening = 5;

        /// <summary>
        /// Уровневая система: FearLevel, терапия/излечение в save, или активный level-buff в игре.
        /// </summary>
        public static bool UsesLevelSystem(SaveData data, StateService stateService)
        {
            if (data.Darkness.FearLevel > 0)
                return true;

            if (data.Darkness.IsTherapyActive || data.Darkness.IsCured)
                return true;

            return HasAnyLevelBuffInGame(stateService);
        }

        public static bool HasAnyLevelBuffInGame(StateService stateService)
            => GetActiveLevelBuffId(stateService) != null;

        /// <summary>Legacy или level-buff страха темноты в игре.</summary>
        public static bool HasAnyDarknessStressBuffInGame(StateService stateService)
            => HasAnyLevelBuffInGame(stateService)
               || stateService.HasBuffInGame(BuffIds.Darkness);

        public static bool ShouldSkipLegacyDebuffSelector(SaveData data, StateService stateService, string buffId)
            => buffId == BuffIds.Darkness && UsesLevelSystem(data, stateService);

        /// <summary>Страх темноты по уровням: нужен Харви, терапия ещё не начата.</summary>
        public static bool NeedsHarveyDarknessTherapy(SaveData data, StateService stateService)
            => UsesLevelSystem(data, stateService)
               && !data.Darkness.IsTherapyActive
               && !data.Darkness.IsCured
               && data.Darkness.FearLevel > 0
               && HasAnyDarknessStressBuffInGame(stateService);

        /// <summary>Level-buff без legacy ActiveTreatment (отдельная система DarknessProgress).</summary>
        public static bool IsUntreatedLevelDarkness(SaveData data, StateService stateService, string buffId)
        {
            if (!IsDarknessLevelBuff(buffId))
                return false;

            if (!stateService.HasBuffInGame(buffId))
                return false;

            return NeedsHarveyDarknessTherapy(data, stateService);
        }

        public static bool IsDarknessLevelBuff(string? buffId) =>
            buffId is BuffIds.DarknessLevel1 or BuffIds.DarknessLevel2 or BuffIds.DarknessLevel3;

        /// <summary>Нельзя создавать ActiveTreatment / ApplyStressBuff для level-buff.</summary>
        public static bool BlocksLegacyTreatmentPipeline(string? buffId)
            => IsDarknessLevelBuff(buffId);

        /// <summary>Активный level-buff страха темноты (приоритет: 3 → 2 → 1).</summary>
        public static string? GetActiveLevelBuffId(StateService stateService)
        {
            if (stateService.HasBuffInGame(BuffIds.DarknessLevel3))
                return BuffIds.DarknessLevel3;

            if (stateService.HasBuffInGame(BuffIds.DarknessLevel2))
                return BuffIds.DarknessLevel2;

            if (stateService.HasBuffInGame(BuffIds.DarknessLevel1))
                return BuffIds.DarknessLevel1;

            return null;
        }

        /// <summary>Level-buff → канонический buffId для legacy StartTreatment / диалогов CP.</summary>
        public static string ResolveTreatmentBuffId(string buffId) =>
            IsDarknessLevelBuff(buffId) ? BuffIds.Darkness : buffId;

        public static string? GetStepQuestIdForStage(int therapyStage) => therapyStage switch
        {
            1 => QuestIds.DarknessStep1,
            2 => QuestIds.DarknessStep2,
            3 => QuestIds.DarknessStep3,
            _ => null,
        };

        public static bool HasStepQuestInJournal(int therapyStage)
        {
            var questId = GetStepQuestIdForStage(therapyStage);
            return questId != null && Game1.player.hasQuest(questId);
        }

        /// <summary>Текст currentObjective step-квеста в журнале (null если квеста нет).</summary>
        public static string? GetStepQuestCurrentObjective(int therapyStage)
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return null;

            var questId = GetStepQuestIdForStage(therapyStage);
            if (questId == null)
                return null;

            var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == questId);
            return quest?.currentObjective;
        }

        /// <summary>Удаляет ошибочные ActiveTreatment для level-buff (отдельная система).</summary>
        public static int RemoveErroneousLevelDarknessTreatments(SaveData data, IMonitor? monitor = null)
        {
            int removed = 0;
            foreach (var buffId in LevelBuffIds)
            {
                foreach (var treatment in data.StressState.GetActiveTreatmentsByBuff(buffId).ToList())
                {
                    if (data.StressState.RemoveTreatment(treatment.TreatmentKey))
                    {
                        removed++;
                        monitor?.Log(
                            $"[StressSync] Removed TreatmentState {treatment.TreatmentKey}: reason=ErroneousLevelDarknessTreatment, buffId={buffId}",
                            LogLevel.Info);
                    }
                }
            }

            return removed;
        }

        public static void AppendDarknessProgressSnapshot(StringBuilder sb, DarknessProgress d)
        {
            sb.AppendLine($"  fearLevel={d.FearLevel} therapy={d.IsTherapyActive} stage={d.TherapyStage} cured={d.IsCured}");
            sb.AppendLine(
                $"  step1 evenings={d.SafeDarknessEveningsCompleted}/{Step1EveningsRequired} today={d.SafeDarknessProgressToday}/{Step1MinutesPerEvening}");
            sb.AppendLine($"  step2 zones=[{string.Join(", ", d.SafeZonesVisited)}] step3 mountainSec={d.MountainNightSeconds}/120");
            if (d.IsTherapyActive && d.TherapyStage >= 1)
            {
                var questId = GetStepQuestIdForStage(d.TherapyStage);
                sb.AppendLine($"  stepQuest={questId ?? "?"} inJournal={HasStepQuestInJournal(d.TherapyStage)}");
            }
        }
    }
}

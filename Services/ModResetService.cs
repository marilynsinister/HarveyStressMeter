using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StardewModdingAPI;
using StardewValley;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;

namespace HarveyStressMeter.Services
{
    public sealed class ModResetResult
    {
        public int RemovedBuffs { get; init; }
        public int RemovedQuests { get; init; }
        public int RemovedTopics { get; init; }
    }

    /// <summary>
    /// Полный сброс игрового и mod-save состояния HarveyStressMeter.
    /// </summary>
    public class ModResetService
    {
        private readonly IModHelper _helper;
        private readonly SaveData _data;
        private readonly BuffService _buffService;
        private readonly StressDialogueService _stressDialogueService;

        private static readonly string[] KnownBuffIds = typeof(BuffIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        public ModResetService(
            IModHelper helper,
            SaveData data,
            BuffService buffService,
            StressDialogueService stressDialogueService)
        {
            _helper = helper;
            _data = data;
            _buffService = buffService;
            _stressDialogueService = stressDialogueService;
        }

        public ModResetResult ResetAll()
        {
            int removedBuffs = RemoveModBuffs();
            int removedQuests = RemoveModQuests();
            int removedTopics = RemoveModTopics();

            SaveDataHelper.ResetSaveDataInPlace(_data);
            _stressDialogueService.ClearPendingTreatment();
            _helper.Data.WriteSaveData(SaveDataHelper.SaveKey, _data);

            return new ModResetResult
            {
                RemovedBuffs = removedBuffs,
                RemovedQuests = removedQuests,
                RemovedTopics = removedTopics
            };
        }

        private int RemoveModBuffs()
        {
            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var buffId in KnownBuffIds)
            {
                if (_buffService.HasBuff(buffId))
                {
                    _buffService.RemoveBuff(buffId);
                    removed.Add(buffId);
                }
            }

            foreach (var buffId in Game1.player.buffs.AppliedBuffs.Keys.ToList())
            {
                if (!IsModBuffId(buffId) || removed.Contains(buffId))
                    continue;

                _buffService.RemoveBuff(buffId);
                removed.Add(buffId);
            }

            return removed.Count;
        }

        private static int RemoveModQuests()
        {
            var questIds = Game1.player.questLog
                .Select(q => q.id.Value)
                .Where(id => !string.IsNullOrEmpty(id) && id.StartsWith("HarveyMod_", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var questId in questIds)
                Game1.player.removeQuest(questId);

            return questIds.Count;
        }

        private static int RemoveModTopics()
        {
            var topicsToRemove = Game1.player.activeDialogueEvents.Keys
                .Where(IsModTopic)
                .ToList();

            foreach (var topic in topicsToRemove)
                Game1.player.activeDialogueEvents.Remove(topic);

            return topicsToRemove.Count;
        }

        private static bool IsModBuffId(string buffId)
        {
            return buffId.StartsWith("buffStress", StringComparison.OrdinalIgnoreCase)
                || buffId.StartsWith("buffDarkness", StringComparison.OrdinalIgnoreCase)
                || buffId.StartsWith("buffResting", StringComparison.OrdinalIgnoreCase)
                || buffId.StartsWith("buffOverwork", StringComparison.OrdinalIgnoreCase)
                || buffId.StartsWith("buffLight", StringComparison.OrdinalIgnoreCase)
                || buffId.StartsWith("buffCalming", StringComparison.OrdinalIgnoreCase)
                || buffId.StartsWith("buffDim", StringComparison.OrdinalIgnoreCase)
                || buffId.StartsWith("buffHarvey", StringComparison.OrdinalIgnoreCase)
                || buffId.StartsWith("HarveyStress.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsModTopic(string topicId)
        {
            return topicId.StartsWith("topicStress", StringComparison.OrdinalIgnoreCase)
                || topicId.StartsWith("topicOverworkBreak", StringComparison.OrdinalIgnoreCase)
                || topicId.StartsWith("topicDarkness", StringComparison.OrdinalIgnoreCase)
                || topicId.StartsWith("topicHarvey", StringComparison.OrdinalIgnoreCase)
                || topicId.StartsWith("HarveyMod_", StringComparison.OrdinalIgnoreCase)
                || topicId == TopicIds.SpokeToday
                || topicId == TopicIds.AteToday;
        }
    }
}

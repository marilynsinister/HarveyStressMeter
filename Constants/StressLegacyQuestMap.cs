using System;
using System.Collections.Generic;

namespace HarveyStressMeter.Constants;

/// <summary>Legacy stress buff ↔ quest ↔ отображаемое имя для плана Харви.</summary>
public static class StressLegacyQuestMap
{
    private static readonly Dictionary<string, string> BuffToQuest = new(StringComparer.OrdinalIgnoreCase)
    {
        [BuffIds.Thunder] = QuestIds.Thunder,
        [BuffIds.Darkness] = QuestIds.Darkness,
        [BuffIds.Lonely] = QuestIds.Lonely,
        [BuffIds.Overwork] = QuestIds.Overwork,
        [BuffIds.Hunger] = QuestIds.Hunger,
        [BuffIds.TooCold] = QuestIds.TooCold,
        [BuffIds.Social] = QuestIds.Social,
        [BuffIds.NoSleep] = QuestIds.NoSleep,
        [BuffIds.Tired] = QuestIds.Tired,
    };

    private static readonly Dictionary<string, string> QuestToBuff =
        BuffToQuest.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> BuffDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [BuffIds.Tired] = "Усталость",
        [BuffIds.Lonely] = "Одиночество",
        [BuffIds.Thunder] = "Пугающая гроза",
        [BuffIds.Hunger] = "Слабость от голода",
        [BuffIds.Overwork] = "Переработка",
        [BuffIds.NoSleep] = "Недосып",
        [BuffIds.TooCold] = "Переохлаждение",
        [BuffIds.Darkness] = "Вечерний страх",
        [BuffIds.Social] = "Социальная тревожность",
    };

    public static bool TryGetQuestId(string buffId, out string questId)
        => BuffToQuest.TryGetValue(buffId, out questId!);

    public static string? TryGetBuffId(string questId)
        => QuestToBuff.GetValueOrDefault(questId);

    public static bool IsLegacyRecoveryQuest(string questId)
        => QuestToBuff.ContainsKey(questId);

    public static string GetDisplayName(string? buffId, string? questId = null)
    {
        if (!string.IsNullOrEmpty(buffId) && BuffDisplayNames.TryGetValue(buffId, out var byBuff))
            return byBuff;

        string? resolvedBuff = buffId ?? TryGetBuffId(questId ?? "");
        if (!string.IsNullOrEmpty(resolvedBuff) && BuffDisplayNames.TryGetValue(resolvedBuff, out var name))
            return name;

        return questId ?? buffId ?? "Назначение стресса";
    }
}

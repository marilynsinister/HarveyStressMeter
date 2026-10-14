using System.Collections.Generic;
using HarmonyLib;
using StardewValley.Quests;

namespace HarveyStressMeter.Patches
{
    /// <summary>
    /// В 1.6 геттер <see cref="Quest.currentObjective"/> у квестов из Data/Quests при каждом чтении
    /// перезаписывает цель текстом из данных, а описание перечитывается после загрузки сейва.
    /// Поэтому прогресс из C# («Вечера дома: 1/3») в журнал не попадал. Храним свои тексты здесь
    /// и подставляем их поверх данных.
    /// </summary>
    internal static class QuestTextPatch
    {
        private static readonly Dictionary<string, string> Objectives = new();
        private static readonly Dictionary<string, string> Descriptions = new();

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.PropertyGetter(typeof(Quest), nameof(Quest.currentObjective)),
                postfix: new HarmonyMethod(typeof(QuestTextPatch), nameof(ObjectivePostfix)));
            harmony.Patch(
                original: AccessTools.PropertyGetter(typeof(Quest), nameof(Quest.questDescription)),
                postfix: new HarmonyMethod(typeof(QuestTextPatch), nameof(DescriptionPostfix)));
        }

        /// <summary>Растёт при каждом сбросе — чтобы кэши «уже записано» знали, что тексты пропали.</summary>
        public static int Generation { get; private set; }

        public static void SetObjective(string questId, string text) => Objectives[questId] = text;

        public static void SetDescription(string questId, string text) => Descriptions[questId] = text;

        public static void Clear(string questId)
        {
            Objectives.Remove(questId);
            Descriptions.Remove(questId);
            Generation++;
        }

        /// <summary>Новый сейв / выход в меню — тексты прошлой игры не нужны.</summary>
        public static void ClearAll()
        {
            Objectives.Clear();
            Descriptions.Clear();
            Generation++;
        }

        private static void ObjectivePostfix(Quest __instance, ref string __result)
        {
            if (__instance.id.Value is { } id && Objectives.TryGetValue(id, out var text))
                __result = text;
        }

        private static void DescriptionPostfix(Quest __instance, ref string __result)
        {
            if (__instance.id.Value is { } id && Descriptions.TryGetValue(id, out var text))
                __result = text;
        }
    }
}

using System;
using HarveyStressMeter.Models;
using StardewModdingAPI;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Подмножество API Generic Mod Config Menu (spacechase0.GenericModConfigMenu).</summary>
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

        void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);

        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);

        void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue, Func<string> name, Func<string>? tooltip = null, float? min = null, float? max = null, float? interval = null, Func<float, string>? formatValue = null, string? fieldId = null);

        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string>? tooltip = null, string[]? allowedValues = null, Func<string, string>? formatAllowedValue = null, string? fieldId = null);
    }

    /// <summary>Меню настроек стресса в GMCM (необязательная зависимость). Меняет общий экземпляр ModConfig.</summary>
    internal static class GenericModConfigMenuIntegration
    {
        public static void Register(IModHelper helper, IManifest manifest, ModConfig config)
        {
            var gmcm = helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm == null)
                return;

            gmcm.Register(
                manifest,
                reset: () => ResetToDefaults(config),
                save: () => helper.WriteConfig(config));

            gmcm.AddSectionTitle(manifest, () => "Сложность");
            gmcm.AddTextOption(
                manifest,
                () => config.GameplayMode.ToString(),
                value => config.GameplayMode = Enum.Parse<StressGameplayMode>(value),
                () => "Влияние стресса",
                () => "Сюжет: без штрафов, только реплики и HUD.\nБаланс: умеренные штрафы.\nВыживание: штрафы примерно на 15% сильнее.",
                Enum.GetNames<StressGameplayMode>(),
                FormatGameplayMode);
            gmcm.AddNumberOption(
                manifest,
                () => config.StressDecayPerHour,
                value => config.StressDecayPerHour = value,
                () => "Естественный спад за час",
                min: 0f, max: 10f, interval: 0.5f);

            gmcm.AddSectionTitle(manifest, () => "Как снять стресс");
            gmcm.AddBoolOption(
                manifest,
                () => config.EnableComfortActivities,
                value => config.EnableComfortActivities = value,
                () => "Занятия для души",
                () => "Рыбалка, сбор, животные, питомец, закат, салун и аркада понемногу снижают стресс.");
            gmcm.AddNumberOption(
                manifest,
                () => config.ComfortDailyReliefCap,
                value => config.ComfortDailyReliefCap = (int)value,
                () => "Лимит занятий за день",
                () => "Сколько стресса максимум можно снять занятиями за один день.",
                min: 0f, max: 60f, interval: 5f);
            gmcm.AddBoolOption(
                manifest,
                () => config.EnableHarveySafePersonAura,
                value => config.EnableHarveySafePersonAura = value,
                () => "Рядом с Харви легче");
            gmcm.AddBoolOption(
                manifest,
                () => config.EnableHarveyFlashbackRescue,
                value => config.EnableHarveyFlashbackRescue = value,
                () => "Харви ищет в лесу во время грозы");

            gmcm.AddSectionTitle(manifest, () => "Интерфейс");
            gmcm.AddBoolOption(
                manifest,
                () => config.ShowStressMeter,
                value => config.ShowStressMeter = value,
                () => "Шкала стресса");
            gmcm.AddBoolOption(
                manifest,
                () => config.ShowOnlyWhenStressed,
                value => config.ShowOnlyWhenStressed = value,
                () => "Шкала только при стрессе");
            gmcm.AddBoolOption(
                manifest,
                () => config.EnableHudMessages,
                value => config.EnableHudMessages = value,
                () => "HUD-сообщения");
        }

        private static string FormatGameplayMode(string value) => value switch
        {
            nameof(StressGameplayMode.StoryFocus) => "Сюжет",
            nameof(StressGameplayMode.Balanced) => "Баланс",
            nameof(StressGameplayMode.Survival) => "Выживание",
            _ => value,
        };

        private static void ResetToDefaults(ModConfig config)
        {
            var defaults = new ModConfig();
            config.GameplayMode = defaults.GameplayMode;
            config.StressDecayPerHour = defaults.StressDecayPerHour;
            config.EnableComfortActivities = defaults.EnableComfortActivities;
            config.ComfortDailyReliefCap = defaults.ComfortDailyReliefCap;
            config.EnableHarveySafePersonAura = defaults.EnableHarveySafePersonAura;
            config.EnableHarveyFlashbackRescue = defaults.EnableHarveyFlashbackRescue;
            config.ShowStressMeter = defaults.ShowStressMeter;
            config.ShowOnlyWhenStressed = defaults.ShowOnlyWhenStressed;
            config.EnableHudMessages = defaults.EnableHudMessages;
        }
    }
}

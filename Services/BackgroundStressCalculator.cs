using System;
using System.Collections.Generic;
using System.Linq;
using HarveyStressMeter.Constants;
using StardewValley;
using StardewValley.GameData.Buffs;

namespace HarveyStressMeter.Services
{
    /// <summary>Одна строка разбивки шкалы для подсказки HUD.</summary>
    public sealed record StressContribution(string Label, int Weight);

    /// <summary>
    /// «Фоновая» нагрузка поверх причин стресса: травмы и болезни (Injury), осложнения, мелкие неприятности,
    /// тяжёлые стресс-эпизоды, любые чужие дебаффы, низкое здоровье и поздняя ночь.
    /// Забота Харви, лечебные баффы и еда — облегчение (отрицательный вклад).
    /// Тело и психика ограничены отдельно, чтобы одна сломанная нога не давала «Критический».
    /// </summary>
    public sealed class BackgroundStressCalculator
    {
        public const int MaxBodyLoad = 40;
        public const int MaxMindLoad = 40;
        public const int MaxRelief = 15;
        private const int UnknownDebuffWeight = 4;
        private const int CareReliefWeight = 3;

        /// <summary>Травмы: вес острой фазы. Фазовые баффы HarveyMod_&lt;X&gt;_&lt;Фаза&gt; — та же травма, вес по фазе.</summary>
        private static readonly Dictionary<string, int> InjuryFamilyWeights = new(StringComparer.Ordinal)
        {
            ["Hurt"] = 5,
            ["SprainedAnkle"] = 7,
            ["BruisedRibs"] = 7,
            ["BackStrain"] = 8,
            ["Cold"] = 8,
            ["AlcoholPoisoning"] = 8,
            ["DeepCuts"] = 10,
            ["TornMuscles"] = 10,
            ["SurgicalWound"] = 10,
            ["BurnWounds"] = 12,
            ["ShrapnelWounds"] = 14,
            ["BadlyHurt"] = 14,
            ["FracturedBone"] = 15,
            ["Concussion"] = 15,
            ["InfectedWound"] = 16,
        };

        private static readonly Dictionary<string, string> FamilyAliases = new(StringComparer.Ordinal)
        {
            ["Shrapnel"] = "ShrapnelWounds",
        };

        /// <summary>Чем дальше лечение, тем меньше тревоги: острая фаза 100%, заживление 70%, восстановление 40%.</summary>
        private static float GetPhaseFactor(string phase) => phase switch
        {
            "Healing" or "Cast" or "Rest" or "Treatment" => 0.7f,
            "Recovery" or "Rehab" or "Limited" => 0.4f,
            _ => 1f,
        };

        /// <summary>Явные веса: осложнения, неприятности, ограничения, тяжёлые эпизоды стресса.</summary>
        private static readonly Dictionary<string, int> BodyWeights = new(StringComparer.Ordinal)
        {
            ["HarveyMod_WetBandage"] = 4,
            ["HarveyMod_DirtyWound"] = 6,
            ["HarveyMod_Neglect"] = 6,
            ["HarveyMod_PainFlare"] = 8,
            ["buffPainFlare"] = 8,
            ["HarveyMod_AllergicRash"] = 4,
            ["HarveyMod_WetStitches"] = 4,
            ["HarveyMod_ImpairedMobility"] = 6,
            ["HarveyMod_MuscleAtrophy"] = 8,
            ["HarveyMod_BreathingDifficulty"] = 10,
            ["HarveyMod_Sepsis"] = 18,
            ["HarveyMod_Splinter"] = 3,
            ["HarveyMod_BeeSting"] = 3,
            ["HarveyMod_Sunstroke"] = 5,
            ["HarveyMod_Frostbite"] = 4,
            ["buffFarmerExhausted"] = 8,
            ["buffSleepy"] = 4,
            ["buffTooCold"] = 4,
            ["HarveyMod_MineForbidden"] = 3,
            ["HarveyMod_MineRestricted"] = 2,
            ["buffHarveyRehab"] = 3,
            ["HarveyMod_DoctorVisitNeeded"] = 3,
        };

        private static readonly Dictionary<string, int> MindWeights = new(StringComparer.Ordinal)
        {
            [BuffIds.BadDream] = 10,
            [BuffIds.Criticism] = 10,
            [BuffIds.MentalFatigue] = 12,
            [BuffIds.FreezeResponse] = 12,
            [BuffIds.AnxietyWave] = 15,
            [BuffIds.Numbness] = 15,
            [BuffIds.ShadowParanoia] = 15,
            [BuffIds.Isolation] = 15,
            ["buffSleepDeprivation"] = 15,
            ["buffStressExhaustion"] = 15,
            ["buffStressPhysicalDepletion"] = 15,
            ["buffStressNightTerror"] = 15,
            [BuffIds.Panic] = 20,
            ["buffStressBurnout"] = 20,
            [BuffIds.Breakdown] = 25,
            [BuffIds.Collapse] = 25,
            [BuffIds.Despair] = 25,
            ["buffStressMentalBreakdown"] = 25,
            ["buffStressPanicCollapse"] = 25,
            ["buffStressDespairCollapse"] = 25,
            ["buffStressPanicBreakdown"] = 25,
            ["buffStressParanoidCollapse"] = 25,
            ["buffStressCritical"] = 30,
            ["buffStressCriticalExhaustion"] = 30,
        };

        /// <summary>Сильное облегчение: седация, госпитализация, полный курс у Харви.</summary>
        private static readonly Dictionary<string, int> StrongRelief = new(StringComparer.Ordinal)
        {
            ["buffForcedSedation"] = 8,
            ["HarveyMod_Hospitalized"] = 5,
            [BuffIds.Immunity] = 5,
            [BuffIds.LightAndSafe] = 5,
            [BuffIds.CalmingAtHospital] = 5,
            ["buffCalmTeaEffect"] = 5,
            ["HarveyMod_WarmTea"] = 4,
        };

        /// <summary>Не учитываем: уже считаются причинами стресса, служебные или сам бафф шкалы.</summary>
        private static readonly HashSet<string> Ignored = new(StressCauses.BuffToCause.Keys, StringComparer.Ordinal)
        {
            BuffIds.StressLoadTier,
            BuffIds.ClearMind,
            BuffIds.CareAura,
            "buffEmergencySupervision",
        };

        private Dictionary<string, BuffData>? _buffData;

        public IReadOnlyList<StressContribution> Contributions { get; private set; } = Array.Empty<StressContribution>();

        /// <summary>Сбросить кэш Data/Buffs (после перезагрузки ассетов).</summary>
        public void InvalidateData() => _buffData = null;

        /// <summary>Чистый вклад (может быть отрицательным) и разбивка в <see cref="Contributions"/>.</summary>
        public int Compute(bool gotoroActive)
        {
            var player = Game1.player;
            if (player == null)
            {
                Contributions = Array.Empty<StressContribution>();
                return 0;
            }

            _buffData ??= DataLoader.Buffs(Game1.content);

            var body = new List<StressContribution>();
            var mind = new List<StressContribution>();
            var relief = new List<StressContribution>();
            var injuryFamilies = new Dictionary<string, StressContribution>(StringComparer.Ordinal);

            foreach (var (id, buff) in player.buffs.AppliedBuffs)
            {
                if (Ignored.Contains(id) || (gotoroActive && id == BuffIds.Panic))
                    continue;

                string label = ResolveLabel(id, buff);
                _buffData.TryGetValue(id, out var data);

                if (TryGetInjuryWeight(id, out string family, out int injuryWeight))
                {
                    if (data?.IsDebuff == false)
                        continue;

                    if (!injuryFamilies.TryGetValue(family, out var existing) || existing.Weight < injuryWeight)
                        injuryFamilies[family] = new StressContribution(label, injuryWeight);
                    continue;
                }

                if (MindWeights.TryGetValue(id, out int mindWeight))
                {
                    mind.Add(new StressContribution(label, mindWeight));
                    continue;
                }

                if (BodyWeights.TryGetValue(id, out int bodyWeight))
                {
                    body.Add(new StressContribution(label, bodyWeight));
                    continue;
                }

                if (StrongRelief.TryGetValue(id, out int strong))
                {
                    relief.Add(new StressContribution(label, -strong));
                    continue;
                }

                if (id is "food" or "drink")
                {
                    relief.Add(new StressContribution(id == "food" ? "Вкусная еда" : "Напиток", -CareReliefWeight));
                    continue;
                }

                bool isDebuff = data?.IsDebuff ?? buff.effects.HasAnyValue() && IsMostlyNegative(buff);
                if (isDebuff)
                {
                    // Свои стресс-баффы без веса не дублируем (их учитывают причины/эпизоды).
                    if (!IsOwnStressBuff(id))
                        body.Add(new StressContribution(label, UnknownDebuffWeight));
                    continue;
                }

                if (IsHarveyCareBuff(id))
                    relief.Add(new StressContribution(label, -CareReliefWeight));
            }

            body.AddRange(injuryFamilies.Values);
            AddSituational(body, player);

            int bodySum = Math.Min(MaxBodyLoad, body.Sum(c => c.Weight));
            int mindSum = Math.Min(MaxMindLoad, mind.Sum(c => c.Weight));
            int reliefSum = Math.Min(MaxRelief, -relief.Sum(c => c.Weight));

            Contributions = body.Concat(mind).Concat(relief)
                .OrderByDescending(c => Math.Abs(c.Weight))
                .ToList();

            return bodySum + mindSum - reliefSum;
        }

        private static void AddSituational(List<StressContribution> body, Farmer player)
        {
            if (player.maxHealth > 0 && player.health <= player.maxHealth * 0.3f)
                body.Add(new StressContribution("Мало здоровья", 8));

            if (Game1.timeOfDay >= 2500)
                body.Add(new StressContribution("Глубокая ночь", 8));
            else if (Game1.timeOfDay >= 2400)
                body.Add(new StressContribution("Поздний час", 4));
        }

        private static bool TryGetInjuryWeight(string id, out string family, out int weight)
        {
            family = "";
            weight = 0;
            string phase = "";

            if (id.StartsWith("buff", StringComparison.Ordinal))
            {
                family = id.Substring(4);
            }
            else if (id.StartsWith("HarveyMod_", StringComparison.Ordinal))
            {
                string rest = id.Substring("HarveyMod_".Length);
                int sep = rest.LastIndexOf('_');
                if (sep <= 0)
                    return false;

                family = rest[..sep];
                phase = rest[(sep + 1)..];
            }
            else
            {
                return false;
            }

            if (FamilyAliases.TryGetValue(family, out var alias))
                family = alias;

            if (!InjuryFamilyWeights.TryGetValue(family, out int baseWeight))
                return false;

            weight = Math.Max(1, (int)MathF.Round(baseWeight * GetPhaseFactor(phase)));
            return true;
        }

        private static bool IsHarveyCareBuff(string id)
            => id.StartsWith("buffHarvey", StringComparison.Ordinal)
               || id.StartsWith("buffStressRecovery", StringComparison.Ordinal)
               || id is "HarveyMod_SelfCare" or "HarveyMod_CleanBandage" or "HarveyMod_Resilience"
                   or "buffTeracitin" or "buffAntibioticsTreatment" or "buffPostSurgicalCare" or "buffTraumaHealing"
                   or "buffStrictSupervision" or "buffConstantSupervision"
                   or BuffIds.RestingAtHome or BuffIds.OverworkBreak or BuffIds.DarknessOvercome or BuffIds.HarveyLantern;

        private static bool IsOwnStressBuff(string id)
            => id.StartsWith("buffStress", StringComparison.Ordinal) || id.StartsWith("buffDarkness", StringComparison.Ordinal);

        private static bool IsMostlyNegative(Buff buff)
            => buff.effects.MaxStamina.Value + buff.effects.Speed.Value * 10 + buff.effects.Defense.Value * 5
               + buff.effects.LuckLevel.Value * 5 < 0;

        private static string ResolveLabel(string id, Buff buff)
        {
            string name = buff.displayName;
            if (string.IsNullOrWhiteSpace(name))
                name = id;

            // «Перелом: острая фаза (фаза 1/3)» → «Перелом» — коротко для подсказки.
            int colon = name.IndexOf(':');
            return colon > 0 ? name[..colon] : name;
        }
    }
}

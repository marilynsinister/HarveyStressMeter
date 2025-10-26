using System;
using System.Collections.Generic;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.GameData.Buffs;
using HarveyStressMeter.Constants;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Сервис для управления баффами
    /// </summary>
    public class BuffService
    {
        public void ApplyBuff(string buffId, string displayName, BuffEffects effects, int durationMs)
        {
            if (Game1.player.hasBuff(buffId))
                Game1.player.buffs.Remove(buffId);

            var buff = new Buff(buffId, displayName, iconTexture: null, iconSheetIndex: 0, 
                duration: durationMs, effects: effects)
            { visible = true };
            
            Game1.player.applyBuff(buff);
        }

        public void ApplyBuffFromData(string buffId)
        {
            var dict = Game1.content.Load<Dictionary<string, BuffData>>("Data/Buffs");

            if (!dict.TryGetValue(buffId, out var data))
                return;

            int duration = Buff.ENDLESS;

            if (Game1.player.hasBuff(buffId))
                Game1.player.buffs.Remove(buffId);

            var effects = data.Effects != null ? ConvertToEffects(data.Effects) : new BuffEffects();

            var buff = new Buff(buffId, data.DisplayName, iconTexture: null, iconSheetIndex: 0, 
                duration: duration, effects: effects)
            { visible = true };

            Game1.player.applyBuff(buff);
        }

        public void RemoveBuff(string buffId)
        {
            if (Game1.player.hasBuff(buffId))
                Game1.player.buffs.Remove(buffId);
        }

        public bool HasBuff(string buffId)
        {
            return Game1.player.hasBuff(buffId);
        }

        private static BuffEffects ConvertToEffects(BuffAttributesData attributes)
        {
            var effects = new BuffEffects();

            if (attributes.MaxStamina != 0) effects.MaxStamina.Add(attributes.MaxStamina);
            if (attributes.Speed != 0) effects.Speed.Add(attributes.Speed);
            if (attributes.Defense != 0) effects.Defense.Add(attributes.Defense);
            if (attributes.Attack != 0) effects.Attack.Add(attributes.Attack);
            if (attributes.Immunity != 0) effects.Immunity.Add(attributes.Immunity);
            if (attributes.LuckLevel != 0) effects.LuckLevel.Add(attributes.LuckLevel);
            if (attributes.MagneticRadius != 0) effects.MagneticRadius.Add(attributes.MagneticRadius);
            if (attributes.FarmingLevel != 0) effects.FarmingLevel.Add(attributes.FarmingLevel);
            if (attributes.FishingLevel != 0) effects.FishingLevel.Add(attributes.FishingLevel);
            if (attributes.MiningLevel != 0) effects.MiningLevel.Add(attributes.MiningLevel);
            if (attributes.ForagingLevel != 0) effects.ForagingLevel.Add(attributes.ForagingLevel);
            if (attributes.CombatLevel != 0) effects.CombatLevel.Add(attributes.CombatLevel);

            return effects;
        }
    }
}


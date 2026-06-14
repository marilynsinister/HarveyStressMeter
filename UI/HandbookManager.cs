using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using HarveyOverhaul.Core.Models;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;

namespace HarveyStressMeter.UI
{
    /// <summary>
    /// Менеджер для справочника Харви
    /// </summary>
    public class HandbookManager
    {
        private readonly Texture2D _iconsTex;
        private readonly Dictionary<string, Rectangle> _iconRects;

        public HandbookManager(Texture2D iconsTex)
        {
            _iconsTex = iconsTex;
            _iconRects = InitializeIconRects();
        }

        private static Dictionary<string, Rectangle> InitializeIconRects()
        {
            return new(StringComparer.OrdinalIgnoreCase)
            {
                ["Overwork"] = new Rectangle(0, 0, 16, 16),
                ["Crowd"] = new Rectangle(16, 0, 16, 16),
                ["Storm"] = new Rectangle(32, 0, 16, 16),
                ["LateNight"] = new Rectangle(48, 0, 16, 16),
                ["Cave"] = new Rectangle(64, 0, 16, 16),
                ["Lonely"] = new Rectangle(80, 0, 16, 16),
                ["Blank"] = new Rectangle(96, 0, 16, 16),

                ["Glass"] = new Rectangle(0, 16, 16, 16),
                ["ThinIce"] = new Rectangle(16, 16, 16, 16),
                ["Clutter"] = new Rectangle(32, 16, 16, 16),
                ["Map"] = new Rectangle(48, 16, 16, 16),
                ["Cold"] = new Rectangle(64, 16, 16, 16),
                ["Insomnia"] = new Rectangle(80, 16, 16, 16),
                ["Numb"] = new Rectangle(96, 16, 16, 16),

                ["Heart"] = new Rectangle(0, 32, 16, 16),
                ["Care"] = new Rectangle(16, 32, 16, 16),
                ["Tea"] = new Rectangle(32, 32, 16, 16),
                ["Fish"] = new Rectangle(48, 32, 16, 16),
                ["Candle"] = new Rectangle(64, 32, 16, 16),
                ["Hands"] = new Rectangle(80, 32, 16, 16),
                ["Shadow"] = new Rectangle(96, 32, 16, 16),
            };
        }

        public HandbookViewModel BuildViewModel(SaveData data)
        {
            var vm = new HandbookViewModel();
            var activeBuffIds = GetActiveBuffIds(data);

            var iconKeyByBuff = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [BuffIds.Overwork] = "Overwork",
                [BuffIds.Lonely] = "Lonely",
                [BuffIds.Thunder] = "Storm",
                [BuffIds.Darkness] = "LateNight",
                [BuffIds.NoSleep] = "Insomnia",
                [BuffIds.Hunger] = "Tea",
                [BuffIds.TooCold] = "Cold",
                [BuffIds.Tired] = "Glass",
                [BuffIds.Social] = "Crowd",
                [BuffIds.CareAura] = "Heart",
            };

            foreach (var state in GetAllStates())
            {
                string iconKey = iconKeyByBuff.TryGetValue(state.Id, out var k) ? k : "Blank";
                Rectangle src = _iconRects[iconKey];

                var cureSummary = state.CureText;
                if (string.Equals(state.Id, BuffIds.Social, StringComparison.OrdinalIgnoreCase))
                {
                    var exposure = data.SocialExposure.SocialExposureToday;
                    var socialActive = activeBuffIds.Contains(BuffIds.Social);
                    var exposureLabel = SocialStressHelper.GetCompactStatusLabel(exposure, socialActive);
                    cureSummary = $"{cureSummary} Сегодня: {exposureLabel} ({exposure}/100).";
                }

                var row = new HandbookRow
                {
                    BuffId = state.Id,
                    Title = state.Title,
                    Effects = state.EffectsText,
                    Causes = state.CauseText,
                    CureSummary = cureSummary,
                    IconSprite = new SpriteView(_iconsTex, src),
                };

                if (activeBuffIds.Contains(state.Id))
                {
                    row.StatusText = "Сейчас";
                    row.StatusColor = "#7f6139";
                    vm.ActiveStates.Add(row);
                }
                else
                {
                    row.StatusText = "Не беспокоит";
                    row.StatusColor = "#6b6b6b";
                }
                
                vm.AllStates.Add(row);
            }

            return vm;
        }

        private static HashSet<string> GetActiveBuffIds(SaveData data)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Используем новую архитектуру для получения активных баффов
            foreach (var treatment in data.StressState.ActiveTreatments.Values)
            {
                if (!treatment.IsCured)
                    set.Add(treatment.BuffId);
            }

            foreach (var buff in Game1.player.buffs.AppliedBuffs.Values)
            {
                if (buff == null) continue;
                var id = buff.id ?? "";
                if (!string.IsNullOrEmpty(id))
                    set.Add(id);
            }

            return set;
        }

        private static IEnumerable<StateInfo> GetAllStates()
        {
            yield return new StateInfo
            {
                Id = BuffIds.Thunder,
                Title = "Пугающая гроза",
                EffectsText = "Защита слабее, чем обычно.",
                CauseText = "Гроза на улице.",
                CureText = "Харви просит побудьте рядом с ним или укройтесь в безопасном месте."
            };

            yield return new StateInfo
            {
                Id = BuffIds.Darkness,
                Title = "Вечерний страх",
                EffectsText = "Защита слабее, чем обычно.",
                CauseText = "Ночь на улице, нарастающий страх.",
                CureText = "Останьтесь дома при свете. Харви просил не проверять себя на прочность."
            };

            yield return new StateInfo
            {
                Id = BuffIds.Lonely,
                Title = "Одиночество",
                EffectsText = "Защита слабее, чем обычно.",
                CauseText = "Целый день ни с кем не говорили.",
                CureText = "Поговорите с жителями. Не обязательно долго — просто дайте миру напомнить, что вы не одна."
            };

            yield return new StateInfo
            {
                Id = BuffIds.Hunger,
                Title = "Слабость от голода",
                EffectsText = "Выносливость заметно ниже.",
                CauseText = "Вы долго не ели.",
                CureText = "Съешьте что-нибудь. Харви заметит, если вы снова пропустите еду."
            };

            yield return new StateInfo
            {
                Id = BuffIds.Overwork,
                Title = "Переутомление",
                EffectsText = "Выносливость заметно ниже.",
                CauseText = "Тяжёлая работа без перерыва.",
                CureText = "Сделайте перерыв. Не «ещё один ряд грядок», а настоящий перерыв."
            };

            yield return new StateInfo
            {
                Id = BuffIds.NoSleep,
                Title = "Недосып",
                EffectsText = "Реакции и защита слабее.",
                CauseText = "Поздний отбой.",
                CureText = "Лечь до полуночи. Харви просил не проверять себя на прочность."
            };

            yield return new StateInfo
            {
                Id = BuffIds.TooCold,
                Title = "Переохлаждение",
                EffectsText = "Защита слабее, чем обычно.",
                CauseText = "Холод или непогода на улице.",
                CureText = "Согрейтесь в тёплом месте."
            };

            yield return new StateInfo
            {
                Id = BuffIds.Tired,
                Title = "Усталость",
                EffectsText = "Лёгкая вялость.",
                CauseText = "Обычная нагрузка без отдыха.",
                CureText = "Короткий отдых дома."
            };

            yield return new StateInfo
            {
                Id = BuffIds.Social,
                Title = "Социальное напряжение",
                EffectsText = "Защита слабее, чем обычно.",
                CauseText = "Слишком много разговоров с малознакомыми за день.",
                CureText = "Харви просит снизить нагрузку и восстановиться."
            };
        }

        private class StateInfo
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string EffectsText { get; set; } = "";
            public string CauseText { get; set; } = "";
            public string CureText { get; set; } = "";
        }
    }
}


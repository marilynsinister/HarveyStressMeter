using HarveyStressMeter.Constants;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Фонарь Харви в руках светит в темноте — как настоящий фонарь, а не «виртуальный».
    /// Свет создаётся, пока фонарь выбран в панели инструментов и вокруг темно; на смене локации
    /// игра очищает источники света, поэтому свет пересоздаётся каждый тик.
    /// </summary>
    public sealed class HarveyLanternLightService
    {
        private const float Radius = 2.5f;

        private string LightId => $"marilynsinister.HarveyStressMeter_Lantern_{Game1.player.UniqueMultiplayerID}";

        public void OnUpdateTicked()
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return;

            var location = Game1.currentLocation;
            bool shine = location != null
                         && Game1.player.ActiveObject?.QualifiedItemId == HarveyItems.Lantern
                         && (Game1.isDarkOut(location) || location is MineShaft || !location.IsOutdoors && location.IsGreenhouse == false && Game1.timeOfDay >= 1800);

            string id = LightId;
            if (!shine)
            {
                Game1.currentLightSources.Remove(id);
                return;
            }

            var position = Game1.player.getStandingPosition() + new Vector2(0, -32);
            if (Game1.currentLightSources.TryGetValue(id, out var light))
            {
                light.position.Value = position;
                return;
            }

            Game1.currentLightSources[id] = new LightSource(
                id, LightSource.lantern, position, Radius, LightSource.LightContext.None, Game1.player.UniqueMultiplayerID, location!.NameOrUniqueName);
        }
    }
}

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using HarveyStressMeter.Models;
using HarveyStressMeter.UI;
// ⭐ ВРЕМЕННО ОТКЛЮЧЕНО: StardewUI
// using StardewUI.Framework;

namespace HarveyStressMeter.Handlers
{
    /// <summary>
    /// Handles UI elements: handbook, HUD messages, menu interactions
    /// Follows Single Responsibility Principle - only UI concerns
    /// </summary>
    public class UIHandler
    {
        private readonly IMonitor _monitor;
        private readonly SaveData _data;
        private readonly IModHelper _helper;
        private Texture2D? _handbookTex;
        private Texture2D? _iconsTex;
        private Rectangle _handbookRect;
        private const int HandbookSize = 44;
        private HandbookManager? _handbookManager;

        public UIHandler(IMonitor monitor, SaveData data, IModHelper helper)
        {
            _monitor = monitor;
            _data = data;
            _helper = helper;
        }

        public void Initialize()
        {
            // ⭐ ВРЕМЕННО ОТКЛЮЧЕНО: StardewUI
            _monitor.Log("Справочник временно отключён (StardewUI закомментирован)", LogLevel.Info);
            return;
            
            /*
            var viewEngine = _helper.ModRegistry.GetApi<IViewEngine>("focustense.StardewUI");
            if (viewEngine == null)
            {
                _monitor.Log("StardewUI не найден. Справочник будет отключён.", LogLevel.Warn);
                return;
            }

            try
            {
                _handbookTex = _helper.ModContent.Load<Texture2D>("assets/sprites/handbook.png");
                _iconsTex = _helper.ModContent.Load<Texture2D>("assets/sprites/stressIcons.png");
            }
            catch (Exception ex)
            {
                _monitor.Log($"Failed to load textures: {ex.Message}", LogLevel.Warn);
                return;
            }

            viewEngine.RegisterViews("Mods/marilynsinister.HarveyStressMeter/Views", "assets/views");
            viewEngine.RegisterSprites("Mods/marilynsinister.HarveyStressMeter/Sprites", "assets/sprites");

            _handbookManager = new HandbookManager(_iconsTex);
            */
        }

        public void HandleRenderedActiveMenu(RenderedActiveMenuEventArgs e)
        {
            if (Game1.activeClickableMenu is not GameMenu gm) return;
            if (!IsInventoryPage(gm)) return;

            if (_handbookTex == null) return;

            float ui = Game1.options.uiScale;
            int size = (int)(HandbookSize * ui);

            var anchor = TryGetTrashCanBounds(gm)
                ?? new Rectangle(
                    gm.xPositionOnScreen + gm.width - (int)(96 * ui),
                    gm.yPositionOnScreen + gm.height - (int)(96 * ui),
                    (int)(64 * ui), (int)(64 * ui));

            int x = anchor.X + (anchor.Width - size) / 2;
            int y = anchor.Bottom + (int)(18 * ui);
            _handbookRect = new Rectangle(x, y, size, size);

            e.SpriteBatch.Draw(_handbookTex, _handbookRect, Color.White);

            if (_handbookRect.Contains(Game1.getMouseX(), Game1.getMouseY()))
                IClickableMenu.drawHoverText(e.SpriteBatch, "Справочник Харви", Game1.smallFont);

            gm.drawMouse(e.SpriteBatch);
        }

        public void HandleButtonPressed(ButtonPressedEventArgs e)
        {
            if (Game1.activeClickableMenu is not GameMenu gm) return;
            if (!IsInventoryPage(gm)) return;

            if (_handbookTex == null) return;

            if (e.Button.IsUseToolButton() || e.Button == SButton.MouseLeft || e.Button == SButton.ControllerA)
            {
                if (_handbookRect.Contains(Game1.getMouseX(), Game1.getMouseY()))
                {
                    OpenHandbook();
                    _helper.Input.Suppress(SButton.MouseLeft);
                }
            }
        }

        public void HandleButtonsChanged(ButtonsChangedEventArgs e)
        {
            if (!Context.IsPlayerFree) return;

            // TODO: Add config for open handbook key
            // if (_config.OpenHandbook.JustPressed())
            // {
            //     OpenHandbook();
            //     Helper.Input.SuppressActiveKeybinds(_config.OpenHandbook);
            // }
        }

        public void OpenHandbook()
        {
            // ⭐ ВРЕМЕННО ОТКЛЮЧЕНО: StardewUI
            _monitor.Log("Справочник временно отключён", LogLevel.Info);
            return;
            
            /*
            if (_handbookManager == null) return;

            var viewEngine = _helper.ModRegistry.GetApi<IViewEngine>("focustense.StardewUI");
            if (viewEngine == null) return;

            var vm = _handbookManager.BuildViewModel(_data);
            Game1.activeClickableMenu = viewEngine.CreateMenuFromAsset(
                $"Mods/marilynsinister.HarveyStressMeter/Views/Handbook", vm);
            */
        }

        private bool IsInventoryPage(GameMenu gm) => gm.currentTab == GameMenu.inventoryTab;

        private Rectangle? TryGetTrashCanBounds(GameMenu gm)
        {
            if (gm.pages[gm.currentTab] is InventoryPage inv)
            {
                var field = _helper.Reflection.GetField<ClickableTextureComponent>(inv, "trashCan", false);
                var trash = field?.GetValue();
                if (trash != null) return trash.bounds;
            }
            return null;
        }
    }
}

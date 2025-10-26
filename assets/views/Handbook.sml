<frame
  layout="900px 620px"
  background={@Mods/StardewUI/Sprites/MenuBackground}
  border={@Mods/StardewUI/Sprites/MenuBorder}
  border-thickness="36,36,40,36"
  horizontal-content-alignment="middle"
  vertical-content-alignment="middle"
  padding="24,20,24,20">
    <banner
      background= {@Mods/StardewUI/Sprites/BannerBackground}
      background-border-thickness="48,0,48,0"
      padding="10,6"
      margin="0,0,8,0"
      text="Справочник Харви" />

  <lane orientation="vertical">
    <!-- Баннер-заголовок -->


    <label font="small" text="Активные состояния" color="#7f6139" margin="0,0,6,0" />

    <!-- Активные -->
    <!-- Полоса активных: горизонтальная прокрутка карточек -->
    <scrollable peeking="32">
      <grid layout="stretch content" item-layout="length: 260">
        <frame *repeat="{ActiveStates}"
          layout="260px 200px"
          background= {@Mods/StardewUI/Sprites/MenuBackground}
          border= {@Mods/StardewUI/Sprites/MenuBorder}
          border-thickness="12,12,12,12"
          padding="8,8,8,8"
          margin="0,0,0,0">

          <lane orientation="vertical">
            <lane orientation="horizontal">
              <image layout="40px 40px" texture="{IconSprite.Texture}" source-rect="{IconSprite.SourceRect}" margin="6,8,0,0" />
              <label font="dialogue" text="{Title}" />
            </lane>

            <label font="small" text="Эффекты: {Effects}"  />
            <label font="small" text="Причины: {Causes}"  />
            <label font="small" text="Лечение: {CureSummary}"  />
            <label font="small" text="{StatusText}" color="{StatusColor}" margin="0,4,0,0" />
            <label font="small" text="Этап лечения: {TreatmentStageText}" color="#6b6b6b" />

          </lane>
        </frame>
        <!-- пробел между карточками (через «пустой» элемент сетки) -->
        <image layout="8px 1px" />
      </grid>
    </scrollable>


    <!-- ВСЕ -->
    <label font="small" text="Все состояния" color="#7f6139" margin="8,0,6,0" />

    <!-- Сетка всех карточек с вертикальной прокруткой -->
    <scrollable peeking="32">
      <!-- 3 колонки с авто-подбором ширины ячейки -->
      <grid layout="stretch content" item-layout="count: 3">

        <frame *repeat="{AllStates}"
          background= {@Mods/StardewUI/Sprites/MenuBackground}
          border= {@Mods/StardewUI/Sprites/MenuBorder}
          border-thickness="12,12,12,12"
          padding="8,8,8,8"
          margin="0,0,8,8">

          <lane orientation="vertical">
            <lane orientation="horizontal">
              <image layout="40px 40px" sprite="{Icon}" margin="0,0,6,0" />
              <label font="dialogue" text="{Title}" />
            </lane>

            <label font="small" text="{Description}"  margin="0,4,0,0" />
            <label font="small" text="Эффекты: {Effects}"  />
            <label font="small" text="Причины: {Causes}"  />
            <label font="small" text="Лечение: {CureSummary}"  />
            <label font="small" text="{StatusText}" color="{StatusColor}" margin="0,4,0,0" />
            <label font="small" text="Этап лечения: {TreatmentStageText}" color="#6b6b6b" />
          </lane>
        </frame>

      </grid>
    </scrollable>

  </lane>
</frame>
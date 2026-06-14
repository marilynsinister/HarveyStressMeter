<frame
  layout="920px 640px"
  background={@Mods/StardewUI/Sprites/MenuBackground}
  border={@Mods/StardewUI/Sprites/MenuBorder}
  border-thickness="36,36,40,36"
  horizontal-content-alignment="middle"
  vertical-content-alignment="middle"
  padding="24,20,24,20">
  <lane orientation="vertical">
    <banner
      background={@Mods/StardewUI/Sprites/BannerBackground}
      background-border-thickness="48,0,48,0"
      padding="10,6"
      margin="0,0,8,0"
      text="План Харви" />

    <lane orientation="horizontal" margin="0,0,10,0">
      <tab *repeat="{Tabs}"
        layout="108px"
        text="{Label}"
        active="{<>Active}"
        activate=|^SelectTab(Key)| />
    </lane>

    <lane *switch="{SelectedTabKey}" *case="Overview" orientation="vertical">
      <label font="small" text="{OverviewStateLine}" margin="0,0,6,0" />
      <label font="small" text="{OverviewAssignmentLine}" margin="0,0,4,0" />
      <label font="small" text="{OverviewProgressLine}" margin="0,0,4,0" />
      <label font="small" text="{OverviewAfterLine}" margin="0,0,8,0" />
      <label font="small" text="{OverviewStressLine}" margin="0,0,4,0" />
      <label font="small" text="{OverviewInjuriesLine}" margin="0,0,8,0" />
      <label font="small" text="{OverviewAdviceLine}" color="#7f6139" />
    </lane>

    <lane *switch="{SelectedTabKey}" *case="Stress" orientation="vertical">
      <label font="small" text="Назначение" color="#7f6139" margin="0,0,4,0" />
      <label font="small" text="{StressAssignmentTitle}" margin="0,0,2,0" />
      <label font="small" text="{StressAssignmentProgress}" margin="0,0,6,0" />
      <label font="small" text="{StressAssignmentObjective}" margin="0,0,6,0" />
      <label font="small" text="{StressAssignmentAfter}" margin="0,0,8,0" />
      <label font="small" text="{StressNoAssignmentLine}" margin="0,0,8,0" />

      <label font="small" text="Сейчас" color="#7f6139" margin="0,0,6,0" />
      <scrollable peeking="32">
        <grid layout="stretch content" item-layout="length: 260">
          <frame *repeat="{Handbook.ActiveStates}"
            layout="260px 200px"
            background={@Mods/StardewUI/Sprites/MenuBackground}
            border={@Mods/StardewUI/Sprites/MenuBorder}
            border-thickness="12,12,12,12"
            padding="8,8,8,8">
            <lane orientation="vertical">
              <lane orientation="horizontal">
                <image layout="40px 40px" texture="{IconSprite.Texture}" source-rect="{IconSprite.SourceRect}" margin="6,8,0,0" />
                <label font="dialogue" text="{Title}" />
              </lane>
              <label font="small" text="{Effects}" />
              <label font="small" text="{Causes}" />
              <label font="small" text="{CureSummary}" />
              <label font="small" text="{StatusText}" color="{StatusColor}" margin="0,4,0,0" />
              <label font="small" text="Сейчас: {TreatmentStageText}" color="#6b6b6b" />
            </lane>
          </frame>
          <image layout="8px 1px" />
        </grid>
      </scrollable>

      <label font="small" text="Справочник" color="#7f6139" margin="8,0,6,0" />
      <scrollable peeking="32">
        <grid layout="stretch content" item-layout="count: 2">
          <frame *repeat="{Handbook.AllStates}"
            background={@Mods/StardewUI/Sprites/MenuBackground}
            border={@Mods/StardewUI/Sprites/MenuBorder}
            border-thickness="12,12,12,12"
            padding="8,8,8,8"
            margin="0,0,8,8">
            <lane orientation="vertical">
              <lane orientation="horizontal">
                <image layout="40px 40px" texture="{IconSprite.Texture}" source-rect="{IconSprite.SourceRect}" margin="0,0,6,0" />
                <label font="dialogue" text="{Title}" />
              </lane>
              <label font="small" text="{Effects}" />
              <label font="small" text="{Causes}" />
              <label font="small" text="{CureSummary}" />
              <label font="small" text="{StatusText}" color="{StatusColor}" margin="0,4,0,0" />
              <label font="small" text="Сейчас: {TreatmentStageText}" color="#6b6b6b" />
            </lane>
          </frame>
        </grid>
      </scrollable>
    </lane>

    <lane *switch="{SelectedTabKey}" *case="Injuries" orientation="vertical">
      <label font="small" text="{InjuriesBody}" />
    </lane>

    <lane *switch="{SelectedTabKey}" *case="Plan" orientation="vertical">
      <label font="dialogue" text="{PlanTitle}" margin="0,0,8,0" />
      <label font="small" text="{PlanBody}" />
    </lane>

    <lane *switch="{SelectedTabKey}" *case="Trust" orientation="vertical">
      <label font="small" text="{TrustLevelLine}" margin="0,0,6,0" />
      <label font="small" text="{TrustDescriptionLine}" margin="0,0,6,0" />
      <label font="small" text="{TrustPermissionsLine}" margin="0,0,8,0" />
      <label font="small" text="{TrustPlaceholder}" color="#7f6139" />
    </lane>
  </lane>
</frame>

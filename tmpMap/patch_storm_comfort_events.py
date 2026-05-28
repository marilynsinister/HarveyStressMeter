import pathlib

path = pathlib.Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\events.json"
)
text = path.read_text(encoding="utf-8")

gate = (
    "/!FestivalDay/GameStateQuery !PLAYER_HAS_CONVERSATION_TOPIC Current HarveyMod_CD_StormComfort/"
    'GameStateQuery ANY \\"PLAYER_HAS_BUFF Current buffStressThunder\\" '
    '\\"PLAYER_HAS_CONVERSATION_TOPIC Current topicHarveyStormStress\\"'
)

replacements = {
    "eventHarveyStormComfortFarm/GameStateQuery PLAYER_HAS_BUFF Current buffStressThunder/Weather storm/Time 2000 2600/Friendship Harvey 750/Random 0.6": (
        "eventHarveyStormComfortFarm/Weather storm/Time 2000 2600/Friendship Harvey 750/Random 0.6" + gate
    ),
    "eventHarveyStormComfortMountain/GameStateQuery PLAYER_HAS_BUFF Current buffStressThunder/Weather storm/Friendship Harvey 750/Random 0.4": (
        "eventHarveyStormComfortMountain/Weather storm/Friendship Harvey 750/Random 0.4" + gate
    ),
    "eventHarveyStormComfortTown/GameStateQuery PLAYER_HAS_BUFF Current buffStressThunder/Weather storm/Friendship Harvey 750/Random 0.3": (
        "eventHarveyStormComfortTown/Weather storm/Friendship Harvey 750/Random 0.3" + gate
    ),
    "eventHarveyStormComfortForest/GameStateQuery PLAYER_HAS_BUFF Current buffStressThunder/Weather storm/Friendship Harvey 750/Random 0.55": (
        "eventHarveyStormComfortForest/Weather storm/Friendship Harvey 750/Random 0.55" + gate
    ),
    "eventHarveyStormComfortDesert/GameStateQuery PLAYER_HAS_BUFF Current buffStressThunder/Weather storm/Friendship Harvey 750/Random 0.3": (
        "eventHarveyStormComfortDesert/Weather storm/Friendship Harvey 750/Random 0.3" + gate
    ),
    "eventHarveyStormComfortMine/GameStateQuery PLAYER_HAS_BUFF Current buffStressThunder/Weather storm/Friendship Harvey 750/Random 0.8": (
        "eventHarveyStormComfortMine/Weather storm/Friendship Harvey 750/Random 0.8" + gate
    ),
}

for old, new in replacements.items():
    if old not in text:
        raise SystemExit(f"Missing key: {old[:100]}...")
    text = text.replace(old, new)

cleanup_old = r"\\action removeConversationTopic topicStressThunder\\"
cleanup_new = (
    r"\\action removeConversationTopic topicStressThunder\\"
    r"action removeConversationTopic topicHarveyStormStress\\"
    r"action addConversationTopic HarveyMod_CD_StormComfort 3\\"
)
count = text.count(cleanup_old)
text = text.replace(cleanup_old, cleanup_new)

path.write_text(text, encoding="utf-8", newline="\n")
print("storm event keys updated:", len(replacements))
print("topic cleanup blocks updated:", count)

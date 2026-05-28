import json
import pathlib

path = pathlib.Path(
    r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\events.json"
)
text = path.read_text(encoding="utf-8")
old = (
    'GameStateQuery ANY "PLAYER_HAS_BUFF Current buffStressThunder" '
    '"PLAYER_HAS_CONVERSATION_TOPIC Current topicHarveyStormStress"'
)
new = (
    'GameStateQuery ANY \\"PLAYER_HAS_BUFF Current buffStressThunder\\" '
    '\\"PLAYER_HAS_CONVERSATION_TOPIC Current topicHarveyStormStress\\"'
)
count = text.count(old)
if count == 0:
    raise SystemExit("pattern not found")
text = text.replace(old, new)
path.write_text(text, encoding="utf-8", newline="\n")
json.loads(text)
print(f"Fixed {count} occurrences; JSON valid")

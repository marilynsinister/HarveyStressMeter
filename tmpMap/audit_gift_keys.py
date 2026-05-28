import re, json, collections, sys
path = r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code\dialoguesHarvey.json"
raw = open(path, encoding="utf-8").read()
text = re.sub(r"//[^\n]*", "", raw)
text = re.sub(r",(\s*[}\]])", r"\1", text)
data = json.loads(text)

gift_re = re.compile(r"^AcceptGift_\(O\)(.+)$")
prompt2 = ["342","237","196","200","201","610","618","651","614"]
prompt3 = ["24","192","18","22","20","78","404","281","257","422","436","438","442","444"]
prompt4 = ["349","773","80","72","74","279","373","446","797"]
prompt5 = ["346","303","296","396","88","90","2","30"]
expected_new = set(prompt2 + prompt3 + prompt4 + prompt5)
skipped = {
    "395": "Dating-блок (Prompt 2)",
    "432": "tier-патчи 4-7 / 8-10 / Dating",
    "348": "tier-патчи 4-7 / 8-10 / Dating",
    "0": "Weeds — не добавлялся (сомнительный gift Object)",
}

harvey_changes = [c for c in data["Changes"] if c.get("Target") == "Characters/Dialogue/Harvey"]
base = harvey_changes[0]["Entries"]
base_gift_keys = sorted(k for k in base if k.startswith("AcceptGift_"))

dupes_in_patch = []
bad_format = []
long_lines = []
invalid_tokens = []
valid_emote = set("0123456789lhuaqsdkpe^#@*")

for idx, ch in enumerate(harvey_changes):
    entries = ch.get("Entries", {})
    when = ch.get("When")
    counter = collections.Counter(k for k in entries if k.startswith("AcceptGift_"))
    for k, v in counter.items():
        if v > 1:
            dupes_in_patch.append((idx, when, k, v))
    for k, v in entries.items():
        if not k.startswith("AcceptGift_"):
            continue
        if not gift_re.match(k):
            bad_format.append((idx, k))
        vis = re.sub(r"\$[0-9lhuaqsdkpe^#@*]", "", v)
        vis = re.sub(r"#\$[a-z]#", "", vis)
        if len(vis) > 220:
            long_lines.append((k, len(vis), when))
        for m in re.finditer(r"\$.", v):
            tok = m.group(0)
            if tok[1] not in valid_emote and tok not in ("$7", "$8"):
                invalid_tokens.append((k, tok, when))

base_ids = {gift_re.match(k).group(1) for k in base_gift_keys if gift_re.match(k)}
missing_new = expected_new - base_ids

# new comments inside added lines only - scan raw lines 37-76
added_line_range = raw.splitlines()[36:76]
new_jsonc = [i+37 for i,l in enumerate(added_line_range) if "//" in l and '"AcceptGift_' in l]

# vy/ty in new gifts
vy = ty = 0
for k in base_gift_keys:
    m = gift_re.match(k)
    if not m or m.group(1) not in expected_new:
        continue
    t = base[k]
    vy += len(re.findall(r"\b[Вв]ы\b|\b[Вв]ам\b|\b[Вв]аш", t))
    ty += len(re.findall(r"\b[Тт]ы\b|\b[Тт]ебе\b|\b[Тт]во", t))

tier = {}
for ch in harvey_changes[1:]:
    when = ch.get("When")
    for k in ch.get("Entries", {}):
        if k.startswith("AcceptGift_"):
            tier.setdefault(k, []).append(when)

print("=== SUMMARY ===")
print(f"JSON valid: yes")
print(f"Changed files: 1 (dialoguesHarvey.json)")
print(f"Base AcceptGift keys: {len(base_gift_keys)}")
print(f"New keys added (prompts 2-5): {len(expected_new & base_ids)} / {len(expected_new)}")
print(f"Missing new: {sorted(missing_new) or 'none'}")
print(f"Dupes within patch: {dupes_in_patch or 'none'}")
print(f"Bad key format: {bad_format or 'none'}")
print(f"Invalid $ tokens: {invalid_tokens or 'none'}")
print(f"Jsonc in new gift lines: {new_jsonc or 'none'}")
print(f"Long replies (>220 visible): {len(long_lines)}")
for k, n, w in sorted(long_lines, key=lambda x: -x[1]):
    print(f"  {k}: {n} chars")
print(f"Vy refs in new base gifts: {vy}; ty refs: {ty}")
print(f"Tier overrides: {list(tier.keys())}")

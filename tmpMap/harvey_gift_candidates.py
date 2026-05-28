import openpyxl

xlsx = r"d:\Mods for games\Stardew Valley\1.6\mymods\Stardew Valley - Event Modding Resource.xlsx"
wb = openpyxl.load_workbook(xlsx, read_only=True, data_only=True)
ws = wb["Item IDs"]
id2name = {}
for row in ws.iter_rows(min_row=2, values_only=True):
    if row[0] is None:
        continue
    try:
        oid = int(row[0])
    except (TypeError, ValueError):
        continue
    id2name[oid] = row[5] or row[1]

existing = {
    0, 2, 18, 20, 22, 24, 30, 72, 74, 80, 88, 90, 192, 196, 200, 201, 237, 279, 281,
    296, 303, 342, 346, 348, 373, 395, 396, 404, 422, 432, 436, 438, 442, 444, 446,
    610, 614, 618, 651, 773, 797,
}
tiers = [
    ("loved", "348 237 432 395 342"),
    ("liked", "-81 -79 -7 402 614 418 422 436 438 442 444"),
    ("disliked", "-4 424 426 330 233 232 238 234 223 222 221 220 216 211 210 208 206 205"),
    ("hated", "296 245 397 396 394 393 392"),
]
cats = {"-81": "Spring Forage", "-79": "Flowers", "-7": "Fruit", "-4": "Fish"}

for label, tier in tiers:
    print(f"=== {label} ===")
    for part in tier.split():
        if part.startswith("-"):
            print(f"  CAT {part} ({cats.get(part, part)})")
        else:
            oid = int(part)
            mark = "DONE" if oid in existing else "NEW?"
            print(f"  {oid:4} {id2name.get(oid, '?')} [{mark}]")

# sample spring forage / flowers not done
print("\n=== Spring forage samples (-81) ===")
for oid in [16, 592, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 193, 194, 195, 197, 198, 199]:
    if oid not in existing and oid in id2name:
        print(f"  {oid} {id2name[oid]}")

print("\n=== Liked item IDs not done ===")
for oid in [402, 418]:
    print(f"  {oid} {id2name.get(oid,'?')}")

print("\n=== Disliked fish/junk samples ===")
for oid in [130, 136, 142, 148, 150, 206, 208, 210, 212, 218, 224, 228, 230, 233, 242, 707, 715]:
    if oid not in existing and oid in id2name:
        print(f"  {oid} {id2name[oid]}")

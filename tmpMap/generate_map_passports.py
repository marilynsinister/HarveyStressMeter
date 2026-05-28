#!/usr/bin/env python3
"""Generate technical map passports from TMX files in tmpMap/."""

from __future__ import annotations

import base64
import re
import struct
import xml.etree.ElementTree as ET
import zlib
from collections import Counter, defaultdict, deque
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parent
OUT = ROOT.parent / "docs" / "CheckEvent" / "map-passports.md"

# Location -> (primary TMX, source note, fallback TMX or None)
MAP_SOURCES: dict[str, tuple[Path, str, Path | None]] = {
    "Mine": (
        ROOT / "sve" / "maps" / "Locations" / "Mine.tmx",
        "SVE Load (`Mine.tbin` → экспорт TMX в tmpMap)",
        ROOT / "vanilla" / "maps" / "Mine.tmx",
    ),
    "Hospital": (
        ROOT / "sve" / "maps" / "Locations" / "Hospital.tmx",
        "SVE Load",
        ROOT / "vanilla" / "maps" / "Hospital.tmx",
    ),
    "SkullCave": (
        ROOT / "vanilla" / "maps" / "SkullCave.tmx",
        "Vanilla (SVE только EditMap warp)",
        None,
    ),
    "Woods": (
        ROOT / "sve" / "maps" / "Locations" / "Woods2.tmx",
        "SVE Load (`Woods2.tmx` → LocationName Woods)",
        ROOT / "vanilla" / "maps" / "Woods.tmx",
    ),
    "Forest": (
        ROOT / "sve" / "maps" / "Locations" / "Forest.tmx",
        "SVE Load + CP EditMap",
        ROOT / "vanilla" / "maps" / "Forest.tmx",
    ),
    "Custom_AdventurerSummit": (
        ROOT / "sve" / "maps" / "NewLocations" / "AdventurerSummit.tmx",
        "SVE only (нет vanilla .xnb)",
        None,
    ),
    "Mountain": (
        ROOT / "sve" / "maps" / "Locations" / "Mountain.tmx",
        "SVE Load + CP EditMap",
        ROOT / "vanilla" / "maps" / "Mountain.tmx",
    ),
    "Town": (
        ROOT / "sve" / "maps" / "Locations" / "Town.tmx",
        "SVE Load (CC); альтернатива: `Town_Joja.tmx`",
        ROOT / "vanilla" / "maps" / "Town.tmx",
    ),
    "Saloon": (
        ROOT / "vanilla" / "maps" / "Saloon.tmx",
        "Vanilla/SVE `.tbin` — в tmpMap только vanilla TMX",
        None,
    ),
    "Desert": (
        ROOT / "sve" / "maps" / "Locations" / "Desert.tmx",
        "SVE Load",
        ROOT / "vanilla" / "maps" / "Desert.tmx",
    ),
    "BusStop": (
        ROOT / "sve" / "maps" / "Locations" / "BusStop.tmx",
        "SVE Load + CP EditMap",
        ROOT / "vanilla" / "maps" / "BusStop.tmx",
    ),
    "Beach": (
        ROOT / "sve" / "maps" / "Locations" / "Beach.tmx",
        "SVE Load + CP EditMap",
        ROOT / "vanilla" / "maps" / "Beach.tmx",
    ),
    "ArchaeologyHouse": (
        ROOT / "sve" / "maps" / "Locations" / "ArchaeologyHouse.tmx",
        "SVE Load",
        ROOT / "vanilla" / "maps" / "ArchaeologyHouse.tmx",
    ),
}

EVENT_COORDS: dict[str, list[str]] = {
    "Mine": [
        "17 7 / 17 10 — mine rescue, interception",
        "15 5 — storm comfort (farmer)",
        "18 13 — storm comfort (Harvey warp)",
    ],
    "Hospital": [
        "4 6 / 5 9 — FirstTreatment кушетка",
        "6 10 — E2 exam",
        "10 15–19 — E5/E6, medical check dating",
        "15 8 — NightCrisis dating",
        "20 5 — mine rescue / medical check палата",
        "3 15 — rescue operation (телефон)",
    ],
    "SkullCave": ["5 5 farmer / 7 7 Harvey — skull prevention"],
    "Woods": ["27 18 / 40 20 — rescue operation"],
    "Forest": [
        "23 13 — storm comfort",
        "48 14 / 50 13 — E3/E3B",
        "66 16 — rescue pickup (+ sprite 67 12)",
    ],
    "Custom_AdventurerSummit": ["41 27 farmer / 32 42 Harvey — storm comfort act 1"],
    "Mountain": ["79 1 — storm comfort act 2 / 44 21 — E4B перила"],
    "Town": [
        "39 73 — storm comfort",
        "28 67 — E2B",
        "26 22 — E7 (+ Penny 24 22)",
        "35 88 — E9 у клиники",
        "72 22 — storm comfort mine finale",
    ],
    "Saloon": ["14 23 — storm comfort act 2"],
    "Desert": ["15 23 farmer / 17 26 Harvey — storm comfort"],
    "BusStop": [
        "19 23 / 27 23 — first meeting",
        "20 23 / 26 22 — E1 (viewport 52 24)",
        "⚠ 5 9 — eventHarveyCheckup (координаты Hospital?)",
    ],
    "Beach": ["39 23 — E4 pier"],
    "ArchaeologyHouse": ["18 9 — E8 / Harvey warp 3 15"],
}

BLOCKING_LAYER_NAMES = {
    "Buildings",
    "Buildings2",
    "Buildings3",
    "Buildings4",
}
FRONT_LAYER_NAMES = {
    "Front",
    "Front2",
    "Front3",
    "Front4",
    "AlwaysFront",
    "AlwaysFront2",
    "AlwaysFront3",
    "AlwaysFront4",
    "Back2",
    "Back3",
    "Back4",
}

FURNITURE_KEYWORDS = {
    "chair": ("стул", "Chair", "sit", "Sit"),
    "bed": ("кровать", "Bed", "bed"),
    "bench": ("лавка", "Bench"),
    "couch": ("диван", "Couch", "sofa"),
    "table": ("стол", "Table"),
    "counter": ("стойка", "Counter", "Shop"),
    "exam": ("кушетка/осмотр", "Hospital", "Exam", "Medical"),
    "lamp": ("лампа", "Light", "Lamp"),
    "cabinet": ("шкаф", "Cabinet", "Shelf", "Fridge"),
    "door": ("дверь", "Door"),
}


@dataclass
class MapData:
    path: Path
    source_note: str
    location: str
    width: int = 0
    height: int = 0
    tilewidth: int = 16
    tileheight: int = 16
    layers: list[str] = field(default_factory=list)
    map_props: dict[str, str] = field(default_factory=dict)
    back: list[list[int]] = field(default_factory=list)
    buildings: dict[str, list[list[int]]] = field(default_factory=dict)
    front: dict[str, list[list[int]]] = field(default_factory=dict)
    passable_f: list[tuple[int, int, str]] = field(default_factory=list)
    tile_actions: list[tuple[int, int, str, str]] = field(default_factory=list)
    objects: list[dict] = field(default_factory=list)
    warps: list[dict] = field(default_factory=list)
    doors: list[dict] = field(default_factory=list)
    error: str | None = None


def decode_layer_data(data_el: ET.Element, width: int, height: int) -> list[list[int]]:
    encoding = data_el.get("encoding")
    compression = data_el.get("compression")
    text = (data_el.text or "").strip()
    count = width * height

    if encoding == "base64":
        raw = base64.b64decode(text)
        if compression == "zlib":
            raw = zlib.decompress(raw)
        nums = list(struct.unpack(f"<{count}I", raw[: count * 4]))
    else:
        nums = [
            int(x.strip()) if x.strip() else 0
            for x in text.replace("\n", ",").split(",")
            if x.strip() != ""
        ]
        if len(nums) != count:
            nums = nums[:count]
            while len(nums) < count:
                nums.append(0)

    grid = []
    for y in range(height):
        grid.append(nums[y * width : (y + 1) * width])
    return grid


def merge_buildings(grids: dict[str, list[list[int]]]) -> list[list[bool]]:
    if not grids:
        return []
    h = len(next(iter(grids.values())))
    w = len(grids.values().__iter__().__next__()[0])
    blocked = [[False] * w for _ in range(h)]
    for grid in grids.values():
        for y in range(h):
            for x in range(w):
                if grid[y][x] != 0:
                    blocked[y][x] = True
    return blocked


def parse_warps(value: str) -> list[dict]:
    parts = value.split()
    warps = []
    i = 0
    while i + 4 <= len(parts):
        try:
            x, y = int(parts[i]), int(parts[i + 1])
        except ValueError:
            break
        # location name may be multiple tokens? SDV uses single token names
        loc = parts[i + 2]
        try:
            dx, dy = int(parts[i + 3]), int(parts[i + 4])
        except (ValueError, IndexError):
            break
        warps.append({"x": x, "y": y, "dest": loc, "dest_x": dx, "dest_y": dy})
        i += 5
    return warps


def parse_doors(value: str) -> list[dict]:
    parts = value.split()
    doors = []
    i = 0
    while i + 3 <= len(parts):
        try:
            x, y = int(parts[i]), int(parts[i + 1])
            a, b = parts[i + 2], parts[i + 3]
            doors.append({"x": x, "y": y, "open": a, "closed": b})
            i += 4
        except (ValueError, IndexError):
            break
    return doors


def classify_action(action: str) -> str:
    low = action.lower()
    for label, keys in FURNITURE_KEYWORDS.items():
        if any(k.lower() in low for k in keys):
            return label
    if action.startswith("Message"):
        return "message"
    if "warp" in low:
        return "warp"
    if "none" in low:
        return "none"
    return "other"


def scene_usable(x: int, y: int, walkable: list[list[bool]], blocked: list[list[bool]], width: int, height: int) -> str:
    if not (0 <= x < width and 0 <= y < height):
        return "нет (вне карты)"
    if not walkable[y][x]:
        return "нет (непроходимо)"
    # adjacent warp tile?
    for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < width and 0 <= ny < height and blocked[ny][nx]:
            pass
    return "да" if walkable[y][x] else "нет"


def adjacent_free(walkable: list[list[bool]], x: int, y: int, width: int, height: int) -> bool:
    for dx, dy in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < width and 0 <= ny < height and walkable[ny][nx]:
            return True
    return False


def find_regions(walkable: list[list[bool]], max_regions: int = 8, min_size: int = 12) -> list[dict]:
    h = len(walkable)
    w = len(walkable[0]) if h else 0
    seen = [[False] * w for _ in range(h)]
    regions = []
    for sy in range(h):
        for sx in range(w):
            if not walkable[sy][sx] or seen[sy][sx]:
                continue
            q = deque([(sx, sy)])
            seen[sy][sx] = True
            cells = []
            while q:
                x, y = q.popleft()
                cells.append((x, y))
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and walkable[ny][nx] and not seen[ny][nx]:
                        seen[ny][nx] = True
                        q.append((nx, ny))
            if len(cells) >= min_size:
                xs = [c[0] for c in cells]
                ys = [c[1] for c in cells]
                regions.append(
                    {
                        "size": len(cells),
                        "x0": min(xs),
                        "x1": max(xs),
                        "y0": min(ys),
                        "y1": max(ys),
                        "center": (sum(xs) // len(xs), sum(ys) // len(ys)),
                    }
                )
    regions.sort(key=lambda r: -r["size"])
    return regions[:max_regions]


def find_narrow_tiles(walkable: list[list[bool]], limit: int = 40) -> list[tuple[int, int, int]]:
    h = len(walkable)
    w = len(walkable[0]) if h else 0
    narrow = []
    for y in range(h):
        for x in range(w):
            if not walkable[y][x]:
                continue
            n = 0
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and walkable[ny][nx]:
                    n += 1
            if n <= 1:
                narrow.append((x, y, n))
    narrow.sort(key=lambda t: t[2])
    return narrow[:limit]


def find_edge_walkable(walkable: list[list[bool]], limit: int = 30) -> list[tuple[int, int]]:
    h = len(walkable)
    w = len(walkable[0]) if h else 0
    edge = []
    for y in range(h):
        for x in range(w):
            if not walkable[y][x]:
                continue
            if x in (0, w - 1) or y in (0, h - 1):
                edge.append((x, y))
    return edge[:limit]


def load_map(location: str, primary: Path, note: str, fallback: Path | None) -> MapData:
    md = MapData(path=primary, source_note=note, location=location)
    path = primary if primary.exists() else (fallback if fallback and fallback.exists() else None)
    if path is None:
        md.error = f"Файл карты не найден: {primary}" + (f" (fallback {fallback} тоже отсутствует)" if fallback else "")
        return md
    md.path = path
    try:
        tree = ET.parse(path)
    except ET.ParseError as e:
        md.error = f"XML parse error: {e}"
        return md

    root = tree.getroot()
    md.width = int(root.get("width", 0))
    md.height = int(root.get("height", 0))
    md.tilewidth = int(root.get("tilewidth", 16))
    md.tileheight = int(root.get("tileheight", 16))

    props_el = root.find("properties")
    if props_el is not None:
        for prop in props_el.findall("property"):
            name = prop.get("name")
            val = prop.get("value") or prop.text or ""
            md.map_props[name] = val

    if "Warp" in md.map_props:
        md.warps = parse_warps(md.map_props["Warp"])
    if "Doors" in md.map_props:
        md.doors = parse_doors(md.map_props["Doors"])

    # tileset properties
    passable_f_gids: set[int] = set()
    gid_actions: dict[int, str] = {}
    for ts in root.findall("tileset"):
        firstgid = int(ts.get("firstgid", 1))
        for tile in ts.findall("tile"):
            tid = int(tile.get("id", 0))
            gid = firstgid + tid
            props = {p.get("name"): (p.get("value") or p.text or "") for p in tile.findall("properties/property")}
            if props.get("Passable") == "F":
                passable_f_gids.add(gid)
            if "Action" in props:
                gid_actions[gid] = props["Action"]
            if "TouchAction" in props:
                gid_actions.setdefault(gid, props["TouchAction"])

    for layer in root.findall("layer"):
        name = layer.get("name", "")
        md.layers.append(name)
        data_el = layer.find("data")
        if data_el is None or data_el.text is None:
            continue
        grid = decode_layer_data(data_el, md.width, md.height)
        lname = name
        if lname == "Back":
            md.back = grid
        elif lname in BLOCKING_LAYER_NAMES:
            md.buildings[lname] = grid
        elif lname in FRONT_LAYER_NAMES or lname.startswith("Front") or lname.startswith("AlwaysFront"):
            md.front[lname] = grid

    # Passable=F on placed Back tiles
    if md.back:
        for y in range(md.height):
            for x in range(md.width):
                gid = md.back[y][x]
                if gid in passable_f_gids:
                    md.passable_f.append((x, y, gid_actions.get(gid, "Passable=F")))

    # tile actions from back layer gids
    if md.back:
        for y in range(md.height):
            for x in range(md.width):
                gid = md.back[y][x]
                if gid in gid_actions:
                    md.tile_actions.append((x, y, "Back", gid_actions[gid]))

    for og in root.findall("objectgroup"):
        og_name = og.get("name", "")
        md.layers.append(f"objectgroup:{og_name}")
        for obj in og.findall("object"):
            ox = float(obj.get("x", 0))
            oy = float(obj.get("y", 0))
            tw = md.tilewidth
            th = md.tileheight
            tx = int(ox // tw)
            ty = int(oy // th)
            props = {p.get("name"): (p.get("value") or p.text or "") for p in obj.findall("properties/property")}
            entry = {
                "x": tx,
                "y": ty,
                "layer": og_name,
                "props": props,
                "name": obj.get("name", ""),
            }
            md.objects.append(entry)
            action = props.get("Action") or props.get("TouchAction")
            if action:
                md.tile_actions.append((tx, ty, og_name, action))

    return md


def compute_walkable(md: MapData) -> list[list[bool]]:
    blocked = merge_buildings(md.buildings)
    walkable = [[False] * md.width for _ in range(md.height)]
    passable_f_set = {(x, y) for x, y, _ in md.passable_f}
    if not md.back:
        return walkable
    for y in range(md.height):
        for x in range(md.width):
            if md.back[y][x] == 0:
                continue
            if (x, y) in passable_f_set:
                continue
            if blocked[y][x]:
                continue
            walkable[y][x] = True
    return walkable


def has_front(md: MapData, x: int, y: int) -> bool:
    for grid in md.front.values():
        if grid[y][x] != 0:
            return True
    return False


def fmt_range(r: dict) -> str:
    return f"({r['x0']},{r['y0']})–({r['x1']},{r['y1']})"


def render_passport(md: MapData) -> str:
    lines: list[str] = []
    loc = md.location
    lines.append(f"## {loc} / {md.path.name}")
    lines.append("")
    if md.error:
        lines.append(f"**Статус:** карта не прочитана — {md.error}")
        lines.append("")
        return "\n".join(lines)

    rel = md.path.relative_to(ROOT.parent) if md.path.is_relative_to(ROOT.parent) else md.path
    lines.append(f"**Файл:** `{rel}`")
    lines.append(f"**Источник:** {md.source_note}")
    if EVENT_COORDS.get(loc):
        lines.append(f"**Координаты событий (из audit):** {'; '.join(EVENT_COORDS[loc])}")
    lines.append("")

    # Size and layers
    lines.append("### Размер и слои")
    lines.append("")
    lines.append(f"- **width:** {md.width} | **height:** {md.height}")
    lines.append(f"- **tilewidth:** {md.tilewidth} | **tileheight:** {md.tileheight}")
    layer_names = [l for l in md.layers if not l.startswith("objectgroup:")]
    og_names = [l.replace("objectgroup:", "") for l in md.layers if l.startswith("objectgroup:")]
    lines.append(f"- **Tile-слои:** {', '.join(layer_names) if layer_names else '—'}")
    if og_names:
        lines.append(f"- **Object groups:** {', '.join(og_names)}")
    if md.map_props:
        skip = {"Warp", "Doors"}
        other = {k: v for k, v in md.map_props.items() if k not in skip}
        if other:
            lines.append("- **Map properties (кроме Warp/Doors):**")
            for k, v in sorted(other.items()):
                vv = v if len(v) < 120 else v[:117] + "..."
                lines.append(f"  - `{k}` = `{vv}`")
    lines.append("")

    walkable = compute_walkable(md)
    blocked = merge_buildings(md.buildings)
    walk_count = sum(sum(row) for row in walkable)
    block_count = sum(sum(row) for row in blocked)
    pf_count = len(md.passable_f)

    lines.append("**Проходимость (по TMX, без runtime-патчей):**")
    lines.append(f"- Проходимых тайлов (Back≠0, Buildings=0, без Passable=F): **{walk_count}**")
    lines.append(f"- Блокирующих Buildings-тайлов (любой Buildings-слой ≠0): **{block_count}**")
    lines.append(f"- Тайлов с **Passable=F** на Back: **{pf_count}**")
    if pf_count and pf_count <= 25:
        lines.append(f"  - Координаты: {', '.join(f'({x},{y})' for x,y,_ in md.passable_f)}")
    elif pf_count:
        sample = md.passable_f[:20]
        lines.append(f"  - Примеры (первые 20): {', '.join(f'({x},{y})' for x,y,_ in sample)} …")
    lines.append("")

    # Doors and warps
    lines.append("### Двери и warp")
    lines.append("")
    lines.append("| Tile X | Tile Y | Тип | Destination | Комментарий |")
    lines.append("|--------|--------|-----|-------------|-------------|")
    if not md.warps and not md.doors:
        lines.append("| — | — | — | — | Warp/Doors properties отсутствуют |")
    for w in md.warps:
        comm = []
        x, y = w["x"], w["y"]
        if 0 <= x < md.width and 0 <= y < md.height:
            if blocked[y][x]:
                comm.append("Buildings блокирует тайл")
            elif not walkable[y][x]:
                comm.append("тайл непроходим (Back=0 или Passable=F)")
            else:
                comm.append("тайл проходим")
            if adjacent_free(walkable, x, y, md.width, md.height):
                comm.append("рядом есть свободный тайл для spawn")
            else:
                comm.append("⚠ рядом нет свободного тайла")
        else:
            comm.append("координаты вне карты")
        lines.append(
            f"| {x} | {y} | Warp | {w['dest']} ({w['dest_x']}, {w['dest_y']}) | {'; '.join(comm)} |"
        )
    for d in md.doors:
        x, y = d["x"], d["y"]
        comm = []
        if 0 <= x < md.width and 0 <= y < md.height:
            if walkable[y][x]:
                comm.append("тайл проходим")
            else:
                comm.append("тайл непроходим")
            # find door action on object
            door_actions = [o for o in md.objects if o["x"] == x and o["y"] == y and "Door" in (o["props"].get("Action") or "")]
            if door_actions:
                comm.append(f"Action: {door_actions[0]['props'].get('Action')}")
        lines.append(
            f"| {x} | {y} | Door | open={d['open']} closed={d['closed']} | {'; '.join(comm) or '—'} |"
        )
    lines.append("")

    # Interactive objects
    lines.append("### Интерактивные объекты и мебель")
    lines.append("")
    lines.append("| Tile X | Tile Y | Объект | Layer | Свойства | Можно использовать в сцене? |")
    lines.append("|--------|--------|--------|-------|----------|----------------------------|")
    seen = set()
    rows = []
    for x, y, layer, action in md.tile_actions:
        key = (x, y, action)
        if key in seen:
            continue
        seen.add(key)
        kind = classify_action(action)
        usable = scene_usable(x, y, walkable, blocked, md.width, md.height)
        if kind in ("none", "message") and usable == "да":
            usable_note = "да (декор/сообщение)"
        elif kind == "door":
            usable_note = "осторожно (дверь)"
        elif kind in ("chair", "bed", "bench", "couch", "exam"):
            usable_note = f"рядом, не на тайле ({kind})"
        else:
            usable_note = usable
        rows.append((kind, x, y, layer, action, usable_note))
    rows.sort(key=lambda r: (r[0], r[2], r[1]))
    if not rows:
        lines.append("| — | — | — | — | Action/TouchAction не найдены | — |")
    else:
        max_rows = 80
        for kind, x, y, layer, action, usable_note in rows[:max_rows]:
            act = action.replace("|", "\\|")
            if len(act) > 80:
                act = act[:77] + "..."
            lines.append(f"| {x} | {y} | {kind} | {layer} | `{act}` | {usable_note} |")
        if len(rows) > max_rows:
            lines.append(f"| … | … | … | … | ещё {len(rows) - max_rows} объектов | … |")
    lines.append("")

    # Safe zones
    lines.append("### Безопасные постановочные зоны")
    lines.append("")
    lines.append("| Zone name | Tile range | Подходит для | Комментарий |")
    lines.append("|-----------|------------|--------------|-------------|")
    regions = find_regions(walkable, max_regions=6, min_size=max(8, (md.width * md.height) // 500))
    if regions:
        for i, r in enumerate(regions, 1):
            cx, cy = r["center"]
            front_at_center = has_front(md, cx, cy) if 0 <= cx < md.width and 0 <= cy < md.height else False
            comm = f"~{r['size']} тайлов; центр ({cx},{cy})"
            if front_at_center:
                comm += "; ⚠ AlwaysFront/Front на центре"
            lines.append(
                f"| Open area #{i} | {fmt_range(r)} | farmer, Harvey, temp NPC | {comm} |"
            )
    else:
        lines.append("| — | — | — | Нет крупных связных зон (min size filter) |")

    # Event-specific spot checks
    event_checks = {
        "Mine": [(17, 7), (17, 10), (15, 5), (18, 14)],
        "Hospital": [(4, 6), (5, 9), (6, 10), (10, 19), (20, 5), (3, 15)],
        "SkullCave": [(5, 5), (7, 7)],
        "Woods": [(27, 18), (40, 20)],
        "Forest": [(23, 13), (48, 14), (50, 13), (66, 16), (67, 12)],
        "Custom_AdventurerSummit": [(41, 27), (32, 42)],
        "Mountain": [(79, 1), (44, 21)],
        "Town": [(39, 73), (28, 67), (26, 22), (35, 88), (72, 22)],
        "Saloon": [(14, 23)],
        "Desert": [(15, 23), (17, 26)],
        "BusStop": [(19, 23), (27, 23), (20, 23), (26, 22), (5, 9)],
        "Beach": [(39, 23)],
        "ArchaeologyHouse": [(18, 9), (3, 15)],
    }
    for x, y in event_checks.get(loc, []):
        if 0 <= x < md.width and 0 <= y < md.height:
            w_ok = walkable[y][x]
            b = blocked[y][x]
            fr = has_front(md, x, y)
            lines.append(
                f"| Event ref ({x},{y}) | ({x},{y}) | audit coords | farmer/Harvey | "
                f"{'проходим' if w_ok else 'НЕ проходим'}; Buildings={'да' if b else 'нет'}; Front={'да' if fr else 'нет'} |"
            )
    lines.append("")

    # Risky zones
    lines.append("### Рискованные зоны")
    lines.append("")
    lines.append("| Tile/range | Причина риска | Что не делать |")
    lines.append("|------------|---------------|---------------|")
    for w in md.warps:
        lines.append(
            f"| ({w['x']},{w['y']}) | Warp → {w['dest']} | не ставить NPC на warp-тайл; проверить соседний spawn |"
        )
    narrow = find_narrow_tiles(walkable, limit=25)
    if narrow:
        coords = ", ".join(f"({x},{y})" for x, y, _ in narrow[:15])
        lines.append(f"| {coords} | узкий проход (≤1 сосед) | не блокировать advancedMove / не ставить двух NPC |")
    edges = find_edge_walkable(walkable, limit=20)
    if edges:
        ec = ", ".join(f"({x},{y})" for x, y in edges[:12])
        lines.append(f"| {ec} | край карты | не walk-out за bounds; проверить viewport |")
    # AlwaysFront on walkable event tiles
    af_tiles = []
    for y in range(md.height):
        for x in range(md.width):
            if walkable[y][x] and has_front(md, x, y):
                af_tiles.append((x, y))
    if af_tiles:
        sample = af_tiles[:15]
        sc = ", ".join(f"({x},{y})" for x, y in sample)
        lines.append(
            f"| {sc}{'…' if len(af_tiles)>15 else ''} | проходимо, но Front/AlwaysFront | NPC может визуально перекрыться объектом |"
        )
    if not md.warps and not narrow and not edges:
        lines.append("| — | явных рисков не выделено | — |")
    lines.append("")

    # Recommendations
    lines.append("### Рекомендации для событий")
    lines.append("")
    recs = []
    if regions:
        cx, cy = regions[0]["center"]
        recs.append(f"- **Общая открытая зона:** {fmt_range(regions[0])} — центр ({cx},{cy}) для постановки и walk path.")
    if md.warps:
        recs.append("- **Warp-точки:** см. таблицу; для `warp`/`changeLocation` сверять dest с map property.")
    if loc == "Mine":
        recs.append("- **Mine rescue/interception:** зона 17,7–17,10 — проверено в `Mine_event_placement_analysis.md`; лестница y=9 проходима.")
        recs.append("- **Viewport:** ViewportFollowPlayer=True; шахтный вход — северная часть x≈15–21.")
    elif loc == "Hospital":
        recs.append("- **Осмотры/кушетка:** западная комната x≈4–6, y≈6–10; палата x≈20, y≈5.")
        recs.append("- **Выход:** warp (10,20)→Town; двери HarveyRoom (9–10,5) и (10,1).")
    elif loc == "Custom_AdventurerSummit":
        recs.append("- **Storm comfort act 1:** farmer 41,27 на склоне; Harvey long advancedMove от 32,42 — проверить коллизии тропы.")
    elif loc == "Mountain":
        recs.append("- **Storm act 2:** warp 79,1 — край карты, проверить SVE warp с Summit.")
        recs.append("- **E4B:** 44,21 — перила; farmer +2 по X.")
    elif loc == "Town":
        recs.append("- **Большая карта:** E7/E9/storm — юг и центр; учитывать CC vs Joja (`Town_Joja.tmx` другая планировка).")
    elif loc == "Forest":
        recs.append("- **Rescue pickup 66,16:** temporaryAnimatedSprite 67,12 — проверить Buildings/Front на дороге.")
    elif loc == "BusStop":
        recs.append("- **E1 viewport 52,24** — восточная часть; ⚠ eventHarveyCheckup coords как Hospital — target BusStop.")
    elif loc == "ArchaeologyHouse":
        recs.append("- **E8:** полки 18,9; Harvey path от 3,15 advancedMove — проверить проходимость музея.")
    if not recs:
        recs.append("- Сверить координаты audit с таблицей Event ref выше.")
    recs.append("- **Runtime:** SVE EditMap и save-условия могут менять карту поверх этого TMX.")
    lines.extend(recs)
    lines.append("")
    return "\n".join(lines)


def main() -> None:
    parts = [
        "# Технические паспорта карт (CP-события Harvey Overhaul)",
        "",
        "Автогенерация из TMX в `tmpMap/`. **События и координаты не менялись.**",
        "",
        "Метод: парсинг XML (`width/height`, слои, map `Warp`/`Doors`, objectgroup `Action`, Buildings=коллизия, Back=пол, Passable=F).",
        "Не учтено: runtime `.tbin` патчи SVE после Load, `positionOffset`, NPC collision с Front-тайлами в движке.",
        "",
        "Приоритет файлов: SVE TMX (если Load заменяет vanilla) → vanilla TMX. Saloon/SkullCave — только vanilla.",
        "",
        "---",
        "",
    ]

    for loc, (primary, note, fallback) in MAP_SOURCES.items():
        md = load_map(loc, primary, note, fallback)
        parts.append(render_passport(md))
        parts.append("---")
        parts.append("")

    # Town_Joja supplement
    joja = ROOT / "sve" / "maps" / "Locations" / "Town_Joja.tmx"
    if joja.exists():
        md = load_map("Town (Joja variant)", joja, "SVE альтернатива при Joja route", None)
        parts.append(render_passport(md))
        parts.append("---")
        parts.append("")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(parts), encoding="utf-8")
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()

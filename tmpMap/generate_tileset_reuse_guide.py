#!/usr/bin/env python3
"""Generate tileset reuse guide from audit-map TMX files."""

from __future__ import annotations

import base64
import html
import re
import struct
import xml.etree.ElementTree as ET
import zlib
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT.parent / "docs" / "CheckEvent" / "tileset-reuse-guide.md"

AUDIT_MAPS: dict[str, Path] = {
    "Mine": ROOT / "sve" / "maps" / "Locations" / "Mine.tmx",
    "Hospital": ROOT / "sve" / "maps" / "Locations" / "Hospital.tmx",
    "SkullCave": ROOT / "vanilla" / "maps" / "SkullCave.tmx",
    "Woods": ROOT / "sve" / "maps" / "Locations" / "Woods2.tmx",
    "Forest": ROOT / "sve" / "maps" / "Locations" / "Forest.tmx",
    "Custom_AdventurerSummit": ROOT / "sve" / "maps" / "NewLocations" / "AdventurerSummit.tmx",
    "Mountain": ROOT / "sve" / "maps" / "Locations" / "Mountain.tmx",
    "Town": ROOT / "sve" / "maps" / "Locations" / "Town.tmx",
    "Saloon": ROOT / "vanilla" / "maps" / "Saloon.tmx",
    "Desert": ROOT / "sve" / "maps" / "Locations" / "Desert.tmx",
    "BusStop": ROOT / "sve" / "maps" / "Locations" / "BusStop.tmx",
    "Beach": ROOT / "sve" / "maps" / "Locations" / "Beach.tmx",
    "ArchaeologyHouse": ROOT / "sve" / "maps" / "Locations" / "ArchaeologyHouse.tmx",
}

EVENT_COORDS: dict[str, list[tuple[int, int]]] = {
    "Mine": [(17, 7), (17, 10), (15, 5), (18, 14)],
    "Hospital": [(4, 6), (5, 9), (6, 10), (10, 19), (20, 5), (3, 15), (9, 5), (10, 5)],
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

USEFUL_PROP_KEYS = {
    "Action",
    "TouchAction",
    "Passable",
    "Type",
    "PathType",
    "Water",
    "Diggable",
    "Buildable",
    "NoSpawn",
    "Light",
}

CATEGORY_RULES: list[tuple[str, re.Pattern]] = [
    ("door", re.compile(r"door|Door|LockedDoor", re.I)),
    ("chair_seat", re.compile(r"Sit|Chair|Stool|seat", re.I)),
    ("bed_exam", re.compile(r"Bed|Exam|Hospital|couch|Couch|bench|Bench", re.I)),
    ("counter_shop", re.compile(r"Shop|Counter|Fridge|Till", re.I)),
    ("path", re.compile(r"PathType|path", re.I)),
    ("light", re.compile(r"Light|Lamp|light", re.I)),
    ("window", re.compile(r"Window|Curtain", re.I)),
    ("carpet", re.compile(r"Carpet|rug|Rug|mat", re.I)),
    ("shelf_cabinet", re.compile(r"Notes|Shelf|Cabinet|Fridge|Gunther", re.I)),
    ("decorative", re.compile(r"Message|Animation|Rearrange|SandDragon", re.I)),
    ("warp", re.compile(r"Warp|LoadMap|Minecart|MineElevator|DesertBus", re.I)),
    ("passability", re.compile(r"Passable|NoSpawn|Water", re.I)),
    ("terrain", re.compile(r"Type|Diggable|Buildable|Grass|Dirt|Stone|Wood", re.I)),
]


@dataclass
class TilesetInfo:
    key: str
    name: str
    image_source: str
    tilewidth: int = 16
    tileheight: int = 16
    columns: int = 0
    tilecount: int = 0
    image_width: int = 0
    image_height: int = 0
    firstgid_by_map: dict[str, int] = field(default_factory=dict)
    tile_props: dict[int, dict[str, str]] = field(default_factory=dict)
    local_image_path: str | None = None

    @property
    def local_id_range(self) -> str:
        if self.tilecount:
            return f"0–{self.tilecount - 1}"
        return "unknown"

    def resolve_image_path(self) -> str:
        if self.local_image_path:
            return self.local_image_path
        src = self.image_source.replace("\\", "/")
        base = Path(src).name
        if base.endswith(".png"):
            p = ROOT / "sve" / "tilesets" / base
            if p.exists():
                return str(p.relative_to(ROOT.parent))
        stem = base.replace(".png", "").replace(".PNG", "")
        candidates = [
            ROOT / "vanilla" / "tilesets" / f"{stem}.xnb",
            ROOT / "vanilla" / "tilesets" / "Mines" / f"{stem}.xnb",
            Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Content\Maps") / f"{stem}.xnb",
            Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Content\Maps\Mines") / f"{stem}.xnb",
            Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\Stardew Valley Expanded\[CP] Stardew Valley Expanded\Assets\Tilesheets") / f"{stem}.png",
        ]
        for c in candidates:
            if c.exists():
                return str(c)
        return f"Content/Maps/{src} (не найден локально — needs visual check)"


@dataclass
class UsefulTile:
    tileset_key: str
    tileset_name: str
    local_id: int
    category: str
    description: str
    layer_hint: str
    props_summary: str
    passability: str
    npc_adjacent: str
    seat_use: str
    global_context: str
    risks: str
    source: str


def norm_source(src: str) -> str:
    return src.replace("\\", "/").replace(".png", "").replace(".PNG", "").lower()


def decode_layer(data_el: ET.Element, width: int, height: int) -> list[int]:
    text = (data_el.text or "").strip()
    count = width * height
    if data_el.get("encoding") == "base64":
        raw = base64.b64decode(text)
        if data_el.get("compression") == "zlib":
            raw = zlib.decompress(raw)
        return list(struct.unpack(f"<{count}I", raw[: count * 4]))
    nums = [int(x.strip()) if x.strip() else 0 for x in text.replace("\n", ",").split(",") if x.strip()]
    while len(nums) < count:
        nums.append(0)
    return nums[:count]


def categorize(props: dict[str, str], action_text: str = "") -> tuple[str, str]:
    blob = " ".join(f"{k}={v}" for k, v in props.items()) + " " + action_text
    for cat, pat in CATEGORY_RULES:
        if pat.search(blob):
            desc = action_text or ", ".join(f"{k}={v}" for k, v in sorted(props.items()))
            return cat, desc
    if props:
        return "other_prop", ", ".join(f"{k}={v}" for k, v in sorted(props.items()))
    return "unknown", action_text or ""


def layer_hint_for(category: str, on_buildings: bool, on_front: bool) -> str:
    if category in ("door", "counter_shop", "bed_exam", "shelf_cabinet"):
        return "Buildings (+ object Action)"
    if category == "path":
        return "Paths"
    if on_front or category in ("decorative", "light", "window"):
        return "Front / AlwaysFront"
    if on_buildings:
        return "Buildings"
    return "Back (или декор Front)"


def parse_map(path: Path, location: str) -> tuple[dict[str, TilesetInfo], list[UsefulTile], list[dict]]:
    tree = ET.parse(path)
    root = tree.getroot()
    w, h = int(root.get("width")), int(root.get("height"))
    tilesets: dict[str, TilesetInfo] = {}
    gid_map: list[tuple[int, str, TilesetInfo]] = []

    for ts in root.findall("tileset"):
        name = ts.get("name", "unnamed")
        img_el = ts.find("image")
        src = img_el.get("source", "") if img_el is not None else ""
        key = norm_source(src) or name.lower()
        info = tilesets.get(key)
        if info is None:
            info = TilesetInfo(
                key=key,
                name=name,
                image_source=src,
                tilewidth=int(ts.get("tilewidth", 16)),
                tileheight=int(ts.get("tileheight", 16)),
                columns=int(ts.get("columns", 0)),
                tilecount=int(ts.get("tilecount", 0)),
                image_width=int(img_el.get("width", 0)) if img_el is not None else 0,
                image_height=int(img_el.get("height", 0)) if img_el is not None else 0,
            )
            tilesets[key] = info
        firstgid = int(ts.get("firstgid", 1))
        info.firstgid_by_map[location] = firstgid
        gid_map.append((firstgid, key, info))

        for tile in ts.findall("tile"):
            tid = int(tile.get("id", 0))
            props = {
                p.get("name"): (p.get("value") or p.text or "")
                for p in tile.findall("properties/property")
            }
            if props:
                merged = info.tile_props.setdefault(tid, {})
                merged.update(props)

    gid_map.sort(key=lambda x: -x[0])

    layers: dict[str, list[int]] = {}
    for layer in root.findall("layer"):
        d = layer.find("data")
        if d is not None and d.text:
            layers[layer.get("name", "")] = decode_layer(d, w, h)

    def gid_at(layer: str, x: int, y: int) -> int:
        if layer not in layers or not (0 <= x < w and 0 <= y < h):
            return 0
        return layers[layer][y * w + x]

    def resolve_gid(gid: int) -> tuple[TilesetInfo | None, int]:
        if gid <= 0:
            return None, 0
        for firstgid, key, info in gid_map:
            if gid >= firstgid:
                return info, gid - firstgid
        return None, 0

    useful: list[UsefulTile] = []
    seen: set[tuple[str, int, str]] = set()

    def add_tile(
        info: TilesetInfo,
        local_id: int,
        category: str,
        desc: str,
        layer_hint: str,
        props_summary: str,
        passability: str,
        npc_adjacent: str,
        seat_use: str,
        global_ctx: str,
        risks: str,
        source: str,
    ) -> None:
        sig = (info.key, local_id, category + desc[:40])
        if sig in seen:
            return
        seen.add(sig)
        useful.append(
            UsefulTile(
                tileset_key=info.key,
                tileset_name=info.name,
                local_id=local_id,
                category=category,
                description=desc,
                layer_hint=layer_hint,
                props_summary=props_summary,
                passability=passability,
                npc_adjacent=npc_adjacent,
                seat_use=seat_use,
                global_context=global_ctx,
                risks=risks,
                source=source,
            )
        )

    # From tileset property definitions
    for info in tilesets.values():
        for local_id, props in info.tile_props.items():
            if not props or not (USEFUL_PROP_KEYS & set(props.keys())):
                continue
            cat, desc = categorize(props)
            if cat == "unknown" and not props:
                continue
            pf = props.get("Passable", "")
            passability = f"Passable={pf}" if pf else ("—" if "Passable" not in props else props.get("Passable", "—"))
            if pf == "F":
                passability = "Passable=F (Back блокирует)"
                npc_adj = "нет на тайле"
                seat = "нет"
                risk = "коллизия Back; не ставить NPC"
            elif pf == "T":
                npc_adj = "можно на тайле"
                seat = "needs visual check"
                risk = "Passable=T — проверить Buildings-слой на карте"
            else:
                npc_adj = "needs visual check"
                seat = "needs visual check"
                risk = "сверить Buildings/Front на целевой карте"

            fg = info.firstgid_by_map.get(location, 0)
            gctx = f"gid {fg + local_id} на {location} (firstgid={fg})" if fg else "—"
            add_tile(
                info,
                local_id,
                cat,
                desc or "needs visual check",
                layer_hint_for(cat, False, False),
                ", ".join(f"{k}={v}" for k, v in sorted(props.items())),
                passability,
                npc_adj,
                seat,
                gctx,
                risk,
                f"tileset property ({location} TMX)",
            )

    # Map object TileData
    for og in root.findall("objectgroup"):
        og_name = og.get("name", "")
        for obj in og.findall("object"):
            tx = int(float(obj.get("x", 0)) // 16)
            ty = int(float(obj.get("y", 0)) // 16)
            props = {
                p.get("name"): (p.get("value") or p.text or "")
                for p in obj.findall("properties/property")
            }
            action = props.get("Action") or props.get("TouchAction") or ""
            if not action:
                continue
            cat, desc = categorize({}, action)
            b = gid_at("Buildings", tx, ty)
            back = gid_at("Back", tx, ty)
            fr = gid_at("Front", tx, ty) or gid_at("AlwaysFront", tx, ty)
            info, local_id = resolve_gid(b or back)
            if info is None:
                info = next(iter(tilesets.values()), None)
                local_id = -1
            if info is None:
                continue
            pf = info.tile_props.get(local_id, {}).get("Passable", "") if local_id >= 0 else ""
            passability = "Buildings≠0 → непроходимо" if b else ("Passable=F" if pf == "F" else "object overlay")
            npc_adj = "рядом, не на тайле" if b else "осторожно"
            seat = "нет" if "Door" in action else "needs visual check"
            risk = "не блокировать дверь/warp" if cat == "door" else "Action активен в runtime"
            gctx = f"({tx},{ty}) on {location}; gid={b or back}"
            if local_id >= 0 and info.firstgid_by_map.get(location):
                gctx += f"; local={local_id}, global={info.firstgid_by_map[location]+local_id}"
            add_tile(
                info,
                local_id if local_id >= 0 else 0,
                cat,
                action,
                og_name + " objectgroup",
                action,
                passability,
                npc_adj,
                seat,
                gctx,
                risk,
                f"map object ({location})",
            )

    # Event coordinate sampling
    event_samples = []
    for x, y in EVENT_COORDS.get(location, []):
        sample = {"map": location, "x": x, "y": y, "layers": {}}
        for lname in ("Back", "Buildings", "Front", "AlwaysFront", "Paths"):
            g = gid_at(lname, x, y)
            if g:
                info, lid = resolve_gid(g)
                sample["layers"][lname] = {
                    "gid": g,
                    "tileset": info.name if info else "?",
                    "local_id": lid,
                    "global": (info.firstgid_by_map[location] + lid) if info and info.firstgid_by_map.get(location) is not None else g,
                }
        event_samples.append(sample)

    return tilesets, useful, event_samples


def is_useful_category(cat: str) -> bool:
    return cat not in ("unknown", "terrain", "other_prop") or cat == "passability"


def render_tileset_section(info: TilesetInfo, tiles: list[UsefulTile], all_maps: dict[str, TilesetInfo]) -> str:
    lines = [f"## Tileset: `{info.name}` (`{info.image_source}`)", ""]
    lines.append("### Общая информация")
    lines.append("")
    lines.append(f"- **Normalized key:** `{info.key}`")
    lines.append(f"- **Tile size:** {info.tilewidth}×{info.tileheight} px")
    if info.image_width and info.image_height:
        lines.append(f"- **Image size:** {info.image_width}×{info.image_height} px")
    lines.append(f"- **Columns:** {info.columns or '—'}")
    lines.append(f"- **Tile count:** {info.tilecount or '—'}")
    lines.append(f"- **Local tile ID range:** {info.local_id_range}")
    lines.append(f"- **Image source (открыть для visual check):** `{info.resolve_image_path()}`")
    lines.append("")
    lines.append("**firstgid на картах audit:**")
    lines.append("")
    lines.append("| Map | firstgid | Global ID formula |")
    lines.append("|-----|----------|-------------------|")
    for loc, fg in sorted(info.firstgid_by_map.items()):
        lines.append(f"| {loc} | {fg} | global = {fg} + local_id |")
    if not info.firstgid_by_map:
        lines.append("| — | — | — |")
    lines.append("")

    # Filter and sort useful tiles
    filtered = [t for t in tiles if t.tileset_key == info.key]
    priority = [t for t in filtered if t.category not in ("terrain", "unknown")]
    if not priority:
        priority = filtered[:30]

    lines.append("### Полезные тайлы")
    lines.append("")
    if not priority:
        lines.append("_В TMX нет tile properties / object Action для этого sheet. **needs visual check** — откройте image source._")
        lines.append("")
    else:
        lines.append("| Local tile ID | Global ID / firstgid context | Что это | Где использовать | Слой | Passability | NPC рядом | Сиденье | Риски |")
        lines.append("|---------------|-------------------------------|---------|------------------|------|-------------|-----------|---------|-------|")
        for t in priority[:60]:
            desc = t.description.replace("|", "\\|")
            if len(desc) > 70:
                desc = desc[:67] + "..."
            lines.append(
                f"| {t.local_id if t.local_id >= 0 else '—'} | {t.global_context} | {t.category}: {desc} | {t.source} | {t.layer_hint} | {t.passability} | {t.npc_adjacent} | {t.seat_use} | {t.risks} |"
            )
        if len(priority) > 60:
            lines.append(f"| … | … | ещё {len(priority)-60} записей | … | … | … | … | … | … |")
        lines.append("")

    lines.append("### Рекомендации по повторному использованию")
    lines.append("")
    recs = recommend_for_tileset(info, priority)
    lines.extend(recs if recs else ["- **needs visual check** — откройте image source и сверьте с картой в Tiled/игре."])
    lines.append("")
    return "\n".join(lines)


def recommend_for_tileset(info: TilesetInfo, tiles: list[UsefulTile]) -> list[str]:
    key = info.key
    recs = []
    cats = {t.category for t in tiles}

    if key in ("towninterior", "towninterior_2") or "interior" in key:
        recs.append("- **Клиника / Hospital / Saloon / ArchaeologyHouse:** двери, стойки, кушетки, шкафы — Buildings-слой; NPC ставить на соседний проходимый Back-тайл.")
        recs.append("- **Сидение в сцене:** showFrame/animate в событии, не tile Action; рядом с Buildings-мебелью.")
        recs.append("- **Не ставить персонажей** на тайлы с Buildings≠0 (двери, кушетки, стойки).")
    if key == "paths":
        recs.append("- **Paths-слой:** NPC-маршруты; для событий не заменять path-тайлы без проверки schedule.")
    if "outdoor" in key or key.startswith("spring_") or key.startswith("fall_"):
        recs.append("- **Лес / гроза / outdoor:** Passable=T Type=Wood — возможные мостики/настилы (**needs visual check**).")
        recs.append("- **Front/AlwaysFront:** декор, тени, кусты — персонаж может визуально перекрыться.")
    if key == "mine" or "mines" in key:
        recs.append("- **Mine / SkullCave:** пол и стены mine sheet; лифт/шахта — object Action, не декор.")
    if "spring_sve_tilesheet2" in key or "spring_z_extras" in key:
        recs.append("- **SVE-локации:** уникальные тайлы Summit, BusStop patches — только при наличии sheet в игре.")
    if "spring_town" in key:
        recs.append("- **Town / Forest декор:** здания, скамейки, машины — часто Front/Buildings; rescue truck = temporaryAnimatedSprite, не tile.")
    if "spring_beach" in key:
        recs.append("- **Beach / pier:** пирс E4 — проверить Passable и Front на (39,23).")
    if "deserttiles" in key:
        recs.append("- **Desert storm:** площадка у автобуса; Light property на карте, не на tile.")
    if "spring_shadows" in key or "shadow" in key:
        recs.append("- **Только визуал:** тени/canopy — AlwaysFront; не использовать для коллизий.")
    if not recs:
        if cats & {"door", "bed_exam", "counter_shop"}:
            recs.append("- Подходит для интерьерных сцен с опорой на Buildings-коллизии.")
        elif cats & {"path"}:
            recs.append("- Подходит для pathing и outdoor дорожек.")
    return recs


def render_event_tile_appendix(samples: list[dict]) -> str:
    lines = ["## Приложение: тайлы на координатах событий (audit)", ""]
    lines.append("Извлечено из layer GID на coords событий. **needs visual check** для визуального типа (стул/кровать/лава).")
    lines.append("")
    lines.append("| Map | X | Y | Layer | GID | Tileset | Local ID | Global ID |")
    lines.append("|-----|---|---|-------|-----|---------|----------|-----------|")
    for s in samples:
        if not s["layers"]:
            lines.append(f"| {s['map']} | {s['x']} | {s['y']} | — | — | — | — | — |")
            continue
        first = True
        for layer, data in s["layers"].items():
            lines.append(
                f"| {s['map'] if first else ''} | {s['x'] if first else ''} | {s['y'] if first else ''} | {layer} | {data['gid']} | {data['tileset']} | {data['local_id']} | {data['global']} |"
            )
            first = False
    lines.append("")
    return "\n".join(lines)


def main() -> None:
    merged_tilesets: dict[str, TilesetInfo] = {}
    all_useful: list[UsefulTile] = []
    all_event_samples: list[dict] = []

    for location, path in AUDIT_MAPS.items():
        if not path.exists():
            continue
        tilesets, useful, events = parse_map(path, location)
        all_useful.extend(useful)
        all_event_samples.extend(events)
        for key, info in tilesets.items():
            if key not in merged_tilesets:
                merged_tilesets[key] = info
            else:
                m = merged_tilesets[key]
                m.firstgid_by_map.update(info.firstgid_by_map)
                for tid, props in info.tile_props.items():
                    m.tile_props.setdefault(tid, {}).update(props)
                if not m.image_width and info.image_width:
                    m.image_width = info.image_width
                    m.image_height = info.image_height

    # Sort tilesets: interiors first, then outdoors, paths, SVE custom
    def sort_key(k: str) -> tuple:
        order = {
            "towninterior": 0,
            "towninterior_2": 1,
            "paths": 2,
            "mine": 3,
            "spring_outdoorstilesheet": 4,
            "spring_town": 5,
        }
        for prefix, rank in order.items():
            if k.startswith(prefix):
                return (rank, k)
        if "sve" in k or "z_" in k:
            return (8, k)
        return (5, k)

    parts = [
        "# Справочник tileset’ов для сцен",
        "",
        "Справочник для переиспользования тайлов при проверке/кастомизации CP-событий Harvey Overhaul.",
        "",
        "**Источники:** TMX audit-карт в `tmpMap/sve/maps/`, `tmpMap/vanilla/maps/` (см. [`maps-and-tilesets-inventory.md`](maps-and-tilesets-inventory.md)).",
        "**Не менялось:** карты, события, tileset-файлы.",
        "",
        "Легенда:",
        "- **Local tile ID** — индекс внутри tileset (0-based).",
        "- **Global ID** = `firstgid` + local_id на конкретной карте.",
        "- **needs visual check** — тип тайла (стул/кровать/окно) не определён без PNG/XNB; указан путь к image source.",
        "- **Сиденье:** в SDV-сценах обычно `showFrame`/animate, не tile `Sit`; NPC ставится на соседний проходимый тайл.",
        "",
        "---",
        "",
        "## Сводка tileset’ов",
        "",
        "| Image source | Tileset name(s) | Tiles | Columns | Maps (firstgid) | Локальный файл |",
        "|--------------|-----------------|------:|--------:|-----------------|----------------|",
    ]

    for key in sorted(merged_tilesets.keys(), key=sort_key):
        info = merged_tilesets[key]
        names = info.name
        maps_fg = ", ".join(f"{m}:{fg}" for m, fg in sorted(info.firstgid_by_map.items()))
        local = info.resolve_image_path()
        short = local if len(local) < 50 else "…" + local[-47:]
        parts.append(
            f"| `{info.image_source}` | {names} | {info.tilecount or '—'} | {info.columns or '—'} | {maps_fg or '—'} | `{short}` |"
        )

    parts.append("")
    parts.append("---")
    parts.append("")

    for key in sorted(merged_tilesets.keys(), key=sort_key):
        info = merged_tilesets[key]
        tiles = [t for t in all_useful if t.tileset_key == key]
        parts.append(render_tileset_section(info, tiles, merged_tilesets))
        parts.append("---")
        parts.append("")

    parts.append(render_event_tile_appendix(all_event_samples))

    parts.append("## Ограничения")
    parts.append("")
    parts.append("- `townInterior` / `townInterior_2` в SVE Hospital TMX **без embedded tile properties** — мебель видна только как Buildings GID + object Action.")
    parts.append("- Saloon — vanilla TMX; SVE `.tbin` может отличаться.")
    parts.append("- Seasonal sheets (`spring_*`, `fall_*`) в runtime подменяются сезоном — firstgid сохраняется, текстуры меняются.")
    parts.append("- Для полного каталога стульев/кровать **needs visual check** на `Content/Maps/townInterior.xnb` + Tiled.")
    parts.append("")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(parts), encoding="utf-8")
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes, {len(merged_tilesets)} tilesets)")


if __name__ == "__main__":
    main()

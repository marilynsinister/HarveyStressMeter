#!/usr/bin/env python3
"""Generate per-map passport files under docs/CheckEvent/maps/ and index."""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# Reuse TMX parser from monolithic generator
sys.path.insert(0, str(Path(__file__).resolve().parent))
from generate_map_passports import (  # noqa: E402
    EVENT_COORDS,
    MAP_SOURCES,
    classify_action,
    compute_walkable,
    find_edge_walkable,
    find_narrow_tiles,
    find_regions,
    fmt_range,
    has_front,
    load_map,
    merge_buildings,
    scene_usable,
)

ROOT = Path(__file__).resolve().parent.parent
MAPS_DIR = ROOT / "docs" / "CheckEvent" / "maps"
INDEX = ROOT / "docs" / "CheckEvent" / "map-passports.md"

# Maps to emit (user list, used by events)
USER_MAPS = [
    "Hospital",
    "Farm",
    "Town",
    "Forest",
    "Woods",
    "Mountain",
    "Mine",
    "SkullCave",
    "Desert",
    "BusStop",
    "Beach",
    "HarveyRoom",
    "Custom_AdventurerSummit",
]

FILE_NAMES = {
    "Custom_AdventurerSummit": "Custom_AdventurerSummit.md",
}

EVENT_CHECKS = {
    "Mine": [(17, 7), (17, 10), (15, 5), (18, 13), (18, 14)],
    "Hospital": [(4, 6), (5, 9), (6, 10), (10, 15), (10, 16), (10, 19), (15, 8), (20, 5), (3, 15)],
    "SkullCave": [(5, 5), (7, 7)],
    "Woods": [(27, 18), (40, 20)],
    "Forest": [(23, 13), (48, 14), (50, 13), (66, 16), (67, 12)],
    "Custom_AdventurerSummit": [(41, 27), (32, 42)],
    "Mountain": [(79, 1), (44, 21)],
    "Town": [(39, 73), (28, 67), (26, 22), (35, 88), (72, 22)],
    "Desert": [(15, 23), (17, 26)],
    "BusStop": [(19, 23), (27, 23), (20, 23), (26, 22), (5, 9)],
    "Beach": [(39, 23)],
}

MAP_EVENTS: dict[str, list[tuple[str, str, str, str, str]]] = {
    # Event ID, File, Status, Coords checked?, Notes
    "Hospital": [
        ("HarveyMod_FirstTreatment", "events.json", "needs-review", "partial", "кушетка 4,6 / 5,9"),
        ("HarveyOverhaulStory.E2_InsistentExam", "events.json", "needs-review", "partial", "6,10"),
        ("HarveyOverhaulStory.E5_StormBeside", "events.json", "needs-review", "yes", "10,19"),
        ("HarveyOverhaulStory.E6_SayItOutLoud", "events.json", "needs-review", "partial", "10,16"),
        ("eventHarveyMedicalCheck_Dating", "events.json", "needs-review", "partial", "маршрут к 20,5"),
        ("HarveyMod_NightCrisis_Dating", "events.json", "needs-review", "partial", "15,8"),
        ("HarveyMod_NightCrisis_PreDating", "events.json", "needs-review", "partial", "15,8"),
        ("HarveyMod_BirthdayHospital_Dating", "events.json", "needs-review", "partial", "10,15"),
        ("HarveyMod_BirthdayHospital_Friend", "events.json", "needs-review", "partial", "10,15"),
        ("eventHarveyMineRescue", "eventsMineRescue.json", "checked-ok", "yes", "финал 20,5 + offset"),
        ("eventHarveyMineRescueDating", "eventsMineRescue.json", "checked-ok", "yes", "финал 20,5"),
        ("eventHarveyMinorMineRescue", "eventsMineRescue.json", "checked-ok", "yes", "14,6 / 15,6"),
        ("eventRescueOperation", "events.json", "needs-review", "partial", "телефон 3,15; финал 20,5"),
        ("eventHarveyMedicalCheck", "events.json", "manually-verified-do-not-touch", "yes", "исключён из audit"),
        ("eventHarveyEmergencyCare", "events.json", "manually-verified-do-not-touch", "yes", ""),
        ("eventHarveyExhaustion", "events.json", "manually-verified-do-not-touch", "yes", ""),
        ("eventHarveyTraumaExam", "events.json", "manually-verified-do-not-touch", "yes", ""),
        ("eventHarveyTreatmentCollapse", "events.json", "manually-verified-do-not-touch", "yes", ""),
        ("eventStayInHospital", "events.json", "manually-verified-do-not-touch", "yes", ""),
        ("HarveyMod_TreatmentPlanMeeting", "events.json", "manually-verified-do-not-touch", "yes", ""),
        ("HarveyMod_TreatmentReview", "events.json", "manually-verified-do-not-touch", "yes", ""),
        ("HarveyMod_RecoveryComplete", "events.json", "manually-verified-do-not-touch", "yes", ""),
        ("HarveyMod_NightCrisis", "events.json", "manually-verified-do-not-touch", "yes", ""),
        ("HarveyMod_BirthdayHospital", "events.json", "manually-verified-do-not-touch", "yes", ""),
    ],
    "Farm": [
        ("eventHarveyFirstVisit", "events.json", "manually-verified-do-not-touch", "partial", "Standard ~64,15–18"),
        ("eventHarveySecondVisit", "events.json", "manually-verified-do-not-touch", "partial", ""),
        ("eventHarveyFirstWalk", "events.json", "manually-verified-do-not-touch", "partial", ""),
        ("acceptWalk", "events.json", "manually-verified-do-not-touch", "partial", "fork → Forest"),
        ("eventHarveyCheckHealthFarmer", "events.json", "manually-verified-do-not-touch", "partial", "→ Hospital"),
        ("eventHarveyCheckFarmerOutsideAfter22", "events.json", "manually-verified-do-not-touch", "partial", ""),
        ("eventHarveyMorningCheckup", "events.json", "manually-verified-do-not-touch", "partial", ""),
        ("eventHarveyStormComfortFarm", "events.json", "manually-verified-do-not-touch", "partial", "→ Hospital fork"),
    ],
    "HarveyRoom": [
        ("eventHarveyRoomCheckup", "events.json", "manually-verified-do-not-touch", "no", "needs map export"),
        ("eventHarveyRoomCheckup2", "events.json", "manually-verified-do-not-touch", "no", "needs map export"),
    ],
    "Mine": [
        ("eventHarveyMineRescue", "eventsMineRescue.json", "needs-review", "yes", "17,7 / 17,10"),
        ("eventHarveyMineRescueDating", "eventsMineRescue.json", "needs-review", "yes", ""),
        ("eventHarveyMinorMineRescue", "eventsMineRescue.json", "checked-ok", "yes", ""),
        ("eventHarveyMineInterception", "eventsCare.json", "checked-ok", "yes", "17,7 / 17,10"),
        ("eventHarveyStormComfortMine", "events.json", "checked-ok", "partial", "15,5 / 18,13 → Town"),
    ],
    "SkullCave": [
        ("eventHarveySkullCavePrevention", "eventsCare.json", "checked-ok", "yes", "5,5 / 7,7"),
    ],
    "Woods": [
        ("eventRescueOperation", "events.json", "needs-review", "partial", "27,18 / 40,20"),
    ],
    "Forest": [
        ("eventHarveyStormComfortForest", "events.json", "checked-ok", "yes", "23,13"),
        ("HarveyOverhaulStory.E3_ForestApothecary", "events.json", "checked-ok", "yes", "50,13"),
        ("HarveyOverhaulStory.E3B_WingPatient", "events.json", "checked-ok", "yes", "48,14"),
        ("eventRescueOperation", "events.json", "needs-review", "partial", "пикап 66,16"),
        ("eventHarveyFirstDate", "events.json", "manually-verified-do-not-touch", "yes", ""),
        ("acceptWalk", "events.json", "manually-verified-do-not-touch", "partial", "fork destination"),
    ],
    "Custom_AdventurerSummit": [
        ("eventHarveyStormComfortMountain", "events.json", "needs-review", "partial", "акт 1: 41,27 / 32,42"),
    ],
    "Mountain": [
        ("eventHarveyStormComfortMountain", "events.json", "needs-review", "partial", "акт 2: 79,1"),
        ("HarveyOverhaulStory.E4B_TooQuiet", "events.json", "checked-ok", "yes", "44,21"),
        ("eventHarveyMountainDate", "events.json", "manually-verified-do-not-touch", "yes", ""),
    ],
    "Town": [
        ("eventHarveyStormComfortTown", "events.json", "needs-review", "partial", "39,73 → Saloon"),
        ("HarveyOverhaulStory.E2B_QuietAgreement", "events.json", "checked-ok", "yes", "28,67"),
        ("HarveyOverhaulStory.E7_TownSip_Sunny", "events.json", "needs-review", "partial", "26,22; Harvey 27,22 Buildings"),
        ("HarveyOverhaulStory.E9_LightInWindow", "events.json", "needs-review", "partial", "35,88"),
        ("eventHarveyStormComfortMine", "events.json", "needs-review", "partial", "финал 72,22"),
        ("eventHarveyLateNightCollapse", "events.json", "manually-verified-do-not-touch", "yes", "→ Hospital"),
    ],
    "Desert": [
        ("eventHarveyStormComfortDesert", "events.json", "needs-review", "partial", "15,23; Harvey 17,26 Buildings"),
    ],
    "BusStop": [
        ("eventHarveyFirstMeeting", "events.json", "needs-review", "partial", "19,23 / 27,23"),
        ("HarveyOverhaulStory.E1_SlipperyPath", "events.json", "checked-ok", "yes", "viewport 52,24"),
        ("eventHarveyCheckup", "eventsCare.json", "risky", "no", "target BusStop, coords Hospital"),
    ],
    "Beach": [
        ("HarveyOverhaulStory.E4_PierBreath", "events.json", "checked-ok", "yes", "39,23"),
        ("eventHarveyPropose", "events.json", "manually-verified-do-not-touch", "yes", ""),
    ],
}

INDEX_META = {
    "Hospital": ("ready", "SVE + vanilla TMX", "partial", "tmpMap/sve/maps/Locations/Hospital.tmx"),
    "Farm": ("partial-by-design", "variable layouts", "risky/procedural", "—"),
    "Town": ("partial", "SVE Load (CC/Joja)", "external SVE/custom", "tmpMap/sve/maps/Locations/Town.tmx"),
    "Forest": ("partial", "SVE Load + EditMap", "external SVE/custom", "tmpMap/sve/maps/Locations/Forest.tmx"),
    "Woods": ("partial", "SVE Woods2.tmx", "external SVE/custom", "tmpMap/sve/maps/Locations/Woods2.tmx"),
    "Mountain": ("partial", "SVE Load + EditMap", "external SVE/custom", "tmpMap/sve/maps/Locations/Mountain.tmx"),
    "Mine": ("partial", "SVE Load", "external SVE/custom", "tmpMap/sve/maps/Locations/Mine.tmx"),
    "SkullCave": ("partial", "vanilla TMX", "external vanilla", "tmpMap/vanilla/maps/SkullCave.tmx"),
    "Desert": ("partial", "SVE Load", "external SVE/custom", "tmpMap/sve/maps/Locations/Desert.tmx"),
    "BusStop": ("partial", "SVE Load + EditMap", "external SVE/custom", "tmpMap/sve/maps/Locations/BusStop.tmx"),
    "Beach": ("partial", "SVE Load + EditMap", "external SVE/custom", "tmpMap/sve/maps/Locations/Beach.tmx"),
    "HarveyRoom": ("partial-by-audit", "vanilla interior", "external vanilla", "—"),
    "Custom_AdventurerSummit": ("partial", "SVE only", "external SVE/custom", "tmpMap/sve/maps/NewLocations/AdventurerSummit.tmx"),
}


def parse_tilesets(path: Path) -> list[dict]:
    if not path.exists():
        return []
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError:
        return []
    out = []
    for ts in root.findall("tileset"):
        name = ts.get("name", "?")
        firstgid = ts.get("firstgid", "1")
        tw = ts.get("tilewidth", "16")
        th = ts.get("tileheight", "16")
        img = ts.find("image")
        src = img.get("source", "—") if img is not None else "—"
        external = "да" if ts.get("source") else "нет"
        out.append(
            {
                "name": name,
                "firstgid": firstgid,
                "size": f"{tw}×{th}",
                "source": src,
                "external": external,
                "notes": "",
            }
        )
    return out


def source_kind(loc: str, note: str) -> str:
    if loc == "Farm":
        return "vanilla (procedural layouts)"
    if loc == "HarveyRoom":
        return "vanilla"
    if "SVE only" in note or loc == "Custom_AdventurerSummit":
        return "SVE custom"
    if "Vanilla" in note or loc == "SkullCave":
        return "vanilla"
    if "SVE" in note:
        return "SVE"
    return "unknown"


def status_for(loc: str, md_error: str | None) -> str:
    if md_error or loc in ("Farm", "HarveyRoom"):
        return "needs map export" if loc in ("Farm", "HarveyRoom") else "partial"
    st, _, _, _ = INDEX_META.get(loc, ("partial", "", "", ""))
    return st


def render_doors_warps(md, walkable, blocked) -> str:
    lines = [
        "| X | Y | Type | Destination | Direction/use | Safe nearby tiles | Notes |",
        "|---|---|------|-------------|---------------|-------------------|-------|",
    ]
    if not md.warps and not md.doors:
        return lines[0] + "\n" + lines[1] + "\n| — | — | — | — | — | — | Warp/Doors отсутствуют |"
    from generate_map_passports import adjacent_free

    for w in md.warps:
        x, y = w["x"], w["y"]
        comm = []
        safe = "—"
        if 0 <= x < md.width and 0 <= y < md.height:
            if walkable[y][x]:
                comm.append("проходим")
            else:
                comm.append("непроходим/Buildings")
            safe = "да" if adjacent_free(walkable, x, y, md.width, md.height) else "⚠ нет"
        else:
            comm.append("вне карты")
        lines.append(
            f"| {x} | {y} | Warp | {w['dest']} ({w['dest_x']}, {w['dest_y']}) | exit/transition | {safe} | {'; '.join(comm)} |"
        )
    for d in md.doors:
        x, y = d["x"], d["y"]
        use = "Action Door"
        safe = "—"
        if 0 <= x < md.width and 0 <= y < md.height:
            safe = "нет (Buildings)" if blocked[y][x] else ("да" if adjacent_free(walkable, x, y, md.width, md.height) else "⚠")
        lines.append(
            f"| {x} | {y} | Door | open={d['open']} closed={d['closed']} | {use} | {safe} | не setup-тайл |"
        )
    return "\n".join(lines)


def render_action_tiles(md, walkable, blocked) -> str:
    lines = [
        "| X | Y | Layer | Property | Meaning | Event risk |",
        "|---|---|-------|----------|---------|------------|",
    ]
    seen = set()
    rows = []
    for x, y, layer, action in md.tile_actions:
        key = (x, y, action)
        if key in seen:
            continue
        seen.add(key)
        kind = classify_action(action)
        risk = "low"
        if "Door" in action or "Warp" in action:
            risk = "high — warp/door"
        elif kind in ("bed", "exam", "counter"):
            risk = "medium — medical/furniture"
        elif not (0 <= x < md.width and 0 <= y < md.height and walkable[y][x]):
            risk = "high — blocked"
        rows.append((x, y, layer, action, kind, risk))
    rows.sort(key=lambda r: (r[1], r[0]))
    if not rows:
        lines.append("| — | — | — | — | Action/TouchAction не найдены | — |")
    else:
        for x, y, layer, action, kind, risk in rows[:60]:
            act = action.replace("|", "\\|")
            if len(act) > 70:
                act = act[:67] + "..."
            lines.append(f"| {x} | {y} | {layer} | `{act}` | {kind} | {risk} |")
        if len(rows) > 60:
            lines.append(f"| … | … | … | … | ещё {len(rows)-60} | см. TMX |")
    return "\n".join(lines)


def render_furniture(md, walkable, blocked) -> str:
    lines = [
        "| Object | Approx coords/range | Layer | Blocks movement? | Blocks visibility? | Notes |",
        "|--------|---------------------|-------|------------------|--------------------|-------|",
    ]
    rows = []
    for x, y, layer, action in md.tile_actions:
        kind = classify_action(action)
        if kind in ("message", "none", "other", "warp"):
            continue
        bm = "yes" if (0 <= x < md.width and 0 <= y < md.height and blocked[y][x]) else "partial"
        bv = "yes" if (0 <= x < md.width and 0 <= y < md.height and has_front(md, x, y)) else "no"
        rows.append((kind, f"({x},{y})", layer, bm, bv, action[:60]))
    rows.sort(key=lambda r: (r[0], r[1]))
    if not rows:
        lines.append("| — | — | — | — | — | см. TMX objectgroups |")
    else:
        for kind, coords, layer, bm, bv, note in rows[:40]:
            lines.append(f"| {kind} | {coords} | {layer} | {bm} | {bv} | `{note}` |")
        if len(rows) > 40:
            lines.append(f"| … | … | … | … | … | ещё {len(rows)-40} объектов |")
    return "\n".join(lines)


def render_safe_zones(md, walkable) -> str:
    lines = [
        "| Zone ID | Coordinates/range | Best for | Why safe | Camera suggestion | Notes |",
        "|---------|-------------------|----------|----------|-------------------|-------|",
    ]
    regions = find_regions(walkable, max_regions=6, min_size=max(8, (md.width * md.height) // 500))
    zone_names = {
        "Hospital": ["hospital_main_floor", "hospital_reception", "hospital_bedside_right", "hospital_warp_harveyroom"],
        "Mine": ["mine_entrance_staging", "mine_rescue_zone", "mine_storm_ladder"],
        "Town": ["town_south_storm", "town_clinic_facade", "town_bench_mine_finale", "town_center_e7"],
        "Forest": ["forest_storm_lane", "forest_apothecary", "forest_rescue_pickup"],
        "Woods": ["woods_rescue_search", "woods_rescue_hideout"],
        "BusStop": ["busstop_road_center", "busstop_e1_viewport"],
        "Beach": ["beach_pier_approach"],
        "Mountain": ["mountain_summit_warp", "mountain_railing_e4b"],
        "Desert": ["desert_bus_shelter"],
        "SkullCave": ["skullcave_entrance"],
        "Custom_AdventurerSummit": ["summit_storm_slope"],
    }
    names = zone_names.get(md.location, [])
    for i, r in enumerate(regions):
        zid = names[i] if i < len(names) else f"open_area_{i+1}"
        cx, cy = r["center"]
        cam = f"viewport ~({cx},{cy})"
        lines.append(
            f"| `{zid}` | {fmt_range(r)} | dialogue, staging | ~{r['size']} passable tiles | {cam} | center ({cx},{cy}) |"
        )
    for x, y in EVENT_CHECKS.get(md.location, []):
        if 0 <= x < md.width and 0 <= y < md.height and walkable[y][x]:
            lines.append(
                f"| `event_ref_{x}_{y}` | ({x},{y}) | audit coords | проходим по TMX | viewport ({x},{y}) | Event ref |"
            )
    if md.location == "Hospital":
        lines.extend([
            "| `hospital_bedside_major` | (19,5)–(21,5), (20,6) | major rescue, lying farmer | Harvey (19,5); bed (20,5) Buildings | viewport (20,5) | ignoreCollisions + offset 32 -52 |",
            "| `hospital_bedside_minor` | (13,6)–(16,6) | minor rescue, seated exam | открытый пол | viewport (14,6) | farmer (14,6), Harvey (15,6) |",
            "| `hospital_exam_west` | (4,6)–(6,10) | FirstTreatment, E2 | кушетка запад | viewport (5,8) | (5,9) door — не setup |",
            "| `hospital_counter_front` | (1,14)–(14,19) | reception, phone | открытый зал | viewport (8,16) | телефон rescue (3,15) |",
        ])
    if len(lines) == 2:
        lines.append("| — | — | — | — | — | — |")
    return "\n".join(lines)


def render_risk_zones(md, walkable) -> str:
    lines = [
        "| Coords/range | Risk | Why dangerous | Avoid in event commands |",
        "|--------------|------|---------------|---------------------------|",
    ]
    blocked = merge_buildings(md.buildings)
    for w in md.warps:
        lines.append(
            f"| ({w['x']},{w['y']}) | door/warp tile | Warp → {w['dest']} | setup NPC, block path |"
        )
    for x, y, _ in find_narrow_tiles(walkable, limit=12):
        lines.append(f"| ({x},{y}) | narrow passage | ≤1 сосед | двойной advancedMove |")
    for x, y in find_edge_walkable(walkable, limit=8):
        lines.append(f"| ({x},{y}) | map edge | walk-out / viewport | длинный move к краю |")
    if len(lines) == 2:
        lines.append("| — | — | — | — |")
    return "\n".join(lines)


def scene_notes(loc: str) -> str:
    notes = {
        "Hospital": """- Западная комната (x≈4–6, y≈6–10): кушетка, FirstTreatment, E2.
- Палата B (20,5): кровать major rescue — только `ignoreCollisions` + `positionOffset 32 -52` + lying animate.
- Палата A (14,6 / 15,6): minor rescue, сидячий осмотр.
- Стойка HospitalShop (5–7, 16): блокирует юг; оставлять проход.
- Warp (10,20)→Town — exit; (9–10,1)→HarveyRoom.
- Подробный разбор палат: `tmpMap/Hospital_event_placement_analysis.md`.""",
        "Farm": """- **Needs map export** — TMX Farm в репозитории отсутствует.
- Standard Farm: события часто используют зону **(64,15–18)** у FarmHouse.
- Layout зависит от типа фермы (Standard, Riverland, Forest, Hill-top, Wilderness, Four Corners, Beach, Meadowlands).
- Фиксированные объекты (куст, сарай, мост) меняются между layouts — не копировать координаты вслепую.
- SVE Grandpa's Farm / IF2R патчит BusStop и Forest warps — косвенно влияет на Farm-сцены.""",
        "HarveyRoom": """- **Needs map export** — TMX HarveyRoom в репозитории отсутствует.
- Маленькое помещение; мало места для movement — предпочитать статичную постановку.
- Warp из Hospital: (9–10,1) → HarveyRoom (6,12).
- Сцены checkup: `eventHarveyRoomCheckup`, `eventHarveyRoomCheckup2` (manually verified).""",
        "Mine": """- Стабильная карта входа (77×20); **не путать с MineShaft** (процедурно).
- Rescue zone: (17,7) farmer, (17,10) Harvey — проверено в `Mine_event_placement_analysis.md`.
- **Запрещены** move/advancedMove NPC с y ≥ 11.
- Warp (18,14)→Custom_AdventurerSummit — не setup.
- ViewportFollowPlayer=True.""",
        "SkullCave": """- Маленькая карта (16×10); мало проходимых тайлов.
- Warp (7,9)→Desert — не ставить NPC.
- Сцена prevention: farmer (5,5), Harvey (7,7).""",
        "Forest": """- Огромная карта (120×120); длинные advancedMove рискованны.
- Storm comfort: farmer (23,13), Harvey warp (35,13).
- Rescue pickup (66,16) + sprite (67,12).
- SVE canopy/shadow patches могут сместить визуал.""",
        "Woods": """- Secret Woods (Woods2.tmx, 90×75); густые деревья и Passable=F зоны.
- Rescue: телефонная сцена не здесь; поиск (27,18), укрытие (40,20).
- Warp на Forest (82,29) и Custom_ForestWest (47–49,71).""",
        "Mountain": """- Узкие проходы у озера и шахтного LoadMap (103,16).
- Storm act 2: warp (79,1) — северный край, связь с AdventurerSummit.
- E4B: (44,21) перила — farmer +2 по X.""",
        "Town": """- Большая карта; CC vs **Town_Joja.tmx** — другая планировка при Joja route.
- Клиника: LockedDoorWarp (36,55)→Hospital; E9 у фасада (35,88).
- Storm comfort: farmer (39,73), затем **Saloon** (отдельная карта, акт 2).
- **Saloon (storm act 2):** TMX `tmpMap/vanilla/maps/Saloon.tmx` (46×25); warp farmer/Harvey **(14,23)**; exit warp (14,25)→Town (45,71). Двери (11,9), (20,9). Open area центр ~(21,18).
- E7: Harvey (27,22) на Buildings — broken в audit.""",
        "Desert": """- Открытое пространство; автобус DesertBus (18,27).
- Storm: farmer (15,23); Harvey (17,26) на Buildings — risky.
- NPCWarp 18,26 BusStop.""",
        "BusStop": """- ViewportClamp 10,0,35,30 — учитывать камеру.
- E1 viewport (52,24) — восток карты.
- ⚠ eventHarveyCheckup: target BusStop, coords как Hospital (5,9).""",
        "Beach": """- Пирс E4: (39,23); exit move 0,-10 к воде.
- BrokenBeachBridge (58,13); много воды на востоке.
- Warp (37–40,-1)→Town.""",
        "Custom_AdventurerSummit": """- SVE-only локация; warp (31–33,43)→Mountain, (19,14)→Mine.
- Storm act 1: farmer (41,27), Harvey long advancedMove от (32,42).
- Узкие тропы и края склона — проверять advancedMove.""",
    }
    return notes.get(loc, "—")


def camera_zones(loc: str) -> str:
    rows = {
        "Hospital": [
            ("medical care", "20, 5", "койка major rescue", "ignoreCollisions + offset"),
            ("medical care", "14, 6", "minor rescue / exam", "faceDirection на Harvey"),
            ("normal dialogue", "15, 8", "NightCrisis", "вечерний interior light"),
            ("entrance/exit", "10, 19", "E5 storm", "коридор"),
            ("medical care", "6, 10", "E2 exam", "кушетка"),
        ],
        "Mine": [
            ("rescue", "17, 7", "mine rescue start", "ViewportFollowPlayer"),
            ("storm comfort", "15, 5", "farmer у лестницы", "короткий move"),
            ("rescue", "17, 10", "Harvey spawn", "move Harvey 0 -2"),
        ],
        "Town": [
            ("storm comfort", "39, 73", "юг Town", "advancedMove chase"),
            ("normal dialogue", "28, 67", "E2B", "площадь"),
            ("romantic", "35, 88", "E9 у клиники", "ambientLight evening"),
            ("panic/weakness", "72, 22", "mine storm finale", "скамейка"),
        ],
        "Farm": [
            ("normal dialogue", "64, 16", "Standard Farm front", "needs map export"),
        ],
        "HarveyRoom": [
            ("medical care", "—", "checkup scenes", "needs map export"),
        ],
    }
    lines = [
        "| Scene type | viewport X Y | Why | Notes |",
        "|------------|--------------|-----|-------|",
    ]
    for st, vp, why, note in rows.get(loc, [("normal dialogue", "—", "см. audit coords", "—")]):
        lines.append(f"| {st} | {vp} | {why} | {note} |")
    return "\n".join(lines)


def movement_guidance(loc: str, md, walkable) -> str:
    good = []
    avoid = []
    if loc == "Hospital":
        good = [
            ("Harvey", "(6,10)", "(4,6)", "`move` / `faceDirection`", "осмотр E2"),
            ("farmer", "(10,19)", "(10,16)", "`proceedPosition`", "E5/E6"),
            ("Harvey", "(19,5)", "(20,5)", "static + offset farmer", "major rescue"),
        ]
        avoid = [("(5,9)", "(5,5)", "дверь Buildings"), ("(20,5)", "(21,5)", "кровать без ignoreCollisions")]
    elif loc == "Mine":
        good = [("Harvey", "(17,10)", "(17,8)", "`move 0 -2`", "interception/rescue")]
        avoid = [("(17,7)", "(17,13)", "y≥11 blocked"), ("(18,14)", "any", "warp tile")]
    elif loc == "Forest":
        good = [("Harvey", "(35,13)", "(24,13)", "`move -11 0`", "storm comfort")]
        avoid = [("(66,16)", "(67,12)", "sprite overlap — проверить Front")]
    elif loc == "Custom_AdventurerSummit":
        good = [("Harvey", "(32,42)", "(41,27)", "`advancedMove`", "storm act 1 — stopAdvancedMoves")]
        avoid = [("(32,43)", "(31,43)", "warp row south")]
    elif loc == "Farm":
        good = [("Harvey", "FarmHouse door", "(64,16)", "`move` short", "needs layout verify")]
        avoid = [("fixed coords", "other layout", "procedural farm")]
    elif loc == "HarveyRoom":
        good = [("both", "static", "static", "minimal move", "small room")]
        avoid = [("any", "any", "needs map export")]
    else:
        good = [("NPC", "audit from", "audit to", "`move` short", "см. Event ref")]
    lines = ["### Good paths", "", "| Actor | From | To | Suggested commands | Notes |", "|-------|------|-----|----------------------|-------|"]
    for row in good:
        lines.append(f"| {row[0]} | {row[1]} | {row[2]} | {row[3]} | {row[4]} |")
    lines += ["", "### Avoid paths", "", "| From | To | Why avoid |", "|------|-----|-----------|"]
    for row in avoid:
        lines.append(f"| {row[0]} | {row[1]} | {row[2]} |")
    return "\n".join(lines)


def quick_rules(loc: str) -> str:
    rules = {
        "Hospital": [
            "Не ставить farmer/Harvey на (5,9) и (10,13) — Action Door, Buildings.",
            "Койка (20,5) — только через ignoreCollisions + positionOffset 32 -52 + lying animate.",
            "Harvey у major bed: (19,5), faceDirection 1.",
            "Minor rescue: farmer (14,6), Harvey (15,6).",
            "Warp (10,20)→Town — только exit, не setup.",
            "Warp (9–10,1)→HarveyRoom — не блокировать.",
            "Стойка (5–7,16) — не ставить NPC на counter tiles.",
            "Кушетка FirstTreatment: Harvey (4,6), farmer (5,9) — (5,9) Buildings, используется в CP осознанно — проверять визуал.",
            "E5/E6: зона (10,15–19) — коридор/рабочая зона.",
            "NightCrisis dating: (15,8) — кресло, вечер.",
            "Телефон rescue: (3,15) — проходим, Front да.",
            "После changeLocation — re-warp всех NPC.",
            "DayTiles/NightTiles меняют Front у кроватей — проверять в игре.",
            "Не делать длинный advancedMove через узкие проходы (19,14) и т.п.",
        ],
        "Farm": [
            "Needs map export — не выдумывать координаты для новых layouts.",
            "Standard Farm: зона (64,15–18) у FarmHouse — baseline для onboarding.",
            "Проверять layout type перед фиксацией координат.",
            "Riverland/Forest farm — другие warps и obstacles.",
            "SVE IF2R/Grandpa's Farm — runtime патчи BusStop/Forest.",
            "Fork acceptWalk → Forest — re-setup после changeLocation.",
            "Storm comfort fork → Hospital — fade + warp.",
            "Не ставить NPC на FarmHouse door tile.",
            "Outdoor checkup — держать farmer на проходимых тайлах у дома.",
            "Morning checkup dating — короткие move, emote + pause.",
        ],
        "HarveyRoom": [
            "Needs map export — экспортировать HarveyRoom.tmx из игры.",
            "Маленькая комната — предпочитать static setup.",
            "Минимум advancedMove.",
            "Warp dest из Hospital: (6,12).",
            "checkup events manually verified — не менять без причины.",
            "Учитывать townInterior tileset (стены, кровать, стол).",
            "Камера: центр комнаты, без pan за bounds.",
            "После speak — faceDirection для интимного тона.",
        ],
        "Mine": [
            "Farmer rescue: (17,7); Harvey: (17,10).",
            "Запрещены move NPC с y ≥ 11.",
            "Warp (18,14)→Summit — не setup.",
            "Storm: farmer (15,5), Harvey warp (18,13).",
            "ViewportFollowPlayer=True.",
            "После rescue — changeLocation Hospital, не длинная сцена в Mine.",
            "Лестница y=9 проходима.",
            "Не блокировать узкие проходы (43,6), (18,14).",
            "SVE Load может менять runtime — сверять экспорт.",
            "C# BeginMineRescueWarp обязателен для rescue events.",
        ],
        "SkullCave": [
            "Farmer (5,5), Harvey (7,7) — audit OK.",
            "Не ставить на (7,9) warp Desert.",
            "Короткая сцена — quickQuestion fork.",
            "Мало места — static preferred.",
            "Vanilla map, SVE только EditMap warp.",
        ],
        "Forest": [
            "Storm: farmer (23,13), Harvey (35,13).",
            "E3: (50,13); E3B: (48,14).",
            "Rescue pickup (66,16), sprite (67,12).",
            "120×120 — короткие move, не длинный advancedMove без проверки.",
            "SVE canopy shadows — visual check.",
            "Warps на Town east, Farm north, Woods west.",
        ],
        "Woods": [
            "Rescue search (27,18), hideout (40,20).",
            "Front на (27,18) — visual overlap возможен.",
            "Большие open areas — но много Passable=F.",
            "Warps Forest/Custom_ForestWest — не setup.",
        ],
        "Mountain": [
            "Storm act 2: (79,1) после changeLocation с Summit.",
            "E4B: (44,21) перила — farmer +2 X.",
            "Узкие проходы у озера — avoid dual NPC.",
            "LoadMap Mine (103,16) — не путать с event coords.",
            "Warp Summit (78–80,-1).",
        ],
        "Town": [
            "Storm start (39,73); finale mine (72,22).",
            "E2B (28,67); E7 (26,22); E9 (35,88).",
            "E7: Harvey (27,22) Buildings — risky.",
            "CC vs Town_Joja — разная планировка.",
            "Saloon act 2 — changeLocation, re-warp в Saloon (14,23); см. tmpMap/vanilla/maps/Saloon.tmx.",
            "Клиника вход (36,55) LockedDoorWarp.",
            "Большая карта — viewport clamp вручную.",
        ],
        "Desert": [
            "Storm farmer (15,23).",
            "Harvey (17,26) Buildings — warp рядом, не на bus tile.",
            "DesertBus (18,27) — декор.",
            "Открытое пространство — storm comfort OK.",
        ],
        "BusStop": [
            "First meeting (19,23)/(27,23).",
            "E1 (20,23)/(26,22), viewport (52,24).",
            "ViewportClamp 10,0,35,30.",
            "⚠ eventHarveyCheckup (5,9) — target mismatch Hospital coords.",
            "Warps Farm west, Town east, Backwoods north.",
            "Не setup на warp tiles (9,22–25), (44,22–25).",
        ],
        "Beach": [
            "E4 pier (39,23).",
            "Exit move 0,-10 — проверить map edge/water.",
            "BrokenBeachBridge — не block path.",
            "Warp Town north (37–40,-1).",
            "eventHarveyPropose manually verified.",
        ],
        "Custom_AdventurerSummit": [
            "Storm act 1: farmer (41,27), Harvey (32,42).",
            "Long advancedMove — stopAdvancedMoves before changeLocation.",
            "Warp south (31–33,43)→Mountain.",
            "Warp (19,14)→Mine — не setup.",
            "Узкие тропы — проверить коллизии.",
            "SVE-only — нет vanilla fallback.",
        ],
    }
    return "\n".join(f"{i}. {r}" for i, r in enumerate(rules.get(loc, ["Сверить координаты с audit перед правкой."]), 1))


def render_no_tmx_passport(loc: str) -> str:
    events_used = [e[0] for e in MAP_EVENTS.get(loc, [])]
    st = "needs map export"
    src = source_kind(loc, "")
    lines = [
        f"# Map Passport: {loc}",
        "",
        "## 1. Metadata",
        f"- LocationName: {loc}",
        f"- Map asset: Maps/{loc}",
        "- Map file: **Needs map export / not available in repository**",
        f"- Source: {src}",
        f"- Status: {st}",
        "- Used by events:",
    ]
    for e in events_used:
        lines.append(f"  - {e}")
    lines += [
        "",
        "## 2. Map size and layers",
        "",
        "**Needs map export / not available in repository.**",
        "",
        "Экспорт: `debug export current` в игре на локации или из Content/Maps/*.xnb.",
        "",
        "## 3. Tilesets",
        "",
        "| Tileset name | firstgid | tile size | image source | external tsx | notes |",
        "|--------------|----------|-----------|--------------|--------------|-------|",
        "| — | — | — | — | — | Needs map export |",
        "",
        "## 4. Doors, warps, exits",
        "",
        "| X | Y | Type | Destination | Direction/use | Safe nearby tiles | Notes |",
        "|---|---|------|-------------|---------------|-------------------|-------|",
        "| — | — | — | — | — | — | Needs map export |",
        "",
        "## 5. Action / TouchAction tiles",
        "",
        "| X | Y | Layer | Property | Meaning | Event risk |",
        "|---|---|-------|----------|---------|------------|",
        "| — | — | — | — | Needs map export | — |",
        "",
        "## 6. Furniture and visual blockers",
        "",
        "| Object | Approx coords/range | Layer | Blocks movement? | Blocks visibility? | Notes |",
        "|--------|---------------------|-------|------------------|--------------------|-------|",
        "| — | — | — | — | — | Needs map export |",
        "",
        "## 7. Safe staging zones",
        "",
        "| Zone ID | Coordinates/range | Best for | Why safe | Camera suggestion | Notes |",
        "|---------|-------------------|----------|----------|-------------------|-------|",
    ]
    if loc == "Farm":
        lines.append("| `farmhouse_front_safe` | ~(64,15–18) Standard | onboarding, checkup | manually verified baseline | viewport farm house | **не подтверждено TMX** |")
    elif loc == "HarveyRoom":
        lines.append("| `harveyroom_center` | unknown | checkup | small interior | center room | warp from Hospital (6,12) |")
    else:
        lines.append("| — | — | — | — | — | Needs map export |")
    lines += [
        "",
        "## 8. Risk zones",
        "",
        "| Coords/range | Risk | Why dangerous | Avoid in event commands |",
        "|--------------|------|---------------|---------------------------|",
        f"| all fixed coords | procedural/unstable | нет TMX в repo | выдумывать координаты |" if loc == "Farm" else "| — | — | Needs map export | — |",
        "",
        "## 9. Recommended camera / viewport zones",
        "",
        camera_zones(loc),
        "",
        "## 10. Movement guidance",
        "",
        movement_guidance(loc, None, []),
        "",
        "## 11. Scene-specific notes",
        "",
        scene_notes(loc),
        "",
        "## 12. Events using this map",
        "",
        "| Event ID | File | Status | Coordinates checked? | Notes |",
        "|----------|------|--------|----------------------|-------|",
    ]
    for row in MAP_EVENTS.get(loc, []):
        lines.append(f"| {row[0]} | {row[1]} | {row[2]} | {row[3]} | {row[4]} |")
    lines += [
        "",
        "## 13. Quick rules for this map",
        "",
        quick_rules(loc),
        "",
        "---",
        "",
        "Метод: паспорт без TMX; координаты из audit/manual verify только.",
        "Не учтено: runtime SVE патчи, positionOffset.",
    ]
    return "\n".join(lines)


def render_tmx_passport(loc: str, md) -> str:
    walkable = compute_walkable(md)
    blocked = merge_buildings(md.buildings)
    rel = str(md.path.relative_to(ROOT) if md.path.is_relative_to(ROOT) else md.path).replace("\\", "/")
    events_used = [e[0] for e in MAP_EVENTS.get(loc, [])]
    st = status_for(loc, md.error)
    src = source_kind(loc, md.source_note)

    layer_names = [l for l in md.layers if not l.startswith("objectgroup:")]
    walk_count = sum(sum(row) for row in walkable)
    block_count = sum(sum(row) for row in blocked)

    lines = [
        f"# Map Passport: {loc}",
        "",
        "## 1. Metadata",
        f"- LocationName: {loc}",
        f"- Map asset: Maps/{loc}" + (" (Woods2.tmx → LocationName Woods)" if loc == "Woods" else ""),
        f"- Map file: `{rel}`",
        f"- Source: {src} — {md.source_note}",
        f"- Status: {st}",
        "- Used by events:",
    ]
    for e in events_used:
        lines.append(f"  - {e}")
    if EVENT_COORDS.get(loc):
        lines.append("- Audit coordinates:")
        for c in EVENT_COORDS[loc]:
            lines.append(f"  - {c}")

    lines += [
        "",
        "## 2. Map size and layers",
        f"- Width: {md.width}",
        f"- Height: {md.height}",
        f"- Tile size: {md.tilewidth}×{md.tileheight} px",
        "- Layers:",
    ]
    for ln in layer_names:
        lines.append(f"  - {ln}")
    lines.append(f"- Passable tiles (TMX): **{walk_count}** | Blocking Buildings: **{block_count}** | Passable=F: **{len(md.passable_f)}**")
    if md.map_props:
        skip = {"Warp", "Doors"}
        other = {k: v for k, v in md.map_props.items() if k not in skip}
        if other:
            lines.append("- Map properties (sample):")
            for k, v in sorted(other.items()):
                vv = v if len(v) < 100 else v[:97] + "..."
                lines.append(f"  - `{k}` = `{vv}`")

    tilesets = parse_tilesets(md.path)
    lines += [
        "",
        "## 3. Tilesets",
        "",
        "| Tileset name | firstgid | tile size | image source | external tsx | notes |",
        "|--------------|----------|-----------|--------------|--------------|-------|",
    ]
    if tilesets:
        for ts in tilesets[:15]:
            lines.append(
                f"| {ts['name']} | {ts['firstgid']} | {ts['size']} | `{ts['source']}` | {ts['external']} | {ts['notes']} |"
            )
        if len(tilesets) > 15:
            lines.append(f"| … | … | … | … | … | ещё {len(tilesets)-15} tilesets |")
    else:
        lines.append("| — | — | — | — | — | — |")

    lines += [
        "",
        "## 4. Doors, warps, exits",
        "",
        render_doors_warps(md, walkable, blocked),
        "",
        "## 5. Action / TouchAction tiles",
        "",
        render_action_tiles(md, walkable, blocked),
        "",
        "## 6. Furniture and visual blockers",
        "",
        render_furniture(md, walkable, blocked),
        "",
        "## 7. Safe staging zones",
        "",
        render_safe_zones(md, walkable),
        "",
        "## 8. Risk zones",
        "",
        render_risk_zones(md, walkable),
        "",
        "## 9. Recommended camera / viewport zones",
        "",
        camera_zones(loc),
        "",
        "## 10. Movement guidance",
        "",
        movement_guidance(loc, md, walkable),
        "",
        "## 11. Scene-specific notes",
        "",
        scene_notes(loc),
        "",
        "## 12. Events using this map",
        "",
        "| Event ID | File | Status | Coordinates checked? | Notes |",
        "|----------|------|--------|----------------------|-------|",
    ]
    for row in MAP_EVENTS.get(loc, []):
        lines.append(f"| {row[0]} | {row[1]} | {row[2]} | {row[3]} | {row[4]} |")

    lines += [
        "",
        "## 13. Quick rules for this map",
        "",
        quick_rules(loc),
        "",
        "---",
        "",
        "Метод: парсинг TMX (`width/height`, слои, Warp/Doors, Action, Buildings=коллизия).",
        "Не учтено: runtime `.tbin` патчи SVE, `positionOffset`, NPC vs Front collision.",
    ]
    return "\n".join(lines)


def render_index() -> str:
    lines = [
        "# Map Passports Index",
        "",
        "## Назначение",
        "",
        "Паспорта карт используются при проверке и правке **CP-событий** Harvey Overhaul: "
        "координаты farmer/Harvey/NPC, проходимость, двери и warp, мебель, safe staging zones, "
        "camera/viewport и рискованные зоны.",
        "",
        "Каждая локация — отдельный файл в [`maps/`](maps/). "
        "Метод генерации TMX-секций: `tmpMap/generate_map_passports.py` / `generate_split_map_passports.py`.",
        "",
        "**Не учтено в TMX:** runtime-патчи SVE (`Load`/`EditMap`), `positionOffset`, коллизии NPC с Front-тайлами.",
        "",
        "## Карты",
        "",
        "| Location | Passport file | Source | Status | Used by events | Notes |",
        "|----------|---------------|--------|--------|----------------|-------|",
    ]
    for loc in USER_MAPS:
        fname = FILE_NAMES.get(loc, f"{loc}.md")
        st, src_note, risk_tag, tmx = INDEX_META.get(loc, ("partial", "", "", ""))
        ev_count = len(MAP_EVENTS.get(loc, []))
        ev_sample = ", ".join(e[0] for e in MAP_EVENTS.get(loc, [])[:3])
        if ev_count > 3:
            ev_sample += f", … (+{ev_count-3})"
        notes = src_note
        if loc == "HarveyRoom":
            notes += "; hand-maintained; TMX needs export"
        if loc == "Custom_AdventurerSummit":
            notes += "; hand-maintained; SVE-only summit"
        if loc == "Farm":
            notes += "; visitor zone у farmhouse; hand-maintained passport"
        if loc == "Town":
            notes += "; alt Town_Joja.tmx"
        lines.append(
            f"| **{loc}** | [`maps/{fname}`](maps/{fname}) | {src_note} | **{st}** | {ev_sample} | {notes} |"
        )

    lines += [
        "",
        "### Связанные локации без отдельного паспорта",
        "",
        "| Location | Используется | Где смотреть |",
        "|----------|--------------|--------------|",
        "| **Saloon** | `eventHarveyStormComfortTown` (акт 2) | TMX: `tmpMap/vanilla/maps/Saloon.tmx`; staging **(14,23)**; warp exit (14,25)→Town |",
        "| **ArchaeologyHouse** | `HarveyOverhaulStory.E8_QuietShelf` | TMX: `tmpMap/sve/maps/Locations/ArchaeologyHouse.tmx`; farmer **(18,9)**; Harvey warp **(3,15)** (warp→Town) |",
        "| **Town_Joja** | альтернатива Town при Joja route | `tmpMap/sve/maps/Locations/Town_Joja.tmx`; см. [Town](maps/Town.md) |",
        "",
        "## Как использовать паспорт карты",
        "",
        "1. Открыть паспорт нужной карты из таблицы выше.",
        "2. Проверить стартовые координаты farmer / Harvey / NPC (§1, §12, Event ref в §7).",
        "3. Проверить movement path (§10) — Good paths / Avoid paths.",
        "4. Проверить doors / warp / action tiles (§4, §5) — не setup на warp/door.",
        "5. Проверить viewport / camera (§9) — clamp, края карты.",
        "6. Проверить визуальную логику сцены (§6, §11) — мебель, Front/AlwaysFront.",
        "7. Только потом править событие в `assets/Code/events*.json`.",
        "",
        "Чеклист ревью: [`cp-event-review-checklist.md`](cp-event-review-checklist.md).  ",
        "Правила авторинга: [`../EventPatterns/cp-event-authoring-rules.md`](../EventPatterns/cp-event-authoring-rules.md).",
        "",
        "## Статусы",
        "",
        "| Status | Значение |",
        "|--------|----------|",
        "| **ready** | TMX в repo, ключевые event coords проверены |",
        "| **partial** | TMX есть, но runtime/SVE может отличаться |",
        "| **needs map export** | Нет TMX в репозитории — координаты только из audit/manual |",
        "| **external vanilla** | Vanilla xnb/tmx, SVE минимально патчит |",
        "| **external SVE/custom** | SVE Load/EditMap, custom локации |",
        "| **risky/procedural** | Farm layouts / нестабильные coords |",
        "",
        "## Приоритет файлов TMX",
        "",
        "SVE TMX (если Load заменяет vanilla) → vanilla TMX. SkullCave — vanilla. Woods → `Woods2.tmx`.",
        "",
    ]
    return "\n".join(lines)


def main() -> None:
    MAPS_DIR.mkdir(parents=True, exist_ok=True)
    for loc in USER_MAPS:
        fname = FILE_NAMES.get(loc, f"{loc}.md")
        out = MAPS_DIR / fname
        if loc == "Farm":
            print(f"Skipped {out} (hand-maintained variable-layout passport)")
            continue
        if loc == "Town":
            print(f"Skipped {out} (hand-maintained detailed Town passport)")
            continue
        if loc == "Hospital":
            print(f"Skipped {out} (hand-maintained detailed Hospital passport)")
            continue
        if loc == "Forest":
            print(f"Skipped {out} (hand-maintained detailed Forest passport)")
            continue
        if loc == "Woods":
            print(f"Skipped {out} (hand-maintained detailed Woods passport)")
            continue
        if loc == "Mountain":
            print(f"Skipped {out} (hand-maintained detailed Mountain passport)")
            continue
        if loc == "Mine":
            print(f"Skipped {out} (hand-maintained detailed Mine passport)")
            continue
        if loc == "SkullCave":
            print(f"Skipped {out} (hand-maintained detailed SkullCave passport)")
            continue
        if loc == "Desert":
            print(f"Skipped {out} (hand-maintained detailed Desert passport)")
            continue
        if loc == "BusStop":
            print(f"Skipped {out} (hand-maintained detailed BusStop passport)")
            continue
        if loc == "Beach":
            print(f"Skipped {out} (hand-maintained detailed Beach passport)")
            continue
        if loc == "HarveyRoom":
            print(f"Skipped {out} (hand-maintained detailed HarveyRoom passport)")
            continue
        if loc == "Custom_AdventurerSummit":
            print(f"Skipped {out} (hand-maintained detailed Custom_AdventurerSummit passport)")
            continue
        elif loc in MAP_SOURCES:
            primary, note, fallback = MAP_SOURCES[loc]
            md = load_map(loc, primary, note, fallback)
            if md.error:
                text = render_no_tmx_passport(loc)
                # prepend error
                text = text.replace(
                    "**Needs map export / not available in repository.**",
                    f"**TMX error:** {md.error}\n\n**Needs map export / not available in repository.**",
                    1,
                )
            else:
                text = render_tmx_passport(loc, md)
        else:
            text = render_no_tmx_passport(loc)
        out.write_text(text, encoding="utf-8")
        print(f"Wrote {out}")

    print(f"Skipped {INDEX} (hand-maintained index — edit docs/CheckEvent/map-passports.md directly)")


if __name__ == "__main__":
    main()

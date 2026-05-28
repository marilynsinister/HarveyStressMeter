#!/usr/bin/env python3
"""Coordinate audit for unverified CP events."""

from __future__ import annotations

import base64
import json
import re
import struct
import xml.etree.ElementTree as ET
import zlib
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent
CP = Path(r"D:\Games\Steam\steamapps\common\Stardew Valley\Mods\HarveyOverhaul\HarveyOverhaul [CP]\assets\Code")
OUT = ROOT.parent / "docs" / "CheckEvent" / "events-coordinate-audit.md"

MAP_PATHS = {
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

AUDIT_META: dict[str, dict] = {
    "eventHarveyMineRescue": {"loc": "Mine → Hospital", "file": "eventsMineRescue.json", "pri": "High", "target": "Mine"},
    "eventHarveyMineRescueDating": {"loc": "Mine → Hospital", "file": "eventsMineRescue.json", "pri": "High", "target": "Mine"},
    "eventHarveyMinorMineRescue": {"loc": "Mine → Hospital", "file": "eventsMineRescue.json", "pri": "High", "target": "Mine"},
    "HarveyMod_FirstTreatment": {"loc": "Hospital", "file": "events.json", "pri": "Medium", "target": "Hospital"},
    "eventRescueOperation": {"loc": "Hospital → Woods → Forest → Hospital", "file": "events.json", "pri": "High", "target": "Woods"},
    "eventHarveyMineInterception": {"loc": "Mine", "file": "eventsCare.json", "pri": "High", "target": "Mine"},
    "eventHarveySkullCavePrevention": {"loc": "SkullCave", "file": "eventsCare.json", "pri": "Medium", "target": "SkullCave"},
    "eventHarveyStormComfortForest": {"loc": "Forest", "file": "events.json", "pri": "Medium", "target": "Forest"},
    "eventHarveyStormComfortMountain": {"loc": "Custom_AdventurerSummit → Mountain", "file": "events.json", "pri": "High", "target": "Custom_AdventurerSummit"},
    "eventHarveyStormComfortTown": {"loc": "Town → Saloon", "file": "events.json", "pri": "High", "target": "Town"},
    "eventHarveyStormComfortDesert": {"loc": "Desert", "file": "events.json", "pri": "Medium", "target": "Desert"},
    "eventHarveyStormComfortMine": {"loc": "Mine → Town", "file": "events.json", "pri": "High", "target": "Mine"},
    "HarveyOverhaulStory.E1_SlipperyPath": {"loc": "BusStop", "file": "events.json", "pri": "High", "target": "BusStop"},
    "HarveyOverhaulStory.E2_InsistentExam": {"loc": "Hospital", "file": "events.json", "pri": "High", "target": "Hospital"},
    "HarveyOverhaulStory.E2B_QuietAgreement": {"loc": "Town", "file": "events.json", "pri": "Medium", "target": "Town"},
    "HarveyOverhaulStory.E3_ForestApothecary": {"loc": "Forest", "file": "events.json", "pri": "Medium", "target": "Forest"},
    "HarveyOverhaulStory.E3B_WingPatient": {"loc": "Forest", "file": "events.json", "pri": "Medium", "target": "Forest"},
    "HarveyOverhaulStory.E4_PierBreath": {"loc": "Beach", "file": "events.json", "pri": "Medium", "target": "Beach"},
    "HarveyOverhaulStory.E4B_TooQuiet": {"loc": "Mountain", "file": "events.json", "pri": "Medium", "target": "Mountain"},
    "HarveyOverhaulStory.E5_StormBeside": {"loc": "Hospital", "file": "events.json", "pri": "High", "target": "Hospital"},
    "HarveyOverhaulStory.E6_SayItOutLoud": {"loc": "Hospital", "file": "events.json", "pri": "High", "target": "Hospital"},
    "HarveyOverhaulStory.E7_TownSip_Sunny": {"loc": "Town", "file": "events.json", "pri": "High", "target": "Town"},
    "HarveyOverhaulStory.E8_QuietShelf": {"loc": "ArchaeologyHouse", "file": "events.json", "pri": "High", "target": "ArchaeologyHouse"},
    "HarveyOverhaulStory.E9_LightInWindow": {"loc": "Town", "file": "events.json", "pri": "Medium", "target": "Town"},
    "eventHarveyFirstMeeting": {"loc": "BusStop", "file": "eventsCare.json", "pri": "Medium", "target": "BusStop"},
    "eventHarveyCheckup": {"loc": "BusStop ⚠", "file": "eventsCare.json", "pri": "High", "target": "BusStop"},
    "eventHarveyMedicalCheck_Dating": {"loc": "Hospital", "file": "events.json", "pri": "High", "target": "Hospital"},
    "HarveyMod_NightCrisis_Dating": {"loc": "Hospital", "file": "events.json", "pri": "Medium", "target": "Hospital"},
    "HarveyMod_NightCrisis_PreDating": {"loc": "Hospital", "file": "events.json", "pri": "Medium", "target": "Hospital"},
    "HarveyMod_BirthdayHospital_Dating": {"loc": "Hospital", "file": "events.json", "pri": "Low", "target": "Hospital"},
    "HarveyMod_BirthdayHospital_Friend": {"loc": "Hospital", "file": "events.json", "pri": "Low", "target": "Hospital"},
}


@dataclass
class MapState:
    width: int = 0
    height: int = 0
    walkable: list[list[bool]] = field(default_factory=list)
    buildings: list[list[bool]] = field(default_factory=list)
    front: list[list[bool]] = field(default_factory=list)
    warps: list[tuple[int, int, str]] = field(default_factory=list)
    objects: list[dict] = field(default_factory=list)


def strip_json_comments(text: str) -> str:
    return re.sub(r"//[^\n]*", "", text)


def load_json(path: Path) -> dict:
    return json.loads(strip_json_comments(path.read_text(encoding="utf-8")))


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


def load_map(location: str) -> MapState | None:
    path = MAP_PATHS.get(location)
    if not path or not path.exists():
        return None
    root = ET.parse(path).getroot()
    w, h = int(root.get("width")), int(root.get("height"))
    ms = MapState(width=w, height=h)
    back = [[0] * w for _ in range(h)]
    bld: dict[str, list[list[int]]] = {}
    fr: dict[str, list[list[int]]] = {}
    for layer in root.findall("layer"):
        name = layer.get("name", "")
        d = layer.find("data")
        if d is None or not d.text:
            continue
        grid = decode_layer(d, w, h)
        g2 = [grid[i * w : (i + 1) * w] for i in range(h)]
        if name == "Back":
            back = g2
        elif "Building" in name:
            bld[name] = g2
        elif name.startswith("Front") or name.startswith("AlwaysFront") or name == "Back2":
            fr[name] = g2
    blocked = [[False] * w for _ in range(h)]
    for g in bld.values():
        for y in range(h):
            for x in range(w):
                if g[y][x]:
                    blocked[y][x] = True
    pf = set()
    for ts in root.findall("tileset"):
        firstgid = int(ts.get("firstgid", 1))
        for tile in ts.findall("tile"):
            tid = int(tile.get("id", 0))
            props = {p.get("name"): (p.get("value") or p.text or "") for p in tile.findall("properties/property")}
            if props.get("Passable") == "F":
                pf.add(firstgid + tid)
    walk = [[False] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            if back[y][x] == 0 or blocked[y][x]:
                continue
            if back[y][x] in pf:
                continue
            walk[y][x] = True
    front = [[False] * w for _ in range(h)]
    for g in fr.values():
        for y in range(h):
            for x in range(w):
                if g[y][x]:
                    front[y][x] = True
    ms.walkable = walk
    ms.buildings = blocked
    ms.front = front
    props_el = root.find("properties")
    if props_el is not None:
        for p in props_el.findall("property"):
            if p.get("name") == "Warp":
                val = p.get("value") or p.text or ""
                parts = val.split()
                i = 0
                while i + 4 <= len(parts):
                    try:
                        wx, wy = int(parts[i]), int(parts[i + 1])
                        ms.warps.append((wx, wy, parts[i + 2]))
                        i += 5
                    except ValueError:
                        break
    for og in root.findall("objectgroup"):
        for obj in og.findall("object"):
            tx = int(float(obj.get("x", 0)) // 16)
            ty = int(float(obj.get("y", 0)) // 16)
            op = {p.get("name"): (p.get("value") or p.text or "") for p in obj.findall("properties/property")}
            act = op.get("Action") or op.get("TouchAction") or ""
            if act:
                ms.objects.append({"x": tx, "y": ty, "action": act})
    return ms


MAP_CACHE: dict[str, MapState | None] = {}


def get_map(loc: str) -> MapState | None:
    key = loc.split(" →")[0].split(" ")[0].strip()
    if key not in MAP_CACHE:
        MAP_CACHE[key] = load_map(key)
    return MAP_CACHE[key]


def check_tile(loc: str, x: int, y: int, ignore_collisions: bool = False) -> dict:
    m = get_map(loc)
    if m is None:
        return {"result": "Needs map export", "detail": "нет TMX"}
    if x >= 500 or y >= 500:
        return {"result": "OK", "detail": "off-screen staging (1000,1000)"}
    if not (0 <= x < m.width and 0 <= y < m.height):
        return {"result": "Broken", "detail": f"вне карты ({m.width}×{m.height})"}
    w, b, f = m.walkable[y][x], m.buildings[y][x], m.front[y][x]
    warp = any(wx == x and wy == y for wx, wy, _ in m.warps)
    detail_parts = []
    if b:
        detail_parts.append("Buildings")
    if f:
        detail_parts.append("Front")
    if warp:
        detail_parts.append("Warp")
    if w:
        detail_parts.append("Back проходим")
    else:
        detail_parts.append("Back непроходим")
    if ignore_collisions:
        res = "OK (ignoreCollisions)"
    elif b and not ignore_collisions:
        res = "Broken"
    elif not w and not ignore_collisions:
        res = "Broken"
    elif f:
        res = "Warning"
    elif warp:
        res = "Warning"
    else:
        res = "OK"
    return {"result": res, "detail": ", ".join(detail_parts)}


def nearby_risks(loc: str, x: int, y: int) -> str:
    m = get_map(loc)
    if not m:
        return "—"
    if not (0 <= x < m.width and 0 <= y < m.height):
        return "координата вне карты"
    risks = []
    for ox, oy, dest in m.warps:
        if abs(ox - x) <= 1 and abs(oy - y) <= 1:
            risks.append(f"Warp→{dest} ({ox},{oy})")
    for o in m.objects:
        if abs(o["x"] - x) <= 1 and abs(o["y"] - y) <= 1:
            a = o["action"][:40]
            risks.append(f"Action {a} ({o['x']},{o['y']})")
    if m.buildings[y][x]:
        risks.append("Buildings на тайле")
    if m.front[y][x]:
        risks.append("Front/AlwaysFront на тайле")
    return "; ".join(risks[:4]) if risks else "—"


def parse_script(script: str) -> list[str]:
    # naive split — CP scripts rarely have unescaped / in strings
    parts = [p.strip() for p in script.split("/") if p.strip()]
    return parts


def extract_event_id(key: str) -> str:
    return key.split("/")[0].strip()


def extract_string_at(text: str, quote_pos: int) -> tuple[str, int]:
    i = quote_pos + 1
    chars: list[str] = []
    while i < len(text):
        c = text[i]
        if c == "\\" and i + 1 < len(text):
            n = text[i + 1]
            if n == "n":
                chars.append("\n")
            elif n == '"':
                chars.append('"')
            elif n == "\\":
                chars.append("\\")
            else:
                chars.append(n)
            i += 2
        elif c == '"':
            return "".join(chars), i + 1
        else:
            chars.append(c)
            i += 1
    return "", i


def find_event_entry(text: str, eid: str) -> tuple[str, str, int] | None:
    """Return (key, script, match_start) or None."""
    start = text.find('"' + eid)
    if start < 0:
        return None
    candidates: list[tuple[int, str]] = []
    for marker in ('""": "', '": "'):
        pos = start
        while True:
            kpos = text.find(marker, pos)
            if kpos < 0 or kpos <= start:
                break
            vq = kpos + len(marker) - 1
            script, end = extract_string_at(text, vq)
            if script and ("/" in script or script.split()[0] in {"none", "continue", "spring_day_ambient", "Hospital_Ambient", "nightTime", "rain", "thunder_small", "EarthMine"}):
                key = text[start + 1 : kpos + (1 if marker == '": "' else 2)]
                candidates.append((kpos, key, script))
            pos = kpos + 1
    if not candidates:
        return None
    # prefer earliest valid key-value boundary
    kpos, key, script = min(candidates, key=lambda t: t[0])
    return key, script, start


def collect_cp_events() -> dict[str, dict]:
    found: dict[str, dict] = {}
    for fname in ("events.json", "eventsCare.json", "eventsMineRescue.json"):
        text = strip_json_comments((CP / fname).read_text(encoding="utf-8"))
        for eid in AUDIT_META:
            if eid in found:
                continue
            entry = find_event_entry(text, eid)
            if not entry:
                continue
            key, script, mstart = entry
            chunk_start = max(0, mstart - 12000)
            chunk = text[chunk_start : mstart]
            targets = re.findall(r'"Target":\s*"(Data/Events/[^"]+)"', chunk)
            target = targets[-1] if targets else "?"
            patch_loc = target.replace("Data/Events/", "")
            if patch_loc == "?":
                meta_loc = AUDIT_META[eid]["target"]
                patch_loc = meta_loc.split(" →")[0].split(" ")[0].replace("⚠", "").strip()
            found[eid] = {
                "script": script.strip(),
                "target": patch_loc,
                "key": key,
                "file": fname,
            }
    return found


@dataclass
class AuditResult:
    event_id: str
    coords: list[dict] = field(default_factory=list)
    moves: list[dict] = field(default_factory=list)
    problems: list[tuple[str, str]] = field(default_factory=list)
    recommendations: list[str] = field(default_factory=list)
    status: str = "OK"


def simulate_event(eid: str, script: str, patch_target: str) -> AuditResult:
    ar = AuditResult(event_id=eid)
    parts = parse_script(script)
    loc = patch_target
    positions: dict[str, tuple[int, int]] = {}
    facing: dict[str, int] = {}
    ignore: set[str] = set()
    viewport = None
    i = 0
    high = medium = low = 0

    def actor_pos(name: str) -> tuple[int, int] | None:
        return positions.get(name)

    def record_coord(cmd: str, actor: str, x: int, y: int, note: str = "") -> None:
        chk = check_tile(loc, x, y, actor in ignore)
        ar.coords.append({
            "cmd": cmd, "actor": actor, "x": x, "y": y,
            "check": chk["detail"], "result": chk["result"], "note": note, "loc": loc,
        })
        if chk["result"] == "Broken":
            ar.problems.append(("High", f"{cmd} {actor} ({x},{y}) на {loc}: {chk['detail']}"))
        elif chk["result"] == "Warning":
            ar.problems.append(("Medium", f"{cmd} {actor} ({x},{y}) на {loc}: {chk['detail']}"))
        elif chk["result"] == "Needs map export":
            ar.problems.append(("Medium", f"Нет TMX для {loc}"))

    def record_move(actor: str, fx: int, fy: int, cmd: str, tx: int, ty: int) -> None:
        path_ok = True
        details = []
        if fx == tx and fy == ty:
            details.append("нет смещения")
        else:
            steps = max(abs(tx - fx), abs(ty - fy))
            for s in range(1, steps + 1):
                sx = fx + round((tx - fx) * s / steps)
                sy = fy + round((ty - fy) * s / steps)
                c = check_tile(loc, sx, sy, actor in ignore)
                if c["result"] in ("Broken", "Needs map export"):
                    path_ok = False
                    details.append(f"({sx},{sy})={c['result']}")
        res = "OK" if path_ok else "Warning"
        if "advancedMove" in cmd:
            res = "Warning"
            details.append("advancedMove — проверить в игре")
        ar.moves.append({
            "actor": actor, "frm": f"({fx},{fy})", "cmd": cmd,
            "to": f"({tx},{ty})", "path": "; ".join(details) or "прямой", "result": res, "loc": loc,
        })
        if not path_ok:
            ar.problems.append(("Medium", f"Путь {actor} {cmd}: {'; '.join(details)}"))

    # parse header
    while i < len(parts):
        p = parts[i]
        tokens = p.split()
        if not tokens:
            i += 1
            continue
        cmd = tokens[0]

        # setup: farmer X Y D [npc...] OR Harvey X Y D only
        if tokens[0] == "farmer" and len(tokens) >= 4:
            positions["farmer"] = (int(tokens[1]), int(tokens[2]))
            facing["farmer"] = int(tokens[3])
            j = 4
            while j + 3 < len(tokens):
                name = tokens[j]
                positions[name] = (int(tokens[j + 1]), int(tokens[j + 2]))
                facing[name] = int(tokens[j + 3])
                j += 4
            for name, (x, y) in positions.items():
                if x < 500 and y < 500:
                    record_coord("setup", name, x, y, "стартовая позиция")
            i += 1
            continue
        if tokens[0] in ("Harvey", "Lewis", "Maru", "Penny", "Gus", "Gunther") and len(tokens) >= 4:
            name = tokens[0]
            positions[name] = (int(tokens[1]), int(tokens[2]))
            facing[name] = int(tokens[3])
            x, y = positions[name]
            if x < 500 and y < 500:
                record_coord("setup", name, x, y, "стартовая позиция")
            i += 1
            continue

        # viewport center line: two integers (not a command)
        if len(tokens) == 2 and tokens[0].lstrip("-").isdigit() and tokens[1].lstrip("-").isdigit():
            try:
                viewport = (int(tokens[0]), int(tokens[1]))
            except ValueError:
                pass
            i += 1
            continue

        if cmd == "changeLocation" and len(tokens) >= 2:
            loc = tokens[1]
            i += 1
            continue

        if cmd == "ignoreCollisions" and len(tokens) >= 2:
            ignore.add(tokens[1])
            i += 1
            continue

        if cmd == "warp":
            if len(tokens) == 3 and tokens[1].lstrip("-").isdigit():
                actor, x, y = "farmer", int(tokens[1]), int(tokens[2])
            elif len(tokens) >= 4 and tokens[2].lstrip("-").isdigit():
                actor, x, y = tokens[1], int(tokens[2]), int(tokens[3])
            else:
                i += 1
                continue
            positions[actor] = (x, y)
            record_coord("warp", actor, x, y)
            i += 1
            continue

        if cmd == "move" and len(tokens) >= 4:
            actor = tokens[1]
            dx, dy = int(tokens[2]), int(tokens[3])
            fx, fy = positions.get(actor, (0, 0))
            tx, ty = fx + dx, fy + dy
            record_move(actor, fx, fy, p, tx, ty)
            positions[actor] = (tx, ty)
            if len(tokens) >= 5:
                facing[actor] = int(tokens[4])
            i += 1
            continue

        if cmd == "advancedMove":
            actor = tokens[1] if len(tokens) > 1 else "farmer"
            fx, fy = positions.get(actor, (0, 0))
            nums = [int(x) for x in tokens[3:] if x.lstrip("-").isdigit()]
            tx, ty = fx, fy
            # accumulate pairs skipping facing-like every 3rd if pattern
            idx = 0
            while idx + 1 < len(nums):
                tx += nums[idx]
                ty += nums[idx + 1]
                idx += 3 if idx + 2 < len(nums) and nums[idx + 2] in (0, 1, 2, 3) else 2
            record_move(actor, fx, fy, p[:60], tx, ty)
            positions[actor] = (tx, ty)
            ar.problems.append(("Medium", f"advancedMove {actor}: финальная точка ({tx},{ty}) — проверить траекторию в игре"))
            i += 1
            continue

        if cmd == "viewport":
            if len(tokens) >= 2 and tokens[1] == "move":
                i += 1
                continue
            if len(tokens) >= 3 and tokens[1].lstrip("-").isdigit():
                viewport = (int(tokens[1]), int(tokens[2]))
                vx, vy = viewport
                if vx >= 0 and vy >= 0:
                    c = check_tile(loc, vx, vy, True)
                    if c["result"] == "Broken":
                        ar.problems.append(("Medium", f"viewport ({vx},{vy}) на {loc}: {c['detail']}"))
            i += 1
            continue

        if cmd == "faceDirection" and len(tokens) >= 3:
            actor, d = tokens[1], int(tokens[2])
            facing[actor] = d
            i += 1
            continue

        if cmd == "positionOffset":
            i += 1
            continue

        if cmd == "doAction" and len(tokens) >= 3:
            x, y = int(tokens[1]), int(tokens[2])
            record_coord("doAction", "—", x, y, "door/action tile")
            i += 1
            continue

        if cmd == "addTemporaryActor" and len(tokens) >= 5:
            name = tokens[1]
            x, y = int(tokens[3]), int(tokens[4])
            positions[name] = (x, y)
            record_coord("addTemporaryActor", name, x, y)
            i += 1
            continue

        if cmd == "temporaryAnimatedSprite" and len(tokens) >= 3:
            try:
                x, y = int(tokens[-2]), int(tokens[-1])
                record_coord("temporaryAnimatedSprite", "sprite", x, y)
            except ValueError:
                pass
            i += 1
            continue

        if cmd in ("animate", "showFrame", "speak", "emote", "message", "pause", "quickQuestion", "question"):
            i += 1
            continue

        if cmd == "end" and "position" in p:
            m = re.search(r"position\s+(\d+)\s+(\d+)", p)
            if m:
                x, y = int(m.group(1)), int(m.group(2))
                record_coord("end position", "farmer", x, y)
            i += 1
            continue

        i += 1

    # Target mismatch
    meta = AUDIT_META.get(eid, {})
    if eid == "eventHarveyCheckup" and meta.get("target") == "BusStop":
        ar.problems.append(("High", "Target Data/Events/BusStop, но координаты и viewport (5,9) как Hospital — рассинхрон локации"))
        ar.recommendations.append("Либо перенести патч в Data/Events/Hospital, либо заменить все координаты на BusStop (напр. старт farmer ~20,23 / Harvey ~26,22 по E1)")

    # Specific known issues from passports
    if eid == "HarveyMod_FirstTreatment":
        c59 = check_tile("Hospital", 5, 9)
        if c59["result"] == "Broken":
            ar.recommendations.append("Старт farmer (5,9): заменить на (4,6) или (6,10) — проходимые тайлы у кушетки (map-passports)")

    if eid == "eventHarveyStormComfortMountain":
        c791 = check_tile("Mountain", 79, 1)
        if c791.get("result"):
            ar.problems.append(("Medium", f"Mountain warp (79,1): {c791['detail']} — край карты, SVE warp с Summit"))

    if eid in ("eventHarveyMineRescue", "eventHarveyMineRescueDating", "eventRescueOperation"):
        ar.problems.append(("Low", "warp farmer (20,5) + positionOffset + ignoreCollisions — палата, Buildings ожидаем; Warning в TMX, OK с animate лёжа"))

    # Status
    if any(s == "High" for s, _ in ar.problems):
        ar.status = "Broken"
    elif any(s == "Medium" for s, _ in ar.problems):
        ar.status = "Warning"
    elif any(c["result"] == "Needs map export" for c in ar.coords):
        ar.status = "Needs map export"
    else:
        ar.status = "OK"

    # Face direction hints
    if len(positions) >= 2 and "Harvey" in positions and "farmer" in positions:
        hx, hy = positions["Harvey"]
        fx, fy = positions["farmer"]
        if hx == fx and hy == fy:
            ar.problems.append(("High", "Harvey и farmer на одном тайле в конце сценария"))

    return ar


def render_event(eid: str, data: dict, ar: AuditResult) -> str:
    meta = AUDIT_META[eid]
    lines = [f"## {eid}", ""]
    lines.append(f"- **Локация:** {meta['loc']}")
    lines.append(f"- **Файл:** `{data['file']}` → `{data['target']}`")
    lines.append(f"- **Приоритет:** {meta['pri']}")
    lines.append(f"- **Статус:** **{ar.status}**")
    lines.append("")

    lines.append("### Использованные координаты")
    lines.append("")
    lines.append("| Команда | Actor | X | Y | Loc | Проверка тайла | Результат |")
    lines.append("|---------|-------|---|---|-----|----------------|-----------|")
    if ar.coords:
        for c in ar.coords:
            lines.append(f"| {c['cmd']} | {c['actor']} | {c['x']} | {c['y']} | {c['loc']} | {c['check']} | {c['result']} |")
    else:
        lines.append("| — | — | — | — | — | — | — |")
    lines.append("")

    lines.append("### Движение")
    lines.append("")
    lines.append("| Actor | From | Move command | To | Проходимость пути | Результат |")
    lines.append("|-------|------|--------------|-----|-------------------|-----------|")
    if ar.moves:
        for m in ar.moves[:25]:
            cmd = m["cmd"].replace("|", "\\|")
            if len(cmd) > 50:
                cmd = cmd[:47] + "..."
            lines.append(f"| {m['actor']} | {m['frm']} | {cmd} | {m['to']} | {m['path']} | {m['result']} |")
        if len(ar.moves) > 25:
            lines.append(f"| … | … | … | … | ещё {len(ar.moves)-25} move | … |")
    else:
        lines.append("| — | — | — | — | — | — |")
    lines.append("")

    lines.append("### Объекты рядом")
    lines.append("")
    lines.append("| Координата | Loc | Что рядом | Риск |")
    lines.append("|------------|-----|-----------|------|")
    seen = set()
    for c in ar.coords:
        k = (c["loc"], c["x"], c["y"])
        if k in seen:
            continue
        seen.add(k)
        risk = nearby_risks(c["loc"], c["x"], c["y"])
        lines.append(f"| ({c['x']},{c['y']}) | {c['loc']} | {risk} | {c['result']} |")
    lines.append("")

    lines.append("### Проблемы")
    lines.append("")
    if ar.problems:
        for sev, msg in ar.problems:
            lines.append(f"- **[{sev}]** {msg}")
    else:
        lines.append("- Нет проблем по TMX (runtime/SVE-патчи всё ещё проверить в игре).")
    lines.append("")

    lines.append("### Рекомендации")
    lines.append("")
    if ar.recommendations:
        for r in ar.recommendations:
            lines.append(f"- {r}")
    else:
        if ar.status == "OK":
            lines.append("- Координаты согласуются с TMX; финальная проверка — один прогон в игре с SVE.")
        elif ar.status == "Warning":
            lines.append("- Провести in-game тест на runtime-карте после SVE Load.")
        else:
            lines.append("- Исправить координаты/target перед in-game тестом (см. проблемы выше).")
    lines.append("")
    return "\n".join(lines)


def main() -> None:
    events = collect_cp_events()
    missing = set(AUDIT_META) - set(events)
    results: list[tuple[str, dict, AuditResult]] = []
    for eid in AUDIT_META:
        if eid not in events:
            continue
        data = events[eid]
        ar = simulate_event(eid, data["script"], data["target"])
        results.append((eid, data, ar))

    summary = {"OK": 0, "Warning": 0, "Broken": 0, "Needs map export": 0}
    for _, _, ar in results:
        summary[ar.status] = summary.get(ar.status, 0) + 1

    parts = [
        "# Аудит координат CP-событий (техническая постановка)",
        "",
        "Анализ **непроверенных** событий из [`events-map-audit-plan.md`](events-map-audit-plan.md).",
        "Сверка с [`map-passports.md`](map-passports.md) и TMX в `tmpMap/`. **События не изменялись.**",
        "",
        f"**Проверено событий:** {len(results)} / {len(AUDIT_META)}",
        f"**Статусы:** OK={summary['OK']}, Warning={summary['Warning']}, Broken={summary['Broken']}, Needs map export={summary.get('Needs map export', 0)}",
        "",
    ]
    if missing:
        parts.append(f"**Не найдено в CP JSON:** {', '.join(sorted(missing))}")
        parts.append("")

    parts.append("## Сводная таблица")
    parts.append("")
    parts.append("| Event ID | Target | Приоритет | Статус |")
    parts.append("|----------|--------|-----------|--------|")
    for eid, data, ar in results:
        parts.append(f"| {eid} | {data['target']} | {AUDIT_META[eid]['pri']} | **{ar.status}** |")
    parts.append("")
    parts.append("---")
    parts.append("")

    order = {"Broken": 0, "Warning": 1, "Needs map export": 2, "OK": 3}
    results.sort(key=lambda t: (order.get(t[2].status, 9), t[0]))
    for eid, data, ar in results:
        parts.append(render_event(eid, data, ar))
        parts.append("---")
        parts.append("")

    parts.append("## Методология")
    parts.append("")
    parts.append("- Проходимость: Back≠0, Buildings=0, без Passable=F (TMX SVE/vanilla).")
    parts.append("- `ignoreCollisions` учитывается для farmer в mine-rescue палате.")
    parts.append("- `advancedMove` — эвристика конечной точки + **Warning** (нужен in-game).")
    parts.append("- Saloon — vanilla TMX; SVE `.tbin` может отличаться.")
    parts.append("- SkullCave/Mine — фиксированные координаты на входе, не процедурные этажи.")
    parts.append("")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(parts), encoding="utf-8")
    print(f"Wrote {OUT} ({len(results)} events)")


if __name__ == "__main__":
    main()

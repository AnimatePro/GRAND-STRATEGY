#!/usr/bin/env python3
"""Генератор реальных данных мира для GRAND-STRATEGY (аналог MapImporterTool на Python).

Вход: data/source/ne_10m_admin_0_countries.geojson (страны) и
      data/source/ne_10m_admin_1_states_provinces.geojson (провинции/регионы) — Natural Earth 10m.
Выход: data/cache/world.json + id_map.png + border_mask.png (схема = WorldFileDto в C#).

Структура world.json должна совпадать с C# WorldFileDto (PascalCase, т.к. System.Text.Json
с IncludeFields=true сериализует поля по именам).
"""
import json, math, os
from collections import defaultdict
import numpy as np
from PIL import Image, ImageDraw

SCALE = 16.0
W = int(360 * SCALE)
H = int(180 * SCALE)

ADMIN0 = "data/source/ne_10m_admin_0_countries.geojson"
ADMIN1 = "data/source/ne_10m_admin_1_states_provinces.geojson"
POPULATED = "data/source/ne_10m_populated_places.geojson"
OUT_DIR = "data/cache"

# --- Палитра стран (совпадает с C# CountryPalette) ---
PALETTE = [
    "#4E79A7", "#F28E2B", "#E15759", "#76B7B2", "#59A14F",
    "#EDC948", "#B07AA1", "#FF9DA7", "#9C755F", "#BAB0AC",
    "#6B8E23", "#7B68EE", "#CD853F", "#4682B4", "#C71585",
    "#2E8B57", "#8B0000", "#DAA520", "#4169E1", "#A0522D",
]

CONTINENT_MAP = {
    "Africa": 0, "Asia": 1, "Europe": 2, "North America": 3,
    "South America": 4, "Oceania": 5, "Antarctica": 6, "Seven seas (open ocean)": 7,
}

INCOME_PC = {
    "1. High income: OECD": 40000,
    "2. High income: nonOECD": 30000,
    "3. Upper middle income": 9000,
    "4. Lower middle income": 3000,
    "5. Low income": 1200,
}


def color_for(code):
    h = 0
    for ch in code:
        h = (h * 31 + ord(ch)) & 0x7FFFFFFF
    return PALETTE[h % len(PALETTE)]


def climate_from_lat(lat):
    a = abs(lat)
    if a < 23.5:
        return 0  # Tropical
    if a < 35:
        return 1  # Arid
    if a < 55:
        return 2  # Temperate
    if a < 66.5:
        return 3  # Continental
    return 4  # Polar


def to_int(value, default=0):
    if value is None:
        return default
    if isinstance(value, str):
        try:
            return int(float(value))
        except (ValueError, TypeError):
            return default
    if isinstance(value, (int, float)):
        if value < 0:
            return default
        return int(value)
    return default


def to_float(value, default=0.0):
    if value is None:
        return default
    if isinstance(value, str):
        try:
            return float(value)
        except (ValueError, TypeError):
            return default
    if isinstance(value, (int, float)):
        if value < 0:
            return default
        return float(value)
    return default


def parse_geometry(geom):
    """Возвращает список полигонов; каждый полигон — список колец; кольцо — список (lon, lat)."""
    gtype = geom.get("type")
    coords = geom.get("coordinates")
    if gtype == "Polygon":
        return [coords]
    if gtype == "MultiPolygon":
        return coords
    return []


def ring_area_px(ring):
    """Площадь кольца (шулейс) в проекционных пикселях (со знаком)."""
    n = len(ring)
    if n < 3:
        return 0.0
    s = 0.0
    for i in range(n):
        x1 = (ring[i][0] + 180.0) * SCALE
        y1 = (90.0 - ring[i][1]) * SCALE
        x2 = (ring[(i + 1) % n][0] + 180.0) * SCALE
        y2 = (90.0 - ring[(i + 1) % n][1]) * SCALE
        s += x1 * y2 - x2 * y1
    return s / 2.0


def feature_area_km2(polygons):
    """Площадь геометрии в км²: сумма знаковых площадей колец (дырки вычитаются)."""
    area_px = 0.0
    for poly in polygons:
        for ring in poly:
            area_px += ring_area_px(ring)
    km2 = abs(area_px) / (SCALE * SCALE) * (111.32 * 111.32)
    return km2


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    # --- Страны (admin-0) ---
    d0 = json.load(open(ADMIN0, encoding="utf-8"))
    countries = []
    code_to_cid = {}
    for f in d0["features"]:
        p = f["properties"]
        code = (p.get("ADM0_A3") or p.get("ISO_A3") or "").strip()
        if not code or code == "-99":
            continue
        name = p.get("NAME") or p.get("NAME_LONG") or code
        name_ru = p.get("NAME_RU") or p.get("NAME") or name
        pop = to_int(p.get("POP_EST"), 0)

        gdp = to_float(p.get("GDP_MD_EST"), -1)
        if gdp < 0:
            gdp = to_float(p.get("GDP_MD"), -1)
        if gdp < 0:
            gdp = pop * INCOME_PC.get(p.get("INCOME_GRP"), 5000)

        continent = CONTINENT_MAP.get(p.get("CONTINENT"), 0)
        cid = len(countries)
        code_to_cid[code] = cid
        countries.append({
            "Id": cid,
            "Code": code,
            "NameKey": name,
            "NameRu": name_ru,
            "FlagId": code.lower(),
            "ColorHex": color_for(code),
            "GovernmentType": 1,  # Republic
            "Ideology": 0,        # None
            "CapitalProvinceId": -1,
            "Population": pop,
            "Gdp": gdp,
            "Literacy": 0.5,
            "Urbanization": 0.5,
            "Treasury": max(gdp * 0.02, 1000.0),
            "Continent": continent,
        })
    print(f"countries: {len(countries)}")

    # ВВП на душу населения (для развития провинций).
    gdp_pc = {}
    for c in countries:
        pop = c["Population"] if c["Population"] > 0 else 1
        gdp_pc[c["Code"]] = c["Gdp"] / pop

    # --- Провинции (admin-1) + растеризация ---
    d1 = json.load(open(ADMIN1, encoding="utf-8"))
    img = Image.new("RGB", (W, H), (0, 0, 0))
    draw = ImageDraw.Draw(img)

    provinces = []
    area_by_country = defaultdict(list)
    skipped = defaultdict(int)

    for f in d1["features"]:
        p = f["properties"]
        code = (p.get("adm0_a3") or p.get("ADM0_A3") or "").strip()
        if not code or code == "-99" or code not in code_to_cid:
            skipped[code or "(empty)"] += 1
            continue

        polygons = parse_geometry(f["geometry"])
        if not polygons:
            continue

        pid = len(provinces)
        cid = code_to_cid[code]

        # Центроид: метка Natural Earth (lat/lon), иначе bbox-центр.
        lon = p.get("longitude")
        lat = p.get("latitude")
        if lon is None or lat is None:
            bbox = f.get("bbox")
            if bbox:
                lon = (bbox[0] + bbox[2]) / 2.0
                lat = (bbox[1] + bbox[3]) / 2.0
            else:
                first = polygons[0][0]
                lon = sum(c[0] for c in first) / len(first)
                lat = sum(c[1] for c in first) / len(first)

        cx = (lon + 180.0) * SCALE
        cy = (90.0 - lat) * SCALE

        # Площадь по геометрии (area_sqkm в Natural Earth 10m пустое).
        area = feature_area_km2(polygons)
        if area <= 0:
            area = 1.0

        name_en = p.get("name_en") or p.get("name") or ""
        name_ru = p.get("name_ru") or name_en

        # Развитие и инфраструктура — из ВВП на душу населения страны.
        dev = min(max(0.2 + gdp_pc.get(code, 5000.0) / 150000.0, 0.2), 0.95)
        infra = min(max(0.3 + 0.5 * dev, 0.3), 0.95)

        # Растеризация: внешний контур — цвет провинции, дырки — океан (0).
        col = ((pid + 1) & 0xFF, ((pid + 1) >> 8) & 0xFF, ((pid + 1) >> 16) & 0xFF)
        for poly in polygons:
            if not poly:
                continue
            outer = poly[0]
            pts = [(int(round((lon_ + 180.0) * SCALE)), int(round((90.0 - lat_) * SCALE)))
                   for lon_, lat_ in outer]
            if len(pts) >= 3:
                draw.polygon(pts, fill=col)
            for hole in poly[1:]:
                hpts = [(int(round((lon_ + 180.0) * SCALE)), int(round((90.0 - lat_) * SCALE)))
                        for lon_, lat_ in hole]
                if len(hpts) >= 3:
                    draw.polygon(hpts, fill=(0, 0, 0))

        provinces.append({
            "Id": pid,
            "OwnerCode": code,
            "NameEn": name_en,
            "NameRu": name_ru,
            "RegionId": -1,
            "ContinentId": countries[cid]["Continent"],
            "IsCoastal": False,
            "Terrain": 0,  # Plains
            "Climate": climate_from_lat(lat),
            "AreaKm2": area,
            "Development": dev,
            "Infrastructure": infra,
            "CentroidX": cx,
            "CentroidY": cy,
            "TotalPopulation": 0,
            "NeighborIds": [],
            "ResourceIds": [],
            "CoreCodes": [code],
        })
        area_by_country[cid].append((pid, area))

    print(f"provinces: {len(provinces)}")
    print(f"skipped admin-1 features (unknown/-99 owner): {dict(skipped)}")

    # --- Микропровинции: гарантируем рендер каждой (маркер в океане, итеративно) ---
    arr_tmp = np.asarray(img).astype(np.int32)
    grid = arr_tmp[:, :, 0] + arr_tmp[:, :, 1] * 256 + arr_tmp[:, :, 2] * 65536 - 1  # pid или -1
    draw_marker = ImageDraw.Draw(img)
    used = set()

    def place_marker(pid, cx, cy):
        col = ((pid + 1) & 0xFF, ((pid + 1) >> 8) & 0xFF, ((pid + 1) >> 16) & 0xFF)
        # Проход 1: ближайший океан-пиксель (не затираем чужие провинции).
        for r in range(0, 60):
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    x, y = cx + dx, cy + dy
                    if not (0 <= x < W and 0 <= y < H):
                        continue
                    if (x, y) in used or grid[y, x] != -1:
                        continue
                    grid[y, x] = pid
                    used.add((x, y))
                    draw_marker.point((x, y), fill=col)
                    return True
        # Проход 2: любой свободный пиксель (редкий fallback).
        for r in range(0, 60):
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    x, y = cx + dx, cy + dy
                    if not (0 <= x < W and 0 <= y < H):
                        continue
                    if (x, y) in used:
                        continue
                    grid[y, x] = pid
                    used.add((x, y))
                    draw_marker.point((x, y), fill=col)
                    return True
        return False

    marked = 0
    for _ in range(30):
        counts = np.bincount(grid[grid >= 0], minlength=len(provinces))
        zero = [pid for pid in range(len(provinces)) if counts[pid] == 0]
        if not zero:
            break
        for pid in zero:
            p = provinces[pid]
            if place_marker(pid, int(round(p["CentroidX"])), int(round(p["CentroidY"]))):
                marked += 1
    print(f"micro-provinces marked: {marked}")

    # --- Соседство и прибрежность из ID-карты (numpy) ---
    arr = np.asarray(img).astype(np.int32)
    ids = arr[:, :, 0] + arr[:, :, 1] * 256 + arr[:, :, 2] * 65536
    ids = ids - 1  # 0 = океан -> -1

    Hh, Ww = ids.shape
    right = np.full_like(ids, -1)
    right[:, :-1] = ids[:, 1:]
    down = np.full_like(ids, -1)
    down[:-1, :] = ids[1:, :]
    left = np.full_like(ids, -1)
    left[:, 1:] = ids[:, :-1]
    up = np.full_like(ids, -1)
    up[1:, :] = ids[:-1, :]

    land = ids >= 0

    neighbors = defaultdict(set)
    coastal = set()
    for nb in (right, down, left, up):
        diff = land & (nb >= 0) & (nb != ids)
        a = ids[diff]
        b = nb[diff]
        for x, y in zip(a.tolist(), b.tolist()):
            neighbors[x].add(y)
            neighbors[y].add(x)
        sea = land & (nb == -1)
        coastal.update(ids[sea].tolist())

    for p in provinces:
        pid = p["Id"]
        p["NeighborIds"] = sorted(neighbors.get(pid, ()))
        p["IsCoastal"] = pid in coastal

    # --- Города (население по реальным точкам) ---
    def sample_province(x, y):
        for r in range(0, 4):
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    xx, yy = x + dx, y + dy
                    if 0 <= xx < Ww and 0 <= yy < Hh:
                        v = ids[yy, xx]
                        if v >= 0:
                            return int(v)
        return -1

    city_pop = defaultdict(int)
    d_cities = json.load(open(POPULATED, encoding="utf-8"))
    for f in d_cities["features"]:
        p = f["properties"]
        pop = to_int(p.get("POP_MAX"), 0)
        if pop <= 0:
            continue
        lon = p.get("LONGITUDE")
        lat = p.get("LATITUDE")
        if lon is None or lat is None:
            continue
        x = int(round((lon + 180.0) * SCALE))
        y = int(round((90.0 - lat) * SCALE))
        pid = sample_province(x, y)
        if pid < 0:
            continue
        # Город должен принадлежать той же стране, что и провинция.
        owner = provinces[pid]["OwnerCode"]
        city_code = (p.get("ADM0_A3") or p.get("SOV_A3") or owner).strip()
        if city_code != owner:
            continue
        city_pop[pid] += pop

    # --- Распределение населения: города + остаток по площади ---
    for cid, items in area_by_country.items():
        total_pop = countries[cid]["Population"]
        if total_pop <= 0:
            continue
        csum = sum(city_pop[pid] for pid, _ in items)
        # Города не должны превышать 80% населения страны.
        if csum > total_pop * 0.8:
            k = total_pop * 0.8 / csum
            for pid, _ in items:
                city_pop[pid] = int(city_pop[pid] * k)
            csum = sum(city_pop[pid] for pid, _ in items)
        remaining = max(total_pop - csum, 0)
        total_area = sum(a for _, a in items)
        ordered = sorted(items, key=lambda x: -x[1])
        assigned = 0
        for pid, area in ordered:
            share = int(remaining * area / total_area) if total_area > 0 else 0
            provinces[pid]["TotalPopulation"] = share + city_pop[pid]
            assigned += provinces[pid]["TotalPopulation"]
        # Остаток — в крупнейшую провинцию.
        diff = total_pop - assigned
        if ordered:
            provinces[ordered[0][0]]["TotalPopulation"] += diff

    # --- Столицы: провинция с макс. населением ---
    best = {}
    for p in provinces:
        cid = code_to_cid[p["OwnerCode"]]
        if cid not in best or p["TotalPopulation"] > best[cid][1]:
            best[cid] = (p["Id"], p["TotalPopulation"])
    for cid, (pid, _) in best.items():
        countries[cid]["CapitalProvinceId"] = pid

    # --- Вывод ---
    world = {
        "Version": 1,
        "MapWidthPx": float(W),
        "MapHeightPx": float(H),
        "Countries": [{k: v for k, v in c.items() if k != "Continent"} for c in countries],
        "Provinces": provinces,
    }

    with open(os.path.join(OUT_DIR, "world.json"), "w", encoding="utf-8") as fh:
        json.dump(world, fh, ensure_ascii=False, separators=(",", ":"))

    img.save(os.path.join(OUT_DIR, "id_map.png"))

    # Маска границ.
    same_h = (arr[:, :-1] == arr[:, 1:]).all(axis=2)
    same_v = (arr[:-1, :] == arr[1:, :]).all(axis=2)
    border = np.zeros((Hh, Ww), dtype=bool)
    border[:, :-1] |= ~same_h
    border[:-1, :] |= ~same_v
    border[:, -1] = True
    border[-1, :] = True
    # Утолщение границ на 1px (компенсация высокого разрешения — границы видны при отдалении).
    border = (border | np.roll(border, 1, axis=0) | np.roll(border, -1, axis=0)
              | np.roll(border, 1, axis=1) | np.roll(border, -1, axis=1))
    bm = np.zeros((Hh, Ww, 3), dtype=np.uint8)
    bm[border] = 255
    Image.fromarray(bm).save(os.path.join(OUT_DIR, "border_mask.png"))

    # Статистика.
    countries_with_land = sum(1 for c in countries if c["CapitalProvinceId"] >= 0)
    total_pop = sum(c["Population"] for c in countries)
    print(f"countries with land: {countries_with_land}")
    print(f"total population: {total_pop:,}")
    print("output ->", OUT_DIR)


if __name__ == "__main__":
    main()

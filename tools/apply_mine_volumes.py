#!/usr/bin/env python3
"""Объёмы добычи рудников из fineprint -> интенсивность добычи провинций.

Считает суммарную добычу (в тоннах) по каждому руднику (лист minerals_ores_conce),
привязывает рудник к провинции (лист general: координаты) и записывает в
ProvinceDto поле ResourceAmounts (int, тонны) — объём добычи ресурса.

Пишет в data/cache/world.json и world_1936.json поле ResourceAmounts (dict goodId->тонны).
"""
import openpyxl, json
import numpy as np
from PIL import Image

SCALE = 16.0
IDMAP = "data/cache/id_map.png"

MINERAL_TO_GOOD = {
    "coal": 4, "thermal coal": 4, "metallurgical coal": 4, "coking coal": 4,
    "lignite": 4, "bituminous coal": 4, "sub-bituminous coal": 4,
    "metallurgical bituminous coal": 4, "pulverized coal injection": 4, "coke": 4,
    "iron": 3, "iron ore": 3, "iron ore pellets": 3,
    "gold": 12,
    "nickel": 13, "cobalt": 13, "platinum": 13, "palladium": 13, "pgm": 13,
    "molybdenum": 13, "manganese": 13, "ilmenite": 13, "rare earths": 13,
    "copper": 13, "zinc": 13, "lead": 13, "silver": 13, "aluminium": 13, "alumina": 13,
    "bauxite": 13, "phosphate": 13,
}

def main():
    ids = np.asarray(Image.open(IDMAP).convert("RGB")).astype(np.int32)
    prov_raster = ids[:,:,0] + ids[:,:,1]*256 + ids[:,:,2]*65536 - 1
    H, W = prov_raster.shape

    wb = openpyxl.load_workbook("/tmp/mining.xlsx", read_only=True)

    # 1. mine_fac -> (lat, lon, commodities)
    ws = wb["general"]
    rows = ws.iter_rows(values_only=True)
    header = next(rows)
    hidx = {h:i for i,h in enumerate(header) if h}
    mine_loc = {}
    for r in rows:
        fac = r[hidx["mine_fac"]]
        lat = r[hidx["latitude"]]; lon = r[hidx["longitude"]]
        comm = r[hidx["commodities_products"]]
        if fac and lat is not None and lon is not None:
            x = int(round((lon + 180.0) * SCALE)); y = int(round((90.0 - lat) * SCALE))
            if 0 <= x < W and 0 <= y < H:
                pid = prov_raster[y, x]
                if pid >= 0:
                    goods = set()
                    if comm:
                        for c in str(comm).split(","):
                            g = MINERAL_TO_GOOD.get(c.strip().lower())
                            if g: goods.add(g)
                    mine_loc[fac] = (pid, goods)

    # 2. Объёмы добычи: mine_fac -> good_id -> тонны (максимум по годам)
    ws2 = wb["minerals_ores_conce"]
    rows2 = ws2.iter_rows(values_only=True)
    header2 = next(rows2)
    h2 = {h:i for i,h in enumerate(header2) if h}
    mine_amount = {}  # pid -> good_id -> тонны
    for r in rows2:
        fac = r[h2["mine_fac"]]
        mincon = r[h2["min_ore_con"]]
        value = r[h2["value"]]
        unit = r[h2["unit"]]
        if not fac or value is None or not mincon:
            continue
        if fac not in mine_loc:
            continue
        pid, goods = mine_loc[fac]
        # value в Kt (тысячи тонн) или t; может содержать формулы/текст — пропускаем.
        try:
            tons = float(value)
        except (ValueError, TypeError):
            continue
        if unit and "Kt" in str(unit):
            tons *= 1000.0
        # сопоставляем mineral_con с товаром
        g = MINERAL_TO_GOOD.get(str(mincon).lower())
        if g is None:
            # пробуем по commodities из general
            continue
        d = mine_amount.setdefault(pid, {})
        d[g] = max(d.get(g, 0.0), tons)

    print(f"рудников с объёмами: {len(mine_amount)}")

    for path in ["data/cache/world.json", "data/cache/world_1936.json"]:
        w = json.load(open(path, encoding="utf-8"))
        for p in w["Provinces"]:
            pid = p["Id"]
            if pid in mine_amount:
                p["ResourceAmounts"] = {str(k): int(v) for k, v in mine_amount[pid].items()}
        json.dump(w, open(path, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
        print(f"обновлён {path}")

if __name__ == "__main__":
    main()

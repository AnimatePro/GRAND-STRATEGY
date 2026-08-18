#!/usr/bin/env python3
"""Точная привязка реальных месторождений (fineprint compilation_mining_data)
к провинциям карты. Объединяет:
1) реальные шахты/рудники по координатам (fineprint general.xlsx),
2) ресурсы по стране (data/resources.csv) — детерминированно ~25% провинций.

Записывает ПОЛНЫЙ ResourceIds в world.json и world_1936.json,
чтобы WorldDataLoader просто читал их из json (fallback — по ландшафту).
"""
import openpyxl, json, csv
import numpy as np
from PIL import Image

SCALE = 16.0
IDMAP = "data/cache/id_map.png"

MINERAL_TO_GOOD = {
    "coal": 4, "thermal coal": 4, "metallurgical coal": 4, "coking coal": 4,
    "lignite": 4, "bituminous coal": 4, "sub-bituminous coal": 4,
    "metallurgical bituminous coal": 4, "pulverized coal injection": 4, "coke": 4,
    "iron": 3, "iron ore": 3, "iron ore pellets": 3, "steel": 7, "crude steel": 7,
    "pig iron": 7, "rebar": 7, "merchant bars": 7, "wire rod": 7,
    "gold": 12,
    "nickel": 13, "cobalt": 13, "platinum": 13, "palladium": 13, "pgm": 13,
    "molybdenum": 13, "manganese": 13, "ilmenite": 13, "rare earths": 13,
    "copper": 13, "copper cathode": 13, "copper cathodes": 13,
    "zinc": 13, "lead": 13, "silver": 13, "aluminium": 13, "alumina": 13, "bauxite": 13,
    "phosphate": 13,
}

# Ресурсы по стране (из resources.csv) -> id товара
COUNTRY_RES = {
    "oil": 5, "gas": 6, "coal": 4, "iron": 3, "rare_metals": 13, "gold": 12,
}

def load_country_resources():
    result = {}
    for row in csv.DictReader(open("data/resources.csv", encoding="utf-8")):
        code = row["code"]
        ids = []
        for key, gid in COUNTRY_RES.items():
            if row.get(key) == "1":
                ids.append(gid)
        result[code] = ids
    return result

def main():
    ids = np.asarray(Image.open(IDMAP).convert("RGB")).astype(np.int32)
    prov_raster = ids[:,:,0] + ids[:,:,1]*256 + ids[:,:,2]*65536 - 1
    H, W = prov_raster.shape

    # Реальные рудники -> провинция.
    prov_mines = {}
    wb = openpyxl.load_workbook("/tmp/mining.xlsx", read_only=True)
    ws = wb["general"]
    rows = ws.iter_rows(values_only=True)
    header = next(rows)
    hidx = {h:i for i,h in enumerate(header) if h}
    for r in rows:
        lat = r[hidx["latitude"]]; lon = r[hidx["longitude"]]
        comm = r[hidx["commodities_products"]]
        if lat is None or lon is None or comm is None:
            continue
        x = int(round((lon + 180.0) * SCALE)); y = int(round((90.0 - lat) * SCALE))
        if not (0 <= x < W and 0 <= y < H):
            continue
        pid = prov_raster[y, x]
        if pid < 0:
            continue
        s = prov_mines.setdefault(int(pid), set())
        for c in str(comm).split(","):
            g = MINERAL_TO_GOOD.get(c.strip().lower())
            if g is not None:
                s.add(g)
    print(f"провинций с реальными рудниками: {len(prov_mines)}")

    country_res = load_country_resources()
    print(f"стран с ресурсами по стране: {len(country_res)}")

    for path in ["data/cache/world.json", "data/cache/world_1936.json"]:
        w = json.load(open(path, encoding="utf-8"))
        for p in w["Provinces"]:
            pid = p["Id"]
            merged = set(prov_mines.get(pid, set()))
            # Ресурсы по стране (детерминированно ~25% провинций).
            code = p.get("OwnerCode", "")
            if code in country_res:
                for gid in country_res[code]:
                    h = (pid * 2654435761) ^ (gid * 40503)
                    if (h & 3) == 0:
                        merged.add(gid)
            p["ResourceIds"] = sorted(merged)
        json.dump(w, open(path, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
        print(f"обновлён {path}")

if __name__ == "__main__":
    main()

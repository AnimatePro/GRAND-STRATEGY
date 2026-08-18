#!/usr/bin/env python3
"""Генератор исторической карты 1938 (сценарий 1936) из aourednik/historical-basemaps.

Вход: /tmp/world_1938.geojson (реальные границы стран 1938, близко к 1936).
Выход: data/cache/world_1938.json + id_map_1938.png + border_mask_1938.png.

Каждая историческая «страна» (включая колонии) становится провинцией.
Сопоставление с лидерами 1936 (data/leaders.csv) по имени страны.
"""
import json, csv, os
import numpy as np
from PIL import Image, ImageDraw

SCALE = 16.0
W = int(360 * SCALE)
H = int(180 * SCALE)
SRC = "/tmp/world_1938.geojson"
OUT = "data/cache"

# Палитра (совпадает с C# CountryPalette)
PALETTE = ["#4E79A7","#F28E2B","#E15759","#76B7B2","#59A14F","#EDC948","#B07AA1",
           "#FF9DA7","#9C755F","#BAB0AC","#6B8E23","#7B68EE","#CD853F","#4682B4",
           "#C71585","#2E8B57","#8B0000","#DAA520","#4169E1","#A0522D"]

def color_for(name):
    h = 0
    for ch in name:
        h = (h * 31 + ord(ch)) & 0x7FFFFFFF
    return PALETTE[h % len(PALETTE)]

# Имя 1938 -> код страны для лидера (где применимо). Колонии -> код метрополии.
NAME_TO_LEADER = {
    "Germany":"DEU","USSR":"RUS","United States":"USA","United Kingdom":"GBR",
    "France":"FRA","Italy":"ITA","Empire of Japan":"JPN","China":"CHN",
    "Chinese warlords":"CHN","Xinjiang":"CHN","Tibet":"CHN",
    "India":"IND","British Raj":"GBR","Czechoslovakia":"CZE","Yugoslavia":"SRB",
    "Poland":"POL","Romania":"ROU","Bulgaria":"BGR","Hungary":"HUN",
    "Spain":"ESP","Portugal":"PRT","Netherlands":"NLD","Belgium":"BEL",
    "Switzerland":"CHE","Sweden":"SWE","Norway":"NOR","Denmark":"DNK",
    "Finland":"FIN","Estonia":"EST","Latvia":"LVA","Lithuania":"LTU",
    "Ireland":"IRL","Iceland":"ISL","Greece":"GRC","Turkey":"TUR",
    "Iran":"IRN","Iraq":"IRQ","Saudi Arabia":"SAU","Afghanistan":"AFG",
    "Siam":"THA","Nepal":"NPL","Bhutan":"BTN","Mongolia":"MNG",
    "Mexico":"MEX","Canada":"CAN","Brazil":"BRA","Argentina":"ARG",
    "Chile":"CHL","Peru":"PER","Colombia":"COL","Venezuela":"VEN",
    "Ecuador":"ECU","Bolivia":"BOL","Paraguay":"PRY","Uruguay":"URY",
    "Cuba":"CUB","Dominican Republic":"DOM","Haiti":"HTI","Guatemala":"GTM",
    "Honduras":"HND","Nicaragua":"NIC","El Salvador":"SLV","Costa Rica":"CRI",
    "Panama":"PAN","Egypt":"EGY","Liberia":"LBR","Union of South Africa":"ZAF",
    "Ethiopia (Italy)":"ITA","Yemen":"YEM","Luxembourg":"LUX",
    "Australia":"AUS","New Zealand":"NZL","Muscat and Oman":"OMN",
    "Kuwait":"KWT","Qatar":"QAT","Albania":"ALB","Ceylon":"GBR",
    "Belgian Congo":"BEL","French West Africa":"FRA","French Equatorial Africa":"FRA",
    "French Indo-China":"FRA","French Cameroons":"FRA","French Somaliland":"FRA",
    "Madagascar (France)":"FRA","Morocco (France)":"FRA","Algeria (France)":"FRA",
    "Tunisia":"FRA","Syria (France)":"FRA","Congo (France)":"FRA",
    "Dutch East Indies":"NLD","Angola (Portugal)":"PRT","Mozambique (Portugal)":"PRT",
    "British Somaliland":"GBR","Kenya":"GBR","Uganda":"GBR","Nigeria":"GBR",
    "Gold Coast":"GBR","Sierra Leone":"GBR","Gambia, The":"GBR","Sudan":"GBR",
    "Northern Rhodesia":"GBR","Southern Rhodesia":"GBR","Malawi":"GBR",
    "Mandatory Palestine (GB)":"GBR","Mesopotamia (GB)":"GBR","Jordan":"GBR",
    "Italian Somaliland":"ITA","Libya":"ITA","Eritrea (Italy)":"ITA",
    "Rwanda (Belgium)":"BEL","Burundi":"BEL",
    "Trinidad":"GBR","Jamaica":"GBR","Bahamas":"GBR","Barbados":"GBR",
    "Guyana":"GBR","Suriname":"NLD","Malaysia":"GBR","Brunei":"GBR",
    "Philippines":"USA","Puerto Rico":"USA","Guam":"USA",
    "Dominion of Newfoundland":"GBR","Greenland":"DNK",
    "Trucial Oman":"GBR","Laos":"FRA","Cambodia":"FRA",
    "Tanzania, United Republic of":"GBR","Botswana":"GBR","Swaziland":"GBR",
    "Lesotho":"GBR","Belize":"GBR","Togo":"FRA","French Guiana":"FRA",
    "Equatorial Guinea":"ESP","Israel":"GBR","Guinea-Bissau":"PRT",
    "Cochin China":"FRA","Yemen (UK)":"GBR","Spanish Sahara":"ESP",
    "Rio De Oro":"ESP","Walbis Bay":"ZAF","Antigua and Barbuda":"GBR",
    "Dominica":"GBR","Grenada":"GBR","Martinique":"FRA","Montserrat":"GBR",
    "Anguilla":"GBR","Saint Kitts and Nevis":"GBR","Saint Lucia":"GBR",
    "Saint Vincent and the Grenadines":"GBR","United States Virgin Islands":"USA",
    "Guadeloupe":"FRA","Netherlands Antilles":"NLD","Saint Martin":"FRA",
    "Saint Barthelemy":"FRA","Turks and Caicos Islands":"GBR",
    "Oman (British Raj)":"GBR","Emirate of Bin Shal'an":"SAU","Hejaz":"SAU",
    "Hail":"SAU","Armenia":"RUS","New Hebrides":"GBR","New Caledonia":"FRA",
    "Gilbert and Elice Islands":"GBR","Saipan":"JPN","American Samoa":"USA",
    "Fiji":"GBR","Niue":"NZL","Tonga":"GBR","Wallis and Futuna Islands":"FRA",
    "Samoa":"NZL","Rapa Nui":"CHL","Hong Kong":"GBR","Andorra":"AND",
}

def parse_geometry(geom):
    t = geom.get("type"); c = geom.get("coordinates")
    if t == "Polygon": return [c]
    if t == "MultiPolygon": return c
    return []

def main():
    d = json.load(open(SRC, encoding="utf-8"))

    # Лидеры 1936 по коду.
    leaders = {}
    for row in csv.DictReader(open("data/leaders.csv", encoding="utf-8")):
        leaders[row["code"]] = row["leader_1936"]

    # Население 1936 по коду.
    pop1936 = {}
    if os.path.exists("data/population_1936.csv"):
        for row in csv.DictReader(open("data/population_1936.csv", encoding="utf-8")):
            pop1936[row["code"]] = int(row["population"])

    img = Image.new("RGB", (W, H), (0,0,0))
    draw = ImageDraw.Draw(img)

    provinces = []
    for f in d["features"]:
        name = f["properties"].get("NAME") or "?"
        code = NAME_TO_LEADER.get(name)
        polygons = parse_geometry(f["geometry"])
        if not polygons:
            continue
        pid = len(provinces)
        col = ((pid+1)&0xFF, ((pid+1)>>8)&0xFF, ((pid+1)>>16)&0xFF)
        # центроид по bbox
        bbox = f.get("bbox")
        lon = (bbox[0]+bbox[2])/2 if bbox else 0
        lat = (bbox[1]+bbox[3])/2 if bbox else 0
        for poly in polygons:
            outer = poly[0]
            pts = [(int(round((a+180)*SCALE)), int(round((90-b)*SCALE))) for a,b in outer]
            if len(pts) >= 3:
                draw.polygon(pts, fill=col)
            for hole in poly[1:]:
                hpts = [(int(round((a+180)*SCALE)), int(round((90-b)*SCALE))) for a,b in hole]
                if len(hpts) >= 3:
                    draw.polygon(hpts, fill=(0,0,0))
        provinces.append({
            "Id": pid,
            "OwnerCode": code or "",
            "NameEn": name,
            "NameRu": name,
            "CentroidX": (lon+180)*SCALE,
            "CentroidY": (90-lat)*SCALE,
            "AreaKm2": 1.0,
            "Development": 0.5,
            "Infrastructure": 0.5,
            "IsCoastal": False,
            "Terrain": 0,
            "Climate": 2,
            "TotalPopulation": pop1936.get(code or "", 0),
            "ReligionId": 0,
            "CultureId": 0,
            "NeighborIds": [],
            "ResourceIds": [],
            "CoreCodes": [code] if code else [],
        })

    # --- Соседство из ID-карты ---
    arr = np.asarray(img).astype(np.int32)
    ids = arr[:,:,0] + arr[:,:,1]*256 + arr[:,:,2]*65536 - 1
    Hh, Ww = ids.shape
    neighbors = {}
    coastal = set()
    for nb, shift in [(np.roll(ids,1,1),1),(np.roll(ids,-1,1),-1),(np.roll(ids,1,0),0),(np.roll(ids,-1,0),0)]:
        pass
    # простой перебор сдвигов
    def neigh_and_coast():
        nb = {}
        cs = set()
        for y in range(Hh):
            for x in range(Ww):
                c = ids[y,x]
                if c < 0: continue
                for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                    nx, ny = x+dx, y+dy
                    if 0<=nx<Ww and 0<=ny<Hh:
                        o = ids[ny,nx]
                        if o >= 0 and o != c:
                            nb.setdefault(c,set()).add(o)
                        elif o < 0:
                            cs.add(c)
        return nb, cs
    nb, cs = neigh_and_coast()
    for p in provinces:
        p["NeighborIds"] = sorted(int(x) for x in nb.get(p["Id"], ()))
        p["IsCoastal"] = p["Id"] in cs

    # --- Распределение населения страны по её провинциям (по площади в пикселях) ---
    counts = np.bincount(ids[ids >= 0], minlength=len(provinces))
    area_by_code = {}
    for p in provinces:
        code = p["OwnerCode"]
        if code:
            area_by_code[code] = area_by_code.get(code, 0) + counts[p["Id"]]
    for p in provinces:
        code = p["OwnerCode"]
        if not code:
            p["TotalPopulation"] = 0
            continue
        pop = pop1936.get(code, 0)
        total_area = max(area_by_code.get(code, 1), 1)
        p["TotalPopulation"] = int(pop * counts[p["Id"]] / total_area)
        p["AreaKm2"] = float(counts[p["Id"]]) / (SCALE * SCALE) * (111.32 * 111.32)

    # --- Страны ---
    countries = []
    name_to_country = {}
    for p in provinces:
        code = p["OwnerCode"]
        if not code:
            continue
        if code in name_to_country:
            continue
        cid = len(countries)
        name_to_country[code] = cid
        countries.append({
            "Id": cid, "Code": code, "NameKey": p["NameEn"], "NameRu": p["NameEn"],
            "FlagId": code.lower(), "ColorHex": color_for(code),
            "GovernmentType": 1, "Ideology": 0, "CapitalProvinceId": p["Id"],
            "Population": pop1936.get(code, 0), "Gdp": pop1936.get(code,0) * 800,
            "Literacy": 0.5, "Urbanization": 0.3, "Treasury": 10000.0,
            "ReligionId": 0, "CultureId": 0,
            "Leader": leaders.get(code, ""),
        })
    # Привязка OwnerCode -> countryId
    code_to_cid = {c["Code"]: c["Id"] for c in countries}
    # провинции получают OwnerId по коду (или -1 для несуверенных без кода)
    for p in provinces:
        code = p["OwnerCode"]
        p["OwnerId"] = code_to_cid.get(code, -1) if code else -1

    world = {
        "Version": 2,
        "MapWidthPx": float(W), "MapHeightPx": float(H),
        "Countries": countries, "Provinces": provinces,
    }
    with open(os.path.join(OUT,"world_1938.json"),"w",encoding="utf-8") as fh:
        json.dump(world, fh, ensure_ascii=False, separators=(",",":"))
    img.save(os.path.join(OUT,"id_map_1938.png"))

    # маска границ
    same_h = (arr[:,:-1]==arr[:,1:]).all(axis=2)
    same_v = (arr[:-1,:]==arr[1:,:]).all(axis=2)
    border = np.zeros((Hh,Ww),bool)
    border[:,:-1] |= ~same_h
    border[:-1,:] |= ~same_v
    border[:,-1]=True; border[-1,:]=True
    for _ in range(2):
        border = border | np.roll(border,1,0)|np.roll(border,-1,0)|np.roll(border,1,1)|np.roll(border,-1,1)
    bm = np.zeros((Hh,Ww,3),np.uint8)
    bm[border]=255
    Image.fromarray(bm).save(os.path.join(OUT,"border_mask_1938.png"))

    print(f"countries: {len(countries)}, provinces: {len(provinces)}")
    print("output ->", OUT)

if __name__ == "__main__":
    main()

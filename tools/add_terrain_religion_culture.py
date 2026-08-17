#!/usr/bin/env python3
"""Пост-обработка world.json: реальный рельеф (горы из Natural Earth пиков + климат)
и реальные религия/культура по странам + провинциям.

Не пересоздаёт карту — только обогащает существующий data/cache/world.json
и data/cache/id_map.png (для привязки пиков к провинциям).
"""
import json, csv, os
import numpy as np
from PIL import Image

SCALE = 16.0
WORLD = "data/cache/world.json"
ID_MAP = "data/cache/id_map.png"
ELEV = "/tmp/elevation_points.geojson"

# Terrain enum (совпадает с C# Enums.cs): 0 Plains,1 Hills,2 Mountains,3 Forest,4 Jungle,5 Desert,6 Tundra
# Climate enum: 0 Tropical,1 Arid,2 Temperate,3 Continental,4 Polar

def load_religion_ids():
    ids = {}
    with open("data/religions.csv", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            ids[row["name_key"]] = int(row["id"])
    return ids

def load_culture_ids():
    ids = {}
    with open("data/cultures.csv", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            ids[row["name_key"]] = int(row["id"])
    return ids

# Страна -> (religion_name_key, culture_name_key)
COUNTRY_REL_CUL = {
    # --- Европа ---
    "ALB": ("REL_ISLAM", "CUL_ALBANIAN"), "AUT": ("REL_CHRISTIANITY", "CUL_GERMAN"),
    "BLR": ("REL_CHRISTIANITY", "CUL_BELARUSIAN"), "BEL": ("REL_CHRISTIANITY", "CUL_DUTCH"),
    "BIH": ("REL_ISLAM", "CUL_SOUTH_SLAVIC"), "BGR": ("REL_CHRISTIANITY", "CUL_BULGARIAN"),
    "HRV": ("REL_CHRISTIANITY", "CUL_SOUTH_SLAVIC"), "CZE": ("REL_IRRELIGION", "CUL_CZECH"),
    "DNK": ("REL_CHRISTIANITY", "CUL_SCANDINAVIAN"), "EST": ("REL_IRRELIGION", "CUL_BALTIC"),
    "FIN": ("REL_CHRISTIANITY", "CUL_FINNISH"), "FRA": ("REL_CHRISTIANITY", "CUL_FRENCH"),
    "DEU": ("REL_CHRISTIANITY", "CUL_GERMAN"), "GRC": ("REL_CHRISTIANITY", "CUL_GREEK"),
    "HUN": ("REL_CHRISTIANITY", "CUL_HUNGARIAN"), "ISL": ("REL_CHRISTIANITY", "CUL_SCANDINAVIAN"),
    "IRL": ("REL_CHRISTIANITY", "CUL_IRISH"), "ITA": ("REL_CHRISTIANITY", "CUL_ITALIAN"),
    "XKX": ("REL_ISLAM", "CUL_ALBANIAN"), "KOS": ("REL_ISLAM", "CUL_ALBANIAN"),
    "LVA": ("REL_CHRISTIANITY", "CUL_BALTIC"), "LTU": ("REL_CHRISTIANITY", "CUL_BALTIC"),
    "LUX": ("REL_CHRISTIANITY", "CUL_GERMAN"), "MKD": ("REL_CHRISTIANITY", "CUL_SOUTH_SLAVIC"),
    "MDA": ("REL_CHRISTIANITY", "CUL_ROMANIAN"), "MNE": ("REL_CHRISTIANITY", "CUL_SOUTH_SLAVIC"),
    "NLD": ("REL_IRRELIGION", "CUL_DUTCH"), "NOR": ("REL_CHRISTIANITY", "CUL_SCANDINAVIAN"),
    "POL": ("REL_CHRISTIANITY", "CUL_POLISH"), "PRT": ("REL_CHRISTIANITY", "CUL_PORTUGUESE"),
    "ROU": ("REL_CHRISTIANITY", "CUL_ROMANIAN"), "RUS": ("REL_CHRISTIANITY", "CUL_RUSSIAN"),
    "SRB": ("REL_CHRISTIANITY", "CUL_SOUTH_SLAVIC"), "SVK": ("REL_CHRISTIANITY", "CUL_CZECH"),
    "SVN": ("REL_CHRISTIANITY", "CUL_SOUTH_SLAVIC"), "ESP": ("REL_CHRISTIANITY", "CUL_SPANISH"),
    "SWE": ("REL_CHRISTIANITY", "CUL_SCANDINAVIAN"), "CHE": ("REL_CHRISTIANITY", "CUL_GERMAN"),
    "UKR": ("REL_CHRISTIANITY", "CUL_UKRAINIAN"), "GBR": ("REL_CHRISTIANITY", "CUL_ENGLISH"),
    "AND": ("REL_CHRISTIANITY", "CUL_CATALAN"), "LIE": ("REL_CHRISTIANITY", "CUL_GERMAN"),
    "MCO": ("REL_CHRISTIANITY", "CUL_FRENCH"), "MLT": ("REL_CHRISTIANITY", "CUL_ITALIAN"),
    "SMR": ("REL_CHRISTIANITY", "CUL_ITALIAN"), "VAT": ("REL_CHRISTIANITY", "CUL_ITALIAN"),
    # --- Азия ---
    "AFG": ("REL_ISLAM", "CUL_PASHTUN"), "ARM": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "AZE": ("REL_ISLAM", "CUL_TURKISH"), "BHR": ("REL_ISLAM", "CUL_ARABIC"),
    "BGD": ("REL_ISLAM", "CUL_BENGALI"), "BTN": ("REL_BUDDHISM", "CUL_TIBETAN"),
    "BRN": ("REL_ISLAM", "CUL_MALAY"), "KHM": ("REL_BUDDHISM", "CUL_KHMER"),
    "CHN": ("REL_IRRELIGION", "CUL_CHINESE"), "CYP": ("REL_CHRISTIANITY", "CUL_GREEK"),
    "GEO": ("REL_CHRISTIANITY", "CUL_OTHER"), "IND": ("REL_HINDUISM", "CUL_HINDI"),
    "IDN": ("REL_ISLAM", "CUL_MALAY"), "IRN": ("REL_ISLAM", "CUL_PERSIAN"),
    "IRQ": ("REL_ISLAM", "CUL_ARABIC"), "ISR": ("REL_JUDAISM", "CUL_HEBREW"),
    "JPN": ("REL_SHINTO", "CUL_JAPANESE"), "JOR": ("REL_ISLAM", "CUL_ARABIC"),
    "KAZ": ("REL_ISLAM", "CUL_KAZAKH"), "KWT": ("REL_ISLAM", "CUL_ARABIC"),
    "KGZ": ("REL_ISLAM", "CUL_KAZAKH"), "LAO": ("REL_BUDDHISM", "CUL_THAI"),
    "LBN": ("REL_ISLAM", "CUL_ARABIC"), "MYS": ("REL_ISLAM", "CUL_MALAY"),
    "MDV": ("REL_ISLAM", "CUL_OTHER"), "MNG": ("REL_BUDDHISM", "CUL_MONGOLIAN"),
    "MMR": ("REL_BUDDHISM", "CUL_BURMESE"), "NPL": ("REL_HINDUISM", "CUL_NEPALI"),
    "PRK": ("REL_IRRELIGION", "CUL_KOREAN"), "OMN": ("REL_ISLAM", "CUL_ARABIC"),
    "PAK": ("REL_ISLAM", "CUL_URDU"), "PHL": ("REL_CHRISTIANITY", "CUL_FILIPINO"),
    "QAT": ("REL_ISLAM", "CUL_ARABIC"), "SAU": ("REL_ISLAM", "CUL_ARABIC"),
    "SGP": ("REL_BUDDHISM", "CUL_CHINESE"), "KOR": ("REL_IRRELIGION", "CUL_KOREAN"),
    "LKA": ("REL_BUDDHISM", "CUL_SINHALESE"), "SYR": ("REL_ISLAM", "CUL_ARABIC"),
    "TWN": ("REL_CHINESE_FOLK", "CUL_CHINESE"), "TJK": ("REL_ISLAM", "CUL_PERSIAN"),
    "THA": ("REL_BUDDHISM", "CUL_THAI"), "TLS": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "TUR": ("REL_ISLAM", "CUL_TURKISH"), "TKM": ("REL_ISLAM", "CUL_KAZAKH"),
    "ARE": ("REL_ISLAM", "CUL_ARABIC"), "UZB": ("REL_ISLAM", "CUL_UZBEK"),
    "VNM": ("REL_FOLK", "CUL_VIETNAMESE"), "YEM": ("REL_ISLAM", "CUL_ARABIC"),
    # --- Африка ---
    "DZA": ("REL_ISLAM", "CUL_ARABIC"), "AGO": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "BEN": ("REL_FOLK", "CUL_OTHER"), "BWA": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "BFA": ("REL_ISLAM", "CUL_OTHER"), "BDI": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "CMR": ("REL_CHRISTIANITY", "CUL_OTHER"), "CPV": ("REL_CHRISTIANITY", "CUL_PORTUGUESE"),
    "CAF": ("REL_CHRISTIANITY", "CUL_OTHER"), "TCD": ("REL_ISLAM", "CUL_OTHER"),
    "COM": ("REL_ISLAM", "CUL_OTHER"), "COD": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "COG": ("REL_CHRISTIANITY", "CUL_OTHER"), "CIV": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "DJI": ("REL_ISLAM", "CUL_SOMALI"), "EGY": ("REL_ISLAM", "CUL_ARABIC"),
    "GNQ": ("REL_CHRISTIANITY", "CUL_OTHER"), "ERI": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "SWZ": ("REL_CHRISTIANITY", "CUL_OTHER"), "ETH": ("REL_CHRISTIANITY", "CUL_AMHARIC"),
    "GAB": ("REL_CHRISTIANITY", "CUL_OTHER"), "GMB": ("REL_ISLAM", "CUL_OTHER"),
    "GHA": ("REL_CHRISTIANITY", "CUL_OTHER"), "GIN": ("REL_ISLAM", "CUL_OTHER"),
    "GNB": ("REL_ISLAM", "CUL_OTHER"), "KEN": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "LSO": ("REL_CHRISTIANITY", "CUL_OTHER"), "LBR": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "LBY": ("REL_ISLAM", "CUL_ARABIC"), "MDG": ("REL_CHRISTIANITY", "CUL_MALAGASY"),
    "MWI": ("REL_CHRISTIANITY", "CUL_OTHER"), "MLI": ("REL_ISLAM", "CUL_OTHER"),
    "MRT": ("REL_ISLAM", "CUL_ARABIC"), "MUS": ("REL_HINDUISM", "CUL_OTHER"),
    "MAR": ("REL_ISLAM", "CUL_ARABIC"), "MOZ": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "NAM": ("REL_CHRISTIANITY", "CUL_OTHER"), "NER": ("REL_ISLAM", "CUL_HAUSA"),
    "NGA": ("REL_CHRISTIANITY", "CUL_YORUBA"), "RWA": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "STP": ("REL_CHRISTIANITY", "CUL_PORTUGUESE"), "SEN": ("REL_ISLAM", "CUL_OTHER"),
    "SYC": ("REL_CHRISTIANITY", "CUL_OTHER"), "SLE": ("REL_ISLAM", "CUL_OTHER"),
    "SOM": ("REL_ISLAM", "CUL_SOMALI"), "ZAF": ("REL_CHRISTIANITY", "CUL_ZULU"),
    "SSD": ("REL_CHRISTIANITY", "CUL_OTHER"), "SDN": ("REL_ISLAM", "CUL_ARABIC"),
    "TZA": ("REL_CHRISTIANITY", "CUL_SWAHILI"), "TGO": ("REL_FOLK", "CUL_OTHER"),
    "TUN": ("REL_ISLAM", "CUL_ARABIC"), "UGA": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "ZMB": ("REL_CHRISTIANITY", "CUL_OTHER"), "ZWE": ("REL_CHRISTIANITY", "CUL_SHONA"),
    # --- Америка ---
    "USA": ("REL_CHRISTIANITY", "CUL_ENGLISH"), "CAN": ("REL_CHRISTIANITY", "CUL_ENGLISH"),
    "MEX": ("REL_CHRISTIANITY", "CUL_MESTIZO"), "GTM": ("REL_CHRISTIANITY", "CUL_MESTIZO"),
    "BLZ": ("REL_CHRISTIANITY", "CUL_MESTIZO"), "SLV": ("REL_CHRISTIANITY", "CUL_MESTIZO"),
    "HND": ("REL_CHRISTIANITY", "CUL_MESTIZO"), "NIC": ("REL_CHRISTIANITY", "CUL_MESTIZO"),
    "CRI": ("REL_CHRISTIANITY", "CUL_SPANISH"), "PAN": ("REL_CHRISTIANITY", "CUL_MESTIZO"),
    "CUB": ("REL_CHRISTIANITY", "CUL_SPANISH"), "DOM": ("REL_CHRISTIANITY", "CUL_MESTIZO"),
    "HTI": ("REL_CHRISTIANITY", "CUL_CREOLE"), "JAM": ("REL_CHRISTIANITY", "CUL_CREOLE"),
    "BHS": ("REL_CHRISTIANITY", "CUL_CREOLE"), "BRB": ("REL_CHRISTIANITY", "CUL_CREOLE"),
    "TTO": ("REL_CHRISTIANITY", "CUL_CREOLE"), "GUY": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "SUR": ("REL_CHRISTIANITY", "CUL_OTHER"), "COL": ("REL_CHRISTIANITY", "CUL_MESTIZO"),
    "VEN": ("REL_CHRISTIANITY", "CUL_MESTIZO"), "ECU": ("REL_CHRISTIANITY", "CUL_MESTIZO"),
    "PER": ("REL_CHRISTIANITY", "CUL_MESTIZO"), "BOL": ("REL_CHRISTIANITY", "CUL_MESTIZO"),
    "CHL": ("REL_CHRISTIANITY", "CUL_MESTIZO"), "ARG": ("REL_CHRISTIANITY", "CUL_SPANISH"),
    "URY": ("REL_CHRISTIANITY", "CUL_SPANISH"), "PRY": ("REL_CHRISTIANITY", "CUL_MESTIZO"),
    "BRA": ("REL_CHRISTIANITY", "CUL_PORTUGUESE"),
    # --- Океания ---
    "AUS": ("REL_CHRISTIANITY", "CUL_ANGLO_AUSTRALIAN"), "NZL": ("REL_CHRISTIANITY", "CUL_ANGLO_AUSTRALIAN"),
    "PNG": ("REL_CHRISTIANITY", "CUL_OTHER"), "FJI": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "SLB": ("REL_CHRISTIANITY", "CUL_OTHER"), "VUT": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "WSM": ("REL_CHRISTIANITY", "CUL_OTHER"), "TON": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "PLW": ("REL_CHRISTIANITY", "CUL_OTHER"), "FSM": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "MHL": ("REL_CHRISTIANITY", "CUL_OTHER"), "KIR": ("REL_CHRISTIANITY", "CUL_OTHER"),
    "NRU": ("REL_CHRISTIANITY", "CUL_OTHER"), "TUV": ("REL_CHRISTIANITY", "CUL_OTHER"),
}

# Провинциальные исключения (подстрока в NameEn -> (religion, culture)) — реальные меньшинства.
PROVINCE_OVERRIDES = [
    ("Kashmir", "REL_ISLAM", "CUL_OTHER"),
    ("Punjab", "REL_SIKHISM", "CUL_PUNJABI"),
    ("Kerala", "REL_CHRISTIANITY", "CUL_TAMIL"),
    ("Xinjiang", "REL_ISLAM", "CUL_OTHER"),
    ("Tibet", "REL_BUDDHISM", "CUL_TIBETAN"),
    ("Inner Mongolia", "REL_CHINESE_FOLK", "CUL_MONGOLIAN"),
    ("Quebec", "REL_CHRISTIANITY", "CUL_FRENCH"),
    ("Cataluña", "REL_CHRISTIANITY", "CUL_CATALAN"),
    ("Catalonia", "REL_CHRISTIANITY", "CUL_CATALAN"),
    ("País Vasco", "REL_CHRISTIANITY", "CUL_BASQUE"),
    ("Basque", "REL_CHRISTIANITY", "CUL_BASQUE"),
    ("Scotland", "REL_CHRISTIANITY", "CUL_SCOTTISH"),
    ("Wales", "REL_CHRISTIANITY", "CUL_WELSH"),
    ("Northern Ireland", "REL_CHRISTIANITY", "CUL_IRISH"),
    ("Kano", "REL_ISLAM", "CUL_HAUSA"),
    ("Sokoto", "REL_ISLAM", "CUL_HAUSA"),
    ("Borno", "REL_ISLAM", "CUL_HAUSA"),
    ("Mindanao", "REL_ISLAM", "CUL_FILIPINO"),
    ("Bali", "REL_HINDUISM", "CUL_MALAY"),
    ("Wallonia", "REL_CHRISTIANITY", "CUL_FRENCH"),
    ("Ticino", "REL_CHRISTIANITY", "CUL_ITALIAN"),
    ("Tatarstan", "REL_ISLAM", "CUL_OTHER"),
    ("Bashkortostan", "REL_ISLAM", "CUL_OTHER"),
    ("Chechnya", "REL_ISLAM", "CUL_OTHER"),
    ("Dagestan", "REL_ISLAM", "CUL_OTHER"),
    ("Kurdistan", "REL_ISLAM", "CUL_KURDISH"),
    ("Aceh", "REL_ISLAM", "CUL_MALAY"),
    ("Vojvodina", "REL_CHRISTIANITY", "CUL_HUNGARIAN"),
    ("Flanders", "REL_CHRISTIANITY", "CUL_DUTCH"),
    ("Transylvania", "REL_CHRISTIANITY", "CUL_HUNGARIAN"),
]

def terrain_from_climate(climate, rng_hash):
    # rng_hash — детерминированное «случайное» число для провинции.
    if climate == 0:  # Tropical
        return 4  # Jungle
    if climate == 1:  # Arid
        return 5  # Desert
    if climate == 4:  # Polar
        return 6  # Tundra
    # Temperate (2) / Continental (3): лес либо равнина.
    return 3 if (rng_hash % 100) < 60 else 0  # Forest / Plains

def main():
    rel_ids = load_religion_ids()
    cul_ids = load_culture_ids()

    world = json.load(open(WORLD, encoding="utf-8"))
    provinces = world["Provinces"]
    countries = world["Countries"]

    # --- Привязка пиков к провинциям через id_map ---
    ids_img = np.asarray(Image.open(ID_MAP).convert("RGB")).astype(np.int32)
    prov_ids = ids_img[:, :, 0] + ids_img[:, :, 1] * 256 + ids_img[:, :, 2] * 65536 - 1
    H, W = prov_ids.shape

    elev = json.load(open(ELEV, encoding="utf-8"))
    is_mountain = np.zeros(len(provinces), dtype=bool)
    is_hill = np.zeros(len(provinces), dtype=bool)
    for f in elev["features"]:
        fc = f["properties"].get("featurecla")
        lon = f["properties"].get("long_x")
        lat = f["properties"].get("lat_y")
        if lon is None or lat is None:
            continue
        x = int(round((lon + 180.0) * SCALE))
        y = int(round((90.0 - lat) * SCALE))
        if not (0 <= x < W and 0 <= y < H):
            continue
        pid = prov_ids[y, x]
        if pid < 0:
            continue
        if fc == "mountain":
            is_mountain[pid] = True
        elif fc in ("spot elevation", "plateau"):
            is_hill[pid] = True

    # --- Террейн ---
    mountains = int(is_mountain.sum())
    hills = int(is_hill.sum())
    for p in provinces:
        pid = p["Id"]
        if is_mountain[pid]:
            p["Terrain"] = 2  # Mountains
        elif is_hill[pid]:
            p["Terrain"] = 1  # Hills
        else:
            p["Terrain"] = terrain_from_climate(p.get("Climate", 2), pid * 7919 + 13)

    # --- Религия/культура ---
    for c in countries:
        rel_key, cul_key = COUNTRY_REL_CUL.get(c["Code"], ("REL_OTHER", "CUL_OTHER"))
        c["ReligionId"] = rel_ids.get(rel_key, rel_ids["REL_OTHER"])
        c["CultureId"] = cul_ids.get(cul_key, cul_ids["CUL_OTHER"])

    # Провинции: дефолт = страна, затем исключения.
    code_to_rel = {c["Code"]: c["ReligionId"] for c in countries}
    code_to_cul = {c["Code"]: c["CultureId"] for c in countries}
    for p in provinces:
        p["ReligionId"] = code_to_rel.get(p["OwnerCode"], rel_ids["REL_OTHER"])
        p["CultureId"] = code_to_cul.get(p["OwnerCode"], cul_ids["CUL_OTHER"])

    overridden = 0
    for p in provinces:
        name = p.get("NameEn", "")
        for sub, rel_key, cul_key in PROVINCE_OVERRIDES:
            if sub.lower() in name.lower():
                p["ReligionId"] = rel_ids.get(rel_key, p["ReligionId"])
                p["CultureId"] = cul_ids.get(cul_key, p["CultureId"])
                overridden += 1
                break

    json.dump(world, open(WORLD, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
    print(f"terrain: {mountains} mountain provinces, {hills} hill provinces")
    print(f"province overrides applied: {overridden}")
    print("done ->", WORLD)

if __name__ == "__main__":
    main()

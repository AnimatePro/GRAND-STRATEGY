#!/usr/bin/env python3
"""Детальная карта сценария 1936: сетка провинций 2024 (4596, реальные внутренние
границы) с историческим владением 1936.

Подход: каждая современная страна (из world.json) сопоставляется со своим
историческим владельцем 1936 (метрополия/суверен/оккупант) через точную таблицу.
Это даёт детальную играбельную сетку + корректное историческое владение без
артефактов грубой растеризации.

Выход: data/cache/world_1936.json (та же id_map.png/border_mask.png, что и 2024).
"""
import json, csv
from collections import defaultdict

WORLD2024 = "data/cache/world.json"
OUT = "data/cache/world_1936.json"

PALETTE = ["#4E79A7","#F28E2B","#E15759","#76B7B2","#59A14F","#EDC948","#B07AA1",
           "#FF9DA7","#9C755F","#BAB0AC","#6B8E23","#7B68EE","#CD853F","#4682B4",
           "#C71585","#2E8B57","#8B0000","#DAA520","#4169E1","#A0522D"]
def color_for(code):
    h = 0
    for ch in code:
        h = (h * 31 + ord(ch)) & 0x7FFFFFFF
    return PALETTE[h % len(PALETTE)]

# Современная страна (ISO alpha-3) -> исторический владелец 1936.
# Отсутствие в таблице = identity (независима и в 1936, код тот же).
OWNER_1936 = {
    # --- Постсоветское пространство ---
    "BLR":"RUS","UKR":"RUS","AZE":"RUS","ARM":"RUS","GEO":"RUS",
    "KAZ":"RUS","UZB":"RUS","TKM":"RUS","KGZ":"RUS","TJK":"RUS",
    "MDA":"ROU",           # Бессарабия у Румынии до 1940
    # --- Югославия ---
    "HRV":"SRB","SVN":"SRB","BIH":"SRB","MNE":"SRB","MKD":"SRB","XKX":"SRB","KOS":"SRB",
    # --- Чехословакия ---
    "SVK":"CZE",
    # --- Африка: колонии ---
    # Французские
    "DZA":"FRA","TUN":"FRA","MAR":"FRA","MRT":"FRA","MLI":"FRA","NER":"FRA",
    "TCD":"FRA","BFA":"FRA","SEN":"FRA","GIN":"FRA","CIV":"FRA","BEN":"FRA",
    "TGO":"FRA","CAF":"FRA","COG":"FRA","GAB":"FRA","MDG":"FRA","DJI":"FRA","COM":"FRA",
    # Британские
    "SDN":"GBR","SOM":"GBR","KEN":"GBR","UGA":"GBR","TZA":"GBR","MWI":"GBR",
    "ZMB":"GBR","ZWE":"GBR","BWA":"GBR","LSO":"GBR","SWZ":"GBR","NGA":"GBR",
    "GHA":"GBR","SLE":"GBR","GMB":"GBR",
    # Бельгийские
    "COD":"BEL","RWA":"BEL","BDI":"BEL",
    # Португальские
    "AGO":"PRT","MOZ":"PRT","GNB":"PRT","CPV":"PRT","STP":"PRT",
    # Итальянские
    "LBY":"ITA","ERI":"ITA","ETH":"ITA",
    # Испанские
    "GNQ":"ESP","ESH":"ESP",
    # --- Азия: колонии/мандаты ---
    "IND":"GBR","PAK":"GBR","BGD":"GBR","LKA":"GBR","MMR":"GBR",
    "MYS":"GBR","BRN":"GBR","SGP":"GBR","YEM":"GBR","ISR":"GBR","JOR":"GBR",
    "KWT":"GBR","QAT":"GBR","BHR":"GBR","ARE":"GBR","OMN":"GBR",
    "VNM":"FRA","LAO":"FRA","KHM":"FRA","SYR":"FRA","LBN":"FRA",
    "IDN":"NLD","PHL":"USA","KOR":"JPN","PRK":"JPN","TWN":"JPN",
    # --- Америка: колонии ---
    "GUF":"FRA","SUR":"NLD","GUY":"GBR","BHS":"GBR","JAM":"GBR","TTO":"GBR",
    "BRB":"GBR","BLZ":"GBR","DMA":"GBR","GRD":"GBR","LCA":"GBR","VCT":"GBR",
    "ATG":"GBR","KNA":"GBR","PRI":"USA","GRL":"DNK",
    # --- Океания ---
    "PNG":"AUS","FJI":"GBR","SLB":"GBR","VUT":"GBR","NCL":"FRA","PYF":"FRA",
    "MHL":"JPN","FSM":"JPN","PLW":"JPN","MNP":"JPN",
    "HKG":"GBR","MAC":"PRT",
    # --- Антарктида и пустые ---
    "ATA":"",
}

# Религия/культура/ВВП на душу (как ранее)
REL = {"CHRIST":1,"ISLAM":2,"HINDU":3,"BUDD":4,"JEW":5,"SIKH":6,"FOLK":7,"IRREL":8,"CHFOLK":9,"SHINTO":10,"OTHER":11}
DATA = {
 "USA":("United States","CHRIST","CUL_ENGLISH",6200), "GBR":("United Kingdom","CHRIST","CUL_ENGLISH",6000),
 "FRA":("France","CHRIST","CUL_FRENCH",4700), "RUS":("Soviet Union","IRREL","CUL_RUSSIAN",2300),
 "DEU":("Germany","CHRIST","CUL_GERMAN",5100), "ITA":("Italy","CHRIST","CUL_ITALIAN",3000),
 "JPN":("Empire of Japan","SHINTO","CUL_JAPANESE",2300), "CHN":("China","CHFOLK","CUL_CHINESE",560),
 "IND":("India (British)","HINDU","CUL_HINDI",700), "ESP":("Spain","CHRIST","CUL_SPANISH",2400),
 "PRT":("Portugal","CHRIST","CUL_PORTUGUESE",2000), "NLD":("Netherlands","CHRIST","CUL_DUTCH",5900),
 "BEL":("Belgium","CHRIST","CUL_DUTCH",5500), "CHE":("Switzerland","CHRIST","CUL_GERMAN",7500),
 "SWE":("Sweden","CHRIST","CUL_SCANDINAVIAN",5700), "NOR":("Norway","CHRIST","CUL_SCANDINAVIAN",5300),
 "DNK":("Denmark","CHRIST","CUL_SCANDINAVIAN",5900), "FIN":("Finland","CHRIST","CUL_FINNISH",3400),
 "EST":("Estonia","CHRIST","CUL_BALTIC",2700), "LVA":("Latvia","CHRIST","CUL_BALTIC",2600),
 "LTU":("Lithuania","CHRIST","CUL_BALTIC",2500), "IRL":("Ireland","CHRIST","CUL_IRISH",3000),
 "ISL":("Iceland","CHRIST","CUL_SCANDINAVIAN",4500), "LUX":("Luxembourg","CHRIST","CUL_GERMAN",7500),
 "AND":("Andorra","CHRIST","CUL_CATALAN",2500), "GRC":("Greece","CHRIST","CUL_GREEK",2100),
 "TUR":("Turkey","ISLAM","CUL_TURKISH",1300), "IRN":("Iran","ISLAM","CUL_PERSIAN",1000),
 "AFG":("Afghanistan","ISLAM","CUL_PASHTUN",600), "SAU":("Saudi Arabia","ISLAM","CUL_ARABIC",900),
 "YEM":("Yemen","ISLAM","CUL_ARABIC",600), "OMN":("Muscat and Oman","ISLAM","CUL_ARABIC",700),
 "KWT":("Kuwait","ISLAM","CUL_ARABIC",800), "QAT":("Qatar","ISLAM","CUL_ARABIC",700),
 "EGY":("Egypt","ISLAM","CUL_ARABIC",800), "THA":("Siam","BUDD","CUL_THAI",800),
 "MNG":("Mongolia","BUDD","CUL_MONGOLIAN",600), "NPL":("Nepal","HINDU","CUL_NEPALI",600),
 "BTN":("Bhutan","BUDD","CUL_TIBETAN",500), "CZE":("Czechoslovakia","CHRIST","CUL_CZECH",3000),
 "HUN":("Hungary","CHRIST","CUL_HUNGARIAN",2700), "POL":("Poland","CHRIST","CUL_POLISH",2200),
 "ROU":("Romania","CHRIST","CUL_ROMANIAN",1800), "BGR":("Bulgaria","CHRIST","CUL_BULGARIAN",1600),
 "SRB":("Yugoslavia","CHRIST","CUL_SOUTH_SLAVIC",1600), "ALB":("Albania","ISLAM","CUL_ALBANIAN",900),
 "MEX":("Mexico","CHRIST","CUL_MESTIZO",2000), "CAN":("Canada","CHRIST","CUL_ENGLISH",4700),
 "AUS":("Australia","CHRIST","CUL_ANGLO_AUSTRALIAN",5000), "NZL":("New Zealand","CHRIST","CUL_ANGLO_AUSTRALIAN",5400),
 "BRA":("Brazil","CHRIST","CUL_PORTUGUESE",1300), "ARG":("Argentina","CHRIST","CUL_SPANISH",3900),
 "CHL":("Chile","CHRIST","CUL_MESTIZO",3200), "URY":("Uruguay","CHRIST","CUL_SPANISH",3600),
 "PRY":("Paraguay","CHRIST","CUL_MESTIZO",1500), "BOL":("Bolivia","CHRIST","CUL_MESTIZO",1500),
 "PER":("Peru","CHRIST","CUL_MESTIZO",2000), "ECU":("Ecuador","CHRIST","CUL_MESTIZO",1500),
 "COL":("Colombia","CHRIST","CUL_MESTIZO",1800), "VEN":("Venezuela","CHRIST","CUL_MESTIZO",3000),
 "CUB":("Cuba","CHRIST","CUL_SPANISH",2500), "DOM":("Dominican Republic","CHRIST","CUL_MESTIZO",1200),
 "HTI":("Haiti","CHRIST","CUL_CREOLE",900), "GTM":("Guatemala","CHRIST","CUL_MESTIZO",1400),
 "HND":("Honduras","CHRIST","CUL_MESTIZO",1200), "SLV":("El Salvador","CHRIST","CUL_MESTIZO",1200),
 "NIC":("Nicaragua","CHRIST","CUL_MESTIZO",1100), "CRI":("Costa Rica","CHRIST","CUL_MESTIZO",1600),
 "PAN":("Panama","CHRIST","CUL_MESTIZO",1800), "LBR":("Liberia","CHRIST","CUL_OTHER",500),
 "ZAF":("Union of South Africa","CHRIST","CUL_ENGLISH",3000), "ETH":("Ethiopia","CHRIST","CUL_AMHARIC",500),
}

def load_culture_ids():
    ids = {}
    for row in csv.DictReader(open("data/cultures.csv", encoding="utf-8")):
        ids[row["name_key"]] = int(row["id"])
    return ids
CULT = load_culture_ids()

def main():
    w = json.load(open(WORLD2024, encoding="utf-8"))

    pop1936 = {}
    for row in csv.DictReader(open("data/population_1936.csv", encoding="utf-8")):
        pop1936[row["code"]] = int(row["population"])
    leaders = {}
    for row in csv.DictReader(open("data/leaders.csv", encoding="utf-8")):
        leaders[row["code"]] = row["leader_1936"]

    # Собираем реальных владельцев (исторические коды).
    historical = set(OWNER_1936.values())
    for c in w["Countries"]:
        code = c["Code"]
        if code not in OWNER_1936 and code != "ATA":
            historical.add(code)  # identity

    # Страны 1936.
    countries = []
    code_to_cid = {}
    for code in sorted(historical):
        if not code:
            continue
        name, relkey, culkey, gdp_pc = DATA.get(code, (code, "OTHER", "CUL_OTHER", 1000))
        pop = pop1936.get(code, 0)
        cid = len(countries)
        code_to_cid[code] = cid
        countries.append({
            "Id": cid, "Code": code, "NameKey": name, "NameRu": name,
            "FlagId": code.lower(), "ColorHex": color_for(code),
            "GovernmentType": 1, "Ideology": 0, "CapitalProvinceId": -1,
            "Population": pop, "Gdp": float(pop) * gdp_pc,
            "Literacy": 0.5, "Urbanization": 0.3, "Treasury": 10000.0,
            "ReligionId": REL[relkey], "CultureId": CULT.get(culkey, CULT["CUL_OTHER"]),
        })

    # Перекраска провинций.
    area_by_hist = defaultdict(float)
    for p in w["Provinces"]:
        owner = p.get("OwnerCode", "")
        hist = OWNER_1936.get(owner, owner)  # identity если не в таблице
        if hist:
            area_by_hist[hist] += max(p.get("AreaKm2", 1.0), 1.0)

    provinces_out = []
    for p in w["Provinces"]:
        owner = p.get("OwnerCode", "")
        hist = OWNER_1936.get(owner, owner)
        np_ = dict(p)
        np_["OwnerCode"] = hist or ""
        if hist:
            np_["CoreCodes"] = [hist]
            pop = pop1936.get(hist, 0)
            share = max(p.get("AreaKm2", 1.0), 1.0) / max(area_by_hist.get(hist, 1.0), 1.0)
            np_["TotalPopulation"] = int(pop * share)
            np_["ReligionId"] = REL[DATA.get(hist, (hist, "OTHER", "CUL_OTHER", 1000))[1]]
            np_["CultureId"] = CULT.get(DATA.get(hist, (hist, "OTHER", "CUL_OTHER", 1000))[2], CULT["CUL_OTHER"])
        else:
            np_["TotalPopulation"] = 0
            np_["ReligionId"] = 11
            np_["CultureId"] = CULT["CUL_OTHER"]
            np_["CoreCodes"] = []
        provinces_out.append(np_)

    # Столицы: провинция с максимальным населением; fallback — любая провинция страны.
    best_cap = defaultdict(lambda: (None, 0))
    any_prov = {}
    for p in provinces_out:
        code = p["OwnerCode"]
        if not code:
            continue
        if code not in any_prov:
            any_prov[code] = p["Id"]
        if p["TotalPopulation"] > best_cap[code][1]:
            best_cap[code] = (p["Id"], p["TotalPopulation"])
    for c in countries:
        pid = best_cap.get(c["Code"], (None, 0))[0]
        if pid is None:
            pid = any_prov.get(c["Code"], 0)
        c["CapitalProvinceId"] = pid

    world = {"Version": 2, "MapWidthPx": w["MapWidthPx"], "MapHeightPx": w["MapHeightPx"],
             "Countries": countries, "Provinces": provinces_out}
    json.dump(world, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
    print(f"стран: {len(countries)}, провинций: {len(provinces_out)}")

if __name__ == "__main__":
    main()

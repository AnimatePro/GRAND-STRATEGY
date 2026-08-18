#!/usr/bin/env python3
"""Пост-обработка world_1938.json: правильные имена стран, реальная религия,
культура и исторический ВВП (Maddison-оценки 1936) для сценария 1936.
"""
import json, csv

WORLD = "data/cache/world_1938.json"

# Религия ID из data/religions.csv: 1 Христ, 2 Ислам, 3 Индуизм, 4 Буддизм, 5 Иудаизм,
# 6 Сикхизм, 7 Народные, 8 Атеизм, 9 Кит.народные, 10 Синто, 11 Прочие.
REL = {"CHRIST":1,"ISLAM":2,"HINDU":3,"BUDD":4,"JEW":5,"SIKH":6,"FOLK":7,"IRREL":8,"CHFOLK":9,"SHINTO":10,"OTHER":11}

def load_culture_ids():
    ids = {}
    with open("data/cultures.csv", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            ids[row["name_key"]] = int(row["id"])
    return ids

CULT = load_culture_ids()

# code -> (proper_name_1936, religion, culture_name_key, gdp_per_capita_1990intl$)
DATA = {
 "USA":("United States","CHRIST","CUL_ENGLISH",6200),
 "GBR":("United Kingdom","CHRIST","CUL_ENGLISH",6000),
 "FRA":("France","CHRIST","CUL_FRENCH",4700),
 "RUS":("Soviet Union","IRREL","CUL_RUSSIAN",2300),
 "DEU":("Germany","CHRIST","CUL_GERMAN",5100),
 "ITA":("Italy","CHRIST","CUL_ITALIAN",3000),
 "JPN":("Empire of Japan","SHINTO","CUL_JAPANESE",2300),
 "CHN":("China","CHFOLK","CUL_CHINESE",560),
 "IND":("India (British)","HINDU","CUL_HINDI",700),
 "ESP":("Spain","CHRIST","CUL_SPANISH",2400),
 "PRT":("Portugal","CHRIST","CUL_PORTUGUESE",2000),
 "NLD":("Netherlands","CHRIST","CUL_DUTCH",5900),
 "BEL":("Belgium","CHRIST","CUL_DUTCH",5500),
 "CHE":("Switzerland","CHRIST","CUL_GERMAN",7500),
 "SWE":("Sweden","CHRIST","CUL_SCANDINAVIAN",5700),
 "NOR":("Norway","CHRIST","CUL_SCANDINAVIAN",5300),
 "DNK":("Denmark","CHRIST","CUL_SCANDINAVIAN",5900),
 "FIN":("Finland","CHRIST","CUL_FINNISH",3400),
 "EST":("Estonia","CHRIST","CUL_BALTIC",2700),
 "LVA":("Latvia","CHRIST","CUL_BALTIC",2600),
 "LTU":("Lithuania","CHRIST","CUL_BALTIC",2500),
 "IRL":("Ireland","CHRIST","CUL_IRISH",3000),
 "ISL":("Iceland","CHRIST","CUL_SCANDINAVIAN",4500),
 "LUX":("Luxembourg","CHRIST","CUL_GERMAN",7500),
 "AND":("Andorra","CHRIST","CUL_CATALAN",2500),
 "GRC":("Greece","CHRIST","CUL_GREEK",2100),
 "TUR":("Turkey","ISLAM","CUL_TURKISH",1300),
 "IRN":("Iran","ISLAM","CUL_PERSIAN",1000),
 "AFG":("Afghanistan","ISLAM","CUL_PASHTUN",600),
 "SAU":("Saudi Arabia","ISLAM","CUL_ARABIC",900),
 "YEM":("Yemen","ISLAM","CUL_ARABIC",600),
 "OMN":("Muscat and Oman","ISLAM","CUL_ARABIC",700),
 "KWT":("Kuwait","ISLAM","CUL_ARABIC",800),
 "QAT":("Qatar","ISLAM","CUL_ARABIC",700),
 "EGY":("Egypt","ISLAM","CUL_ARABIC",800),
 "THA":("Siam","BUDD","CUL_THAI",800),
 "MNG":("Mongolia","BUDD","CUL_MONGOLIAN",600),
 "NPL":("Nepal","HINDU","CUL_NEPALI",600),
 "BTN":("Bhutan","BUDD","CUL_TIBETAN",500),
 "CZE":("Czechoslovakia","CHRIST","CUL_CZECH",3000),
 "HUN":("Hungary","CHRIST","CUL_HUNGARIAN",2700),
 "POL":("Poland","CHRIST","CUL_POLISH",2200),
 "ROU":("Romania","CHRIST","CUL_ROMANIAN",1800),
 "BGR":("Bulgaria","CHRIST","CUL_BULGARIAN",1600),
 "SRB":("Yugoslavia","CHRIST","CUL_SOUTH_SLAVIC",1600),
 "ALB":("Albania","ISLAM","CUL_ALBANIAN",900),
 "MEX":("Mexico","CHRIST","CUL_MESTIZO",2000),
 "CAN":("Canada","CHRIST","CUL_ENGLISH",4700),
 "AUS":("Australia","CHRIST","CUL_ANGLO_AUSTRALIAN",5000),
 "NZL":("New Zealand","CHRIST","CUL_ANGLO_AUSTRALIAN",5400),
 "BRA":("Brazil","CHRIST","CUL_PORTUGUESE",1300),
 "ARG":("Argentina","CHRIST","CUL_SPANISH",3900),
 "CHL":("Chile","CHRIST","CUL_MESTIZO",3200),
 "URY":("Uruguay","CHRIST","CUL_SPANISH",3600),
 "PRY":("Paraguay","CHRIST","CUL_MESTIZO",1500),
 "BOL":("Bolivia","CHRIST","CUL_MESTIZO",1500),
 "PER":("Peru","CHRIST","CUL_MESTIZO",2000),
 "ECU":("Ecuador","CHRIST","CUL_MESTIZO",1500),
 "COL":("Colombia","CHRIST","CUL_MESTIZO",1800),
 "VEN":("Venezuela","CHRIST","CUL_MESTIZO",3000),
 "CUB":("Cuba","CHRIST","CUL_SPANISH",2500),
 "DOM":("Dominican Republic","CHRIST","CUL_MESTIZO",1200),
 "HTI":("Haiti","CHRIST","CUL_CREOLE",900),
 "GTM":("Guatemala","CHRIST","CUL_MESTIZO",1400),
 "HND":("Honduras","CHRIST","CUL_MESTIZO",1200),
 "SLV":("El Salvador","CHRIST","CUL_MESTIZO",1200),
 "NIC":("Nicaragua","CHRIST","CUL_MESTIZO",1100),
 "CRI":("Costa Rica","CHRIST","CUL_MESTIZO",1600),
 "PAN":("Panama","CHRIST","CUL_MESTIZO",1800),
 "LBR":("Liberia","CHRIST","CUL_OTHER",500),
 "ZAF":("Union of South Africa","CHRIST","CUL_ENGLISH",3000),
 "ETH":("Ethiopia","CHRIST","CUL_AMHARIC",500),
}

def main():
    w = json.load(open(WORLD, encoding="utf-8"))
    code_to_rel = {}
    code_to_cul = {}
    for c in w["Countries"]:
        code = c["Code"]
        if code in DATA:
            name, relkey, culkey, gdp_pc = DATA[code]
            c["NameKey"] = name
            c["NameRu"] = name
            c["ReligionId"] = REL[relkey]  # ID религии напрямую
            c["CultureId"] = CULT.get(culkey, CULT["CUL_OTHER"])
            c["Gdp"] = float(c["Population"]) * gdp_pc  # ВВП = население × подушевой
            code_to_rel[code] = REL[relkey]
            code_to_cul[code] = CULT.get(culkey, CULT["CUL_OTHER"])

    # Провинции: религия/культура от страны
    for p in w["Provinces"]:
        code = p.get("OwnerCode")
        if code:
            p["ReligionId"] = code_to_rel.get(code, 0)
            p["CultureId"] = code_to_cul.get(code, 0)

    json.dump(w, open(WORLD, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
    print("страны обновлены:", len([c for c in w["Countries"] if c["Code"] in DATA]))
    print("итого стран:", len(w["Countries"]), "провинций:", len(w["Provinces"]))

if __name__ == "__main__":
    main()

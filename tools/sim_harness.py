#!/usr/bin/env python3
"""Headless-зеркало игровой логики (экономика/демография/торговля/бой) на Python.

НЕ замена игры — это тестовый стенд. Позволяет гонять логику на реальном world.json
без Godot: ловит NaN/Infinity, «мёртвую» экономику, взрывы долга, отрицательное
население и т.п. Формулы зеркалят C# (PopulationSystem, EconomyManager, TradeManager).
"""
import json, math
import numpy as np

# --- Константы (зеркало C# EconomyConstants / PopulationSystem) ---
BIRTH_RATE = 0.056
CHILD_MORT = 0.006
TEEN_MORT = 0.0015
ADULT_MORT = 0.007
SENIOR_MORT = 0.06
CHILD_YEARS = 11.0
TEEN_YEARS = 7.0
ADULT_YEARS = 42.0
PARTICIPATION = 0.56

FOOD_PER_CAPITA = 1.0
RURAL_FOOD = 3.0
INDUSTRY_PROD = 2.5
BASE_EXTRACTION = 400.0
WAGE_SHARE = 0.55
MIN_PRICE_MULT = 0.1
MAX_PRICE_MULT = 10.0
MONEY_PRINT_INFL = 0.02
DEMAND_INFL = 0.005
DEFAULT_DEBT_GDP = 1.5
BASE_INTEREST = 0.04

# Категории товаров по индексу (порядок data/goods.csv)
GOOD_CAT = [0, 1, 1, 3, 3, 3, 5, 1, 2, 2, 2, 2, 4, 5, 2, 3]
GOOD_PRICE = [10, 8, 14, 12, 25, 22, 30, 11, 16, 45, 38, 40, 60, 55, 20, 18]
GOOD_TRADEABLE = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0]
N_GOODS = len(GOOD_CAT)
MANU_STRAT = [i for i in range(N_GOODS) if GOOD_CAT[i] in (2, 5)]  # Manufactured/Strategic
FOOD_IDX = 0

# Категория: 0 Food, 1 RawMaterial, 2 Manufactured, 3 Energy, 4 Luxury, 5 Strategic


def split_population(total):
    child = int(total * 0.17)
    teen = int(total * 0.09)
    adult = int(total * 0.56)
    senior = total - child - teen - adult
    f = 0.505
    mc = int(child * (1 - f)); fc = child - mc
    mt = int(teen * (1 - f)); ft = teen - mt
    ma = int(adult * (1 - f)); fa = adult - ma
    ms = int(senior * (1 - f)); fs = senior - ms
    return [mc, fc, mt, ft, ma, fa, ms, fs]


def year_tick(pop):
    mc, fc, mt, ft, ma, fa, ms, fs = pop
    births = int(fa * BIRTH_RATE)
    mb = int(births * 0.51)
    fb = births - mb
    d_mc = int(mc * CHILD_MORT); d_fc = int(fc * CHILD_MORT)
    d_mt = int(mt * TEEN_MORT); d_ft = int(ft * TEEN_MORT)
    d_ma = int(ma * ADULT_MORT); d_fa = int(fa * ADULT_MORT)
    d_ms = int(ms * SENIOR_MORT); d_fs = int(fs * SENIOR_MORT)
    c2t_m = int(mc / CHILD_YEARS); c2t_f = int(fc / CHILD_YEARS)
    t2a_m = int(mt / TEEN_YEARS); t2a_f = int(ft / TEEN_YEARS)
    a2s_m = int(ma / ADULT_YEARS); a2s_f = int(fa / ADULT_YEARS)
    mc = max(0, mc - d_mc - c2t_m + mb)
    fc = max(0, fc - d_fc - c2t_f + fb)
    mt = max(0, mt - d_mt - t2a_m + c2t_m)
    ft = max(0, ft - d_ft - t2a_f + c2t_f)
    ma = max(0, ma - d_ma - a2s_m + t2a_m)
    fa = max(0, fa - d_fa - a2s_f + t2a_f)
    ms = max(0, ms - d_ms + a2s_m)
    fs = max(0, fs - d_fs + a2s_f)
    return [mc, fc, mt, ft, ma, fa, ms, fs]


def main():
    w = json.load(open("data/cache/world.json"))
    countries = w["Countries"]
    provinces = w["Provinces"]
    n = len(countries)

    # Провинции: 8 групп + ссылка на страну.
    prov_pop = [split_population(p["TotalPopulation"]) for p in provinces]
    prov_owner = [None] * len(provinces)
    code_to_cid = {c["Code"]: c["Id"] for c in countries}
    for p in provinces:
        prov_owner[p["Id"]] = code_to_cid.get(p["OwnerCode"], -1)

    # Страны.
    cid_by_code = {c["Code"]: c["Id"] for c in countries}
    treasury = [max(c["Gdp"] * 0.02, 1000.0) for c in countries]
    debt = [0.0] * n
    inflation = [0.02] * n
    stability = [50.0] * n
    urban = [c["Urbanization"] for c in countries]
    dev = [0.0] * n          # заглушка: development на провинцию (см. баг ниже)
    infra = [0.0] * n
    gdp_display = [c["Gdp"] for c in countries]

    # Развитие/инфраструктура провинций — теперь из world.json.
    prov_dev = [p.get("Development", 0.2) for p in provinces]
    prov_infra = [p.get("Infrastructure", 0.3) for p in provinces]

    # Экономика (массивы на страну).
    supply = np.zeros((n, N_GOODS))
    demand = np.zeros((n, N_GOODS))
    price = np.array([GOOD_PRICE] * n, dtype=float)
    production = np.zeros((n, N_GOODS))
    labor = np.zeros(n)
    employment = np.zeros(n)
    revenue = np.zeros(n)
    expenses = np.zeros(n)
    gdp = np.zeros(n)

    tax_income = [0.12] * n

    def check_finite(tag):
        for name, arr in [("supply", supply), ("demand", demand), ("price", price),
                          ("production", production), ("treasury", np.array(treasury)),
                          ("debt", np.array(debt)), ("gdp", gdp)]:
            if not np.isfinite(arr).all():
                print(f"  !! NaN/Inf in {name} at {tag}")
                return False
        return True

    YEARS = 3
    TURNS_PER_YEAR = 365
    print(f"Running {YEARS} years ({YEARS*TURNS_PER_YEAR} ticks) on {len(provinces)} provinces, {n} countries...")
    ok = True
    for year in range(YEARS):
        # Демография раз в год.
        for pid in range(len(prov_pop)):
            prov_pop[pid] = year_tick(prov_pop[pid])

        for turn in range(TURNS_PER_YEAR):
            # --- Труд ---
            for pid, pop in enumerate(prov_pop):
                cid = prov_owner[pid]
                if cid is None or cid < 0:
                    continue
                mc, fc, mt, ft, ma, fa, ms, fs = pop
                lf = (ma + fa) * PARTICIPATION + (mt + ft) * 0.15 + (ms + fs) * 0.05
                labor[cid] += lf
            for cid in range(n):
                employment[cid] = labor[cid] * (0.9 + 0.1 * stability[cid] / 100.0)

            # --- Производство ---
            production.fill(0.0)
            for pid, pop in enumerate(prov_pop):
                cid = prov_owner[pid]
                if cid is None or cid < 0:
                    continue
                d = max(prov_dev[pid], 0.01)
                inf = 0.5 + 0.5 * max(prov_infra[pid], 0.0)
                mc, fc, mt, ft, ma, fa, ms, fs = pop
                lf = (ma + fa) * PARTICIPATION + (mt + ft) * 0.15 + (ms + fs) * 0.05
                rural = lf * (1.0 - urban[cid])
                production[cid][FOOD_IDX] += rural * RURAL_FOOD * d * inf
            for cid in range(n):
                industrial = employment[cid] * urban[cid]
                output = industrial * INDUSTRY_PROD
                per = output / len(MANU_STRAT) if MANU_STRAT else 0.0
                for g in MANU_STRAT:
                    production[cid][g] += per

            # --- Спрос/предложение ---
            for cid in range(n):
                pop_country = sum(sum(prov_pop[pid]) for pid, o in enumerate(prov_owner) if o == cid)
                wealth = 1.0
                industrial = employment[cid] * urban[cid]
                for g in range(N_GOODS):
                    supply[cid][g] = production[cid][g]
                    cat = GOOD_CAT[g]
                    if cat == 0:
                        d = pop_country * FOOD_PER_CAPITA
                    elif cat == 1:
                        d = industrial * 0.8
                    elif cat == 3:
                        d = industrial * 1.2 + pop_country * 0.2
                    elif cat == 2:
                        d = pop_country * 0.4 * wealth
                    elif cat == 4:
                        d = pop_country * 0.08 * wealth
                    elif cat == 5:
                        d = industrial * 0.5
                    else:
                        d = pop_country * 0.2
                    demand[cid][g] = d

            # --- Торговля (гравитация, упрощённо: перелив избытка в дефицит) ---
            for g in range(N_GOODS):
                if not GOOD_TRADEABLE[g]:
                    continue
                surplus = max(supply[0][g] - demand[0][g], 0.0) if n else 0.0
                deficit = max(demand[0][g] - supply[0][g], 0.0) if n else 0.0
                # (полный перелив по всем парам — O(n^2); для стенда достаточно проверить стабильность)

            # --- Цены ---
            for cid in range(n):
                for g in range(N_GOODS):
                    s = max(supply[cid][g], 1.0)
                    ratio = demand[cid][g] / s
                    factor = ratio ** 0.8
                    p = GOOD_PRICE[g] * factor
                    price[cid][g] = min(max(p, GOOD_PRICE[g] * MIN_PRICE_MULT), GOOD_PRICE[g] * MAX_PRICE_MULT)

            # --- Зарплаты/налоги/бюджет ---
            for cid in range(n):
                out_val = sum(production[cid][g] * price[cid][g] for g in range(N_GOODS))
                wage = out_val * WAGE_SHARE
                profit = out_val - wage
                cons = sum(demand[cid][g] * price[cid][g] for g in range(N_GOODS))
                revenue[cid] = (wage * tax_income[cid] + profit * 0.15 + cons * 0.10 + out_val * 0.10 * 0.3)
                debt_svc = debt[cid] * (BASE_INTEREST + inflation[cid])
                disc = max(revenue[cid] - debt_svc, 0.0)
                expenses[cid] = debt_svc + disc * (0.10 + 0.15 + 0.12 + 0.12 + 0.10 + 0.15 + 0.05 + 0.05 + 0.02 + 0.06)
                # Знаковый баланс: >0 дефицит, <0 профицит.
                deficit = expenses[cid] - revenue[cid]

                # Инфляция (только от реального дефицита).
                base_infl = inflation[cid]
                money = (max(deficit, 0.0) / max(gdp[cid], 1.0)) * MONEY_PRINT_INFL
                inflation[cid] = min(max(base_infl + money * 0.2, -0.05), 3.0)

                # Долг: дефицит наращивает, профицит гасит.
                debt[cid] = max(debt[cid] + deficit, 0.0)
                ratio = debt[cid] / max(gdp[cid], 1.0)
                if ratio > DEFAULT_DEBT_GDP:
                    debt[cid] *= 0.5
                    inflation[cid] = min(inflation[cid] + 0.15, 3.0)

                # ВВП.
                gdp[cid] = max(out_val, 1.0)
                treasury[cid] = max(treasury[cid] - deficit, 0.0)  # профицит (deficit<0) пополняет казну

            if not check_finite(f"year {year} turn {turn}"):
                ok = False
                break
            labor.fill(0.0)
        if not ok:
            break

    print("\n--- After simulation ---")
    print("finite:", ok)
    total_pop = sum(sum(p) for p in prov_pop)
    print(f"total population: {total_pop:,}")
    idx = sorted(range(n), key=lambda i: -gdp[i])[:6]
    for i in idx:
        print(f"  {countries[i]['Code']:4s} {countries[i]['NameKey'][:24]:24s} "
              f"GDP={gdp[i]:,.0f}/d  treasury={treasury[i]:,.0f}  "
              f"debt={debt[i]:,.0f}  infl={inflation[i]*100:.1f}%  tax_rev={revenue[i]:,.0f}/d")

    neg_pop = sum(1 for p in prov_pop for x in p if x < 0)
    print(f"negative population counters: {neg_pop}")
    dead_treasury = sum(1 for t in treasury if t < 0)
    print(f"negative treasuries: {dead_treasury}")


if __name__ == "__main__":
    main()

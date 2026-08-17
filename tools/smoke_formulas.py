#!/usr/bin/env python3
"""Зеркало C# SimFormulas на Python + property-проверки (аналог xUnit-тестов).

Проверяет те же инварианты, что tests/GrandStrategy.Tests/SimFormulasTests.cs:
неотрицательность демографии, стабильность населения, клампы цен/инфляции/долга,
монотонность рейтинга, вероятность боя, эффективность торговли.
"""
import math
import random

# --- Зеркало SimFormulas (точная копия логики) ---
BIRTH_RATE = 0.056
CHILD_MORT = 0.006
TEEN_MORT = 0.0015
ADULT_MORT = 0.007
SENIOR_MORT = 0.06
CHILD_YEARS = 11.0
TEEN_YEARS = 7.0
ADULT_YEARS = 42.0
PARTICIPATION = 0.56
MIN_PRICE_MULT = 0.1
MAX_PRICE_MULT = 10.0
MONEY_PRINT_INFL = 0.02
DEMAND_INFL = 0.005


def clamp_int(v):
    return int(max(0, min(v, 2**31 - 1)))


def tick_demographics(pop):
    mc, fc, mt, ft, ma, fa, ms, fs = pop
    births = int(fa * BIRTH_RATE)
    mb = int(births * 0.51); fb = births - mb
    mcD = int(mc * CHILD_MORT); fcD = int(fc * CHILD_MORT)
    mtD = int(mt * TEEN_MORT); ftD = int(ft * TEEN_MORT)
    maD = int(ma * ADULT_MORT); faD = int(fa * ADULT_MORT)
    msD = int(ms * SENIOR_MORT); fsD = int(fs * SENIOR_MORT)
    mC2T = int(mc / CHILD_YEARS); fC2T = int(fc / CHILD_YEARS)
    mT2A = int(mt / TEEN_YEARS); fT2A = int(ft / TEEN_YEARS)
    mA2S = int(ma / ADULT_YEARS); fA2S = int(fa / ADULT_YEARS)
    return [
        clamp_int(mc - mcD - mC2T + mb),
        clamp_int(fc - fcD - fC2T + fb),
        clamp_int(mt - mtD - mT2A + mC2T),
        clamp_int(ft - ftD - fT2A + fC2T),
        clamp_int(ma - maD - mA2S + mT2A),
        clamp_int(fa - faD - fA2S + fT2A),
        clamp_int(ms - msD + mA2S),
        clamp_int(fs - fsD + fA2S),
    ]


def labor_force(pop):
    mc, fc, mt, ft, ma, fa, ms, fs = pop
    return (ma + fa) * PARTICIPATION + (mt + ft) * 0.15 + (ms + fs) * 0.05


def price_for(base, supply, demand, elast):
    s = max(supply, 1.0)
    factor = (demand / s) ** elast
    return max(min(base * factor, base * MAX_PRICE_MULT), base * MIN_PRICE_MULT)


def debt_next(debt, deficit):
    return max(debt + deficit, 0.0)


def inflation_step(cur, base, money, pull):
    target = base + money + pull
    return max(min(cur + (target - cur) * 0.2, 3.0), -0.05)


def interest_rate(base, ratio, infl):
    return max(min(base + ratio * 0.05 + max(infl, 0.0) * 0.3, 0.5), 0.001)


def rating_from_debt(r):
    if r < 0.3: return 0
    if r < 0.5: return 1
    if r < 0.7: return 2
    if r < 0.9: return 3
    if r < 1.1: return 4
    if r < 1.3: return 5
    if r < 1.5: return 6
    return 7


def combat_win(atk, dfn):
    t = atk + dfn
    return 0.5 if t <= 0 else atk / t


def trade_eff(rel01, agreement, sanctioned, dist):
    eff = 0.3 + 0.7 * max(min(rel01, 1.0), 0.0)
    eff *= {1: 1.5, 2: 1.3, 3: 1.8, 4: 2.0}.get(agreement, 1.0)
    if sanctioned:
        eff *= 0.4
    eff *= 1.0 / (1.0 + dist / 800.0)
    return max(min(eff, 2.0), 0.0)


def check(name, cond):
    global failures
    if not cond:
        failures += 1
        print(f"  FAIL: {name}")


failures = 0
print("Running SimFormulas smoke tests...")

# 1. Демография неотрицательна (500 лет, случайные стартовые значения).
for trial in range(200):
    pop = [random.randint(0, 100000) for _ in range(8)]
    for _ in range(500):
        pop = tick_demographics(pop)
        if any(x < 0 for x in pop):
            check("demographics non-negative", False)
            break

# 2. Стабильность населения (±20% за 100 лет).
pop = [5000, 5000, 3000, 3000, 40000, 40000, 6000, 6000]
before = sum(pop)
for _ in range(100):
    pop = tick_demographics(pop)
after = sum(pop)
check("population drift within 20%", abs(after - before) / before < 0.2)

# 3. Цены: кламп и монотонность.
check("price neutral", abs(price_for(10, 100, 100, 1.0) - 10.0) < 1e-6)
check("price cap 10x", price_for(10, 10, 1000, 1.0) <= 100.0 and price_for(10, 10, 1000, 1.0) > 10)
check("price floor 0.1x", price_for(10, 1000, 1, 1.0) >= 1.0)

# 4. Долг/инфляция/ставка/рейтинг.
check("debt never negative", debt_next(0, -100) == 0 and debt_next(100, 50) == 150)
check("inflation clamped", -0.05 <= inflation_step(0.5, 10, 10, 10) <= 3.0)
check("interest clamped", 0.001 <= interest_rate(0.04, 10, 3.0) <= 0.5)
prev = -1
for r in [0.1, 0.4, 0.6, 0.8, 1.0, 1.2, 1.4, 2.0]:
    v = rating_from_debt(r)
    check("rating monotonic", v >= prev)
    prev = v

# 5. Бой.
check("combat sums to 1", abs(combat_win(30, 70) + combat_win(70, 30) - 1.0) < 1e-6)
check("combat zero-zero 0.5", combat_win(0, 0) == 0.5)

# 6. Торговля.
check("trade clamp", 0.0 <= trade_eff(0.5, 0, False, 0) <= 2.0)
check("agreement boosts", trade_eff(1.0, 4, False, 0) > trade_eff(1.0, 0, False, 0))
check("sanctions reduce", trade_eff(1.0, 0, True, 0) < trade_eff(1.0, 0, False, 0))
check("huge distance ok", trade_eff(1.0, 4, False, 1e9) >= 0.0)

# 7. NaN-проверка на случайных входах.
for _ in range(10000):
    if math.isnan(price_for(random.random() * 100, random.random() * 1e6, random.random() * 1e6, random.random() * 3)):
        check("price NaN", False)
    if math.isnan(trade_eff(random.random(), random.randint(0, 4), random.random() < 0.5, random.random() * 1e6)):
        check("trade NaN", False)

print(f"\n{'ALL PASSED' if failures == 0 else f'{failures} FAILURES'}")
raise SystemExit(1 if failures else 0)

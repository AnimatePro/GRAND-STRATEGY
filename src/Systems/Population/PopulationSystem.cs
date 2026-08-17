using System;
using GrandStrategy.Data;

namespace GrandStrategy.Systems.Population;

/// <summary>
/// Упрощённая демография: 8 групп на провинцию (пол × 4 возрастные когорты).
/// Годовые рождения/смертность/старение + ежемесячная миграция (диффузия
/// из низкодоходных провинций в высокодоходные). Все счётчики целочисленные,
/// клампятся в [0, int.MaxValue] — отрицательное население исключено.
/// </summary>
public static class PopulationSystem
{
    // Годовые коэффициенты (настраиваются в data, здесь fallback).
    // 0.056 рождений/женщина/год ≈ TFR 2.0 (замещение; при смертности ~1.6%/год население стабильно).
    private const double BirthRate = 0.056;
    private const double ChildMortality = 0.006;
    private const double TeenMortality = 0.0015;
    private const double AdultMortality = 0.007;
    private const double SeniorMortality = 0.06;

    private const double ChildrenYears = 11.0;  // 0-10
    private const double TeenYears = 7.0;       // 11-17
    private const double AdultYears = 42.0;     // 18-59

    private const double ParticipationRate = 0.56; // доля взрослых в рабочей силе

    /// <summary>Годовой демографический проход по всем провинциям.</summary>
    public static void YearTick(WorldData world)
    {
        for (int i = 0; i < world.ProvinceCount; i++)
        {
            ProvinceData p = world.Provinces[i];
            YearTickProvince(ref p);
            world.Provinces[i] = p;
        }
    }

    private static void YearTickProvince(ref ProvinceData p)
    {
        // --- Рождения (от женской взрослой группы) ---
        long births = (long)(p.FemaleAdults * BirthRate);
        int maleBirths = (int)(births * 0.51);
        int femaleBirths = (int)(births - maleBirths);

        // --- Смертность по когортам ---
        long mcD = (long)(p.MaleChildren * ChildMortality);
        long fcD = (long)(p.FemaleChildren * ChildMortality);
        long mtD = (long)(p.MaleTeens * TeenMortality);
        long ftD = (long)(p.FemaleTeens * TeenMortality);
        long maD = (long)(p.MaleAdults * AdultMortality);
        long faD = (long)(p.FemaleAdults * AdultMortality);
        long msD = (long)(p.MaleSeniors * SeniorMortality);
        long fsD = (long)(p.FemaleSeniors * SeniorMortality);

        // --- Старение (переход в следующую когорту) ---
        long mChildToTeen = (long)(p.MaleChildren / ChildrenYears);
        long fChildToTeen = (long)(p.FemaleChildren / ChildrenYears);
        long mTeenToAdult = (long)(p.MaleTeens / TeenYears);
        long fTeenToAdult = (long)(p.FemaleTeens / TeenYears);
        long mAdultToSenior = (long)(p.MaleAdults / AdultYears);
        long fAdultToSenior = (long)(p.FemaleAdults / AdultYears);

        // --- Применение (с клампом в неотрицательные) ---
        p.MaleChildren = Clamp(p.MaleChildren - mcD - mChildToTeen + maleBirths);
        p.FemaleChildren = Clamp(p.FemaleChildren - fcD - fChildToTeen + femaleBirths);

        p.MaleTeens = Clamp(p.MaleTeens - mtD - mTeenToAdult + mChildToTeen);
        p.FemaleTeens = Clamp(p.FemaleTeens - ftD - fTeenToAdult + fChildToTeen);

        p.MaleAdults = Clamp(p.MaleAdults - maD - mAdultToSenior + mTeenToAdult);
        p.FemaleAdults = Clamp(p.FemaleAdults - faD - fAdultToSenior + fTeenToAdult);

        p.MaleSeniors = Clamp(p.MaleSeniors - msD + mAdultToSenior);
        p.FemaleSeniors = Clamp(p.FemaleSeniors - fsD + fAdultToSenior);
    }

    /// <summary>
    /// Миграция: часть населения движется из провинций с низким ВВП на душу
    /// в соседние с более высоким (диффузия). Только внутри страны по умолчанию.
    /// </summary>
    public static void MigrateTick(WorldData world, Func<ProvinceData, double> gdpPerCapita)
    {
        for (int i = 0; i < world.ProvinceCount; i++)
        {
            ProvinceData src = world.Provinces[i];
            if (src.OwnerId < 0 || src.WorkingAgePopulation < 100)
                continue;

            double srcGdpPc = gdpPerCapita(src);
            int bestNeighbor = -1;
            double bestGdpPc = srcGdpPc;

            foreach (int nid in src.NeighborIds)
            {
                ProvinceData n = world.Provinces[nid];
                if (n.OwnerId != src.OwnerId)
                    continue; // без внешней миграции в M5
                double nGdpPc = gdpPerCapita(n);
                if (nGdpPc > bestGdpPc)
                {
                    bestGdpPc = nGdpPc;
                    bestNeighbor = nid;
                }
            }

            if (bestNeighbor < 0 || bestGdpPc <= srcGdpPc * 1.02)
                continue;

            // Доля мигрантов пропорциональна разрыву, ограничена сверху.
            double gap = (bestGdpPc - srcGdpPc) / Math.Max(srcGdpPc, 1.0);
            double share = Math.Min(0.004 * gap, 0.02);
            int migrants = (int)(src.WorkingAgePopulation * share);
            if (migrants <= 0)
                continue;

            int maleMove = (int)(migrants * 0.5);
            int femaleMove = migrants - maleMove;

            ProvinceData dst = world.Provinces[bestNeighbor];
            src.MaleAdults = Clamp(src.MaleAdults - maleMove);
            src.FemaleAdults = Clamp(src.FemaleAdults - femaleMove);
            dst.MaleAdults = Clamp(dst.MaleAdults + maleMove);
            dst.FemaleAdults = Clamp(dst.FemaleAdults + femaleMove);

            world.Provinces[i] = src;
            world.Provinces[bestNeighbor] = dst;
        }
    }

    /// <summary>Рабочая сила провинции (взрослые × участие, частично подростки/пожилые).</summary>
    public static double LaborForce(in ProvinceData p) =>
        (p.MaleAdults + p.FemaleAdults) * ParticipationRate
        + (p.MaleTeens + p.FemaleTeens) * 0.15
        + (p.MaleSeniors + p.FemaleSeniors) * 0.05;

    private static int Clamp(long v) => (int)Math.Clamp(v, 0L, int.MaxValue);
}

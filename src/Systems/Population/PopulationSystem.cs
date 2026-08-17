using System;
using GrandStrategy.Data;
using GrandStrategy.SimCore;

namespace GrandStrategy.Systems.Population;

/// <summary>
/// Демография: 8 групп на провинцию (пол × 4 возрастные когорты).
/// Годовые рождения/смертность/старение (формулы — в SimFormulas) + миграция.
/// Все счётчики целочисленные и неотрицательные.
/// </summary>
public static class PopulationSystem
{
    /// <summary>Годовой демографический проход по всем провинциям.</summary>
    public static void YearTick(WorldData world)
    {
        for (int i = 0; i < world.ProvinceCount; i++)
        {
            ProvinceData p = world.Provinces[i];
            SimFormulas.TickDemographics(
                ref p.MaleChildren, ref p.FemaleChildren,
                ref p.MaleTeens, ref p.FemaleTeens,
                ref p.MaleAdults, ref p.FemaleAdults,
                ref p.MaleSeniors, ref p.FemaleSeniors);
            world.Provinces[i] = p;
        }
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
                    continue;
                double nGdpPc = gdpPerCapita(n);
                if (nGdpPc > bestGdpPc)
                {
                    bestGdpPc = nGdpPc;
                    bestNeighbor = nid;
                }
            }

            if (bestNeighbor < 0 || bestGdpPc <= srcGdpPc * 1.02)
                continue;

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

    /// <summary>Рабочая сила провинции (формула — в SimFormulas).</summary>
    public static double LaborForce(in ProvinceData p) =>
        SimFormulas.LaborForce(p.MaleChildren, p.FemaleChildren, p.MaleTeens, p.FemaleTeens,
            p.MaleAdults, p.FemaleAdults, p.MaleSeniors, p.FemaleSeniors);

    private static int Clamp(long v) => (int)Math.Clamp(v, 0L, int.MaxValue);
}

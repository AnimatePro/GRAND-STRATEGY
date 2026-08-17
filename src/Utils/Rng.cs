namespace GrandStrategy.Utils;

/// <summary>
/// Детерминированный ГПСЧ (SplitMix64). Одинаковое начальное зерно даёт
/// одинаковую последовательность — обязательное условие детерминированной симуляции.
/// Используется во всей симуляции (не для криптографии).
/// </summary>
public sealed class Rng
{
    private ulong _state;

    public Rng(long seed)
    {
        _state = unchecked((ulong)seed) + 0x9E3779B97F4A7C15UL;
        if (_state == 0)
            _state = 1;
    }

    public ulong NextU64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Равномерное [0, 1).</summary>
    public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Равномерное [min, max).</summary>
    public double NextDouble(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Целое [min, max] включительно.</summary>
    public int NextInt(int min, int max) => min + (int)(NextU64() % (uint)(max - min + 1));

    /// <summary>Гауссово (метод Бокса—Мюллера).</summary>
    public double NextGaussian(double mean = 0.0, double stdDev = 1.0)
    {
        double u1 = 1.0 - NextDouble();
        double u2 = NextDouble();
        double z = System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2);
        return mean + stdDev * z;
    }

    /// <summary>true с вероятностью p.</summary>
    public bool Chance(double p) => NextDouble() < p;
}

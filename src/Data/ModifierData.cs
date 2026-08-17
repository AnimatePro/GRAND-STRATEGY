using System.Collections.Generic;

namespace GrandStrategy.Data;

/// <summary>
/// Модификатор — временный/постоянный эффект на страну или провинцию.
/// Effects: словарь ключ-статистика -> значение. Стекинг задаётся правилом.
/// Длительность в ходах; -1 = бессрочный.
/// </summary>
public sealed class Modifier
{
    public string Id = string.Empty;
    public int Duration = -1;                 // ходы; -1 = вечный
    public string Icon = string.Empty;
    public string DescKey = string.Empty;
    public Dictionary<string, double> Effects = new();
    public string Source = string.Empty;      // откуда (событие/закон/здание)
    public ModifierStacking Stacking = ModifierStacking.Additive;
}

/// <summary>Контейнер модификаторов с агрегацией по статистике.</summary>
public sealed class ModifierList
{
    private readonly List<Modifier> _mods = new();

    public IReadOnlyList<Modifier> All => _mods;

    public void Add(Modifier mod) => _mods.Add(mod);

    public void RemoveById(string id) => _mods.RemoveAll(m => m.Id == id);

    public bool Has(string id) => _mods.Exists(m => m.Id == id);

    /// <summary>Уменьшает длительность временных модификаторов на 1 ход, удаляет истёкшие.</summary>
    public void Tick()
    {
        for (int i = _mods.Count - 1; i >= 0; i--)
        {
            Modifier m = _mods[i];
            if (m.Duration == -1)
                continue;
            m.Duration--;
            if (m.Duration <= 0)
                _mods.RemoveAt(i);
        }
    }

    /// <summary>Агрегированное значение статистики с учётом правил стекинга.</summary>
    public double Get(string stat, double baseValue = 0.0)
    {
        double additive = 0.0;
        double multiplicative = 1.0;
        double maxOverride = double.MinValue;
        bool hasOverride = false;

        foreach (Modifier m in _mods)
        {
            if (!m.Effects.TryGetValue(stat, out double v))
                continue;
            switch (m.Stacking)
            {
                case ModifierStacking.Additive:
                    additive += v;
                    break;
                case ModifierStacking.Multiplicative:
                    multiplicative *= v;
                    break;
                case ModifierStacking.Override:
                    if (v > maxOverride) { maxOverride = v; hasOverride = true; }
                    break;
            }
        }

        if (hasOverride)
            return maxOverride;
        return (baseValue + additive) * multiplicative;
    }
}

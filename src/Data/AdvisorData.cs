namespace GrandStrategy.Data;

/// <summary>Сфера компетенции советника/министра.</summary>
public enum AdvisorDomain
{
    Economy = 0,
    Military = 1,
    Diplomacy = 2,
}

/// <summary>Советник — даёт бонус в своей сфере (как командир, но для государства).</summary>
public sealed class AdvisorData
{
    public int Id;
    public int OwnerId;
    public string Name = string.Empty;
    public AdvisorDomain Domain;
    public int Skill; // 1..5
}

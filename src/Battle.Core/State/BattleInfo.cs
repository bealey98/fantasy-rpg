namespace Battle.Core;

/// <summary>
/// Facts about a battle that never change once it starts: names, the seed, and each unit's
/// label and moves. Copies of the battle state share one BattleInfo.
/// </summary>
public sealed class BattleInfo
{
    public required int BattleId { get; init; }
    public required string ArenaName { get; init; }
    public required string EncounterId { get; init; }
    public required ulong Seed { get; init; }

    /// <summary>"day" or "night".</summary>
    public required string Time { get; init; }

    /// <summary>Each unit's label in logs: "hero", "loper", then enemy letters "a", "b", ...</summary>
    public required string[] Labels { get; init; }

    /// <summary>Each unit's moves, as indices into GameData.Moves.</summary>
    public required int[][] Kits { get; init; }

    public required int HeroIndex { get; init; }

    /// <summary>The Loper's unit index, or -1 when it isn't in the fight.</summary>
    public required int LoperIndex { get; init; }

    public required int ElderwoodTier { get; init; }

    public int UnitIndex(string label) => Array.IndexOf(Labels, label);
}

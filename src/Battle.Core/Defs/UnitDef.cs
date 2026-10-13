namespace Battle.Core;

public enum Side : byte { Party, Enemy }

/// <summary>
/// Weight classes. When something pulls or pushes, the heavier side moves the lighter one;
/// equal weights hold each other in place. Objects never move.
/// </summary>
public enum Weight : byte { Light = 1, Medium = 2, Heavy = 3 }

[Flags]
public enum UnitTags : ushort
{
    None = 0,
    Hero = 1,
    Companion = 2,
    Humanoid = 4,
    Beast = 8,
    Goblin = 16,
}

/// <summary>A kind of unit, read from content/units.csv: its stats and its moves.</summary>
public sealed class UnitDef
{
    public required int Index { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Side Side { get; init; }
    public required int Hp { get; init; }

    /// <summary>Movement per turn in half-points (8 = four tiles).</summary>
    public required int Move { get; init; }

    public required Weight Weight { get; init; }
    public required int Defence { get; init; }

    /// <summary>Enemies act in order of power, strongest first.</summary>
    public required int Power { get; init; }

    public required UnitTags Tags { get; init; }

    /// <summary>Move ids from moves.json. Filled in from the units file's moves column.</summary>
    public required IReadOnlyList<string> Moves { get; init; }

    public bool Has(UnitTags tag) => (Tags & tag) != 0;
}

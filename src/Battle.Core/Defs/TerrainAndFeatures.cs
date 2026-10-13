namespace Battle.Core;

/// <summary>The five elements, as flags so one feature can feed several.</summary>
[Flags]
public enum Element : byte
{
    None = 0,
    Root = 1,
    Echo = 2,
    Kinship = 4,
    Wind = 8,
    Light = 16,
    Darkness = 32,
}

/// <summary>How much a tile blocks sight. Higher ground sees over low cover.</summary>
public enum Cover : byte { None, Low, High }

/// <summary>How a living feature moves in the creatures step between the phases.</summary>
public enum CreatureBehaviour : byte
{
    None,
    /// <summary>Wanders one tile now and then, like fireflies.</summary>
    Drift,
    /// <summary>Flies off when an attack lands nearby, like birds.</summary>
    FleeAttack,
    /// <summary>Runs when anyone comes close, like deer.</summary>
    FleeNear,
}

/// <summary>
/// A kind of ground, read from content/terrain.csv. Every tile has exactly one.
/// Movement costs are in half-points: 2 means one tile, 3 means one and a half.
/// </summary>
public sealed class TerrainDef
{
    public required int Index { get; init; }
    public required char Glyph { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Half-points of movement to step onto this tile.</summary>
    public required int MoveCost { get; init; }

    /// <summary>Can units walk onto it? Rock and no ground can't be walked onto.</summary>
    public required bool Walkable { get; init; }

    /// <summary>Anything that enters no ground dies at once.</summary>
    public required bool NoGround { get; init; }

    /// <summary>A ladder lets units climb two or more levels to or from a neighbouring tile.</summary>
    public required bool Ladder { get; init; }

    public required bool Water { get; init; }
    public required Cover Cover { get; init; }

    /// <summary>Objects never move. Units pushed into one take collision damage, and Tether can anchor to one.</summary>
    public required bool IsObject { get; init; }
}

/// <summary>
/// A natural feature, read from content/features.csv: a tree, a thicket, a stream, a flock of birds.
/// Features are what magic draws on. Light spells spend a feature's uses, which regenerate each round.
/// Dark spells drain its HP, and a drained feature dies.
/// </summary>
public sealed class FeatureDef
{
    public required int Index { get; init; }
    public required char Glyph { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Which elements it feeds.</summary>
    public required Element Feeds { get; init; }

    /// <summary>Blocks movement, like a tree.</summary>
    public required bool Blocks { get; init; }

    public required Cover Cover { get; init; }

    /// <summary>An object never moves; units pushed into it take collision damage, and Tether can anchor to it.</summary>
    public required bool IsObject { get; init; }

    /// <summary>Extra half-points to step onto its tile, like dense growth.</summary>
    public required int ExtraMoveCost { get; init; }

    /// <summary>Uses for light spells; they regenerate each round up to this number.</summary>
    public required int Uses { get; init; }

    /// <summary>Uses regained at the end of each round.</summary>
    public required int Regen { get; init; }

    /// <summary>HP that dark spells drain. Zero means dark spells can't draw on it.</summary>
    public required int Hp { get; init; }

    /// <summary>Living features move on their own in the creatures step.</summary>
    public required CreatureBehaviour Creature { get; init; }

    public bool IsCreature => Creature != CreatureBehaviour.None;
}

namespace Battle.Core;

/// <summary>Where a unit starts: its letter in the arena's units layer and its tile.</summary>
public readonly record struct UnitPlacement(char Glyph, Pos Pos);

/// <summary>
/// An arena as read from a text file in content/arenas. It never changes once loaded;
/// a battle copies it into its own state, where craters and dead trees can change the board.
/// Each layer is a flat array indexed by y * Width + x.
/// </summary>
public sealed class ArenaDef
{
    public required string Name { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>"day" or "night". It matters for Light and Darkness later on.</summary>
    public required string Time { get; init; }

    /// <summary>0 no ground, 1 low, 2 base, 3 high, 4 very high.</summary>
    public required byte[] Heights { get; init; }

    /// <summary>Index into GameData.Terrain for each tile.</summary>
    public required byte[] Terrain { get; init; }

    /// <summary>Index into GameData.Features for each tile, or -1 for none.</summary>
    public required short[] Features { get; init; }

    public required IReadOnlyList<UnitPlacement> Units { get; init; }

    public int Index(Pos p) => p.Y * Width + p.X;

    public bool InBounds(Pos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
}

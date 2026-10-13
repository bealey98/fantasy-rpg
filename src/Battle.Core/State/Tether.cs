namespace Battle.Core;

public enum TetherVariant : byte { Plain, Pull, Sap }

/// <summary>
/// A Tether between two ends. Each end is a unit, or an object tile (a tree or rock) when the
/// unit index is -1. While it lasts, the ends can't move further apart than its length unless
/// the heavier end drags the lighter one.
/// </summary>
public struct Tether
{
    public bool Active;
    public int Caster;
    public int UnitA;
    public int UnitB;
    public Pos TileA;
    public Pos TileB;

    /// <summary>The furthest apart the ends may be, in tiles (orthogonal steps).</summary>
    public int Length;

    /// <summary>Phase ends left (two per round).</summary>
    public int Timer;

    public TetherVariant Variant;

    /// <summary>For a sapping tether: HP taken from each enemy end at the end of every round.</summary>
    public int Amount;

    public readonly bool Involves(int unit) => Active && (UnitA == unit || UnitB == unit);

    /// <summary>The other end's unit index (or -1 for an object tile) for one end's unit.</summary>
    public readonly int OtherUnit(int unit) => UnitA == unit ? UnitB : UnitA;

    public readonly Pos OtherTile(int unit) => UnitA == unit ? TileB : TileA;
}

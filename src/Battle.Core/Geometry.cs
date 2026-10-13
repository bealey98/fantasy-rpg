namespace Battle.Core;

/// <summary>
/// A tile position: column X and row Y, counted from the top left, starting at 0.
/// </summary>
public readonly record struct Pos(int X, int Y)
{
    /// <summary>"No tile", used where a position is optional.</summary>
    public static readonly Pos None = new(-1, -1);

    public bool IsNone => X < 0;

    /// <summary>Distance counting only orthogonal steps (no diagonals).</summary>
    public int Manhattan(Pos other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    public Pos Step(Dir d) => new(X + d.Dx(), Y + d.Dy());

    public Pos Step(Dir d, int times) => new(X + d.Dx() * times, Y + d.Dy() * times);

    public override string ToString() => $"({X},{Y})";
}

/// <summary>The four orthogonal directions. Movement on the grid never goes diagonally.</summary>
public enum Dir : byte { North, East, South, West }

public static class Dirs
{
    /// <summary>Always visited in this order, so ties are broken the same way on every machine.</summary>
    public static readonly Dir[] All = [Dir.North, Dir.East, Dir.South, Dir.West];

    public static int Dx(this Dir d) => d switch { Dir.East => 1, Dir.West => -1, _ => 0 };

    public static int Dy(this Dir d) => d switch { Dir.South => 1, Dir.North => -1, _ => 0 };

    public static Dir Opposite(this Dir d) => d switch
    {
        Dir.North => Dir.South,
        Dir.South => Dir.North,
        Dir.East => Dir.West,
        _ => Dir.East,
    };

    /// <summary>The direction from one tile to another if they share a row or column, otherwise null.</summary>
    public static Dir? StraightToward(Pos from, Pos to)
    {
        if (from == to) return null;
        if (from.X == to.X) return to.Y < from.Y ? Dir.North : Dir.South;
        if (from.Y == to.Y) return to.X < from.X ? Dir.West : Dir.East;
        return null;
    }

    /// <summary>
    /// The direction that best points from one tile towards another, preferring the axis with the
    /// larger gap and, on a tie, the horizontal axis. Used for pushes and pulls between tiles that
    /// don't share a row or column.
    /// </summary>
    public static Dir Toward(Pos from, Pos to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Abs(dx) >= Math.Abs(dy) && dx != 0) return dx > 0 ? Dir.East : Dir.West;
        if (dy != 0) return dy > 0 ? Dir.South : Dir.North;
        return Dir.North;
    }
}

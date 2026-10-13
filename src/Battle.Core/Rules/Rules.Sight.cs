namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // Sight and range
    //   - A straight line between tile centres must be clear. Tiles it only grazes at a corner block
    //     only if both tiles beside the corner block, so sight works the same in both directions.
    //   - High cover (rock, trees, dead or alive) blocks. Low cover (thickets) blocks unless the
    //     viewer stands higher than it: higher ground sees over low cover.
    //   - Ground higher than both ends (a ridge) blocks. Units don't block sight.
    //   - Ranged attacks, and spells where it makes sense, gain +1 range per level the user stands
    //     above the target.
    // ------------------------------------------------------------------

    public static bool HasSight(BattleState s, Pos from, Pos to)
    {
        if (from == to) return true;
        int viewer = s.Heights[s.Idx(from)];
        int ridge = Math.Max(viewer, s.Heights[s.Idx(to)]);

        int dx = to.X - from.X, dy = to.Y - from.Y;
        int nx = Math.Abs(dx), ny = Math.Abs(dy);
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        int x = from.X, y = from.Y;
        int ix = 0, iy = 0;
        while (ix < nx || iy < ny)
        {
            // Which edge does the line cross next? Compared in whole numbers, so every machine agrees.
            long decision = (1L + 2 * ix) * ny - (1L + 2 * iy) * nx;
            if (decision == 0)
            {
                // Exactly through a corner: blocked only if both tiles beside the corner block.
                var side1 = new Pos(x + sx, y);
                var side2 = new Pos(x, y + sy);
                bool blocked1 = side1 != to && BlocksSight(s, side1, viewer, ridge);
                bool blocked2 = side2 != to && BlocksSight(s, side2, viewer, ridge);
                if (blocked1 && blocked2) return false;
                x += sx; y += sy; ix++; iy++;
            }
            else if (decision < 0) { x += sx; ix++; }
            else { y += sy; iy++; }

            var p = new Pos(x, y);
            if (p == to) break;
            if (BlocksSight(s, p, viewer, ridge)) return false;
        }
        return true;
    }

    private static bool BlocksSight(BattleState s, Pos p, int viewerHeight, int ridgeHeight)
    {
        int tile = s.Idx(p);
        int h = s.Heights[tile];
        if (h > ridgeHeight) return true;
        var cover = s.TerrainAt(tile).Cover;
        var feature = s.FeatureAt(tile);
        if (feature != null && feature.Cover > cover) cover = feature.Cover;
        return cover switch
        {
            Cover.High => true,
            Cover.Low => viewerHeight <= h,
            _ => false,
        };
    }

    /// <summary>A move's reach from one tile to another, with +1 per level of height advantage if it gets that bonus.</summary>
    public static int RangeFrom(BattleState s, MoveDef move, Pos from, Pos to)
    {
        int range = move.Range;
        if (move.HeightBonus) range += Math.Max(0, s.Heights[s.Idx(from)] - s.Heights[s.Idx(to)]);
        return range;
    }

    /// <summary>Within the move's range (and sight, if it needs it)?</summary>
    public static bool InReach(BattleState s, MoveDef move, Pos from, Pos to) =>
        from.Manhattan(to) <= RangeFrom(s, move, from, to) && (!move.NeedsSight || HasSight(s, from, to));
}

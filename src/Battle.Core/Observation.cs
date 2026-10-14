namespace Battle.Core;

/// <summary>
/// What a side can see. Abilities are known and decisions are not, as in chess: the player can
/// inspect any enemy and see its danger zone, every tile it could hit next turn.
/// </summary>
public static class Observation
{
    /// <summary>
    /// Every tile that a living unit not on <paramref name="threatened"/>'s side (or a wild beast) could
    /// hit on its next turn, from anywhere it could walk to. A burrowed beast threatens the blast around
    /// its rumbling tile. Terrain combos (pushes into hazards) aren't included.
    /// </summary>
    public static bool[] Danger(BattleState s, Side threatened)
    {
        var danger = new bool[s.TileCount];
        for (int i = 0; i < s.Units.Length; i++)
        {
            if (!s.Units[i].Alive || (s.Units[i].Side == threatened && !Rules.IsWild(s, i))) continue;
            MarkThreat(s, i, danger);
        }
        return danger;
    }

    /// <summary>The danger zone of one unit.</summary>
    public static bool[] DangerOf(BattleState s, int unit)
    {
        var danger = new bool[s.TileCount];
        if (s.Units[unit].Alive) MarkThreat(s, unit, danger);
        return danger;
    }

    private static void MarkThreat(BattleState s, int unit, bool[] danger)
    {
        // Underground, a beast can only burst out at its rumbling tile: the blast around it is the danger.
        if (s.Units[unit].Has(UnitFlags.Burrowed))
        {
            var burrow = Rules.KitMove(s, unit, MoveKind.Burrow);
            var at = s.Units[unit].BurrowTo;
            for (int tile = 0; tile < s.TileCount; tile++)
                if (s.PosOf(tile).Manhattan(at) <= (burrow?.Radius ?? 0)) danger[tile] = true;
            return;
        }
        if (!s.Units[unit].OnBoard) return;

        // Work on a copy so the unit's next turn can be set up without touching the real battle:
        // its actions are free again, minus what its statuses will cost it.
        var copy = s.Clone();
        ref var u = ref copy.Units[unit];
        u.Set(UnitFlags.MainUsed | UnitFlags.BonusUsed | UnitFlags.Moved, false);
        if (u.Has(UnitFlags.Dazed)) u.Set(UnitFlags.BonusUsed);
        int drain = u.Has(UnitFlags.KnockedOver) ? copy.Data.Rules.KnockedOverDrain : 0;
        u.MoveLeft = Math.Max(0, Rules.TurnMovement(copy, unit) - drain) + Rules.ExtraMovementAvailable(copy, unit);
        var standing = new List<Pos> { u.Pos };
        var tiles = new List<Pos>();
        if (u.MoveLeft > 0 && Rules.CanMove(copy, unit))
        {
            Rules.ReachableTiles(copy, unit, tiles);
            standing.AddRange(tiles);
        }
        foreach (int m in s.Info.Kits[unit])
        {
            var move = s.Data.Moves[m];
            foreach (var from in standing) Rules.MarkThreat(copy, unit, move, from, danger);
        }
    }
}

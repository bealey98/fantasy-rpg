namespace Battle.Core;

/// <summary>
/// What a side can see. Abilities are known and decisions are not, as in chess: the player can
/// inspect any enemy and see its danger zone, every tile it could hit next turn.
/// </summary>
public static class Observation
{
    /// <summary>
    /// Every tile that a living unit not on <paramref name="threatened"/>'s side could hit on its next
    /// turn, from anywhere it could walk to. Terrain combos (pushes into hazards) aren't included.
    /// </summary>
    public static bool[] Danger(BattleState s, Side threatened)
    {
        var danger = new bool[s.TileCount];
        for (int i = 0; i < s.Units.Length; i++)
        {
            if (s.Units[i].Side == threatened || !s.Units[i].Alive || !s.Units[i].OnBoard) continue;
            MarkThreat(s, i, danger);
        }
        return danger;
    }

    /// <summary>The danger zone of one unit.</summary>
    public static bool[] DangerOf(BattleState s, int unit)
    {
        var danger = new bool[s.TileCount];
        if (s.Units[unit].Alive && s.Units[unit].OnBoard) MarkThreat(s, unit, danger);
        return danger;
    }

    private static void MarkThreat(BattleState s, int unit, bool[] danger)
    {
        // Work on a copy so the unit's movement can be refilled without touching the real battle.
        var copy = s.Clone();
        ref var u = ref copy.Units[unit];
        u.MoveLeft = Rules.TurnMovement(copy, unit) + Rules.ExtraMovementAvailable(copy, unit);
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

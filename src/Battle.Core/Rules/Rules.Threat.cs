namespace Battle.Core;

public static partial class Rules
{
    /// <summary>
    /// Marks the tiles a move could hit if its user stood on <paramref name="from"/>. Used for danger
    /// zones, which show what an enemy could do without saying what it will do.
    /// </summary>
    public static void MarkThreat(BattleState s, int unit, MoveDef move, Pos from, bool[] mark)
    {
        switch (move.Kind)
        {
            case MoveKind.Melee:
                foreach (var d in Dirs.All)
                {
                    var p = from.Step(d);
                    if (s.InBounds(p) && MeleeReach(s, from, p)) mark[s.Idx(p)] = true;
                }
                break;
            default:
                MarkKindThreat(s, unit, move, from, mark);
                break;
        }
    }

    /// <summary>Movement a unit could add this turn on top of its normal allowance (Scamper, later).</summary>
    public static int ExtraMovementAvailable(BattleState s, int unit) => 0;
}

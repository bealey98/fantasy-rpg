namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // The switchboard: which rule handles each kind of move.
    // Melee lives in Rules.Combat.cs; the other kinds join as their build steps land.
    // ------------------------------------------------------------------

    private static void KindTargets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        switch (move.Kind)
        {
            case MoveKind.Charge: ChargeTargets(s, actor, move, output); break;
            case MoveKind.Push: PushTargets(s, actor, output); break;
            case MoveKind.Parry: ParryTargets(s, actor, output); break;
            case MoveKind.Bind: BindTargets(s, actor, move, output); break;
            case MoveKind.Tether: TetherTargets(s, actor, move, output); break;
            case MoveKind.Taunt: TauntTargets(s, actor, move, output); break;
            case MoveKind.Brace: BraceTargets(s, actor, output); break;
            case MoveKind.Ranged: RangedTargets(s, actor, move, output); break;
            case MoveKind.Swap: SwapTargets(s, actor, output); break;
            case MoveKind.Scamper: ScamperTargets(s, actor, output); break;
            case MoveKind.Trap: TrapTargets(s, actor, move, output); break;
            case MoveKind.Point: PointTargets(s, actor, move, output); break;
            case MoveKind.Whip: WhipTargets(s, actor, move, output); break;
            case MoveKind.Burrow: BurrowTargets(s, actor, move, output); break;
        }
    }

    private static void ExecuteKind(BattleState s, in BattleAction a, MoveDef move)
    {
        switch (move.Kind)
        {
            case MoveKind.Charge: ExecuteCharge(s, a, move); break;
            case MoveKind.Push: ExecutePush(s, a, move); break;
            case MoveKind.Parry: ExecuteParry(s, a, move); break;
            case MoveKind.Bind: ExecuteBind(s, a, move); break;
            case MoveKind.Tether: ExecuteTether(s, a, move); break;
            case MoveKind.Taunt: ExecuteTaunt(s, a, move); break;
            case MoveKind.Brace: ExecuteBrace(s, a, move); break;
            case MoveKind.Ranged: Attack(s, a.Actor, a.Target.Unit, move, a.Timing, AttackKind.Ranged); break;
            case MoveKind.Swap: ExecuteSwap(s, a, move); break;
            case MoveKind.Scamper: ExecuteScamper(s, a, move); break;
            case MoveKind.Trap: ExecuteTrap(s, a, move); break;
            case MoveKind.Point: ExecutePoint(s, a, move); break;
            case MoveKind.Whip: ExecuteWhip(s, a, move); break;
            case MoveKind.Burrow: ExecuteBurrow(s, a, move); break;
        }
    }

    /// <summary>Danger zones for each kind of move.</summary>
    private static void MarkKindThreat(BattleState s, int unit, MoveDef move, Pos from, bool[] mark)
    {
        switch (move.Kind)
        {
            case MoveKind.Charge:
                MarkChargeThreat(s, move, from, mark);
                break;
            case MoveKind.Push:
                foreach (var d in Dirs.All)
                {
                    var p = from.Step(d);
                    if (s.InBounds(p) && MeleeReach(s, from, p)) mark[s.Idx(p)] = true;
                }
                break;
            case MoveKind.Ranged:
                for (int tile = 0; tile < s.TileCount; tile++)
                {
                    var p = s.PosOf(tile);
                    if (p != from && InReach(s, move, from, p)) mark[tile] = true;
                }
                break;
            case MoveKind.Burrow:
                // Nothing yet: a beast that digs in this turn bursts out on its next one, and by then
                // the danger zone shows the blast around its rumbling tile (see Observation).
                break;
        }
    }

    /// <summary>
    /// Moves that need more than a free action slot. Dark moves need something to pay with, and a
    /// fleeing goblin can only scamper.
    /// </summary>
    private static bool ExtraAvailability(BattleState s, int actor, MoveDef move)
    {
        if (move.Source == SourceKind.Dark && !CanPayDark(s, actor, move)) return false;
        return FleeingAllows(s, actor, move);
    }

    /// <summary>Can an area move hurt the party? Only a Burrow bursting out near them.</summary>
    private static bool AreaCanHurtParty(BattleState s, in BattleAction a, MoveDef move) =>
        move.Kind == MoveKind.Burrow && BurrowCanHurtParty(s, a, move);

}

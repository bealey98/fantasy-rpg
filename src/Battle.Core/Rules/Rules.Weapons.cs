namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // Weapon moves (the hero's, and most humanoid enemies' in time)
    //   Strike     a melee hit (Rules.Combat.cs)
    //   Bull Rush  charge in a straight line, then a slightly weaker hit that knocks lighter enemies back
    //   Shove      push a neighbouring enemy; lighter targets go further
    //   Parry      ready a block against the next melee attack; good timing strikes back
    // Charges are shared with the Loper's Gore.
    // ------------------------------------------------------------------

    /// <summary>
    /// Charge targets: enemies in a straight line, reached by stepping over empty, walkable tiles
    /// with no trap and no cliff (one level up or down at most per step). A rooted or braced unit
    /// can only hit what's already next to it.
    /// </summary>
    private static void ChargeTargets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        var start = s.Units[actor].Pos;
        int reach = Anchored(s, actor) ? 0 : move.Charge;
        foreach (var dir in Dirs.All)
        {
            var landing = start;
            for (int dist = 1; dist <= reach + 1; dist++)
            {
                var p = start.Step(dir, dist);
                if (!s.InBounds(p)) break;
                if (s.Occupant[s.Idx(p)] >= 0)
                {
                    if (MeleeReach(s, landing, p)) AddEnemiesOn(s, actor, p, output);
                    break;
                }
                if (dist > reach || !CanChargeOnto(s, actor, landing, p)) break;
                landing = p;
            }
        }
    }

    private static bool CanChargeOnto(BattleState s, int actor, Pos from, Pos to)
    {
        int tile = s.Idx(to);
        return s.Standable(tile)
            && s.Trap[tile] == TrapKind.None
            && Math.Abs(s.Heights[tile] - s.Heights[s.Idx(from)]) <= 1
            && TetherAllows(s, actor, to);
    }

    private static void ExecuteCharge(BattleState s, in BattleAction a, MoveDef move)
    {
        int actor = a.Actor, target = a.Target.Unit;
        var from = s.Units[actor].Pos;
        var targetPos = s.Units[target].Pos;
        var dir = Dirs.StraightToward(from, targetPos) ?? Dir.North;
        var landing = targetPos.Step(dir.Opposite());
        if (landing != from)
        {
            s.Place(actor, landing);
            s.Emit(EventType.Charge, actor, s.Body(actor), from, landing, from.Manhattan(landing), move: move.Index);
            Arrive(s, s.Body(actor), from);
        }

        bool hit = Attack(s, actor, target, move, a.Timing, AttackKind.Melee);
        if (hit && move.Push >= 0 && s.Units[target].Alive)
            Push(s, actor, target, dir, PushDistance(s, actor, target, move.Push));
    }

    /// <summary>Shove targets: enemies on neighbouring tiles within melee reach.</summary>
    private static void PushTargets(BattleState s, int actor, List<(Target, Target)> output) => MeleeTargets(s, actor, output);

    private static void ExecutePush(BattleState s, in BattleAction a, MoveDef move)
    {
        int actor = a.Actor, target = a.Target.Unit;
        var dir = Dirs.StraightToward(s.Units[actor].Pos, s.Units[target].Pos) ?? Dir.North;
        bool hit = Attack(s, actor, target, move, a.Timing, AttackKind.Melee);
        if (hit && s.Units[target].Alive)
            Push(s, actor, target, dir, PushDistance(s, actor, target, Math.Max(0, move.Push)));
    }

    /// <summary>Parry is readied on yourself, once.</summary>
    private static void ParryTargets(BattleState s, int actor, List<(Target, Target)> output)
    {
        if (!s.Units[actor].Has(UnitFlags.ParryReady)) output.Add((Target.Self, Target.None));
    }

    private static void ExecuteParry(BattleState s, in BattleAction a, MoveDef move)
    {
        ref var u = ref s.Units[a.Actor];
        u.Set(UnitFlags.ParryReady);
        s.Emit(EventType.Status, a.Actor, a.Actor, u.Pos, move: move.Index, detail: "parry_ready");
    }

    /// <summary>Danger zone for a charge: straight lines out to the charge's reach.</summary>
    private static void MarkChargeThreat(BattleState s, MoveDef move, Pos from, bool[] mark)
    {
        foreach (var dir in Dirs.All)
            for (int dist = 1; dist <= move.Charge + 1; dist++)
            {
                var p = from.Step(dir, dist);
                if (!s.InBounds(p) || !s.Standable(s.Idx(p))) break;
                mark[s.Idx(p)] = true;
            }
    }
}

namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // Beasts (the Snuffler)
    //
    // A beast has no mind of its own. It acts straight after its handler, as one joint turn, and
    // does what its orders say by these fixed rules, so the handler makes every decision:
    //   1. Told to dig to a tile this turn (Point at a tile), it burrows there if it can.
    //   2. Taunted, it bites its taunter if it can walk up to it this turn.
    //   3. Its target (the unit it was last pointed at) it bites if it can walk up to it this turn.
    //      Otherwise it burrows next to it, if that's within Burrow's range. Otherwise it walks
    //      towards it, and burrows after all if the walk brings it in range.
    //   4. With no target (it was never given one, or its target has left the fight) it does nothing.
    // Wild (its handler gone), it picks the nearest unit of either side as its target at the start
    // of each turn. Underground, it can only burst out. It never stops on a trap.
    //
    // The legal list for a beast holds just the one action these rules give, so a script, a
    // network or a replayed log can only make it do that.
    // ------------------------------------------------------------------

    [ThreadStatic] private static List<(Target, Target)>? _beastOptions;
    [ThreadStatic] private static bool[]? _beastGoals;
    [ThreadStatic] private static int[]? _beastField;

    /// <summary>Does this unit act by fixed rules rather than its own choices? Any beast with a handler, wild or not.</summary>
    public static bool ActsByRule(BattleState s, int unit) => s.Units[unit].Handler >= 0;

    /// <summary>The one thing a beast does next, by the rules above. Ending its turn when there's nothing to do.</summary>
    public static BattleAction BeastAction(BattleState s, int beast)
    {
        ref var b = ref s.Units[beast];
        var end = BattleAction.End(beast);
        if (b.Has(UnitFlags.Burrowed))
        {
            var surface = KitMove(s, beast, MoveKind.Burrow);
            return surface != null && MoveAvailable(s, beast, surface) ? BattleAction.Use(beast, surface, Target.OfTile(b.BurrowTo)) : end;
        }
        if (!b.OnBoard || b.Has(UnitFlags.MainUsed)) return end;

        var options = _beastOptions ??= new List<(Target, Target)>();
        var bite = ReadyMove(s, beast, MoveKind.Melee);
        var burrow = ReadyMove(s, beast, MoveKind.Burrow);

        // 1. A dig order.
        if (b.OrderTile != Pos.None && burrow != null)
        {
            Targets(s, beast, burrow, options);
            var dig = Target.OfTile(b.OrderTile);
            if (options.Contains((dig, Target.None))) return BattleAction.Use(beast, burrow, dig);
        }

        // 2. A taunt.
        if (bite != null && b.Taunted > 0 && b.TauntBy >= 0 && s.Units[b.TauntBy].Alive && s.Units[b.TauntBy].OnBoard
            && BiteStep(s, beast, bite, b.TauntBy) is BattleAction atTaunter)
            return atTaunter;

        // 3. Its target; 4. nothing without one.
        int target = b.OrderUnit;
        if (target < 0 || !s.Units[target].Alive || !s.Units[target].OnBoard || !AreEnemies(s, beast, target)) return end;
        if (bite != null && BiteStep(s, beast, bite, target) is BattleAction atTarget) return atTarget;
        if (burrow != null && BurrowNextTo(s, beast, burrow, target) is BattleAction under) return under;
        return WalkTowards(s, beast, target) ?? end;
    }

    /// <summary>A move of this kind in the unit's kit that it could use right now, or null.</summary>
    private static MoveDef? ReadyMove(BattleState s, int unit, MoveKind kind)
    {
        foreach (int m in s.Info.Kits[unit])
        {
            var move = s.Data.Moves[m];
            if (move.Kind == kind && MoveAvailable(s, unit, move)) return move;
        }
        return null;
    }

    /// <summary>
    /// The next step to biting a unit this turn: the bite, if it's in reach, or else the walk to the
    /// cheapest tile it could bite from (ties to the lowest tile number). Null if it can't get there.
    /// </summary>
    private static BattleAction? BiteStep(BattleState s, int beast, MoveDef bite, int victim)
    {
        var options = _beastOptions!;
        Targets(s, beast, bite, options);
        if (options.Contains((Target.OfUnit(victim), Target.None))) return BattleAction.Use(beast, bite, Target.OfUnit(victim));
        if (!CanMove(s, beast)) return null;

        var at = s.Units[victim].Pos;
        ComputeReach(s, beast, s.Units[beast].MoveLeft);
        var dist = S.Dist;
        int best = -1;
        foreach (var d in Dirs.All)
        {
            var p = at.Step(d);
            if (!s.InBounds(p) || !MeleeReach(s, p, at)) continue;
            int tile = s.Idx(p);
            if (dist[tile] == Unreachable || s.Trap[tile] != TrapKind.None || !CanEndOn(s, beast, tile)) continue;
            if (best < 0 || dist[tile] < dist[best] || (dist[tile] == dist[best] && tile < best)) best = tile;
        }
        return best < 0 ? null : BattleAction.MoveTo(beast, s.PosOf(best));
    }

    /// <summary>
    /// Burrowing to a tile from which bursting out would hit the unit: of those it can dig to, the
    /// one whose blast catches the most of its enemies and the fewest of its own side.
    /// </summary>
    private static BattleAction? BurrowNextTo(BattleState s, int beast, MoveDef burrow, int victim)
    {
        var options = _beastOptions!;
        Targets(s, beast, burrow, options);
        var at = s.Units[victim].Pos;
        var best = Target.None;
        int bestScore = int.MinValue;
        foreach (var (t, _) in options)
        {
            if (t.Kind != TargetKind.Tile || t.Tile.Manhattan(at) > burrow.Radius) continue;
            int score = 0;
            for (int i = 0; i < s.Units.Length; i++)
            {
                ref var u = ref s.Units[i];
                if (i == beast || !u.Alive || !u.OnBoard || u.Pos.Manhattan(t.Tile) > burrow.Radius) continue;
                score += AreEnemies(s, beast, i) ? 1 : -1;
            }
            if (score > bestScore) { bestScore = score; best = t; }
        }
        return best.Kind == TargetKind.Tile ? BattleAction.Use(beast, burrow, best) : null;
    }

    /// <summary>
    /// Walking towards a unit: to the tile it can reach this turn that leaves it the shortest walk to
    /// a tile next to the unit (ties to the cheaper walk now, then the lowest tile number). Null if
    /// no tile gets it any closer.
    /// </summary>
    private static BattleAction? WalkTowards(BattleState s, int beast, int victim)
    {
        if (!CanMove(s, beast)) return null;
        int n = s.TileCount;
        var goals = _beastGoals is { } g && g.Length >= n ? g : _beastGoals = new bool[Math.Max(n, 1024)];
        var field = _beastField is { } f && f.Length >= n ? f : _beastField = new int[Math.Max(n, 1024)];
        Array.Clear(goals, 0, n);
        var at = s.Units[victim].Pos;
        foreach (var d in Dirs.All)
        {
            var p = at.Step(d);
            if (!s.InBounds(p) || !MeleeReach(s, p, at)) continue;
            int tile = s.Idx(p);
            if (s.Trap[tile] == TrapKind.None && (s.Occupant[tile] == beast || CanEndOn(s, beast, tile))) goals[tile] = true;
        }
        CostToGo(s, beast, goals, field);

        ComputeReach(s, beast, s.Units[beast].MoveLeft);
        var dist = S.Dist;
        int start = s.Idx(s.Units[beast].Pos);
        int best = -1, bestField = field[start];
        for (int tile = 0; tile < n; tile++)
        {
            if (tile == start || dist[tile] == Unreachable || s.Trap[tile] != TrapKind.None || !CanEndOn(s, beast, tile)) continue;
            int left = field[tile];
            if (left == Unreachable) continue;
            if (left < bestField || (left == bestField && best >= 0 && dist[tile] < dist[best]))
            {
                bestField = left;
                best = tile;
            }
        }
        return best < 0 ? null : BattleAction.MoveTo(beast, s.PosOf(best));
    }

    /// <summary>
    /// A wild beast's target, picked at the start of its turn: the unit it could walk up to most
    /// cheaply, of either side (ties to the lower unit number). If it can walk up to none, the
    /// nearest in straight steps.
    /// </summary>
    private static void PickWildTarget(BattleState s, int beast)
    {
        ref var b = ref s.Units[beast];
        b.OrderTile = Pos.None;
        if (!b.OnBoard) return;
        ComputeReach(s, beast, 1 << 20);
        var dist = S.Dist;
        int best = -1;
        long bestCost = long.MaxValue;
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var t = ref s.Units[i];
            if (!t.Alive || !t.OnBoard || !AreEnemies(s, beast, i)) continue;
            long cost = long.MaxValue;
            if (b.Pos.Manhattan(t.Pos) == 1 && MeleeReach(s, b.Pos, t.Pos)) cost = 0;
            else
                foreach (var d in Dirs.All)
                {
                    var p = t.Pos.Step(d);
                    if (s.InBounds(p) && MeleeReach(s, p, t.Pos) && dist[s.Idx(p)] != Unreachable) cost = Math.Min(cost, dist[s.Idx(p)]);
                }
            if (cost == long.MaxValue) cost = (1L << 40) + b.Pos.Manhattan(t.Pos);
            if (cost < bestCost) { bestCost = cost; best = i; }
        }
        if (best == b.OrderUnit) return;
        b.OrderUnit = best;
        if (best >= 0) s.Emit(EventType.Order, beast, best, detail: "wild");
    }
}

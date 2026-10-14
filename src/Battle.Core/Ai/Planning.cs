namespace Battle.Core.Ai;

/// <summary>
/// A controller that plans the rest of a unit's turn, then hands out the steps one at a time.
/// When a step stops being legal (a trap sprang, a target fell) or the plan runs out, it plans again
/// from the battle as it now stands, so a plan can be short: "walk there and hit that", then look
/// again for a bonus action. Steps are always picked from the legal list, and the battle itself is
/// never changed: looking ahead happens on copies.
/// </summary>
public abstract class PlannedController : IController
{
    private long _turn = -1;
    private readonly List<BattleAction> _plan = new();
    private int _next;
    private bool _fresh;

    public abstract string Name { get; }

    /// <summary>Plans the rest of the actor's turn from the battle as it stands. An empty plan ends the turn.</summary>
    protected abstract void Plan(BattleState s, int actor, List<BattleAction> plan);

    public BattleAction Choose(BattleState s, int actor, IReadOnlyList<BattleAction> legal)
    {
        long turn = ((long)s.Round << 32) | ((long)s.Phase << 28) | ((long)s.OrderPos << 16) | (uint)actor;
        if (turn != _turn)
        {
            _turn = turn;
            Replan(s, actor);
        }
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (_next < _plan.Count)
            {
                var step = _plan[_next];
                for (int i = 0; i < legal.Count; i++)
                {
                    if (!legal[i].SameChoice(step)) continue;
                    _next++;
                    _fresh = false;
                    return legal[i];
                }
            }
            else if (_fresh) break; // a new plan with nothing left to do
            Replan(s, actor);
        }
        return Finish(legal);
    }

    private void Replan(BattleState s, int actor)
    {
        _plan.Clear();
        _next = 0;
        _fresh = true;
        Plan(s, actor, _plan);
    }

    /// <summary>Ends the turn, or, for a unit that has to act first (a burrowed beast), takes its one action.</summary>
    protected static BattleAction Finish(IReadOnlyList<BattleAction> legal)
    {
        for (int i = legal.Count - 1; i >= 0; i--)
            if (legal[i].Kind == ActionKind.End) return legal[i];
        return legal[0];
    }
}

/// <summary>
/// Tools the hand-written scripts share. None of them change the battle.
///   - Where a unit could stand this turn, and how far every tile is from a goal by its movement rules.
///   - What a unit could hit from a tile, found by trying the tile on a scratch copy of the battle.
///   - Rough estimates of damage and danger, for scoring choices.
/// Scores are whole numbers (about ten to a point of damage), so every machine makes the same choices.
/// </summary>
public sealed class Tactics
{
    public const int Far = Rules.Unreachable;

    private readonly int[] _heap = new int[4 * 4096];
    private readonly List<(Target, Target)> _targets = new();

    // ------------------------------------------------------------------
    // Where a unit could go
    // ------------------------------------------------------------------

    /// <summary>
    /// The tiles a unit could finish this turn on with this much movement (half-points), starting
    /// with the one it stands on. Tiles with traps are left out unless asked for.
    /// </summary>
    public static void Standing(BattleState s, int unit, int movement, List<Pos> output, bool includeTraps = false)
    {
        output.Clear();
        ref var u = ref s.Units[unit];
        if (!u.OnBoard) return;
        output.Add(u.Pos);
        if (movement <= 0 || Rules.Anchored(s, unit)) return;
        Rules.ComputeReach(s, unit, movement);
        int start = s.Idx(u.Pos);
        for (int tile = 0; tile < s.TileCount; tile++)
        {
            if (tile == start || (!includeTraps && s.Trap[tile] != TrapKind.None)) continue;
            if (Rules.ReachCost(s.PosOf(tile)) != Far && Rules.CanEndOn(s, unit, tile)) output.Add(s.PosOf(tile));
        }
    }

    /// <summary>The movement cost from the unit to every tile, ignoring how much movement it has.</summary>
    public static void ReachFrom(BattleState s, int unit, int[] into)
    {
        Rules.ComputeReach(s, unit, 1 << 20);
        for (int tile = 0; tile < s.TileCount; tile++) into[tile] = Rules.ReachCost(s.PosOf(tile));
    }

    /// <summary>
    /// For every tile, the cheapest movement (half-points) for this unit to get from there onto one
    /// of the goal tiles by its own movement rules, or Far. Traps stop movement, so paths don't run
    /// through them, unless <paramref name="trapCost"/> says what stopping on one is worth (a turn
    /// lost, a bite taken) for a unit willing to.
    /// </summary>
    public void CostToGo(BattleState s, int unit, bool[] goals, int[] field, int trapCost = -1)
    {
        int size = 0;
        for (int tile = 0; tile < s.TileCount; tile++)
        {
            field[tile] = Far;
            if (!goals[tile]) continue;
            field[tile] = 0;
            HeapPush(ref size, tile);
        }
        while (size > 0)
        {
            int key = HeapPop(ref size);
            int tile = key & 1023, cost = key >> 10;
            if (cost > field[tile]) continue;
            bool trap = !goals[tile] && s.Trap[tile] != TrapKind.None;
            if (trap && trapCost < 0) continue;
            int extra = trap ? trapCost : 0;
            var here = s.PosOf(tile);
            foreach (var d in Dirs.All)
            {
                var before = here.Step(d);
                if (!s.InBounds(before)) continue;
                int b = s.Idx(before);
                int step = Rules.StepCost(s, unit, b, tile);
                if (step < 0 || cost + step + extra >= field[b]) continue;
                field[b] = cost + step + extra;
                HeapPush(ref size, (field[b] << 10) | b);
            }
        }
    }

    /// <summary>
    /// The cheapest cost from a reach map (see <see cref="ReachFrom"/>) to a tile next to
    /// <paramref name="target"/>: how much walking it takes to get into melee with it. 0 if already there.
    /// </summary>
    public static int ApproachCost(BattleState s, int[] reach, Pos from, Pos target)
    {
        if (from.Manhattan(target) == 1) return 0;
        int best = Far;
        foreach (var d in Dirs.All)
        {
            var p = target.Step(d);
            if (s.InBounds(p) && reach[s.Idx(p)] < best) best = reach[s.Idx(p)];
        }
        return best;
    }

    // ------------------------------------------------------------------
    // What a unit could hit from a tile
    // ------------------------------------------------------------------

    /// <summary>Moves a unit on a scratch copy of the battle. No rules run: nothing springs, nothing is spent.</summary>
    public static void Relocate(BattleState probe, int unit, Pos p)
    {
        if (probe.Units[unit].Pos == p) return;
        probe.RemoveFromBoard(unit);
        probe.Place(unit, p);
    }

    /// <summary>
    /// The targets a move would have if the unit stood on the tile, tried on the scratch copy. The
    /// list is reused by the next call.
    /// </summary>
    public List<(Target, Target)> TargetsFrom(BattleState probe, int unit, Pos p, MoveDef move)
    {
        Relocate(probe, unit, p);
        Rules.Targets(probe, unit, move, _targets);
        return _targets;
    }

    /// <summary>Applies an action to a copy of the battle, with a good timing grade where one is needed. Null if it's illegal there.</summary>
    public static BattleState? After(BattleState s, BattleAction action)
    {
        var copy = s.Clone();
        if (Rules.TimingFor(copy, action) != TimingNeed.None) action = action.WithTiming(Timing.Good);
        try
        {
            Rules.Apply(copy, action);
        }
        catch (IllegalActionException)
        {
            return null;
        }
        return copy;
    }

    // ------------------------------------------------------------------
    // Estimates
    // ------------------------------------------------------------------

    /// <summary>
    /// Rough damage of one attack: the move's damage plus Gang up and any whip buff, plus a good
    /// action command from the party, minus a good guard against enemies, minus defence. A readied
    /// Parry blocks melee. Never below 0.
    /// </summary>
    public static int Damage(BattleState s, int attacker, int target, MoveDef move)
    {
        ref var a = ref s.Units[attacker];
        ref var t = ref s.Units[target];
        if (move.Damage <= 0) return 0;
        bool melee = move.Kind is MoveKind.Melee or MoveKind.Charge;
        if (melee && t.Has(UnitFlags.ParryReady)) return 0;
        var rules = s.Data.Rules;
        int damage = move.Damage + Rules.GangUpBonus(s, attacker, target, move) + a.BuffAmount;
        if (a.Side == Side.Party && move.Command && a.Blinded == 0) damage += RulesConfig.ByGrade(rules.AttackBonus, Timing.Good);
        if (t.Side == Side.Party && a.Side == Side.Enemy) damage -= RulesConfig.ByGrade(rules.GuardReduction, Timing.Good);
        damage -= t.Defence + t.BraceDefence;
        return Math.Max(0, damage);
    }

    /// <summary>Movement a unit will have next turn, in half-points, counting Scamper and its statuses.</summary>
    public static int NextTurnMovement(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        if (u.Rooted > 1 || u.Braced > 1) return 0; // still held when its next turn comes
        int move = u.MoveMax + (Rules.KitMove(s, unit, MoveKind.Scamper)?.Amount ?? 0);
        if (u.Slowed > 1) move -= s.Data.Rules.SlowAmount;
        if (u.Has(UnitFlags.KnockedOver)) move -= s.Data.Rules.KnockedOverDrain;
        return Math.Max(0, move);
    }

    /// <summary>
    /// A quick, generous guess at the damage a unit standing on <paramref name="at"/> could take before
    /// its next turn: each enemy that could get close enough to use one of its attacks counts its best
    /// one, and a burrowed beast threatens the blast around its rumbling tile. Distances are counted
    /// in straight steps, so walls and climbs are ignored.
    /// </summary>
    public static int Threat(BattleState s, int unit, Pos at)
    {
        int total = 0;
        for (int e = 0; e < s.Units.Length; e++)
        {
            ref var en = ref s.Units[e];
            if (!en.Alive || !Rules.AreEnemies(s, e, unit)) continue;
            if (en.Has(UnitFlags.Burrowed))
            {
                var burrow = Rules.KitMove(s, e, MoveKind.Burrow);
                if (burrow != null && en.BurrowTo.Manhattan(at) <= burrow.Radius) total += Damage(s, e, unit, burrow) + 1;
                continue;
            }
            if (!en.OnBoard || en.Has(UnitFlags.Fleeing)) continue;
            int walk = NextTurnMovement(s, e) / 2;
            int distance = en.Pos.Manhattan(at);
            int best = 0;
            foreach (int m in s.Info.Kits[e])
            {
                var move = s.Data.Moves[m];
                if (!move.UsesMain || move.Damage <= 0) continue;
                int reach = move.Kind switch
                {
                    MoveKind.Melee => 1,
                    MoveKind.Ranged => move.Range,
                    MoveKind.Charge => move.Charge + 1,
                    _ => 0, // a burrow lands a turn later
                };
                if (reach > 0 && distance <= walk + reach) best = Math.Max(best, Damage(s, e, unit, move));
            }
            total += best;
        }
        return total;
    }

    /// <summary>The living, on-board unit nearest to a tile by straight steps, among those the filter accepts; -1 if none.</summary>
    public static int Nearest(BattleState s, Pos from, Func<int, bool> accept)
    {
        int best = -1, bestDistance = int.MaxValue;
        for (int i = 0; i < s.Units.Length; i++)
        {
            if (!s.Units[i].Alive || !s.Units[i].OnBoard || !accept(i)) continue;
            int d = s.Units[i].Pos.Manhattan(from);
            if (d < bestDistance) { best = i; bestDistance = d; }
        }
        return best;
    }

    /// <summary>Does the unit have a move of this kind it could use right now?</summary>
    public static MoveDef? Ready(BattleState s, int unit, MoveKind kind, MoveSlot? slot = null)
    {
        foreach (int m in s.Info.Kits[unit])
        {
            var move = s.Data.Moves[m];
            if (move.Kind == kind && (slot == null || move.Slot == slot) && Rules.MoveAvailable(s, unit, move)) return move;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // A small binary heap of ints (smallest first): cost * 1024 + tile
    // ------------------------------------------------------------------

    private void HeapPush(ref int size, int key)
    {
        int i = size++;
        _heap[i] = key;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (_heap[parent] <= _heap[i]) break;
            (_heap[parent], _heap[i]) = (_heap[i], _heap[parent]);
            i = parent;
        }
    }

    private int HeapPop(ref int size)
    {
        int top = _heap[0];
        _heap[0] = _heap[--size];
        int i = 0;
        while (true)
        {
            int left = 2 * i + 1, right = left + 1, smallest = i;
            if (left < size && _heap[left] < _heap[smallest]) smallest = left;
            if (right < size && _heap[right] < _heap[smallest]) smallest = right;
            if (smallest == i) break;
            (_heap[smallest], _heap[i]) = (_heap[i], _heap[smallest]);
            i = smallest;
        }
        return top;
    }
}

namespace Battle.Core.Ai;

/// <summary>
/// A simulated player for the hero and the Loper, so the simulator can play whole battles and
/// estimate how hard an encounter is. It plays a straightforward game:
///   - Main action: from every tile it could walk to, it tries each attack on a copy of the battle
///     and scores the result (damage dealt, enemies dropped, how exposed it's left). The Loper also
///     weighs Taunting an enemy that could reach the hero. With nothing to hit, it closes in.
///   - Bonus action, by simple rules: the hero Binds the rumbling ground when the Snuffler is under
///     it, Shoves when that does real harm, Parries when a melee hit is coming, and otherwise Binds
///     the enemy most likely to reach him. The Loper Braces when enemies are close.
///   - The hero steps back out of danger with any movement left after attacking.
/// It never Tethers, mounts or swaps the party order: a real player will do better than it.
/// </summary>
public sealed class PartyBot : PlannedController
{
    private readonly Tactics _t = new();
    private Rng _rng;
    private readonly List<Pos> _tiles = new();
    private readonly List<(Target, Target)> _options = new();
    private bool[] _goals = Array.Empty<bool>();
    private int[] _field = Array.Empty<int>();
    private int[] _reach = Array.Empty<int>();

    public PartyBot(ulong seed) => _rng = new Rng(seed ^ 0xB0B5_EED0_1234_5678UL);

    public override string Name => "scripted";

    protected override void Plan(BattleState s, int me, List<BattleAction> plan)
    {
        if (_goals.Length < s.TileCount)
        {
            _goals = new bool[s.TileCount];
            _field = new int[s.TileCount];
            _reach = new int[s.TileCount];
        }
        ref var u = ref s.Units[me];
        bool mainFree = !u.Has(UnitFlags.MainUsed), bonusFree = !u.Has(UnitFlags.BonusUsed);
        if (bonusFree && BindRumble(s, me, plan)) return;
        if (mainFree)
        {
            MainPlan(s, me, plan);
            if (plan.Count > 0) return;
        }
        if (bonusFree)
        {
            BonusPlan(s, me, plan);
            if (plan.Count > 0) return;
        }
        if (!mainFree && me == s.Info.HeroIndex) Retreat(s, me, plan);
    }

    // ------------------------------------------------------------------
    // The main action
    // ------------------------------------------------------------------

    private void MainPlan(BattleState s, int me, List<BattleAction> plan)
    {
        ref var u = ref s.Units[me];
        var probe = s.Clone();
        Tactics.Standing(s, me, u.MoveLeft, _tiles, includeTraps: true);
        BattleAction? best = null;
        Pos bestTile = u.Pos;
        int bestScore = int.MinValue;
        foreach (var p in _tiles)
        {
            int place = RumbleBonus(s, me, p) - TrapPenalty(s, me, p);
            foreach (int m in s.Info.Kits[me])
            {
                var move = s.Data.Moves[m];
                if (!move.UsesMain || !Rules.MoveAvailable(s, me, move)) continue;
                bool attack = move.Kind is MoveKind.Melee or MoveKind.Charge or MoveKind.Ranged;
                if (!attack && move.Kind != MoveKind.Taunt) continue;
                foreach (var (first, second) in _t.TargetsFrom(probe, me, p, move))
                {
                    if (first.Kind != TargetKind.Unit || !Rules.AreEnemies(s, me, first.Unit)) continue;
                    var action = BattleAction.Use(me, move, first, second);
                    int score;
                    if (attack)
                    {
                        var after = Tactics.After(probe, action);
                        if (after == null) continue;
                        score = Score(s, after, me);
                    }
                    else
                    {
                        int taunt = TauntValue(s, first.Unit);
                        if (taunt <= 0) continue;
                        score = Score(s, probe, me) + taunt;
                    }
                    score += place + Noise(3);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = action;
                    bestTile = p;
                }
            }
        }
        if (best != null && bestScore > -2000)
        {
            if (bestTile != u.Pos) plan.Add(BattleAction.MoveTo(me, bestTile));
            plan.Add(best.Value);
            return;
        }
        Approach(s, me, plan);
    }

    /// <summary>
    /// How good a position is after an action, compared with before it: damage dealt and taken,
    /// enemies dropped (the dangerous ones more), statuses landed, and how exposed the unit is left.
    /// </summary>
    private static int Score(BattleState before, BattleState after, int me)
    {
        if (after.Outcome == Outcome.PartyWon) return 100_000;
        if (after.Outcome == Outcome.PartyLost) return -100_000;
        int hero = before.Info.HeroIndex;
        int score = 0;
        for (int i = 0; i < before.Units.Length; i++)
        {
            ref var b = ref before.Units[i];
            ref var a = ref after.Units[i];
            int lost = b.Hp - a.Hp;
            if (b.Side == Side.Enemy)
            {
                score += lost * 30;
                if (b.Alive && !a.Alive) score += 120 + Bounty(before, i);
                if (!b.Has(UnitFlags.KnockedOver) && a.Has(UnitFlags.KnockedOver)) score += 15;
                if (a.Rooted > b.Rooted) score += 10;
            }
            else score -= lost * (i == hero ? 60 : 30);
        }
        ref var u = ref after.Units[me];
        if (u.Alive && u.OnBoard) score -= Tactics.Threat(after, me, u.Pos) * (me == hero ? 25 : 10);
        return score;
    }

    /// <summary>Extra worth in dropping an enemy: the beast hits hardest, and its handler steers it.</summary>
    private static int Bounty(BattleState s, int enemy)
    {
        var def = s.DefOf(enemy);
        if (def.Has(UnitTags.Beast)) return 60;
        if (Rules.KitMove(s, enemy, MoveKind.Point) != null) return 40;
        if (Rules.KitMove(s, enemy, MoveKind.Ranged) != null) return 20;
        return 0;
    }

    /// <summary>Taunt is worth what the taunted enemy would otherwise do to the hero next turn.</summary>
    private static int TauntValue(BattleState s, int enemy)
    {
        int hero = s.Info.HeroIndex;
        if (s.Units[enemy].Taunted > 0) return -50;
        int threat = ThreatFrom(s, enemy, hero, s.Units[hero].Pos);
        return threat > 0 ? threat * 40 : -20;
    }

    /// <summary>The damage one enemy could do to a unit standing on a tile next turn (the same generous guess as Tactics.Threat).</summary>
    private static int ThreatFrom(BattleState s, int enemy, int unit, Pos at)
    {
        ref var e = ref s.Units[enemy];
        if (!e.Alive || !e.OnBoard || e.Has(UnitFlags.Fleeing)) return 0;
        int walk = Tactics.NextTurnMovement(s, enemy) / 2;
        int best = 0;
        foreach (int m in s.Info.Kits[enemy])
        {
            var move = s.Data.Moves[m];
            if (!move.UsesMain || move.Damage <= 0) continue;
            int reach = move.Kind switch { MoveKind.Melee => 1, MoveKind.Ranged => move.Range, MoveKind.Charge => move.Charge + 1, _ => 0 };
            if (reach > 0 && e.Pos.Manhattan(at) <= walk + reach) best = Math.Max(best, Tactics.Damage(s, enemy, unit, move));
        }
        return best;
    }

    /// <summary>
    /// What stepping onto a trap would cost: a snap trap bites and slows. The party's own root traps
    /// don't catch it. It only steps on one when nothing else gets it anywhere.
    /// </summary>
    private static int TrapPenalty(BattleState s, int me, Pos p)
    {
        int tile = s.Idx(p);
        if (s.Trap[tile] == TrapKind.None || p == s.Units[me].Pos) return 0;
        int owner = s.TrapOwner[tile];
        if (owner >= 0 && !Rules.AreEnemies(s, owner, me)) return 0;
        int damage = owner >= 0 ? Rules.KitMove(s, owner, MoveKind.Trap)?.Damage ?? 0 : 0;
        return 60 + damage * (me == s.Info.HeroIndex ? 60 : 30);
    }

    /// <summary>A reason to stand within Bind's reach of rumbling ground: rooting it catches the beast as it bursts out.</summary>
    private static int RumbleBonus(BattleState s, int me, Pos p)
    {
        var bind = Tactics.Ready(s, me, MoveKind.Bind);
        if (bind == null) return 0;
        for (int tile = 0; tile < s.TileCount; tile++)
        {
            if (s.Rumble[tile] < 0 || s.Trap[tile] != TrapKind.None) continue;
            int d = s.PosOf(tile).Manhattan(p);
            if (d <= bind.Range && d > 1) return 40;
        }
        return 0;
    }

    /// <summary>
    /// Nothing to hit: close in on the nearest enemy that isn't running, minding the danger, the hero
    /// staying near the Loper. If only runners are left and none can be reached, it stands back to
    /// let them go: a goblin that gets away still ends the fight.
    /// </summary>
    private void Approach(BattleState s, int me, List<BattleAction> plan)
    {
        ref var u = ref s.Units[me];
        Tactics.ReachFrom(s, me, _reach);
        int target = -1, bestCost = int.MaxValue;
        for (int pass = 0; pass < 2 && target < 0; pass++)
            for (int i = 0; i < s.Units.Length; i++)
            {
                ref var e = ref s.Units[i];
                if (!e.Alive || !e.OnBoard || !Rules.AreEnemies(s, me, i)) continue;
                if (pass == 0 && e.Has(UnitFlags.Fleeing)) continue;
                int cost = Tactics.ApproachCost(s, _reach, u.Pos, e.Pos);
                if (cost == Tactics.Far) cost = 100_000 + u.Pos.Manhattan(e.Pos);
                if (cost < bestCost) { bestCost = cost; target = i; }
            }
        if (target < 0) return;
        var targetPos = s.Units[target].Pos;

        Array.Clear(_goals, 0, s.TileCount);
        foreach (var d in Dirs.All)
        {
            var p = targetPos.Step(d);
            if (!s.InBounds(p) || !Rules.MeleeReach(s, p, targetPos) || s.Trap[s.Idx(p)] != TrapKind.None) continue;
            if (p == u.Pos || Rules.CanEndOn(s, me, s.Idx(p))) _goals[s.Idx(p)] = true;
        }
        _t.CostToGo(s, me, _goals, _field, trapCost: 8);
        bool letGo = s.Units[target].Has(UnitFlags.Fleeing) && _field[s.Idx(u.Pos)] == Tactics.Far;

        int hero = s.Info.HeroIndex, loper = s.Info.LoperIndex;
        int weight = me == hero ? 25 : 10;
        Tactics.Standing(s, me, u.MoveLeft, _tiles, includeTraps: true);
        Pos best = u.Pos;
        int bestScore = int.MinValue;
        foreach (var p in _tiles)
        {
            int field = _field[s.Idx(p)];
            int distance = field == Tactics.Far ? 40 + p.Manhattan(targetPos) * 2 : field;
            int score = (letGo ? p.Manhattan(targetPos) * 10 : -distance * 10) - Tactics.Threat(s, me, p) * weight
                + RumbleBonus(s, me, p) - TrapPenalty(s, me, p) + Noise(2);
            if (me == hero && loper >= 0 && s.Units[loper].Alive && s.Units[loper].OnBoard)
                score -= Math.Max(0, p.Manhattan(s.Units[loper].Pos) - 3) * 3;
            if (score > bestScore) { bestScore = score; best = p; }
        }
        if (best != u.Pos) plan.Add(BattleAction.MoveTo(me, best));
    }

    // ------------------------------------------------------------------
    // The bonus action
    // ------------------------------------------------------------------

    /// <summary>The Snuffler is under rumbling ground in Bind's reach: root it, so it's caught as it bursts out.</summary>
    private bool BindRumble(BattleState s, int me, List<BattleAction> plan)
    {
        var bind = Tactics.Ready(s, me, MoveKind.Bind);
        if (bind == null) return false;
        Rules.Targets(s, me, bind, _options);
        for (int i = 0; i < s.Units.Length; i++)
        {
            if (!s.Units[i].Alive || !s.Units[i].Has(UnitFlags.Burrowed) || !Rules.AreEnemies(s, me, i)) continue;
            var target = Target.OfTile(s.Units[i].BurrowTo);
            if (!_options.Contains((target, Target.None))) continue;
            plan.Add(BattleAction.Use(me, bind, target));
            return true;
        }
        return false;
    }

    private void BonusPlan(BattleState s, int me, List<BattleAction> plan)
    {
        ref var u = ref s.Units[me];
        int hero = s.Info.HeroIndex;
        BattleAction? best = null;
        int bestValue = 29; // anything less isn't worth the bother

        void Consider(BattleAction action, int value)
        {
            if (value <= bestValue) return;
            bestValue = value;
            best = action;
        }

        // Shove, when it does real harm (into a rock, off a ledge) or clears a goblin off the hero.
        var shove = Tactics.Ready(s, me, MoveKind.Push);
        if (shove != null)
        {
            int baseline = Score(s, s, me);
            Rules.Targets(s, me, shove, _options);
            foreach (var (target, _) in _options)
            {
                if (target.Kind != TargetKind.Unit || !Rules.AreEnemies(s, me, target.Unit)) continue;
                var after = Tactics.After(s, BattleAction.Use(me, shove, target));
                if (after != null) Consider(BattleAction.Use(me, shove, target), Score(s, after, me) - baseline + Noise(3));
            }
        }

        // Parry, when a melee hit is coming.
        var parry = Tactics.Ready(s, me, MoveKind.Parry);
        if (parry != null)
        {
            int melee = MeleeThreat(s, me);
            if (melee > 0) Consider(BattleAction.Use(me, parry, Target.Self), 30 + melee * 20);
        }

        // Brace, when enemies are close.
        var brace = Tactics.Ready(s, me, MoveKind.Brace);
        if (brace != null)
        {
            int threat = Tactics.Threat(s, me, u.Pos);
            if (threat > 0) Consider(BattleAction.Use(me, brace, Target.Self), 25 + threat * 10);
        }

        // Bind the enemy most likely to reach the hero next turn: rooted, it can't walk up.
        var bind = Tactics.Ready(s, me, MoveKind.Bind);
        if (bind != null)
        {
            bool full = Rules.NearestSource(s, u.Pos, Element.Root, t => s.FeatureUses[t] > 0) >= 0;
            Rules.Targets(s, me, bind, _options);
            foreach (var (target, _) in _options)
            {
                if (target.Kind != TargetKind.Unit || !Rules.AreEnemies(s, me, target.Unit)) continue;
                int e = target.Unit;
                if (NextToParty(s, s.Units[e].Pos) || s.Units[e].Rooted > 0) continue; // a root doesn't stop a hit from where it stands
                int threat = ThreatFrom(s, e, hero, s.Units[hero].Pos);
                if (threat <= 0) continue;
                int value = 20 + threat * 30;
                Consider(BattleAction.Use(me, bind, target), (full ? value : value / 3) + Noise(3));
            }
        }

        if (best != null) plan.Add(best.Value);
    }

    /// <summary>How much melee damage could land on this unit next turn (for Parry).</summary>
    private static int MeleeThreat(BattleState s, int me)
    {
        int best = 0;
        var at = s.Units[me].Pos;
        for (int e = 0; e < s.Units.Length; e++)
        {
            ref var en = ref s.Units[e];
            if (!en.Alive || !en.OnBoard || !Rules.AreEnemies(s, e, me) || en.Has(UnitFlags.Fleeing)) continue;
            int walk = Tactics.NextTurnMovement(s, e) / 2;
            foreach (int m in s.Info.Kits[e])
            {
                var move = s.Data.Moves[m];
                if (move.Kind == MoveKind.Melee && move.Damage > 0 && en.Pos.Manhattan(at) <= walk + 1)
                    best = Math.Max(best, Tactics.Damage(s, e, me, move));
            }
        }
        return best;
    }

    // ------------------------------------------------------------------
    // Hit and run
    // ------------------------------------------------------------------

    /// <summary>Movement left after attacking: step somewhere safer if that helps a lot.</summary>
    private void Retreat(BattleState s, int me, List<BattleAction> plan)
    {
        ref var u = ref s.Units[me];
        if (u.MoveLeft <= 0 || Rules.Anchored(s, me)) return;
        Tactics.Standing(s, me, u.MoveLeft, _tiles);
        int here = Tactics.Threat(s, me, u.Pos);
        Pos best = u.Pos;
        int bestScore = here * 10;
        foreach (var p in _tiles)
        {
            int score = Tactics.Threat(s, me, p) * 10 + p.Manhattan(u.Pos);
            if (score < bestScore) { bestScore = score; best = p; }
        }
        if (best != u.Pos && here - Tactics.Threat(s, me, best) >= 2) plan.Add(BattleAction.MoveTo(me, best));
    }

    private int Noise(int max) => _rng.Next(max + 1);

    private static bool NextToParty(BattleState s, Pos p)
    {
        foreach (var d in Dirs.All)
        {
            int unit = s.UnitAt(p.Step(d));
            if (unit >= 0 && s.Units[unit].Side == Side.Party) return true;
        }
        return false;
    }
}

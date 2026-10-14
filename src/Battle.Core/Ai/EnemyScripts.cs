namespace Battle.Core.Ai;

/// <summary>
/// The enemies' hand-written scripts: one per goblin type, plus the Snuffler's fixed rules. They're
/// the baseline trained enemies have to beat, and how early content ships.
///   Cutter       closes in and stabs, ganging up where it can; headbutts the hero to cost him his
///                next bonus action; once wounded, swaps a mate into its place (After you).
///   Plinker      keeps its distance, high up if it can; throws dirt at whoever is about to hit a
///                goblin, otherwise plinks; drops its snap trap where the party would stand to attack.
///   Beastmaster  hangs back near its beast, points it at the best target and whips it on; lashes
///                when it's safe to.
///   Snuffler     no mind of its own: see <see cref="Beast"/>.
///   Any goblin   whose nerve has broken runs for the nearest edge.
/// A little seeded randomness breaks near-ties, so simulated battles vary.
/// </summary>
public sealed class EnemyScripts : PlannedController
{
    private readonly Tactics _t = new();
    private Rng _rng;
    private readonly List<Pos> _near = new();
    private readonly List<Pos> _far = new();
    private readonly List<Pos> _scratchTiles = new();
    private readonly List<(Target, Target)> _options = new();
    private readonly List<int> _party = new();
    private readonly List<int[]> _partyReach = new();
    private bool[] _mark = Array.Empty<bool>();
    private bool[] _goals = Array.Empty<bool>();
    private int[] _field = Array.Empty<int>();
    private int[] _reach = Array.Empty<int>();

    public EnemyScripts(ulong seed) => _rng = new Rng(seed ^ 0x9E37_79B9_7F4A_7C15UL);

    public override string Name => "scripted";

    protected override void Plan(BattleState s, int me, List<BattleAction> plan)
    {
        Size(s);
        ref var u = ref s.Units[me];
        if (u.Has(UnitFlags.Burrowed))
        {
            var burrow = Rules.KitMove(s, me, MoveKind.Burrow)!;
            plan.Add(BattleAction.Use(me, burrow, Target.OfTile(u.BurrowTo)));
            return;
        }
        if (u.Has(UnitFlags.Fleeing))
        {
            Flee(s, me, plan);
            return;
        }
        if (s.DefOf(me).Has(UnitTags.Beast))
        {
            Beast(s, me, plan);
            return;
        }
        switch (s.DefOf(me).Id)
        {
            case "plinker": Plinker(s, me, plan); break;
            case "beastmaster": Beastmaster(s, me, plan); break;
            default: Fighter(s, me, plan); break; // the Cutter, and anyone without a script of its own
        }
    }

    // ------------------------------------------------------------------
    // Cutter (and the fallback for any fighter)
    // ------------------------------------------------------------------

    private void Fighter(BattleState s, int me, List<BattleAction> plan)
    {
        ref var u = ref s.Units[me];
        if (u.Has(UnitFlags.MainUsed))
        {
            AfterYou(s, me, plan);
            return;
        }

        // The best attack it could make this turn, from anywhere it could get to.
        var scamper = Tactics.Ready(s, me, MoveKind.Scamper);
        Candidates(s, me, scamper);
        var probe = s.Clone();
        BattleAction? best = null;
        Pos bestTile = u.Pos;
        bool bestScamper = false;
        int bestScore = int.MinValue;
        for (int pass = 0; pass < 2; pass++)
        {
            bool withScamper = pass == 1;
            foreach (var p in withScamper ? _far : _near)
                foreach (int m in s.Info.Kits[me])
                {
                    var move = s.Data.Moves[m];
                    if (!IsAttack(move) || !Rules.MoveAvailable(s, me, move)) continue;
                    if (withScamper && move.UsesBonus) continue; // Scamper takes the bonus action
                    foreach (var (target, _) in _t.TargetsFrom(probe, me, p, move))
                    {
                        if (target.Kind != TargetKind.Unit || !Rules.AreEnemies(s, me, target.Unit)) continue;
                        int score = AttackScore(s, me, target.Unit, move) - (withScamper ? 5 : 0) + Noise(9);
                        if (score <= bestScore) continue;
                        bestScore = score;
                        best = BattleAction.Use(me, move, target);
                        bestTile = p;
                        bestScamper = withScamper;
                    }
                }
        }
        if (best != null)
        {
            Go(s, me, scamper, bestScamper, bestTile, plan);
            plan.Add(best.Value);
            return;
        }
        Approach(s, me, scamper, plan);
    }

    private static bool IsAttack(MoveDef move) =>
        move.UsesMain && (move.Kind is MoveKind.Melee or MoveKind.Ranged or MoveKind.Charge) && (move.Damage > 0 || move.Status != StatusId.None);

    /// <summary>How good an attack on a target looks: damage, a kill, and what a daze would cost him.</summary>
    private static int AttackScore(BattleState s, int me, int target, MoveDef move)
    {
        ref var t = ref s.Units[target];
        bool hero = target == s.Info.HeroIndex;
        int damage = Tactics.Damage(s, me, target, move);
        int score = 10 + damage * 30 - t.Hp * 2;
        if (damage >= t.Hp) score += hero ? 1000 : 150;
        if (hero) score += 20; // goblins go for the elf
        if (move.Status == StatusId.Dazed)
        {
            if (t.Has(UnitFlags.Dazed)) score -= 100; // already dazed: wasted
            else score += hero ? 40 : 15;             // costs it its next bonus action
            if (move.SelfStatus == StatusId.Dazed) score -= 10;
        }
        return score;
    }

    /// <summary>Nothing to hit this turn: close in on the best target, keeping near other goblins.</summary>
    private void Approach(BattleState s, int me, MoveDef? scamper, List<BattleAction> plan)
    {
        int target = PickTarget(s, me);
        if (target < 0) return;
        GoalsNextTo(s, me, s.Units[target].Pos);
        _t.CostToGo(s, me, _goals, _field);
        var (tile, withScamper, _) = BestTile(s, me, (p, scampering) =>
            -Cost(_field[s.Idx(p)]) * 10 - (scampering ? 15 : 0) + (NextToMate(s, me, p) ? 4 : 0) + Noise(3));
        Go(s, me, scamper, withScamper, tile, plan);
    }

    /// <summary>The party member to go for: the nearest by walking, but the hero if he's at most two tiles further.</summary>
    private int PickTarget(BattleState s, int me)
    {
        ref var u = ref s.Units[me];
        if (u.Taunted > 0 && u.TauntBy >= 0 && s.Units[u.TauntBy].Alive && s.Units[u.TauntBy].OnBoard) return u.TauntBy;
        Tactics.ReachFrom(s, me, _reach);
        int best = -1, bestScore = int.MinValue;
        for (int i = 0; i < s.Units.Length; i++)
        {
            if (!s.Units[i].Alive || !s.Units[i].OnBoard || !Rules.AreEnemies(s, me, i)) continue;
            int cost = Tactics.ApproachCost(s, _reach, u.Pos, s.Units[i].Pos);
            int score = cost == Tactics.Far ? -1000 - u.Pos.Manhattan(s.Units[i].Pos) * 2 : -cost;
            if (i == s.Info.HeroIndex) score += 4;
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    /// <summary>After you: a wounded fighter in the thick of it swaps places with a neighbouring mate who isn't.</summary>
    private void AfterYou(BattleState s, int me, List<BattleAction> plan)
    {
        var swap = Tactics.Ready(s, me, MoveKind.Swap);
        ref var u = ref s.Units[me];
        if (swap == null || u.Hp * 2 > u.MaxHp || !NextToParty(s, u.Pos)) return;
        Rules.Targets(s, me, swap, _options);
        int best = -1;
        foreach (var (target, _) in _options)
        {
            int mate = target.Unit;
            if (NextToParty(s, s.Units[mate].Pos) || s.Units[mate].Hp < u.Hp) continue;
            if (best < 0 || s.Units[mate].Hp > s.Units[best].Hp) best = mate;
        }
        if (best >= 0) plan.Add(BattleAction.Use(me, swap, Target.OfUnit(best)));
    }

    // ------------------------------------------------------------------
    // Plinker
    // ------------------------------------------------------------------

    private void Plinker(BattleState s, int me, List<BattleAction> plan)
    {
        ref var u = ref s.Units[me];
        PrepareParty(s);
        var scamper = Tactics.Ready(s, me, MoveKind.Scamper);
        bool mainFree = !u.Has(UnitFlags.MainUsed);
        Candidates(s, me, scamper);
        var probe = s.Clone();

        Pos bestTile = u.Pos;
        bool bestScamper = false;
        BattleAction? bestShot = null;
        int bestScore = int.MinValue;
        for (int pass = 0; pass < 2; pass++)
        {
            bool withScamper = pass == 1;
            foreach (var p in withScamper ? _far : _near)
            {
                var (hitters, nearest) = PartyReach(s, p);
                int score = -hitters * 60 + Math.Min(Cost(nearest), 12) * 4 + s.Heights[s.Idx(p)] * 8
                    - (withScamper ? 10 : 0) + (p == u.Pos ? Stay(s, me) : 0) + Noise(5);
                BattleAction? shot = null;
                int shotScore = 0;
                if (mainFree)
                    foreach (int m in s.Info.Kits[me])
                    {
                        var move = s.Data.Moves[m];
                        if (move.Kind != MoveKind.Ranged || !Rules.MoveAvailable(s, me, move)) continue;
                        foreach (var (target, _) in _t.TargetsFrom(probe, me, p, move))
                        {
                            if (target.Kind != TargetKind.Unit || !Rules.AreEnemies(s, me, target.Unit)) continue;
                            int v = ShotScore(s, me, target.Unit, move);
                            if (v > shotScore) { shotScore = v; shot = BattleAction.Use(me, move, target); }
                        }
                    }
                score += shotScore;
                if (score <= bestScore) continue;
                bestScore = score;
                bestTile = p;
                bestScamper = withScamper;
                bestShot = shot;
            }
        }

        Go(s, me, scamper, bestScamper, bestTile, plan);
        if (bestShot != null) plan.Add(bestShot.Value);
        if (!bestScamper) SnapTrap(s, me, probe, bestTile, plan);
    }

    /// <summary>Pocket dirt for whoever is about to hit a goblin and isn't blinded yet; otherwise the plink that hurts most.</summary>
    private static int ShotScore(BattleState s, int me, int target, MoveDef move)
    {
        ref var t = ref s.Units[target];
        bool hero = target == s.Info.HeroIndex;
        if (move.Status == StatusId.Blinded)
        {
            if (t.Blinded > 0 || !HasCommandAttack(s, target)) return 0; // blindness only spoils action commands
            return 40 + (NextToGoblin(s, t.Pos) ? 40 : 0) + (hero ? 10 : 0);
        }
        int damage = Tactics.Damage(s, me, target, move);
        int score = 10 + damage * 30;
        if (damage >= t.Hp) score += hero ? 1000 : 150;
        return score + (hero ? 5 : 0);
    }

    /// <summary>
    /// The snap trap goes where the party would stand to attack a goblin (next to one), as close to
    /// the party as possible. It stays put while it's still useful: one trap per Plinker.
    /// </summary>
    private void SnapTrap(BattleState s, int me, BattleState probe, Pos from, List<BattleAction> plan)
    {
        var trap = Tactics.Ready(s, me, MoveKind.Trap);
        if (trap == null || _party.Count == 0) return;
        for (int tile = 0; tile < s.TileCount; tile++)
            if (s.Trap[tile] == TrapKind.Snap && s.TrapOwner[tile] == me && PartyDistance(s, s.PosOf(tile)) <= 2) return;

        Target best = Target.None;
        int bestScore = 0;
        foreach (var (target, _) in _t.TargetsFrom(probe, me, from, trap))
        {
            var q = target.Tile;
            if (q == from) continue;
            int distance = PartyDistance(s, q);
            if (distance > 5) continue;
            int score = (NextToGoblin(s, q, except: me) || q.Manhattan(from) == 1 ? 50 : 0) - distance * 5 + 10 + Noise(5);
            if (score > bestScore) { bestScore = score; best = target; }
        }
        if (best.Kind == TargetKind.Tile) plan.Add(BattleAction.Use(me, trap, best));
    }

    // ------------------------------------------------------------------
    // Beastmaster
    // ------------------------------------------------------------------

    private void Beastmaster(BattleState s, int me, List<BattleAction> plan)
    {
        ref var u = ref s.Units[me];
        PrepareParty(s);
        int beast = Rules.BeastOf(s, me);
        var point = Tactics.Ready(s, me, MoveKind.Point);
        var whip = Tactics.Ready(s, me, MoveKind.Whip);
        var scamper = Tactics.Ready(s, me, MoveKind.Scamper);
        bool mainFree = !u.Has(UnitFlags.MainUsed);

        // The bonus action: point the beast at better prey, or else whip it on.
        BattleAction? order = null;
        if (beast >= 0 && point != null)
        {
            int want = BestPrey(s, beast);
            if (want >= 0 && want != BeastTarget(s, beast, wild: false))
            {
                Rules.Targets(s, me, point, _options);
                if (_options.Contains((Target.OfUnit(want), Target.None))) order = BattleAction.Use(me, point, Target.OfUnit(want));
            }
        }
        bool beastOut = beast >= 0 && s.Units[beast].OnBoard;
        bool wantWhip = order == null && whip != null && beastOut && s.Units[beast].BuffAmount == 0;
        var beastPos = beastOut ? s.Units[beast].Pos : Pos.None;

        // Where to stand: safe from the party, close to the beast (in whip reach if it's wanted), lashing if it can.
        var lash = mainFree ? Tactics.Ready(s, me, MoveKind.Ranged) : null;
        Candidates(s, me, order == null && !wantWhip ? scamper : null);
        var probe = s.Clone();
        Pos bestTile = u.Pos;
        bool bestScamper = false;
        BattleAction? bestLash = null;
        int bestScore = int.MinValue;
        for (int pass = 0; pass < 2; pass++)
        {
            bool withScamper = pass == 1;
            foreach (var p in withScamper ? _far : _near)
            {
                var (hitters, nearest) = PartyReach(s, p);
                int score = -hitters * 50 + Math.Min(Cost(nearest), 10) * 4 - (withScamper ? 10 : 0) + (p == u.Pos ? Stay(s, me) : 0) + Noise(5);
                if (beastOut)
                {
                    score -= Math.Max(0, p.Manhattan(beastPos) - 2) * 6;
                    if (wantWhip && Rules.InReach(s, whip!, p, beastPos)) score += 40;
                }
                BattleAction? hit = null;
                int hitScore = 0;
                if (lash != null)
                    foreach (var (target, _) in _t.TargetsFrom(probe, me, p, lash))
                    {
                        if (target.Kind != TargetKind.Unit || !Rules.AreEnemies(s, me, target.Unit)) continue;
                        int v = AttackScore(s, me, target.Unit, lash);
                        if (v > hitScore) { hitScore = v; hit = BattleAction.Use(me, lash, target); }
                    }
                score += hitScore;
                if (score <= bestScore) continue;
                bestScore = score;
                bestTile = p;
                bestScamper = withScamper;
                bestLash = hit;
            }
        }

        Go(s, me, scamper, bestScamper, bestTile, plan);
        if (bestLash != null) plan.Add(bestLash.Value);
        if (order != null) plan.Add(order.Value);
        else if (wantWhip && Rules.InReach(s, whip!, bestTile, beastPos)) plan.Add(BattleAction.Use(me, whip!, Target.OfUnit(beast)));
    }

    /// <summary>The party member the beast should be after: close to it, the hero by preference, the weak and the held more so.</summary>
    private int BestPrey(BattleState s, int beast)
    {
        ref var b = ref s.Units[beast];
        bool onBoard = b.OnBoard;
        var from = onBoard ? b.Pos : b.BurrowTo;
        if (onBoard) Tactics.ReachFrom(s, beast, _reach);
        int best = -1, bestScore = int.MinValue;
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var t = ref s.Units[i];
            if (!t.Alive || !t.OnBoard || t.Side != Side.Party) continue;
            int cost = onBoard ? Tactics.ApproachCost(s, _reach, from, t.Pos) : Tactics.Far;
            if (cost == Tactics.Far) cost = from.Manhattan(t.Pos) * 2;
            int score = -cost * 5 - t.Hp * 3 + (i == s.Info.HeroIndex ? 30 : 0) + (t.Braced > 0 ? -30 : 0)
                + (t.Rooted > 0 || t.Has(UnitFlags.KnockedOver) ? 10 : 0);
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    // ------------------------------------------------------------------
    // The Snuffler
    // ------------------------------------------------------------------

    /// <summary>
    /// The Snuffler has no mind of its own. These rules are all it does:
    ///   - Told to dig to a tile, it burrows there if it still can.
    ///   - Told to go for a unit (and, until it's told anything, the nearest of the party), it walks up
    ///     and bites if it can reach this turn; if not, it burrows next to its target when that's in
    ///     range; otherwise it walks towards it.
    ///   - With its handler gone it's wild: the same, but at the nearest unit of either side.
    ///   - Taunted, it bites the taunter whenever it can.
    /// (Underground it can only burst out, which <see cref="Plan"/> handles.)
    /// </summary>
    private void Beast(BattleState s, int me, List<BattleAction> plan)
    {
        ref var u = ref s.Units[me];
        if (u.Has(UnitFlags.MainUsed)) return;
        bool wild = Rules.IsWild(s, me);
        var bite = Tactics.Ready(s, me, MoveKind.Melee, MoveSlot.Main);
        var burrow = Tactics.Ready(s, me, MoveKind.Burrow);

        if (!wild && u.Order == OrderKind.Tile && burrow != null)
        {
            Rules.Targets(s, me, burrow, _options);
            if (_options.Contains((Target.OfTile(u.OrderTile), Target.None)))
            {
                plan.Add(BattleAction.Use(me, burrow, Target.OfTile(u.OrderTile)));
                return;
            }
        }

        int target = BeastTarget(s, me, wild);
        if (target < 0) return;

        // Bite from here, or walk up and bite.
        if (bite != null)
        {
            var probe = s.Clone();
            Tactics.Standing(s, me, u.MoveLeft, _near);
            CopyReachCosts(s, me, u.MoveLeft);
            Pos bestTile = Pos.None;
            Target bestTarget = Target.None;
            int bestCost = int.MaxValue;
            foreach (var p in _near)
            {
                int cost = p == u.Pos ? 0 : _reach[s.Idx(p)];
                if (cost >= bestCost) continue;
                var options = _t.TargetsFrom(probe, me, p, bite);
                var pick = Target.None;
                foreach (var (t, _) in options)
                    if (t.Kind == TargetKind.Unit && t.Unit == target) pick = t;
                // Taunted: the taunter is the only target it may bite, whatever it was told.
                if (pick.Kind == TargetKind.None && u.Taunted > 0)
                    foreach (var (t, _) in options)
                        if (t.Kind == TargetKind.Unit && t.Unit == u.TauntBy) pick = t;
                if (pick.Kind == TargetKind.None) continue;
                bestCost = cost;
                bestTile = p;
                bestTarget = pick;
            }
            if (bestTarget.Kind == TargetKind.Unit)
            {
                if (bestTile != u.Pos) plan.Add(BattleAction.MoveTo(me, bestTile));
                plan.Add(BattleAction.Use(me, bite, bestTarget));
                return;
            }
        }

        // Out of reach: burrow next to it.
        if (burrow != null)
        {
            Rules.Targets(s, me, burrow, _options);
            var targetPos = s.Units[target].Pos;
            Target best = Target.None;
            int bestScore = int.MinValue;
            foreach (var (t, _) in _options)
            {
                if (t.Kind != TargetKind.Tile || t.Tile.Manhattan(targetPos) > burrow.Radius) continue;
                int score = BlastScore(s, me, t.Tile, burrow.Radius, wild);
                if (score > bestScore) { bestScore = score; best = t; }
            }
            if (best.Kind == TargetKind.Tile)
            {
                plan.Add(BattleAction.Use(me, burrow, best));
                return;
            }
        }

        // Otherwise walk towards it.
        GoalsNextTo(s, me, s.Units[target].Pos);
        _t.CostToGo(s, me, _goals, _field);
        Tactics.Standing(s, me, u.MoveLeft, _near);
        Pos walkTo = u.Pos;
        int bestField = _field[s.Idx(u.Pos)];
        foreach (var p in _near)
            if (_field[s.Idx(p)] < bestField) { bestField = _field[s.Idx(p)]; walkTo = p; }
        if (walkTo != u.Pos) plan.Add(BattleAction.MoveTo(me, walkTo));
    }

    /// <summary>
    /// What the beast is after: the unit it was told to go for, or else the nearest by walking: of
    /// the party, or of anyone once it's wild.
    /// </summary>
    private int BeastTarget(BattleState s, int beast, bool wild)
    {
        ref var b = ref s.Units[beast];
        if (!wild && b.Order == OrderKind.Unit && b.OrderUnit >= 0 && s.Units[b.OrderUnit].Alive && s.Units[b.OrderUnit].OnBoard)
            return b.OrderUnit;
        if (!b.OnBoard) return -1;
        Tactics.ReachFrom(s, beast, _reach);
        int best = -1, bestCost = int.MaxValue;
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var t = ref s.Units[i];
            if (i == beast || !t.Alive || !t.OnBoard) continue;
            if (wild ? !Rules.AreEnemies(s, beast, i) : t.Side != Side.Party) continue;
            int cost = Tactics.ApproachCost(s, _reach, b.Pos, t.Pos);
            if (cost == Tactics.Far) cost = 1_000_000 + b.Pos.Manhattan(t.Pos);
            if (cost < bestCost) { bestCost = cost; best = i; }
        }
        return best;
    }

    /// <summary>Who a burst at this tile would catch: the more of its prey the better, and its own side only if it's wild.</summary>
    private static int BlastScore(BattleState s, int me, Pos at, int radius, bool wild)
    {
        int score = 0;
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var t = ref s.Units[i];
            if (i == me || !t.Alive || !t.OnBoard || t.Pos.Manhattan(at) > radius) continue;
            if (t.Side == Side.Party || wild) score += 10;
            else score -= 10;
        }
        return score;
    }

    // ------------------------------------------------------------------
    // Running away
    // ------------------------------------------------------------------

    /// <summary>A goblin whose nerve has broken heads for the nearest edge, away from the party, and gets away by ending its turn there.</summary>
    private void Flee(BattleState s, int me, List<BattleAction> plan)
    {
        ref var u = ref s.Units[me];
        if (s.OnEdge(u.Pos)) return;
        PrepareParty(s);
        Array.Clear(_goals, 0, s.TileCount);
        for (int tile = 0; tile < s.TileCount; tile++)
            if (s.OnEdge(s.PosOf(tile)) && s.Trap[tile] == TrapKind.None && Rules.CanEndOn(s, me, tile)) _goals[tile] = true;
        _t.CostToGo(s, me, _goals, _field);
        var scamper = Tactics.Ready(s, me, MoveKind.Scamper);
        var (tile2, withScamper, _) = BestTile(s, me, (p, scampering) =>
            -Cost(_field[s.Idx(p)]) * 20 + Math.Min(Cost(PartyDistanceCost(s, p)), 10) * 2 - (scampering ? 5 : 0));
        Go(s, me, scamper, withScamper, tile2, plan);
    }

    // ------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------

    private void Size(BattleState s)
    {
        int n = s.TileCount;
        if (_mark.Length >= n) return;
        _mark = new bool[n];
        _goals = new bool[n];
        _field = new int[n];
        _reach = new int[n];
        _partyReach.Clear();
    }

    private int Noise(int max) => _rng.Next(max + 1);

    /// <summary>A preference for staying put, much stronger once it has already walked this turn, so it doesn't dither.</summary>
    private static int Stay(BattleState s, int me) => s.Units[me].Has(UnitFlags.Moved) ? 25 : 3;

    /// <summary>Half-point costs as whole numbers to score with; unreachable counts as far away.</summary>
    private static int Cost(int halfPoints) => halfPoints == Tactics.Far ? 99 : halfPoints;

    /// <summary>Fills the tiles this unit could stand on this turn: <c>_near</c> without Scamper, <c>_far</c> the extra ones Scamper would add.</summary>
    private void Candidates(BattleState s, int me, MoveDef? scamper)
    {
        ref var u = ref s.Units[me];
        Tactics.Standing(s, me, u.MoveLeft, _near);
        _far.Clear();
        if (scamper == null) return;
        Array.Clear(_mark, 0, s.TileCount);
        foreach (var p in _near) _mark[s.Idx(p)] = true;
        Tactics.Standing(s, me, u.MoveLeft + scamper.Amount, _scratchTiles);
        foreach (var p in _scratchTiles)
            if (!_mark[s.Idx(p)]) _far.Add(p);
    }

    /// <summary>The best tile to stand on by a score, with and without Scamper.</summary>
    private (Pos Tile, bool Scamper, int Score) BestTile(BattleState s, int me, Func<Pos, bool, int> score)
    {
        var scamper = Tactics.Ready(s, me, MoveKind.Scamper);
        Candidates(s, me, scamper);
        var best = (Tile: s.Units[me].Pos, Scamper: false, Score: int.MinValue);
        foreach (var p in _near)
        {
            int v = score(p, false);
            if (v > best.Score) best = (p, false, v);
        }
        foreach (var p in _far)
        {
            int v = score(p, true);
            if (v > best.Score) best = (p, true, v);
        }
        return best;
    }

    /// <summary>Adds the steps to get to a tile: Scamper first if it's needed, then the walk.</summary>
    private static void Go(BattleState s, int me, MoveDef? scamper, bool useScamper, Pos tile, List<BattleAction> plan)
    {
        if (useScamper && scamper != null) plan.Add(BattleAction.Use(me, scamper, Target.Self));
        if (tile != s.Units[me].Pos) plan.Add(BattleAction.MoveTo(me, tile));
    }

    /// <summary>Marks the free tiles a melee attacker could hit a position from (next to it, not up or down a cliff) as goals.</summary>
    private void GoalsNextTo(BattleState s, int me, Pos target)
    {
        Array.Clear(_goals, 0, s.TileCount);
        foreach (var d in Dirs.All)
        {
            var p = target.Step(d);
            if (!s.InBounds(p) || !Rules.MeleeReach(s, p, target)) continue;
            int tile = s.Idx(p);
            if (s.Trap[tile] == TrapKind.None && (s.Occupant[tile] == me || Rules.CanEndOn(s, me, tile))) _goals[tile] = true;
        }
    }

    private void CopyReachCosts(BattleState s, int me, int budget)
    {
        Rules.ComputeReach(s, me, budget);
        for (int tile = 0; tile < s.TileCount; tile++) _reach[tile] = Rules.ReachCost(s.PosOf(tile));
    }

    /// <summary>Works out how far each of the party could walk, for judging how safe a tile is.</summary>
    private void PrepareParty(BattleState s)
    {
        _party.Clear();
        for (int i = 0; i < s.Units.Length; i++)
            if (s.Units[i].Side == Side.Party && s.Units[i].Alive && s.Units[i].OnBoard && s.Units[i].Rider < 0) _party.Add(i);
        while (_partyReach.Count < _party.Count) _partyReach.Add(new int[s.TileCount]);
        for (int k = 0; k < _party.Count; k++) Tactics.ReachFrom(s, s.Units[_party[k]].MountedOn >= 0 ? s.Units[_party[k]].MountedOn : _party[k], _partyReach[k]);
    }

    /// <summary>How many of the party could hit a goblin on this tile next turn, and how far (half-points) the nearest would have to walk.</summary>
    private (int Hitters, int Nearest) PartyReach(BattleState s, Pos p)
    {
        int hitters = 0, nearest = Tactics.Far;
        for (int k = 0; k < _party.Count; k++)
        {
            int unit = _party[k];
            int cost = Tactics.ApproachCost(s, _partyReach[k], s.Units[unit].Pos, p);
            nearest = Math.Min(nearest, cost);
            int charge = (Rules.KitMove(s, unit, MoveKind.Charge)?.Charge ?? 0) * 2;
            if (cost != Tactics.Far && cost <= Tactics.NextTurnMovement(s, unit) + charge) hitters++;
        }
        return (hitters, nearest);
    }

    private int PartyDistanceCost(BattleState s, Pos p) => PartyReach(s, p).Nearest;

    /// <summary>Straight-step distance to the nearest of the party.</summary>
    private static int PartyDistance(BattleState s, Pos p)
    {
        int best = int.MaxValue;
        for (int i = 0; i < s.Units.Length; i++)
            if (s.Units[i].Side == Side.Party && s.Units[i].Alive && s.Units[i].OnBoard) best = Math.Min(best, s.Units[i].Pos.Manhattan(p));
        return best;
    }

    private static bool NextToParty(BattleState s, Pos p)
    {
        foreach (var d in Dirs.All)
        {
            int unit = s.UnitAt(p.Step(d));
            if (unit >= 0 && s.Units[unit].Side == Side.Party) return true;
        }
        return false;
    }

    /// <summary>Is a goblin (any enemy-side unit other than <paramref name="except"/>) standing next to this tile?</summary>
    private static bool NextToGoblin(BattleState s, Pos p, int except = -1)
    {
        foreach (var d in Dirs.All)
        {
            int unit = s.UnitAt(p.Step(d));
            if (unit >= 0 && unit != except && s.Units[unit].Side == Side.Enemy) return true;
        }
        return false;
    }

    private static bool NextToMate(BattleState s, int me, Pos p)
    {
        foreach (var d in Dirs.All)
        {
            int unit = s.UnitAt(p.Step(d));
            if (unit >= 0 && unit != me && s.Units[unit].Side == s.Units[me].Side) return true;
        }
        return false;
    }

    /// <summary>Does the unit have an attack with an action command (the only kind blindness spoils)?</summary>
    private static bool HasCommandAttack(BattleState s, int unit)
    {
        foreach (int m in s.Info.Kits[unit])
            if (s.Data.Moves[m].Command && s.Data.Moves[m].Damage > 0) return true;
        return false;
    }
}

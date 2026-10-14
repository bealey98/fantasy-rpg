namespace Battle.Core.Ai;

/// <summary>
/// The enemies' hand-written scripts, one per goblin type. They're the baseline trained enemies
/// have to beat, and how early content ships.
///   Cutter       closes in and stabs, ganging up where it can, then headbutts (a bonus move) to
///                cost its target its next bonus action; once wounded, swaps a mate into its place
///                (After you) instead.
///   Taunted      any goblin goes for its taunter, even where the rules would let it hit another.
///   Plinker      keeps its distance, high up if it can; throws dirt at whoever is about to hit a
///                goblin, otherwise plinks; drops its snap trap where the party would stand to attack.
///   Beastmaster  hangs back near its beast, points it at the best target and whips it on; lashes
///                when it's safe to.
///   Snuffler     no script: it follows its handler's orders by the rules (Rules.Beasts.cs), so its
///                only legal action is the one they give.
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
    private readonly List<bool[]> _partyDanger = new();
    private readonly List<int> _partyHit = new();
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
        if (Rules.ActsByRule(s, me))
        {
            plan.Add(Rules.BeastAction(s, me)); // no choices: the rules say what it does
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
            if (plan.Count == 0) Headbutt(s, me, plan);
            return;
        }

        // The best attack it could make this turn, from anywhere it could get to, counting a
        // headbutt to follow where it wouldn't need Scamper (both take the bonus action). A taunted
        // goblin only goes for its taunter, even where the rules would let it hit someone else.
        var scamper = Tactics.Ready(s, me, MoveKind.Scamper);
        var headbutt = Tactics.Ready(s, me, MoveKind.Melee, MoveSlot.Bonus);
        Candidates(s, me, scamper);
        var probe = s.Clone();
        int taunter = Taunter(s, me);
        BattleAction? best = null;
        Pos bestTile = u.Pos;
        bool bestScamper = false;
        int bestScore = int.MinValue;
        for (int pass = 0; pass < 2; pass++)
        {
            bool withScamper = pass == 1;
            foreach (var p in withScamper ? _far : _near)
            {
                int follow = withScamper || headbutt == null ? 0 : HeadbuttValue(s, me, probe, p, headbutt);
                int risk = BlastRisk(s, me, p);
                foreach (int m in s.Info.Kits[me])
                {
                    var move = s.Data.Moves[m];
                    if (!IsAttack(move) || !Rules.MoveAvailable(s, me, move)) continue;
                    if (withScamper && move.UsesBonus) continue; // Scamper takes the bonus action
                    foreach (var (target, _) in _t.TargetsFrom(probe, me, p, move))
                    {
                        if (target.Kind != TargetKind.Unit || !Rules.AreEnemies(s, me, target.Unit)) continue;
                        if (taunter >= 0 && target.Unit != taunter) continue;
                        int score = AttackScore(s, me, target.Unit, move) + follow / 2 - risk - (withScamper ? 10 : 0) + Noise(9);
                        if (score <= bestScore) continue;
                        bestScore = score;
                        best = BattleAction.Use(me, move, target);
                        bestTile = p;
                        bestScamper = withScamper;
                    }
                }
            }
        }
        if (best != null)
        {
            Go(s, me, scamper, bestScamper, bestTile, plan);
            // Into a readied Parry, lead with the headbutt: the block takes it instead of the stab.
            int target = best.Value.Target.Unit;
            if (!bestScamper && headbutt != null && s.Units[target].Has(UnitFlags.ParryReady))
                plan.Add(BattleAction.Use(me, headbutt, Target.OfUnit(target)));
            plan.Add(best.Value);
            return;
        }
        Approach(s, me, scamper, plan);
    }

    /// <summary>Main-action attacks. A bonus attack (Headbutt) is weighed separately, as a follow-up.</summary>
    private static bool IsAttack(MoveDef move) =>
        move.UsesMain && (move.Kind is MoveKind.Melee or MoveKind.Ranged or MoveKind.Charge) && (move.Damage > 0 || move.Status != StatusId.None);

    /// <summary>The best headbutt it could make standing on a tile (0 if none is worth it).</summary>
    private int HeadbuttValue(BattleState s, int me, BattleState probe, Pos p, MoveDef headbutt)
    {
        int taunter = Taunter(s, me), best = 0;
        foreach (var (target, _) in _t.TargetsFrom(probe, me, p, headbutt))
        {
            if (target.Kind != TargetKind.Unit || !Rules.AreEnemies(s, me, target.Unit)) continue;
            if (taunter >= 0 && target.Unit != taunter) continue;
            best = Math.Max(best, AttackScore(s, me, target.Unit, headbutt));
        }
        return best;
    }

    /// <summary>After its main action: headbutt whoever in reach it's most worth dazing, if anyone.</summary>
    private void Headbutt(BattleState s, int me, List<BattleAction> plan)
    {
        var headbutt = Tactics.Ready(s, me, MoveKind.Melee, MoveSlot.Bonus);
        if (headbutt == null) return;
        int taunter = Taunter(s, me), bestScore = 0;
        BattleAction? best = null;
        Rules.Targets(s, me, headbutt, _options);
        foreach (var (target, _) in _options)
        {
            if (target.Kind != TargetKind.Unit || !Rules.AreEnemies(s, me, target.Unit)) continue;
            if (taunter >= 0 && target.Unit != taunter) continue;
            int score = AttackScore(s, me, target.Unit, headbutt) + Noise(5);
            if (score <= bestScore) continue;
            bestScore = score;
            best = BattleAction.Use(me, headbutt, target);
        }
        if (best != null) plan.Add(best.Value);
    }

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
            -Cost(_field[s.Idx(p)]) * 10 - BlastRisk(s, me, p) - (scampering ? 15 : 0) + (NextToMate(s, me, p) ? 4 : 0) + Noise(3));
        Go(s, me, scamper, withScamper, tile, plan);
    }

    /// <summary>The party member to go for: the nearest by walking, but the hero if he's at most two tiles further.</summary>
    private int PickTarget(BattleState s, int me)
    {
        ref var u = ref s.Units[me];
        int taunter = Taunter(s, me);
        if (taunter >= 0) return taunter;
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
        PrepareParty(s, me);
        var scamper = Tactics.Ready(s, me, MoveKind.Scamper);
        bool mainFree = !u.Has(UnitFlags.MainUsed);
        Candidates(s, me, scamper);
        var probe = s.Clone();
        int taunter = Taunter(s, me);

        Pos bestTile = u.Pos;
        bool bestScamper = false;
        BattleAction? bestShot = null;
        int bestScore = int.MinValue;
        for (int pass = 0; pass < 2; pass++)
        {
            bool withScamper = pass == 1;
            foreach (var p in withScamper ? _far : _near)
            {
                var (hitters, lethal, nearest) = PartyReach(s, me, p);
                int score = -hitters * 50 - lethal * 100 - BlastRisk(s, me, p) + Math.Min(Cost(nearest), 12) * 4 + s.Heights[s.Idx(p)] * 8
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
                            if (taunter >= 0 && target.Unit != taunter) continue;
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
        PrepareParty(s, me);
        int beast = Rules.BeastOf(s, me);
        var point = Tactics.Ready(s, me, MoveKind.Point);
        var whip = Tactics.Ready(s, me, MoveKind.Whip);
        var scamper = Tactics.Ready(s, me, MoveKind.Scamper);
        bool mainFree = !u.Has(UnitFlags.MainUsed);

        // The bonus action: point the beast at better prey, or else whip it on. It points from where
        // it stands, before moving, so the order is never out of range by the time it's given. The
        // beast acts straight after it and, without a target, does nothing.
        BattleAction? order = null;
        if (beast >= 0 && point != null)
        {
            Rules.Targets(s, me, point, _options);
            int want = BestPrey(s, beast, _options);
            if (want >= 0 && want != s.Units[beast].OrderUnit) order = BattleAction.Use(me, point, Target.OfUnit(want));
        }
        bool beastOut = beast >= 0 && s.Units[beast].OnBoard;
        bool wantWhip = order == null && whip != null && beastOut && s.Units[beast].BuffAmount == 0;
        var beastPos = beastOut ? s.Units[beast].Pos : Pos.None;

        // Where to stand: safe from the party, close to the beast (in whip reach if it's wanted), lashing if it can.
        var lash = mainFree ? Tactics.Ready(s, me, MoveKind.Ranged) : null;
        Candidates(s, me, order == null && !wantWhip ? scamper : null);
        var probe = s.Clone();
        int taunter = Taunter(s, me);
        Pos bestTile = u.Pos;
        bool bestScamper = false;
        BattleAction? bestLash = null;
        int bestScore = int.MinValue;
        for (int pass = 0; pass < 2; pass++)
        {
            bool withScamper = pass == 1;
            foreach (var p in withScamper ? _far : _near)
            {
                var (hitters, lethal, nearest) = PartyReach(s, me, p);
                int score = -hitters * 50 - lethal * 100 - BlastRisk(s, me, p) + Math.Min(Cost(nearest), 10) * 4 - (withScamper ? 10 : 0)
                    + (p == u.Pos ? Stay(s, me) : 0) + Noise(5);
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
                        if (taunter >= 0 && target.Unit != taunter) continue;
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

        if (order != null) plan.Add(order.Value);
        Go(s, me, scamper, bestScamper, bestTile, plan);
        if (bestLash != null) plan.Add(bestLash.Value);
        if (order == null && wantWhip && Rules.InReach(s, whip!, bestTile, beastPos)) plan.Add(BattleAction.Use(me, whip!, Target.OfUnit(beast)));
    }

    /// <summary>
    /// Of the party members it could point at, the one the beast should be after: close to it, the
    /// hero by preference, the weak and the held more so.
    /// </summary>
    private int BestPrey(BattleState s, int beast, List<(Target, Target)> pointable)
    {
        ref var b = ref s.Units[beast];
        bool onBoard = b.OnBoard;
        var from = onBoard ? b.Pos : b.BurrowTo;
        if (onBoard) Tactics.ReachFrom(s, beast, _reach);
        int best = -1, bestScore = int.MinValue;
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var t = ref s.Units[i];
            if (!t.Alive || !t.OnBoard || t.Side != Side.Party || !pointable.Contains((Target.OfUnit(i), Target.None))) continue;
            int cost = onBoard ? Tactics.ApproachCost(s, _reach, from, t.Pos) : Tactics.Far;
            if (cost == Tactics.Far) cost = from.Manhattan(t.Pos) * 2;
            int score = -cost * 5 - t.Hp * 3 + (i == s.Info.HeroIndex ? 30 : 0) + (t.Braced > 0 ? -30 : 0)
                + (t.Rooted > 0 || t.Has(UnitFlags.KnockedOver) ? 10 : 0);
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    // ------------------------------------------------------------------
    // Running away
    // ------------------------------------------------------------------

    /// <summary>A goblin whose nerve has broken heads for the nearest edge, away from the party, and gets away by ending its turn there.</summary>
    private void Flee(BattleState s, int me, List<BattleAction> plan)
    {
        ref var u = ref s.Units[me];
        if (s.OnEdge(u.Pos)) return;
        PrepareParty(s, me);
        Array.Clear(_goals, 0, s.TileCount);
        for (int tile = 0; tile < s.TileCount; tile++)
            if (s.OnEdge(s.PosOf(tile)) && s.Trap[tile] == TrapKind.None && Rules.CanEndOn(s, me, tile)) _goals[tile] = true;
        _t.CostToGo(s, me, _goals, _field);
        var scamper = Tactics.Ready(s, me, MoveKind.Scamper);
        var (tile2, withScamper, _) = BestTile(s, me, (p, scampering) =>
            -Cost(_field[s.Idx(p)]) * 20 + Math.Min(Cost(PartyDistanceCost(s, me, p)), 10) * 2 - (scampering ? 5 : 0));
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

    /// <summary>The unit that has taunted this one, if the taunt is still on and the taunter still in the fight; else -1.</summary>
    private static int Taunter(BattleState s, int me)
    {
        ref var u = ref s.Units[me];
        return u.Taunted > 0 && u.TauntBy >= 0 && s.Units[u.TauntBy].Alive && s.Units[u.TauntBy].OnBoard ? u.TauntBy : -1;
    }

    /// <summary>
    /// What standing on a tile risks from a burrowed beast bursting out nearby: the blast hits
    /// goblins too, and the beast usually comes up before this goblin's next turn.
    /// </summary>
    private static int BlastRisk(BattleState s, int me, Pos p)
    {
        int risk = 0;
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var b = ref s.Units[i];
            if (i == me || !b.Alive || !b.Has(UnitFlags.Burrowed)) continue;
            var burrow = Rules.KitMove(s, i, MoveKind.Burrow);
            if (burrow == null || b.BurrowTo.Manhattan(p) > burrow.Radius) continue;
            int damage = Math.Max(0, burrow.Damage + b.BuffAmount - s.Units[me].Defence);
            risk += damage * 30 + (damage >= s.Units[me].Hp ? 150 : 0);
        }
        return risk;
    }

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

    /// <summary>
    /// Sizes up the party for judging how safe a tile is for this goblin: how far each of them would
    /// have to walk, every tile each could hit next turn (its danger zone), and its hardest hit on
    /// this goblin.
    /// </summary>
    private void PrepareParty(BattleState s, int me)
    {
        _party.Clear();
        _partyDanger.Clear();
        _partyHit.Clear();
        for (int i = 0; i < s.Units.Length; i++)
            if (s.Units[i].Side == Side.Party && s.Units[i].Alive && s.Units[i].OnBoard && s.Units[i].Rider < 0) _party.Add(i);
        while (_partyReach.Count < _party.Count) _partyReach.Add(new int[s.TileCount]);
        for (int k = 0; k < _party.Count; k++)
        {
            int unit = _party[k];
            Tactics.ReachFrom(s, s.Units[unit].MountedOn >= 0 ? s.Units[unit].MountedOn : unit, _partyReach[k]);
            _partyDanger.Add(Observation.DangerOf(s, unit));
            int hardest = 0;
            foreach (int m in s.Info.Kits[unit])
                if (s.Data.Moves[m].UsesMain) hardest = Math.Max(hardest, Tactics.Damage(s, unit, me, s.Data.Moves[m]));
            _partyHit.Add(hardest);
        }
    }

    /// <summary>
    /// For a goblin on this tile: how many of the party could hit it next turn, how many of those
    /// could kill it outright, and how far (half-points) the nearest would have to walk to reach it.
    /// </summary>
    private (int Hitters, int Lethal, int Nearest) PartyReach(BattleState s, int me, Pos p)
    {
        int hitters = 0, lethal = 0, nearest = Tactics.Far;
        int tile = s.Idx(p);
        for (int k = 0; k < _party.Count; k++)
        {
            nearest = Math.Min(nearest, Tactics.ApproachCost(s, _partyReach[k], s.Units[_party[k]].Pos, p));
            if (!_partyDanger[k][tile]) continue;
            hitters++;
            if (_partyHit[k] >= s.Units[me].Hp) lethal++;
        }
        return (hitters, lethal, nearest);
    }

    private int PartyDistanceCost(BattleState s, int me, Pos p) => PartyReach(s, me, p).Nearest;

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

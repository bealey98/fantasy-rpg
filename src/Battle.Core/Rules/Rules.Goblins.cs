namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // Goblins and their beast
    //   All goblins: light weight; Scamper (bonus) for extra movement this turn.
    //   Morale: a goblin that's the only non-beast enemy left, with more than one of the hero's side
    //     still standing, tries to flee: it can only move, and it escapes if it ends a turn on the
    //     arena's edge. (Still open: whether a living Snuffler should count; here it doesn't.)
    //   Cutter: Shiv (Gang up: +1 when another goblin is also next to the target), Headbutt (a
    //     special: dazes the target and the Cutter), After you (swap with a neighbouring goblin).
    //   Plinker: Plink, Pocket dirt (blinds), Snap trap (a jaw trap on a tile; catches anyone).
    //   Beastmaster: Lash (reach 2), Point (tells its beast what to attack), Whip (+1 on the beast's
    //     next attack).
    //   Snuffler: Gnash, and Burrow: it goes underground and the destination rumbles; on its next
    //     turn it bursts out, hitting and knocking over everything in a small area and leaving a
    //     crater. A root on that tile catches it. Without its handler it goes wild and attacks the
    //     nearest unit, goblins included.
    // ------------------------------------------------------------------

    /// <summary>A beast whose handler is gone (dead or fled) is wild: it fights everyone, and everyone fights it.</summary>
    public static bool IsWild(BattleState s, int unit)
    {
        int handler = s.Units[unit].Handler;
        return handler >= 0 && !s.Units[handler].Alive;
    }

    // ---- Scamper ----

    private static void ScamperTargets(BattleState s, int actor, List<(Target, Target)> output)
    {
        if (!Anchored(s, actor)) output.Add((Target.Self, Target.None));
    }

    private static void ExecuteScamper(BattleState s, in BattleAction a, MoveDef move)
    {
        s.Units[a.Actor].MoveLeft += move.Amount;
        s.Emit(EventType.Scamper, a.Actor, a.Actor, s.Units[a.Actor].Pos, amount: move.Amount, move: move.Index);
    }

    /// <summary>Movement a unit could add this turn: Scamper, if its bonus action is still free.</summary>
    public static int ExtraMovementAvailable(BattleState s, int unit)
    {
        if (s.Units[unit].Has(UnitFlags.BonusUsed)) return 0;
        var scamper = KitMove(s, unit, MoveKind.Scamper);
        return scamper?.Amount ?? 0;
    }

    // ---- After you ----

    private static void SwapTargets(BattleState s, int actor, List<(Target, Target)> output)
    {
        if (Anchored(s, actor) || IsTethered(s, actor)) return;
        var pos = s.Units[actor].Pos;
        foreach (var d in Dirs.All)
        {
            int other = s.UnitAt(pos.Step(d));
            if (other < 0 || other == actor || s.Units[other].Side != s.Units[actor].Side) continue;
            if (!s.DefOf(other).Has(UnitTags.Goblin) || Anchored(s, other) || IsTethered(s, other)) continue;
            if (Math.Abs(s.Heights[s.Idx(s.Units[other].Pos)] - s.Heights[s.Idx(pos)]) > 1) continue;
            output.Add((Target.OfUnit(other), Target.None));
        }
    }

    private static void ExecuteSwap(BattleState s, in BattleAction a, MoveDef move)
    {
        int actor = a.Actor, other = a.Target.Unit;
        var p1 = s.Units[actor].Pos;
        var p2 = s.Units[other].Pos;
        s.RemoveFromBoard(actor);
        s.RemoveFromBoard(other);
        s.Place(actor, p2);
        s.Place(other, p1);
        s.Emit(EventType.Swap, actor, actor, p1, p2, move: move.Index);
        s.Emit(EventType.Swap, actor, other, p2, p1, move: move.Index);
    }

    // ---- Ranged attacks: Plink, Pocket dirt, Lash ----

    private static void RangedTargets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        var from = s.Units[actor].Pos;
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var u = ref s.Units[i];
            if (u.Alive && u.OnBoard && AreEnemies(s, actor, i) && u.Pos != from && InReach(s, move, from, u.Pos))
                output.Add((Target.OfUnit(i), Target.None));
        }
    }

    // ---- Snap trap ----

    private static void TrapTargets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        var from = s.Units[actor].Pos;
        for (int tile = 0; tile < s.TileCount; tile++)
        {
            var p = s.PosOf(tile);
            if (p == from || p.Manhattan(from) > move.Range) continue;
            if (!s.Standable(tile) || s.Occupant[tile] >= 0 || s.Trap[tile] != TrapKind.None) continue;
            output.Add((Target.OfTile(p), Target.None));
        }
    }

    private static void ExecuteTrap(BattleState s, in BattleAction a, MoveDef move)
    {
        // One snap trap per Plinker: lobbing a new one takes the old one away.
        for (int tile = 0; tile < s.TileCount; tile++)
            if (s.Trap[tile] == TrapKind.Snap && s.TrapOwner[tile] == a.Actor)
            {
                s.Trap[tile] = TrapKind.None;
                s.TrapOwner[tile] = -1;
                s.Emit(EventType.TrapEnd, a.Actor, -1, to: s.PosOf(tile), detail: "snap");
            }
        int t = s.Idx(a.Target.Tile);
        s.Trap[t] = TrapKind.Snap;
        s.TrapOwner[t] = a.Actor;
        s.TrapTimer[t] = 0; // lasts until something steps on it
        s.Emit(EventType.TrapSet, a.Actor, -1, to: a.Target.Tile, move: move.Index, detail: "snap");
    }

    /// <summary>A snap trap bites whoever steps on it, goblins included: its damage, then its status (slowed).</summary>
    private static void SpringSnapTrap(BattleState s, int owner, int body)
    {
        var trap = owner >= 0 ? KitMove(s, owner, MoveKind.Trap) : null;
        if (trap == null) return;
        if (trap.Damage > 0) DealDamage(s, owner, body, trap.Damage, trap.Index, detail: "snap trap");
        if (s.Units[body].Alive) ApplyStatus(s, owner, body, trap.Status, trap.StatusRounds, trap.Index);
        // It stops whoever is walking: the unit itself, or the hero riding it.
        int actor = CurrentActor(s);
        if (actor >= 0 && (actor == body || s.Units[actor].MountedOn == body)) s.Units[actor].MoveLeft = 0;
    }

    // ---- Point and Whip ----

    /// <summary>The first living, non-burrowed or burrowed beast this unit handles, or -1.</summary>
    public static int BeastOf(BattleState s, int handler)
    {
        for (int i = 0; i < s.Units.Length; i++)
            if (s.Units[i].Handler == handler && s.Units[i].Alive) return i;
        return -1;
    }

    /// <summary>
    /// Point: tell the beast what to attack. A unit order means "go and bite it"; a tile order means
    /// "burrow there". Units anywhere within the move's range count; tiles must be ones the beast
    /// could burrow to.
    /// </summary>
    private static void PointTargets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        int beast = BeastOf(s, actor);
        if (beast < 0) return;
        var from = s.Units[actor].Pos;
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var u = ref s.Units[i];
            if (u.Alive && u.OnBoard && AreEnemies(s, actor, i) && u.Pos.Manhattan(from) <= move.Range)
                output.Add((Target.OfUnit(i), Target.None));
        }
        var burrow = KitMove(s, beast, MoveKind.Burrow);
        if (burrow == null || !s.Units[beast].OnBoard) return;
        var beastPos = s.Units[beast].Pos;
        for (int tile = 0; tile < s.TileCount; tile++)
            if (CanBurrowTo(s, beastPos, burrow, tile)) output.Add((Target.OfTile(s.PosOf(tile)), Target.None));
    }

    private static void ExecutePoint(BattleState s, in BattleAction a, MoveDef move)
    {
        int beast = BeastOf(s, a.Actor);
        ref var b = ref s.Units[beast];
        if (a.Target.Kind == TargetKind.Unit)
        {
            b.Order = OrderKind.Unit;
            b.OrderUnit = a.Target.Unit;
            b.OrderTile = Pos.None;
            s.Emit(EventType.Order, a.Actor, a.Target.Unit, move: move.Index, detail: s.Label(beast));
        }
        else
        {
            b.Order = OrderKind.Tile;
            b.OrderUnit = -1;
            b.OrderTile = a.Target.Tile;
            s.Emit(EventType.Order, a.Actor, -1, to: a.Target.Tile, move: move.Index, detail: s.Label(beast));
        }
    }

    private static void WhipTargets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        int beast = BeastOf(s, actor);
        if (beast < 0 || !s.Units[beast].OnBoard || s.Units[beast].BuffAmount > 0) return;
        var from = s.Units[actor].Pos;
        if (InReach(s, move, from, s.Units[beast].Pos)) output.Add((Target.OfUnit(beast), Target.None));
    }

    private static void ExecuteWhip(BattleState s, in BattleAction a, MoveDef move)
    {
        ref var b = ref s.Units[a.Target.Unit];
        b.BuffAmount = move.Amount;
        // The buff waits for the beast's next attack, through to the end of the next enemy phase:
        // beasts are stronger than their handlers, so they act first, and a whip cracked after the
        // beast's turn has to last until its turn next round.
        b.Buffed = Rounds(1) + 1;
        s.Emit(EventType.Buff, a.Actor, a.Target.Unit, b.Pos, amount: move.Amount, move: move.Index);
    }

    // ---- Burrow ----

    private static bool CanBurrowTo(BattleState s, Pos from, MoveDef burrow, int tile)
    {
        var p = s.PosOf(tile);
        if (p == from || p.Manhattan(from) > burrow.Range) return false;
        var terrain = s.TerrainAt(tile);
        return terrain.Walkable && !terrain.Water && !terrain.Ladder && !(s.FeatureAt(tile)?.Blocks ?? false)
            && s.Occupant[tile] < 0 && s.Rumble[tile] < 0;
    }

    /// <summary>
    /// Burrow targets. On the board: tiles in range to dig towards (not while rooted, braced or
    /// tethered). Underground: only surfacing at the rumbling tile.
    /// </summary>
    private static void BurrowTargets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        ref var u = ref s.Units[actor];
        if (u.Has(UnitFlags.Burrowed))
        {
            output.Add((Target.OfTile(u.BurrowTo), Target.None));
            return;
        }
        if (Anchored(s, actor) || IsTethered(s, actor)) return;
        for (int tile = 0; tile < s.TileCount; tile++)
            if (CanBurrowTo(s, u.Pos, move, tile)) output.Add((Target.OfTile(s.PosOf(tile)), Target.None));
    }

    private static void ExecuteBurrow(BattleState s, in BattleAction a, MoveDef move)
    {
        ref var u = ref s.Units[a.Actor];
        if (u.Has(UnitFlags.Burrowed))
        {
            Surface(s, a.Actor, move, a.Timing);
            return;
        }
        var from = u.Pos;
        s.RemoveFromBoard(a.Actor);
        u.Set(UnitFlags.Burrowed);
        u.BurrowTo = a.Target.Tile;
        u.MoveLeft = 0;
        s.Rumble[s.Idx(a.Target.Tile)] = a.Actor;
        if (u.Order == OrderKind.Tile) u.Order = OrderKind.None; // a tile order is used up by digging
        s.Emit(EventType.Burrow, a.Actor, a.Actor, from, a.Target.Tile, move: move.Index);
    }

    /// <summary>
    /// Bursting out: everything within the radius of the rumbling tile takes the move's damage (plus
    /// any whip buff, minus a party unit's guard and defence) and is knocked over. The tile caves into a
    /// crater, one level lower. The beast lands there, or on the nearest free tile if something is
    /// standing on it, and a trap where it lands springs on it.
    /// </summary>
    private static void Surface(BattleState s, int unit, MoveDef move, Timing guard)
    {
        ref var u = ref s.Units[unit];
        var at = u.BurrowTo;
        int center = s.Idx(at);
        s.Rumble[center] = -1;
        u.Set(UnitFlags.Burrowed, false);
        u.BurrowTo = Pos.None;
        u.MoveLeft = 0;

        // Who's in the blast, in a fixed order.
        var hit = new List<int>();
        for (int i = 0; i < s.Units.Length; i++)
            if (i != unit && s.Units[i].Alive && s.Units[i].OnBoard && s.Units[i].Pos.Manhattan(at) <= move.Radius) hit.Add(i);

        var landing = s.Occupant[center] < 0 ? at : NearestFreeTile(s, at);
        s.Place(unit, landing);
        s.Emit(EventType.Surface, unit, unit, at, landing, move: move.Index);

        int buff = u.BuffAmount;
        foreach (int target in hit)
        {
            if (!s.Units[target].Alive) continue;
            ref var t = ref s.Units[target];
            int damage = move.Damage + buff;
            if (t.Side == Side.Party) damage -= RulesConfig.ByGrade(s.Data.Rules.GuardReduction, guard);
            damage -= t.Defence + t.BraceDefence;
            DealDamage(s, unit, target, Math.Max(0, damage), move.Index, t.Side == Side.Party ? guard : Timing.None);
            if (s.Units[target].Alive) ApplyStatus(s, unit, target, move.Status, move.StatusRounds, move.Index);
        }
        ConsumeBuff(s, unit);

        if (s.Heights[center] > 1)
        {
            s.Heights[center]--;
            s.Emit(EventType.Crater, unit, -1, to: at, amount: s.Heights[center], move: move.Index);
        }
        if (s.Units[unit].Alive) Arrive(s, unit, at);
    }

    private static Pos NearestFreeTile(BattleState s, Pos around)
    {
        Pos best = Pos.None;
        int bestDistance = int.MaxValue;
        for (int tile = 0; tile < s.TileCount; tile++)
        {
            if (!s.Standable(tile) || s.Occupant[tile] >= 0) continue;
            int d = s.PosOf(tile).Manhattan(around);
            if (d < bestDistance) { best = s.PosOf(tile); bestDistance = d; }
        }
        return best;
    }

    /// <summary>For danger zones and guards: could a Burrow aimed at this tile hit the party?</summary>
    private static bool BurrowCanHurtParty(BattleState s, in BattleAction a, MoveDef move)
    {
        if (!s.Units[a.Actor].Has(UnitFlags.Burrowed)) return false;
        var at = s.Units[a.Actor].BurrowTo;
        for (int i = 0; i < s.Units.Length; i++)
            if (s.Units[i].Side == Side.Party && s.Units[i].Alive && s.Units[i].OnBoard && s.Units[i].Pos.Manhattan(at) <= move.Radius)
                return true;
        return false;
    }

    // ---- Morale ----

    /// <summary>
    /// At the start of a goblin's turn: is it the only non-beast enemy left while more than one of
    /// the hero's side still stands? Then it tries to flee.
    /// </summary>
    private static void CheckNerve(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        if (!s.Data.Rules.FleeEnabled || u.Has(UnitFlags.Fleeing) || !s.DefOf(unit).Has(UnitTags.Goblin)) return;
        int goblinsLeft = 0, partyStanding = 0;
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var o = ref s.Units[i];
            if (!o.Alive) continue;
            if (o.Side == Side.Enemy && !s.DefOf(i).Has(UnitTags.Beast)) goblinsLeft++;
            if (o.Side == Side.Party && o.OnBoard) partyStanding++;
        }
        if (goblinsLeft == 1 && partyStanding > 1)
        {
            u.Set(UnitFlags.Fleeing);
            s.Emit(EventType.FleeStart, unit, unit, u.Pos);
        }
    }

    /// <summary>A fleeing goblin that ends its turn on the arena's edge gets away.</summary>
    private static void TryEscape(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        if (!u.Alive || !u.OnBoard || !u.Has(UnitFlags.Fleeing) || !s.OnEdge(u.Pos)) return;
        var where = u.Pos;
        s.RemoveFromBoard(unit);
        u.Set(UnitFlags.Alive, false);
        u.Set(UnitFlags.Fled);
        s.Emit(EventType.Fled, unit, unit, where);
        OnUnitGone(s, unit);
    }

    /// <summary>A fleeing goblin can only walk, scamper or end its turn.</summary>
    private static bool FleeingAllows(BattleState s, int actor, MoveDef move) =>
        !s.Units[actor].Has(UnitFlags.Fleeing) || move.Kind == MoveKind.Scamper;

    private static void OnTurnStart(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        if (u.Has(UnitFlags.Burrowed))
        {
            // Underground: its only action is to burst out (Burrow, aimed at the rumbling tile).
            u.MoveLeft = 0;
            u.Set(UnitFlags.BonusUsed);
        }
        if (u.Side == Side.Enemy) CheckNerve(s, unit);
    }

    private static void OnTurnEnd(BattleState s, int unit) => TryEscape(s, unit);

    /// <summary>
    /// When a unit leaves the fight: beasts ordered at it forget the order. A handler leaving makes its
    /// beast wild (see IsWild).
    /// </summary>
    private static void OnUnitGoneGoblins(BattleState s, int unit)
    {
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var b = ref s.Units[i];
            if (b.Order == OrderKind.Unit && b.OrderUnit == unit)
            {
                b.Order = OrderKind.None;
                b.OrderUnit = -1;
            }
        }
    }
}

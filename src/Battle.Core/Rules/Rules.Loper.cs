namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // The Loper, and riding it
    //   Gore (main, dismounted only): a charge with the antlers that knocks the target over.
    //     It's a charge, so it's handled with Bull Rush in Rules.Weapons.cs.
    //   Taunt (main): one enemy in range must attack the Loper, when it can, for a round.
    //   Brace (bonus, dismounted only): vines anchor the Loper: extra defence, but it can't move or
    //     be moved until its next turn.
    //
    // Mounting
    //   - The hero mounts a Loper on a neighbouring tile, or dismounts to a neighbouring tile.
    //     Either uses all his movement that turn, so he can't have moved first.
    //   - Mounted, the two share a tile and move as one. The hero rides at the Loper's speed on his
    //     own turn; the Loper can't walk on its turn while carrying him.
    //   - Each move says whether it works mounted (moves.json "mount": "dismounted" for those that don't).
    //   - Enemies can target either of the pair; area effects and pushes affect both, and the pair
    //     counts as the heavier of the two.
    // ------------------------------------------------------------------

    /// <summary>Movement for a unit's turn before statuses: a rider uses its mount's, and a carrying mount has none of its own.</summary>
    private static int MountedMovement(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        if (u.MountedOn >= 0) return s.Units[u.MountedOn].MoveMax;
        if (u.Rider >= 0) return 0;
        return u.MoveMax;
    }

    // ---- Taunt ----

    private static void TauntTargets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        var from = s.Units[actor].Pos;
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var u = ref s.Units[i];
            if (u.Alive && u.OnBoard && AreEnemies(s, actor, i) && InReach(s, move, from, u.Pos))
                output.Add((Target.OfUnit(i), Target.None));
        }
    }

    private static void ExecuteTaunt(BattleState s, in BattleAction a, MoveDef move) =>
        ApplyStatus(s, a.Actor, a.Target.Unit, StatusId.Taunted, move.Rounds, move.Index);

    /// <summary>
    /// A taunted unit's attacks must target its taunter whenever the taunter is a valid target for
    /// that move. Moves that don't target enemy units (traps, orders, buffs) are unaffected.
    /// </summary>
    private static void ApplyTaunt(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        ref var u = ref s.Units[actor];
        if (u.Taunted <= 0 || u.TauntBy < 0 || !s.Units[u.TauntBy].Alive) return;
        int taunter = u.TauntBy;
        bool canHitTaunter = false;
        foreach (var (first, _) in output)
            if (first.Kind == TargetKind.Unit && first.Unit == taunter) { canHitTaunter = true; break; }
        if (!canHitTaunter) return;
        output.RemoveAll(t => t.Item1.Kind == TargetKind.Unit && AreEnemies(s, actor, t.Item1.Unit) && t.Item1.Unit != taunter);
    }

    // ---- Brace ----

    private static void BraceTargets(BattleState s, int actor, List<(Target, Target)> output)
    {
        if (s.Units[actor].Braced == 0) output.Add((Target.Self, Target.None));
    }

    private static void ExecuteBrace(BattleState s, in BattleAction a, MoveDef move)
    {
        ref var u = ref s.Units[a.Actor];
        u.BraceDefence = move.Amount;
        ApplyStatus(s, a.Actor, a.Actor, StatusId.Braced, move.Rounds, move.Index);
    }

    // ---- Mounting ----

    /// <summary>The hero can mount if he hasn't moved this turn and a free Loper stands next to him.</summary>
    public static bool CanMount(BattleState s, int actor)
    {
        int loper = s.Info.LoperIndex;
        if (actor != s.Info.HeroIndex || loper < 0) return false;
        ref var h = ref s.Units[actor];
        ref var l = ref s.Units[loper];
        return h.MountedOn < 0 && !h.Has(UnitFlags.Moved) && !Anchored(s, actor)
            && l.Alive && l.OnBoard && l.Rider < 0 && MeleeReach(s, h.Pos, l.Pos) && !IsTethered(s, actor) && !IsTethered(s, loper);
    }

    /// <summary>Tiles the mounted hero can dismount to: free neighbours within a level, if he hasn't moved this turn.</summary>
    public static void DismountTiles(BattleState s, int actor, List<Pos> output)
    {
        output.Clear();
        ref var h = ref s.Units[actor];
        if (actor != s.Info.HeroIndex || h.MountedOn < 0 || h.Has(UnitFlags.Moved) || Anchored(s, actor)) return;
        foreach (var d in Dirs.All)
        {
            var p = h.Pos.Step(d);
            if (s.InBounds(p) && s.Standable(s.Idx(p)) && s.Occupant[s.Idx(p)] < 0 && s.Trap[s.Idx(p)] == TrapKind.None
                && Math.Abs(s.Heights[s.Idx(p)] - s.Heights[s.Idx(h.Pos)]) <= 1)
                output.Add(p);
        }
    }

    private static void AddMountActions(BattleState s, int actor, List<BattleAction> output)
    {
        if (CanMount(s, actor))
            output.Add(new BattleAction(ActionKind.Mount, actor, -1, Target.OfUnit(s.Info.LoperIndex), Target.None, Timing.None));
        var tiles = _tileBuffer ??= new List<Pos>();
        DismountTiles(s, actor, tiles);
        foreach (var p in tiles)
            output.Add(new BattleAction(ActionKind.Dismount, actor, -1, Target.OfTile(p), Target.None, Timing.None));
    }

    private static void ApplyMount(BattleState s, in BattleAction a)
    {
        ref var h = ref s.Units[a.Actor];
        if (a.Kind == ActionKind.Mount)
        {
            if (!CanMount(s, a.Actor) || a.Target.Unit != s.Info.LoperIndex) throw new IllegalActionException("the hero can't mount the Loper right now");
            int loper = s.Info.LoperIndex;
            var from = h.Pos;
            s.Occupant[s.Idx(from)] = -1;
            h.MountedOn = loper;
            s.Units[loper].Rider = a.Actor;
            h.Pos = s.Units[loper].Pos;
            h.MoveLeft = 0;
            h.Set(UnitFlags.Moved);
            s.Emit(EventType.Mount, a.Actor, loper, from, h.Pos);
            return;
        }

        var tiles = new List<Pos>();
        DismountTiles(s, a.Actor, tiles);
        if (!tiles.Contains(a.Target.Tile)) throw new IllegalActionException($"the hero can't dismount to {a.Target.Tile}");
        int mount = h.MountedOn;
        var at = h.Pos;
        s.Units[mount].Rider = -1;
        h.MountedOn = -1;
        h.Set(UnitFlags.OnBoard, false);
        s.Place(a.Actor, a.Target.Tile);
        h.MoveLeft = 0;
        h.Set(UnitFlags.Moved);
        s.Emit(EventType.Dismount, a.Actor, mount, at, a.Target.Tile);
        Arrive(s, a.Actor, at);
    }
}

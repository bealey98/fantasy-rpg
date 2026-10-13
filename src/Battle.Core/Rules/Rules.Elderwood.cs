namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // The Path of the Elderwood (Root)
    //
    // Tier 1
    //   Bind (bonus): root an enemy for a round, set a root trap on a tile, or bind the hero's
    //   weapon so his next hit roots its target. Bind traps only catch the caster's enemies.
    //   Tether (main): bind two targets together for 2 rounds: two enemies, an enemy and an
    //   object, or an enemy and an ally. The ends can't move further apart than they were, unless
    //   the heavier end drags the lighter one. Binding either end holds both.
    // Tier 2
    //   Bind adds damage, and a slow once the root ends.
    //   Tether splits into a light version that pulls its ends together and a dark one that saps
    //   each enemy end every round, healing the caster.
    //
    // Both draw on nearby growth (Rules.Sources.cs). Cast with no source in range, Bind only slows
    // and Tether lasts one round.
    // ------------------------------------------------------------------

    // ---- Bind ----

    private static void BindTargets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        var from = s.Units[actor].Pos;

        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var u = ref s.Units[i];
            if (u.Alive && u.OnBoard && AreEnemies(s, actor, i) && InReach(s, move, from, u.Pos))
                output.Add((Target.OfUnit(i), Target.None));
        }

        // Root traps: any empty, standable tile in range without a trap. Rumbling ground is fine:
        // a trap there catches whatever surfaces.
        for (int tile = 0; tile < s.TileCount; tile++)
        {
            var p = s.PosOf(tile);
            if (p.Manhattan(from) > move.Range || s.Occupant[tile] >= 0 || s.Trap[tile] != TrapKind.None || !s.Standable(tile)) continue;
            output.Add((Target.OfTile(p), Target.None));
        }

        if (s.Units[actor].Side == Side.Party && !s.Units[actor].Has(UnitFlags.WeaponBind))
            output.Add((Target.Weapon, Target.None));
    }

    private static void ExecuteBind(BattleState s, in BattleAction a, MoveDef move)
    {
        bool weak = PayForMove(s, a.Actor, move) == SourcePayment.Weak;
        switch (a.Target.Kind)
        {
            case TargetKind.Unit:
                Root(s, a.Actor, a.Target.Unit, move, weak);
                break;
            case TargetKind.Tile:
                int tile = s.Idx(a.Target.Tile);
                s.Trap[tile] = weak ? TrapKind.BindWeak : move.Upgraded ? TrapKind.BindUpgraded : TrapKind.Bind;
                s.TrapOwner[tile] = a.Actor;
                s.TrapTimer[tile] = Rounds(move.Rounds);
                s.Emit(EventType.TrapSet, a.Actor, -1, to: a.Target.Tile, move: move.Index, detail: weak ? "weak bind" : "bind");
                break;
            case TargetKind.Weapon:
                ref var u = ref s.Units[a.Actor];
                u.Set(UnitFlags.WeaponBind);
                u.Set(UnitFlags.WeaponBindWeak, weak);
                u.Set(UnitFlags.WeaponBindUpgraded, move.Upgraded && !weak);
                u.WeaponBindTimer = Rounds(move.Rounds);
                s.Emit(EventType.Status, a.Actor, a.Actor, u.Pos, amount: move.Rounds, move: move.Index, detail: "weapon_bind");
                break;
        }
    }

    /// <summary>
    /// Roots a unit for the Bind's rounds. A weak Bind slows instead. The Tier 2 Bind also deals its
    /// damage and leaves the target slowed once the root ends.
    /// </summary>
    private static void Root(BattleState s, int source, int target, MoveDef bind, bool weak)
    {
        if (!s.Units[target].Alive) return;
        if (weak)
        {
            ApplyStatus(s, source, target, StatusId.Slowed, bind.Rounds, bind.Index);
            return;
        }
        if (bind.Upgraded && bind.Damage > 0) DealDamage(s, source, target, bind.Damage, bind.Index);
        if (!s.Units[target].Alive) return;
        ApplyStatus(s, source, target, StatusId.Rooted, bind.Rounds, bind.Index);
        if (bind.Upgraded) s.Units[target].Set(UnitFlags.SlowAfterRoot);
    }

    /// <summary>The Tier 2 Bind: when the root ends, the target is slowed for a round.</summary>
    private static void OnRootEnded(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        if (!u.Has(UnitFlags.SlowAfterRoot)) return;
        u.Set(UnitFlags.SlowAfterRoot, false);
        ApplyStatus(s, -1, unit, StatusId.Slowed, 1);
    }

    /// <summary>A weapon Bind is used up by the hero's next hit, which roots (or, if weak, slows) its target.</summary>
    private static void OnWeaponHit(BattleState s, int attacker, int target)
    {
        ref var a = ref s.Units[attacker];
        if (!a.Has(UnitFlags.WeaponBind)) return;
        bool weak = a.Has(UnitFlags.WeaponBindWeak);
        a.Set(UnitFlags.WeaponBind | UnitFlags.WeaponBindWeak | UnitFlags.WeaponBindUpgraded, false);
        a.WeaponBindTimer = 0;
        var bind = KitMove(s, attacker, MoveKind.Bind);
        if (bind != null) Root(s, attacker, target, bind, weak);
    }

    // ---- Traps ----

    /// <summary>
    /// What happens when a unit lands on a tile: a trap there springs, and anything tethered to it
    /// is dragged along if it's heavy enough to drag.
    /// </summary>
    private static void ArriveEffects(BattleState s, int body, Pos from)
    {
        SpringTrap(s, body);
        if (s.Units[body].Alive && s.Units[body].OnBoard) DragTethered(s, body);
    }

    private static void SpringTrap(BattleState s, int body)
    {
        ref var u = ref s.Units[body];
        if (!u.Alive || !u.OnBoard) return;
        int tile = s.Idx(u.Pos);
        var kind = s.Trap[tile];
        if (kind == TrapKind.None) return;
        int owner = s.TrapOwner[tile];
        bool isBind = kind is TrapKind.Bind or TrapKind.BindWeak or TrapKind.BindUpgraded;
        // Root traps know friend from foe; snap traps catch anyone.
        if (isBind && (owner < 0 || !AreEnemies(s, owner, body))) return;

        s.Trap[tile] = TrapKind.None;
        s.TrapOwner[tile] = -1;
        s.TrapTimer[tile] = 0;
        s.Emit(EventType.TrapTriggered, owner, body, to: u.Pos, detail: isBind ? "bind" : "snap");

        if (isBind)
        {
            var bind = owner >= 0 ? KitMove(s, owner, MoveKind.Bind) : null;
            if (bind != null) Root(s, owner, body, bind, kind == TrapKind.BindWeak);
        }
        else SpringSnapTrap(s, owner, body);
    }

    // ---- Tether ----

    /// <summary>Which end of a tether a unit (or its mount or rider) is: 0 for A, 1 for B, -1 for neither.</summary>
    private static int TetherEnd(BattleState s, in Tether t, int unit)
    {
        if (!t.Active) return -1;
        if (t.UnitA == unit) return 0;
        if (t.UnitB == unit) return 1;
        return -1;
    }

    private static Pos EndPos(BattleState s, in Tether t, int end) =>
        end == 0 ? (t.UnitA >= 0 ? s.Units[t.UnitA].Pos : t.TileA) : (t.UnitB >= 0 ? s.Units[t.UnitB].Pos : t.TileB);

    private static int EndUnit(in Tether t, int end) => end == 0 ? t.UnitA : t.UnitB;

    public static bool IsTethered(BattleState s, int unit)
    {
        for (int i = 0; i < s.TetherCount; i++)
            if (TetherEnd(s, s.Tethers[i], unit) >= 0) return true;
        return false;
    }

    /// <summary>
    /// Can a unit at one end drag the other end along? Only if the other end is a unit that isn't
    /// held (rooted, braced) and is lighter. Objects never move, and equal weights hold each other.
    /// </summary>
    private static bool CanDrag(BattleState s, int mover, in Tether t, int moverEnd)
    {
        int other = EndUnit(t, 1 - moverEnd);
        if (other < 0 || Anchored(s, other)) return false;
        return PairWeight(s, mover) > PairWeight(s, other);
    }

    /// <summary>
    /// The tether rule for movement: a tethered unit may not step from <paramref name="from"/> to a tile
    /// further from the other end than the tether's length, unless it can drag the other end along.
    /// Stepping closer is always allowed.
    /// </summary>
    public static bool TetherAllows(BattleState s, int unit, Pos from, Pos to)
    {
        if (s.TetherCount == 0) return true;
        int body = s.Body(unit);
        for (int i = 0; i < s.TetherCount; i++)
        {
            ref var t = ref s.Tethers[i];
            int end = TetherEnd(s, t, body);
            if (end < 0) continue;
            var other = EndPos(s, t, 1 - end);
            int after = to.Manhattan(other);
            if (after <= t.Length || after <= from.Manhattan(other)) continue;
            if (!CanDrag(s, body, t, end)) return false;
        }
        return true;
    }

    /// <summary>After a heavier end moves, the lighter end is dragged after it, a tile at a time.</summary>
    private static void DragTethered(BattleState s, int body)
    {
        for (int i = 0; i < s.TetherCount; i++)
        {
            int end = TetherEnd(s, s.Tethers[i], body);
            if (end < 0 || !CanDrag(s, body, s.Tethers[i], end)) continue;
            int other = EndUnit(s.Tethers[i], 1 - end);
            for (int guard = 0; guard < 16; guard++)
            {
                if (!s.Tethers[i].Active || other < 0 || !s.Units[other].Alive) break;
                var otherPos = s.Units[other].Pos;
                var mine = s.Units[body].Pos;
                if (otherPos.Manhattan(mine) <= s.Tethers[i].Length) break;
                if (Push(s, body, other, Dirs.Toward(otherPos, mine), 1, EventType.Drag) == 0
                    && Push(s, body, other, OtherAxis(otherPos, mine), 1, EventType.Drag) == 0)
                {
                    EndTether(s, i, "it snaps");
                    break;
                }
            }
        }
    }

    /// <summary>The second-best direction from one tile towards another (the other axis), for when the first is blocked.</summary>
    private static Dir OtherAxis(Pos from, Pos to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Abs(dx) >= Math.Abs(dy)) return dy != 0 ? (dy > 0 ? Dir.South : Dir.North) : Dirs.Toward(from, to);
        return dx != 0 ? (dx > 0 ? Dir.East : Dir.West) : Dirs.Toward(from, to);
    }

    /// <summary>
    /// Tether targets: the first end is an enemy within range; the second is another enemy, an ally
    /// or an object tile (a tree or rock) within range. The ends must be close enough together, and
    /// neither can already be tethered. Mounted pairs can't be tethered (yet).
    /// </summary>
    private static void TetherTargets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        var from = s.Units[actor].Pos;
        int maxLength = s.Data.Rules.TetherMaxLength;
        for (int a = 0; a < s.Units.Length; a++)
        {
            if (!Tetherable(s, a) || !AreEnemies(s, actor, a) || s.Units[a].Pos.Manhattan(from) > move.Range) continue;
            var pa = s.Units[a].Pos;
            for (int b = 0; b < s.Units.Length; b++)
            {
                if (b == a || !Tetherable(s, b) || s.Units[b].Pos.Manhattan(from) > move.Range) continue;
                if (AreEnemies(s, actor, b) && b < a) continue; // each enemy pair once
                int d = s.Units[b].Pos.Manhattan(pa);
                if (d >= 1 && d <= maxLength) output.Add((Target.OfUnit(a), Target.OfUnit(b)));
            }
            for (int tile = 0; tile < s.TileCount; tile++)
            {
                if (!s.IsObject(tile)) continue;
                var p = s.PosOf(tile);
                int d = p.Manhattan(pa);
                if (p.Manhattan(from) <= move.Range && d >= 1 && d <= maxLength)
                    output.Add((Target.OfUnit(a), Target.OfTile(p)));
            }
        }
    }

    private static bool Tetherable(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        return u.Alive && u.OnBoard && u.MountedOn < 0 && u.Rider < 0 && !IsTethered(s, unit);
    }

    private static void ExecuteTether(BattleState s, in BattleAction a, MoveDef move)
    {
        bool weak = PayForMove(s, a.Actor, move) == SourcePayment.Weak;
        int slot = 0;
        while (slot < s.TetherCount && s.Tethers[slot].Active) slot++;
        if (slot == s.Tethers.Length) Array.Resize(ref s.Tethers, s.Tethers.Length * 2);
        if (slot == s.TetherCount) s.TetherCount++;

        int unitA = a.Target.Unit;
        int unitB = a.Target2.Kind == TargetKind.Unit ? a.Target2.Unit : -1;
        var tileB = a.Target2.Kind == TargetKind.Tile ? a.Target2.Tile : Pos.None;
        var posA = s.Units[unitA].Pos;
        var posB = unitB >= 0 ? s.Units[unitB].Pos : tileB;
        s.Tethers[slot] = new Tether
        {
            Active = true,
            Caster = a.Actor,
            UnitA = unitA,
            UnitB = unitB,
            TileA = Pos.None,
            TileB = tileB,
            Length = Math.Max(1, posA.Manhattan(posB)),
            Timer = Rounds(weak ? 1 : move.Rounds),
            Variant = move.Variant,
            Amount = move.Amount,
        };
        s.Emit(EventType.TetherSet, a.Actor, unitA, posA, posB, s.Tethers[slot].Length, move: move.Index, detail: TetherLabel(s, slot));

        if (move.Variant == TetherVariant.Pull && !weak) PullTogether(s, a.Actor, slot, move.Amount);
    }

    public static string TetherLabel(BattleState s, int slot)
    {
        var t = s.Tethers[slot];
        string end(int unit, Pos tile) => unit >= 0 ? s.Label(unit) : $"{s.FeatureAt(s.Idx(tile))?.Id ?? s.TerrainAt(s.Idx(tile)).Id}@{tile.X},{tile.Y}";
        return $"{end(t.UnitA, t.TileA)}~{end(t.UnitB, t.TileB)}";
    }

    /// <summary>
    /// The light Tier 2 Tether pulls its ends together, a tile at a time, up to its amount. The
    /// lighter end moves towards the heavier; equal weights meet in the middle; objects and held
    /// units stay put. Collisions and falls on the way count.
    /// </summary>
    private static void PullTogether(BattleState s, int caster, int slot, int maxSteps)
    {
        for (int step = 0; step < maxSteps; step++)
        {
            ref var t = ref s.Tethers[slot];
            if (!t.Active) return;
            var pa = EndPos(s, t, 0);
            var pb = EndPos(s, t, 1);
            if (pa.Manhattan(pb) <= 1) break;

            int a = t.UnitA, b = t.UnitB;
            bool aFree = a >= 0 && !Anchored(s, a), bFree = b >= 0 && !Anchored(s, b);
            int mover;
            if (aFree && !bFree) mover = a;
            else if (bFree && !aFree) mover = b;
            else if (!aFree && !bFree) break;
            else
            {
                var wa = PairWeight(s, a);
                var wb = PairWeight(s, b);
                mover = wa < wb ? a : wb < wa ? b : (step % 2 == 0 ? a : b);
            }
            var from = s.Units[mover].Pos;
            var toward = mover == a ? pb : pa;
            if (Push(s, caster, mover, Dirs.Toward(from, toward), 1, EventType.Pull) == 0
                && Push(s, caster, mover, OtherAxis(from, toward), 1, EventType.Pull) == 0) break;
        }
        ref var tether = ref s.Tethers[slot];
        if (tether.Active) tether.Length = Math.Max(1, EndPos(s, tether, 0).Manhattan(EndPos(s, tether, 1)));
    }

    private static void EndTether(BattleState s, int slot, string why)
    {
        ref var t = ref s.Tethers[slot];
        if (!t.Active) return;
        string label = TetherLabel(s, slot);
        t.Active = false;
        s.Emit(EventType.TetherEnd, t.Caster, t.UnitA, detail: label, move: -1);
        while (s.TetherCount > 0 && !s.Tethers[s.TetherCount - 1].Active) s.TetherCount--;
    }

    /// <summary>The dark Tier 2 Tether: at the end of each round it saps each enemy end and heals the caster.</summary>
    private static void SapTethers(BattleState s)
    {
        for (int i = 0; i < s.TetherCount; i++)
        {
            var t = s.Tethers[i];
            if (!t.Active || t.Variant != TetherVariant.Sap) continue;
            int total = 0;
            foreach (int end in new[] { t.UnitA, t.UnitB })
            {
                if (end < 0 || !s.Units[end].Alive || !AreEnemies(s, t.Caster, end)) continue;
                int before = s.Units[end].Hp;
                DealDamage(s, t.Caster, end, t.Amount, detail: "sap");
                total += before - s.Units[end].Hp;
            }
            if (total > 0 && s.Units[t.Caster].Alive) Heal(s, t.Caster, t.Caster, total);
        }
    }

    // ---- Cleaning up ----

    /// <summary>When a unit leaves the fight, its tethers break.</summary>
    private static void OnUnitGone(BattleState s, int unit)
    {
        for (int i = 0; i < s.TetherCount; i++)
            if (TetherEnd(s, s.Tethers[i], unit) >= 0) EndTether(s, i, "its end is gone");
        OnUnitGoneGoblins(s, unit);
    }
}

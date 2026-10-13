namespace Battle.Core;

/// <summary>What a timing grade is for: the attacker's action command or the defender's guard.</summary>
public enum TimingNeed : byte { None, Attack, Guard }

public enum AttackKind : byte { Melee, Ranged, Spell }

public static partial class Rules
{
    // ------------------------------------------------------------------
    // Who can be hit
    // ------------------------------------------------------------------

    public static bool AreEnemies(BattleState s, int a, int b) => s.Units[a].Side != s.Units[b].Side;

    /// <summary>
    /// Adds the living enemies of <paramref name="actor"/> standing on a tile: the unit there and, for
    /// a mounted pair, its rider too (either can be targeted).
    /// </summary>
    private static void AddEnemiesOn(BattleState s, int actor, Pos p, List<(Target, Target)> output)
    {
        int unit = s.UnitAt(p);
        if (unit < 0) return;
        if (s.Units[unit].Alive && AreEnemies(s, actor, unit)) output.Add((Target.OfUnit(unit), Target.None));
        int rider = s.Units[unit].Rider;
        if (rider >= 0 && s.Units[rider].Alive && AreEnemies(s, actor, rider)) output.Add((Target.OfUnit(rider), Target.None));
    }

    /// <summary>The units standing on a tile: the occupant and, for a mounted pair, its rider.</summary>
    public static void UnitsOn(BattleState s, Pos p, List<int> output)
    {
        output.Clear();
        int unit = s.UnitAt(p);
        if (unit < 0) return;
        output.Add(unit);
        if (s.Units[unit].Rider >= 0) output.Add(s.Units[unit].Rider);
    }

    /// <summary>Can a melee attack reach from one tile to a neighbouring one? Not up or down a cliff.</summary>
    public static bool MeleeReach(BattleState s, Pos from, Pos to) =>
        from.Manhattan(to) == 1 && Math.Abs(s.Heights[s.Idx(from)] - s.Heights[s.Idx(to)]) <= s.Data.Rules.MeleeMaxHeightDiff;

    /// <summary>Enemies on neighbouring tiles within melee reach.</summary>
    private static void MeleeTargets(BattleState s, int actor, List<(Target, Target)> output)
    {
        var pos = s.Units[actor].Pos;
        foreach (var d in Dirs.All)
        {
            var p = pos.Step(d);
            if (s.InBounds(p) && MeleeReach(s, pos, p)) AddEnemiesOn(s, actor, p, output);
        }
    }

    // ------------------------------------------------------------------
    // Hitting
    // ------------------------------------------------------------------

    /// <summary>
    /// One attack from one unit on another. In order:
    ///   1. A blinded attacker (with an action command) misses on a fumble.
    ///   2. A melee attack on a unit with Parry readied is blocked; a good guard strikes back.
    ///   3. Damage: the move's damage, plus Gang up, plus any whip buff, plus the attacker's timing
    ///      bonus (none while blinded), minus the defender's guard, minus defence. Never below 0.
    ///   4. The move's status lands, and a weapon Bind roots the target.
    /// Returns true if the attack landed.
    /// </summary>
    public static bool Attack(BattleState s, int attacker, int target, MoveDef move, Timing timing, AttackKind kind)
    {
        ref var a = ref s.Units[attacker];
        ref var t = ref s.Units[target];
        if (!t.Alive) return false;

        bool command = a.Side == Side.Party && move.Command;
        bool blinded = a.Blinded > 0 && kind != AttackKind.Spell && command;
        if (blinded && timing == Timing.Fumble)
        {
            s.Emit(EventType.Miss, attacker, target, a.Pos, t.Pos, timing: timing, move: move.Index, detail: "blinded");
            ConsumeBuff(s, attacker);
            return false;
        }

        if (kind == AttackKind.Melee && t.Has(UnitFlags.ParryReady) && AreEnemies(s, attacker, target))
        {
            ResolveParry(s, attacker, target, move, timing);
            ConsumeBuff(s, attacker);
            return false;
        }

        if (move.Damage > 0)
        {
            int damage = move.Damage + GangUpBonus(s, attacker, target, move) + a.BuffAmount;
            if (command && !blinded) damage += RulesConfig.ByGrade(s.Data.Rules.AttackBonus, timing);
            if (t.Side == Side.Party && a.Side == Side.Enemy) damage -= RulesConfig.ByGrade(s.Data.Rules.GuardReduction, timing);
            damage -= t.Defence + t.BraceDefence;
            DealDamage(s, attacker, target, Math.Max(0, damage), move.Index, timing);
        }
        ConsumeBuff(s, attacker);

        if (s.Units[target].Alive)
        {
            ApplyStatus(s, attacker, target, move.Status, move.StatusRounds, move.Index);
            if (kind == AttackKind.Melee && s.Units[attacker].Has(UnitFlags.WeaponBind)) OnWeaponHit(s, attacker, target);
        }
        return true;
    }

    /// <summary>Gang up: extra damage when another goblin is also next to the target.</summary>
    private static int GangUpBonus(BattleState s, int attacker, int target, MoveDef move)
    {
        if (move.GangUp <= 0) return 0;
        var where = s.Units[target].Pos;
        for (int i = 0; i < s.Units.Length; i++)
        {
            if (i == attacker || !s.Units[i].Alive || !s.Units[i].OnBoard || !s.DefOf(i).Has(UnitTags.Goblin)) continue;
            if (s.Units[i].Side == s.Units[attacker].Side && s.Units[i].Pos.Manhattan(where) == 1) return move.GangUp;
        }
        return 0;
    }

    /// <summary>A whip buff lasts until its unit's next attack.</summary>
    private static void ConsumeBuff(BattleState s, int attacker)
    {
        ref var a = ref s.Units[attacker];
        if (a.BuffAmount == 0) return;
        a.BuffAmount = 0;
        a.Buffed = 0;
        s.Emit(EventType.StatusEnd, attacker, attacker, detail: "buffed");
    }

    /// <summary>
    /// Parry: the block is automatic and stops the whole attack. Striking back needs the defender's
    /// timing: a good or perfect guard counters for the Parry move's amount.
    /// </summary>
    private static void ResolveParry(BattleState s, int attacker, int defender, MoveDef move, Timing guard)
    {
        ref var d = ref s.Units[defender];
        d.Set(UnitFlags.ParryReady, false);
        s.Emit(EventType.Parry, attacker, defender, s.Units[attacker].Pos, d.Pos, timing: guard, move: move.Index);
        if (guard is not (Timing.Good or Timing.Perfect)) return;
        var parry = KitMove(s, defender, MoveKind.Parry);
        int counter = parry?.Amount ?? 1;
        s.Emit(EventType.Counter, defender, attacker, d.Pos, s.Units[attacker].Pos, timing: guard, move: parry?.Index ?? -1);
        DealDamage(s, defender, attacker, counter, parry?.Index ?? -1, guard, "counter");
        if (s.Units[attacker].Alive && s.Units[defender].Has(UnitFlags.WeaponBind)) OnWeaponHit(s, defender, attacker);
    }

    /// <summary>The first move of a kind in a unit's kit, or null.</summary>
    public static MoveDef? KitMove(BattleState s, int unit, MoveKind kind)
    {
        foreach (int m in s.Info.Kits[unit])
            if (s.Data.Moves[m].Kind == kind) return s.Data.Moves[m];
        return null;
    }

    /// <summary>Takes HP from a unit, logs it, and defeats the unit at 0 HP.</summary>
    public static void DealDamage(BattleState s, int source, int target, int amount, int move = -1,
        Timing timing = Timing.None, string? detail = null)
    {
        ref var t = ref s.Units[target];
        if (!t.Alive) return;
        amount = Math.Max(0, amount);
        t.Hp = Math.Max(0, t.Hp - amount);
        if (source >= 0 && amount > 0 && t.OnBoard) s.Disturbed[s.Idx(t.Pos)] = true;
        s.Emit(EventType.Damage, source, target, t.Pos, t.Pos, amount, t.Hp, timing, move, detail);
        if (t.Hp == 0) Defeat(s, target, source);
    }

    public static void Heal(BattleState s, int source, int target, int amount, int move = -1)
    {
        ref var t = ref s.Units[target];
        if (!t.Alive || amount <= 0) return;
        int healed = Math.Min(amount, t.MaxHp - t.Hp);
        t.Hp += healed;
        s.Emit(EventType.Heal, source, target, t.Pos, t.Pos, healed, t.Hp, move: move);
    }

    /// <summary>
    /// A unit at 0 HP leaves the fight. A companion is knocked out for the rest of the battle rather
    /// than killed. Whatever it was part of (tethers, orders, a mount) is cleaned up.
    /// </summary>
    public static void Defeat(BattleState s, int unit, int by)
    {
        ref var u = ref s.Units[unit];
        bool companion = s.DefOf(unit).Has(UnitTags.Companion);
        var where = u.Pos;
        s.RemoveFromBoard(unit);
        u.Set(UnitFlags.Alive, false);
        if (companion)
        {
            u.Set(UnitFlags.KnockedOut);
            s.Emit(EventType.KnockedOut, by, unit, where);
        }
        else s.Emit(EventType.Death, by, unit, where);
        OnUnitGone(s, unit);
    }

    // ------------------------------------------------------------------
    // Timing inputs
    // ------------------------------------------------------------------

    /// <summary>
    /// Does this action need a timing grade, and whose? A party move with an action command needs
    /// the attacker's grade. An enemy move that can hurt the party needs the defender's guard.
    /// </summary>
    public static TimingNeed TimingFor(BattleState s, in BattleAction a)
    {
        if (a.Kind is not (ActionKind.Main or ActionKind.Bonus or ActionKind.Special) || a.Move < 0) return TimingNeed.None;
        var move = s.Data.Moves[a.Move];
        if (s.Units[a.Actor].Side == Side.Party) return move.Command ? TimingNeed.Attack : TimingNeed.None;
        return CanHurtParty(s, a, move) ? TimingNeed.Guard : TimingNeed.None;
    }

    private static bool CanHurtParty(BattleState s, in BattleAction a, MoveDef move)
    {
        if (a.Target.Kind == TargetKind.Unit && a.Target.Unit >= 0 && s.Units[a.Target.Unit].Side == Side.Party)
            return move.Damage > 0 || move.Status != StatusId.None;
        return AreaCanHurtParty(s, a, move);
    }
}

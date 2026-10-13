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
    /// One attack from one unit on another, using a move's damage. Timing, guards, statuses and the
    /// rest join in step 5; for now an attack simply deals the move's damage.
    /// </summary>
    public static void Attack(BattleState s, int attacker, int target, MoveDef move, Timing timing, AttackKind kind)
    {
        DealDamage(s, attacker, target, move.Damage, move.Index, timing);
    }

    /// <summary>Takes HP from a unit, logs it, and defeats the unit at 0 HP.</summary>
    public static void DealDamage(BattleState s, int source, int target, int amount, int move = -1,
        Timing timing = Timing.None, string? detail = null)
    {
        ref var t = ref s.Units[target];
        if (!t.Alive) return;
        amount = Math.Max(0, amount);
        t.Hp = Math.Max(0, t.Hp - amount);
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

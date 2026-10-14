namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // Status effects
    //   rooted        can't move (or be pushed) while it lasts
    //   slowed        less movement each turn
    //   blinded       its attacks miss if their action command is fumbled, and a good command only
    //                 lands the hit, without its usual bonus. Spells aren't affected.
    //   dazed         loses its next bonus action
    //   knocked over  getting up drains movement at the start of its next turn
    //   taunted       must attack its taunter when it can
    //   braced        extra defence, but can't move (or be pushed)
    // ------------------------------------------------------------------

    public static string StatusName(StatusId status) => status switch
    {
        StatusId.Rooted => "rooted",
        StatusId.Slowed => "slowed",
        StatusId.Blinded => "blinded",
        StatusId.Dazed => "dazed",
        StatusId.KnockedOver => "knocked_over",
        StatusId.Taunted => "taunted",
        StatusId.Braced => "braced",
        _ => "",
    };

    /// <summary>
    /// Puts a status on a unit. A timed status already running is extended to the longer of the two.
    /// A status counts from the phase it lands in, so one that lands during its victim's own phase
    /// (it stepped on a trap) gets one more phase end, to last through the victim's next turn too.
    /// Statuses a unit gives itself (Brace) don't.
    /// </summary>
    public static void ApplyStatus(BattleState s, int source, int target, StatusId status, int rounds, int move = -1)
    {
        if (status == StatusId.None) return;
        ref var t = ref s.Units[target];
        if (!t.Alive) return;
        int timer = Rounds(Math.Max(1, rounds));
        if (source != target && ActingSide(s) == t.Side) timer++;
        switch (status)
        {
            case StatusId.Rooted:
                t.Rooted = Math.Max(t.Rooted, timer);
                if (CurrentActor(s) == target) t.MoveLeft = 0;
                break;
            case StatusId.Slowed:
                t.Slowed = Math.Max(t.Slowed, timer);
                break;
            case StatusId.Blinded:
                t.Blinded = Math.Max(t.Blinded, timer);
                break;
            case StatusId.Taunted:
                t.Taunted = Math.Max(t.Taunted, timer);
                t.TauntBy = source;
                break;
            case StatusId.Braced:
                t.Braced = Math.Max(t.Braced, timer);
                if (CurrentActor(s) == target) t.MoveLeft = 0;
                break;
            case StatusId.Dazed:
                t.Set(UnitFlags.Dazed);
                break;
            case StatusId.KnockedOver:
                t.Set(UnitFlags.KnockedOver);
                break;
        }
        bool timed = status is not (StatusId.Dazed or StatusId.KnockedOver);
        s.Emit(EventType.Status, source, target, t.Pos, amount: timed ? Math.Max(1, rounds) : 0, move: move, detail: StatusName(status));
    }

    /// <summary>The side whose phase it is, or null between them (the creatures step).</summary>
    private static Side? ActingSide(BattleState s) => s.Phase switch
    {
        Phase.Party => Side.Party,
        Phase.Enemy => Side.Enemy,
        _ => null,
    };

    /// <summary>
    /// Rooted or braced units can't move or be moved. For a rider and its mount, either one being
    /// held holds the pair.
    /// </summary>
    public static bool Anchored(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        if (u.Rooted > 0 || u.Braced > 0) return true;
        int partner = u.MountedOn >= 0 ? u.MountedOn : u.Rider;
        return partner >= 0 && (s.Units[partner].Rooted > 0 || s.Units[partner].Braced > 0);
    }
}

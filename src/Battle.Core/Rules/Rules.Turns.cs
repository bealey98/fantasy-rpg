namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // The flow of a battle
    //
    // A round is: the party phase (the hero and the Loper, in the chosen order), the creatures
    // step (living features move), then the enemy phase (strongest first). Each unit's turn
    // starts automatically; it then takes inputs until it chooses End.
    // ------------------------------------------------------------------

    /// <summary>Starts the battle: round 1, the first phase and the first turn.</summary>
    public static void Start(BattleState s)
    {
        if (s.Round > 0) throw new InvalidOperationException("the battle has already started");
        var first = s.Phase;
        s.Round = 1;
        s.Emit(EventType.RoundStart, amount: 1);
        CheckOutcome(s);
        if (s.Outcome == Outcome.Ongoing) BeginPhase(s, first);
    }

    /// <summary>The unit whose turn it is, or -1 when the battle hasn't started or is over.</summary>
    public static int CurrentActor(BattleState s) =>
        s.Outcome == Outcome.Ongoing && s.Round > 0 && s.OrderPos < s.OrderCount ? s.Order[s.OrderPos] : -1;

    public static string PhaseName(Phase phase) => phase switch
    {
        Phase.Party => "party",
        Phase.Creatures => "creatures",
        _ => "enemy",
    };

    private static void BeginPhase(BattleState s, Phase phase)
    {
        s.Phase = phase;
        s.PhaseActed = false;
        s.Emit(EventType.PhaseStart, detail: PhaseName(phase));
        if (phase == Phase.Creatures)
        {
            s.OrderCount = 0;
            s.OrderPos = 0;
            CreatureStep(s);
            EndPhase(s);
            return;
        }
        BuildOrder(s, phase == Phase.Party ? Side.Party : Side.Enemy);
        s.OrderPos = 0;
        AdvanceToNextActor(s);
    }

    /// <summary>
    /// Works out who acts in a phase. The party: the hero then the Loper, unless the player swapped
    /// them. Enemies: strongest first (by power), ties broken by unit number.
    /// </summary>
    private static void BuildOrder(BattleState s, Side side)
    {
        s.OrderCount = 0;
        if (side == Side.Party)
        {
            int hero = s.Info.HeroIndex, loper = s.Info.LoperIndex;
            if (s.LoperFirst && loper >= 0) { s.Order[s.OrderCount++] = loper; s.Order[s.OrderCount++] = hero; }
            else
            {
                s.Order[s.OrderCount++] = hero;
                if (loper >= 0) s.Order[s.OrderCount++] = loper;
            }
            return;
        }

        for (int i = 0; i < s.Units.Length; i++)
            if (s.Units[i].Side == Side.Enemy && s.Units[i].Alive) s.Order[s.OrderCount++] = i;
        // Insertion sort: strongest first, then by unit number. Small lists, fully deterministic.
        for (int i = 1; i < s.OrderCount; i++)
        {
            int unit = s.Order[i], j = i - 1;
            while (j >= 0 && Before(s, unit, s.Order[j])) { s.Order[j + 1] = s.Order[j]; j--; }
            s.Order[j + 1] = unit;
        }

        static bool Before(BattleState s, int a, int b) =>
            s.Units[a].Power != s.Units[b].Power ? s.Units[a].Power > s.Units[b].Power : a < b;
    }

    /// <summary>Can this unit take a turn? It must be alive, and on the board or underground.</summary>
    private static bool CanTakeTurn(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        return u.Alive && (u.OnBoard || u.Has(UnitFlags.Burrowed));
    }

    /// <summary>Starts the next unit's turn in the order, or ends the phase if nobody is left.</summary>
    private static void AdvanceToNextActor(BattleState s)
    {
        while (s.OrderPos < s.OrderCount)
        {
            int unit = s.Order[s.OrderPos];
            if (CanTakeTurn(s, unit))
            {
                StartTurn(s, unit);
                return;
            }
            s.OrderPos++;
        }
        EndPhase(s);
    }

    /// <summary>
    /// The start of a unit's turn: fresh actions and movement, adjusted by its statuses.
    ///   - Rooted or braced: no movement.
    ///   - Slowed: less movement.
    ///   - Knocked over: getting up drains movement (once).
    ///   - Dazed: no bonus action this turn (once).
    ///   - A readied Parry lasts until the start of the parrying unit's next turn.
    /// </summary>
    private static void StartTurn(BattleState s, int unit)
    {
        s.TurnStartBackup = s.Units[unit];
        ref var u = ref s.Units[unit];
        u.Set(UnitFlags.Moved | UnitFlags.MainUsed | UnitFlags.BonusUsed, false);
        u.Set(UnitFlags.ParryReady, false);
        u.MoveLeft = TurnMovement(s, unit);
        s.Emit(EventType.TurnStart, unit, unit, u.Pos);

        if (u.Has(UnitFlags.KnockedOver))
        {
            u.Set(UnitFlags.KnockedOver, false);
            int drain = Math.Min(u.MoveLeft, s.Data.Rules.KnockedOverDrain);
            u.MoveLeft -= drain;
            s.Emit(EventType.StatusEnd, unit, unit, amount: drain, detail: "knocked_over");
        }
        if (u.Has(UnitFlags.Dazed))
        {
            u.Set(UnitFlags.Dazed, false);
            u.Set(UnitFlags.BonusUsed);
            s.Emit(EventType.StatusEnd, unit, unit, detail: "dazed");
        }
        OnTurnStart(s, unit);
    }

    /// <summary>Movement for the turn about to start, in half-points.</summary>
    public static int TurnMovement(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        if (Anchored(s, unit)) return 0;
        int move = MountedMovement(s, unit);
        if (u.Slowed > 0) move -= s.Data.Rules.SlowAmount;
        return Math.Max(0, move);
    }

    /// <summary>Ends the current unit's turn and starts the next one.</summary>
    private static void EndTurn(BattleState s)
    {
        int unit = CurrentActor(s);
        OnTurnEnd(s, unit);
        ulong checksum = s.Sink.Enabled ? s.Checksum() : 0;
        s.Emit(EventType.TurnEnd, unit, unit, checksum: checksum);
        s.OrderPos++;
        CheckOutcome(s);
        if (s.Outcome == Outcome.Ongoing) AdvanceToNextActor(s);
    }

    private static void EndPhase(BattleState s)
    {
        if (s.Phase is Phase.Party or Phase.Enemy) TickTimers(s);
        CheckOutcome(s);
        if (s.Outcome != Outcome.Ongoing) return;
        switch (s.Phase)
        {
            case Phase.Party:
                BeginPhase(s, Phase.Creatures);
                break;
            case Phase.Creatures:
                BeginPhase(s, Phase.Enemy);
                break;
            default:
                EndRound(s);
                break;
        }
    }

    private static void EndRound(BattleState s)
    {
        RoundEndEffects(s);
        CheckOutcome(s);
        if (s.Outcome != Outcome.Ongoing) return;
        if (s.Round >= s.Data.Rules.RoundCap)
        {
            s.Outcome = Outcome.Draw;
            s.Emit(EventType.BattleEnd, detail: "draw");
            return;
        }
        s.Round++;
        s.Emit(EventType.RoundStart, amount: s.Round);
        BeginPhase(s, Phase.Party);
    }

    /// <summary>
    /// The party loses if the hero falls. The party wins once no enemy is left in the fight
    /// (dead or fled).
    /// </summary>
    public static void CheckOutcome(BattleState s)
    {
        if (s.Outcome != Outcome.Ongoing) return;
        if (!s.Units[s.Info.HeroIndex].Alive)
        {
            s.Outcome = Outcome.PartyLost;
            s.Emit(EventType.BattleEnd, detail: "party_lost");
            return;
        }
        for (int i = 0; i < s.Units.Length; i++)
            if (s.Units[i].Side == Side.Enemy && s.Units[i].Alive) return;
        s.Outcome = Outcome.PartyWon;
        s.Emit(EventType.BattleEnd, detail: "party_won");
    }

    // ------------------------------------------------------------------
    // Timers: every status counts phase ends, two per round
    // ------------------------------------------------------------------

    private static void TickTimers(BattleState s)
    {
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var u = ref s.Units[i];
            if (!u.Alive) continue;
            if (Tick(ref u.Rooted)) { s.Emit(EventType.StatusEnd, i, i, detail: "rooted"); OnRootEnded(s, i); }
            if (Tick(ref u.Slowed)) s.Emit(EventType.StatusEnd, i, i, detail: "slowed");
            if (Tick(ref u.Blinded)) s.Emit(EventType.StatusEnd, i, i, detail: "blinded");
            if (Tick(ref u.Braced)) { u.BraceDefence = 0; s.Emit(EventType.StatusEnd, i, i, detail: "braced"); }
            if (Tick(ref u.Taunted)) { u.TauntBy = -1; s.Emit(EventType.StatusEnd, i, i, detail: "taunted"); }
            if (Tick(ref u.Buffed)) { u.BuffAmount = 0; s.Emit(EventType.StatusEnd, i, i, detail: "buffed"); }
            if (Tick(ref u.WeaponBindTimer))
            {
                u.Set(UnitFlags.WeaponBind | UnitFlags.WeaponBindWeak | UnitFlags.WeaponBindUpgraded, false);
                s.Emit(EventType.StatusEnd, i, i, detail: "weapon_bind");
            }
        }
        TickBoardTimers(s);
    }

    /// <summary>Counts a timer down by one; true when it has just run out.</summary>
    private static bool Tick(ref int timer)
    {
        if (timer <= 0) return false;
        timer--;
        return timer == 0;
    }

    /// <summary>A status lasting this many rounds, in phase ends.</summary>
    public static int Rounds(int rounds) => rounds * 2;
}

namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // The flow of a battle
    //
    // A round is: the party phase, the creatures step (living features move), then the enemy
    // phase. Each unit's turn starts automatically; it then takes inputs until it chooses End.
    //   - Party phase: the hero's and the Loper's turns both start at once and run side by side.
    //     The hero has the controls first; the player can hand over to the Loper and back as often
    //     as they like (SwapPartner) until one of them ends its turn. The phase ends when both have.
    //   - Enemy phase: strongest first. A beast acts straight after its handler, as one joint turn.
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
        s.Emit(EventType.PhaseStart, detail: PhaseName(phase));
        switch (phase)
        {
            case Phase.Creatures:
                s.OrderCount = 0;
                s.OrderPos = 0;
                CreatureStep(s);
                EndPhase(s);
                return;
            case Phase.Party:
                BeginPartyPhase(s);
                return;
            default:
                BuildEnemyOrder(s);
                s.OrderPos = 0;
                AdvanceToNextActor(s);
                return;
        }
    }

    /// <summary>
    /// The party phase: the hero's and the Loper's turns both start now, so whatever happens at the
    /// start of a turn happens to both before either acts. The hero has the controls first.
    /// </summary>
    private static void BeginPartyPhase(BattleState s)
    {
        s.OrderCount = 0;
        s.Order[s.OrderCount++] = s.Info.HeroIndex;
        if (s.Info.LoperIndex >= 0) s.Order[s.OrderCount++] = s.Info.LoperIndex;
        for (int i = 0; i < s.OrderCount; i++)
        {
            int unit = s.Order[i];
            if (!CanTakeTurn(s, unit)) continue;
            s.OrderPos = i;
            StartTurn(s, unit);
            if (s.Outcome != Outcome.Ongoing) return;
        }
        NextPartyActor(s);
    }

    /// <summary>In the party phase: has this unit's turn started and not yet ended?</summary>
    public static bool PartyTurnOpen(BattleState s, int unit) =>
        s.Phase == Phase.Party && s.Units[unit].Side == Side.Party && CanTakeTurn(s, unit) && !s.Units[unit].Has(UnitFlags.TurnDone);

    /// <summary>Gives the controls to the first of the pair whose turn is still going, or ends the phase.</summary>
    private static void NextPartyActor(BattleState s)
    {
        for (int i = 0; i < s.OrderCount; i++)
            if (PartyTurnOpen(s, s.Order[i]))
            {
                s.OrderPos = i;
                return;
            }
        s.OrderPos = s.OrderCount;
        EndPhase(s);
    }

    /// <summary>
    /// The enemies' order: strongest first (by power), ties broken by unit number. A beast with a
    /// handler acts straight after it, as one joint turn, and the pair takes the place of the
    /// stronger of the two. A wild beast (its handler gone) takes its own place.
    /// </summary>
    private static void BuildEnemyOrder(BattleState s)
    {
        s.OrderCount = 0;
        for (int i = 0; i < s.Units.Length; i++)
            if (s.Units[i].Side == Side.Enemy && s.Units[i].Alive && !Led(s, i)) s.Order[s.OrderCount++] = i;
        // Insertion sort: strongest first, then by unit number. Small lists, fully deterministic.
        for (int i = 1; i < s.OrderCount; i++)
        {
            int unit = s.Order[i], j = i - 1;
            while (j >= 0 && Before(s, unit, s.Order[j])) { s.Order[j + 1] = s.Order[j]; j--; }
            s.Order[j + 1] = unit;
        }
        // Each handler's beasts go straight after it, by unit number.
        for (int i = 0; i < s.OrderCount; i++)
        {
            int handler = s.Order[i];
            for (int b = 0; b < s.Units.Length; b++)
            {
                if (!Led(s, b) || s.Units[b].Handler != handler) continue;
                Array.Copy(s.Order, i + 1, s.Order, i + 2, s.OrderCount - i - 1);
                s.Order[++i] = b;
                s.OrderCount++;
            }
        }

        static bool Before(BattleState s, int a, int b)
        {
            int pa = PairPower(s, a), pb = PairPower(s, b);
            return pa != pb ? pa > pb : a < b;
        }
    }

    /// <summary>A living beast whose handler is still in the fight: it acts in its handler's turn.</summary>
    private static bool Led(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        return u.Alive && u.Handler >= 0 && s.Units[u.Handler].Alive && s.Units[u.Handler].Side == u.Side;
    }

    /// <summary>A unit's power for the turn order: a handler's is the stronger of its own and its beasts'.</summary>
    private static int PairPower(BattleState s, int unit)
    {
        int power = s.Units[unit].Power;
        for (int b = 0; b < s.Units.Length; b++)
            if (Led(s, b) && s.Units[b].Handler == unit) power = Math.Max(power, s.Units[b].Power);
        return power;
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
    ///   - Slowed: half movement.
    ///   - Knocked over: getting up drains movement (once).
    ///   - Dazed: no bonus action this turn (once).
    ///   - A readied Parry lasts until the start of the parrying unit's next turn.
    /// </summary>
    private static void StartTurn(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        u.Set(UnitFlags.Moved | UnitFlags.MainUsed | UnitFlags.BonusUsed | UnitFlags.TurnDone, false);
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

    /// <summary>
    /// Movement for the turn about to start, in half-points. A slowed unit (or a mounted pair with
    /// either of them slowed) gets half, rounded down to a half-tile.
    /// </summary>
    public static int TurnMovement(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        if (Anchored(s, unit)) return 0;
        int move = MountedMovement(s, unit);
        int partner = u.MountedOn >= 0 ? u.MountedOn : u.Rider;
        if (u.Slowed > 0 || (partner >= 0 && s.Units[partner].Slowed > 0)) move = SlowedMovement(move);
        return move;
    }

    /// <summary>What slowed leaves of a turn's movement (half-points): half of it.</summary>
    public static int SlowedMovement(int movement) => movement / 2;

    /// <summary>
    /// Ends the current unit's turn. In the party phase the partner gets the controls if its turn
    /// is still going; otherwise the next unit in the order starts its turn.
    /// </summary>
    private static void EndTurn(BattleState s)
    {
        int unit = CurrentActor(s);
        OnTurnEnd(s, unit);
        s.Units[unit].Set(UnitFlags.TurnDone);
        ulong checksum = s.Sink.Enabled ? s.Checksum() : 0;
        s.Emit(EventType.TurnEnd, unit, unit, checksum: checksum);
        CheckOutcome(s);
        if (s.Outcome != Outcome.Ongoing) return;
        if (s.Phase == Phase.Party)
        {
            NextPartyActor(s);
            return;
        }
        s.OrderPos++;
        AdvanceToNextActor(s);
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

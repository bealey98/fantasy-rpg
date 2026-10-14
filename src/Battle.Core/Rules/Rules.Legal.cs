namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // Legal actions: every input the acting unit could give right now.
    // The interface highlights them, and AIs choose from them, so nobody can do anything illegal.
    // Applying an action checks it against the same rules, so the two can never disagree.
    // ------------------------------------------------------------------

    [ThreadStatic] private static List<Pos>? _tileBuffer;
    [ThreadStatic] private static List<(Target, Target)>? _targetBuffer;
    [ThreadStatic] private static List<(Target, Target)>? _checkBuffer;

    /// <summary>Fills <paramref name="output"/> with every legal action for the unit whose turn it is.</summary>
    public static void LegalActions(BattleState s, List<BattleAction> output)
    {
        output.Clear();
        int actor = CurrentActor(s);
        if (actor < 0) return;

        // A beast makes no choices of its own: its one legal action is what its orders say.
        if (ActsByRule(s, actor))
        {
            output.Add(BeastAction(s, actor));
            return;
        }

        if (CanMove(s, actor))
        {
            var tiles = _tileBuffer ??= new List<Pos>();
            ReachableTiles(s, actor, tiles);
            foreach (var p in tiles) output.Add(BattleAction.MoveTo(actor, p));
        }

        var targets = _targetBuffer ??= new List<(Target, Target)>();
        foreach (int m in s.Info.Kits[actor])
        {
            var move = s.Data.Moves[m];
            if (!MoveAvailable(s, actor, move)) continue;
            Targets(s, actor, move, targets);
            foreach (var (first, second) in targets) output.Add(BattleAction.Use(actor, move, first, second));
        }

        AddPartyActions(s, actor, output);
        if (MayEndTurn(s, actor)) output.Add(BattleAction.End(actor));
    }

    /// <summary>Every turn can end, except an underground unit's: it has to burst out first.</summary>
    public static bool MayEndTurn(BattleState s, int actor) =>
        !s.Units[actor].Has(UnitFlags.Burrowed) || s.Units[actor].Has(UnitFlags.MainUsed);

    /// <summary>
    /// Is the move usable this turn at all, before thinking about targets? Its action must still be
    /// free, the user must be on the board, and some moves only work dismounted.
    /// </summary>
    public static bool MoveAvailable(BattleState s, int actor, MoveDef move)
    {
        ref var u = ref s.Units[actor];
        if (u.Has(UnitFlags.Burrowed)) return move.Kind == MoveKind.Burrow && !u.Has(UnitFlags.MainUsed);
        if (!u.OnBoard) return false;
        if (move.UsesMain && u.Has(UnitFlags.MainUsed)) return false;
        if (move.UsesBonus && u.Has(UnitFlags.BonusUsed)) return false;
        if (move.Mount == MountRule.Dismounted && (u.MountedOn >= 0 || u.Rider >= 0)) return false;
        return ExtraAvailability(s, actor, move);
    }

    /// <summary>Every valid target (and second target, for Tether) for a move.</summary>
    public static void Targets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        output.Clear();
        switch (move.Kind)
        {
            case MoveKind.Melee: MeleeTargets(s, actor, output); break;
            default: KindTargets(s, actor, move, output); break;
        }
        ApplyTaunt(s, actor, move, output);
    }

    /// <summary>The party's extra choices: handing over to the partner, fleeing, and mounting or dismounting.</summary>
    private static void AddPartyActions(BattleState s, int actor, List<BattleAction> output)
    {
        int partner = SwapTarget(s, actor);
        if (partner >= 0)
            output.Add(new BattleAction(ActionKind.SwapPartner, actor, -1, Target.OfUnit(partner), Target.None, Timing.None));
        if (CanFlee(s, actor))
            output.Add(new BattleAction(ActionKind.Flee, actor, -1, Target.None, Target.None, Timing.None));
        AddMountActions(s, actor, output);
    }

    /// <summary>
    /// The partner the acting unit can hand over to: the other of the hero and the Loper, if its turn
    /// is still going. -1 outside the party phase, or once the partner has ended its turn (or can't
    /// take one).
    /// </summary>
    public static int SwapTarget(BattleState s, int actor)
    {
        if (s.Phase != Phase.Party || s.Units[actor].Side != Side.Party) return -1;
        for (int i = 0; i < s.OrderCount; i++)
        {
            int other = s.Order[i];
            if (other != actor && PartyTurnOpen(s, other)) return other;
        }
        return -1;
    }

    /// <summary>
    /// The party can flee when everyone in it who's on the board stands on the arena's edge and no
    /// enemy is within range of any of them, counted in straight steps. A burrowed enemy counts
    /// where it will surface. A knocked-out Loper doesn't hold the party back.
    /// </summary>
    public static bool CanFlee(BattleState s, int actor)
    {
        if (s.Phase != Phase.Party || s.Units[actor].Side != Side.Party) return false;
        int range = s.Data.Rules.PartyFleeRange;
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var p = ref s.Units[i];
            if (p.Side != Side.Party || !p.Alive || !p.OnBoard) continue;
            if (!s.OnEdge(p.Pos)) return false;
            for (int e = 0; e < s.Units.Length; e++)
            {
                ref var en = ref s.Units[e];
                if (!en.Alive || !AreEnemies(s, i, e)) continue;
                var at = en.OnBoard ? en.Pos : en.Has(UnitFlags.Burrowed) ? en.BurrowTo : Pos.None;
                if (at != Pos.None && at.Manhattan(p.Pos) <= range) return false;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------
    // Applying an input
    // ------------------------------------------------------------------

    /// <summary>
    /// Applies one input from the unit whose turn it is. Throws IllegalActionException (and changes
    /// nothing) if the input isn't legal right now.
    /// </summary>
    public static void Apply(BattleState s, BattleAction a)
    {
        int actor = CurrentActor(s);
        if (actor < 0) throw new IllegalActionException("the battle isn't running");
        if (a.Actor != actor) throw new IllegalActionException($"it's {s.Label(actor)}'s turn, not {s.Label(a.Actor)}'s");
        if (ActsByRule(s, actor) && !a.SameChoice(BeastAction(s, actor)))
            throw new IllegalActionException($"{s.Label(actor)} has no choices of its own: it follows its orders");

        switch (a.Kind)
        {
            case ActionKind.End:
                if (!MayEndTurn(s, actor)) throw new IllegalActionException($"{s.Label(actor)} must burst out of the ground first");
                EndTurn(s);
                return;
            case ActionKind.Move:
                if (a.Target.Kind != TargetKind.Tile || !MoveTo(s, actor, a.Target.Tile))
                    throw new IllegalActionException($"{s.Label(actor)} can't move to {a.Target.Tile}");
                break;
            case ActionKind.SwapPartner:
                SwapPartner(s, actor, a.Target);
                return;
            case ActionKind.Flee:
                Flee(s, actor);
                return;
            case ActionKind.Mount:
            case ActionKind.Dismount:
                ApplyMount(s, a);
                break;
            default:
                UseMove(s, a);
                break;
        }
        AfterAction(s, actor);
    }

    private static void AfterAction(BattleState s, int actor)
    {
        CheckOutcome(s);
        if (s.Outcome != Outcome.Ongoing) return;
        // A unit killed (or sent fleeing) during its own action loses the rest of its turn.
        if (!s.Units[actor].Alive && CurrentActor(s) == actor) EndTurn(s);
    }

    private static void UseMove(BattleState s, in BattleAction a)
    {
        if (a.Move < 0 || a.Move >= s.Data.Moves.Length) throw new IllegalActionException("unknown move");
        var move = s.Data.Moves[a.Move];
        if (Array.IndexOf(s.Info.Kits[a.Actor], a.Move) < 0)
            throw new IllegalActionException($"{s.Label(a.Actor)} doesn't know {move.Name}");
        var expected = move.Slot switch { MoveSlot.Main => ActionKind.Main, MoveSlot.Bonus => ActionKind.Bonus, _ => ActionKind.Special };
        if (a.Kind != expected) throw new IllegalActionException($"{move.Name} is a {expected.ToString().ToLowerInvariant()} move");
        if (!MoveAvailable(s, a.Actor, move)) throw new IllegalActionException($"{s.Label(a.Actor)} can't use {move.Name} right now");

        var check = _checkBuffer ??= new List<(Target, Target)>();
        Targets(s, a.Actor, move, check);
        if (!check.Contains((a.Target, a.Target2)))
            throw new IllegalActionException($"{s.Label(a.Actor)} can't use {move.Name} on that target");

        ref var u = ref s.Units[a.Actor];
        if (move.UsesMain) u.Set(UnitFlags.MainUsed);
        if (move.UsesBonus) u.Set(UnitFlags.BonusUsed);
        Execute(s, a, move);
    }

    private static void Execute(BattleState s, in BattleAction a, MoveDef move)
    {
        switch (move.Kind)
        {
            case MoveKind.Melee:
                Attack(s, a.Actor, a.Target.Unit, move, a.Timing, AttackKind.Melee);
                break;
            default:
                ExecuteKind(s, a, move);
                break;
        }
        // Some moves cost their user too: the Cutter's Headbutt dazes the Cutter.
        if (move.SelfStatus != StatusId.None && s.Units[a.Actor].Alive)
            ApplyStatus(s, a.Actor, a.Actor, move.SelfStatus, move.SelfStatusRounds, move.Index);
    }

    /// <summary>Hands the controls to the partner, mid-turn. Nothing else changes: both turns carry on where they were.</summary>
    private static void SwapPartner(BattleState s, int actor, Target target)
    {
        int partner = SwapTarget(s, actor);
        if (partner < 0 || target != Target.OfUnit(partner))
            throw new IllegalActionException("there's no partner with its turn still going to hand over to");
        s.OrderPos = Array.IndexOf(s.Order, partner, 0, s.OrderCount);
        s.Emit(EventType.SwapPartner, actor, partner);
    }

    /// <summary>The party leaves the battle, which ends it.</summary>
    private static void Flee(BattleState s, int actor)
    {
        if (!CanFlee(s, actor))
            throw new IllegalActionException($"the party can only flee with everyone on the arena's edge and no enemy within {s.Data.Rules.PartyFleeRange} tiles");
        s.Outcome = Outcome.PartyFled;
        s.Emit(EventType.BattleEnd, actor, detail: "party_fled");
    }
}

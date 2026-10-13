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
        output.Add(BattleAction.End(actor));
    }

    /// <summary>
    /// Is the move usable this turn at all, before thinking about targets? Its action must still be
    /// free, the user must be on the board, and some moves only work dismounted.
    /// </summary>
    public static bool MoveAvailable(BattleState s, int actor, MoveDef move)
    {
        ref var u = ref s.Units[actor];
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

    /// <summary>The party's extra choices: swapping who acts first, and mounting or dismounting.</summary>
    private static void AddPartyActions(BattleState s, int actor, List<BattleAction> output)
    {
        if (CanSwapOrder(s, actor))
            output.Add(new BattleAction(ActionKind.SwapOrder, actor, -1, Target.None, Target.None, Timing.None));
        AddMountActions(s, actor, output);
    }

    /// <summary>
    /// The hero and the Loper can swap who acts first, but only at the very start of the party
    /// phase, before either has done anything, and only once per phase.
    /// </summary>
    public static bool CanSwapOrder(BattleState s, int actor)
    {
        int loper = s.Info.LoperIndex;
        return s.Phase == Phase.Party && !s.PhaseActed && s.OrderPos == 0 && loper >= 0
            && s.Units[loper].Alive && s.Units[actor].Side == Side.Party;
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

        switch (a.Kind)
        {
            case ActionKind.End:
                EndTurn(s);
                return;
            case ActionKind.Move:
                if (a.Target.Kind != TargetKind.Tile || !MoveTo(s, actor, a.Target.Tile))
                    throw new IllegalActionException($"{s.Label(actor)} can't move to {a.Target.Tile}");
                break;
            case ActionKind.SwapOrder:
                SwapOrder(s, actor);
                return;
            case ActionKind.Mount:
            case ActionKind.Dismount:
                ApplyMount(s, a);
                break;
            default:
                UseMove(s, a);
                break;
        }
        s.PhaseActed = true;
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
    }

    private static void SwapOrder(BattleState s, int actor)
    {
        if (!CanSwapOrder(s, actor)) throw new IllegalActionException("the party order can only be swapped before anyone acts");
        // Undo the start of this unit's turn exactly, then start the other unit's turn instead.
        s.Units[actor] = s.TurnStartBackup;
        s.LoperFirst = !s.LoperFirst;
        s.PhaseActed = true;
        s.Emit(EventType.SwapOrder, actor, detail: s.LoperFirst ? "loper_first" : "hero_first");
        BuildOrder(s, Side.Party);
        s.OrderPos = 0;
        AdvanceToNextActor(s);
    }
}

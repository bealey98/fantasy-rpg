using Battle.Core.Ai;

namespace Battle.Core;

/// <summary>
/// Plays a battle from start to finish: asks the right controller for each input, fills in timing
/// grades, applies the input, and records it. The recorded inputs plus the seed replay the battle
/// exactly.
/// </summary>
public sealed class BattleRunner
{
    private readonly IController _party;
    private readonly IController _enemies;
    private readonly ITimingSource _timing;
    private readonly List<BattleAction> _legal = new();

    public BattleState State { get; }

    /// <summary>Every input applied so far, with the round it was made in.</summary>
    public List<(int Round, BattleAction Action)> Inputs { get; } = new();

    /// <summary>Called at the start of every round (after round 1's first turn starts), e.g. to write a snapshot.</summary>
    public Action<BattleState>? OnRoundStart { get; set; }

    /// <summary>A safety net: no battle should need this many inputs.</summary>
    public int MaxDecisions { get; set; } = 50_000;

    public int Decisions { get; private set; }

    public BattleRunner(BattleState state, IController party, IController enemies, ITimingSource timing)
    {
        State = state;
        _party = party;
        _enemies = enemies;
        _timing = timing;
    }

    public Outcome Run()
    {
        if (State.Round == 0)
        {
            Rules.Start(State);
            if (State.Outcome == Outcome.Ongoing) OnRoundStart?.Invoke(State);
        }
        while (State.Outcome == Outcome.Ongoing) Step();
        return State.Outcome;
    }

    /// <summary>One input from whoever's turn it is.</summary>
    public void Step()
    {
        int actor = Rules.CurrentActor(State);
        if (actor < 0) return;
        Rules.LegalActions(State, _legal);
        var controller = State.Units[actor].Side == Side.Party ? _party : _enemies;
        var action = controller.Choose(State, actor, _legal);
        Apply(action);
    }

    /// <summary>Applies a chosen input, filling in a timing grade when one is needed.</summary>
    public void Apply(BattleAction action)
    {
        var need = Rules.TimingFor(State, action);
        if (need != TimingNeed.None && action.Timing == Timing.None)
            action = action.WithTiming(_timing.Grade(State, action, need));
        int round = State.Round;
        Rules.Apply(State, action);
        Inputs.Add((round, action));
        if (Invariants.Enabled) Invariants.Check(State);
        if (++Decisions > MaxDecisions) throw new InvalidOperationException($"battle passed {MaxDecisions} inputs without ending");
        if (State.Round != round && State.Outcome == Outcome.Ongoing) OnRoundStart?.Invoke(State);
    }

    /// <summary>
    /// Replays recorded inputs on a fresh battle built from the same seed and content. Each input
    /// carries its own timing grade, so no controllers or timing models are involved.
    /// </summary>
    public static void Replay(BattleState fresh, IEnumerable<BattleAction> inputs, Action<BattleState>? onRoundStart = null)
    {
        Rules.Start(fresh);
        if (fresh.Outcome == Outcome.Ongoing) onRoundStart?.Invoke(fresh);
        foreach (var action in inputs)
        {
            int round = fresh.Round;
            Rules.Apply(fresh, action);
            if (Invariants.Enabled) Invariants.Check(fresh);
            if (fresh.Round != round && fresh.Outcome == Outcome.Ongoing) onRoundStart?.Invoke(fresh);
        }
    }
}

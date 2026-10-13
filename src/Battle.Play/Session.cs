using Battle.Content;
using Battle.Content.Logs;
using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Play;

/// <summary>
/// One battle being played: the state, its logs and every input so far. Undo replays the inputs
/// minus the last one onto a fresh battle, which is exact because battles are deterministic.
/// </summary>
public sealed class Session
{
    private readonly string _contentDir;
    private readonly GameData _data;
    private readonly string _encounter;
    private readonly ulong _seed;
    private readonly ITimingSource _timing;

    public BattleState State { get; private set; }
    public BattleLog Log { get; private set; }
    public List<(int Round, BattleAction Action)> Inputs { get; } = new();

    /// <summary>How many events have been shown to the player so far.</summary>
    public int EventsShown { get; set; }

    public Session(string contentDir, GameData data, string encounter, ulong seed, ITimingSource timing)
    {
        _contentDir = contentDir;
        _data = data;
        _encounter = encounter;
        _seed = seed;
        _timing = timing;
        (State, Log) = Fresh();
        Rules.Start(State);
        Log.Snapshot(State);
    }

    private (BattleState, BattleLog) Fresh()
    {
        var state = EncounterLoader.CreateBattle(_contentDir, _data, _encounter, _seed, battleId: (int)(_seed % int.MaxValue));
        return (state, new BattleLog(state));
    }

    /// <summary>Applies an input, filling in a timing grade if it needs one.</summary>
    public void Apply(BattleAction action)
    {
        var need = Rules.TimingFor(State, action);
        if (need != TimingNeed.None && action.Timing == Timing.None)
            action = action.WithTiming(_timing.Grade(State, action, need));
        int round = State.Round;
        Rules.Apply(State, action);
        Inputs.Add((round, action));
        if (Invariants.Enabled) Invariants.Check(State);
        if (State.Round != round && State.Outcome == Outcome.Ongoing) Log.Snapshot(State);
    }

    /// <summary>Can the last input be undone? Only within the party's current turn.</summary>
    public bool CanUndo()
    {
        if (Inputs.Count == 0) return false;
        var last = Inputs[^1];
        int actor = Rules.CurrentActor(State);
        return actor >= 0 && last.Action.Actor == actor && last.Action.Kind != ActionKind.End
            && State.Units[actor].Side == Side.Party && last.Round == State.Round;
    }

    /// <summary>Takes back the last input by replaying everything before it.</summary>
    public void Undo()
    {
        var keep = Inputs.Take(Inputs.Count - 1).ToList();
        (State, Log) = Fresh();
        Inputs.Clear();
        Rules.Start(State);
        Log.Snapshot(State);
        foreach (var (round, action) in keep)
        {
            int before = State.Round;
            Rules.Apply(State, action);
            Inputs.Add((round, action));
            if (State.Round != before && State.Outcome == Outcome.Ongoing) Log.Snapshot(State);
        }
        EventsShown = Log.Events.Count;
    }

    public string Save(string partyController, string enemyController, string timing) =>
        Log.Save("logs", $"{_encounter}-{_seed}", partyController, enemyController, Inputs, timing);
}

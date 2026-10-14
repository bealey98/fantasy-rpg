using Battle.Content;
using Battle.Content.Logs;
using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Play;

/// <summary>One battle being played: the state, its logs and every input so far.</summary>
public sealed class Session
{
    private readonly string _encounter;
    private readonly ulong _seed;
    private readonly ITimingSource _timing;

    public BattleState State { get; }
    public BattleLog Log { get; }
    public List<(int Round, BattleAction Action)> Inputs { get; } = new();

    /// <summary>How many events have been shown to the player so far.</summary>
    public int EventsShown { get; set; }

    public Session(string contentDir, GameData data, string encounter, ulong seed, ITimingSource timing)
    {
        _encounter = encounter;
        _seed = seed;
        _timing = timing;
        State = EncounterLoader.CreateBattle(contentDir, data, encounter, seed, battleId: (int)(seed % int.MaxValue));
        Log = new BattleLog(State);
        Rules.Start(State);
        Log.Snapshot(State);
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

    public string Save(string partyController, string enemyController, string timing) =>
        Log.Save("logs", $"{_encounter}-{_seed}", partyController, enemyController, Inputs, timing);
}

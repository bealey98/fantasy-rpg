using Battle.Core;

namespace Battle.Content.Logs;

/// <summary>
/// Records one battle's three logs while it runs, then writes them next to each other:
///   name.inputs.jsonl     the seed and every input: replays the battle exactly
///   name.events.csv       every event: for the viewer and R
///   name.snapshots.jsonl  the full state at the start of each round: for jumping around
/// </summary>
public sealed class BattleLog
{
    private readonly List<string> _snapshots = new();
    private readonly EventList _events = new();

    public BattleState State { get; }

    public BattleLog(BattleState state)
    {
        State = state;
        state.Sink = _events;
    }

    public IReadOnlyList<BattleEvent> Events => _events.Events;

    /// <summary>Call at the start of every round (BattleRunner.OnRoundStart does this).</summary>
    public void Snapshot(BattleState s) => _snapshots.Add(Snapshots.Line(s));

    public void Attach(BattleRunner runner) => runner.OnRoundStart = Snapshot;

    /// <summary>Writes the three files and returns their shared base path.</summary>
    public string Save(string directory, string name, string partyController, string enemyController,
        IEnumerable<(int Round, BattleAction Action)> inputs, string timing = "")
    {
        Directory.CreateDirectory(directory);
        string basePath = Path.Combine(directory, name);
        using (var w = new StreamWriter(basePath + ".inputs.jsonl"))
            InputLog.Write(w, State, partyController, enemyController, inputs, timing);
        using (var w = new StreamWriter(basePath + ".events.csv"))
        {
            w.NewLine = "\n";
            EventLog.WriteHeader(w);
            EventLog.Write(w, State, _events.Events);
        }
        File.WriteAllLines(basePath + ".snapshots.jsonl", _snapshots);
        return basePath;
    }
}

using Battle.Content;
using Battle.Content.Logs;
using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Tests;

/// <summary>
/// Golden replays: stored input logs whose event logs must not change unless a rule change is
/// intended. When one does change, the failure shows the first event that moved.
///
/// After an intended rule or content change, regenerate them (re-running each battle with the
/// controllers and timing in its header) and review the diff in git:
///   GOLDEN_UPDATE=1 dotnet run --project tests/Battle.Tests -c Release -- Golden
/// </summary>
public class GoldenTests
{
    /// <summary>
    /// The battles kept as goldens: encounter, seed and the party's controller, against the goblins'
    /// scripts. The random party is there to pin what the party bot never does: handing over
    /// mid-turn, mounting, getting thrown and fleeing.
    /// </summary>
    private static readonly (string Encounter, ulong Seed, string Party)[] Specs =
    [
        ("river-crossing", 1, "scripted"),
        ("first-goblins", 1, "scripted"),
        ("first-goblins", 2, "scripted"),
        ("goblin-camp", 1, "scripted"),
        ("goblin-camp", 2, "scripted"),
        ("goblin-camp-tier2", 1, "scripted"),
        ("goblin-camp", 23, "random"),
    ];

    private static string Dir => Path.Combine(TestContent.RepoRoot, "tests", "golden");

    [Test]
    public void GoldenReplaysMatch()
    {
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE") == "1")
        {
            Regenerate();
            return;
        }

        var files = Directory.GetFiles(Dir, "*.inputs.jsonl").OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.True(files.Count >= Specs.Length, $"expected {Specs.Length} goldens in {Dir}; run with GOLDEN_UPDATE=1 to create them");
        foreach (string inputs in files)
        {
            var (header, lines) = InputLog.Read(inputs);
            var state = EncounterLoader.CreateBattle(TestContent.Dir, TestContent.Data, header.Encounter, header.Seed, header.BattleId);
            var events = new EventList();
            state.Sink = events;
            BattleRunner.Replay(state, InputLog.ToActions(state, lines));

            var expected = File.ReadAllLines(inputs.Replace(".inputs.jsonl", ".events.csv")).Skip(1).ToList();
            var actual = events.Events.Select(e => EventLog.Row(state, e)).ToList();
            string name = Path.GetFileName(inputs);
            for (int i = 0; i < Math.Min(expected.Count, actual.Count); i++)
                if (expected[i] != actual[i])
                    throw new AssertionException($"{name}: event {i + 1} changed\n      was: {expected[i]}\n      now: {actual[i]}\n" +
                        "      if the change is intended, regenerate with GOLDEN_UPDATE=1");
            Assert.Equal(expected.Count, actual.Count, $"{name}: number of events");
        }
    }

    private static void Regenerate()
    {
        Directory.CreateDirectory(Dir);
        foreach (string old in Directory.GetFiles(Dir)) File.Delete(old);
        foreach (var (encounter, seed, party) in Specs)
        {
            var state = EncounterLoader.CreateBattle(TestContent.Dir, TestContent.Data, encounter, seed, battleId: 1);
            var log = new BattleLog(state);
            IController partyController = party == "random" ? new RandomController(seed) : Scripts.For(Side.Party, seed);
            var runner = new BattleRunner(state, partyController, Scripts.For(Side.Enemy, seed),
                new TimingModel(TestContent.Data, "average", seed));
            log.Attach(runner);
            runner.Run();
            string name = party == "scripted" ? $"{encounter}-{seed}" : $"{encounter}-{party}-{seed}";
            string basePath = log.Save(Dir, name, party, "scripted", runner.Inputs, "average");
            File.Delete(basePath + ".snapshots.jsonl"); // goldens only need inputs and events
            Console.WriteLine($"  golden regenerated: {Path.GetFileName(basePath)}");
        }
    }
}

using System.Text.Json;
using Battle.Content;
using Battle.Content.Logs;
using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Tests;

public class LogTests
{
    private static (BattleState State, BattleLog Log, BattleRunner Runner) Play(string encounter, ulong seed)
    {
        var state = EncounterLoader.CreateBattle(TestContent.Dir, TestContent.Data, encounter, seed, battleId: 1);
        var log = new BattleLog(state);
        var runner = new BattleRunner(state, Scripts.For(Side.Party, seed), Scripts.For(Side.Enemy, seed), new TimingModel(TestContent.Data, "average", seed));
        log.Attach(runner);
        runner.Run();
        return (state, log, runner);
    }

    [Test]
    public void InputLogsReplayExactly()
    {
        foreach (string encounter in EncounterLoader.All(TestContent.Dir))
            for (ulong seed = 1; seed <= 5; seed++)
            {
                var (state, log, runner) = Play(encounter, seed);
                string dir = Path.Combine(Path.GetTempPath(), "battle-tests");
                string basePath = log.Save(dir, $"{encounter}-{seed}", "scripted", "scripted", runner.Inputs, "average");

                var (header, lines) = InputLog.Read(basePath + ".inputs.jsonl");
                Assert.Equal(seed, header.Seed, "seed in the header");
                var fresh = EncounterLoader.CreateBattle(TestContent.Dir, TestContent.Data, header.Encounter, header.Seed, header.BattleId);
                fresh.Sink = new EventList();
                BattleRunner.Replay(fresh, InputLog.ToActions(fresh, lines));
                Assert.Equal(state.Checksum(), fresh.Checksum(), $"{encounter} seed {seed}: final checksum after replay");
                Assert.Equal(log.Events.Count, ((EventList)fresh.Sink).Events.Count, "same number of events");
            }
    }

    [Test]
    public void EventLogRowsMatchTheHeader()
    {
        var (state, log, _) = Play("river-crossing", 2);
        int columns = EventLog.Header.Split(',').Length;
        foreach (var e in log.Events)
        {
            string row = EventLog.Row(state, e);
            int fields = SplitCsv(row).Count;
            Assert.Equal(columns, fields, $"columns in: {row}");
        }
        Assert.True(log.Events.Any(e => e.Type == EventType.TurnEnd && e.Checksum != 0), "turn ends carry checksums");
    }

    [Test]
    public void SnapshotsAreValidJsonWithTheWholeBoard()
    {
        var (state, _, _) = Play("river-crossing", 3);
        string line = Snapshots.Line(state);
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.Equal(state.Height, root.GetProperty("heights").GetArrayLength(), "height rows");
        Assert.Equal(state.Units.Length, root.GetProperty("units").GetArrayLength(), "units");
        Assert.True(root.TryGetProperty("danger", out _), "danger zone included");
    }

    [Test]
    public void SameSeedSameBattle()
    {
        var (a, logA, _) = Play("river-crossing", 9);
        var (b, logB, _) = Play("river-crossing", 9);
        var rowsA = logA.Events.Select(e => EventLog.Row(a, e)).ToList();
        var rowsB = logB.Events.Select(e => EventLog.Row(b, e)).ToList();
        Assert.Equal(rowsA.Count, rowsB.Count, "event count");
        for (int i = 0; i < rowsA.Count; i++) Assert.Equal(rowsA[i], rowsB[i], $"event {i}");
    }

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (char c in line)
        {
            if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }

    [Test]
    public void TimingRollsDependOnTheBattlesProgressNotOnHowOftenTheyreAsked()
    {
        var s = EncounterLoader.CreateBattle(TestContent.Dir, TestContent.Data, "first-goblins", 3, battleId: 1);
        Rules.Start(s);
        var timing = new TimingModel(TestContent.Data, "average", 3);
        var strike = BattleAction.Use(0, s.Data.Move("strike"), Target.OfUnit(1));
        var first = timing.Grade(s, strike, TimingNeed.Attack);
        for (int i = 0; i < 20; i++)
            Assert.Equal(first, timing.Grade(s, strike, TimingNeed.Attack), "the same moment of the battle gets the same grade, however often it's asked");

        var counts = new int[4];
        for (int seq = 0; seq < 3000; seq++)
        {
            s.Seq = seq;
            counts[(int)timing.Grade(s, strike, TimingNeed.Attack)]++;
        }
        Assert.True(counts[(int)Timing.Good] is > 1650 and < 1950, $"about 60% good at average skill, got {counts[(int)Timing.Good]} of 3000");
    }
}

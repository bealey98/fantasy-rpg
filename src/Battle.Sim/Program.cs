using System.Diagnostics;
using System.Globalization;
using Battle.Content;
using Battle.Content.Logs;
using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Sim;

// The command-line simulator. Run "battle-sim help" for the commands.
public static class Program
{
    public static int Main(string[] args)
    {
        // The board uses a few non-ASCII characters (HP bars, arrows); Windows consoles need telling.
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var a = new Args(args);
        try
        {
            return a.Command switch
            {
                "render" => Render(a),
                "run" => Run(a),
                "replay" => Replay(a),
                "fuzz" => Fuzz(a),
                "balance" => Balance(a),
                "bench" => Bench(a),
                "help" or "--help" or "-h" => Help(0),
                _ => Help(1, $"unknown command '{a.Command}'"),
            };
        }
        catch (ContentException e)
        {
            Console.Error.WriteLine("content error:");
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }

    private static int Help(int code, string? problem = null)
    {
        if (problem != null) Console.Error.WriteLine(problem);
        Console.WriteLine("""
            usage: battle-sim <command> [options]

              render <arena> [--color]          draw an arena from content/arenas
              run <encounter> [options]         play battles and write their logs
                  --battles N                   how many (default 1); battle i uses seed + i
                  --seed S                      first seed (default 1)
                  --party / --enemies NAME      controllers: scripted, simple or random (default scripted)
                  --skill LEVEL                 simulated timing: novice, average or expert (default average)
                  --logs all|summary|none       per-battle logs (default: all for up to 20 battles)
                  --out DIR                     where logs go (default logs)
                  --verbose                     print every event
              replay <file.inputs.jsonl>        replay an input log and check it matches its event log
              fuzz [--battles N] [--seed S]     battles on every encounter, checking every rule invariant after every input
                  --party / --enemies NAME      controllers (default random, which pokes the most rules)
              balance [encounter] [options]     win rates for every encounter (or one) at every timing skill
                  --battles N                   per encounter and skill (default 200)
                  --skills a,b                  which skills (default all in rules.json)
                  --party / --enemies NAME      controllers, as for run (default scripted)
                  --out DIR                     where balance.csv goes (default logs)
              bench [--seconds N] [--no-save]   inputs per second, random and scripted play; appends to bench/results.csv

            every command takes --content DIR (found automatically inside the repository)
            """);
        return code;
    }

    private static (string Dir, GameData Data) LoadContent(Args a)
    {
        string dir = a.Option("content") ?? ContentLoader.FindContentDir();
        return (dir, ContentLoader.Load(dir));
    }

    private static int Render(Args a)
    {
        var (dir, data) = LoadContent(a);
        string name = a.Positional(0) ?? throw new ContentException("render needs an arena name, e.g. battle-sim render river-crossing");
        var arena = ContentLoader.LoadArena(dir, name, data);
        Console.Write(AsciiRender.Arena(arena, data, a.Flag("color") || a.Flag("colour")));
        return 0;
    }

    private static int Run(Args a)
    {
        var (dir, data) = LoadContent(a);
        string encounter = a.Positional(0) ?? a.Option("encounter") ?? throw new ContentException("run needs an encounter, e.g. battle-sim run river-crossing");
        int battles = a.Int("battles", 1);
        ulong seed = (ulong)a.Int("seed", 1);
        string partyName = a.Option("party") ?? "scripted";
        string enemyName = a.Option("enemies") ?? "scripted";
        string skill = a.Option("skill") ?? "average";
        string outDir = a.Option("out") ?? "logs";
        string logs = a.Option("logs") ?? (battles <= 20 ? "all" : "summary");
        bool verbose = a.Flag("verbose");

        var summary = new List<string>
        {
            "battle_id,encounter,seed,party,enemies,skill,outcome,rounds,decisions,hero_hp,loper_hp,enemies_alive,enemies_fled",
        };
        var outcomes = new Dictionary<Outcome, int>();
        long totalRounds = 0;
        var watch = Stopwatch.StartNew();

        for (int i = 0; i < battles; i++)
        {
            ulong s = seed + (ulong)i;
            var state = EncounterLoader.CreateBattle(dir, data, encounter, s, battleId: i + 1);
            var log = logs == "all" ? new BattleLog(state) : null;
            if (log == null && verbose) state.Sink = new EventList();
            var runner = new BattleRunner(state,
                ControllerFactory.Create(partyName, Side.Party, s),
                ControllerFactory.Create(enemyName, Side.Enemy, s),
                new TimingModel(data, skill, s));
            log?.Attach(runner);
            var outcome = runner.Run();

            if (log != null)
            {
                string path = log.Save(outDir, $"{encounter}-{s}", partyName, enemyName, runner.Inputs, skill);
                if (battles <= 3) Console.WriteLine($"logs: {path}.events.csv (and .inputs.jsonl, .snapshots.jsonl)");
            }
            if (verbose)
                foreach (var e in ((EventList)state.Sink).Events)
                    if (Describe.Event(state, e) is string line) Console.WriteLine($"  {line}");

            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
            totalRounds += state.Round;
            int loper = state.Info.LoperIndex;
            int enemiesAlive = 0, enemiesFled = 0;
            for (int u = 0; u < state.Units.Length; u++)
            {
                if (state.Units[u].Side != Side.Enemy) continue;
                if (state.Units[u].Alive) enemiesAlive++;
                if (state.Units[u].Has(UnitFlags.Fled)) enemiesFled++;
            }
            summary.Add(string.Join(",", i + 1, encounter, s, partyName, enemyName, skill, OutcomeName(outcome), state.Round,
                runner.Decisions, state.Units[state.Info.HeroIndex].Hp, loper >= 0 ? state.Units[loper].Hp : 0, enemiesAlive, enemiesFled));
        }

        if (logs != "none")
        {
            Directory.CreateDirectory(outDir);
            string summaryPath = Path.Combine(outDir, $"{encounter}-summary.csv");
            File.WriteAllLines(summaryPath, summary);
            Console.WriteLine($"summary: {summaryPath}");
        }
        Console.WriteLine($"{battles} battle{(battles == 1 ? "" : "s")} of {encounter} in {watch.ElapsedMilliseconds} ms: " +
            $"won {outcomes.GetValueOrDefault(Outcome.PartyWon)}, lost {outcomes.GetValueOrDefault(Outcome.PartyLost)}, " +
            $"drawn {outcomes.GetValueOrDefault(Outcome.Draw)}; {(double)totalRounds / battles:0.0} rounds on average");
        return 0;
    }

    public static string OutcomeName(Outcome outcome) => outcome switch
    {
        Outcome.PartyWon => "won",
        Outcome.PartyLost => "lost",
        Outcome.Draw => "draw",
        _ => "ongoing",
    };

    private static int Replay(Args a)
    {
        var (dir, data) = LoadContent(a);
        string path = a.Positional(0) ?? throw new ContentException("replay needs an input log, e.g. battle-sim replay logs/river-crossing-1.inputs.jsonl");
        var (header, lines) = InputLog.Read(path);
        if (header.Rules != RulesVersion.Current)
            Console.WriteLine($"warning: logged with rules {header.Rules}, replaying with {RulesVersion.Current}");
        if (header.Content != data.ContentHash)
            Console.WriteLine($"warning: logged with content {header.Content}, replaying with {data.ContentHash}");

        var state = EncounterLoader.CreateBattle(dir, data, header.Encounter, header.Seed, header.BattleId);
        var events = new EventList();
        state.Sink = events;
        BattleRunner.Replay(state, InputLog.ToActions(state, lines));
        var actual = events.Events.Where(e => e.Type == EventType.TurnEnd).Select(e => e.Checksum.ToString("x16")).ToList();
        Console.WriteLine($"replayed {lines.Count} inputs: {OutcomeName(state.Outcome)} after {state.Round} rounds");

        string eventsPath = path.Replace(".inputs.jsonl", ".events.csv");
        if (!File.Exists(eventsPath))
        {
            Console.WriteLine("no event log alongside to compare with");
            return 0;
        }
        var expected = File.ReadLines(eventsPath).Skip(1)
            .Select(line => line.Split(','))
            .Where(f => f.Length > 5 && f[5] == "turn_end")
            .Select(f => f[^1])
            .ToList();
        for (int i = 0; i < Math.Min(expected.Count, actual.Count); i++)
            if (expected[i] != actual[i])
            {
                Console.WriteLine($"MISMATCH at turn {i + 1}: logged checksum {expected[i]}, replay gives {actual[i]}");
                return 1;
            }
        if (expected.Count != actual.Count)
        {
            Console.WriteLine($"MISMATCH: logged {expected.Count} turns, replay gives {actual.Count}");
            return 1;
        }
        Console.WriteLine($"replay matches the event log: {actual.Count} turn checksums identical");
        return 0;
    }

    private static int Fuzz(Args a)
    {
        var (dir, data) = LoadContent(a);
        int battles = a.Int("battles", 1000);
        ulong seed = (ulong)a.Int("seed", 1);
        string party = a.Option("party") ?? "random";
        string enemies = a.Option("enemies") ?? "random";
        Invariants.Enabled = true;
        var encounters = EncounterLoader.All(dir).ToList();
        var outcomes = new Dictionary<Outcome, int>();
        long decisions = 0;
        var watch = Stopwatch.StartNew();

        for (int i = 0; i < battles; i++)
        {
            ulong s = seed + (ulong)i;
            string encounter = encounters[i % encounters.Count];
            var state = EncounterLoader.CreateBattle(dir, data, encounter, s, battleId: i + 1);
            var runner = new BattleRunner(state, ControllerFactory.Create(party, Side.Party, s), ControllerFactory.Create(enemies, Side.Enemy, s + 7919),
                new TimingModel(data, "average", s));
            try
            {
                var outcome = runner.Run();
                outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
                decisions += runner.Decisions;
            }
            catch (Exception e)
            {
                Console.WriteLine($"FAILED: {encounter}, seed {s}, round {state.Round}: {e.GetType().Name}: {e.Message}");
                string path = Path.Combine("logs", $"fuzz-failure-{encounter}-{s}.inputs.jsonl");
                Directory.CreateDirectory("logs");
                using (var w = new StreamWriter(path)) InputLog.Write(w, state, party, enemies, runner.Inputs);
                Console.WriteLine($"inputs up to the failure: {path}");
                return 1;
            }
        }
        Console.WriteLine($"fuzzed {battles} battles ({party} party, {enemies} enemies) across {encounters.Count} encounters in {watch.ElapsedMilliseconds} ms " +
            $"({decisions} inputs, invariants checked after each): won {outcomes.GetValueOrDefault(Outcome.PartyWon)}, " +
            $"lost {outcomes.GetValueOrDefault(Outcome.PartyLost)}, drawn {outcomes.GetValueOrDefault(Outcome.Draw)}");
        return 0;
    }

    // ------------------------------------------------------------------
    // balance: how hard is each encounter?
    // ------------------------------------------------------------------

    /// <summary>What one battle came to, for the balance table.</summary>
    private readonly record struct Result(Outcome Outcome, int Rounds, int Decisions, int HeroHp, bool LoperOut, int Fled);

    private static Result PlayOne(string dir, GameData data, string encounter, ulong seed, int id, string party, string enemies, string skill)
    {
        var state = EncounterLoader.CreateBattle(dir, data, encounter, seed, battleId: id);
        var runner = new BattleRunner(state,
            ControllerFactory.Create(party, Side.Party, seed),
            ControllerFactory.Create(enemies, Side.Enemy, seed),
            new TimingModel(data, skill, seed));
        var outcome = runner.Run();
        int loper = state.Info.LoperIndex;
        int fled = 0;
        for (int u = 0; u < state.Units.Length; u++)
            if (state.Units[u].Has(UnitFlags.Fled)) fled++;
        return new Result(outcome, state.Round, runner.Decisions, state.Units[state.Info.HeroIndex].Hp,
            loper >= 0 && !state.Units[loper].Alive, fled);
    }

    private static int Balance(Args a)
    {
        var (dir, data) = LoadContent(a);
        int battles = a.Int("battles", 200);
        ulong seed = (ulong)a.Int("seed", 1);
        string party = a.Option("party") ?? "scripted";
        string enemies = a.Option("enemies") ?? "scripted";
        string outDir = a.Option("out") ?? "logs";
        var encounters = a.Positional(0) is string one ? new List<string> { one } : EncounterLoader.All(dir).ToList();
        var skills = (a.Option("skills") ?? string.Join(",", data.Rules.TimingSkill.Keys)).Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (string skill in skills)
            if (!data.Rules.TimingSkill.ContainsKey(skill)) throw new ContentException($"no timing skill called '{skill}' in rules.json");

        var csv = new List<string> { "encounter,skill,battles,won,lost,drawn,win_rate,avg_rounds,avg_hero_hp,loper_out_rate,avg_fled,party,enemies,seed,rules,content" };
        Console.WriteLine($"{battles} battles per row · party {party} · enemies {enemies} · rules {RulesVersion.Current}");
        Console.WriteLine();
        Console.WriteLine($"{"encounter",-20} {"skill",-8} {"won",6} {"lost",6} {"drawn",6} {"rounds",7} {"hero HP",8} {"Loper out",10} {"fled",5}");
        var watch = Stopwatch.StartNew();
        foreach (string encounter in encounters)
            foreach (string skill in skills)
            {
                var results = new Result[battles];
                // Battles are independent, so they run side by side; each lands in its own slot, so the
                // table comes out the same however the threads are scheduled.
                Parallel.For(0, battles, i =>
                    results[i] = PlayOne(dir, data, encounter, seed + (ulong)i, i + 1, party, enemies, skill));
                int won = results.Count(r => r.Outcome == Outcome.PartyWon);
                int lost = results.Count(r => r.Outcome == Outcome.PartyLost);
                int drawn = results.Count(r => r.Outcome == Outcome.Draw);
                double rounds = results.Average(r => r.Rounds);
                double heroHp = results.Average(r => r.HeroHp);
                double loperOut = results.Count(r => r.LoperOut) / (double)battles;
                double fled = results.Average(r => r.Fled);
                Console.WriteLine($"{encounter,-20} {skill,-8} {Pct(won, battles),6} {Pct(lost, battles),6} {Pct(drawn, battles),6} " +
                    $"{rounds,7:0.0} {heroHp,8:0.0} {loperOut * 100,9:0}% {fled,5:0.0}");
                csv.Add(string.Join(",", encounter, skill, battles, won, lost, drawn, F(won / (double)battles, "0.000"), F(rounds, "0.00"),
                    F(heroHp, "0.00"), F(loperOut, "0.000"), F(fled, "0.00"), party, enemies, seed, RulesVersion.Current, data.ContentHash));
            }
        Directory.CreateDirectory(outDir);
        string path = Path.Combine(outDir, "balance.csv");
        File.WriteAllLines(path, csv);
        Console.WriteLine();
        Console.WriteLine($"{encounters.Count * skills.Length * battles} battles in {watch.Elapsed.TotalSeconds:0.0} s; table saved to {path}");
        return 0;

        static string Pct(int n, int of) => $"{100.0 * n / of:0}%";
    }

    private static string F(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    // bench: how fast?
    // ------------------------------------------------------------------

    /// <summary>
    /// Plays battles on every encounter for a while and counts inputs per second: once with random
    /// controllers (the rules engine on its own; the target is 50,000 a second) and once with the
    /// scripts (what simulated battles and training will run at). One thread, no logs.
    /// </summary>
    private static int Bench(Args a)
    {
        var (dir, data) = LoadContent(a);
        double seconds = a.Int("seconds", 5);
        var encounters = EncounterLoader.All(dir).ToList();
        string machine = $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()} · {Environment.ProcessorCount} cores · .NET {Environment.Version}";
        Console.WriteLine($"rules {RulesVersion.Current} · {machine}");

        var rows = new List<string>();
        foreach (string mode in new[] { "random", "scripted" })
        {
            long decisions = 0;
            int battles = 0;
            var timer = new Stopwatch();
            var wall = Stopwatch.StartNew();
            while (wall.Elapsed.TotalSeconds < seconds)
            {
                string encounter = encounters[battles % encounters.Count];
                ulong seed = (ulong)battles + 1;
                var state = EncounterLoader.CreateBattle(dir, data, encounter, seed, battleId: battles + 1);
                var runner = new BattleRunner(state,
                    ControllerFactory.Create(mode, Side.Party, seed),
                    ControllerFactory.Create(mode, Side.Enemy, seed),
                    new TimingModel(data, "average", seed));
                timer.Start();
                runner.Run();
                timer.Stop();
                decisions += runner.Decisions;
                battles++;
            }
            double perSecond = decisions / Math.Max(timer.Elapsed.TotalSeconds, 1e-9);
            Console.WriteLine($"{mode + " play:",-15} {battles,6} battles, {decisions,9} inputs in {timer.Elapsed.TotalSeconds:0.00} s -> {perSecond,10:N0} inputs a second");
            rows.Add(string.Join(",", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), RulesVersion.Current,
                data.ContentHash, '"' + machine + '"', mode, battles, decisions, F(timer.Elapsed.TotalSeconds, "0.000"), F(perSecond, "0")));
        }

        if (a.Flag("no-save")) return 0;
        // Kept with the code, so speed can be followed from step to step and machine to machine.
        string repoRoot = Path.GetDirectoryName(Path.GetFullPath(dir))!;
        string path = Path.Combine(repoRoot, "bench", "results.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, "date,rules,content,machine,mode,battles,inputs,seconds,inputs_per_second\n");
        File.AppendAllLines(path, rows);
        Console.WriteLine($"appended to {Path.GetRelativePath(Environment.CurrentDirectory, path)}");
        return 0;
    }
}

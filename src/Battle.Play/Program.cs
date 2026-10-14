using Battle.Content;
using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Play;

/// <summary>
/// Play a battle in the terminal. You control the hero and the Loper by picking from the legal
/// actions; the enemies use their scripts. The battle is logged like any other, so you can replay
/// it in the viewer afterwards.
///
///   dotnet run --project src/Battle.Play -- first-goblins [--seed 7] [--timing ask] [--no-color]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        // The board uses a few non-ASCII characters (HP bars, arrows); Windows consoles need telling.
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var a = new Args(args, hasCommand: false);
        try
        {
            string contentDir = a.Option("content") ?? ContentLoader.FindContentDir();
            var data = ContentLoader.Load(contentDir);
            string? encounter = a.Positional(0);
            if (encounter == null || a.Flag("help"))
            {
                Console.WriteLine("usage: battle-play <encounter> [--seed N] [--timing average|novice|expert|ask] [--pace step|fast] [--no-color]");
                Console.WriteLine("encounters: " + string.Join(", ", EncounterLoader.All(contentDir)));
                return encounter == null ? 1 : 0;
            }

            ulong seed = a.Option("seed") is string s ? ulong.Parse(s) : (ulong)(DateTime.UtcNow.Ticks % 1_000_000);
            string timing = a.Option("timing") ?? "average";
            bool colour = !a.Flag("no-color") && !Console.IsOutputRedirected;
            Ansi.Enabled = colour;
            ITimingSource timingSource = timing == "ask" ? new TimingPrompt() : new TimingModel(data, timing, seed);

            var session = new Session(contentDir, data, encounter, seed, timingSource);
            var game = new Game(session, Scripts.For(Side.Enemy, seed), colour, a.Option("pace") ?? "step");
            Console.WriteLine($"{encounter} · seed {seed} · timing: {timing}. Type ? for help.");
            game.Play();

            string path = session.Save("human", "scripted", timing);
            Console.WriteLine();
            Console.WriteLine($"logs saved: {path}.events.csv");
            Console.WriteLine($"replay it:  python viewer/replay.py {path}.events.csv");
            return 0;
        }
        catch (ContentException e)
        {
            Console.Error.WriteLine("content error:");
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }
}

/// <summary>Asks the player how the action command went, until the real timing minigames exist.</summary>
public sealed class TimingPrompt : ITimingSource
{
    public Timing Grade(BattleState s, in BattleAction action, TimingNeed need)
    {
        string what = need == TimingNeed.Attack ? $"Action command for {s.Data.Moves[action.Move].Name}!" : "Guard!";
        Console.Write($"{what} 1 fumble · 2 good · 3 perfect (Enter = good): ");
        return (Console.ReadLine() ?? "").Trim() switch
        {
            "1" => Timing.Fumble,
            "3" => Timing.Perfect,
            _ => Timing.Good,
        };
    }
}

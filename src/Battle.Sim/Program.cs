using Battle.Content;
using Battle.Core;

namespace Battle.Sim;

// The command-line simulator. Run "battle-sim help" for the commands.
public static class Program
{
    public static int Main(string[] args)
    {
        var a = new Args(args);
        try
        {
            switch (a.Command)
            {
                case "render": return Render(a);
                case "bench":
                    Console.WriteLine($"rules {RulesVersion.Current}: no battles to benchmark yet (the benchmark arrives in step 8)");
                    return 0;
                case "help":
                    Help();
                    return 0;
                default:
                    Console.Error.WriteLine($"unknown command '{a.Command}'");
                    Help();
                    return 1;
            }
        }
        catch (ContentException e)
        {
            Console.Error.WriteLine("content error:");
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }

    private static void Help()
    {
        Console.WriteLine("usage: battle-sim <command> [options]");
        Console.WriteLine("  render <arena> [--color]   draw an arena from content/arenas (or a path to an arena file)");
        Console.WriteLine("  bench                      measure decisions per second");
        Console.WriteLine("options for every command:");
        Console.WriteLine("  --content <dir>            the content folder (found automatically inside the repository)");
    }

    private static int Render(Args a)
    {
        string contentDir = a.Option("content") ?? ContentLoader.FindContentDir();
        var data = ContentLoader.Load(contentDir);
        string name = a.Positional(0) ?? throw new ContentException("render needs an arena name, e.g. battle-sim render river-crossing");
        var arena = ContentLoader.LoadArena(contentDir, name, data);
        Console.Write(AsciiRender.Arena(arena, data, a.Flag("color") || a.Flag("colour")));
        return 0;
    }
}

/// <summary>Minimal command-line parsing: a command, positional values, --options with values and --flags.</summary>
public sealed class Args
{
    private readonly List<string> _positional = new();
    private readonly Dictionary<string, string?> _options = new(StringComparer.Ordinal);

    public string Command { get; }

    public Args(string[] args)
    {
        Command = args.Length > 0 ? args[0] : "help";
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                string key = args[i][2..];
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                _options[key] = hasValue ? args[++i] : null;
            }
            else _positional.Add(args[i]);
        }
    }

    public string? Positional(int index) => index < _positional.Count ? _positional[index] : null;

    public string? Option(string key) => _options.TryGetValue(key, out var v) ? v : null;

    public bool Flag(string key) => _options.ContainsKey(key);

    public int Int(string key, int fallback) =>
        _options.TryGetValue(key, out var v) && int.TryParse(v, out int n) ? n : fallback;
}

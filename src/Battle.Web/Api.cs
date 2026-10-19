using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using Battle.Content;
using Battle.Core;

namespace Battle.Web;

/// <summary>
/// What the page can call. Every call returns JSON: the battle as the page draws it (see
/// <see cref="Views"/>), plus anything new that happened, or {"error": "..."}.
///
/// A turn from the page's side: draw the view; when the party acts, send the chosen action's index
/// (<see cref="Act"/>); when an enemy acts, call <see cref="Step"/> once per enemy action and show
/// each result; when a result has "pending", ask the player how the action command or guard went
/// and send it with <see cref="Grade"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Api
{
    private const string ContentDir = "/battle/content";
    private static GameData? _data;
    private static WebGame? _game;
    private static int _eventsSent;

    /// <summary>Unpacks the content and loads it. Returns the encounters to choose from.</summary>
    [JSExport]
    public static string Boot() => Guard(() =>
    {
        if (_data == null)
        {
            Unpack();
            _data = ContentLoader.Load(ContentDir);
        }
        return Views.Boot(ContentDir, _data);
    });

    /// <summary>Starts a battle. An empty seed picks one at random, like the terminal game.</summary>
    [JSExport]
    public static string New(string encounter, string seed, string timing) => Guard(() =>
    {
        var data = Data();
        ulong s = seed.Trim().Length > 0 ? ulong.Parse(seed.Trim()) : (ulong)(DateTime.UtcNow.Ticks % 1_000_000);
        if (timing != "ask" && !data.Rules.TimingSkill.ContainsKey(timing)) throw new ArgumentException($"no timing called '{timing}'");
        _game = new WebGame(ContentDir, data, encounter, s, timing);
        _eventsSent = 0;
        return Result(withSetup: true);
    });

    /// <summary>Rebuilds a battle from its input log (the page keeps it, to survive a reload).</summary>
    [JSExport]
    public static string Resume(string inputLog) => Guard(() =>
    {
        var data = Data();
        string path = "/battle/resume.inputs.jsonl";
        File.WriteAllText(path, inputLog);
        _game = WebGame.Replay(ContentDir, data, path);
        _eventsSent = 0;
        return Result(withSetup: true);
    });

    /// <summary>The player's choice: an action's index from the view. <paramref name="version"/> is the view's "v", so a stale tap does nothing.</summary>
    [JSExport]
    public static string Act(int version, int index) => Guard(() =>
    {
        var game = Game();
        if (version != game.Inputs.Count) return Result(note: "The battle moved on before that tap arrived.");
        game.Act(index);
        return Result();
    });

    /// <summary>The enemy whose turn it is plays its next action.</summary>
    [JSExport]
    public static string Step() => Guard(() =>
    {
        Game().EnemyStep();
        return Result();
    });

    /// <summary>Grades the waiting action command or guard: "fumble", "good" or "perfect".</summary>
    [JSExport]
    public static string Grade(string grade) => Guard(() =>
    {
        Game().Grade(grade switch
        {
            "fumble" => Timing.Fumble,
            "good" => Timing.Good,
            "perfect" => Timing.Perfect,
            _ => throw new ArgumentException($"no grade called '{grade}'"),
        });
        return Result();
    });

    /// <summary>The battle as it stands, without new events.</summary>
    [JSExport]
    public static string View() => Guard(() => Result());

    /// <summary>A unit's danger zone, or every enemy's when <paramref name="unit"/> is -1, as tile indices.</summary>
    [JSExport]
    public static string Danger(int unit) => Guard(() => Views.Danger(Game().S, unit));

    /// <summary>The battle's input log so far (JSON Lines), for saving and for replaying in battle-sim.</summary>
    [JSExport]
    public static string Log() => Guard(() => Game().InputLog());

    /// <summary>The battle's event log so far (CSV, as the viewer reads it), with a checksum at every turn's end.</summary>
    [JSExport]
    public static string EventLog() => Guard(() =>
    {
        var game = Game();
        var writer = new StringWriter { NewLine = "\n" };
        Battle.Content.Logs.EventLog.WriteHeader(writer);
        Battle.Content.Logs.EventLog.Write(writer, game.S, game.Events);
        return writer.ToString();
    });

    // ------------------------------------------------------------------

    private static GameData Data() => _data ?? throw new InvalidOperationException("the game hasn't booted");

    private static WebGame Game() => _game ?? throw new InvalidOperationException("no battle is running");

    private static string Result(bool withSetup = false, string? note = null)
    {
        var game = Game();
        string json = Views.Result(game, _eventsSent, withSetup, note);
        _eventsSent = game.Events.Count;
        return json;
    }

    private static string Guard(Func<string> body)
    {
        try { return body(); }
        catch (Exception e) when (e is IllegalActionException or ContentException or ArgumentException or FormatException or OverflowException)
        {
            return Views.Error(e.Message);
        }
        catch (Exception e)
        {
            // Anything else is a bug: send the whole thing, so it can be reported.
            return Views.Error($"{e.GetType().Name}: {e.Message}", e.ToString());
        }
    }

    /// <summary>Writes the embedded content files into the in-memory file system, where the loaders expect a folder.</summary>
    private static void Unpack()
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (string name in assembly.GetManifestResourceNames())
        {
            string relative = name.Replace('\\', '/');
            if (!relative.StartsWith("content/", StringComparison.Ordinal)) continue;
            string path = "/battle/" + relative;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            File.WriteAllText(path, reader.ReadToEnd());
        }
    }
}

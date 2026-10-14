using Battle.Content;
using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Play;

/// <summary>The terminal game loop: draw the board, read a command, apply it, show what happened.</summary>
public sealed class Game
{
    private readonly Session _session;
    private readonly IController _enemies;
    private readonly bool _colour;
    private readonly string _pace;
    private readonly List<BattleAction> _legal = new();
    private bool _showDanger;
    private int _inspect = -1;
    private string? _message;
    private bool _quit;

    public Game(Session session, IController enemies, bool colour, string pace)
    {
        _session = session;
        _enemies = enemies;
        _colour = colour;
        _pace = pace;
    }

    private BattleState S => _session.State;

    public void Play()
    {
        while (!_quit && S.Outcome == Outcome.Ongoing)
        {
            int actor = Rules.CurrentActor(S);
            if (actor < 0) break;
            if (S.Units[actor].Side == Side.Party) PartyTurn(actor);
            else EnemyTurn(actor);
        }
        ShowNewEvents();
        if (S.Outcome != Outcome.Ongoing) Console.WriteLine(Render());
    }

    // ------------------------------------------------------------------
    // The enemies
    // ------------------------------------------------------------------

    private void EnemyTurn(int actor)
    {
        while (S.Outcome == Outcome.Ongoing && Rules.CurrentActor(S) == actor)
        {
            Rules.LegalActions(S, _legal);
            _session.Apply(_enemies.Choose(S, actor, _legal));
        }
        bool showed = ShowNewEvents();
        if (showed && _pace == "step" && S.Outcome == Outcome.Ongoing)
        {
            Console.Write("  (Enter to continue) ");
            if (Console.ReadLine() == null) _quit = true;
        }
        else if (showed && _pace == "fast") Thread.Sleep(400);
    }

    // ------------------------------------------------------------------
    // The party
    // ------------------------------------------------------------------

    private void PartyTurn(int actor)
    {
        ShowNewEvents();
        Rules.LegalActions(S, _legal);
        Console.WriteLine();
        Console.WriteLine(Render());
        if (_message != null) { Console.WriteLine(_message); _message = null; }
        Console.Write("> ");
        string? line = Console.ReadLine();
        if (line == null) { _quit = true; return; }
        Handle(actor, line.Trim());
    }

    private void Handle(int actor, string line)
    {
        string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;
        var moves = MoveChoices();

        switch (words[0].ToLowerInvariant())
        {
            case "q" or "quit":
                _quit = true;
                return;
            case "?" or "h" or "help":
                _message = Help;
                return;
            case "e" or "end":
                Try(BattleAction.End(actor));
                return;
            case "d" or "danger":
                _showDanger = !_showDanger;
                _inspect = -1;
                return;
            case "u" or "undo":
                if (_session.CanUndo()) { _session.Undo(); _message = "undone"; }
                else _message = "nothing to undo in this turn";
                return;
            case "l" or "log":
                _message = string.Join("\n", _session.Log.Events.Select(e => Describe.Event(S, e)).OfType<string>().TakeLast(20));
                return;
            case "i" or "inspect":
                Inspect(words.Length > 1 ? words[1] : "");
                return;
            case "m" or "move" when words.Length == 3:
                MoveTo(actor, words[1], words[2]);
                return;
        }

        if (int.TryParse(words[0], out int choice) && choice >= 1 && choice <= moves.Count)
        {
            var options = moves[choice - 1].Options;
            if (options.Count == 1) { Try(options[0]); return; }
            if (words.Length > 1 && int.TryParse(words[1], out int pick) && pick >= 1 && pick <= options.Count)
            {
                Try(options[pick - 1]);
                return;
            }
            Console.WriteLine($"{moves[choice - 1].Label}:");
            for (int i = 0; i < options.Count; i++) Console.WriteLine($"  {i + 1,2}  {Describe.Action(S, options[i])}");
            Console.Write("target> ");
            if (int.TryParse(Console.ReadLine(), out int t) && t >= 1 && t <= options.Count) Try(options[t - 1]);
            else _message = "cancelled";
            return;
        }
        _message = "not a command; type ? for help";
    }

    private void MoveTo(int actor, string x, string y)
    {
        if (!int.TryParse(x, out int px) || !int.TryParse(y, out int py)) { _message = "move needs two numbers: m X Y"; return; }
        var action = BattleAction.MoveTo(actor, new Pos(px, py));
        if (!_legal.Any(a => a.SameChoice(action))) { _message = $"can't move to ({px},{py}) from here"; return; }
        Try(action);
    }

    private void Try(BattleAction action)
    {
        try { _session.Apply(action); }
        catch (IllegalActionException e) { _message = $"not allowed: {e.Message}"; }
    }

    /// <summary>The legal actions other than walking, grouped by move, numbered for the menu.</summary>
    private List<(string Label, List<BattleAction> Options)> MoveChoices()
    {
        var groups = new List<(string Label, List<BattleAction> Options)>();
        foreach (var a in _legal)
        {
            if (a.Kind is ActionKind.Move or ActionKind.End) continue;
            string label = a.Kind switch
            {
                ActionKind.SwapOrder => "Swap order",
                ActionKind.Mount => "Mount",
                ActionKind.Dismount => "Dismount",
                _ => S.Data.Moves[a.Move].Name + (S.Data.Moves[a.Move].Slot switch
                {
                    MoveSlot.Bonus => " (bonus)",
                    MoveSlot.Special => " (main + bonus)",
                    _ => "",
                }),
            };
            int index = groups.FindIndex(g => g.Label == label);
            if (index < 0) groups.Add((label, new List<BattleAction> { a }));
            else groups[index].Options.Add(a);
        }
        return groups;
    }

    private void Inspect(string label)
    {
        int unit = S.Info.UnitIndex(label.ToLowerInvariant());
        if (unit < 0 && label.Equals("h", StringComparison.OrdinalIgnoreCase)) unit = S.Info.HeroIndex;
        if (unit < 0 && label.Equals("l", StringComparison.OrdinalIgnoreCase)) unit = S.Info.LoperIndex;
        if (unit < 0) { _message = "inspect who? e.g. i a, i hero, i loper"; return; }
        ref var u = ref S.Units[unit];
        var def = S.DefOf(unit);
        var lines = new List<string>
        {
            $"{Describe.Name(S, unit)}: {u.Hp}/{u.MaxHp} HP, {def.Weight.ToString().ToLowerInvariant()}, " +
            $"moves {u.MoveMax / 2.0:0.#} tiles, power {u.Power}. {AsciiRender.Statuses(S, unit)}".TrimEnd(),
        };
        foreach (int m in S.Info.Kits[unit])
        {
            var move = S.Data.Moves[m];
            lines.Add($"  {move.Name} ({move.Slot.ToString().ToLowerInvariant()}): {move.Text}");
        }
        lines.Add("Its danger zone is marked on the board.");
        _inspect = unit;
        _showDanger = false;
        _message = string.Join("\n", lines);
    }

    // ------------------------------------------------------------------
    // Drawing
    // ------------------------------------------------------------------

    private string Render()
    {
        int actor = Rules.CurrentActor(S);
        bool[]? marked = null;
        string markNote = "";
        if (_inspect >= 0) { marked = Observation.DangerOf(S, _inspect); markNote = $"marked: {Describe.Name(S, _inspect)}'s danger zone"; }
        else if (_showDanger) { marked = Observation.Danger(S, Side.Party); markNote = "marked: the enemies' danger zone"; }
        else if (actor >= 0 && S.Units[actor].Side == Side.Party)
        {
            marked = new bool[S.TileCount];
            foreach (var a in _legal)
                if (a.Kind == ActionKind.Move) marked[S.Idx(a.Target.Tile)] = true;
            markNote = _colour ? "highlighted: where you can walk" : "*: where you can walk";
        }

        var panel = new List<string>(AsciiRender.UnitPanel(S)) { "" };
        if (markNote.Length > 0) panel.Add(markNote);

        var sb = new System.Text.StringBuilder();
        string phase = Rules.PhaseName(S.Phase);
        sb.AppendLine(actor >= 0
            ? $"round {S.Round} · {phase} phase · {Describe.Name(S, actor)}'s turn"
            : $"round {S.Round} · {Sim(S.Outcome)}");
        sb.Append(AsciiRender.Board(S, marked, _colour, panel.ToArray()));

        if (actor >= 0 && S.Units[actor].Side == Side.Party)
        {
            ref var u = ref S.Units[actor];
            sb.AppendLine($"movement left {Tiles(u.MoveLeft)} · main {(u.Has(UnitFlags.MainUsed) ? "used" : "ready")} · " +
                $"bonus {(u.Has(UnitFlags.BonusUsed) ? "used" : "ready")}");
            var moves = MoveChoices();
            var items = moves.Select((m, i) => $"{i + 1} {m.Label}{(m.Options.Count > 1 ? $" [{m.Options.Count}]" : "")}").ToList();
            items.Add("e End turn");
            sb.AppendLine("actions: " + string.Join("   ", items));
            if (S.Info.Kits[actor].Any(m => S.Data.Moves[m].Element == Element.Root)) sb.AppendLine(RootSources(actor));
            sb.Append("walk with: m X Y   ·   ? for help");
        }
        return sb.ToString();
    }

    /// <summary>The Root features this unit can draw on, so the current stance's sources stand out.</summary>
    private string RootSources(int actor)
    {
        var at = S.Units[actor].Pos;
        var sources = new List<string>();
        for (int tile = 0; tile < S.TileCount; tile++)
        {
            if (!Rules.Feeds(S, tile, Element.Root) || S.PosOf(tile).Manhattan(at) > S.Data.Rules.SourceRange) continue;
            var f = S.FeatureAt(tile)!;
            sources.Add($"{f.Name.ToLowerInvariant()} {S.PosOf(tile)} {S.FeatureUses[tile]} uses, {S.FeatureHp[tile]} HP");
        }
        return sources.Count > 0
            ? "Root sources in reach: " + string.Join("; ", sources)
            : "No Root source in reach: Bind will only slow and Tether lasts a round";
    }

    private static string Tiles(int halfPoints) => halfPoints == 2 ? "1 tile" : $"{halfPoints / 2.0:0.#} tiles";

    private static string Sim(Outcome outcome) => outcome switch
    {
        Outcome.PartyWon => "victory!",
        Outcome.PartyLost => "defeat",
        Outcome.Draw => "draw",
        _ => "",
    };

    /// <summary>Prints the events nobody has seen yet. Returns whether anything was printed.</summary>
    private bool ShowNewEvents()
    {
        var events = _session.Log.Events;
        bool any = false;
        for (int i = _session.EventsShown; i < events.Count; i++)
        {
            if (Describe.Event(S, events[i]) is string line)
            {
                Console.WriteLine("  " + line);
                any = true;
            }
        }
        _session.EventsShown = events.Count;
        return any;
    }

    private const string Help = """
        commands:
          m X Y                  walk to a tile (x across, y down)
          N                      use action N from the list (you'll be asked for a target)
          N T                    use action N on target T
          e                      end this unit's turn
          i a / i hero           inspect a unit: stats, moves and its danger zone
          d                      show or hide the enemies' danger zone
          u                      undo your last action this turn
          l                      the last 20 events
          q                      quit (the battle so far is still logged)
        on the board:
          H L                    the hero and the Loper; letters are enemies (i a to inspect one)
          * (or highlighted)     where the unit whose turn it is can walk
          ^  "                   a goblin's snap trap (bites anyone)  ·  a root trap (catches enemies)
          !                      rumbling ground: something will burst out of it next turn
        """;
}

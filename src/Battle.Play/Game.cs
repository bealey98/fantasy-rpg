using System.Text;
using Battle.Content;
using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Play;

/// <summary>
/// The terminal game loop: draw the board, read a command, apply it, show what happened. It can
/// also play by message (<see cref="PlayByMessage"/>), for a phone: each run replays the moves so
/// far, applies the new ones and prints a narrow board.
/// </summary>
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
    private bool _byMessage;

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
    // Playing by message
    // ------------------------------------------------------------------

    /// <summary>
    /// One exchange of a game played by message. The moves file holds every command accepted so far;
    /// they're replayed (battles are deterministic, so this rebuilds the same battle), then the new
    /// commands are applied, with the enemies taking their turns in between. It prints what the new
    /// commands led to and the board, narrow enough for a phone, and adds the accepted commands to
    /// the file. Commands can be separated by ';' or ','. Nothing ever waits for input.
    /// </summary>
    public void PlayByMessage(string movesPath, string? newCommands)
    {
        _byMessage = true;
        var old = File.Exists(movesPath)
            ? File.ReadAllLines(movesPath).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
            : new List<string>();
        foreach (string command in old)
        {
            AdvanceEnemies();
            if (S.Outcome != Outcome.Ongoing) break;
            Rules.LegalActions(S, _legal);
            Handle(Rules.CurrentActor(S), command);
        }
        AdvanceEnemies();
        _message = null;
        _inspect = -1;
        _showDanger = false;
        if (old.Count > 0) _session.EventsShown = _session.Log.Events.Count;

        var accepted = new List<string>();
        var notes = new List<string>();
        bool heights = old.Count == 0;
        foreach (string command in (newCommands ?? "").Split(new[] { ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (S.Outcome != Outcome.Ongoing) { notes.Add("The battle is over, so the rest of those commands were ignored."); break; }
            if (command is "heights" or "ht") { heights = true; continue; }
            int before = _session.Inputs.Count;
            _message = null;
            Rules.LegalActions(S, _legal);
            Handle(Rules.CurrentActor(S), command);
            bool applied = _session.Inputs.Count > before;
            if (applied) accepted.Add(command);
            if (_message != null) notes.Add(applied ? _message : $"\"{command}\": {_message}");
            AdvanceEnemies();
        }
        if (accepted.Count > 0) File.AppendAllLines(movesPath, accepted);

        ShowNewEvents();
        Console.WriteLine();
        Console.Write(RenderNarrow());
        if (heights)
        {
            Console.WriteLine();
            Console.WriteLine("heights (1 low, 2 base, 3 high, 4 very high):");
            Console.Write(AsciiRender.Heights(S));
        }
        foreach (string note in notes)
        {
            Console.WriteLine();
            Console.WriteLine(note);
        }
    }

    /// <summary>Lets the enemies play until it's the party's turn or the battle is over, printing nothing.</summary>
    private void AdvanceEnemies()
    {
        while (S.Outcome == Outcome.Ongoing)
        {
            int actor = Rules.CurrentActor(S);
            if (actor < 0 || S.Units[actor].Side == Side.Party) return;
            Rules.LegalActions(S, _legal);
            _session.Apply(_enemies.Choose(S, actor, _legal));
        }
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

        // An action by name instead of its number, so a list of commands still works after the
        // menu's numbering shifts: "strike a", "bull rush b", "bind 3 4", "swap", "flee", "mount".
        if (ByName(moves, words) is (int index, int used))
            words = [(index + 1).ToString(), .. words[used..]];

        if (int.TryParse(words[0], out int choice) && choice >= 1 && choice <= moves.Count)
        {
            var (label, options) = moves[choice - 1];
            if (options.Count == 1 && words.Length == 1) { Try(options[0]); return; }
            if (words.Length > 1)
            {
                if (PickOption(options, words[1..]) is BattleAction picked) Try(picked);
                else _message = $"{label} can't take that target. Its targets:\n{OptionList(options)}";
                return;
            }
            if (_byMessage)
            {
                _message = $"{label}: pick a target, e.g. \"{choice} 1\"\n{OptionList(options)}";
                return;
            }
            Console.WriteLine($"{label}:");
            Console.WriteLine(OptionList(options));
            Console.Write("target> ");
            if (int.TryParse(Console.ReadLine(), out int t) && t >= 1 && t <= options.Count) Try(options[t - 1]);
            else _message = "cancelled";
            return;
        }
        string named = string.Concat(words.TakeWhile(w => !w.Any(char.IsDigit)).Take(3));
        MoveDef? Known(int unit) => unit < 0 ? null : S.Info.Kits[unit].Select(m => S.Data.Moves[m])
            .FirstOrDefault(m => named.StartsWith(string.Concat(m.Name.Split(' ')), StringComparison.OrdinalIgnoreCase));
        int partner = Rules.SwapTarget(S, actor);
        if (Known(actor) is MoveDef mine) _message = $"{mine.Name} isn't possible right now: nothing in reach, or its action is used";
        else if (Known(partner) is MoveDef theirs) _message = $"{theirs.Name} is the {Describe.Name(S, partner)}'s move: swap to it first";
        else _message = "not a command; type ? for help";
    }

    /// <summary>
    /// The menu entry named by the first words of a command, and how many words the name took:
    /// "bull rush a" names Bull Rush in two words. "swap" and "flee" name those actions too.
    /// </summary>
    private static (int Index, int Used)? ByName(List<(string Label, List<BattleAction> Options)> moves, string[] words)
    {
        static string Plain(string text) => new(text.ToLowerInvariant().Where(char.IsLetter).ToArray());
        int wordy = 0; // only leading words without digits can be part of a name: "bind 4 3" is Bind on (4,3)
        while (wordy < words.Length && wordy < 3 && !words[wordy].Any(char.IsDigit)) wordy++;
        for (int used = wordy; used >= 1; used--)
        {
            string name = Plain(string.Concat(words[..used]));
            if (name.Length == 0) continue;
            for (int i = 0; i < moves.Count; i++)
            {
                string label = moves[i].Label;
                int paren = label.IndexOf('(');
                string plain = Plain(paren > 0 ? label[..paren] : label);
                if (plain == name || (name == "swap" && plain.StartsWith("swapto")) || (name == "flee" && plain.StartsWith("flee")))
                    return (i, used);
            }
        }
        return null;
    }

    private string OptionList(List<BattleAction> options) =>
        string.Join("\n", options.Select((o, i) => $"  {i + 1,2}  {Describe.Action(S, o)}"));

    /// <summary>
    /// A target written after an action's number: its number in the list, a unit's letter
    /// ("a", "hero", "loper"), two unit letters for Tether's two ends, a tile ("3 5"), "w" for the
    /// hero's weapon or "self". Null if no single option matches.
    /// </summary>
    private BattleAction? PickOption(List<BattleAction> options, string[] args)
    {
        List<BattleAction> Matching(Func<BattleAction, bool> test)
        {
            var found = options.Where(test).ToList();
            return found;
        }

        List<BattleAction> found;
        if (args.Length == 1 && int.TryParse(args[0], out int index))
            return index >= 1 && index <= options.Count ? options[index - 1] : null;
        if (args.Length == 2 && int.TryParse(args[0], out int x) && int.TryParse(args[1], out int y))
            found = Matching(o => o.Target.Kind == TargetKind.Tile && o.Target.Tile == new Pos(x, y) && o.Target2.Kind == TargetKind.None);
        else if (args.Length == 1 && args[0].ToLowerInvariant() is "w" or "weapon")
            found = Matching(o => o.Target.Kind == TargetKind.Weapon);
        else if (args.Length == 1 && args[0].ToLowerInvariant() is "self" or "me")
            found = Matching(o => o.Target.Kind == TargetKind.Self);
        else if (args.Length == 1 && UnitByName(args[0]) is int unit and >= 0)
            found = Matching(o => o.Target.Kind == TargetKind.Unit && o.Target.Unit == unit && o.Target2.Kind == TargetKind.None);
        else if (args.Length == 2 && UnitByName(args[0]) is int first and >= 0 && UnitByName(args[1]) is int second and >= 0)
            found = Matching(o => o.Target.Kind == TargetKind.Unit && o.Target.Unit == first && o.Target2.Kind == TargetKind.Unit && o.Target2.Unit == second);
        else return null;
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>A unit by its letter or name: "a", "hero" or "h", "loper" or "l". -1 if none.</summary>
    private int UnitByName(string name)
    {
        name = name.ToLowerInvariant();
        if (name is "h" or "hero") return S.Info.HeroIndex;
        if (name is "l" or "loper") return S.Info.LoperIndex;
        return S.Info.UnitIndex(name);
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
                ActionKind.SwapPartner => $"Swap to the {Describe.Name(S, a.Target.Unit)}",
                ActionKind.Flee => "Flee the battle",
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

    /// <summary>What to mark on the board: an inspected unit's danger zone, every enemy's, or where the acting unit can walk.</summary>
    private (bool[]? Marked, string Note) Marks(int actor, bool colour)
    {
        if (_inspect >= 0) return (Observation.DangerOf(S, _inspect), $"marked: {Describe.Name(S, _inspect)}'s danger zone");
        if (_showDanger) return (Observation.Danger(S, Side.Party), "marked: the enemies' danger zone");
        if (actor < 0 || S.Units[actor].Side != Side.Party) return (null, "");
        var marked = new bool[S.TileCount];
        foreach (var a in _legal)
            if (a.Kind == ActionKind.Move) marked[S.Idx(a.Target.Tile)] = true;
        return (marked, colour ? "highlighted: where you can walk" : "*: where you can walk");
    }

    /// <summary>
    /// The screen for play by message: the board without the heights beside it, then one line per
    /// unit, then the actions with their targets spelled out, so nothing needs a follow-up prompt.
    /// </summary>
    private string RenderNarrow()
    {
        int actor = Rules.CurrentActor(S);
        Rules.LegalActions(S, _legal);
        var (marked, markNote) = Marks(actor, colour: false);
        var sb = new StringBuilder();
        int partner = actor >= 0 ? Rules.SwapTarget(S, actor) : -1;
        string alongside = partner >= 0 ? $" ({Describe.Name(S, partner)}'s turn still open)" : "";
        sb.AppendLine(actor >= 0
            ? $"Round {S.Round} · {Describe.Name(S, actor)}'s turn{alongside}"
            : $"Round {S.Round} · {Sim(S.Outcome)}");
        sb.Append(AsciiRender.Board(S, marked, colour: false, heights: false));
        sb.AppendLine(MapLegend(marked != null && markNote.StartsWith('*') ? "* where you can walk" : markNote));
        sb.AppendLine();
        for (int i = 0; i < S.Units.Length; i++)
        {
            ref var u = ref S.Units[i];
            string who = $"{AsciiRender.UnitGlyph(S, i)} {(u.Side == Side.Party ? Describe.Name(S, i) : S.DefOf(i).Name)}";
            if (!u.Alive)
            {
                sb.AppendLine($"{who}: {(u.Has(UnitFlags.Fled) ? "fled" : u.Has(UnitFlags.KnockedOut) ? "knocked out" : "defeated")}");
                continue;
            }
            string statuses = AsciiRender.Statuses(S, i);
            sb.AppendLine($"{who} {u.Hp}/{u.MaxHp}{(statuses.Length > 0 ? $" · {statuses}" : "")}");
        }

        if (actor >= 0 && S.Units[actor].Side == Side.Party)
        {
            ref var u = ref S.Units[actor];
            sb.AppendLine();
            sb.AppendLine($"{Describe.Name(S, actor)}: {Tiles(u.MoveLeft)} to walk · main {(u.Has(UnitFlags.MainUsed) ? "used" : "ready")} · " +
                $"bonus {(u.Has(UnitFlags.BonusUsed) ? "used" : "ready")}");
            var moves = MoveChoices();
            for (int i = 0; i < moves.Count; i++) sb.AppendLine($"{i + 1} {moves[i].Label}{TargetSummary(moves[i].Options)}");
            sb.AppendLine("e End turn · m X Y to walk");
            if (S.Info.Kits[actor].Any(m => S.Data.Moves[m].Element == Element.Root)) sb.AppendLine(RootSources(actor));
        }
        return sb.ToString();
    }

    /// <summary>What the board's non-unit characters mean, for those on it now, plus the marking in use.</summary>
    private string MapLegend(string marking)
    {
        var seen = new List<string>();
        void Add(char glyph, string name) { string item = $"{glyph} {name}"; if (!seen.Contains(item)) seen.Add(item); }
        for (int i = 0; i < S.TileCount; i++)
        {
            if (S.Occupant[i] >= 0) continue;
            if (S.Rumble[i] >= 0) Add('!', "rumbling ground");
            else if (S.Trap[i] == TrapKind.Snap) Add('^', "snap trap");
            else if (S.Trap[i] != TrapKind.None) Add('"', "root trap");
            else if (S.FeatureAt(i) is FeatureDef f) Add(f.Glyph, (S.FeatureDead[i] ? "dead " : "") + f.Name.ToLowerInvariant());
            else if (S.TerrainAt(i).Glyph != '.') Add(S.TerrainAt(i).Glyph, S.TerrainAt(i).Name.ToLowerInvariant());
        }
        if (marking.Length > 0) seen.Add(marking);
        return string.Join(" · ", seen);
    }

    /// <summary>A move's targets in a few words: unit letters, "a tile" (N X Y), the weapon, or how many pairs.</summary>
    private string TargetSummary(List<BattleAction> options)
    {
        if (options.Count == 1 && (options[0].Target.Kind is TargetKind.None or TargetKind.Self
            || options[0].Kind is ActionKind.SwapPartner or ActionKind.Mount)) return "";
        if (options.Any(o => o.Target2.Kind != TargetKind.None)) return $": {options.Count} choices (send the number alone to list them)";
        var parts = new List<string>();
        var units = options.Where(o => o.Target.Kind == TargetKind.Unit).Select(o => AsciiRender.UnitGlyph(S, o.Target.Unit).ToString()).Distinct().ToList();
        if (units.Count > 0) parts.Add(string.Join(" ", units));
        int tiles = options.Count(o => o.Target.Kind == TargetKind.Tile);
        if (tiles == 1) parts.Add($"tile {options.First(o => o.Target.Kind == TargetKind.Tile).Target.Tile}");
        else if (tiles > 1) parts.Add($"a tile ({tiles} choices, as N X Y)");
        if (options.Any(o => o.Target.Kind == TargetKind.Weapon)) parts.Add("your weapon (w)");
        return parts.Count > 0 ? ": " + string.Join(", or ", parts) : "";
    }

    private string Render()
    {
        int actor = Rules.CurrentActor(S);
        var (marked, markNote) = Marks(actor, _colour);

        var panel = new List<string>(AsciiRender.UnitPanel(S)) { "" };
        if (markNote.Length > 0) panel.Add(markNote);

        var sb = new System.Text.StringBuilder();
        string phase = Rules.PhaseName(S.Phase);
        int partner = actor >= 0 ? Rules.SwapTarget(S, actor) : -1;
        string alongside = partner >= 0 ? $" (the {Describe.Name(S, partner)}'s is going too)" : "";
        sb.AppendLine(actor >= 0
            ? $"round {S.Round} · {phase} phase · {Describe.Name(S, actor)}'s turn{alongside}"
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
        Outcome.PartyFled => "the party fled",
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
          N T                    use action N on target T: its number in the target list, a unit's
                                 letter (a, hero, loper), a tile (N X Y), or w for your weapon
          strike a, bind 4 3     an action by name works too, as do swap, flee and mount
          e                      end this unit's turn (the other carries on, if its turn is still going)
          i a / i hero           inspect a unit: stats, moves and its danger zone
          d                      show or hide the enemies' danger zone
          l                      the last 20 events
          q                      quit (the battle so far is still logged)
        playing by message, commands can be chained: m 5 4; strike a; e
        on the board:
          H L                    the hero and the Loper; letters are enemies (i a to inspect one)
          * (or highlighted)     where the unit whose turn it is can walk
          ^  "                   a goblin's snap trap (bites anyone)  ·  a root trap (catches enemies)
          !                      rumbling ground: something will burst out of it next turn
        """;
}

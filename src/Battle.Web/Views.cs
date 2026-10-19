using System.Text;
using System.Text.Json;
using Battle.Content;
using Battle.Core;

namespace Battle.Web;

/// <summary>
/// The JSON the page reads. Everything the page shows comes from here, worded by the same code as
/// the terminal game (Describe, AsciiRender.Statuses), so the two never drift apart.
/// </summary>
internal static class Views
{
    // ------------------------------------------------------------------
    // Booting: the encounters to choose from
    // ------------------------------------------------------------------

    public static string Boot(string contentDir, GameData data) => Json(w =>
    {
        w.WriteString("rules", RulesVersion.Current);
        w.WriteString("content", data.ContentHash);
        WriteInts(w, "attackBonus", data.Rules.AttackBonus);
        WriteInts(w, "guardReduction", data.Rules.GuardReduction);
        w.WriteStartArray("terrain");
        foreach (var t in data.Terrain) w.WriteStringValue(t.Id);
        w.WriteEndArray();
        w.WriteStartArray("features");
        foreach (var f in data.Features) w.WriteStringValue(f.Id);
        w.WriteEndArray();
        w.WriteStartArray("skills");
        foreach (var (name, chances) in data.Rules.TimingSkill)
        {
            w.WriteStartObject();
            w.WriteString("id", name);
            WriteInts(w, "chances", chances);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("encounters");
        foreach (string id in EncounterLoader.All(contentDir))
        {
            var encounter = EncounterLoader.Load(contentDir, id, data);
            var arena = ContentLoader.LoadArena(contentDir, encounter.Arena, data);
            w.WriteStartObject();
            w.WriteString("id", id);
            w.WriteString("arena", arena.Name);
            w.WriteNumber("w", arena.Width);
            w.WriteNumber("h", arena.Height);
            w.WriteNumber("tier", encounter.Party.ElderwoodTier);
            w.WriteString("about", LeadingComment(Path.Combine(contentDir, "encounters", id + ".json")));
            w.WriteStartArray("enemies");
            foreach (var (_, unit) in encounter.Enemies.OrderBy(e => e.Key)) w.WriteStringValue(data.Unit(unit).Name);
            w.WriteEndArray();
            // The arena as it starts, for the little map on the encounter's card.
            w.WriteStartObject("map");
            WriteInts(w, "height", arena.Heights.Select(h => (int)h));
            WriteInts(w, "terrain", arena.Terrain.Select(t => (int)t));
            WriteInts(w, "feature", arena.Features.Select(f => (int)f));
            w.WriteStartArray("units");
            foreach (var u in arena.Units)
            {
                w.WriteStartArray();
                w.WriteStringValue(u.Glyph.ToString());
                w.WriteNumberValue(u.Pos.X);
                w.WriteNumberValue(u.Pos.Y);
                w.WriteEndArray();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    });

    /// <summary>The comment at the top of a content file, which says what it's for.</summary>
    private static string LeadingComment(string path)
    {
        var lines = File.ReadLines(path).TakeWhile(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Select(l => l.TrimStart()[2..].Trim());
        return string.Join(" ", lines);
    }

    // ------------------------------------------------------------------
    // A battle
    // ------------------------------------------------------------------

    /// <summary>
    /// The answer to any battle call: events since <paramref name="eventsFrom"/>, the view, and with
    /// <paramref name="withSetup"/> the things that never change during the battle.
    /// </summary>
    public static string Result(WebGame game, int eventsFrom, bool withSetup, string? note) => Json(w =>
    {
        var s = game.S;
        if (withSetup)
        {
            w.WriteStartObject("setup");
            Setup(w, game);
            w.WriteEndObject();
        }
        w.WriteStartArray("events");
        for (int i = eventsFrom; i < game.Events.Count; i++) Event(w, s, game.Events[i]);
        w.WriteEndArray();
        w.WriteStartObject("view");
        View(w, game);
        w.WriteEndObject();
        if (note != null) w.WriteString("note", note);
    });

    private static void Setup(Utf8JsonWriter w, WebGame game)
    {
        var s = game.S;
        w.WriteString("encounter", s.Info.EncounterId);
        w.WriteString("arena", s.Info.ArenaName);
        w.WriteString("time", s.Info.Time);
        w.WriteNumber("seed", game.Seed);
        w.WriteString("timing", game.TimingMode);
        w.WriteNumber("w", s.Width);
        w.WriteNumber("h", s.Height);
        w.WriteNumber("tier", s.Info.ElderwoodTier);

        w.WriteStartArray("terrain");
        foreach (var t in s.Data.Terrain)
        {
            w.WriteStartObject();
            w.WriteString("id", t.Id);
            w.WriteString("name", t.Name);
            w.WriteString("glyph", t.Glyph.ToString());
            w.WriteBoolean("walkable", t.Walkable);
            w.WriteBoolean("water", t.Water);
            w.WriteBoolean("noGround", t.NoGround);
            w.WriteBoolean("ladder", t.Ladder);
            w.WriteBoolean("object", t.IsObject);
            w.WriteString("cost", Tiles(t.MoveCost));
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("features");
        foreach (var f in s.Data.Features)
        {
            w.WriteStartObject();
            w.WriteString("id", f.Id);
            w.WriteString("name", f.Name);
            w.WriteString("glyph", f.Glyph.ToString());
            w.WriteString("feeds", f.Feeds == Element.None ? "" : f.Feeds.ToString().ToLowerInvariant().Replace(", ", " and "));
            w.WriteBoolean("blocks", f.Blocks);
            w.WriteBoolean("creature", f.IsCreature);
            w.WriteNumber("uses", f.Uses);
            w.WriteNumber("hp", f.Hp);
            w.WriteString("extra", f.ExtraMoveCost > 0 ? Tiles(f.ExtraMoveCost) : "");
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("units");
        for (int i = 0; i < s.Units.Length; i++)
        {
            var def = s.DefOf(i);
            ref var u = ref s.Units[i];
            w.WriteStartObject();
            w.WriteString("label", s.Label(i));
            w.WriteString("glyph", AsciiRender.UnitGlyph(s, i).ToString());
            w.WriteString("name", Describe.Name(s, i));
            w.WriteString("type", def.Name);
            w.WriteString("side", u.Side == Side.Party ? "party" : "enemy");
            w.WriteString("weight", def.Weight.ToString().ToLowerInvariant());
            w.WriteString("move", Tiles(u.MoveMax));
            w.WriteNumber("power", u.Power);
            w.WriteNumber("defence", u.Defence);
            w.WriteBoolean("beast", u.Handler >= 0);
            w.WriteStartArray("kit");
            foreach (int m in s.Info.Kits[i])
            {
                var move = s.Data.Moves[m];
                w.WriteStartObject();
                w.WriteString("name", move.Name);
                w.WriteString("slot", move.Slot.ToString().ToLowerInvariant());
                w.WriteString("text", move.Text);
                w.WriteBoolean("command", move.Command);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void View(Utf8JsonWriter w, WebGame game)
    {
        var s = game.S;
        int actor = game.Actor;
        w.WriteNumber("v", game.Inputs.Count);
        w.WriteNumber("round", s.Round);
        w.WriteString("phase", Rules.PhaseName(s.Phase));
        w.WriteString("outcome", s.Outcome switch
        {
            Outcome.PartyWon => "won",
            Outcome.PartyLost => "lost",
            Outcome.Draw => "draw",
            Outcome.PartyFled => "fled",
            _ => "ongoing",
        });
        w.WriteNumber("actor", actor);
        w.WriteNumber("partner", actor >= 0 ? Rules.SwapTarget(s, actor) : -1);

        w.WriteStartObject("board");
        WriteInts(w, "height", s.Heights.Select(h => (int)h));
        WriteInts(w, "terrain", s.Terrain.Select(t => (int)t));
        WriteInts(w, "feature", s.Feature);
        WriteInts(w, "dead", s.FeatureDead.Select(d => d ? 1 : 0));
        WriteInts(w, "uses", s.FeatureUses);
        WriteInts(w, "fhp", s.FeatureHp);
        WriteInts(w, "trap", s.Trap.Select(t => (int)t));
        WriteInts(w, "rumble", s.Rumble);
        w.WriteEndObject();

        w.WriteStartArray("units");
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var u = ref s.Units[i];
            w.WriteStartObject();
            w.WriteNumber("hp", u.Hp);
            w.WriteNumber("max", u.MaxHp);
            w.WriteNumber("x", u.Pos.X);
            w.WriteNumber("y", u.Pos.Y);
            w.WriteBoolean("on", u.OnBoard);
            w.WriteBoolean("alive", u.Alive);
            w.WriteString("gone", u.Alive ? "" : u.Has(UnitFlags.Fled) ? "fled" : u.Has(UnitFlags.KnockedOut) ? "knocked out" : "defeated");
            w.WriteString("status", AsciiRender.Statuses(s, i));
            w.WriteNumber("mount", u.MountedOn);
            w.WriteNumber("rider", u.Rider);
            if (u.Has(UnitFlags.Burrowed)) WritePos(w, "burrow", u.BurrowTo);
            if (u.Side == Side.Party)
            {
                w.WriteString("walk", Tiles(u.MoveLeft));
                w.WriteBoolean("main", !u.Has(UnitFlags.MainUsed));
                w.WriteBoolean("bonus", !u.Has(UnitFlags.BonusUsed));
                w.WriteBoolean("open", Rules.PartyTurnOpen(s, i));
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("tethers");
        for (int t = 0; t < s.TetherCount; t++)
        {
            var tether = s.Tethers[t];
            if (!tether.Active) continue;
            w.WriteStartObject();
            WritePos(w, "a", tether.UnitA >= 0 ? s.Units[tether.UnitA].Pos : tether.TileA);
            WritePos(w, "b", tether.UnitB >= 0 ? s.Units[tether.UnitB].Pos : tether.TileB);
            w.WriteNumber("ua", tether.UnitA);
            w.WriteNumber("ub", tether.UnitB);
            w.WriteString("variant", tether.Variant.ToString().ToLowerInvariant());
            w.WriteNumber("rounds", (tether.Timer + 1) / 2);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        if (game.Pending is BattleAction pending)
        {
            w.WriteStartObject("pending");
            w.WriteString("need", game.PendingNeed == TimingNeed.Attack ? "attack" : "guard");
            w.WriteString("move", Describe.MoveName(s, pending.Move));
            w.WriteNumber("actor", pending.Actor);
            w.WriteNumber("target", pending.Target.Kind == TargetKind.Unit ? pending.Target.Unit : -1);
            w.WriteString("text", Describe.Action(s, pending));
            w.WriteEndObject();
        }

        if (game.PartyToAct) Choices(w, game, actor);
    }

    /// <summary>The acting party unit's choices: where it can walk, and its other actions grouped by move.</summary>
    private static void Choices(Utf8JsonWriter w, WebGame game, int actor)
    {
        var s = game.S;
        var legal = game.Legal();

        w.WriteStartArray("walk");
        bool reach = false;
        for (int i = 0; i < legal.Count; i++)
        {
            if (legal[i].Kind != ActionKind.Move) continue;
            if (!reach)
            {
                Rules.ComputeReach(s, actor, s.Units[actor].MoveLeft);
                reach = true;
            }
            var to = legal[i].Target.Tile;
            w.WriteStartObject();
            w.WriteNumber("i", i);
            w.WriteNumber("x", to.X);
            w.WriteNumber("y", to.Y);
            w.WriteString("cost", Tiles(Rules.ReachCost(to)));
            w.WriteStartArray("path");
            foreach (var p in Rules.PathTo(s, to)) WritePosValue(w, p);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // The other actions, grouped by move in the order the rules list them.
        var groups = new List<(string Key, List<int> Options)>();
        for (int i = 0; i < legal.Count; i++)
        {
            var a = legal[i];
            if (a.Kind is ActionKind.Move or ActionKind.End) continue;
            string key = a.Kind is ActionKind.Main or ActionKind.Bonus or ActionKind.Special ? "move:" + a.Move : a.Kind.ToString();
            int g = groups.FindIndex(x => x.Key == key);
            if (g < 0) groups.Add((key, new List<int> { i }));
            else groups[g].Options.Add(i);
        }
        w.WriteStartArray("groups");
        foreach (var (_, options) in groups)
        {
            var first = legal[options[0]];
            w.WriteStartObject();
            switch (first.Kind)
            {
                case ActionKind.SwapPartner:
                    w.WriteString("kind", "swap");
                    w.WriteString("label", $"Swap to the {Describe.Name(s, first.Target.Unit)}");
                    break;
                case ActionKind.Flee:
                    w.WriteString("kind", "flee");
                    w.WriteString("label", "Flee the battle");
                    break;
                case ActionKind.Mount:
                    w.WriteString("kind", "mount");
                    w.WriteString("label", "Mount");
                    break;
                case ActionKind.Dismount:
                    w.WriteString("kind", "dismount");
                    w.WriteString("label", "Dismount");
                    break;
                default:
                    var move = s.Data.Moves[first.Move];
                    w.WriteString("kind", "move");
                    w.WriteString("label", move.Name);
                    w.WriteString("slot", move.Slot.ToString().ToLowerInvariant());
                    w.WriteString("text", move.Text);
                    w.WriteBoolean("command", move.Command);
                    break;
            }
            w.WriteStartArray("options");
            foreach (int i in options)
            {
                var a = legal[i];
                w.WriteStartObject();
                w.WriteNumber("i", i);
                w.WriteString("text", Describe.Action(s, a));
                WriteTarget(w, "t", a.Target);
                WriteTarget(w, "t2", a.Target2);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();

        int end = legal.FindIndex(a => a.Kind == ActionKind.End);
        w.WriteNumber("end", end);
        if (s.Info.Kits[actor].Any(m => s.Data.Moves[m].Element == Element.Root)) w.WriteString("sources", RootSources(s, actor));
    }

    /// <summary>The Root features this unit can draw on, worded as in the terminal game.</summary>
    private static string RootSources(BattleState s, int actor)
    {
        var at = s.Units[actor].Pos;
        var sources = new List<string>();
        for (int tile = 0; tile < s.TileCount; tile++)
        {
            if (!Rules.Feeds(s, tile, Element.Root) || s.PosOf(tile).Manhattan(at) > s.Data.Rules.SourceRange) continue;
            var f = s.FeatureAt(tile)!;
            sources.Add($"{f.Name.ToLowerInvariant()} {s.PosOf(tile)} {s.FeatureUses[tile]} uses, {s.FeatureHp[tile]} HP");
        }
        return sources.Count > 0
            ? "Root sources in reach: " + string.Join("; ", sources)
            : "No Root source in reach: Bind will only slow and Tether lasts a round";
    }

    private static void Event(Utf8JsonWriter w, BattleState s, in BattleEvent e)
    {
        w.WriteStartObject();
        w.WriteNumber("seq", e.Seq);
        w.WriteNumber("round", e.Round);
        w.WriteString("phase", Rules.PhaseName(e.Phase));
        w.WriteString("type", e.Type.ToString());
        if (Describe.Event(s, e) is string text) w.WriteString("text", text);
        w.WriteNumber("a", e.Actor);
        w.WriteNumber("t", e.Target);
        if (!e.From.IsNone) WritePos(w, "from", e.From);
        if (!e.To.IsNone) WritePos(w, "to", e.To);
        w.WriteNumber("n", e.Amount);
        w.WriteNumber("hp", e.HpAfter);
        if (e.Timing != Timing.None) w.WriteString("timing", e.Timing.ToString().ToLowerInvariant());
        if (e.Detail != null)
        {
            if (e.Type == EventType.Move && e.Detail.StartsWith("path:", StringComparison.Ordinal))
            {
                w.WriteStartArray("path");
                foreach (string step in e.Detail[5..].Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var xy = step.Split(',');
                    w.WriteStartArray();
                    w.WriteNumberValue(int.Parse(xy[0]));
                    w.WriteNumberValue(int.Parse(xy[1]));
                    w.WriteEndArray();
                }
                w.WriteEndArray();
            }
            else w.WriteString("detail", e.Detail);
        }
        w.WriteEndObject();
    }

    public static string Danger(BattleState s, int unit) => Json(w =>
    {
        var marked = unit < 0 ? Observation.Danger(s, Side.Party) : Observation.DangerOf(s, unit);
        w.WriteNumber("unit", unit);
        w.WriteStartArray("tiles");
        for (int i = 0; i < marked.Length; i++)
            if (marked[i]) w.WriteNumberValue(i);
        w.WriteEndArray();
    });

    public static string Error(string message, string? detail = null) => Json(w =>
    {
        w.WriteString("error", message);
        if (detail != null) w.WriteString("detail", detail);
    });

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string Tiles(int halfPoints) => halfPoints == 2 ? "1 tile" : $"{halfPoints / 2.0:0.#} tiles";

    private static void WriteTarget(Utf8JsonWriter w, string key, Target t)
    {
        if (t.Kind == TargetKind.None) return;
        w.WriteStartObject(key);
        switch (t.Kind)
        {
            case TargetKind.Unit:
                w.WriteString("k", "unit");
                w.WriteNumber("u", t.Unit);
                break;
            case TargetKind.Tile:
                w.WriteString("k", "tile");
                w.WriteNumber("x", t.Tile.X);
                w.WriteNumber("y", t.Tile.Y);
                break;
            case TargetKind.Self:
                w.WriteString("k", "self");
                break;
            case TargetKind.Weapon:
                w.WriteString("k", "weapon");
                break;
        }
        w.WriteEndObject();
    }

    private static void WritePos(Utf8JsonWriter w, string key, Pos p)
    {
        w.WritePropertyName(key);
        WritePosValue(w, p);
    }

    private static void WritePosValue(Utf8JsonWriter w, Pos p)
    {
        w.WriteStartArray();
        w.WriteNumberValue(p.X);
        w.WriteNumberValue(p.Y);
        w.WriteEndArray();
    }

    private static void WriteInts(Utf8JsonWriter w, string key, IEnumerable<int> values)
    {
        w.WriteStartArray(key);
        foreach (int v in values) w.WriteNumberValue(v);
        w.WriteEndArray();
    }

    private static string Json(Action<Utf8JsonWriter> body)
    {
        var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

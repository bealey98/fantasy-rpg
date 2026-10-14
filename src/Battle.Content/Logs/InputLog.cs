using System.Text;
using System.Text.Json;
using Battle.Core;

namespace Battle.Content.Logs;

/// <summary>The first line of an input log: everything needed to rebuild the battle before replaying it.</summary>
public sealed record InputLogHeader(
    int Format,
    string Rules,
    string Content,
    string Encounter,
    string Arena,
    ulong Seed,
    int BattleId,
    string PartyController,
    string EnemyController,
    string Timing);

/// <summary>
/// Input logs: JSON Lines with a header, then one line per input. A seed plus its inputs replays a
/// battle exactly. The header pins the rules version and a hash of the content files, so a replay
/// can tell when it no longer matches the current game.
///
///   {"format":1,"rules":"0.1.0","content":"sha256:…","encounter":"first-goblins","arena":"goblin-glade","seed":7,...}
///   {"round":1,"actor":"hero","kind":"move","to":[4,2]}
///   {"round":1,"actor":"hero","kind":"main","move":"strike","target":"b","timing":"perfect"}
///   {"round":1,"actor":"hero","kind":"end"}
/// </summary>
public static class InputLog
{
    public const int Format = 1;

    public static void Write(TextWriter writer, BattleState s, string partyController, string enemyController,
        IEnumerable<(int Round, BattleAction Action)> inputs, string timing = "")
    {
        writer.WriteLine(Json(w =>
        {
            w.WriteNumber("format", Format);
            w.WriteString("rules", RulesVersion.Current);
            w.WriteString("content", s.Data.ContentHash);
            w.WriteString("encounter", s.Info.EncounterId);
            w.WriteString("arena", s.Info.ArenaName);
            w.WriteNumber("seed", s.Info.Seed);
            w.WriteNumber("battle_id", s.Info.BattleId);
            w.WriteStartObject("controllers");
            w.WriteString("party", partyController);
            w.WriteString("enemies", enemyController);
            w.WriteEndObject();
            if (timing.Length > 0) w.WriteString("timing", timing);
        }));
        foreach (var (round, action) in inputs) writer.WriteLine(Line(s, round, action));
    }

    public static string Line(BattleState s, int round, BattleAction a) => Json(w =>
    {
        w.WriteNumber("round", round);
        w.WriteString("actor", s.Label(a.Actor));
        w.WriteString("kind", KindName(a.Kind));
        if (a.Move >= 0) w.WriteString("move", s.Data.Moves[a.Move].Id);
        if (a.Kind is ActionKind.Move or ActionKind.Dismount) WriteTile(w, "to", a.Target.Tile);
        else if (a.Target.Kind != TargetKind.None) WriteTarget(w, s, "target", a.Target);
        if (a.Target2.Kind != TargetKind.None) WriteTarget(w, s, "target2", a.Target2);
        if (a.Timing != Timing.None) w.WriteString("timing", a.Timing.ToString().ToLowerInvariant());
    });

    public static (InputLogHeader Header, List<JsonElement> Lines) Read(string path)
    {
        var lines = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0) throw new ContentException($"{path}: empty input log");
        var head = JsonDocument.Parse(lines[0]).RootElement;
        var controllers = head.GetProperty("controllers");
        var header = new InputLogHeader(
            head.GetProperty("format").GetInt32(),
            head.GetProperty("rules").GetString() ?? "",
            head.GetProperty("content").GetString() ?? "",
            head.GetProperty("encounter").GetString() ?? "",
            head.GetProperty("arena").GetString() ?? "",
            head.GetProperty("seed").GetUInt64(),
            head.GetProperty("battle_id").GetInt32(),
            controllers.GetProperty("party").GetString() ?? "",
            controllers.GetProperty("enemies").GetString() ?? "",
            head.TryGetProperty("timing", out var timing) ? timing.GetString() ?? "" : "");
        if (header.Format != Format) throw new ContentException($"{path}: input log format {header.Format}, but this build reads format {Format}");
        return (header, lines.Skip(1).Select(l => JsonDocument.Parse(l).RootElement).ToList());
    }

    /// <summary>Turns logged lines back into actions, resolving labels and move ids against the battle.</summary>
    public static List<BattleAction> ToActions(BattleState s, IEnumerable<JsonElement> lines)
    {
        var actions = new List<BattleAction>();
        int lineNo = 1;
        foreach (var line in lines)
        {
            lineNo++;
            string actorLabel = line.GetProperty("actor").GetString() ?? "";
            int actor = s.Info.UnitIndex(actorLabel);
            if (actor < 0) throw new ContentException($"input log line {lineNo}: no unit called '{actorLabel}'");
            var kind = ParseKind(line.GetProperty("kind").GetString() ?? "", lineNo);
            int move = -1;
            if (line.TryGetProperty("move", out var moveEl))
                move = s.Data.MoveById(moveEl.GetString() ?? "")?.Index ?? throw new ContentException($"input log line {lineNo}: unknown move '{moveEl.GetString()}'");
            var target = Target.None;
            if (line.TryGetProperty("to", out var toEl)) target = Target.OfTile(ReadTile(toEl));
            else if (line.TryGetProperty("target", out var tEl)) target = ReadTarget(s, tEl, lineNo);
            var target2 = line.TryGetProperty("target2", out var t2El) ? ReadTarget(s, t2El, lineNo) : Target.None;
            var timing = line.TryGetProperty("timing", out var timingEl)
                ? Enum.Parse<Timing>(timingEl.GetString() ?? "None", ignoreCase: true)
                : Timing.None;
            actions.Add(new BattleAction(kind, actor, move, target, target2, timing));
        }
        return actions;
    }

    public static string KindName(ActionKind kind) => kind switch
    {
        ActionKind.Move => "move",
        ActionKind.Main => "main",
        ActionKind.Bonus => "bonus",
        ActionKind.Special => "special",
        ActionKind.Mount => "mount",
        ActionKind.Dismount => "dismount",
        ActionKind.SwapPartner => "swap_partner",
        ActionKind.Flee => "flee",
        _ => "end",
    };

    private static ActionKind ParseKind(string name, int lineNo) => name switch
    {
        "move" => ActionKind.Move,
        "main" => ActionKind.Main,
        "bonus" => ActionKind.Bonus,
        "special" => ActionKind.Special,
        "mount" => ActionKind.Mount,
        "dismount" => ActionKind.Dismount,
        "swap_partner" => ActionKind.SwapPartner,
        "flee" => ActionKind.Flee,
        "end" => ActionKind.End,
        _ => throw new ContentException($"input log line {lineNo}: unknown kind '{name}'"),
    };

    private static void WriteTarget(Utf8JsonWriter w, BattleState s, string key, Target t)
    {
        switch (t.Kind)
        {
            case TargetKind.Unit: w.WriteString(key, s.Label(t.Unit)); break;
            case TargetKind.Tile: WriteTile(w, key, t.Tile); break;
            case TargetKind.Self: w.WriteString(key, "self"); break;
            case TargetKind.Weapon: w.WriteString(key, "weapon"); break;
        }
    }

    private static void WriteTile(Utf8JsonWriter w, string key, Pos p)
    {
        w.WriteStartArray(key);
        w.WriteNumberValue(p.X);
        w.WriteNumberValue(p.Y);
        w.WriteEndArray();
    }

    private static Target ReadTarget(BattleState s, JsonElement el, int lineNo)
    {
        if (el.ValueKind == JsonValueKind.Array) return Target.OfTile(ReadTile(el));
        string text = el.GetString() ?? "";
        if (text == "self") return Target.Self;
        if (text == "weapon") return Target.Weapon;
        int unit = s.Info.UnitIndex(text);
        return unit >= 0 ? Target.OfUnit(unit) : throw new ContentException($"input log line {lineNo}: no unit called '{text}'");
    }

    private static Pos ReadTile(JsonElement el)
    {
        var items = el.EnumerateArray().Select(e => e.GetInt32()).ToArray();
        return new Pos(items[0], items[1]);
    }

    internal static string Json(Action<Utf8JsonWriter> body)
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

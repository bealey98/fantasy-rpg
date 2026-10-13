using System.Text.Json;
using Battle.Core;

namespace Battle.Content;

/// <summary>
/// Reads content/moves.json: an array of moves. Comments are allowed, and unknown keys are errors.
/// Ranges are whole tiles; the "amount" of a Scamper is movement in tiles (halves allowed).
/// </summary>
public static class MovesLoader
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "id", "name", "slot", "kind", "text", "damage", "range", "height_bonus", "needs_sight", "command",
        "push", "charge", "status", "status_rounds", "self_status", "self_status_rounds", "gang_up",
        "element", "source", "dark_cost", "rounds", "radius", "amount", "mount", "variant", "tier", "upgraded",
    };

    public static List<MoveDef> Load(string path)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(path), options); }
        catch (JsonException e) { throw new ContentException($"moves.json: {e.Message}"); }
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new ContentException("moves.json: should be a list of moves");

        var moves = new List<MoveDef>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
            string where = $"moves.json, move '{id}'";
            foreach (var prop in item.EnumerateObject())
                if (!Known.Contains(prop.Name)) throw new ContentException($"{where}: unknown key '{prop.Name}'");

            string Str(string key, string fallback = "") =>
                item.TryGetProperty(key, out var v) ? v.GetString() ?? fallback : fallback;
            int Num(string key, int fallback = 0) =>
                item.TryGetProperty(key, out var v) ? RulesLoader.Int(v, $"{where}: '{key}'") : fallback;
            bool Flag(string key) =>
                item.TryGetProperty(key, out var v) && RulesLoader.Bool(v, $"{where}: '{key}'");

            if (id.Length == 0) throw new ContentException("moves.json: every move needs an id");
            var kind = ParseEnum<MoveKind>(Str("kind"), where, "kind");
            moves.Add(new MoveDef
            {
                Index = moves.Count,
                Id = id,
                Name = Str("name", id),
                Slot = ParseEnum<MoveSlot>(Str("slot"), where, "slot"),
                Kind = kind,
                Text = Str("text"),
                Damage = Num("damage"),
                Range = Num("range", 1),
                HeightBonus = Flag("height_bonus"),
                NeedsSight = Flag("needs_sight"),
                Command = Flag("command"),
                Push = Num("push"),
                Charge = Num("charge"),
                Status = ParseEnum<StatusId>(Str("status", "none"), where, "status"),
                StatusRounds = Num("status_rounds", 1),
                SelfStatus = ParseEnum<StatusId>(Str("self_status", "none"), where, "self_status"),
                SelfStatusRounds = Num("self_status_rounds", 1),
                GangUp = Num("gang_up"),
                Element = Str("element") switch
                {
                    "" or "none" => Element.None,
                    "root" => Element.Root,
                    var other => throw new ContentException($"{where}: element '{other}' isn't in M1 yet"),
                },
                Source = ParseEnum<SourceKind>(Str("source", "none"), where, "source"),
                DarkCost = Num("dark_cost"),
                Rounds = Num("rounds", 1),
                Radius = Num("radius"),
                Amount = kind == MoveKind.Scamper && item.TryGetProperty("amount", out var a) ? RulesLoader.Tiles(a, $"{where}: 'amount'") : Num("amount"),
                Mount = ParseEnum<MountRule>(Str("mount", "any"), where, "mount"),
                Variant = ParseEnum<TetherVariant>(Str("variant", "plain"), where, "variant"),
                Tier = Num("tier"),
                Upgraded = Flag("upgraded"),
            });
        }
        ContentLoader.CheckUnique(moves.Select(m => (m.Id, m.Id)), "moves.json");
        return moves;
    }

    /// <summary>Reads a snake_case word ("knocked_over") as an enum value (KnockedOver).</summary>
    internal static T ParseEnum<T>(string raw, string where, string key) where T : struct, Enum
    {
        string pascal = string.Concat(raw.Split('_').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
        if (raw.Length > 0 && Enum.TryParse<T>(pascal, ignoreCase: false, out var value)) return value;
        string options = string.Join(", ", Enum.GetNames<T>().Select(Snake));
        throw new ContentException($"{where}: {key} should be one of {options}, got '{raw}'");
    }

    /// <summary>"KnockedOver" → "knocked_over".</summary>
    public static string Snake(string pascal) =>
        string.Concat(pascal.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}

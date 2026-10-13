using System.Text.Json;
using Battle.Core;

namespace Battle.Content;

/// <summary>
/// Reads content/rules.json. Comments (// ...) are allowed. Every key must be one the loader
/// knows, so a typo is an error rather than a silently ignored setting.
/// </summary>
public static class RulesLoader
{
    private delegate void Setter(RulesConfig rules, JsonElement value, string key);

    private static readonly Dictionary<string, Setter> Setters = new(StringComparer.Ordinal)
    {
        ["climb_cost"] = (r, v, k) => r.ClimbCost = Tiles(v, k),
        ["ladder_cost_per_level"] = (r, v, k) => r.LadderCostPerLevel = Tiles(v, k),
    };

    public static RulesConfig Load(string path)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(path), options); }
        catch (JsonException e) { throw new ContentException($"rules.json: {e.Message}"); }

        var rules = new RulesConfig();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!Setters.TryGetValue(prop.Name, out var set))
                throw new ContentException($"rules.json: unknown setting '{prop.Name}'");
            set(rules, prop.Value, prop.Name);
        }
        return rules;
    }

    internal static int Int(JsonElement v, string key) =>
        v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n
            : throw new ContentException($"rules.json: '{key}' should be a whole number");

    internal static bool Bool(JsonElement v, string key) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new ContentException($"rules.json: '{key}' should be true or false"),
    };

    /// <summary>A distance in tiles (halves allowed), returned in half-points.</summary>
    internal static int Tiles(JsonElement v, string key)
    {
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out decimal tiles))
            throw new ContentException($"rules.json: '{key}' should be a number of tiles");
        decimal halves = tiles * 2;
        if (halves != decimal.Truncate(halves))
            throw new ContentException($"rules.json: '{key}' should be a whole or half number of tiles");
        return (int)halves;
    }
}

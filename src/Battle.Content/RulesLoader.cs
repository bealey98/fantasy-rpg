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
        ["melee_max_height_diff"] = (r, v, k) => r.MeleeMaxHeightDiff = Int(v, k),
        ["round_cap"] = (r, v, k) => r.RoundCap = Int(v, k),
        ["timing_skill"] = (r, v, k) => r.TimingSkill = TimingSkill(v, k),
    };

    private static Dictionary<string, int[]> TimingSkill(JsonElement v, string key)
    {
        if (v.ValueKind != JsonValueKind.Object) throw new ContentException($"rules.json: '{key}' should be an object of skill levels");
        var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var level in v.EnumerateObject())
        {
            int[] chances = level.Value.ValueKind == JsonValueKind.Array
                ? level.Value.EnumerateArray().Select(e => Int(e, $"{key}.{level.Name}")).ToArray()
                : throw new ContentException($"rules.json: '{key}.{level.Name}' should be [fumble, good, perfect]");
            if (chances.Length != 3 || chances.Sum() != 100 || chances.Any(c => c < 0))
                throw new ContentException($"rules.json: '{key}.{level.Name}' should be three chances that add up to 100");
            result[level.Name] = chances;
        }
        return result;
    }

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

    // The helpers take a "where" naming the file and key, e.g. "rules.json: 'round_cap'".
    private static string Where(string key) => key.Contains(':') ? key : $"rules.json: '{key}'";

    internal static int Int(JsonElement v, string key) =>
        v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n
            : throw new ContentException($"{Where(key)} should be a whole number");

    internal static bool Bool(JsonElement v, string key) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new ContentException($"{Where(key)} should be true or false"),
    };

    /// <summary>A distance in tiles (halves allowed), returned in half-points.</summary>
    internal static int Tiles(JsonElement v, string key)
    {
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out decimal tiles))
            throw new ContentException($"{Where(key)} should be a number of tiles");
        decimal halves = tiles * 2;
        if (halves != decimal.Truncate(halves))
            throw new ContentException($"{Where(key)} should be a whole or half number of tiles");
        return (int)halves;
    }
}

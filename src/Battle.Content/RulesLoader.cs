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
        ["knocked_over_drain"] = (r, v, k) => r.KnockedOverDrain = Tiles(v, k),
        ["slow_amount"] = (r, v, k) => r.SlowAmount = Tiles(v, k),
        ["attack_bonus"] = (r, v, k) => r.AttackBonus = GradeTable(v, k),
        ["guard_reduction"] = (r, v, k) => r.GuardReduction = GradeTable(v, k),
        ["collision_damage"] = (r, v, k) => r.CollisionDamage = Int(v, k),
        ["unit_collision_damage"] = (r, v, k) => r.UnitCollisionDamage = Int(v, k),
        ["fall_damage_per_level"] = (r, v, k) => r.FallDamagePerLevel = Int(v, k),
        ["edges_are_walls"] = (r, v, k) => r.EdgesAreWalls = Bool(v, k),
        ["source_range"] = (r, v, k) => r.SourceRange = Int(v, k),
        ["tether_max_length"] = (r, v, k) => r.TetherMaxLength = Int(v, k),
        ["goblins_flee"] = (r, v, k) => r.FleeEnabled = Bool(v, k),
    };

    /// <summary>{"fumble": 0, "good": 1, "perfect": 2} as [0, 1, 2].</summary>
    private static int[] GradeTable(JsonElement v, string key)
    {
        if (v.ValueKind != JsonValueKind.Object) throw new ContentException($"rules.json: '{key}' should be {{\"fumble\": n, \"good\": n, \"perfect\": n}}");
        int Grade(string name) => v.TryGetProperty(name, out var g) ? Int(g, $"{key}.{name}")
            : throw new ContentException($"rules.json: '{key}' is missing '{name}'");
        return [Grade("fumble"), Grade("good"), Grade("perfect")];
    }

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

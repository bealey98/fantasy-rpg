using System.Security.Cryptography;
using System.Text;
using Battle.Core;

namespace Battle.Content;

/// <summary>
/// Loads everything in the content folder into a GameData, checking it for mistakes as it goes.
/// </summary>
public static class ContentLoader
{
    /// <summary>
    /// Finds the content folder by walking up from the current directory, so the tools work
    /// from the repository root or any folder inside it.
    /// </summary>
    public static string FindContentDir(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "content");
            if (File.Exists(Path.Combine(candidate, "terrain.csv"))) return candidate;
            dir = dir.Parent;
        }
        // Fall back to the folder next to the running program (useful for tests run from bin/).
        dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "content");
            if (File.Exists(Path.Combine(candidate, "terrain.csv"))) return candidate;
            dir = dir.Parent;
        }
        throw new ContentException("couldn't find the content folder; run from inside the repository or pass --content");
    }

    public static GameData Load(string contentDir)
    {
        var terrain = LoadTerrain(Path.Combine(contentDir, "terrain.csv"));
        var features = LoadFeatures(Path.Combine(contentDir, "features.csv"));
        var units = LoadUnits(Path.Combine(contentDir, "units.csv"));
        var rules = RulesLoader.Load(Path.Combine(contentDir, "rules.json"));
        return new GameData(terrain, features, units, rules, HashContent(contentDir));
    }

    /// <summary>
    /// Reads a distance in tiles, where halves are allowed ("1.5"), and returns it in half-points.
    /// </summary>
    internal static int Tiles(CsvRow row, string column)
    {
        string raw = row.Get(column);
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal tiles))
            throw new ContentException($"{row.Where}: '{column}' should be a number of tiles, got '{raw}'");
        decimal halves = tiles * 2;
        if (halves != decimal.Truncate(halves))
            throw new ContentException($"{row.Where}: '{column}' should be a whole or half number of tiles, got '{raw}'");
        return (int)halves;
    }

    private static List<UnitDef> LoadUnits(string path)
    {
        var list = new List<UnitDef>();
        foreach (var row in Csv.Read(path))
        {
            list.Add(new UnitDef
            {
                Index = list.Count,
                Id = row.Get("id"),
                Name = row.Get("name"),
                Side = row.Get("side") switch
                {
                    "party" => Side.Party,
                    "enemy" => Side.Enemy,
                    var other => throw new ContentException($"{row.Where}: side should be party or enemy, got '{other}'"),
                },
                Hp = row.Int("hp"),
                Move = Tiles(row, "move"),
                Weight = row.Get("weight") switch
                {
                    "light" => Weight.Light,
                    "medium" => Weight.Medium,
                    "heavy" => Weight.Heavy,
                    var other => throw new ContentException($"{row.Where}: weight should be light, medium or heavy, got '{other}'"),
                },
                Defence = row.Int("defence"),
                Power = row.Int("power"),
                Tags = ParseTags(row),
                Moves = row.GetOr("moves", "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            });
            if (list[^1].Hp < 1) throw new ContentException($"{row.Where}: hp must be at least 1");
        }
        CheckUnique(list.Select(u => (u.Id, u.Id)), "units.csv");
        foreach (string required in new[] { "hero", "loper" })
            if (!list.Any(u => u.Id == required)) throw new ContentException($"units.csv: there must be a unit called '{required}'");
        return list;
    }

    private static UnitTags ParseTags(CsvRow row)
    {
        var tags = UnitTags.None;
        foreach (string part in row.Get("tags").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            tags |= part switch
            {
                "hero" => UnitTags.Hero,
                "companion" => UnitTags.Companion,
                "humanoid" => UnitTags.Humanoid,
                "beast" => UnitTags.Beast,
                "goblin" => UnitTags.Goblin,
                _ => throw new ContentException($"{row.Where}: unknown tag '{part}'"),
            };
        }
        return tags;
    }

    public static ArenaDef LoadArena(string contentDir, string nameOrPath, GameData data)
    {
        string path = File.Exists(nameOrPath) ? nameOrPath : Path.Combine(contentDir, "arenas", nameOrPath + ".txt");
        if (!File.Exists(path)) throw new ContentException($"no arena called '{nameOrPath}' (looked for {path})");
        return ArenaParser.Parse(File.ReadAllText(path), Path.GetFileName(path), data);
    }

    private static List<TerrainDef> LoadTerrain(string path)
    {
        var list = new List<TerrainDef>();
        foreach (var row in Csv.Read(path))
        {
            list.Add(new TerrainDef
            {
                Index = list.Count,
                Glyph = row.Char("glyph"),
                Id = row.Get("id"),
                Name = row.Get("name"),
                MoveCost = Tiles(row, "move_cost"),
                Walkable = row.Bool("walkable"),
                NoGround = row.Bool("no_ground"),
                Ladder = row.Bool("ladder"),
                Water = row.Bool("water"),
                Cover = ParseCover(row, "cover"),
                IsObject = row.Bool("object"),
            });
            if (list[^1].MoveCost < 1) throw new ContentException($"{row.Where}: move_cost must be at least half a tile");
        }
        CheckUnique(list.Select(t => (t.Glyph.ToString(), t.Id)), "terrain.csv");
        if (!list.Any(t => t.NoGround)) throw new ContentException("terrain.csv: one terrain must be no ground");
        return list;
    }

    private static List<FeatureDef> LoadFeatures(string path)
    {
        var list = new List<FeatureDef>();
        foreach (var row in Csv.Read(path))
        {
            list.Add(new FeatureDef
            {
                Index = list.Count,
                Glyph = row.Char("glyph"),
                Id = row.Get("id"),
                Name = row.Get("name"),
                Feeds = ParseElements(row, "feeds"),
                Blocks = row.Bool("blocks"),
                Cover = ParseCover(row, "cover"),
                IsObject = row.Bool("object"),
                ExtraMoveCost = Tiles(row, "extra_move_cost"),
                Uses = row.Int("uses"),
                Regen = row.Int("regen"),
                Hp = row.Int("hp"),
                Creature = row.Get("creature") switch
                {
                    "none" => CreatureBehaviour.None,
                    "drift" => CreatureBehaviour.Drift,
                    "flee_attack" => CreatureBehaviour.FleeAttack,
                    "flee_near" => CreatureBehaviour.FleeNear,
                    var other => throw new ContentException($"{row.Where}: creature should be none, drift, flee_attack or flee_near, got '{other}'"),
                },
            });
            var f = list[^1];
            if (f.IsCreature && f.Blocks) throw new ContentException($"{row.Where}: creatures move, so they can't block movement");
        }
        CheckUnique(list.Select(f => (f.Glyph.ToString(), f.Id)), "features.csv");
        return list;
    }

    internal static Cover ParseCover(CsvRow row, string column) => row.Get(column) switch
    {
        "none" => Cover.None,
        "low" => Cover.Low,
        "high" => Cover.High,
        var other => throw new ContentException($"{row.Where}: {column} should be none, low or high, got '{other}'"),
    };

    internal static Element ParseElements(CsvRow row, string column)
    {
        var result = Element.None;
        foreach (string part in row.Get(column).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= part switch
            {
                "root" => Element.Root,
                "echo" => Element.Echo,
                "kinship" => Element.Kinship,
                "wind" => Element.Wind,
                "light" => Element.Light,
                "darkness" => Element.Darkness,
                "none" => Element.None,
                _ => throw new ContentException($"{row.Where}: unknown element '{part}' in {column}"),
            };
        }
        return result;
    }

    internal static void CheckUnique(IEnumerable<(string Glyph, string Id)> items, string file)
    {
        var glyphs = new HashSet<string>();
        var ids = new HashSet<string>();
        foreach (var (glyph, id) in items)
        {
            if (!glyphs.Add(glyph)) throw new ContentException($"{file}: glyph '{glyph}' is used twice");
            if (!ids.Add(id)) throw new ContentException($"{file}: id '{id}' is used twice");
        }
    }

    /// <summary>
    /// A short hash of every file in the content folder. Line endings are normalised first,
    /// so a Windows checkout hashes the same as a Linux one.
    /// </summary>
    public static string HashContent(string contentDir)
    {
        using var sha = SHA256.Create();
        var files = Directory.GetFiles(contentDir, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: Path.GetRelativePath(contentDir, f).Replace('\\', '/')))
            .Where(f => !f.Rel.EndsWith(".md", StringComparison.Ordinal))
            .OrderBy(f => f.Rel, StringComparer.Ordinal);
        var all = new MemoryStream();
        foreach (var (full, rel) in files)
        {
            byte[] name = Encoding.UTF8.GetBytes(rel + "\n");
            byte[] body = Encoding.UTF8.GetBytes(File.ReadAllText(full).Replace("\r\n", "\n"));
            all.Write(name);
            all.Write(body);
        }
        byte[] hash = sha.ComputeHash(all.ToArray());
        return "sha256:" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}

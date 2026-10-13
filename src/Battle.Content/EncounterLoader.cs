using System.Text.Json;
using Battle.Core;

namespace Battle.Content;

/// <summary>
/// Reads content/encounters/*.json. An encounter names an arena, says who each enemy letter is,
/// and sets up the party:
///
///   {
///     "arena": "goblin-glade",
///     "enemies": { "a": "cutter", "b": "plinker" },
///     "handlers": { "s": "d" },
///     "party": { "weapon_main": ["strike", "bull_rush"], "weapon_bonus": "shove",
///                "elderwood_tier": 1, "loper": true, "mounted": false, "loper_first": false },
///     "first_phase": "party"
///   }
/// </summary>
public static class EncounterLoader
{
    private static readonly HashSet<string> TopKeys = new(StringComparer.Ordinal)
        { "id", "arena", "enemies", "handlers", "party", "first_phase", "notes" };

    private static readonly HashSet<string> PartyKeys = new(StringComparer.Ordinal)
        { "weapon_main", "weapon_bonus", "elderwood_tier", "loper", "mounted", "loper_first" };

    public static EncounterDef Load(string contentDir, string nameOrPath, GameData data)
    {
        string path = File.Exists(nameOrPath) ? nameOrPath : Path.Combine(contentDir, "encounters", nameOrPath + ".json");
        if (!File.Exists(path)) throw new ContentException($"no encounter called '{nameOrPath}' (looked for {path})");
        string file = Path.GetFileName(path);

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(path), options); }
        catch (JsonException e) { throw new ContentException($"{file}: {e.Message}"); }
        var root = doc.RootElement;
        foreach (var prop in root.EnumerateObject())
            if (!TopKeys.Contains(prop.Name)) throw new ContentException($"{file}: unknown key '{prop.Name}'");

        string id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : Path.GetFileNameWithoutExtension(path);
        string arena = root.TryGetProperty("arena", out var arenaEl) ? arenaEl.GetString() ?? "" : throw new ContentException($"{file}: missing 'arena'");

        var enemies = new Dictionary<char, string>();
        if (root.TryGetProperty("enemies", out var enemiesEl))
            foreach (var e in enemiesEl.EnumerateObject())
            {
                if (e.Name.Length != 1 || !char.IsLower(e.Name[0])) throw new ContentException($"{file}: enemy keys should be single lowercase letters, got '{e.Name}'");
                string unit = e.Value.GetString() ?? "";
                if (data.UnitById(unit) is not { Side: Side.Enemy }) throw new ContentException($"{file}: '{unit}' isn't an enemy in units.csv");
                enemies[e.Name[0]] = unit;
            }

        var handlers = new Dictionary<char, char>();
        if (root.TryGetProperty("handlers", out var handlersEl))
            foreach (var h in handlersEl.EnumerateObject())
            {
                string handler = h.Value.GetString() ?? "";
                if (h.Name.Length != 1 || handler.Length != 1) throw new ContentException($"{file}: handlers map one letter to another, like \"s\": \"d\"");
                handlers[h.Name[0]] = handler[0];
            }

        var party = new PartySetup();
        if (root.TryGetProperty("party", out var partyEl))
        {
            foreach (var prop in partyEl.EnumerateObject())
                if (!PartyKeys.Contains(prop.Name)) throw new ContentException($"{file}: unknown party key '{prop.Name}'");
            party = new PartySetup
            {
                WeaponMain = partyEl.TryGetProperty("weapon_main", out var wm) ? wm.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : party.WeaponMain,
                WeaponBonus = partyEl.TryGetProperty("weapon_bonus", out var wb) ? wb.GetString() ?? "" : party.WeaponBonus,
                ElderwoodTier = partyEl.TryGetProperty("elderwood_tier", out var et) ? et.GetInt32() : party.ElderwoodTier,
                Loper = !partyEl.TryGetProperty("loper", out var lo) || lo.GetBoolean(),
                Mounted = partyEl.TryGetProperty("mounted", out var mo) && mo.GetBoolean(),
                LoperFirst = partyEl.TryGetProperty("loper_first", out var lf) && lf.GetBoolean(),
            };
            if (party.WeaponMain.Count > 2) throw new ContentException($"{file}: the hero has two main weapon slots");
            foreach (string move in Kits.HeroLoadout(party))
                if (data.MoveById(move) == null) throw new ContentException($"{file}: the hero's loadout has '{move}', which isn't in moves.json");
        }

        var firstPhase = root.TryGetProperty("first_phase", out var fp) ? (fp.GetString() ?? "party") switch
        {
            "party" => Side.Party,
            "enemy" => Side.Enemy,
            var other => throw new ContentException($"{file}: first_phase should be party or enemy, got '{other}'"),
        } : Side.Party;

        return new EncounterDef
        {
            Id = id,
            Arena = arena,
            Enemies = enemies,
            Handlers = handlers,
            Party = party,
            FirstPhase = firstPhase,
        };
    }

    /// <summary>Loads an encounter and its arena and builds the starting battle state.</summary>
    public static BattleState CreateBattle(string contentDir, GameData data, string encounterName, ulong seed, int battleId = 0)
    {
        var encounter = Load(contentDir, encounterName, data);
        var arena = ContentLoader.LoadArena(contentDir, encounter.Arena, data);
        try { return BattleFactory.Create(data, arena, encounter, seed, battleId); }
        catch (ArgumentException e) { throw new ContentException($"{encounter.Id}: {e.Message}"); }
    }

    public static IEnumerable<string> All(string contentDir) =>
        Directory.GetFiles(Path.Combine(contentDir, "encounters"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.Ordinal);
}

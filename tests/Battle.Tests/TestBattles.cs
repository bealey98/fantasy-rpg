using Battle.Core;

namespace Battle.Tests;

/// <summary>Builds small battles from arenas written inline in tests.</summary>
public static class TestBattles
{
    /// <summary>
    /// A battle on an inline arena. Enemy letters default to Cutters. The Loper joins only if the
    /// arena places one (or the party setup says the hero starts mounted). Goblins don't flee unless
    /// <paramref name="flee"/> is set, so a lone goblin can stand in as a target.
    /// </summary>
    public static BattleState Create(string arenaText, Dictionary<char, string>? enemies = null,
        PartySetup? party = null, ulong seed = 1, Dictionary<char, char>? handlers = null, Side firstPhase = Side.Party,
        bool flee = false)
    {
        var arena = TestContent.Parse(arenaText);
        enemies ??= arena.Units.Where(u => char.IsLower(u.Glyph)).ToDictionary(u => u.Glyph, _ => "cutter");
        bool hasLoper = arena.Units.Any(u => u.Glyph == 'L');
        party ??= new PartySetup { Loper = hasLoper };
        var encounter = new EncounterDef
        {
            Id = "test",
            Arena = arena.Name,
            Enemies = enemies,
            Handlers = handlers ?? new Dictionary<char, char>(),
            Party = party,
            FirstPhase = firstPhase,
        };
        var state = BattleFactory.Create(flee ? TestContent.Data : TestContent.CalmData, arena, encounter, seed);
        state.Sink = new EventList();
        return state;
    }

    public static List<BattleEvent> Events(BattleState s) => ((EventList)s.Sink).Events;

    /// <summary>An open field of base ground with the given units layer rows.</summary>
    public static string Field(int width, int height, params string[] unitRows)
    {
        string row(char c) => new string(c, width);
        var lines = new List<string>
        {
            "arena: field",
            $"size: {width} x {height}",
            "",
            "[height]",
        };
        lines.AddRange(Enumerable.Repeat(row('2'), height));
        lines.Add("");
        lines.Add("[terrain]");
        lines.AddRange(Enumerable.Repeat(row('.'), height));
        lines.Add("");
        lines.Add("[units]");
        lines.AddRange(unitRows);
        return string.Join("\n", lines);
    }
}

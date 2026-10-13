namespace Battle.Core;

/// <summary>
/// One fight: which arena, who the arena's enemy letters are, and how the party is set up.
/// Read from content/encounters/*.json.
/// </summary>
public sealed class EncounterDef
{
    public required string Id { get; init; }
    public required string Arena { get; init; }

    /// <summary>Enemy letter in the arena's units layer → unit id, e.g. 'a' → "cutter".</summary>
    public required IReadOnlyDictionary<char, string> Enemies { get; init; }

    /// <summary>Snuffler letter → its Beastmaster's letter.</summary>
    public IReadOnlyDictionary<char, char> Handlers { get; init; } = new Dictionary<char, char>();

    public PartySetup Party { get; init; } = new();

    /// <summary>Which phase opens round 1. "enemy" when the enemies won the first strike.</summary>
    public Side FirstPhase { get; init; } = Side.Party;
}

/// <summary>The hero's loadout and the companion's starting state for an encounter.</summary>
public sealed class PartySetup
{
    /// <summary>Main weapon moves (two slots).</summary>
    public IReadOnlyList<string> WeaponMain { get; init; } = ["strike"];

    /// <summary>The bonus weapon move (one slot): "shove" or "parry". Empty for none.</summary>
    public string WeaponBonus { get; init; } = "";

    /// <summary>The Elderwood's tier: 1 at the start of the game, 2 after its chapter, 0 for none (tests only).</summary>
    public int ElderwoodTier { get; init; }

    /// <summary>Whether the Loper is in the fight.</summary>
    public bool Loper { get; init; } = true;

    /// <summary>Start the battle with the hero riding the Loper.</summary>
    public bool Mounted { get; init; }

    /// <summary>The Loper acts before the hero.</summary>
    public bool LoperFirst { get; init; }
}

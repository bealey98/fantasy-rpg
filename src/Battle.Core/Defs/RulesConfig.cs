namespace Battle.Core;

/// <summary>
/// Rule numbers that aren't tied to one unit or move, read from content/rules.json.
/// Every value here is a placeholder to tune. Movement values are stored in half-points.
/// Only the content loader sets these; the rules just read them.
/// </summary>
public sealed class RulesConfig
{
    /// <summary>Extra half-points to climb one level up a slope.</summary>
    public int ClimbCost { get; set; } = 2;

    /// <summary>Half-points per level when a ladder takes a unit up or down two or more levels.</summary>
    public int LadderCostPerLevel { get; set; } = 2;
}

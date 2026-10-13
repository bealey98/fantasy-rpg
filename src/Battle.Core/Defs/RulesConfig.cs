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

    /// <summary>Melee attacks reach a neighbouring tile at most this many levels higher or lower.</summary>
    public int MeleeMaxHeightDiff { get; set; } = 1;

    /// <summary>A battle still going after this many rounds ends in a draw.</summary>
    public int RoundCap { get; set; } = 30;

    /// <summary>Half-points of movement a knocked-over unit loses getting up at the start of its next turn.</summary>
    public int KnockedOverDrain { get; set; } = 6;

    /// <summary>Half-points of movement a slowed unit loses each turn.</summary>
    public int SlowAmount { get; set; } = 4;

    /// <summary>
    /// Simulated players' timing: for each skill level, the chance in 100 of a fumble, a good and a
    /// perfect grade. Used by the simulator and by terminal play until real timing minigames exist.
    /// </summary>
    public Dictionary<string, int[]> TimingSkill { get; set; } = new()
    {
        ["novice"] = [40, 50, 10],
        ["average"] = [20, 60, 20],
        ["expert"] = [5, 45, 50],
    };
}

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

    /// <summary>Extra damage from the attacker's action command, by grade: fumble, good, perfect.</summary>
    public int[] AttackBonus { get; set; } = [0, 1, 2];

    /// <summary>Damage the defender's guard takes off an enemy attack, by grade: fumble, good, perfect.</summary>
    public int[] GuardReduction { get; set; } = [0, 1, 2];

    /// <summary>Damage to a unit driven into an object (rock, a tree, a bank, the arena's edge).</summary>
    public int CollisionDamage { get; set; } = 2;

    /// <summary>Damage to each of two units when one is driven into the other. The design says only objects hurt, so 0.</summary>
    public int UnitCollisionDamage { get; set; }

    /// <summary>Damage per level fallen beyond the first. A one-level drop is free.</summary>
    public int FallDamagePerLevel { get; set; } = 2;

    /// <summary>Whether the arena's edge acts as a wall for pushes (true) or as a drop into no ground (false).</summary>
    public bool EdgesAreWalls { get; set; } = true;

    /// <summary>How far (orthogonal steps) a caster can draw on a natural feature.</summary>
    public int SourceRange { get; set; } = 3;

    /// <summary>The furthest apart Tether's two ends can be when it's cast.</summary>
    public int TetherMaxLength { get; set; } = 4;

    /// <summary>Goblin morale: the last goblin standing against more than one of the party tries to flee.</summary>
    public bool FleeEnabled { get; set; } = true;

    /// <summary>The bonus or reduction for a timing grade from one of the tables above.</summary>
    public static int ByGrade(int[] table, Timing grade) => grade switch
    {
        Timing.Fumble => table[0],
        Timing.Good => table[1],
        Timing.Perfect => table[2],
        _ => 0,
    };

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

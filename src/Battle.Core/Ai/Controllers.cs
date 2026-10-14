namespace Battle.Core.Ai;

/// <summary>
/// Anything that chooses actions: the player's interface, a script or (later) a trained network.
/// Controllers only ever choose from the legal list; the core alone applies the rules.
/// </summary>
public interface IController
{
    /// <summary>A short name for logs, like "scripted" or "random".</summary>
    string Name { get; }

    BattleAction Choose(BattleState s, int actor, IReadOnlyList<BattleAction> legal);
}

/// <summary>Where timing grades come from: the player's minigame, or a simulated player's skill.</summary>
public interface ITimingSource
{
    Timing Grade(BattleState s, in BattleAction action, TimingNeed need);
}

/// <summary>
/// A simulated player's timing: grades drawn from a skill level's chances in rules.json. Each roll
/// depends only on the seed and how far the battle has got (its event count), not on how many
/// grades were asked for before, so a look-ahead or a replay can't shift the rolls.
/// </summary>
public sealed class TimingModel : ITimingSource
{
    private readonly int[] _chances;
    private readonly ulong _seed;

    public string Skill { get; }

    public TimingModel(GameData data, string skill, ulong seed)
    {
        if (!data.Rules.TimingSkill.TryGetValue(skill, out var chances))
            throw new ArgumentException($"no timing skill called '{skill}' in rules.json (try {string.Join(", ", data.Rules.TimingSkill.Keys)})");
        _chances = chances;
        Skill = skill;
        _seed = seed ^ 0x7417_1D35_C0FF_EE00UL;
    }

    public Timing Grade(BattleState s, in BattleAction action, TimingNeed need)
    {
        var rng = new Rng(_seed ^ ((ulong)(uint)s.Seq * 0x9E37_79B9_7F4A_7C15UL));
        int roll = rng.Next(100);
        if (roll < _chances[0]) return Timing.Fumble;
        if (roll < _chances[0] + _chances[1]) return Timing.Good;
        return Timing.Perfect;
    }
}

/// <summary>Always the same grade. Handy for tests.</summary>
public sealed class FixedTiming(Timing grade) : ITimingSource
{
    public Timing Grade(BattleState s, in BattleAction action, TimingNeed need) => grade;
}

/// <summary>
/// Picks legal actions at random, but ends its turn only now and then, so battles keep moving.
/// Used for fuzzing (thousands of battles with every rule poked) and the benchmark.
/// </summary>
public sealed class RandomController : IController
{
    private Rng _rng;

    public RandomController(ulong seed) => _rng = new Rng(seed ^ 0x5EED_0F_FA11UL);

    public string Name => "random";

    public BattleAction Choose(BattleState s, int actor, IReadOnlyList<BattleAction> legal)
    {
        if (legal.Count == 1 || _rng.Chance(15)) return legal[^1]; // End is always last

        // Half the time, prefer a move (attack, spell, swap...) over walking, so fights actually happen.
        int firstNonWalk = 0;
        while (firstNonWalk < legal.Count - 1 && legal[firstNonWalk].Kind == ActionKind.Move) firstNonWalk++;
        int nonWalk = legal.Count - 1 - firstNonWalk;
        var pick = nonWalk > 0 && _rng.Chance(50) ? legal[firstNonWalk + _rng.Next(nonWalk)] : legal[_rng.Next(legal.Count - 1)];
        // Fleeing ends the battle, so it's taken only now and then, or fuzzing would rarely get far.
        if (pick.Kind == ActionKind.Flee && !_rng.Chance(5)) return legal[^1];
        return pick;
    }
}

/// <summary>
/// The simplest sensible fighter: hit the weakest enemy in reach, otherwise walk towards the
/// nearest one, otherwise end the turn. The real scripts for each unit type come later; this one
/// stays as a baseline.
/// </summary>
public sealed class SimpleScript : IController
{
    public string Name => "simple";

    public BattleAction Choose(BattleState s, int actor, IReadOnlyList<BattleAction> legal)
    {
        BattleAction? attack = null;
        int weakest = int.MaxValue;
        foreach (var a in legal)
        {
            if (a.Kind is not (ActionKind.Main or ActionKind.Special) || a.Target.Kind != TargetKind.Unit) continue;
            if (!Rules.AreEnemies(s, actor, a.Target.Unit)) continue;
            int hp = s.Units[a.Target.Unit].Hp;
            if (hp < weakest) { weakest = hp; attack = a; }
        }
        if (attack != null) return attack.Value;

        if (!s.Units[actor].Has(UnitFlags.MainUsed))
        {
            int here = DistanceToNearestEnemy(s, actor, s.Units[actor].Pos);
            BattleAction? step = null;
            int best = here;
            foreach (var a in legal)
            {
                if (a.Kind != ActionKind.Move) continue;
                int d = DistanceToNearestEnemy(s, actor, a.Target.Tile);
                if (d < best) { best = d; step = a; }
            }
            if (step != null) return step.Value;
        }
        return legal[^1];
    }

    public static int DistanceToNearestEnemy(BattleState s, int actor, Pos from)
    {
        int best = int.MaxValue;
        for (int i = 0; i < s.Units.Length; i++)
            if (s.Units[i].Alive && s.Units[i].OnBoard && Rules.AreEnemies(s, actor, i))
                best = Math.Min(best, from.Manhattan(s.Units[i].Pos));
        return best;
    }
}

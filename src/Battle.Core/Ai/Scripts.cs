namespace Battle.Core.Ai;

/// <summary>
/// The hand-written scripts: the enemies' (one per goblin type, see <see cref="EnemyScripts"/>) and a
/// simulated player for the party (<see cref="PartyBot"/>). They're the baseline trained enemies
/// have to beat, the fallback, and how early content ships. The seed only breaks near-ties.
/// </summary>
public static class Scripts
{
    public static IController For(Side side, ulong seed = 1) =>
        side == Side.Party ? new PartyBot(seed) : new EnemyScripts(seed);
}

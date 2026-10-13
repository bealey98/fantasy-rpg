namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // The switchboard: which rule handles each kind of move.
    // Melee lives in Rules.Combat.cs; the other kinds join as their build steps land.
    // ------------------------------------------------------------------

    private static void KindTargets(BattleState s, int actor, MoveDef move, List<(Target, Target)> output)
    {
        switch (move.Kind)
        {
            default:
                break; // not in the game yet
        }
    }

    private static void ExecuteKind(BattleState s, in BattleAction a, MoveDef move)
    {
        switch (move.Kind)
        {
            default:
                throw new IllegalActionException($"{move.Name} ({move.Kind}) isn't implemented yet");
        }
    }

    /// <summary>Moves that need more than a free action slot, such as a special needing both actions.</summary>
    private static bool ExtraAvailability(BattleState s, int actor, MoveDef move) => true;

    /// <summary>Can an area move aimed at a tile hurt the party? (Burrow, later.)</summary>
    private static bool AreaCanHurtParty(BattleState s, in BattleAction a, MoveDef move) => false;

    /// <summary>A taunted unit's attacks must target its taunter when they can. (The Loper's Taunt, later.)</summary>
    private static void ApplyTaunt(BattleState s, int actor, MoveDef move, List<(Target, Target)> output) { }
}

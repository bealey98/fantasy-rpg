namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // Places later build steps plug into the turn loop. Each is empty until its rules arrive.
    // ------------------------------------------------------------------

    /// <summary>Called once a unit's turn has started: burrowed units surface, goblins check their nerve.</summary>
    private static void OnTurnStart(BattleState s, int unit) { }

    /// <summary>Called as a unit's turn ends: a fleeing goblin on the edge escapes.</summary>
    private static void OnTurnEnd(BattleState s, int unit) { }

    /// <summary>Movement for a unit's turn, before statuses. A rider uses its mount's movement (later).</summary>
    private static int MountedMovement(BattleState s, int unit) => s.Units[unit].MoveMax;

    /// <summary>A snap trap springs on whoever steps on it (the Plinker's, later).</summary>
    private static void SpringSnapTrap(BattleState s, int owner, int body) { }

    /// <summary>Goblin bookkeeping when a unit leaves: beasts lose their orders or their handler (later).</summary>
    private static void OnUnitGoneGoblins(BattleState s, int unit) { }

    /// <summary>Mounting and dismounting (later).</summary>
    private static void AddMountActions(BattleState s, int actor, List<BattleAction> output) { }

    private static void ApplyMount(BattleState s, in BattleAction a) =>
        throw new IllegalActionException("mounting arrives in step 7");
}

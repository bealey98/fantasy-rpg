namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // Places later build steps plug into the turn loop. Each is empty until its rules arrive.
    // ------------------------------------------------------------------

    /// <summary>The creatures step between the party and enemy phases: living features move.</summary>
    private static void CreatureStep(BattleState s) { }

    /// <summary>The end of a round: sources regain uses, dark tethers sap.</summary>
    private static void RoundEndEffects(BattleState s) { }

    /// <summary>Board timers counted at each phase end: traps and tethers.</summary>
    private static void TickBoardTimers(BattleState s) { }

    /// <summary>Called when a unit's root runs out (the Tier 2 Bind slows it then).</summary>
    private static void OnRootEnded(BattleState s, int unit) { }

    /// <summary>Called once a unit's turn has started: burrowed units surface, goblins check their nerve.</summary>
    private static void OnTurnStart(BattleState s, int unit) { }

    /// <summary>Called as a unit's turn ends: a fleeing goblin on the edge escapes.</summary>
    private static void OnTurnEnd(BattleState s, int unit) { }

    /// <summary>Called when a unit dies, flees or is knocked out: tethers and orders are cleaned up.</summary>
    private static void OnUnitGone(BattleState s, int unit) { }

    /// <summary>Movement for a unit's turn, before statuses. A rider uses its mount's movement (later).</summary>
    private static int MountedMovement(BattleState s, int unit) => s.Units[unit].MoveMax;

    /// <summary>Mounting and dismounting (later).</summary>
    private static void AddMountActions(BattleState s, int actor, List<BattleAction> output) { }

    private static void ApplyMount(BattleState s, in BattleAction a) =>
        throw new IllegalActionException("mounting arrives in step 7");
}

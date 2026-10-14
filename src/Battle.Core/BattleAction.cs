namespace Battle.Core;

/// <summary>
/// The kind of input. Every choice anyone makes in a battle is one of these, whether it comes from
/// the player, a script, a network or a replayed log.
/// </summary>
public enum ActionKind : byte
{
    /// <summary>Walk to a tile.</summary>
    Move,
    /// <summary>Use a main move.</summary>
    Main,
    /// <summary>Use a bonus move.</summary>
    Bonus,
    /// <summary>Use a special: a move that takes both the main and the bonus action.</summary>
    Special,
    Mount,
    Dismount,
    /// <summary>
    /// Hand over to the partner. The hero's and the Loper's turns both start with the party phase
    /// and run side by side: either can hand over to the other at any point, as often as the player
    /// likes, until the other has ended its turn.
    /// </summary>
    SwapPartner,
    /// <summary>
    /// The whole party leaves the battle. Only while everyone in the party who's on the board stands
    /// on the arena's edge, with no enemy within range (rules.json "party_flee_range").
    /// </summary>
    Flee,
    /// <summary>End this unit's turn. In the party phase the partner carries on, if its turn is still going.</summary>
    End,
}

public enum TargetKind : byte { None, Unit, Tile, Self, Weapon }

/// <summary>What an action is aimed at: a unit, a tile, the user itself, or the hero's weapon.</summary>
public readonly record struct Target(TargetKind Kind, int Unit, Pos Tile)
{
    public static readonly Target None = new(TargetKind.None, -1, Pos.None);
    public static readonly Target Self = new(TargetKind.Self, -1, Pos.None);
    public static readonly Target Weapon = new(TargetKind.Weapon, -1, Pos.None);
    public static Target OfUnit(int unit) => new(TargetKind.Unit, unit, Pos.None);
    public static Target OfTile(Pos tile) => new(TargetKind.Tile, -1, tile);
}

/// <summary>
/// One input: who acts, the kind of action, the move used, its target (and a second target for
/// Tether), and the grade of any action command. Players, scripts, networks, logs and replays all
/// use this same record.
/// </summary>
public readonly record struct BattleAction(ActionKind Kind, int Actor, int Move, Target Target, Target Target2, Timing Timing)
{
    public static BattleAction End(int actor) => new(ActionKind.End, actor, -1, Target.None, Target.None, Timing.None);

    public static BattleAction MoveTo(int actor, Pos tile) => new(ActionKind.Move, actor, -1, Target.OfTile(tile), Target.None, Timing.None);

    public static BattleAction Use(int actor, MoveDef move, Target target, Target? target2 = null)
    {
        var kind = move.Slot switch
        {
            MoveSlot.Main => ActionKind.Main,
            MoveSlot.Bonus => ActionKind.Bonus,
            _ => ActionKind.Special,
        };
        return new BattleAction(kind, actor, move.Index, target, target2 ?? Target.None, Timing.None);
    }

    public BattleAction WithTiming(Timing timing) => this with { Timing = timing };

    /// <summary>The same choice, ignoring timing. Legality never depends on timing.</summary>
    public bool SameChoice(in BattleAction other) =>
        Kind == other.Kind && Actor == other.Actor && Move == other.Move && Target == other.Target && Target2 == other.Target2;
}

/// <summary>Thrown when an input isn't legal right now. Controllers should only pick from the legal list.</summary>
public sealed class IllegalActionException(string message) : Exception(message);

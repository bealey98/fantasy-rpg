namespace Battle.Core;

/// <summary>Which of a turn's actions a move uses. A special uses both the main and the bonus action.</summary>
public enum MoveSlot : byte { Main, Bonus, Special }

/// <summary>
/// Which rule handles a move. The rules live in code (Rules.*.cs); the numbers live in moves.json.
/// </summary>
public enum MoveKind : byte
{
    /// <summary>Hit a neighbouring enemy.</summary>
    Melee,
    /// <summary>Hit an enemy at range, with sight.</summary>
    Ranged,
    /// <summary>Charge in a straight line, then hit (Bull Rush, Gore).</summary>
    Charge,
    /// <summary>Push a neighbouring enemy away (Shove).</summary>
    Push,
    /// <summary>Ready a block against the next melee attack.</summary>
    Parry,
    Bind,
    Tether,
    Burrow,
    /// <summary>Swap places with a neighbouring ally (After you).</summary>
    Swap,
    /// <summary>Gain extra movement this turn.</summary>
    Scamper,
    /// <summary>Lob a trap onto a tile (Snap trap).</summary>
    Trap,
    /// <summary>Tell a beast what to attack.</summary>
    Point,
    /// <summary>Crack a whip at your own beast to buff its next attack.</summary>
    Whip,
    Taunt,
    Brace,
}

public enum MountRule : byte { Any, Dismounted }

public enum StatusId : byte { None, Rooted, Slowed, Blinded, Dazed, KnockedOver, Taunted, Braced }

/// <summary>What a path move draws on: light moves spend a feature's uses, dark moves drain its HP.</summary>
public enum SourceKind : byte { None, Light, Dark }

/// <summary>A move, read from content/moves.json.</summary>
public sealed class MoveDef
{
    public required int Index { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required MoveSlot Slot { get; init; }
    public required MoveKind Kind { get; init; }

    /// <summary>One line for the player, shown when inspecting a unit.</summary>
    public string Text { get; init; } = "";

    public int Damage { get; init; }

    /// <summary>Reach in tiles, counting orthogonal steps.</summary>
    public int Range { get; init; } = 1;

    /// <summary>Gains +1 range per level the user stands above the target.</summary>
    public bool HeightBonus { get; init; }

    /// <summary>Needs a clear line of sight to the target.</summary>
    public bool NeedsSight { get; init; }

    /// <summary>Has an action command: the user's timing changes how well it lands.</summary>
    public bool Command { get; init; }

    /// <summary>
    /// Base push in tiles; the weight difference between pusher and target is added, so lighter
    /// targets go further. -1 means the move doesn't push at all.
    /// </summary>
    public int Push { get; init; } = -1;

    /// <summary>Tiles of straight-line charge before the hit.</summary>
    public int Charge { get; init; }

    public StatusId Status { get; init; }
    public int StatusRounds { get; init; }

    /// <summary>A status the user gives itself (the Cutter's Headbutt dazes the Cutter too).</summary>
    public StatusId SelfStatus { get; init; }
    public int SelfStatusRounds { get; init; }

    /// <summary>Extra damage when another goblin is also next to the target.</summary>
    public int GangUp { get; init; }

    public Element Element { get; init; }
    public SourceKind Source { get; init; }

    /// <summary>HP a dark move drains from its source (or from the hero when no source is left).</summary>
    public int DarkCost { get; init; }

    /// <summary>How long the move's effect lasts, in rounds (binds, tethers, taunts, braces).</summary>
    public int Rounds { get; init; }

    /// <summary>Radius of an area effect, in tiles.</summary>
    public int Radius { get; init; }

    /// <summary>
    /// A move-specific number: Scamper's extra movement (half-points), the whip's buff, Tether's pull
    /// distance or sap per round, Brace's defence, Parry's counter-strike damage.
    /// </summary>
    public int Amount { get; init; }

    public MountRule Mount { get; init; }
    public TetherVariant Variant { get; init; }

    /// <summary>The path tier the move belongs to (0 for moves outside the paths).</summary>
    public int Tier { get; init; }

    /// <summary>The Tier 2 Bind: also deals damage, and slows once the root ends.</summary>
    public bool Upgraded { get; init; }

    public bool UsesMain => Slot is MoveSlot.Main or MoveSlot.Special;
    public bool UsesBonus => Slot is MoveSlot.Bonus or MoveSlot.Special;
}

namespace Battle.Core;

/// <summary>Yes/no facts about a unit, packed into one number so copying a unit stays cheap.</summary>
[Flags]
public enum UnitFlags : uint
{
    None = 0,

    /// <summary>Still in the fight: has HP left and hasn't fled.</summary>
    Alive = 1 << 0,

    /// <summary>Standing on a tile. False while burrowed, and after dying or fleeing.</summary>
    OnBoard = 1 << 1,

    Fled = 1 << 2,

    /// <summary>Has spent some movement this turn.</summary>
    Moved = 1 << 3,

    MainUsed = 1 << 4,
    BonusUsed = 1 << 5,

    /// <summary>Goblin morale has broken: it is running for the edge of the arena.</summary>
    Fleeing = 1 << 6,

    /// <summary>Parry is readied: the next melee attack on it is blocked.</summary>
    ParryReady = 1 << 7,

    /// <summary>Bind is on the hero's weapon: his next hit roots its target.</summary>
    WeaponBind = 1 << 8,

    /// <summary>The weapon Bind was cast without a source, so the hit only slows.</summary>
    WeaponBindWeak = 1 << 9,

    /// <summary>The weapon Bind is the Tier 2 version: it also deals damage and slows afterwards.</summary>
    WeaponBindUpgraded = 1 << 10,

    /// <summary>Dazed: loses its next bonus action.</summary>
    Dazed = 1 << 11,

    /// <summary>Knocked over: loses movement at the start of its next turn.</summary>
    KnockedOver = 1 << 12,

    /// <summary>Underground, surfacing next turn.</summary>
    Burrowed = 1 << 13,

    /// <summary>Tier 2 Bind: slowed once the root ends.</summary>
    SlowAfterRoot = 1 << 14,

    /// <summary>Knocked out: a companion at 0 HP sits out the rest of the battle.</summary>
    KnockedOut = 1 << 15,
}

/// <summary>What a beast has been told to do by its handler.</summary>
public enum OrderKind : byte { None, Unit, Tile }

/// <summary>
/// One combatant's state during a battle. It's a struct (a plain bundle of values), so the
/// array of units copies in one go when the battle state is copied.
///
/// Status timers count phase ends, two per round: a status with 2 left lasts until the end of
/// the next phase after the current one. That's how "effects last whole rounds" works: a Bind
/// cast in the player phase holds through the enemy phase that follows.
/// </summary>
public struct Unit
{
    /// <summary>Index into GameData.Units.</summary>
    public int Def;

    public Side Side;
    public Weight Weight;
    public Pos Pos;
    public int Hp;
    public int MaxHp;
    public int Defence;
    public int Power;

    /// <summary>Movement per turn, in half-points.</summary>
    public int MoveMax;

    /// <summary>Movement left this turn, in half-points.</summary>
    public int MoveLeft;

    public UnitFlags Flags;

    // Status timers, in phase ends (two per round). Zero means the status isn't active.
    public int Rooted;
    public int Slowed;
    public int Blinded;
    public int Braced;
    public int Taunted;
    public int Buffed;
    public int WeaponBindTimer;

    /// <summary>The unit a taunted unit must attack.</summary>
    public int TauntBy;

    /// <summary>Extra damage on its next attack (the Beastmaster's whip).</summary>
    public int BuffAmount;

    /// <summary>Extra defence while braced.</summary>
    public int BraceDefence;

    /// <summary>For a rider: the index of its mount, else -1.</summary>
    public int MountedOn;

    /// <summary>For a mount: the index of its rider, else -1.</summary>
    public int Rider;

    /// <summary>Where a burrowed unit will surface.</summary>
    public Pos BurrowTo;

    /// <summary>For a beast: the index of its handler, else -1.</summary>
    public int Handler;

    public OrderKind Order;
    public int OrderUnit;
    public Pos OrderTile;

    public readonly bool Has(UnitFlags flag) => (Flags & flag) != 0;

    public void Set(UnitFlags flag, bool on = true)
    {
        if (on) Flags |= flag;
        else Flags &= ~flag;
    }

    public readonly bool Alive => Has(UnitFlags.Alive);
    public readonly bool OnBoard => Has(UnitFlags.OnBoard);
}

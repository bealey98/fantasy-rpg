namespace Battle.Core;

/// <summary>The three parts of a round: the party acts, creatures and gusts move, then the enemies act.</summary>
public enum Phase : byte { Party, Creatures, Enemy }

public enum Outcome : byte { Ongoing, PartyWon, PartyLost, Draw }

/// <summary>
/// How well an action command was played. It's an input to the core: the game's minigame
/// produces it, and in simulation a timing model samples it for a simulated player's skill.
/// For an enemy attack on the party, it's the defender's guard.
/// </summary>
public enum Timing : byte { None, Fumble, Good, Perfect }

public enum TrapKind : byte { None, Bind, Snap }

public enum EventType : byte
{
    RoundStart,
    PhaseStart,
    TurnStart,
    TurnEnd,
    Move,
    Charge,
    Push,
    Pull,
    Drag,
    Swap,
    Collide,
    Fall,
    Damage,
    Miss,
    Heal,
    Death,
    KnockedOut,
    Fled,
    Status,
    StatusEnd,
    Parry,
    Counter,
    TrapSet,
    TrapTriggered,
    TrapEnd,
    TetherSet,
    TetherEnd,
    SourceUse,
    SourceDrain,
    SourceDeath,
    Corruption,
    Burrow,
    Surface,
    Crater,
    Order,
    Buff,
    Mount,
    Dismount,
    Scamper,
    SwapOrder,
    CreatureMove,
    FleeStart,
    BattleEnd,
}

/// <summary>
/// One thing that happened in a battle. Each event carries what changed, including HP
/// afterwards, so the viewer and R can follow a battle without running the core.
/// Unit fields hold unit indices (-1 for none); tile fields hold Pos.None when unused.
/// </summary>
public readonly record struct BattleEvent(
    int Round,
    Phase Phase,
    int Seq,
    int Actor,
    EventType Type,
    int Target,
    Pos From,
    Pos To,
    int Amount,
    int HpAfter,
    Timing Timing,
    int Move,
    string? Detail,
    ulong Checksum);

/// <summary>Somewhere events go. Copies of the state used for previews or look-ahead use the null sink.</summary>
public interface IEventSink
{
    /// <summary>False for the null sink, so callers can skip building detail strings nobody will read.</summary>
    bool Enabled { get; }

    void Add(in BattleEvent e);
}

public sealed class NullSink : IEventSink
{
    public static readonly NullSink Instance = new();
    public bool Enabled => false;
    public void Add(in BattleEvent e) { }
}

/// <summary>Collects events in memory, in order.</summary>
public sealed class EventList : IEventSink
{
    public List<BattleEvent> Events { get; } = new();
    public bool Enabled => true;
    public void Add(in BattleEvent e) => Events.Add(e);
}

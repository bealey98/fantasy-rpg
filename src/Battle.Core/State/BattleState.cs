namespace Battle.Core;

/// <summary>
/// Everything about a battle in progress. The board and the units are held in flat arrays,
/// so <see cref="Clone"/> is cheap: previews, undo, danger zones and any AI that looks ahead
/// copy the state, try something and throw the copy away.
///
/// Board arrays are indexed by tile: index = y * Width + x.
/// The rules that change this state live in the Rules class, not here.
/// </summary>
public sealed class BattleState
{
    // ---- Fixed for the whole battle; copies share these ----
    public GameData Data { get; }
    public BattleInfo Info { get; }
    public int Width { get; }
    public int Height { get; }
    public int TileCount => Width * Height;

    // ---- The board ----
    /// <summary>0 no ground, 1 low, 2 base, 3 high, 4 very high. Craters lower it.</summary>
    public byte[] Heights;
    public byte[] Terrain;
    /// <summary>Feature index on each tile, or -1.</summary>
    public int[] Feature;
    public int[] FeatureUses;
    public int[] FeatureHp;
    public bool[] FeatureDead;
    /// <summary>The unit standing on each tile (the mount, for a rider and mount), or -1.</summary>
    public int[] Occupant;
    public TrapKind[] Trap;
    public int[] TrapOwner;
    /// <summary>Phase ends left before a trap crumbles; 0 means it lasts until something steps on it.</summary>
    public int[] TrapTimer;
    /// <summary>The burrowed unit surfacing on each tile next turn, or -1. The game shows these as rumbling ground.</summary>
    public int[] Rumble;

    // ---- The units ----
    public Unit[] Units;
    public Tether[] Tethers;
    public int TetherCount;

    // ---- Turn bookkeeping ----
    public int Round;
    public Phase Phase;
    /// <summary>The units acting in the current phase, in order.</summary>
    public int[] Order;
    public int OrderCount;
    /// <summary>Which entry of Order is acting now.</summary>
    public int OrderPos;
    /// <summary>The player chose to have the Loper act before the hero.</summary>
    public bool LoperFirst;
    /// <summary>Someone has acted in the current phase (the party order can only be swapped before that).</summary>
    public bool PhaseActed;
    public Outcome Outcome;
    /// <summary>How many dark moves have been used: the battle's nudge to the corruption meter.</summary>
    public int DarkUses;
    public Rng Rng;
    /// <summary>Event counter, so every event in a battle has its own sequence number.</summary>
    public int Seq;

    /// <summary>
    /// The acting unit as it was just before its turn started, so swapping the party order before
    /// anyone has acted can undo the start of that turn exactly.
    /// </summary>
    public Unit TurnStartBackup;

    /// <summary>Where events go. Not part of the state proper: copies get the null sink.</summary>
    public IEventSink Sink = NullSink.Instance;

    public BattleState(GameData data, BattleInfo info, int width, int height, int unitCount)
    {
        Data = data;
        Info = info;
        Width = width;
        Height = height;
        int n = width * height;
        Heights = new byte[n];
        Terrain = new byte[n];
        Feature = new int[n];
        FeatureUses = new int[n];
        FeatureHp = new int[n];
        FeatureDead = new bool[n];
        Occupant = new int[n];
        Trap = new TrapKind[n];
        TrapOwner = new int[n];
        TrapTimer = new int[n];
        Rumble = new int[n];
        Array.Fill(Feature, -1);
        Array.Fill(Occupant, -1);
        Array.Fill(TrapOwner, -1);
        Array.Fill(Rumble, -1);
        Units = new Unit[unitCount];
        Tethers = new Tether[8];
        Order = new int[unitCount];
    }

    private BattleState(BattleState other)
    {
        Data = other.Data;
        Info = other.Info;
        Width = other.Width;
        Height = other.Height;
        Heights = (byte[])other.Heights.Clone();
        Terrain = (byte[])other.Terrain.Clone();
        Feature = (int[])other.Feature.Clone();
        FeatureUses = (int[])other.FeatureUses.Clone();
        FeatureHp = (int[])other.FeatureHp.Clone();
        FeatureDead = (bool[])other.FeatureDead.Clone();
        Occupant = (int[])other.Occupant.Clone();
        Trap = (TrapKind[])other.Trap.Clone();
        TrapOwner = (int[])other.TrapOwner.Clone();
        TrapTimer = (int[])other.TrapTimer.Clone();
        Rumble = (int[])other.Rumble.Clone();
        Units = (Unit[])other.Units.Clone();
        Tethers = (Tether[])other.Tethers.Clone();
        TetherCount = other.TetherCount;
        Round = other.Round;
        Phase = other.Phase;
        Order = (int[])other.Order.Clone();
        OrderCount = other.OrderCount;
        OrderPos = other.OrderPos;
        LoperFirst = other.LoperFirst;
        PhaseActed = other.PhaseActed;
        Outcome = other.Outcome;
        DarkUses = other.DarkUses;
        Rng = other.Rng;
        Seq = other.Seq;
        TurnStartBackup = other.TurnStartBackup;
    }

    /// <summary>A full, independent copy. Events from the copy go nowhere unless you give it a sink.</summary>
    public BattleState Clone() => new(this);

    // ---- Tiles ----

    public int Idx(Pos p) => p.Y * Width + p.X;
    public Pos PosOf(int tile) => new(tile % Width, tile / Width);
    public bool InBounds(Pos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
    public bool OnEdge(Pos p) => p.X == 0 || p.Y == 0 || p.X == Width - 1 || p.Y == Height - 1;

    public TerrainDef TerrainAt(int tile) => Data.Terrain[Terrain[tile]];

    /// <summary>The living or dead feature on a tile, or null.</summary>
    public FeatureDef? FeatureAt(int tile) => Feature[tile] >= 0 ? Data.Features[Feature[tile]] : null;

    public bool IsNoGround(int tile) => TerrainAt(tile).NoGround;

    /// <summary>Something blocks this tile for good: rock, or a tree (living or dead).</summary>
    public bool IsObject(int tile) => TerrainAt(tile).IsObject || (FeatureAt(tile)?.IsObject ?? false);

    /// <summary>Could a unit stand here, ignoring other units? Not rock, not no ground, not inside a tree.</summary>
    public bool Standable(int tile) => TerrainAt(tile).Walkable && !(FeatureAt(tile)?.Blocks ?? false);

    /// <summary>The unit on a tile: the mount for a mounted pair; -1 if empty or off the board.</summary>
    public int UnitAt(Pos p) => InBounds(p) ? Occupant[Idx(p)] : -1;

    // ---- Units ----

    public ref Unit U(int index) => ref Units[index];

    public UnitDef DefOf(int index) => Data.Units[Units[index].Def];

    public string Label(int index) => index >= 0 ? Info.Labels[index] : "";

    /// <summary>The unit that physically occupies the tile for this unit: its mount if it's riding, else itself.</summary>
    public int Body(int unit) => Units[unit].MountedOn >= 0 ? Units[unit].MountedOn : unit;

    /// <summary>Puts a unit (and its rider, if it has one) on a tile. The tile must be free.</summary>
    public void Place(int unit, Pos p)
    {
        int body = Body(unit);
        ref var b = ref Units[body];
        if (b.OnBoard) Occupant[Idx(b.Pos)] = -1;
        b.Pos = p;
        b.Set(UnitFlags.OnBoard);
        Occupant[Idx(p)] = body;
        if (b.Rider >= 0)
        {
            Units[b.Rider].Pos = p;
            Units[b.Rider].Set(UnitFlags.OnBoard);
        }
    }

    /// <summary>Takes a unit off the board (dying, fleeing or burrowing). A rider stays on the tile.</summary>
    public void RemoveFromBoard(int unit)
    {
        ref var u = ref Units[unit];
        if (!u.OnBoard) return;
        if (u.MountedOn >= 0)
        {
            // A rider leaves its mount behind.
            Units[u.MountedOn].Rider = -1;
            u.MountedOn = -1;
        }
        else if (Occupant[Idx(u.Pos)] == unit)
        {
            Occupant[Idx(u.Pos)] = -1;
            if (u.Rider >= 0)
            {
                // A mount leaves its rider standing on the tile.
                int rider = u.Rider;
                Units[rider].MountedOn = -1;
                u.Rider = -1;
                Occupant[Idx(u.Pos)] = rider;
            }
        }
        u.Set(UnitFlags.OnBoard, false);
    }

    /// <summary>Living units of a side that are on the board.</summary>
    public IEnumerable<int> UnitsOf(Side side)
    {
        for (int i = 0; i < Units.Length; i++)
            if (Units[i].Side == side && Units[i].Alive) yield return i;
    }

    // ---- Events ----

    /// <summary>Records an event, stamped with the round, phase and next sequence number.</summary>
    public void Emit(EventType type, int actor = -1, int target = -1, Pos? from = null, Pos? to = null,
        int amount = 0, int hpAfter = 0, Timing timing = Timing.None, int move = -1, string? detail = null, ulong checksum = 0)
    {
        Seq++;
        if (!Sink.Enabled) return;
        Sink.Add(new BattleEvent(Round, Phase, Seq, actor, type, target, from ?? Pos.None, to ?? Pos.None,
            amount, hpAfter, timing, move, detail, checksum));
    }

    // ---- Checksum ----

    /// <summary>
    /// A fingerprint of the whole state (FNV-1a over every value, in a fixed order). Logged at the
    /// end of every turn: if a replay's checksums match, the replay is exact.
    /// </summary>
    public ulong Checksum()
    {
        var h = new Fnv();
        h.Add(Width); h.Add(Height);
        h.Add(Heights); h.Add(Terrain); h.Add(Feature); h.Add(FeatureUses); h.Add(FeatureHp);
        foreach (bool d in FeatureDead) h.Add(d ? 1 : 0);
        h.Add(Occupant);
        foreach (var t in Trap) h.Add((int)t);
        h.Add(TrapOwner); h.Add(TrapTimer); h.Add(Rumble);
        foreach (ref var u in Units.AsSpan())
        {
            h.Add(u.Def); h.Add((int)u.Side); h.Add((int)u.Weight); h.Add(u.Pos.X); h.Add(u.Pos.Y);
            h.Add(u.Hp); h.Add(u.MaxHp); h.Add(u.Defence); h.Add(u.Power); h.Add(u.MoveMax); h.Add(u.MoveLeft);
            h.Add((int)u.Flags); h.Add(u.Rooted); h.Add(u.Slowed); h.Add(u.Blinded); h.Add(u.Braced);
            h.Add(u.Taunted); h.Add(u.Buffed); h.Add(u.WeaponBindTimer); h.Add(u.TauntBy); h.Add(u.BuffAmount);
            h.Add(u.BraceDefence); h.Add(u.MountedOn); h.Add(u.Rider); h.Add(u.BurrowTo.X); h.Add(u.BurrowTo.Y);
            h.Add(u.Handler); h.Add((int)u.Order); h.Add(u.OrderUnit); h.Add(u.OrderTile.X); h.Add(u.OrderTile.Y);
        }
        h.Add(TetherCount);
        for (int i = 0; i < TetherCount; i++)
        {
            var t = Tethers[i];
            h.Add(t.Active ? 1 : 0); h.Add(t.Caster); h.Add(t.UnitA); h.Add(t.UnitB);
            h.Add(t.TileA.X); h.Add(t.TileA.Y); h.Add(t.TileB.X); h.Add(t.TileB.Y);
            h.Add(t.Length); h.Add(t.Timer); h.Add((int)t.Variant);
        }
        h.Add(Round); h.Add((int)Phase); h.Add(OrderCount); h.Add(OrderPos);
        for (int i = 0; i < OrderCount; i++) h.Add(Order[i]);
        h.Add(LoperFirst ? 1 : 0); h.Add(PhaseActed ? 1 : 0); h.Add((int)Outcome); h.Add(DarkUses);
        h.Add((long)Rng.State);
        h.Add(Seq);
        return h.Value;
    }

    private struct Fnv
    {
        public ulong Value = 14695981039346656037UL;
        public Fnv() { }

        public void Add(long n)
        {
            for (int i = 0; i < 8; i++)
            {
                Value ^= (byte)(n >> (i * 8));
                Value *= 1099511628211UL;
            }
        }

        public void Add(int n) => Add((long)n);
        public void Add(byte[] values) { foreach (byte b in values) Add(b); }
        public void Add(int[] values) { foreach (int v in values) Add(v); }
    }
}

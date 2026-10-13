namespace Battle.Core;

/// <summary>
/// The rules of the battle, split across files by topic (Rules.Movement.cs, Rules.Combat.cs, ...).
/// Every function takes the battle state and changes it or answers a question about it.
/// </summary>
public static partial class Rules
{
    /// <summary>
    /// Working arrays for pathfinding, reused between calls so nothing is allocated per decision.
    /// One set per thread, so separate threads can run separate battles.
    /// </summary>
    private sealed class Scratch
    {
        public const int MaxTiles = 32 * 32;
        public readonly int[] Dist = new int[MaxTiles];
        public readonly int[] Prev = new int[MaxTiles];
        public readonly int[] Heap = new int[MaxTiles * 4];
        public int ForUnit = -1;
        public int ForBudget = -1;
        public BattleState? ForState;
        public int ForSeq = -1;
    }

    [ThreadStatic] private static Scratch? _scratch;
    private static Scratch S => _scratch ??= new Scratch();

    public const int Unreachable = int.MaxValue;

    // ------------------------------------------------------------------
    // One step
    // ------------------------------------------------------------------

    /// <summary>
    /// The half-points it costs a unit to step from one tile onto a neighbouring one, or -1 if it can't.
    ///   - Rock, no ground and trees can't be entered.
    ///   - Stepping up one level costs extra (a slope); stepping down one level is free.
    ///   - Two or more levels up or down needs a ladder on one of the two tiles.
    ///   - Enemies block the way; allies can be passed through (but not stopped on).
    /// </summary>
    public static int StepCost(BattleState s, int unit, int from, int to)
    {
        var terrain = s.TerrainAt(to);
        if (!terrain.Walkable) return -1;
        var feature = s.FeatureAt(to);
        if (feature != null && feature.Blocks) return -1;

        int occupant = s.Occupant[to];
        if (occupant >= 0 && occupant != s.Body(unit) && s.Units[occupant].Side != s.Units[unit].Side) return -1;

        int cost = terrain.MoveCost + (feature?.ExtraMoveCost ?? 0);
        int dh = s.Heights[to] - s.Heights[from];
        if (dh == 1)
        {
            cost += s.Data.Rules.ClimbCost;
        }
        else if (dh >= 2 || dh <= -2)
        {
            if (!s.TerrainAt(from).Ladder && !terrain.Ladder) return -1;
            cost += s.Data.Rules.LadderCostPerLevel * Math.Abs(dh);
        }

        if (!TetherAllows(s, unit, s.PosOf(to))) return -1;
        return cost;
    }

    /// <summary>Can this unit finish a move on this tile? It must be standable and empty.</summary>
    public static bool CanEndOn(BattleState s, int unit, int tile)
    {
        if (!s.Standable(tile)) return false;
        int occupant = s.Occupant[tile];
        return occupant < 0;
    }

    // ------------------------------------------------------------------
    // Every tile within reach
    // ------------------------------------------------------------------

    /// <summary>
    /// Works out the cheapest cost to every tile the unit can reach within the budget (Dijkstra's
    /// algorithm with whole-number costs). Ties are broken by tile number, so the same board always
    /// gives the same paths. Traps can be walked onto but never through.
    /// Results stay in the scratch arrays until the next call: use <see cref="ReachCost"/> and <see cref="PathTo"/>.
    /// </summary>
    public static void ComputeReach(BattleState s, int unit, int budget)
    {
        var sc = S;
        int n = s.TileCount;
        if (n > Scratch.MaxTiles) throw new InvalidOperationException("arenas are limited to 32 x 32 tiles");
        Array.Fill(sc.Dist, Unreachable, 0, n);
        Array.Fill(sc.Prev, -1, 0, n);
        sc.ForUnit = unit;
        sc.ForBudget = budget;
        sc.ForState = s;
        sc.ForSeq = s.Seq;

        var u = s.Units[unit];
        if (!u.OnBoard) return;
        int start = s.Idx(u.Pos);
        sc.Dist[start] = 0;

        // A binary heap of (cost * 1024 + tile), so the smallest cost pops first and ties go to the lowest tile.
        int heapSize = 0;
        Push(sc.Heap, ref heapSize, start);

        while (heapSize > 0)
        {
            int key = Pop(sc.Heap, ref heapSize);
            int tile = key & 1023;
            int cost = key >> 10;
            if (cost > sc.Dist[tile]) continue; // a stale entry
            if (tile != start && s.Trap[tile] != TrapKind.None) continue; // traps stop movement

            var here = s.PosOf(tile);
            foreach (var d in Dirs.All)
            {
                var next = here.Step(d);
                if (!s.InBounds(next)) continue;
                int nextTile = s.Idx(next);
                int step = StepCost(s, unit, tile, nextTile);
                if (step < 0) continue;
                int total = cost + step;
                if (total > budget || total >= sc.Dist[nextTile]) continue;
                sc.Dist[nextTile] = total;
                sc.Prev[nextTile] = tile;
                Push(sc.Heap, ref heapSize, (total << 10) | nextTile);
            }
        }
    }

    /// <summary>The cost to reach a tile from the last <see cref="ComputeReach"/>, or Unreachable.</summary>
    public static int ReachCost(Pos p) => S.Dist[p.Y * S.ForState!.Width + p.X];

    /// <summary>Every tile the unit could finish a move on with its remaining movement.</summary>
    public static void ReachableTiles(BattleState s, int unit, List<Pos> output)
    {
        output.Clear();
        ref var u = ref s.Units[unit];
        if (!CanMove(s, unit)) return;
        ComputeReach(s, unit, u.MoveLeft);
        var sc = S;
        int start = s.Idx(u.Pos);
        for (int tile = 0; tile < s.TileCount; tile++)
            if (tile != start && sc.Dist[tile] != Unreachable && CanEndOn(s, unit, tile))
                output.Add(s.PosOf(tile));
    }

    /// <summary>Can the unit move at all right now? Rooted or braced units can't.</summary>
    public static bool CanMove(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        if (!u.Alive || !u.OnBoard || u.MoveLeft <= 0) return false;
        return !Anchored(s, unit);
    }

    /// <summary>
    /// The tiles walked from the unit's position to the destination, not counting the start,
    /// using the paths from the last <see cref="ComputeReach"/>.
    /// </summary>
    public static List<Pos> PathTo(BattleState s, Pos destination)
    {
        var sc = S;
        var path = new List<Pos>();
        int tile = s.Idx(destination);
        if (sc.Dist[tile] == Unreachable) return path;
        while (sc.Prev[tile] >= 0)
        {
            path.Add(s.PosOf(tile));
            tile = sc.Prev[tile];
        }
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Moves a unit to a reachable tile, spending the movement. Returns false (and changes nothing)
    /// if the tile can't be reached with the movement left.
    /// </summary>
    public static bool MoveTo(BattleState s, int unit, Pos destination)
    {
        if (!CanMove(s, unit) || !s.InBounds(destination)) return false;
        ref var u = ref s.Units[unit];
        ComputeReach(s, unit, u.MoveLeft);
        int tile = s.Idx(destination);
        int cost = S.Dist[tile];
        if (cost == Unreachable || tile == s.Idx(u.Pos) || !CanEndOn(s, unit, tile)) return false;

        var from = u.Pos;
        string? detail = s.Sink.Enabled ? "path:" + string.Join(";", PathTo(s, destination).Select(p => $"{p.X},{p.Y}")) : null;
        s.Place(unit, destination);
        u.MoveLeft -= cost;
        u.Set(UnitFlags.Moved);
        s.Emit(EventType.Move, unit, unit, from, destination, cost, detail: detail);
        Arrive(s, s.Body(unit), from);
        return true;
    }

    /// <summary>
    /// What happens once a unit lands on a tile, whether it walked, charged or was pushed:
    /// traps spring and tethers drag (added in later steps).
    /// </summary>
    private static void Arrive(BattleState s, int body, Pos from)
    {
        ArriveEffects(s, body, from);
    }

    /// <summary>
    /// Tether rule hook: may this unit step onto the tile without breaking a tether?
    /// Always true until the Elderwood's Tether arrives in step 6.
    /// </summary>
    public static bool TetherAllows(BattleState s, int unit, Pos tile) => true;

    // ------------------------------------------------------------------
    // A tiny binary heap of ints (smallest first)
    // ------------------------------------------------------------------

    private static void Push(int[] heap, ref int size, int key)
    {
        int i = size++;
        heap[i] = key;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (heap[parent] <= heap[i]) break;
            (heap[parent], heap[i]) = (heap[i], heap[parent]);
            i = parent;
        }
    }

    private static int Pop(int[] heap, ref int size)
    {
        int top = heap[0];
        heap[0] = heap[--size];
        int i = 0;
        while (true)
        {
            int left = 2 * i + 1, right = left + 1, smallest = i;
            if (left < size && heap[left] < heap[smallest]) smallest = left;
            if (right < size && heap[right] < heap[smallest]) smallest = right;
            if (smallest == i) break;
            (heap[smallest], heap[i]) = (heap[i], heap[smallest]);
            i = smallest;
        }
        return top;
    }
}

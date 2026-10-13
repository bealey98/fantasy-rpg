namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // Force effects: pushes, knockbacks and pulls
    //   - The heavier side moves the lighter one: a push goes its base distance plus one tile for
    //     each weight class the target is lighter than the pusher.
    //   - Driven into an object (rock, a tree, a bank it can't be pushed up, the arena's edge),
    //     the target stops and takes collision damage.
    //   - Driven into another unit, it stops (no damage by default; see unit_collision_damage).
    //   - A drop of one level is free; a drop of two or more ends the push and deals fall damage.
    //   - Driven onto no ground, it dies at once.
    //   - Rooted or braced units can't be moved.
    // ------------------------------------------------------------------

    /// <summary>The weight that counts for a unit: a rider and its mount count as the heavier of the two.</summary>
    public static Weight PairWeight(BattleState s, int unit)
    {
        ref var u = ref s.Units[unit];
        int partner = u.MountedOn >= 0 ? u.MountedOn : u.Rider;
        return partner >= 0 && s.Units[partner].Weight > u.Weight ? s.Units[partner].Weight : u.Weight;
    }

    /// <summary>How far a push travels: the base plus the weight difference. Zero or less means no push.</summary>
    public static int PushDistance(BattleState s, int pusher, int target, int baseDistance) =>
        baseDistance + (int)PairWeight(s, pusher) - (int)PairWeight(s, target);

    /// <summary>
    /// Moves a unit (and its rider or mount) up to <paramref name="distance"/> tiles in a direction,
    /// applying collisions, falls and no ground. Returns how many tiles it actually moved.
    /// </summary>
    public static int Push(BattleState s, int source, int target, Dir dir, int distance, EventType kind = EventType.Push)
    {
        int body = s.Body(target);
        ref var b = ref s.Units[body];
        if (distance <= 0 || !b.Alive || !b.OnBoard || Anchored(s, body)) return 0;

        // First work out where the push ends and what stops it; only then move and hurt the unit.
        var start = b.Pos;
        var pos = start;
        int moved = 0, drop = 0, hitUnit = -1;
        string? hitObject = null;
        Pos intoVoid = Pos.None;
        for (int step = 0; step < distance; step++)
        {
            var next = pos.Step(dir);
            if (!s.InBounds(next))
            {
                if (s.Data.Rules.EdgesAreWalls) hitObject = "the edge of the arena";
                else intoVoid = next;
                break;
            }
            int tile = s.Idx(next);
            var terrain = s.TerrainAt(tile);
            if (terrain.NoGround) { intoVoid = next; break; }
            if (!s.Standable(tile))
            {
                hitObject = s.FeatureAt(tile) is { Blocks: true } f ? f.Name.ToLowerInvariant() : terrain.Name.ToLowerInvariant();
                break;
            }
            if (s.Occupant[tile] >= 0) { hitUnit = s.Occupant[tile]; break; }
            int dh = s.Heights[tile] - s.Heights[s.Idx(pos)];
            if (dh >= 1) { hitObject = "a bank"; break; }
            if (!TetherAllows(s, body, pos, next)) break;

            pos = next;
            moved++;
            drop = -dh;
            if (drop >= 2 || s.Trap[tile] != TrapKind.None) break; // a real fall, or a trap, ends the push
        }

        if (!intoVoid.IsNone)
        {
            if (moved > 0) s.Place(body, pos);
            s.Emit(kind, source, body, start, intoVoid, moved + 1);
            IntoNoGround(s, source, body, intoVoid);
            return moved + 1;
        }
        if (moved > 0)
        {
            s.Place(body, pos);
            s.Emit(kind, source, body, start, pos, moved);
        }
        if (hitObject != null) Collide(s, source, body, hitObject);
        else if (hitUnit >= 0) CollideUnits(s, source, body, hitUnit);
        if (drop >= 2 && s.Units[body].Alive) Fall(s, source, body, drop);
        if (moved > 0 && s.Units[body].Alive) ArriveByForce(s, body, start);
        return moved;
    }

    private static void Collide(BattleState s, int source, int body, string what)
    {
        s.Emit(EventType.Collide, source, body, s.Units[body].Pos, detail: what);
        HurtPair(s, source, body, s.Data.Rules.CollisionDamage, "collision");
    }

    private static void CollideUnits(BattleState s, int source, int body, int other)
    {
        s.Emit(EventType.Collide, source, body, s.Units[body].Pos, s.Units[other].Pos, detail: s.Label(other));
        int damage = s.Data.Rules.UnitCollisionDamage;
        if (damage <= 0) return;
        HurtPair(s, source, body, damage, "collision");
        HurtPair(s, source, other, damage, "collision");
    }

    /// <summary>A fall of two or more levels: damage for every level beyond the first.</summary>
    public static void Fall(BattleState s, int source, int body, int levels)
    {
        s.Emit(EventType.Fall, source, body, s.Units[body].Pos, amount: levels);
        if (levels >= 2) HurtPair(s, source, body, (levels - 1) * s.Data.Rules.FallDamagePerLevel, "fall");
    }

    /// <summary>Anything that enters no ground dies at once, rider and mount alike.</summary>
    private static void IntoNoGround(BattleState s, int source, int body, Pos where)
    {
        int rider = s.Units[body].Rider;
        s.Emit(EventType.Fall, source, body, where, amount: s.Heights[s.Idx(s.Units[body].Pos)], detail: "no_ground");
        if (rider >= 0)
        {
            s.Units[rider].Hp = 0;
            Defeat(s, rider, source);
        }
        s.Units[body].Hp = 0;
        Defeat(s, body, source);
    }

    /// <summary>Damages a unit and, if it's part of a mounted pair, its partner too.</summary>
    private static void HurtPair(BattleState s, int source, int body, int damage, string why)
    {
        if (damage <= 0) return;
        int rider = s.Units[body].Rider;
        DealDamage(s, source, body, damage, detail: why);
        if (rider >= 0) DealDamage(s, source, rider, damage, detail: why);
    }

    /// <summary>What happens when something lands on a tile by force rather than by walking (traps, later).</summary>
    private static void ArriveByForce(BattleState s, int body, Pos from) => Arrive(s, body, from);
}

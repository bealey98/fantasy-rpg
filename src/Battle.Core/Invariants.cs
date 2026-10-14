namespace Battle.Core;

/// <summary>
/// Rules that must always hold, checked after every action in tests, fuzzing and debug builds:
/// one unit per tile, HP within its limits, nobody standing on no ground, and the bookkeeping
/// (who stands where, who rides whom, who is under which rumbling tile) agreeing with itself.
/// </summary>
public static class Invariants
{
#if DEBUG
    public static bool Enabled { get; set; } = true;
#else
    public static bool Enabled { get; set; }
#endif

    public static void Check(BattleState s)
    {
        string? problem = Find(s);
        if (problem != null)
            throw new InvalidOperationException($"invariant broken in round {s.Round} ({Rules.PhaseName(s.Phase)}): {problem}");
    }

    public static string? Find(BattleState s)
    {
        // Every occupied tile points at a unit that stands there, on the board, and isn't riding.
        for (int tile = 0; tile < s.TileCount; tile++)
        {
            int unit = s.Occupant[tile];
            if (unit < 0) continue;
            ref var u = ref s.Units[unit];
            if (!u.OnBoard) return $"tile {s.PosOf(tile)} lists {s.Label(unit)}, who isn't on the board";
            if (s.Idx(u.Pos) != tile) return $"tile {s.PosOf(tile)} lists {s.Label(unit)}, who stands at {u.Pos}";
            if (u.MountedOn >= 0) return $"tile {s.PosOf(tile)} lists a rider, {s.Label(unit)}, instead of its mount";
        }

        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var u = ref s.Units[i];
            string name = s.Label(i);
            if (u.Hp < 0 || u.Hp > u.MaxHp) return $"{name} has {u.Hp} HP out of {u.MaxHp}";
            if (u.Alive && u.Hp == 0) return $"{name} is alive with 0 HP";
            if (!u.Alive && u.OnBoard) return $"{name} is out of the fight but still on the board";
            if (u.MoveLeft < 0) return $"{name} has negative movement";
            if (u.Rooted < 0 || u.Slowed < 0 || u.Blinded < 0 || u.Braced < 0 || u.Taunted < 0 || u.Buffed < 0)
                return $"{name} has a negative status timer";

            if (u.OnBoard)
            {
                if (!s.InBounds(u.Pos)) return $"{name} stands off the board at {u.Pos}";
                int tile = s.Idx(u.Pos);
                if (s.IsNoGround(tile)) return $"{name} stands on no ground at {u.Pos}";
                if (!s.Standable(tile)) return $"{name} stands inside something solid at {u.Pos}";
                int expected = u.MountedOn >= 0 ? u.MountedOn : i;
                if (s.Occupant[tile] != expected) return $"{name} stands at {u.Pos}, but the tile lists {s.Label(s.Occupant[tile])}";
            }

            if (u.MountedOn >= 0)
            {
                ref var mount = ref s.Units[u.MountedOn];
                if (mount.Rider != i) return $"{name} rides {s.Label(u.MountedOn)}, who doesn't carry them";
                if (mount.Pos != u.Pos) return $"{name} and their mount are on different tiles";
            }
            if (u.Rider >= 0 && s.Units[u.Rider].MountedOn != i) return $"{name} carries {s.Label(u.Rider)}, who doesn't ride them";

            if (u.Has(UnitFlags.Burrowed))
            {
                if (u.OnBoard) return $"{name} is burrowed but on the board";
                if (u.BurrowTo.IsNone || s.Rumble[s.Idx(u.BurrowTo)] != i) return $"{name} is burrowed without a rumbling tile";
            }
            if (u.Alive && u.OrderUnit >= 0 && !s.Units[u.OrderUnit].Alive) return $"{name} is after {s.Label(u.OrderUnit)}, who has left the fight";
        }

        int actor = Rules.CurrentActor(s);
        if (actor >= 0)
        {
            if (s.Units[actor].Has(UnitFlags.TurnDone)) return $"{s.Label(actor)} is acting after ending its turn";
            if (s.Phase == Phase.Party && !Rules.PartyTurnOpen(s, actor)) return $"{s.Label(actor)} has the controls without a turn going";
        }

        for (int tile = 0; tile < s.TileCount; tile++)
        {
            int beast = s.Rumble[tile];
            if (beast >= 0 && (!s.Units[beast].Has(UnitFlags.Burrowed) || s.Units[beast].BurrowTo != s.PosOf(tile)))
                return $"the ground at {s.PosOf(tile)} rumbles for {s.Label(beast)}, who isn't under it";
        }

        for (int t = 0; t < s.TetherCount; t++)
        {
            var tether = s.Tethers[t];
            if (!tether.Active) continue;
            if (tether.UnitA >= 0 && !s.Units[tether.UnitA].Alive) return $"a tether holds {s.Label(tether.UnitA)}, who is gone";
            if (tether.UnitB >= 0 && !s.Units[tether.UnitB].Alive) return $"a tether holds {s.Label(tether.UnitB)}, who is gone";
        }

        if (!s.Units[s.Info.HeroIndex].Alive && s.Outcome == Outcome.Ongoing) return "the hero is down but the battle goes on";
        return null;
    }
}

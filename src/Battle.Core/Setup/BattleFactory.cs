namespace Battle.Core;

/// <summary>Builds the starting state of a battle from an arena, an encounter and a seed.</summary>
public static class BattleFactory
{
    /// <summary>
    /// Units are numbered in a fixed order: the hero, the Loper (if present), then the enemies by
    /// letter. Logs and checksums depend on this order, so it must never change silently.
    /// </summary>
    public static BattleState Create(GameData data, ArenaDef arena, EncounterDef encounter, ulong seed, int battleId = 0)
    {
        var hero = arena.Units.FirstOrDefault(u => u.Glyph == 'H');
        if (hero.Glyph != 'H') throw new ArgumentException($"arena '{arena.Name}' has no hero (H) in its units layer");

        var placements = new List<(UnitPlacement Place, UnitDef Def, string Label)>
        {
            (hero, data.Unit("hero"), "hero"),
        };

        var loper = arena.Units.FirstOrDefault(u => u.Glyph == 'L');
        if (encounter.Party.Loper)
        {
            if (loper.Glyph != 'L' && !encounter.Party.Mounted)
                throw new ArgumentException($"arena '{arena.Name}' has no Loper (L) in its units layer");
            if (loper.Glyph != 'L') loper = new UnitPlacement('L', hero.Pos);
            placements.Add((loper, data.Unit("loper"), "loper"));
        }

        foreach (var enemy in arena.Units.Where(u => char.IsLower(u.Glyph)).OrderBy(u => u.Glyph))
        {
            if (!encounter.Enemies.TryGetValue(enemy.Glyph, out string? id))
                throw new ArgumentException($"encounter '{encounter.Id}' doesn't say who enemy '{enemy.Glyph}' in arena '{arena.Name}' is");
            var def = data.UnitById(id) ?? throw new ArgumentException($"encounter '{encounter.Id}': no unit called '{id}'");
            if (def.Side != Side.Enemy) throw new ArgumentException($"encounter '{encounter.Id}': '{id}' isn't an enemy");
            placements.Add((enemy, def, enemy.Glyph.ToString()));
        }
        foreach (char letter in encounter.Enemies.Keys)
            if (!arena.Units.Any(u => u.Glyph == letter))
                throw new ArgumentException($"encounter '{encounter.Id}' names enemy '{letter}', but arena '{arena.Name}' has no such unit");

        string[] labels = placements.Select(p => p.Label).ToArray();
        var info = new BattleInfo
        {
            BattleId = battleId,
            ArenaName = arena.Name,
            EncounterId = encounter.Id,
            Seed = seed,
            Time = arena.Time,
            Labels = labels,
            Kits = Kits.Build(data, encounter, placements.Select(p => p.Def).ToArray()),
            HeroIndex = 0,
            LoperIndex = encounter.Party.Loper ? 1 : -1,
            ElderwoodTier = encounter.Party.ElderwoodTier,
        };

        var s = new BattleState(data, info, arena.Width, arena.Height, placements.Count);
        Array.Copy(arena.Heights, s.Heights, arena.Heights.Length);
        Array.Copy(arena.Terrain, s.Terrain, arena.Terrain.Length);
        for (int i = 0; i < arena.Features.Length; i++)
        {
            int f = arena.Features[i];
            s.Feature[i] = f;
            if (f >= 0)
            {
                s.FeatureUses[i] = data.Features[f].Uses;
                s.FeatureHp[i] = data.Features[f].Hp;
            }
        }

        for (int i = 0; i < placements.Count; i++)
        {
            var def = placements[i].Def;
            s.Units[i] = new Unit
            {
                Def = def.Index,
                Side = def.Side,
                Weight = def.Weight,
                Pos = placements[i].Place.Pos,
                Hp = def.Hp,
                MaxHp = def.Hp,
                Defence = def.Defence,
                Power = def.Power,
                MoveMax = def.Move,
                MoveLeft = def.Move,
                Flags = UnitFlags.Alive,
                TauntBy = -1,
                MountedOn = -1,
                Rider = -1,
                BurrowTo = Pos.None,
                Handler = -1,
                OrderUnit = -1,
                OrderTile = Pos.None,
            };
        }

        // Place everyone. A mounted hero sits on the Loper's tile.
        for (int i = 0; i < placements.Count; i++)
        {
            if (i == info.HeroIndex && encounter.Party.Mounted && info.LoperIndex >= 0) continue;
            int tile = s.Idx(s.Units[i].Pos);
            if (s.Occupant[tile] >= 0) throw new ArgumentException($"two units start on {s.Units[i].Pos}");
            s.Place(i, s.Units[i].Pos);
        }
        if (encounter.Party.Mounted && info.LoperIndex >= 0)
        {
            ref var h = ref s.Units[info.HeroIndex];
            ref var l = ref s.Units[info.LoperIndex];
            h.MountedOn = info.LoperIndex;
            l.Rider = info.HeroIndex;
            h.Pos = l.Pos;
            h.Set(UnitFlags.OnBoard);
        }

        foreach (var (beast, handler) in encounter.Handlers)
        {
            int b = Array.IndexOf(labels, beast.ToString());
            int h = Array.IndexOf(labels, handler.ToString());
            if (b < 0 || h < 0) throw new ArgumentException($"encounter '{encounter.Id}': handler link {beast} → {handler} names a missing unit");
            s.Units[b].Handler = h;
        }

        s.LoperFirst = encounter.Party.LoperFirst;
        s.Rng = new Rng(seed);
        s.Round = 0;
        s.Phase = encounter.FirstPhase == Side.Enemy ? Phase.Enemy : Phase.Party;
        s.Outcome = Outcome.Ongoing;
        return s;
    }
}

/// <summary>Works out each unit's moves. Moves arrive in step 4; until then every kit is empty.</summary>
public static class Kits
{
    public static int[][] Build(GameData data, EncounterDef encounter, UnitDef[] units) =>
        units.Select(_ => Array.Empty<int>()).ToArray();
}

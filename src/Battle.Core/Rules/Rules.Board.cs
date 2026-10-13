namespace Battle.Core;

public static partial class Rules
{
    // ------------------------------------------------------------------
    // The board between and after phases
    //   - The creatures step (between the party and enemy phases): living features move. Fireflies
    //     drift; birds take fright when an attack has landed within 2 tiles; deer run when anyone
    //     comes within 2 tiles. None of this is excessive: one move each per round at most.
    //   - Phase ends: trap and tether timers count down.
    //   - Round end: sources regain uses, and sapping tethers sap.
    // ------------------------------------------------------------------

    private static void CreatureStep(BattleState s)
    {
        // Gather first, so a creature that moves isn't moved twice.
        var creatures = new List<int>();
        for (int tile = 0; tile < s.TileCount; tile++)
            if (s.Feature[tile] >= 0 && !s.FeatureDead[tile] && s.FeatureAt(tile)!.IsCreature) creatures.Add(tile);

        foreach (int tile in creatures)
        {
            var def = s.FeatureAt(tile)!;
            var here = s.PosOf(tile);
            switch (def.Creature)
            {
                case CreatureBehaviour.Drift:
                    if (s.Rng.Chance(50))
                    {
                        var options = Dirs.All.Select(d => here.Step(d)).Where(p => CreatureCanEnter(s, p)).ToList();
                        if (options.Count > 0) MoveCreature(s, tile, options[s.Rng.Next(options.Count)]);
                    }
                    break;
                case CreatureBehaviour.FleeAttack:
                {
                    var threats = Enumerable.Range(0, s.TileCount).Where(t => s.Disturbed[t]).Select(s.PosOf).ToList();
                    if (threats.Any(p => p.Manhattan(here) <= 2)) Flee(s, tile, threats);
                    break;
                }
                case CreatureBehaviour.FleeNear:
                {
                    var threats = s.Units.Where(u => u.Alive && u.OnBoard).Select(u => u.Pos).ToList();
                    if (threats.Any(p => p.Manhattan(here) <= 2)) Flee(s, tile, threats);
                    break;
                }
            }
        }
        Array.Clear(s.Disturbed);
    }

    private static bool CreatureCanEnter(BattleState s, Pos p)
    {
        if (!s.InBounds(p)) return false;
        int tile = s.Idx(p);
        return s.Feature[tile] < 0 && s.Occupant[tile] < 0 && s.Standable(tile) && s.Trap[tile] == TrapKind.None;
    }

    /// <summary>A frightened creature moves to the free tile within 3 steps that's furthest from every threat.</summary>
    private static void Flee(BattleState s, int tile, List<Pos> threats)
    {
        var here = s.PosOf(tile);
        Pos best = here;
        int bestScore = threats.Min(p => p.Manhattan(here));
        for (int t = 0; t < s.TileCount; t++)
        {
            var p = s.PosOf(t);
            if (p.Manhattan(here) > 3 || !CreatureCanEnter(s, p)) continue;
            int score = threats.Min(q => q.Manhattan(p));
            if (score > bestScore) { best = p; bestScore = score; }
        }
        if (best != here) MoveCreature(s, tile, best);
    }

    private static void MoveCreature(BattleState s, int from, Pos to)
    {
        int target = s.Idx(to);
        s.Feature[target] = s.Feature[from];
        s.FeatureUses[target] = s.FeatureUses[from];
        s.FeatureHp[target] = s.FeatureHp[from];
        s.FeatureDead[target] = false;
        s.Feature[from] = -1;
        s.FeatureUses[from] = 0;
        s.FeatureHp[from] = 0;
        s.Emit(EventType.CreatureMove, from: s.PosOf(from), to: to, detail: s.FeatureAt(target)!.Name.ToLowerInvariant());
    }

    private static void TickBoardTimers(BattleState s)
    {
        for (int tile = 0; tile < s.TileCount; tile++)
        {
            if (s.Trap[tile] == TrapKind.None || s.TrapTimer[tile] <= 0) continue;
            if (--s.TrapTimer[tile] > 0) continue;
            bool bind = s.Trap[tile] != TrapKind.Snap;
            s.Emit(EventType.TrapEnd, s.TrapOwner[tile], -1, to: s.PosOf(tile), detail: bind ? "bind" : "snap");
            s.Trap[tile] = TrapKind.None;
            s.TrapOwner[tile] = -1;
        }
        for (int i = 0; i < s.TetherCount; i++)
        {
            ref var t = ref s.Tethers[i];
            if (t.Active && --t.Timer <= 0) EndTether(s, i, "it fades");
        }
    }

    private static void RoundEndEffects(BattleState s)
    {
        SapTethers(s);
        RegenerateSources(s);
    }
}

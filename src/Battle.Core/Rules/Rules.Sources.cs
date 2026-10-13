namespace Battle.Core;

/// <summary>What paying for a path move came to.</summary>
public enum SourcePayment : byte
{
    /// <summary>Paid from a feature: the move works at full strength.</summary>
    Full,
    /// <summary>A light move with no feature in range that has uses left: it still works, but weaker.</summary>
    Weak,
}

public static partial class Rules
{
    // ------------------------------------------------------------------
    // Magic sources
    //   - Path moves draw on natural features of their element within source range of the caster.
    //   - Light moves spend one use from the nearest feature that has uses. Uses come back each
    //     round. With none in range, the move is cast weaker instead.
    //   - Dark moves drain HP from the nearest living feature, which dies at 0. With no living
    //     feature in range, the hero pays in his own HP (the companion if he can't).
    //   - Every dark move nudges the corruption meter.
    // ------------------------------------------------------------------

    /// <summary>Does this feature feed the element and still live?</summary>
    public static bool Feeds(BattleState s, int tile, Element element) =>
        s.Feature[tile] >= 0 && !s.FeatureDead[tile] && (s.FeatureAt(tile)!.Feeds & element) != 0;

    /// <summary>The nearest feature of an element within source range that satisfies a test, or -1.</summary>
    public static int NearestSource(BattleState s, Pos caster, Element element, Func<int, bool> usable)
    {
        int best = -1, bestDistance = int.MaxValue;
        int range = s.Data.Rules.SourceRange;
        for (int tile = 0; tile < s.TileCount; tile++)
        {
            if (!Feeds(s, tile, element) || !usable(tile)) continue;
            int d = caster.Manhattan(s.PosOf(tile));
            if (d <= range && d < bestDistance) { best = tile; bestDistance = d; }
        }
        return best;
    }

    /// <summary>Can a dark move be paid for right now, from a feature or from the party's HP?</summary>
    public static bool CanPayDark(BattleState s, int caster, MoveDef move)
    {
        if (NearestSource(s, s.Units[caster].Pos, move.Element, t => s.FeatureHp[t] > 0) >= 0) return true;
        return DarkPayer(s, move) >= 0;
    }

    /// <summary>Who pays a dark move in HP when no source is left: the hero, else the companion, else nobody.</summary>
    private static int DarkPayer(BattleState s, MoveDef move)
    {
        int hero = s.Info.HeroIndex, loper = s.Info.LoperIndex;
        if (s.Units[hero].Hp > move.DarkCost) return hero;
        if (loper >= 0 && s.Units[loper].Alive && s.Units[loper].Hp > move.DarkCost) return loper;
        return -1;
    }

    /// <summary>Pays for a path move. Light moves spend a use; dark moves drain a feature or the party.</summary>
    public static SourcePayment PayForMove(BattleState s, int caster, MoveDef move)
    {
        if (move.Source == SourceKind.None) return SourcePayment.Full;
        var at = s.Units[caster].Pos;
        string featureName(int tile) => s.FeatureAt(tile)!.Name.ToLowerInvariant();

        if (move.Source == SourceKind.Light)
        {
            int tile = NearestSource(s, at, move.Element, t => s.FeatureUses[t] > 0);
            if (tile < 0) return SourcePayment.Weak;
            s.FeatureUses[tile]--;
            s.Emit(EventType.SourceUse, caster, -1, at, s.PosOf(tile), s.FeatureUses[tile], move: move.Index, detail: featureName(tile));
            return SourcePayment.Full;
        }

        // Dark: drain a living feature, or the party's own HP.
        s.DarkUses++;
        int source = NearestSource(s, at, move.Element, t => s.FeatureHp[t] > 0);
        if (source >= 0)
        {
            s.FeatureHp[source] = Math.Max(0, s.FeatureHp[source] - move.DarkCost);
            s.Emit(EventType.SourceDrain, caster, -1, at, s.PosOf(source), s.FeatureHp[source], move: move.Index, detail: featureName(source));
            if (s.FeatureHp[source] == 0)
            {
                s.FeatureDead[source] = true;
                s.FeatureUses[source] = 0;
                s.Emit(EventType.SourceDeath, caster, -1, to: s.PosOf(source), move: move.Index, detail: featureName(source));
            }
        }
        else
        {
            int payer = DarkPayer(s, move);
            if (payer >= 0) DealDamage(s, caster, payer, move.DarkCost, move.Index, detail: "dark_cost");
        }
        s.Emit(EventType.Corruption, caster, amount: s.DarkUses, move: move.Index);
        return SourcePayment.Full;
    }

    /// <summary>At the end of each round, living features regain uses up to their maximum.</summary>
    private static void RegenerateSources(BattleState s)
    {
        for (int tile = 0; tile < s.TileCount; tile++)
        {
            if (s.Feature[tile] < 0 || s.FeatureDead[tile]) continue;
            var def = s.FeatureAt(tile)!;
            s.FeatureUses[tile] = Math.Min(def.Uses, s.FeatureUses[tile] + def.Regen);
        }
    }
}

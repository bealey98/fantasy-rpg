using Battle.Core;

namespace Battle.Content;

/// <summary>Plain-English lines for events, for the terminal app and verbose simulator output.</summary>
public static class Describe
{
    /// <summary>A unit's display name: "Hero", "Loper", or the unit type plus its letter, like "Cutter a".</summary>
    public static string Name(BattleState s, int unit)
    {
        if (unit < 0) return "";
        var def = s.DefOf(unit);
        return s.Units[unit].Side == Side.Party ? def.Name : $"{def.Name} {s.Label(unit)}";
    }

    public static string MoveName(BattleState s, int move) => move >= 0 ? s.Data.Moves[move].Name : "";

    /// <summary>One line for an event, or null for bookkeeping events nobody needs to read.</summary>
    public static string? Event(BattleState s, in BattleEvent e)
    {
        string actor = Name(s, e.Actor);
        string target = Name(s, e.Target);
        string move = MoveName(s, e.Move);
        string timing = e.Timing switch
        {
            Timing.Fumble => " (fumbled timing)",
            Timing.Good => " (good timing)",
            Timing.Perfect => " (perfect timing)",
            _ => "",
        };

        return e.Type switch
        {
            EventType.RoundStart => $"── Round {e.Amount} ──",
            EventType.Move => $"{target} moves {e.From} → {e.To}",
            EventType.Charge => $"{target} charges {e.From} → {e.To}",
            EventType.Push => $"{target} is pushed {e.From} → {e.To}",
            EventType.Pull => $"{target} is pulled {e.From} → {e.To}",
            EventType.Drag => $"{target} is dragged {e.From} → {e.To}",
            EventType.Swap => $"{target} swaps to {e.To}",
            EventType.Collide => $"{target} slams into {e.Detail}",
            EventType.Fall => $"{target} falls {e.Amount} level{(e.Amount == 1 ? "" : "s")}",
            EventType.Damage when e.Detail == "dark_cost" => $"{target} pays {e.Amount} HP for dark magic ({e.HpAfter} HP left)",
            EventType.Damage when e.Move >= 0 && e.Actor >= 0 => $"{actor} hits {target} with {move}: {e.Amount} damage{timing} ({e.HpAfter} HP left)",
            EventType.Damage => $"{target} takes {e.Amount} damage{(e.Detail != null ? $" ({e.Detail.Replace('_', ' ')})" : "")} ({e.HpAfter} HP left)",
            EventType.Miss => $"{actor}'s {move} misses {target}{timing}",
            EventType.Heal => $"{target} recovers {e.Amount} HP ({e.HpAfter} HP)",
            EventType.Death => $"{target} is defeated",
            EventType.KnockedOut => $"{target} is knocked out",
            EventType.Fled => $"{target} flees the battle",
            EventType.FleeStart => $"{target} loses its nerve and runs for it!",
            EventType.Status => $"{target} is {StatusWords(e.Detail)}{(e.Amount > 0 ? $" for {e.Amount} round{(e.Amount == 1 ? "" : "s")}" : "")}",
            EventType.StatusEnd when e.Detail == "knocked_over" => $"{target} gets up (loses {e.Amount / 2.0:0.#} tiles of movement)",
            EventType.StatusEnd when e.Detail == "dazed" => $"{target} shakes off the daze (no bonus action this turn)",
            EventType.StatusEnd => $"{target} is no longer {StatusWords(e.Detail)}",
            EventType.Parry => $"{target} parries {actor}'s attack",
            EventType.Counter => $"{actor} strikes back at {target}{timing}",
            EventType.TrapSet => $"{actor} sets a {e.Detail} trap at {e.To}",
            EventType.TrapTriggered => $"{target} is caught by a {e.Detail} trap at {e.To}",
            EventType.TrapEnd => $"the {e.Detail} trap at {e.To} crumbles",
            EventType.TetherSet => $"{actor} tethers {e.Detail}",
            EventType.TetherEnd => $"the tether {e.Detail} breaks",
            EventType.SourceUse => $"{actor} draws on the {e.Detail} at {e.To} ({e.Amount} uses left)",
            EventType.SourceDrain => $"{actor} drains the {e.Detail} at {e.To} ({e.Amount} HP left)",
            EventType.SourceDeath => $"the {e.Detail} at {e.To} withers and dies",
            EventType.Corruption => $"dark magic stirs (dark moves used: {e.Amount})",
            EventType.Burrow => $"{target} burrows underground; the ground rumbles at {e.To}",
            EventType.Surface => $"{target} bursts out of the ground at {e.To}",
            EventType.Crater => $"the ground at {e.To} caves into a crater",
            EventType.Order when e.Detail == "wild" => $"{actor}, wild, goes for {target}",
            EventType.Order => e.Target >= 0 ? $"{actor} points the beast at {target}" : $"{actor} points the beast at {e.To}",
            EventType.Buff => $"{actor} cracks the whip at {target} (+{e.Amount} damage on its next attack)",
            EventType.Mount => $"{actor} mounts {target}",
            EventType.Dismount => $"{actor} dismounts to {e.To}",
            EventType.Thrown => $"{target} is thrown off to {e.To}",
            EventType.Scamper => $"{actor} scampers (+{e.Amount / 2.0:0.#} tiles of movement)",
            EventType.SwapPartner => $"{actor} hands over to {target}",
            EventType.CreatureMove => $"the {e.Detail} move {e.From} → {e.To}",
            EventType.BattleEnd => e.Detail switch
            {
                "party_won" => "Victory!",
                "party_lost" => "Defeat.",
                "party_fled" => "The party flees the battle.",
                _ => "The battle ends in a draw.",
            },
            _ => null,
        };
    }

    /// <summary>A short label for an action, for menus: "Strike → Cutter a (4 HP)".</summary>
    public static string Action(BattleState s, in BattleAction a)
    {
        string move = MoveName(s, a.Move);
        return a.Kind switch
        {
            ActionKind.End => "End turn",
            ActionKind.Move => $"Move to {a.Target.Tile}",
            ActionKind.SwapPartner => $"Swap to the {Name(s, a.Target.Unit)}",
            ActionKind.Flee => "Flee the battle",
            ActionKind.Mount => $"Mount the {Name(s, a.Target.Unit)}",
            ActionKind.Dismount => $"Dismount to {a.Target.Tile}",
            _ => a.Target2.Kind == TargetKind.None
                ? $"{move} → {TargetText(s, a.Target)}"
                : $"{move} → {TargetText(s, a.Target)} ~ {TargetText(s, a.Target2)}",
        };
    }

    public static string TargetText(BattleState s, Target t) => t.Kind switch
    {
        TargetKind.Unit => $"{Name(s, t.Unit)} ({s.Units[t.Unit].Hp} HP)",
        TargetKind.Tile when s.InBounds(t.Tile) && s.FeatureAt(s.Idx(t.Tile)) is FeatureDef f && f.IsObject => $"{f.Name.ToLowerInvariant()} at {t.Tile}",
        TargetKind.Tile => $"tile {t.Tile}",
        TargetKind.Self => "self",
        TargetKind.Weapon => "your weapon",
        _ => "",
    };

    private static string StatusWords(string? status) => status switch
    {
        "rooted" => "rooted",
        "slowed" => "slowed",
        "blinded" => "blinded",
        "dazed" => "dazed",
        "knocked_over" => "knocked over",
        "taunted" => "taunted",
        "braced" => "braced",
        "buffed" => "spurred on",
        "weapon_bind" => "carrying a weapon bind",
        "parry_ready" => "ready to parry",
        _ => status?.Replace('_', ' ') ?? "",
    };
}

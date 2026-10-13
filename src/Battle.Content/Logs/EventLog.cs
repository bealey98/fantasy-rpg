using System.Text;
using Battle.Core;

namespace Battle.Content.Logs;

/// <summary>
/// Event logs: one CSV row per event, tidy enough for R's read.csv. Each event carries what changed
/// (including HP afterwards), so the viewer and R can follow a battle without running the core.
/// Many battles can share one file; battle_id tells them apart.
/// </summary>
public static class EventLog
{
    public const string Header =
        "battle_id,round,phase,seq,actor,event,move,target,from_x,from_y,to_x,to_y,amount,hp_after,timing,detail,checksum";

    public static void WriteHeader(TextWriter w) => w.WriteLine(Header);

    public static void Write(TextWriter w, BattleState s, IEnumerable<BattleEvent> events)
    {
        foreach (var e in events) w.WriteLine(Row(s, e));
    }

    public static string Row(BattleState s, in BattleEvent e)
    {
        var sb = new StringBuilder(96);
        sb.Append(s.Info.BattleId).Append(',');
        sb.Append(e.Round).Append(',');
        sb.Append(Rules.PhaseName(e.Phase)).Append(',');
        sb.Append(e.Seq).Append(',');
        sb.Append(s.Label(e.Actor)).Append(',');
        sb.Append(EventName(e.Type)).Append(',');
        sb.Append(e.Move >= 0 ? s.Data.Moves[e.Move].Id : "").Append(',');
        sb.Append(s.Label(e.Target)).Append(',');
        AppendPos(sb, e.From).Append(',');
        AppendPos(sb, e.To).Append(',');
        sb.Append(e.Amount).Append(',');
        sb.Append(HasHp(e.Type) ? e.HpAfter.ToString() : "").Append(',');
        sb.Append(e.Timing == Timing.None ? "" : e.Timing.ToString().ToLowerInvariant()).Append(',');
        sb.Append(Quote(e.Detail)).Append(',');
        sb.Append(e.Checksum == 0 ? "" : e.Checksum.ToString("x16"));
        return sb.ToString();
    }

    /// <summary>EventType.TrapTriggered → "trap_triggered".</summary>
    public static string EventName(EventType type) => MovesLoader.Snake(type.ToString());

    private static bool HasHp(EventType type) => type is EventType.Damage or EventType.Heal;

    private static StringBuilder AppendPos(StringBuilder sb, Pos p) =>
        p.IsNone ? sb.Append(',') : sb.Append(p.X).Append(',').Append(p.Y);

    private static string Quote(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.IndexOfAny([',', '"', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}

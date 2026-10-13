using System.Text;
using System.Text.Json;
using Battle.Core;

namespace Battle.Content.Logs;

/// <summary>
/// Round snapshots: the full board and every unit at the start of each round, one JSON line per
/// round. Tools jump straight to a round by loading its snapshot and applying the events after
/// its "seq".
/// </summary>
public static class Snapshots
{
    public static string Line(BattleState s)
    {
        var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("battle_id", s.Info.BattleId);
            w.WriteNumber("round", s.Round);
            w.WriteNumber("seq", s.Seq);
            w.WriteString("phase", Rules.PhaseName(s.Phase));
            w.WriteString("actor", s.Label(Rules.CurrentActor(s)));
            w.WriteString("arena", s.Info.ArenaName);
            w.WriteString("encounter", s.Info.EncounterId);
            w.WriteString("time", s.Info.Time);
            w.WriteNumber("width", s.Width);
            w.WriteNumber("height", s.Height);

            WriteRows(w, "heights", s, i => (char)('0' + s.Heights[i]));
            WriteRows(w, "terrain", s, i => s.TerrainAt(i).Glyph);
            WriteRows(w, "features", s, i => s.FeatureAt(i)?.Glyph ?? '.');

            w.WriteStartArray("sources");
            for (int i = 0; i < s.TileCount; i++)
            {
                if (s.Feature[i] < 0) continue;
                var p = s.PosOf(i);
                w.WriteStartObject();
                w.WriteNumber("x", p.X);
                w.WriteNumber("y", p.Y);
                w.WriteString("feature", s.FeatureAt(i)!.Id);
                w.WriteNumber("uses", s.FeatureUses[i]);
                w.WriteNumber("hp", s.FeatureHp[i]);
                w.WriteBoolean("dead", s.FeatureDead[i]);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("traps");
            for (int i = 0; i < s.TileCount; i++)
            {
                if (s.Trap[i] == TrapKind.None) continue;
                var p = s.PosOf(i);
                w.WriteStartObject();
                w.WriteNumber("x", p.X);
                w.WriteNumber("y", p.Y);
                w.WriteString("kind", s.Trap[i].ToString().ToLowerInvariant());
                w.WriteString("owner", s.Label(s.TrapOwner[i]));
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("rumbles");
            for (int i = 0; i < s.TileCount; i++)
            {
                if (s.Rumble[i] < 0) continue;
                var p = s.PosOf(i);
                w.WriteStartObject();
                w.WriteNumber("x", p.X);
                w.WriteNumber("y", p.Y);
                w.WriteString("unit", s.Label(s.Rumble[i]));
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("units");
            for (int i = 0; i < s.Units.Length; i++) WriteUnit(w, s, i);
            w.WriteEndArray();

            w.WriteStartArray("tethers");
            for (int t = 0; t < s.TetherCount; t++)
            {
                var tether = s.Tethers[t];
                if (!tether.Active) continue;
                w.WriteStartObject();
                w.WriteString("a", End(s, tether.UnitA, tether.TileA));
                w.WriteString("b", End(s, tether.UnitB, tether.TileB));
                w.WriteNumber("length", tether.Length);
                w.WriteString("variant", tether.Variant.ToString().ToLowerInvariant());
                w.WriteNumber("rounds_left", (tether.Timer + 1) / 2);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            // The party's danger zone: every tile an enemy could hit on its next turn.
            var danger = Observation.Danger(s, Side.Party);
            w.WriteStartArray("danger");
            for (int i = 0; i < danger.Length; i++)
            {
                if (!danger[i]) continue;
                var p = s.PosOf(i);
                w.WriteStartArray();
                w.WriteNumberValue(p.X);
                w.WriteNumberValue(p.Y);
                w.WriteEndArray();
            }
            w.WriteEndArray();

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string End(BattleState s, int unit, Pos tile) => unit >= 0 ? s.Label(unit) : $"{tile.X},{tile.Y}";

    private static void WriteRows(Utf8JsonWriter w, string name, BattleState s, Func<int, char> glyph)
    {
        w.WriteStartArray(name);
        var row = new StringBuilder(s.Width);
        for (int y = 0; y < s.Height; y++)
        {
            row.Clear();
            for (int x = 0; x < s.Width; x++) row.Append(glyph(y * s.Width + x));
            w.WriteStringValue(row.ToString());
        }
        w.WriteEndArray();
    }

    private static void WriteUnit(Utf8JsonWriter w, BattleState s, int i)
    {
        ref var u = ref s.Units[i];
        var def = s.DefOf(i);
        w.WriteStartObject();
        w.WriteString("label", s.Label(i));
        w.WriteString("type", def.Id);
        w.WriteString("name", def.Name);
        w.WriteString("side", u.Side == Side.Party ? "party" : "enemy");
        w.WriteNumber("x", u.Pos.X);
        w.WriteNumber("y", u.Pos.Y);
        w.WriteNumber("hp", u.Hp);
        w.WriteNumber("max_hp", u.MaxHp);
        w.WriteBoolean("alive", u.Alive);
        w.WriteBoolean("on_board", u.OnBoard);
        w.WriteString("weight", u.Weight.ToString().ToLowerInvariant());
        if (u.MountedOn >= 0) w.WriteString("riding", s.Label(u.MountedOn));
        if (u.Rider >= 0) w.WriteString("carrying", s.Label(u.Rider));

        w.WriteStartObject("statuses");
        void Status(string name, int timer) { if (timer > 0) w.WriteNumber(name, (timer + 1) / 2); }
        Status("rooted", u.Rooted);
        Status("slowed", u.Slowed);
        Status("blinded", u.Blinded);
        Status("braced", u.Braced);
        Status("taunted", u.Taunted);
        Status("buffed", u.Buffed);
        if (u.Has(UnitFlags.Dazed)) w.WriteNumber("dazed", 1);
        if (u.Has(UnitFlags.KnockedOver)) w.WriteNumber("knocked_over", 1);
        if (u.Has(UnitFlags.ParryReady)) w.WriteNumber("parry_ready", 1);
        if (u.Has(UnitFlags.WeaponBind)) w.WriteNumber("weapon_bind", 1);
        if (u.Has(UnitFlags.Burrowed)) w.WriteNumber("burrowed", 1);
        if (u.Has(UnitFlags.Fleeing)) w.WriteNumber("fleeing", 1);
        if (u.Has(UnitFlags.Fled)) w.WriteNumber("fled", 1);
        if (u.Has(UnitFlags.KnockedOut)) w.WriteNumber("knocked_out", 1);
        w.WriteEndObject();
        w.WriteEndObject();
    }
}

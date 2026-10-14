using System.Text;
using Battle.Core;

namespace Battle.Content;

/// <summary>
/// Draws boards as text. Each tile shows its most important thing: a unit, else a feature,
/// else its terrain. Heights are printed as a second grid alongside, or as background
/// shading when colour is on.
/// </summary>
public static class AsciiRender
{
    /// <summary>One tile as drawn: its character, its height and an optional ANSI colour code for the character.</summary>
    public readonly record struct Cell(char Glyph, int Height, string? Colour = null, bool Marked = false);

    public static string Arena(ArenaDef arena, GameData data, bool colour = false)
    {
        var unitAt = arena.Units.ToDictionary(u => arena.Index(u.Pos), u => u.Glyph);
        var cells = new Cell[arena.Width * arena.Height];
        for (int i = 0; i < cells.Length; i++)
        {
            char glyph = unitAt.TryGetValue(i, out char u) ? u
                : arena.Features[i] >= 0 ? data.Features[arena.Features[i]].Glyph
                : data.Terrain[arena.Terrain[i]].Glyph;
            string? fg = unitAt.ContainsKey(i) ? (u == 'H' || u == 'L' ? Ansi.Party : Ansi.Enemy) : null;
            cells[i] = new Cell(glyph, arena.Heights[i], fg);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{arena.Name} · {arena.Width} x {arena.Height} · {arena.Time}");
        sb.AppendLine();
        sb.Append(Grid(arena.Width, arena.Height, cells, colour));
        sb.AppendLine();
        sb.Append(Legend(arena, data));
        return sb.ToString();
    }

    /// <summary>
    /// A battle in progress. Units show as their letters (H hero, L Loper, a b c enemies; a mounted
    /// pair shows as H), traps as ^, rumbling ground as !, and dead features dimmed.
    /// <paramref name="marked"/> highlights tiles: reachable tiles or a danger zone.
    /// </summary>
    public static string Board(BattleState s, bool[]? marked, bool colour, string[]? sidePanel = null, bool heights = true)
    {
        var cells = new Cell[s.TileCount];
        for (int i = 0; i < cells.Length; i++)
        {
            char glyph;
            string? fg = null;
            int unit = s.Occupant[i];
            if (unit >= 0)
            {
                int shown = s.Units[unit].Rider >= 0 ? s.Units[unit].Rider : unit;
                glyph = UnitGlyph(s, shown);
                fg = s.Units[shown].Side == Side.Party ? Ansi.Party : Ansi.Enemy;
            }
            else if (s.Rumble[i] >= 0) { glyph = '!'; fg = Ansi.Warn; }
            else if (s.Trap[i] == TrapKind.Snap) { glyph = '^'; fg = Ansi.Warn; }
            else if (s.Trap[i] != TrapKind.None) { glyph = '"'; fg = Ansi.Feature; }
            else if (s.Feature[i] >= 0)
            {
                glyph = s.FeatureAt(i)!.Glyph;
                fg = s.FeatureDead[i] ? Ansi.Dim : Ansi.Feature;
            }
            else glyph = s.TerrainAt(i).Glyph;
            cells[i] = new Cell(glyph, s.Heights[i], colour || Ansi.Enabled ? fg : null, marked != null && marked[i]);
        }
        return Grid(s.Width, s.Height, cells, colour, sidePanel, heights);
    }

    /// <summary>The heights on their own, as a grid: 0 no ground, 1 low, 2 base, 3 high, 4 very high.</summary>
    public static string Heights(BattleState s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    " + string.Join(" ", Enumerable.Range(0, s.Width).Select(x => (x % 10).ToString())));
        for (int y = 0; y < s.Height; y++)
            sb.AppendLine(y.ToString().PadLeft(2) + "  " + string.Join(" ", Enumerable.Range(0, s.Width).Select(x => s.Heights[y * s.Width + x])));
        return sb.ToString();
    }

    public static char UnitGlyph(BattleState s, int unit) =>
        unit == s.Info.HeroIndex ? 'H' : unit == s.Info.LoperIndex ? 'L' : s.Label(unit)[0];

    /// <summary>One line per unit: name, an HP bar and its statuses.</summary>
    public static string[] UnitPanel(BattleState s)
    {
        var lines = new List<string>();
        int nameWidth = Enumerable.Range(0, s.Units.Length).Max(i => Describe.Name(s, i).Length);
        for (int i = 0; i < s.Units.Length; i++)
        {
            ref var u = ref s.Units[i];
            string name = Describe.Name(s, i).PadRight(nameWidth);
            if (!u.Alive)
            {
                string why = u.Has(UnitFlags.Fled) ? "fled" : u.Has(UnitFlags.KnockedOut) ? "knocked out" : "defeated";
                lines.Add($"{name}  {why}");
                continue;
            }
            int filled = (int)Math.Round(10.0 * u.Hp / u.MaxHp);
            string bar = new string('█', filled) + new string('░', 10 - filled);
            lines.Add($"{name}  {bar} {u.Hp,2}/{u.MaxHp,-2} {Statuses(s, i)}".TrimEnd());
        }
        return lines.ToArray();
    }

    public static string Statuses(BattleState s, int i)
    {
        ref var u = ref s.Units[i];
        var parts = new List<string>();
        void Timed(string name, int timer) { if (timer > 0) parts.Add($"{name} {(timer + 1) / 2}"); }
        Timed("rooted", u.Rooted);
        Timed("slowed", u.Slowed);
        Timed("blinded", u.Blinded);
        Timed("braced", u.Braced);
        Timed("taunted", u.Taunted);
        if (u.BuffAmount > 0) parts.Add($"+{u.BuffAmount} next hit");
        if (u.Has(UnitFlags.Dazed)) parts.Add("dazed");
        if (u.Has(UnitFlags.KnockedOver)) parts.Add("knocked over");
        if (u.Has(UnitFlags.ParryReady)) parts.Add("parrying");
        if (u.Has(UnitFlags.WeaponBind)) parts.Add("weapon bind");
        if (u.Has(UnitFlags.Burrowed)) parts.Add($"burrowed → {u.BurrowTo}");
        if (u.Has(UnitFlags.Fleeing)) parts.Add("fleeing");
        if (Rules.IsWild(s, i)) parts.Add("wild");
        if (u.OrderTile != Pos.None) parts.Add($"told to dig at {u.OrderTile}");
        if (u.OrderUnit >= 0) parts.Add($"after {Describe.Name(s, u.OrderUnit)}");
        else if (u.Handler >= 0 && !Rules.IsWild(s, i)) parts.Add("waiting for orders");
        if (u.MountedOn >= 0) parts.Add($"riding {Describe.Name(s, u.MountedOn)}");
        for (int t = 0; t < s.TetherCount; t++)
            if (s.Tethers[t].Involves(i)) parts.Add("tethered");
        return string.Join(", ", parts);
    }

    /// <summary>
    /// The board itself, with column and row numbers. Without colour, heights are printed as a
    /// second grid to the right (unless <paramref name="heights"/> is off, for narrow screens);
    /// with colour, each tile's background is shaded by height.
    /// </summary>
    public static string Grid(int width, int height, Cell[] cells, bool colour, string[]? sidePanel = null, bool heights = true)
    {
        bool heightGrid = !colour && heights;
        // Every board line has the same visible width, so the side panel lines up.
        int boardWidth = colour ? 4 + 2 * width : 4 + (2 * width - 1) + (heightGrid ? 6 + (2 * width - 1) : 0);
        var lines = new List<(string Text, int Visible)>();

        string columns = string.Join(" ", Enumerable.Range(0, width).Select(x => (x % 10).ToString()));
        string head = "    " + columns + (heightGrid ? "      " + string.Join(" ", Enumerable.Range(0, width).Select(x => (x % 10).ToString())) : "");
        lines.Add((head, head.Length));

        for (int y = 0; y < height; y++)
        {
            var sb = new StringBuilder();
            sb.Append(y.ToString().PadLeft(2)).Append("  ");
            for (int x = 0; x < width; x++)
            {
                var cell = cells[y * width + x];
                if (colour)
                {
                    sb.Append(Ansi.Background(cell.Height, cell.Marked));
                    if (cell.Colour != null) sb.Append(cell.Colour);
                    sb.Append(cell.Glyph).Append(' ').Append(Ansi.Reset);
                }
                else
                {
                    char glyph = cell.Marked && cell.Glyph == '.' ? '*' : cell.Glyph;
                    if (cell.Colour != null && Ansi.Enabled) sb.Append(cell.Colour).Append(glyph).Append(Ansi.Reset);
                    else sb.Append(glyph);
                    if (x < width - 1) sb.Append(' ');
                }
            }
            if (heightGrid)
            {
                sb.Append("      ");
                for (int x = 0; x < width; x++)
                {
                    sb.Append(cells[y * width + x].Height);
                    if (x < width - 1) sb.Append(' ');
                }
            }
            lines.Add((sb.ToString(), boardWidth));
        }

        int panelLines = sidePanel?.Length ?? 0;
        var output = new StringBuilder();
        for (int i = 0; i < Math.Max(lines.Count, panelLines); i++)
        {
            var (text, visible) = i < lines.Count ? lines[i] : ("", 0);
            output.Append(text);
            if (sidePanel != null && i < sidePanel.Length)
                output.Append(new string(' ', Math.Max(0, boardWidth - visible) + 4)).Append(sidePanel[i]);
            output.AppendLine();
        }
        return output.ToString();
    }

    private static string Legend(ArenaDef arena, GameData data)
    {
        var map = new List<string>();
        foreach (var t in data.Terrain)
            if (t.Glyph != '.' && arena.Terrain.Any(b => b == t.Index)) map.Add($"{t.Glyph} {t.Name.ToLowerInvariant()}");
        foreach (var f in data.Features)
            if (arena.Features.Any(s => s == f.Index)) map.Add($"{f.Glyph} {f.Name.ToLowerInvariant()}");

        var units = new List<string>();
        if (arena.Units.Any(u => u.Glyph == 'H')) units.Add("H hero");
        if (arena.Units.Any(u => u.Glyph == 'L')) units.Add("L Loper");
        var enemies = arena.Units.Where(u => char.IsLower(u.Glyph)).Select(u => u.Glyph).ToList();
        if (enemies.Count > 0) units.Add($"{string.Join(" ", enemies)} enemies");

        return $"map:     {string.Join("   ", map)}{Environment.NewLine}" +
               $"units:   {string.Join("   ", units)}{Environment.NewLine}" +
               $"heights: 0 no ground, 1 low, 2 base, 3 high, 4 very high{Environment.NewLine}";
    }
}

/// <summary>ANSI colour codes for terminals. Turned off when output isn't a terminal.</summary>
public static class Ansi
{
    public static bool Enabled { get; set; } = !Console.IsOutputRedirected;

    public const string Reset = "\u001b[0m";
    public const string Party = "\u001b[1;38;5;45m";
    public const string Enemy = "\u001b[1;38;5;203m";
    public const string Dim = "\u001b[38;5;244m";
    public const string Bold = "\u001b[1m";
    public const string Feature = "\u001b[38;5;107m";
    public const string Warn = "\u001b[38;5;214m";

    // Background shades from no ground (black) to very high ground (light grey).
    private static readonly int[] HeightShades = [16, 238, 241, 245, 250];

    public static string Background(int height, bool marked)
    {
        int shade = HeightShades[Math.Clamp(height, 0, 4)];
        string fg = height >= 3 ? "\u001b[38;5;16m" : "\u001b[38;5;255m";
        return marked ? $"\u001b[48;5;58m{fg}" : $"\u001b[48;5;{shade}m{fg}";
    }
}

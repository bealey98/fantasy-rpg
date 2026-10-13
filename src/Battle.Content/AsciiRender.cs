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
    /// The board itself, with column and row numbers. Without colour, heights are printed as a
    /// second grid to the right; with colour, each tile's background is shaded by height.
    /// </summary>
    public static string Grid(int width, int height, Cell[] cells, bool colour, string[]? sidePanel = null)
    {
        // Every board line has the same visible width, so the side panel lines up.
        int boardWidth = colour ? 4 + 2 * width : 4 + (2 * width - 1) + 6 + (2 * width - 1);
        var lines = new List<(string Text, int Visible)>();

        string columns = string.Join(" ", Enumerable.Range(0, width).Select(x => (x % 10).ToString()));
        string head = "    " + columns + (colour ? "" : "      " + string.Join(" ", Enumerable.Range(0, width).Select(x => (x % 10).ToString())));
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
            if (!colour)
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

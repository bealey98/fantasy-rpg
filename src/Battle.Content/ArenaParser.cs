using Battle.Core;

namespace Battle.Content;

/// <summary>
/// Reads an arena text file: a few "key: value" header lines, then layers.
///
///   arena: river-crossing
///   size: 10 x 6
///   time: day
///
///   [height]    legend text after the section name is ignored
///   2222233344
///   ...
///
/// Layers: [height] and [terrain] are required; [features] and [units] are optional.
/// Each layer has one row per line and one character per tile; '.' means "nothing" in
/// the features and units layers. Every mistake found is reported at once, with line numbers.
/// </summary>
public static class ArenaParser
{
    private static readonly string[] KnownLayers = ["height", "terrain", "features", "units"];

    public static ArenaDef Parse(string text, string fileName, GameData data)
    {
        var errors = new List<string>();
        var header = new Dictionary<string, (string Value, int Line)>(StringComparer.Ordinal);
        var layers = new Dictionary<string, (List<string> Rows, int FirstLine)>(StringComparer.Ordinal);

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        string? currentLayer = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            int lineNo = i + 1;
            if (line.Length == 0) { currentLayer = null; continue; }

            if (line.StartsWith('['))
            {
                int close = line.IndexOf(']');
                string layerName = close > 0 ? line[1..close].Trim().ToLowerInvariant() : "";
                if (!KnownLayers.Contains(layerName))
                {
                    errors.Add($"{fileName}:{lineNo}: unknown layer '[{layerName}]' (expected one of {string.Join(", ", KnownLayers)})");
                    currentLayer = null;
                    continue;
                }
                if (layers.ContainsKey(layerName))
                {
                    errors.Add($"{fileName}:{lineNo}: layer '[{layerName}]' appears twice");
                    currentLayer = null;
                    continue;
                }
                layers[layerName] = (new List<string>(), lineNo + 1);
                currentLayer = layerName;
                continue;
            }

            if (currentLayer != null)
            {
                layers[currentLayer].Rows.Add(line);
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0 || layers.Count > 0)
            {
                errors.Add($"{fileName}:{lineNo}: expected 'key: value' or a [layer] heading, got '{line}'");
                continue;
            }
            header[line[..colon].Trim().ToLowerInvariant()] = (line[(colon + 1)..].Trim(), lineNo);
        }

        string name = header.TryGetValue("arena", out var n) ? n.Value : Path.GetFileNameWithoutExtension(fileName);
        string time = header.TryGetValue("time", out var t) ? t.Value.ToLowerInvariant() : "day";
        if (time != "day" && time != "night")
            errors.Add($"{fileName}:{header["time"].Line}: time should be day or night, got '{time}'");

        if (!header.TryGetValue("size", out var sizeEntry) || !TryParseSize(sizeEntry.Value, out int width, out int height))
        {
            errors.Add($"{fileName}: missing or unreadable 'size: W x H' line");
            throw new ContentException(string.Join("\n", errors));
        }

        foreach (string required in new[] { "height", "terrain" })
            if (!layers.ContainsKey(required))
                errors.Add($"{fileName}: missing the [{required}] layer");

        // Check every layer's shape before reading characters.
        foreach (var (layerName, (rows, firstLine)) in layers)
        {
            if (rows.Count != height)
                errors.Add($"{fileName}:{firstLine}: [{layerName}] has {rows.Count} rows, but the size says {height}");
            for (int r = 0; r < rows.Count; r++)
                if (rows[r].Length != width)
                    errors.Add($"{fileName}:{firstLine + r}: [{layerName}] row has {rows[r].Length} characters, but the size says {width}");
        }
        if (errors.Count > 0) throw new ContentException(string.Join("\n", errors));

        int count = width * height;
        var heights = new byte[count];
        var terrain = new byte[count];
        var features = new short[count];
        Array.Fill(features, (short)-1);
        var units = new List<UnitPlacement>();

        ReadLayer(layers["height"], (c, x, y, where) =>
        {
            if (c < '0' || c > '4') { errors.Add($"{where}: height '{c}' should be a digit from 0 to 4"); return; }
            heights[y * width + x] = (byte)(c - '0');
        });

        ReadLayer(layers["terrain"], (c, x, y, where) =>
        {
            var def = data.TerrainByGlyph(c);
            if (def == null) { errors.Add($"{where}: unknown terrain '{c}'"); return; }
            terrain[y * width + x] = (byte)def.Index;
        });

        if (layers.TryGetValue("features", out var featureLayer))
            ReadLayer(featureLayer, (c, x, y, where) =>
            {
                if (c == '.') return;
                var def = data.FeatureByGlyph(c);
                if (def == null) { errors.Add($"{where}: unknown feature '{c}'"); return; }
                features[y * width + x] = (short)def.Index;
            });

        if (layers.TryGetValue("units", out var unitLayer))
            ReadLayer(unitLayer, (c, x, y, where) =>
            {
                if (c == '.') return;
                if (c == 'H' || c == 'L' || (c >= 'a' && c <= 'z')) units.Add(new UnitPlacement(c, new Pos(x, y)));
                else errors.Add($"{where}: unit '{c}' should be H (hero), L (Loper) or a lowercase letter (enemy)");
            });

        if (errors.Count > 0) throw new ContentException(string.Join("\n", errors));

        var arena = new ArenaDef
        {
            Name = name,
            Width = width,
            Height = height,
            Time = time,
            Heights = heights,
            Terrain = terrain,
            Features = features,
            Units = units,
        };
        Validate(arena, data, fileName, layers, errors);
        if (errors.Count > 0) throw new ContentException(string.Join("\n", errors));
        return arena;

        void ReadLayer((List<string> Rows, int FirstLine) layer, Action<char, int, int, string> read)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    read(layer.Rows[y][x], x, y, $"{fileName}:{layer.FirstLine + y}:{x + 1}");
        }
    }

    /// <summary>Checks that the layers agree with each other, once each layer reads cleanly on its own.</summary>
    private static void Validate(ArenaDef arena, GameData data, string fileName,
        Dictionary<string, (List<string> Rows, int FirstLine)> layers, List<string> errors)
    {
        int terrainLine = layers["terrain"].FirstLine;
        string At(Pos p, int firstLine) => $"{fileName}:{firstLine + p.Y}:{p.X + 1}";

        for (int y = 0; y < arena.Height; y++)
            for (int x = 0; x < arena.Width; x++)
            {
                var p = new Pos(x, y);
                int i = arena.Index(p);
                var terrain = data.Terrain[arena.Terrain[i]];
                bool zeroHeight = arena.Heights[i] == 0;
                if (zeroHeight != terrain.NoGround)
                    errors.Add($"{At(p, terrainLine)}: height 0 and no-ground terrain must go together (height {arena.Heights[i]}, terrain '{terrain.Glyph}')");

                if (arena.Features[i] >= 0)
                {
                    var feature = data.Features[arena.Features[i]];
                    int featureLine = layers["features"].FirstLine;
                    if (terrain.NoGround)
                        errors.Add($"{At(p, featureLine)}: '{feature.Glyph}' ({feature.Name}) can't sit on no ground");
                    else if (feature.Blocks && (!terrain.Walkable || terrain.Water))
                        errors.Add($"{At(p, featureLine)}: '{feature.Glyph}' ({feature.Name}) needs dry, open ground");
                }
            }

        var seen = new HashSet<char>();
        foreach (var unit in arena.Units)
        {
            int unitLine = layers["units"].FirstLine;
            int i = arena.Index(unit.Pos);
            var terrain = data.Terrain[arena.Terrain[i]];
            if (!seen.Add(unit.Glyph))
                errors.Add($"{At(unit.Pos, unitLine)}: unit '{unit.Glyph}' appears more than once");
            if (!terrain.Walkable)
                errors.Add($"{At(unit.Pos, unitLine)}: unit '{unit.Glyph}' starts on {terrain.Name.ToLowerInvariant()}, which can't be stood on");
            if (arena.Features[i] >= 0 && data.Features[arena.Features[i]].Blocks)
                errors.Add($"{At(unit.Pos, unitLine)}: unit '{unit.Glyph}' starts inside {data.Features[arena.Features[i]].Name.ToLowerInvariant()}");
        }

        // A ladder only makes sense next to a drop of two or more levels.
        for (int i = 0; i < arena.Terrain.Length; i++)
        {
            if (!data.Terrain[arena.Terrain[i]].Ladder) continue;
            var p = new Pos(i % arena.Width, i / arena.Width);
            bool useful = Dirs.All.Select(d => p.Step(d)).Where(arena.InBounds)
                .Any(q => Math.Abs(arena.Heights[arena.Index(q)] - arena.Heights[i]) >= 2);
            if (!useful)
                errors.Add($"{At(p, terrainLine)}: this ladder has no drop of two or more levels next to it");
        }
    }

    private static bool TryParseSize(string value, out int width, out int height)
    {
        width = height = 0;
        string[] parts = value.ToLowerInvariant().Split('x', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height)
            && width is >= 2 and <= 32 && height is >= 2 and <= 32;
    }
}

namespace Battle.Core;

/// <summary>
/// All the content a battle needs: terrain, features, units, moves and rule numbers.
/// Battle.Content loads it from the content folder; the core only reads it.
/// </summary>
public sealed class GameData
{
    public TerrainDef[] Terrain { get; }
    public FeatureDef[] Features { get; }

    /// <summary>A hash of every content file, written into input logs so replays can spot content changes.</summary>
    public string ContentHash { get; }

    private readonly Dictionary<char, TerrainDef> _terrainByGlyph;
    private readonly Dictionary<char, FeatureDef> _featureByGlyph;

    public GameData(IEnumerable<TerrainDef> terrain, IEnumerable<FeatureDef> features, string contentHash)
    {
        Terrain = terrain.OrderBy(t => t.Index).ToArray();
        Features = features.OrderBy(f => f.Index).ToArray();
        ContentHash = contentHash;
        _terrainByGlyph = Terrain.ToDictionary(t => t.Glyph);
        _featureByGlyph = Features.ToDictionary(f => f.Glyph);
    }

    public TerrainDef? TerrainByGlyph(char glyph) => _terrainByGlyph.GetValueOrDefault(glyph);

    public FeatureDef? FeatureByGlyph(char glyph) => _featureByGlyph.GetValueOrDefault(glyph);
}

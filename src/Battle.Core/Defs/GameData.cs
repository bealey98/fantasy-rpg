namespace Battle.Core;

/// <summary>
/// All the content a battle needs: terrain, features, units, moves and rule numbers.
/// Battle.Content loads it from the content folder; the core only reads it.
/// </summary>
public sealed class GameData
{
    public TerrainDef[] Terrain { get; }
    public FeatureDef[] Features { get; }
    public UnitDef[] Units { get; }
    public MoveDef[] Moves { get; }
    public RulesConfig Rules { get; }

    /// <summary>A hash of every content file, written into input logs so replays can spot content changes.</summary>
    public string ContentHash { get; }

    private readonly Dictionary<char, TerrainDef> _terrainByGlyph;
    private readonly Dictionary<char, FeatureDef> _featureByGlyph;
    private readonly Dictionary<string, UnitDef> _unitById;
    private readonly Dictionary<string, MoveDef> _moveById;

    public GameData(
        IEnumerable<TerrainDef> terrain,
        IEnumerable<FeatureDef> features,
        IEnumerable<UnitDef> units,
        IEnumerable<MoveDef> moves,
        RulesConfig rules,
        string contentHash)
    {
        Terrain = terrain.OrderBy(t => t.Index).ToArray();
        Features = features.OrderBy(f => f.Index).ToArray();
        Units = units.OrderBy(u => u.Index).ToArray();
        Moves = moves.OrderBy(m => m.Index).ToArray();
        Rules = rules;
        ContentHash = contentHash;
        _terrainByGlyph = Terrain.ToDictionary(t => t.Glyph);
        _featureByGlyph = Features.ToDictionary(f => f.Glyph);
        _unitById = Units.ToDictionary(u => u.Id, StringComparer.Ordinal);
        _moveById = Moves.ToDictionary(m => m.Id, StringComparer.Ordinal);
    }

    public MoveDef? MoveById(string id) => _moveById.GetValueOrDefault(id);

    public MoveDef Move(string id) =>
        _moveById.TryGetValue(id, out var def) ? def : throw new ArgumentException($"no move called '{id}' in moves.json");

    public TerrainDef? TerrainByGlyph(char glyph) => _terrainByGlyph.GetValueOrDefault(glyph);

    public FeatureDef? FeatureByGlyph(char glyph) => _featureByGlyph.GetValueOrDefault(glyph);

    public UnitDef? UnitById(string id) => _unitById.GetValueOrDefault(id);

    public UnitDef Unit(string id) =>
        _unitById.TryGetValue(id, out var def) ? def : throw new ArgumentException($"no unit called '{id}' in units.csv");
}

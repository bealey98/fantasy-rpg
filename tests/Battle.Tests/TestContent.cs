using Battle.Content;
using Battle.Core;

namespace Battle.Tests;

/// <summary>The real content folder, loaded once and shared by every test.</summary>
public static class TestContent
{
    private static readonly Lazy<(string Dir, GameData Data)> Loaded = new(() =>
    {
        string dir = ContentLoader.FindContentDir();
        return (dir, ContentLoader.Load(dir));
    });

    /// <summary>
    /// The same content with goblin morale switched off, so a lone goblin in a small test arena stays
    /// and fights instead of fleeing. Tests about fleeing ask for the real rules.
    /// </summary>
    private static readonly Lazy<GameData> Calm = new(() =>
    {
        var data = ContentLoader.Load(Loaded.Value.Dir);
        data.Rules.FleeEnabled = false;
        return data;
    });

    public static string Dir => Loaded.Value.Dir;

    /// <summary>The repository root: the folder holding Battle.sln.</summary>
    public static string RepoRoot => Path.GetDirectoryName(Dir)!;
    public static GameData Data => Loaded.Value.Data;
    public static GameData CalmData => Calm.Value;

    public static ArenaDef Arena(string name) => ContentLoader.LoadArena(Dir, name, Data);

    /// <summary>Parses an arena written inline in a test.</summary>
    public static ArenaDef Parse(string text) => ArenaParser.Parse(text, "test.txt", Data);
}

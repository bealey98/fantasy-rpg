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

    public static string Dir => Loaded.Value.Dir;

    /// <summary>The repository root: the folder holding Battle.sln.</summary>
    public static string RepoRoot => Path.GetDirectoryName(Dir)!;
    public static GameData Data => Loaded.Value.Data;

    public static ArenaDef Arena(string name) => ContentLoader.LoadArena(Dir, name, Data);

    /// <summary>Parses an arena written inline in a test.</summary>
    public static ArenaDef Parse(string text) => ArenaParser.Parse(text, "test.txt", Data);
}

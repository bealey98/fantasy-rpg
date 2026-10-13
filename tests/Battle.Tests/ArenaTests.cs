using Battle.Content;
using Battle.Core;

namespace Battle.Tests;

public class ArenaTests
{
    private const string Small = """
        arena: small
        size: 4 x 3
        time: night

        [height]
        2223
        1220
        2222

        [terrain]
        ...#
        ~..x
        ....

        [features]
        T...
        ....
        .%..

        [units]
        .H..
        ....
        a..b
        """;

    [Test]
    public void ParsesEveryLayer()
    {
        var arena = TestContent.Parse(Small);
        Assert.Equal("small", arena.Name);
        Assert.Equal(4, arena.Width);
        Assert.Equal(3, arena.Height);
        Assert.Equal("night", arena.Time);
        Assert.Equal((byte)3, arena.Heights[arena.Index(new Pos(3, 0))], "height at (3,0)");
        Assert.Equal('#', TestContent.Data.Terrain[arena.Terrain[arena.Index(new Pos(3, 0))]].Glyph, "rock at (3,0)");
        Assert.Equal('T', TestContent.Data.Features[arena.Features[0]].Glyph, "tree at (0,0)");
        Assert.Equal((short)-1, arena.Features[1], "no feature at (1,0)");
        Assert.Equal(3, arena.Units.Count, "unit count");
        Assert.Equal(new Pos(1, 0), arena.Units.Single(u => u.Glyph == 'H').Pos, "hero position");
    }

    [Test]
    public void RiverCrossingLoads()
    {
        var arena = TestContent.Arena("river-crossing");
        Assert.Equal(10, arena.Width);
        Assert.Equal(6, arena.Height);
        Assert.Equal(4, arena.Units.Count);
    }

    [Test]
    public void EveryArenaInContentLoads()
    {
        foreach (string path in Directory.GetFiles(Path.Combine(TestContent.Dir, "arenas"), "*.txt"))
            ContentLoader.LoadArena(TestContent.Dir, path, TestContent.Data);
    }

    [Test]
    public void ReportsWrongRowLength()
    {
        var e = Assert.Throws<ContentException>(() => TestContent.Parse(Small.Replace("1220", "122")));
        Assert.True(e.Message.Contains("row has 3 characters"), e.Message);
    }

    [Test]
    public void ReportsUnknownCharacters()
    {
        var e = Assert.Throws<ContentException>(() => TestContent.Parse(Small.Replace("~..x", "~..?")));
        Assert.True(e.Message.Contains("unknown terrain '?'"), e.Message);
    }

    [Test]
    public void HeightZeroMustBeNoGround()
    {
        var e = Assert.Throws<ContentException>(() => TestContent.Parse(Small.Replace("~..x", "~...")));
        Assert.True(e.Message.Contains("height 0 and no-ground terrain must go together"), e.Message);
    }

    [Test]
    public void UnitsCannotStartOnRock()
    {
        var e = Assert.Throws<ContentException>(() => TestContent.Parse(Small.Replace(".H..", "...H")));
        Assert.True(e.Message.Contains("can't be stood on"), e.Message);
    }

    [Test]
    public void UnitsCannotStartInsideATree()
    {
        var e = Assert.Throws<ContentException>(() => TestContent.Parse(Small.Replace(".H..", "H...")));
        Assert.True(e.Message.Contains("starts inside"), e.Message);
    }

    [Test]
    public void ReportsSeveralMistakesAtOnce()
    {
        string broken = Small.Replace("~..x", "~..?").Replace("a..b", "a..Q");
        var e = Assert.Throws<ContentException>(() => TestContent.Parse(broken));
        Assert.True(e.Message.Contains("unknown terrain '?'") && e.Message.Contains("unit 'Q'"), e.Message);
    }

    [Test]
    public void LadderNeedsADrop()
    {
        var e = Assert.Throws<ContentException>(() => TestContent.Parse(Small.Replace("....\n\n[features]", "..|.\n\n[features]")));
        Assert.True(e.Message.Contains("ladder has no drop"), e.Message);
    }

    [Test]
    public void RenderShowsUnitsOverFeatures()
    {
        string text = AsciiRender.Arena(TestContent.Arena("river-crossing"), TestContent.Data);
        Assert.True(text.Contains(" 1  . . . . . . # . a ."), text);
        Assert.True(text.Contains("T ancient tree"), text);
    }

    [Test]
    public void ContentHashIsStable()
    {
        Assert.Equal(ContentLoader.HashContent(TestContent.Dir), ContentLoader.HashContent(TestContent.Dir));
        Assert.True(TestContent.Data.ContentHash.StartsWith("sha256:"), TestContent.Data.ContentHash);
    }
}

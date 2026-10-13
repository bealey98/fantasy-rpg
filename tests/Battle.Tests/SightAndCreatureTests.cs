using Battle.Core;

namespace Battle.Tests;

public class SightAndCreatureTests
{
    private const string Lines = """
        arena: lines
        size: 7 x 3

        [height]
        2222222
        3222222
        2222242

        [terrain]
        ...#...
        .......
        .......

        [features]
        .......
        ...%...
        .......

        [units]
        H......
        .......
        .......
        """;

    [Test]
    public void RockBlocksSightAndOpenGroundDoesNot()
    {
        var s = TestBattles.Create(Lines);
        Assert.False(Rules.HasSight(s, new Pos(0, 0), new Pos(6, 0)), "rock in the way");
        Assert.True(Rules.HasSight(s, new Pos(0, 0), new Pos(2, 0)), "clear");
    }

    [Test]
    public void HigherGroundSeesOverLowCover()
    {
        var s = TestBattles.Create(Lines);
        Assert.False(Rules.HasSight(s, new Pos(1, 1), new Pos(5, 1)), "a thicket blocks at the same height");
        Assert.True(Rules.HasSight(s, new Pos(0, 1), new Pos(5, 1)), "from high ground the thicket doesn't block");
    }

    [Test]
    public void ARidgeBlocksSight()
    {
        var s = TestBattles.Create(Lines);
        Assert.False(Rules.HasSight(s, new Pos(3, 2), new Pos(6, 2)), "the height-4 tile between blocks");
    }

    [Test]
    public void SightIsSymmetric()
    {
        var arena = TestContent.Arena("river-crossing");
        var encounter = new EncounterDef { Id = "t", Arena = arena.Name, Enemies = new Dictionary<char, string> { ['a'] = "cutter", ['b'] = "cutter" } };
        var s = BattleFactory.Create(TestContent.Data, arena, encounter, 1);
        for (int a = 0; a < s.TileCount; a++)
            for (int b = 0; b < s.TileCount; b++)
            {
                var pa = s.PosOf(a);
                var pb = s.PosOf(b);
                if (s.Heights[a] != s.Heights[b]) continue; // low cover is height-dependent by design
                Assert.Equal(Rules.HasSight(s, pa, pb), Rules.HasSight(s, pb, pa), $"sight {pa} ↔ {pb}");
            }
    }

    [Test]
    public void HeightAddsRange()
    {
        var s = TestBattles.Create(Lines);
        var bind = s.Data.Move("bind");
        Assert.Equal(5, Rules.RangeFrom(s, bind, new Pos(0, 1), new Pos(1, 0)), "one level up: +1");
        Assert.Equal(4, Rules.RangeFrom(s, bind, new Pos(1, 0), new Pos(0, 1)), "no bonus looking up");
    }

    private const string Wildlife = """
        arena: wildlife
        size: 8 x 5

        [height]
        22222222
        22222222
        22222222
        22222222
        22222222

        [terrain]
        ........
        ........
        ........
        ........
        ........

        [features]
        ....v...
        ........
        ........
        ...Y....
        f.......

        [units]
        H.......
        ...a....
        ........
        ........
        ........
        """;

    [Test]
    public void BirdsTakeFrightWhenAnAttackLandsNearby()
    {
        var s = TestBattles.Create(Wildlife);
        Rules.Start(s);
        int a = s.Info.UnitIndex("a");
        Rules.MoveTo(s, 0, new Pos(2, 1));
        Rules.Apply(s, BattleAction.Use(0, s.Data.Move("strike"), Target.OfUnit(a)).WithTiming(Timing.Good));
        Rules.Apply(s, BattleAction.End(0));
        Assert.Equal(-1, s.Feature[s.Idx(new Pos(4, 0))], "the birds have gone from (4,0)");
        Assert.True(TestBattles.Events(s).Any(e => e.Type == EventType.CreatureMove && e.Detail == "flock of birds"), "logged");
    }

    [Test]
    public void DeerRunWhenAnyoneComesClose()
    {
        var s = TestBattles.Create(Wildlife);
        Rules.Start(s);
        Assert.True(Rules.MoveTo(s, 0, new Pos(2, 2)), "the hero walks up");
        Rules.Apply(s, BattleAction.End(0));
        Assert.Equal(-1, s.Feature[s.Idx(new Pos(3, 3))], "the deer ran");
    }

    [Test]
    public void CreaturesMoveTheSameWayForTheSameSeed()
    {
        string Positions(ulong seed)
        {
            var s = TestBattles.Create(Wildlife, seed: seed);
            Rules.Start(s);
            for (int i = 0; i < 12 && s.Outcome == Outcome.Ongoing; i++) Rules.Apply(s, BattleAction.End(Rules.CurrentActor(s)));
            return string.Join(",", Enumerable.Range(0, s.TileCount).Where(t => s.Feature[t] >= 0));
        }
        Assert.Equal(Positions(5), Positions(5), "same seed, same wandering");
    }
}

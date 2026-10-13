using Battle.Core;

namespace Battle.Tests;

public class MovementTests
{
    private static int CostTo(BattleState s, int unit, Pos p, int budget = 99)
    {
        Rules.ComputeReach(s, unit, budget);
        return Rules.ReachCost(p);
    }

    [Test]
    public void OpenGroundReachesEveryTileWithinFourSteps()
    {
        var s = TestBattles.Create(TestBattles.Field(9, 9,
            ".........", ".........", ".........", ".........", "....H....",
            ".........", ".........", ".........", "........."));
        var tiles = new List<Pos>();
        Rules.ReachableTiles(s, 0, tiles);
        Assert.Equal(40, tiles.Count, "tiles within four orthogonal steps");
        Assert.True(tiles.All(p => p.Manhattan(new Pos(4, 4)) <= 4), "nothing beyond four steps");
    }

    private const string Costs = """
        arena: costs
        size: 6 x 3

        [height]
        222321
        222222
        222222

        [terrain]
        .~....
        ......
        ......

        [features]
        ......
        ..%...
        ......

        [units]
        H.....
        ......
        ......
        """;

    [Test]
    public void WaterCostsOneAndAHalf()
    {
        var s = TestBattles.Create(Costs);
        Assert.Equal(3, CostTo(s, 0, new Pos(1, 0)), "one water tile");
    }

    [Test]
    public void ThicketCostsExtra()
    {
        var s = TestBattles.Create(Costs);
        // (2,1) has a thicket: 1 tile + 1 extra. Cheapest route: (0,1) then (1,1) then (2,1) = 2 + 2 + 4.
        Assert.Equal(8, CostTo(s, 0, new Pos(2, 1)), "thicket tile");
    }

    [Test]
    public void ClimbingCostsExtraAndSteppingDownIsFree()
    {
        var s = TestBattles.Create(Costs);
        // (2,0) costs 5 via the water (3 + 2). Climbing to (3,0) at height 3 adds 2 + 2.
        Assert.Equal(9, CostTo(s, 0, new Pos(3, 0)), "up one level");
        // Down from (3,0) to (4,0) at height 2 is a plain step.
        Assert.Equal(11, CostTo(s, 0, new Pos(4, 0)), "back down");
    }

    private const string Ladder = """
        arena: ladder
        size: 4 x 2

        [height]
        2422
        2222

        [terrain]
        .|..
        ....

        [units]
        H...
        ....
        """;

    [Test]
    public void TwoLevelsNeedALadder()
    {
        var s = TestBattles.Create(Ladder);
        // (1,0) is a ladder two levels up: 1 tile + 1 per level = 3 tiles.
        Assert.Equal(6, CostTo(s, 0, new Pos(1, 0)), "up the ladder");

        var noLadder = TestBattles.Create(Ladder.Replace(".|..", ".#.."));
        Assert.Equal(Rules.Unreachable, CostTo(noLadder, 0, new Pos(1, 0)), "rock can't be climbed");

        var cliff = TestBattles.Create(Ladder.Replace(".|..", "...."));
        Assert.Equal(Rules.Unreachable, CostTo(cliff, 0, new Pos(1, 0)), "a two-level cliff without a ladder");
    }

    private const string Blocked = """
        arena: blocked
        size: 5 x 3

        [height]
        22222
        22022
        22222

        [terrain]
        ..#..
        ..x..
        .....

        [features]
        .....
        .....
        ..T..

        [units]
        H....
        .....
        .....
        """;

    [Test]
    public void RockNoGroundAndTreesBlock()
    {
        var s = TestBattles.Create(Blocked);
        Assert.Equal(Rules.Unreachable, CostTo(s, 0, new Pos(2, 0)), "rock");
        Assert.Equal(Rules.Unreachable, CostTo(s, 0, new Pos(2, 1)), "no ground");
        Assert.Equal(Rules.Unreachable, CostTo(s, 0, new Pos(2, 2)), "tree");
        Assert.Equal(Rules.Unreachable, CostTo(s, 0, new Pos(3, 0)), "the far side is walled off");
    }

    private const string Corridor = """
        arena: corridor
        size: 6 x 3

        [height]
        222222
        222222
        222222

        [terrain]
        ######
        ......
        ######

        [units]
        ......
        HL..a.
        ......
        """;

    [Test]
    public void AlliesCanBePassedButNotStoppedOn()
    {
        var s = TestBattles.Create(Corridor);
        var tiles = new List<Pos>();
        Rules.ReachableTiles(s, 0, tiles);
        Assert.False(tiles.Contains(new Pos(1, 1)), "can't stop on the Loper");
        Assert.True(tiles.Contains(new Pos(2, 1)) && tiles.Contains(new Pos(3, 1)), "can walk through the Loper");
    }

    [Test]
    public void EnemiesBlockTheWay()
    {
        var s = TestBattles.Create(Corridor);
        Assert.Equal(Rules.Unreachable, CostTo(s, 0, new Pos(5, 1)), "behind the goblin");
    }

    [Test]
    public void MoveToSpendsMovementAndLogsThePath()
    {
        var s = TestBattles.Create(Corridor);
        Assert.True(Rules.MoveTo(s, 0, new Pos(3, 1)), "the move should succeed");
        Assert.Equal(new Pos(3, 1), s.Units[0].Pos, "hero position");
        Assert.Equal(2, s.Units[0].MoveLeft, "movement left");
        Assert.Equal(0, s.Occupant[s.Idx(new Pos(3, 1))], "the hero occupies the new tile");
        Assert.Equal(-1, s.Occupant[s.Idx(new Pos(0, 1))], "the old tile is empty");
        var move = TestBattles.Events(s).Single(e => e.Type == EventType.Move);
        Assert.Equal("path:1,1;2,1;3,1", move.Detail, "logged path");
        Assert.False(Rules.MoveTo(s, 0, new Pos(5, 1)), "can't move through the goblin");
    }

    [Test]
    public void PathsAreTheSameEveryTime()
    {
        string field = TestBattles.Field(5, 5, "H....", ".....", ".....", ".....", ".....");
        var a = TestBattles.Create(field);
        var b = TestBattles.Create(field);
        Rules.MoveTo(a, 0, new Pos(2, 2));
        Rules.MoveTo(b, 0, new Pos(2, 2));
        Assert.Equal(TestBattles.Events(a)[0].Detail, TestBattles.Events(b)[0].Detail, "same path");
    }

    [Test]
    public void CopiesAreIndependent()
    {
        var s = TestBattles.Create(Corridor);
        ulong before = s.Checksum();
        var copy = s.Clone();
        Assert.Equal(before, copy.Checksum(), "a copy has the same checksum");
        Rules.MoveTo(copy, 0, new Pos(2, 1));
        Assert.Equal(new Pos(0, 1), s.Units[0].Pos, "the original didn't move");
        Assert.Equal(before, s.Checksum(), "the original's checksum is unchanged");
        Assert.NotEqual(before, copy.Checksum(), "the copy's checksum changed");
    }

    [Test]
    public void UnitsAreNumberedHeroLoperThenEnemiesByLetter()
    {
        var s = TestBattles.Create(TestBattles.Field(5, 2, "b.H.a", "....L"));
        Assert.Equal("hero,loper,a,b", string.Join(",", s.Info.Labels), "labels");
        Assert.Equal(Side.Enemy, s.Units[2].Side, "a is an enemy");
    }

    [Test]
    public void MountedHeroSharesTheLopersTile()
    {
        var s = TestBattles.Create(TestBattles.Field(5, 2, "H....", "....L"),
            party: new PartySetup { Loper = true, Mounted = true });
        Assert.Equal(new Pos(4, 1), s.Units[0].Pos, "the hero sits on the Loper");
        Assert.Equal(1, s.Units[0].MountedOn, "hero rides the Loper");
        Assert.Equal(1, s.Occupant[s.Idx(new Pos(4, 1))], "the tile holds the mount");
        Assert.Equal(-1, s.Occupant[s.Idx(new Pos(0, 0))], "the hero's arena tile stays empty");

        Rules.MoveTo(s, 0, new Pos(2, 1));
        Assert.Equal(new Pos(2, 1), s.Units[1].Pos, "moving the rider moves the mount");
        Assert.Equal(new Pos(2, 1), s.Units[0].Pos, "and the rider");
    }

    [Test]
    public void RngIsDeterministic()
    {
        var a = new Rng(42);
        var b = new Rng(42);
        var c = new Rng(43);
        bool anyDifferent = false;
        for (int i = 0; i < 20; i++)
        {
            int x = a.Next(1000);
            Assert.Equal(x, b.Next(1000), "same seed, same numbers");
            if (x != c.Next(1000)) anyDifferent = true;
        }
        Assert.True(anyDifferent, "different seeds give different numbers");
    }
}

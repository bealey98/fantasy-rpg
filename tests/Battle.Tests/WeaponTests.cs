using Battle.Core;

namespace Battle.Tests;

public class WeaponTests
{
    private static BattleAction Use(BattleState s, int actor, string move, int target, Timing timing = Timing.None) =>
        BattleAction.Use(actor, s.Data.Move(move), Target.OfUnit(target)).WithTiming(timing);

    private static int Hp(BattleState s, int unit) => s.Units[unit].Hp;

    private static BattleState Started(string arena, Dictionary<char, string>? enemies = null, PartySetup? party = null)
    {
        var s = TestBattles.Create(arena, enemies, party);
        Rules.Start(s);
        return s;
    }

    // ---- Timing, guards, blind and defence ----

    [Test]
    public void StrikeGainsFromTiming()
    {
        foreach (var (timing, damage) in new[] { (Timing.Fumble, 2), (Timing.Good, 3), (Timing.Perfect, 4) })
        {
            var s = Started(TestBattles.Field(4, 2, "Ha..", "...."), new() { ['a'] = "snuffler" });
            Rules.Apply(s, Use(s, 0, "strike", 1, timing));
            Assert.Equal(8 - damage, Hp(s, 1), $"Strike with {timing}");
        }
    }

    [Test]
    public void GuardsSoftenEnemyHits()
    {
        foreach (var (timing, damage) in new[] { (Timing.Fumble, 2), (Timing.Good, 1), (Timing.Perfect, 0) })
        {
            var s = Started(TestBattles.Field(4, 2, "Ha..", "...."));
            Rules.Apply(s, BattleAction.End(0));
            Rules.Apply(s, Use(s, 1, "shiv", 0, timing));
            Assert.Equal(10 - damage, Hp(s, 0), $"Shiv against a {timing} guard");
        }
    }

    [Test]
    public void BlindAttacksMissOnAFumbleAndLoseTheirBonus()
    {
        var s = Started(TestBattles.Field(4, 2, "Ha..", "...."), new() { ['a'] = "snuffler" });
        s.Units[0].Blinded = Rules.Rounds(1);
        Rules.Apply(s, Use(s, 0, "strike", 1, Timing.Fumble));
        Assert.Equal(8, Hp(s, 1), "a fumbled blind strike misses");
        Assert.True(TestBattles.Events(s).Any(e => e.Type == EventType.Miss), "a miss is logged");

        var t = Started(TestBattles.Field(4, 2, "Ha..", "...."), new() { ['a'] = "snuffler" });
        t.Units[0].Blinded = Rules.Rounds(1);
        Rules.Apply(t, Use(t, 0, "strike", 1, Timing.Perfect));
        Assert.Equal(6, Hp(t, 1), "a perfect blind strike only lands its base damage");
    }

    [Test]
    public void DefenceTakesOffDamage()
    {
        var s = Started(TestBattles.Field(4, 2, "Ha..", "...."), new() { ['a'] = "snuffler" });
        s.Units[1].Defence = 2;
        Rules.Apply(s, Use(s, 0, "strike", 1, Timing.Good));
        Assert.Equal(7, Hp(s, 1), "3 damage minus 2 defence");
    }

    // ---- Shove and weight ----

    [Test]
    public void ShovePushesLighterTargetsFurther()
    {
        var light = Started(TestBattles.Field(7, 1 + 1, "Ha.....", "......."));
        Rules.Apply(light, Use(light, 0, "shove", 1));
        Assert.Equal(new Pos(3, 0), light.Units[1].Pos, "a light goblin goes 2 tiles");
        Assert.Equal(3, Hp(light, 1), "and takes Shove's 1 damage");

        var medium = Started(TestBattles.Field(7, 2, "Ha.....", "......."), new() { ['a'] = "snuffler" });
        Rules.Apply(medium, Use(medium, 0, "shove", 1));
        Assert.Equal(new Pos(2, 0), medium.Units[1].Pos, "an equal weight goes 1 tile");
    }

    private const string Hazards = """
        arena: hazards
        size: 6 x 4

        [height]
        222222
        222022
        222222
        222200

        [terrain]
        ...#..
        ...x..
        ......
        ....xx

        [features]
        ......
        ......
        ...T..
        ......

        [units]
        .Ha...
        .Ha...
        .Ha...
        ......
        """;

    [Test]
    public void ObjectsHurtWhatIsDrivenIntoThem()
    {
        // Each row is its own test: rock (row 0) and a tree (row 2) stop the goblin and hurt it.
        var s = Started(Hazards.Replace(".Ha...\n.Ha...\n.Ha...", ".Ha...\n......\n......"));
        Rules.Apply(s, Use(s, 0, "shove", 1));
        Assert.Equal(new Pos(2, 0), s.Units[1].Pos, "stopped by the rock");
        Assert.Equal(4 - 1 - 2, Hp(s, 1), "Shove's 1 plus 2 collision damage");

        var t = Started(Hazards.Replace(".Ha...\n.Ha...\n.Ha...", "......\n......\n.Ha..."));
        Rules.Apply(t, Use(t, 0, "shove", 1));
        Assert.Equal(1, Hp(t, 1), "the tree hurts too");
    }

    [Test]
    public void NoGroundKillsAtOnce()
    {
        var s = Started(Hazards.Replace(".Ha...\n.Ha...\n.Ha...", "......\n.Ha...\n......"));
        Rules.Apply(s, Use(s, 0, "shove", 1));
        Assert.False(s.Units[1].Alive, "pushed into no ground");
        Assert.True(TestBattles.Events(s).Any(e => e.Type == EventType.Fall && e.Detail == "no_ground"), "logged as a fall into no ground");
        Invariants.Check(s);
    }

    [Test]
    public void TheArenaEdgeIsAWall()
    {
        var s = Started(TestBattles.Field(3, 2, ".Ha", "..."));
        Rules.Apply(s, Use(s, 0, "shove", 1));
        Assert.Equal(new Pos(2, 0), s.Units[1].Pos, "stopped at the edge");
        Assert.Equal(1, Hp(s, 1), "collision damage at the edge");
    }

    private const string Ledge = """
        arena: ledge
        size: 6 x 2

        [height]
        444431
        444431

        [terrain]
        ......
        ......

        [units]
        ..Ha..
        ......
        """;

    [Test]
    public void FallsHurtBeyondOneLevel()
    {
        var s = Started(Ledge);
        // The goblin goes 2 tiles: to (4,0) one level down (free), then on to (5,0) two levels down.
        Rules.Apply(s, Use(s, 0, "shove", 1));
        Assert.Equal(new Pos(5, 0), s.Units[1].Pos, "over the ledge");
        Assert.Equal(4 - 1 - 2, Hp(s, 1), "Shove's 1 plus 2 for the extra level");
    }

    [Test]
    public void PushingIntoAUnitStopsWithoutDamage()
    {
        var s = Started(TestBattles.Field(6, 2, "Hab...", "......"));
        Rules.Apply(s, Use(s, 0, "shove", 1));
        Assert.Equal(new Pos(1, 0), s.Units[1].Pos, "a can't move into b");
        Assert.Equal(3, Hp(s, 1), "only Shove's damage");
        Assert.Equal(4, Hp(s, 2), "b is unhurt");
    }

    [Test]
    public void RootedUnitsCannotBeMoved()
    {
        var s = Started(TestBattles.Field(6, 2, "Ha....", "......"));
        s.Units[1].Rooted = Rules.Rounds(1);
        Rules.Apply(s, Use(s, 0, "shove", 1));
        Assert.Equal(new Pos(1, 0), s.Units[1].Pos, "held by its roots");
    }

    // ---- Bull Rush ----

    [Test]
    public void BullRushChargesAndKnocksLighterEnemiesBack()
    {
        var s = Started(TestBattles.Field(7, 2, "H..a...", "......."));
        var legal = new List<BattleAction>();
        Rules.LegalActions(s, legal);
        Assert.True(legal.Any(a => a.Move == s.Data.Move("bull_rush").Index), "the goblin is in charge range");
        Rules.Apply(s, Use(s, 0, "bull_rush", 1, Timing.Good));
        Assert.Equal(new Pos(2, 0), s.Units[0].Pos, "the hero charges to the goblin");
        Assert.Equal(new Pos(4, 0), s.Units[1].Pos, "a light goblin is knocked back 1 tile");
        Assert.Equal(4 - 2, Hp(s, 1), "1 damage plus 1 for good timing");
        Assert.Equal(8, s.Units[0].MoveLeft, "charging doesn't spend movement");
    }

    [Test]
    public void BullRushDoesNotMoveEqualWeights()
    {
        var s = Started(TestBattles.Field(7, 2, "H..a...", "......."), new() { ['a'] = "snuffler" });
        Rules.Apply(s, Use(s, 0, "bull_rush", 1, Timing.Good));
        Assert.Equal(new Pos(3, 0), s.Units[1].Pos, "the Snuffler holds its ground");
    }

    [Test]
    public void ChargesNeedAClearStraightLine()
    {
        var s = Started(TestBattles.Field(7, 3, "H.b.a..", ".......", "...c..."));
        var legal = new List<BattleAction>();
        Rules.LegalActions(s, legal);
        var targets = legal.Where(a => a.Move == s.Data.Move("bull_rush").Index).Select(a => s.Label(a.Target.Unit)).ToList();
        Assert.True(targets.Contains("b"), "b is in line");
        Assert.False(targets.Contains("a"), "a is behind b");
        Assert.False(targets.Contains("c"), "c isn't in a straight line");
    }

    [Test]
    public void AMountedChargeHitsWithTheLopersWeight()
    {
        // The hero starts mounted on the Loper at (0,1); his own arena tile (0,0) stays empty.
        var s = Started(TestBattles.Field(7, 2, "H......", "L..a..."), new() { ['a'] = "snuffler" },
            new PartySetup { Loper = true, Mounted = true });
        Rules.Apply(s, Use(s, 0, "bull_rush", 2, Timing.Good));
        Assert.Equal(new Pos(2, 1), s.Units[1].Pos, "the pair charges together");
        Assert.Equal(new Pos(4, 1), s.Units[2].Pos, "heavy beats medium: the Snuffler is knocked back");
    }

    // ---- Parry ----

    [Test]
    public void ParryBlocksAndGoodTimingStrikesBack()
    {
        var s = Started(TestBattles.Field(4, 2, "Ha..", "...."), party: new PartySetup { Loper = false, WeaponBonus = "parry" });
        Rules.Apply(s, BattleAction.Use(0, s.Data.Move("parry"), Target.Self));
        Rules.Apply(s, BattleAction.End(0));
        Rules.Apply(s, Use(s, 1, "shiv", 0, Timing.Good));
        Assert.Equal(10, Hp(s, 0), "the hero takes nothing");
        Assert.Equal(2, Hp(s, 1), "the goblin takes the 2-damage counter");
        Assert.False(s.Units[0].Has(UnitFlags.ParryReady), "the parry is used up");
    }

    [Test]
    public void AFumbledParryStillBlocksButDoesNotCounter()
    {
        var s = Started(TestBattles.Field(4, 2, "Ha..", "...."), party: new PartySetup { Loper = false, WeaponBonus = "parry" });
        Rules.Apply(s, BattleAction.Use(0, s.Data.Move("parry"), Target.Self));
        Rules.Apply(s, BattleAction.End(0));
        Rules.Apply(s, Use(s, 1, "shiv", 0, Timing.Fumble));
        Assert.Equal(10, Hp(s, 0), "blocked");
        Assert.Equal(4, Hp(s, 1), "no counter");
    }
}

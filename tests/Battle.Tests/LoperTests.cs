using Battle.Core;

namespace Battle.Tests;

public class LoperTests
{
    private static BattleState Start(string field, bool mounted = false, Dictionary<char, string>? enemies = null)
    {
        var s = TestBattles.Create(field, enemies, new PartySetup { Loper = true, Mounted = mounted });
        Rules.Start(s);
        return s;
    }

    private static void End(BattleState s) => Rules.Apply(s, BattleAction.End(Rules.CurrentActor(s)));

    private static List<BattleAction> Legal(BattleState s)
    {
        var legal = new List<BattleAction>();
        Rules.LegalActions(s, legal);
        return legal;
    }

    [Test]
    public void GoreChargesAndKnocksTheTargetOver()
    {
        var s = Start(TestBattles.Field(6, 2, "H.....", "L..a.."));
        End(s); // hero; the Loper's turn
        int a = s.Info.UnitIndex("a");
        Rules.Apply(s, BattleAction.Use(1, s.Data.Move("gore"), Target.OfUnit(a)).WithTiming(Timing.Good));
        Assert.Equal(new Pos(2, 1), s.Units[1].Pos, "the Loper charges");
        Assert.Equal(1, s.Units[a].Hp, "2 damage plus 1 for good timing");
        Assert.True(s.Units[a].Has(UnitFlags.KnockedOver), "knocked over");
        Assert.Equal(new Pos(3, 1), s.Units[a].Pos, "gore doesn't push");
    }

    [Test]
    public void TauntForcesAttacksOntoTheLoper()
    {
        var s = Start(TestBattles.Field(5, 3, ".....", ".HaL.", "....."));
        int a = s.Info.UnitIndex("a");
        End(s); // hero
        Rules.Apply(s, BattleAction.Use(1, s.Data.Move("taunt"), Target.OfUnit(a)));
        End(s); // Loper; the goblin's turn
        var shivs = Legal(s).Where(x => x.Move == s.Data.Move("shiv").Index).Select(x => x.Target.Unit).ToList();
        Assert.Equal("1", string.Join(",", shivs), "the goblin can only stab the Loper");
    }

    [Test]
    public void BraceDefendsButAnchors()
    {
        var s = Start(TestBattles.Field(5, 2, "H.a..", "..L.."));
        End(s); // hero
        Rules.Apply(s, BattleAction.Use(1, s.Data.Move("brace"), Target.Self));
        Assert.Equal(1, s.Units[1].BraceDefence, "+1 defence");
        Assert.Equal(0, s.Units[1].MoveLeft, "no more walking this turn");
        Assert.Equal(0, Rules.Push(s, 2, 1, Dir.East, 3), "can't be pushed");
        End(s); // Loper; the goblin attacks it
        int a = s.Info.UnitIndex("a");
        Rules.Apply(s, BattleAction.Use(a, s.Data.Move("shiv"), Target.OfUnit(1)).WithTiming(Timing.Fumble));
        Assert.Equal(8 - 1, s.Units[1].Hp, "Shiv's 2 minus 1 defence");
    }

    [Test]
    public void MountingUsesAllTheHerosMovementAndJoinsThePair()
    {
        var s = Start(TestBattles.Field(6, 2, "HL...a", "......"));
        var mount = Legal(s).Single(x => x.Kind == ActionKind.Mount);
        Rules.Apply(s, mount);
        Assert.Equal(s.Units[1].Pos, s.Units[0].Pos, "the hero sits on the Loper");
        Assert.Equal(0, s.Units[0].MoveLeft, "no movement left");
        Assert.Equal(-1, s.Occupant[s.Idx(new Pos(0, 0))], "his old tile is free");
        Invariants.Check(s);

        End(s); // hero
        Assert.Equal(0, s.Units[1].MoveLeft, "the Loper can't walk while carrying him");
        Assert.False(Legal(s).Any(x => x.Move == s.Data.Move("gore").Index || x.Move == s.Data.Move("brace").Index), "no Gore or Brace while mounted");
        End(s); // Loper
        while (Rules.CurrentActor(s) != 0) End(s);
        Assert.Equal(s.Units[1].MoveMax, s.Units[0].MoveLeft, "next turn the hero rides at the Loper's speed");
        Assert.True(Rules.MoveTo(s, 0, new Pos(4, 1)), "a long ride");
        Assert.Equal(new Pos(4, 1), s.Units[1].Pos, "the Loper comes along");
    }

    [Test]
    public void MountingNeedsUnspentMovement()
    {
        var s = Start(TestBattles.Field(6, 2, "H.L..a", "......"));
        Assert.True(Rules.MoveTo(s, 0, new Pos(1, 0)), "walk up to the Loper");
        Assert.False(Legal(s).Any(x => x.Kind == ActionKind.Mount), "too late to mount this turn");
    }

    [Test]
    public void DismountingPlacesTheHeroBeside()
    {
        var s = Start(TestBattles.Field(6, 2, "H....a", ".L...."), mounted: true);
        var options = Legal(s).Where(x => x.Kind == ActionKind.Dismount).ToList();
        Assert.Equal(3, options.Count, "three free neighbours (the fourth is off the board)");
        Rules.Apply(s, options.First(x => x.Target.Tile == new Pos(2, 1)));
        Assert.Equal(new Pos(2, 1), s.Units[0].Pos, "the hero steps off");
        Assert.Equal(-1, s.Units[0].MountedOn, "no longer riding");
        Assert.Equal(0, s.Units[0].MoveLeft, "dismounting uses his movement");
        Invariants.Check(s);
    }

    [Test]
    public void MountingStopsTheLoperWalkingThisTurn()
    {
        var s = Start(TestBattles.Field(6, 2, "HL...a", "......"));
        Rules.Apply(s, Legal(s).Single(x => x.Kind == ActionKind.SwapPartner));
        Rules.Apply(s, BattleAction.MoveTo(1, new Pos(0, 1)));
        Assert.True(s.Units[1].MoveLeft > 0, "the Loper has movement left");
        Rules.Apply(s, Legal(s).Single(x => x.Kind == ActionKind.SwapPartner));
        Rules.Apply(s, Legal(s).Single(x => x.Kind == ActionKind.Mount));
        Assert.Equal(0, s.Units[1].MoveLeft, "carrying him, it can't walk any further this turn");
        Rules.Apply(s, Legal(s).Single(x => x.Kind == ActionKind.SwapPartner));
        Assert.False(Legal(s).Any(x => x.Kind == ActionKind.Move), "no walking for the Loper");
    }

    [Test]
    public void DismountingGivesTheLoperItsOwnMovement()
    {
        var s = Start(TestBattles.Field(6, 2, "H....a", ".L...."), mounted: true);
        Assert.Equal(0, s.Units[1].MoveLeft, "carrying the hero, the Loper starts its turn with no movement of its own");
        Rules.Apply(s, Legal(s).First(x => x.Kind == ActionKind.Dismount));
        Assert.Equal(s.Units[1].MoveMax, s.Units[1].MoveLeft, "free of him, it can walk this turn after all");
        Rules.Apply(s, Legal(s).Single(x => x.Kind == ActionKind.SwapPartner));
        Assert.True(Legal(s).Any(x => x.Kind == ActionKind.Move), "and does");
    }

    [Test]
    public void KnockingOverARiddenLoperThrowsTheHeroBehindIt()
    {
        var s = Start(TestBattles.Field(5, 3, "H....", "..La.", "....."), mounted: true);
        int a = s.Info.UnitIndex("a");
        Rules.ApplyStatus(s, a, 1, StatusId.KnockedOver, 0);
        Assert.Equal(-1, s.Units[0].MountedOn, "the hero is off");
        Assert.Equal(new Pos(1, 1), s.Units[0].Pos, "behind the Loper, away from the goblin that knocked it over");
        Assert.True(s.Units[0].Has(UnitFlags.KnockedOver), "and knocked over himself");
        Assert.Equal(1, TestBattles.Events(s).Count(e => e.Type == EventType.Thrown), "a thrown event");
        Invariants.Check(s);

        var t = Start(TestBattles.Field(5, 3, "H....", ".xLa.", "....."), mounted: true);
        Rules.ApplyStatus(t, t.Info.UnitIndex("a"), 1, StatusId.KnockedOver, 0);
        Assert.Equal(new Pos(2, 0), t.Units[0].Pos, "with the tile behind taken, to the side");
    }

    [Test]
    public void EitherOfThePairCanBeTargeted()
    {
        var s = Start(TestBattles.Field(4, 2, "H...", "La.."), mounted: true);
        End(s);
        End(s);
        int a = s.Info.UnitIndex("a");
        var targets = Legal(s).Where(x => x.Move == s.Data.Move("shiv").Index).Select(x => x.Target.Unit).OrderBy(x => x).ToList();
        Assert.Equal("0,1", string.Join(",", targets), "the goblin can stab the hero or the Loper");
    }

    [Test]
    public void AKnockedOutLoperLeavesTheRiderStanding()
    {
        var s = Start(TestBattles.Field(4, 2, "H...", "La.."), mounted: true);
        s.Units[1].Hp = 1;
        Rules.DealDamage(s, 2, 1, 5);
        Assert.False(s.Units[1].Alive, "the Loper is out");
        Assert.Equal(-1, s.Units[0].MountedOn, "the hero is on foot");
        Assert.Equal(0, s.Occupant[s.Idx(new Pos(0, 1))], "standing where the Loper was");
        Invariants.Check(s);
    }
}

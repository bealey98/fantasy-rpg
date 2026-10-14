using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Tests;

public class TurnTests
{
    private static void EndTurn(BattleState s) => Rules.Apply(s, BattleAction.End(Rules.CurrentActor(s)));

    [Test]
    public void PartyActsHeroFirstThenLoperThenEnemies()
    {
        var s = TestBattles.Create(TestBattles.Field(6, 3, "H....a", "L.....", "......"));
        Rules.Start(s);
        Assert.Equal(0, Rules.CurrentActor(s), "the hero acts first");
        EndTurn(s);
        Assert.Equal(1, Rules.CurrentActor(s), "then the Loper");
        EndTurn(s);
        Assert.Equal(Phase.Enemy, s.Phase, "then the enemy phase");
        Assert.Equal(2, Rules.CurrentActor(s), "the goblin acts");
        EndTurn(s);
        Assert.Equal(2, s.Round, "a new round");
        Assert.Equal(0, Rules.CurrentActor(s), "the hero again");
    }

    [Test]
    public void EnemiesActStrongestFirst()
    {
        var s = TestBattles.Create(TestBattles.Field(6, 3, "H.a.b.", "....c.", "......"),
            enemies: new() { ['a'] = "plinker", ['b'] = "cutter", ['c'] = "beastmaster" });
        Rules.Start(s);
        EndTurn(s); // the hero; no Loper in this arena
        var order = new List<string>();
        while (s.Phase == Phase.Enemy && s.Round == 1)
        {
            order.Add(s.Label(Rules.CurrentActor(s)));
            EndTurn(s);
        }
        Assert.Equal("c,b,a", string.Join(",", order), "beastmaster (3), cutter (2), plinker (1)");
    }

    [Test]
    public void EnemyFirstStrikeOpensWithTheEnemyPhase()
    {
        var s = TestBattles.Create(TestBattles.Field(6, 3, "H....a", "......", "......"), firstPhase: Side.Enemy);
        Rules.Start(s);
        Assert.Equal(Phase.Enemy, s.Phase, "the enemies go first");
        EndTurn(s);
        Assert.Equal(2, s.Round, "round 2 starts after the opening enemy phase");
        Assert.Equal(Phase.Party, s.Phase, "with the party phase");
    }

    private static BattleAction Swap(BattleState s) =>
        Legal(s).Single(a => a.Kind == ActionKind.SwapPartner);

    private static List<BattleAction> Legal(BattleState s)
    {
        var legal = new List<BattleAction>();
        Rules.LegalActions(s, legal);
        return legal;
    }

    [Test]
    public void BothPartyTurnsStartTogether()
    {
        var s = TestBattles.Create(TestBattles.Field(6, 3, "H....a", "L.....", "......"));
        s.Units[0].Set(UnitFlags.Dazed);
        s.Units[1].Set(UnitFlags.KnockedOver);
        Rules.Start(s);
        Assert.Equal(0, Rules.CurrentActor(s), "the hero has the controls first");
        Assert.True(s.Units[0].Has(UnitFlags.BonusUsed), "his daze has already cost him his bonus action");
        Assert.Equal(s.Units[1].MoveMax - s.Data.Rules.KnockedOverDrain, s.Units[1].MoveLeft, "and the Loper has already got up");
        var starts = TestBattles.Events(s).Where(e => e.Type == EventType.TurnStart).Select(e => e.Actor).ToList();
        Assert.Equal("0,1", string.Join(",", starts), "both turns start with the phase");
    }

    [Test]
    public void ThePartyCanHandOverMidTurnAndBack()
    {
        var s = TestBattles.Create(TestBattles.Field(8, 3, "H......a", "L.......", "........"));
        Rules.Start(s);
        Rules.Apply(s, BattleAction.MoveTo(0, new Pos(1, 0)));
        int heroLeft = s.Units[0].MoveLeft;
        Rules.Apply(s, Swap(s));
        Assert.Equal(1, Rules.CurrentActor(s), "the Loper takes over");
        Assert.Equal(1, TestBattles.Events(s).Count(e => e.Type == EventType.SwapPartner), "a swap event");
        Rules.Apply(s, BattleAction.MoveTo(1, new Pos(2, 1)));
        Rules.Apply(s, Swap(s));
        Assert.Equal(0, Rules.CurrentActor(s), "and hands back to the hero");
        Assert.Equal(heroLeft, s.Units[0].MoveLeft, "who carries on with the movement he had left");
        Assert.Equal(s.Units[1].MoveMax - 4, s.Units[1].MoveLeft, "the Loper keeps what it has left too");
        Rules.Apply(s, Swap(s));
        Rules.Apply(s, Swap(s));
        Assert.Equal(0, Rules.CurrentActor(s), "as often as the player likes");
    }

    [Test]
    public void EndingATurnHandsOverToThePartnerUntilBothAreDone()
    {
        var s = TestBattles.Create(TestBattles.Field(6, 3, "H....a", "L.....", "......"));
        Rules.Start(s);
        Rules.Apply(s, Swap(s));
        EndTurn(s);
        Assert.Equal(0, Rules.CurrentActor(s), "the Loper went first and ended, so the hero has the controls");
        Assert.False(Legal(s).Any(a => a.Kind == ActionKind.SwapPartner), "no handing back to a turn that has ended");
        EndTurn(s);
        Assert.Equal(Phase.Enemy, s.Phase, "both done: the enemy phase");
        Assert.False(Legal(s).Any(a => a.Kind == ActionKind.SwapPartner), "enemies have no partner to swap to");
    }

    [Test]
    public void SlowedHalvesMovement()
    {
        var s = TestBattles.Create(TestBattles.Field(6, 3, "H....a", "L.....", "......"));
        s.Units[0].Slowed = Rules.Rounds(1);
        s.Units[1].Slowed = Rules.Rounds(1);
        Rules.Start(s);
        Assert.Equal(s.Units[0].MoveMax / 2, s.Units[0].MoveLeft, "the hero gets half his 4 tiles");
        Assert.Equal(s.Units[1].MoveMax / 2, s.Units[1].MoveLeft, "the Loper half its 6");
        Assert.Equal(6, s.Units[1].MoveLeft, "3 tiles, in half-points");
    }

    [Test]
    public void ThePartyCanFleeFromTheEdgeWithNoEnemyClose()
    {
        var s = TestBattles.Create(TestBattles.Field(9, 3, ".........", "HL......a", "........."));
        Rules.Start(s);
        Assert.False(Legal(s).Any(a => a.Kind == ActionKind.Flee), "the Loper isn't on the edge");
        Rules.Apply(s, Swap(s));
        Rules.Apply(s, BattleAction.MoveTo(1, new Pos(1, 0)));
        var flee = Legal(s).Single(a => a.Kind == ActionKind.Flee);
        Rules.Apply(s, flee);
        Assert.Equal(Outcome.PartyFled, s.Outcome, "away they go");
        Assert.Equal(-1, Rules.CurrentActor(s), "the battle is over");
        Assert.Equal("party_fled", TestBattles.Events(s).Last().Detail, "the last event says how it ended");
    }

    [Test]
    public void NoFleeingWithAnEnemyWithinThreeTilesOrOffTheEdge()
    {
        bool CanFlee(string row1, string row2, string row3)
        {
            var s = TestBattles.Create(TestBattles.Field(7, 3, row1, row2, row3));
            Rules.Start(s);
            return Legal(s).Any(a => a.Kind == ActionKind.Flee);
        }
        Assert.False(CanFlee("H......", "..a....", "......."), "3 steps away is too close");
        Assert.True(CanFlee("H......", "...a...", "......."), "4 steps away is far enough");
        Assert.False(CanFlee(".......", ".H....a", "......."), "the hero isn't on the edge");

        // A burrowed enemy counts where it will surface.
        var b = TestBattles.Create(TestBattles.Field(7, 3, "H......", "......s", "......."), enemies: new() { ['s'] = "snuffler" });
        Rules.Start(b);
        Assert.True(Legal(b).Any(a => a.Kind == ActionKind.Flee), "the Snuffler is far away");
        int sn = b.Info.UnitIndex("s");
        b.RemoveFromBoard(sn);
        b.Units[sn].Set(UnitFlags.Burrowed);
        b.Units[sn].BurrowTo = new Pos(1, 1);
        b.Rumble[b.Idx(new Pos(1, 1))] = sn;
        Assert.False(Legal(b).Any(a => a.Kind == ActionKind.Flee), "but it's coming up 2 steps from the hero");
    }

    [Test]
    public void ABattleNobodyCanWinIsADrawAtTheRoundCap()
    {
        var s = TestBattles.Create("""
            arena: apart
            size: 5 x 2

            [height]
            22022
            22022

            [terrain]
            ..x..
            ..x..

            [units]
            H...a
            .....
            """);
        Rules.Start(s);
        while (s.Outcome == Outcome.Ongoing) EndTurn(s);
        Assert.Equal(Outcome.Draw, s.Outcome, "a draw");
        Assert.Equal(s.Data.Rules.RoundCap, s.Round, "at the round cap");
    }

    [Test]
    public void DefeatingTheLastEnemyWins()
    {
        var s = TestBattles.Create(TestBattles.Field(4, 2, "Ha..", "...."));
        Rules.Start(s);
        s.Units[1].Hp = 2;
        var strike = s.Data.Move("strike");
        Rules.Apply(s, BattleAction.Use(0, strike, Target.OfUnit(1)));
        Assert.False(s.Units[1].Alive, "the goblin is defeated");
        Assert.Equal(Outcome.PartyWon, s.Outcome, "victory");
        Assert.Equal(-1, Rules.CurrentActor(s), "nobody acts after the battle");
    }

    [Test]
    public void TheHeroFallingLosesTheBattle()
    {
        var s = TestBattles.Create(TestBattles.Field(4, 2, "Ha..", "...."));
        Rules.Start(s);
        s.Units[0].Hp = 1;
        EndTurn(s);
        var shiv = s.Data.Move("shiv");
        Rules.Apply(s, BattleAction.Use(1, shiv, Target.OfUnit(0)));
        Assert.Equal(Outcome.PartyLost, s.Outcome, "defeat");
    }

    [Test]
    public void StatusesLastWholeRounds()
    {
        var s = TestBattles.Create(TestBattles.Field(5, 2, "H...a", "....."));
        Rules.Start(s);
        // Rooted for one round during the party phase: it holds through the enemy phase that follows.
        s.Units[1].Rooted = Rules.Rounds(1);
        EndTurn(s);
        Assert.Equal(Phase.Enemy, s.Phase);
        Assert.Equal(0, s.Units[1].MoveLeft, "the goblin is still rooted on its turn");
        EndTurn(s);
        Assert.Equal(0, s.Units[1].Rooted, "and free once the enemy phase ends");
    }

    [Test]
    public void KnockedOverCostsMovementOnce()
    {
        var s = TestBattles.Create(TestBattles.Field(5, 2, "H...a", "....."));
        s.Units[0].Set(UnitFlags.KnockedOver);
        Rules.Start(s);
        Assert.Equal(s.Units[0].MoveMax - s.Data.Rules.KnockedOverDrain, s.Units[0].MoveLeft, "getting up drains movement");
        Assert.False(s.Units[0].Has(UnitFlags.KnockedOver), "only once");
    }

    [Test]
    public void IllegalActionsAreRejectedAndChangeNothing()
    {
        var s = TestBattles.Create(TestBattles.Field(5, 2, "H...a", "....."));
        Rules.Start(s);
        ulong before = s.Checksum();
        Assert.Throws<IllegalActionException>(() => Rules.Apply(s, BattleAction.End(1)), "acting out of turn");
        Assert.Throws<IllegalActionException>(() => Rules.Apply(s, BattleAction.Use(0, s.Data.Move("strike"), Target.OfUnit(1))), "striking from afar");
        Assert.Throws<IllegalActionException>(() => Rules.Apply(s, BattleAction.MoveTo(0, new Pos(4, 0))), "walking onto the goblin");
        Assert.Equal(before, s.Checksum(), "nothing changed");
    }

    [Test]
    public void EveryLegalActionCanBeApplied()
    {
        // Each legal action, tried on its own copy of the battle, must apply without complaint.
        var s = TestBattles.Create(TestBattles.Field(5, 3, "H....", "L.a..", "....."));
        Rules.Start(s);
        var legal = new List<BattleAction>();
        Rules.LegalActions(s, legal);
        foreach (var action in legal)
        {
            var copy = s.Clone();
            Rules.Apply(copy, action.WithTiming(Timing.Good));
            Invariants.Check(copy);
        }
    }
}

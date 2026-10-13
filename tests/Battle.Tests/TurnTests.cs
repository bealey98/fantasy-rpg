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

    [Test]
    public void SwappingOrderLetsTheLoperGoFirst()
    {
        var s = TestBattles.Create(TestBattles.Field(6, 3, "H....a", "L.....", "......"));
        s.Units[0].Set(UnitFlags.Dazed); // the hero's turn start uses up the daze...
        Rules.Start(s);
        Assert.True(s.Units[0].Has(UnitFlags.BonusUsed), "dazed: no bonus this turn");
        var legal = new List<BattleAction>();
        Rules.LegalActions(s, legal);
        var swap = legal.Single(a => a.Kind == ActionKind.SwapOrder);
        Rules.Apply(s, swap);
        Assert.Equal(1, Rules.CurrentActor(s), "the Loper acts first now");
        Assert.True(s.Units[0].Has(UnitFlags.Dazed), "...but swapping undid the hero's turn start, so he's still dazed");
        Rules.LegalActions(s, legal);
        Assert.False(legal.Any(a => a.Kind == ActionKind.SwapOrder), "only one swap per phase");
        EndTurn(s);
        Assert.Equal(0, Rules.CurrentActor(s), "then the hero");
        Assert.True(s.Units[0].Has(UnitFlags.BonusUsed), "and his daze applies now");
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

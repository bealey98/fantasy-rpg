using Battle.Core;

namespace Battle.Tests;

public class ElderwoodTests
{
    /// <summary>A grove: an ancient tree beside the hero, a goblin four tiles away.</summary>
    private static string Grove(string row2 = "..a.....", string row1 = "........", string row3 = "........", int width = 8) => $"""
        arena: grove
        size: {width} x 5

        [height]
        {new string('2', width)}
        {new string('2', width)}
        {new string('2', width)}
        {new string('2', width)}
        {new string('2', width)}

        [terrain]
        {new string('.', width)}
        {new string('.', width)}
        {new string('.', width)}
        {new string('.', width)}
        {new string('.', width)}

        [features]
        .T{new string('.', width - 2)}
        {new string('.', width)}
        {new string('.', width)}
        {new string('.', width)}
        {new string('.', width)}

        [units]
        H{new string('.', width - 1)}
        {row1}
        {row2}
        {row3}
        {new string('.', width)}
        """;

    private static BattleState Start(string arena, int tier = 1, Dictionary<char, string>? enemies = null)
    {
        var s = TestBattles.Create(arena, enemies, new PartySetup { Loper = arena.Contains('L'), ElderwoodTier = tier });
        Rules.Start(s);
        return s;
    }

    private static BattleAction Cast(BattleState s, string move, Target target, Target? second = null) =>
        BattleAction.Use(Rules.CurrentActor(s), s.Data.Move(move), target, second);

    private static void End(BattleState s) => Rules.Apply(s, BattleAction.End(Rules.CurrentActor(s)));

    private static int Tree(BattleState s) => s.Idx(new Pos(1, 0));

    // ---- Bind ----

    [Test]
    public void BindRootsAnEnemyThroughItsNextTurn()
    {
        var s = Start(Grove());
        int a = s.Info.UnitIndex("a");
        Rules.Apply(s, Cast(s, "bind", Target.OfUnit(a)));
        Assert.True(s.Units[a].Rooted > 0, "rooted");
        Assert.Equal(2, s.FeatureUses[Tree(s)], "the tree spent a use");
        End(s);
        Assert.Equal(a, Rules.CurrentActor(s), "the goblin's turn");
        var legal = new List<BattleAction>();
        Rules.LegalActions(s, legal);
        Assert.False(legal.Any(x => x.Kind == ActionKind.Move), "it can't walk while rooted");
        End(s);
        Assert.Equal(0, s.Units[a].Rooted, "free once the enemy phase ends");
        Assert.Equal(3, s.FeatureUses[Tree(s)], "and the tree's use came back at the end of the round");
    }

    [Test]
    public void BindWithoutASourceOnlySlows()
    {
        var s = Start(Grove().Replace(".T......", "........"));
        int a = s.Info.UnitIndex("a");
        Rules.Apply(s, Cast(s, "bind", Target.OfUnit(a)));
        Assert.Equal(0, s.Units[a].Rooted, "not rooted");
        Assert.True(s.Units[a].Slowed > 0, "slowed instead");
    }

    [Test]
    public void RootTrapsCatchEnemiesButNotFriends()
    {
        var s = Start(Grove());
        int a = s.Info.UnitIndex("a");
        var trap = new Pos(2, 1);
        Rules.Apply(s, Cast(s, "bind", Target.OfTile(trap)));
        Assert.Equal(TrapKind.Bind, s.Trap[s.Idx(trap)], "the trap is set");
        End(s);
        Assert.True(Rules.MoveTo(s, a, trap), "the goblin can step onto the trap");
        Assert.True(s.Units[a].Rooted > 0, "and is caught");
        Assert.Equal(0, s.Units[a].MoveLeft, "its move ends there");
        Assert.Equal(TrapKind.None, s.Trap[s.Idx(trap)], "the trap is used up");

        var t = Start(Grove());
        Rules.Apply(t, Cast(t, "bind", Target.OfTile(new Pos(0, 1))));
        Assert.True(Rules.MoveTo(t, 0, new Pos(0, 1)), "the hero walks onto his own trap");
        Assert.Equal(0, t.Units[0].Rooted, "and isn't caught");
    }

    [Test]
    public void RootTrapsCrumbleAfterARound()
    {
        var s = Start(Grove());
        Rules.Apply(s, Cast(s, "bind", Target.OfTile(new Pos(1, 3))));
        End(s); // hero
        End(s); // goblin
        Assert.Equal(TrapKind.None, s.Trap[s.Idx(new Pos(1, 3))], "gone by the next round");
    }

    [Test]
    public void AWeaponBindRootsWhatTheNextHitStrikes()
    {
        var s = Start(Grove(row2: "........", row1: "a......."));
        int a = s.Info.UnitIndex("a");
        Rules.Apply(s, Cast(s, "bind", Target.Weapon));
        Assert.True(s.Units[0].Has(UnitFlags.WeaponBind), "the weapon is bound");
        Rules.Apply(s, BattleAction.Use(0, s.Data.Move("strike"), Target.OfUnit(a)).WithTiming(Timing.Good));
        Assert.True(s.Units[a].Rooted > 0, "the struck goblin is rooted");
        Assert.False(s.Units[0].Has(UnitFlags.WeaponBind), "the bind is used up");
    }

    [Test]
    public void TierTwoBindHurtsAndSlowsAfterwards()
    {
        var s = Start(Grove(), tier: 2);
        int a = s.Info.UnitIndex("a");
        Rules.Apply(s, Cast(s, "bind_2", Target.OfUnit(a)));
        Assert.Equal(3, s.Units[a].Hp, "1 damage");
        End(s);
        End(s); // the root ends with the enemy phase
        Assert.True(s.Units[a].Slowed > 0, "slowed once the root ends");
    }

    // ---- Tether ----

    [Test]
    public void TetheredToATreeAnEnemyCannotLeave()
    {
        var s = Start(Grove());
        int a = s.Info.UnitIndex("a");
        Rules.Apply(s, Cast(s, "tether", Target.OfUnit(a), Target.OfTile(new Pos(1, 0))));
        Assert.Equal(3, s.Tethers[0].Length, "length is the distance when cast");
        End(s);
        var tiles = new List<Pos>();
        Rules.ReachableTiles(s, a, tiles);
        Assert.True(tiles.Count > 0, "it can still move");
        Assert.True(tiles.All(p => p.Manhattan(new Pos(1, 0)) <= 3), "but only within the tether");
    }

    [Test]
    public void EqualWeightsHoldEachOther()
    {
        var s = Start(Grove(row1: "...b...."));
        int a = s.Info.UnitIndex("a"), b = s.Info.UnitIndex("b");
        Rules.Apply(s, Cast(s, "tether", Target.OfUnit(a), Target.OfUnit(b)));
        End(s);
        var tiles = new List<Pos>();
        int first = Rules.CurrentActor(s);
        Rules.ReachableTiles(s, first, tiles);
        int other = first == a ? b : a;
        Assert.True(tiles.All(p => p.Manhattan(s.Units[other].Pos) <= 2), "neither can drag the other");
    }

    [Test]
    public void TheHeavierEndDragsTheLighter()
    {
        var s = Start(Grove(row2: ".La....."));
        int loper = s.Info.LoperIndex, a = s.Info.UnitIndex("a");
        Rules.Apply(s, Cast(s, "tether", Target.OfUnit(a), Target.OfUnit(loper)));
        End(s); // hero; now the Loper's turn
        Assert.Equal(loper, Rules.CurrentActor(s));
        Assert.True(Rules.MoveTo(s, loper, new Pos(0, 4)), "the Loper walks away");
        Assert.True(s.Units[a].Pos.Manhattan(s.Units[loper].Pos) <= 1, $"the goblin is dragged along (now at {s.Units[a].Pos})");
        Assert.True(TestBattles.Events(s).Any(e => e.Type == EventType.Drag), "logged as a drag");
    }

    [Test]
    public void TheLightTierTwoTetherPullsItsEndsTogether()
    {
        var s = Start(Grove(), tier: 2);
        int a = s.Info.UnitIndex("a");
        Rules.Apply(s, Cast(s, "tether_pull", Target.OfUnit(a), Target.OfTile(new Pos(1, 0))));
        Assert.Equal(1, s.Units[a].Pos.Manhattan(new Pos(1, 0)), $"pulled next to the tree (now at {s.Units[a].Pos})");
        Assert.Equal(1, s.Tethers[0].Length, "the tether shortens");
    }

    [Test]
    public void TheDarkTierTwoTetherDrainsItsSourceAndSaps()
    {
        var s = Start(Grove(), tier: 2);
        int a = s.Info.UnitIndex("a");
        s.Units[0].Hp = 7;
        Rules.Apply(s, Cast(s, "tether_sap", Target.OfUnit(a), Target.OfTile(new Pos(1, 0))));
        Assert.Equal(17, s.FeatureHp[Tree(s)], "the tree loses 3 HP");
        Assert.Equal(1, s.DarkUses, "a nudge to the corruption meter");
        End(s);
        End(s); // the round ends: the tether saps
        Assert.Equal(3, s.Units[a].Hp, "the goblin loses 1");
        Assert.Equal(8, s.Units[0].Hp, "the hero gains it");
    }

    [Test]
    public void DarkMovesKillTheirSourceThenCostTheHero()
    {
        var s = Start(Grove(), tier: 2);
        int a = s.Info.UnitIndex("a");
        s.FeatureHp[Tree(s)] = 3;
        Rules.Apply(s, Cast(s, "tether_sap", Target.OfUnit(a), Target.OfTile(new Pos(1, 0))));
        Assert.True(s.FeatureDead[Tree(s)], "the tree dies");
        Assert.Equal(-1, Rules.StepCost(s, 0, s.Idx(new Pos(0, 0)), Tree(s)), "a dead tree still blocks the way");

        var t = Start(Grove().Replace(".T......", "........"), tier: 2);
        int b = t.Info.UnitIndex("a");
        Rules.Apply(t, Cast(t, "tether_sap", Target.OfUnit(b), Target.OfUnit(0)));
        Assert.Equal(7, t.Units[0].Hp, "with no source, the hero pays 3 HP");
        t.Units[0].Hp = 3;
        Assert.False(Rules.CanPayDark(t, 0, t.Data.Move("tether_sap")), "and can't pay what would leave him at 0");
    }

    [Test]
    public void TethersFadeAfterTwoRoundsAndBreakWhenAnEndFalls()
    {
        var s = Start(Grove());
        int a = s.Info.UnitIndex("a");
        Rules.Apply(s, Cast(s, "tether", Target.OfUnit(a), Target.OfTile(new Pos(1, 0))));
        for (int i = 0; i < 4; i++) End(s); // two rounds of hero and goblin
        Assert.Equal(0, s.TetherCount, "faded");

        var t = Start(Grove());
        int b = t.Info.UnitIndex("a");
        Rules.Apply(t, Cast(t, "tether", Target.OfUnit(b), Target.OfTile(new Pos(1, 0))));
        Rules.Defeat(t, b, 0);
        Assert.Equal(0, t.TetherCount, "broken");
    }
}

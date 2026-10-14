using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Tests;

public class GoblinTests
{
    private static BattleState Start(string arena, Dictionary<char, string>? enemies = null, Dictionary<char, char>? handlers = null,
        bool flee = false, int tier = 1)
    {
        var s = TestBattles.Create(arena, enemies, new PartySetup { Loper = arena.Contains('L'), ElderwoodTier = tier },
            handlers: handlers, flee: flee);
        Rules.Start(s);
        return s;
    }

    private static int U(BattleState s, string label) => s.Info.UnitIndex(label);

    private static BattleAction Use(BattleState s, int actor, string move, Target target) =>
        BattleAction.Use(actor, s.Data.Move(move), target);

    private static void End(BattleState s) => Rules.Apply(s, BattleAction.End(Rules.CurrentActor(s)));

    private static List<BattleAction> Legal(BattleState s)
    {
        var legal = new List<BattleAction>();
        Rules.LegalActions(s, legal);
        return legal;
    }

    /// <summary>A small arena with an ancient tree in the top-left corner (a Root source) and the given unit rows.</summary>
    private static string Grove(params string[] rows)
    {
        int w = rows[0].Length;
        string block(char c) => string.Join("\n", rows.Select(_ => new string(c, w)));
        return $"arena: grove\nsize: {w} x {rows.Length}\n\n[height]\n{block('2')}\n\n[terrain]\n{block('.')}\n\n" +
               $"[features]\nT{new string('.', w - 1)}\n{string.Join("\n", rows.Skip(1).Select(_ => new string('.', w)))}\n\n" +
               $"[units]\n{string.Join("\n", rows)}";
    }

    private static readonly Dictionary<char, string> Camp = new() { ['s'] = "snuffler", ['d'] = "beastmaster" };
    private static readonly Dictionary<char, char> Handled = new() { ['s'] = 'd' };

    // ---- Every goblin ----

    [Test]
    public void ScamperAddsTwoTilesOfMovement()
    {
        var s = Start(TestBattles.Field(8, 2, "H......a", "........"));
        End(s); // the hero
        int a = U(s, "a");
        int before = s.Units[a].MoveLeft;
        Rules.Apply(s, Use(s, a, "scamper", Target.Self));
        Assert.Equal(before + 4, s.Units[a].MoveLeft, "2 more tiles (in half-points)");
        Assert.True(s.Units[a].Has(UnitFlags.BonusUsed), "it takes the bonus action");
    }

    // ---- The Cutter ----

    [Test]
    public void ShivGangsUpWhenAMateIsAlsoNextToTheTarget()
    {
        var s = Start(TestBattles.Field(3, 3, "...", "aHb", "..."));
        End(s);
        Rules.Apply(s, Use(s, U(s, "a"), "shiv", Target.OfUnit(0)).WithTiming(Timing.Fumble));
        Assert.Equal(10 - 3, s.Units[0].Hp, "Shiv's 2, plus 1 for ganging up with b (and a fumbled guard)");
    }

    [Test]
    public void HeadbuttDazesBothSoItCantBeChained()
    {
        var s = Start(TestBattles.Field(3, 2, "aH.", "..."));
        End(s); // the hero
        int a = U(s, "a");
        Rules.Apply(s, Use(s, a, "headbutt", Target.OfUnit(0)).WithTiming(Timing.Fumble));
        Assert.Equal(9, s.Units[0].Hp, "1 damage");
        Assert.True(s.Units[0].Has(UnitFlags.Dazed), "the hero is dazed");
        Assert.True(s.Units[a].Has(UnitFlags.Dazed), "and so is the Cutter");
        Assert.True(s.Units[a].Has(UnitFlags.MainUsed) && s.Units[a].Has(UnitFlags.BonusUsed), "a special takes both actions");

        End(s); // round 2: the hero
        Assert.True(s.Units[0].Has(UnitFlags.BonusUsed), "the hero has lost his bonus action");
        End(s); // the Cutter
        var legal = Legal(s);
        Assert.False(legal.Any(x => x.Move == s.Data.Move("headbutt").Index), "dazed, the Cutter can't headbutt again");
        Assert.True(legal.Any(x => x.Move == s.Data.Move("shiv").Index), "but it can still stab");
    }

    [Test]
    public void AfterYouSwapsWithANeighbouringGoblinOnly()
    {
        var s = Start(TestBattles.Field(5, 2, "Hab..", ".s..."), enemies: new() { ['a'] = "cutter", ['b'] = "cutter", ['s'] = "snuffler" });
        End(s); // the hero; the Snuffler (strongest) goes first
        End(s);
        int a = U(s, "a"), b = U(s, "b");
        Assert.Equal(a, Rules.CurrentActor(s), "the first Cutter's turn");
        var swaps = Legal(s).Where(x => x.Move == s.Data.Move("after_you").Index).Select(x => x.Target.Unit).ToList();
        Assert.Equal(b.ToString(), string.Join(",", swaps), "only the other goblin, not the beast");
        Rules.Apply(s, Use(s, a, "after_you", Target.OfUnit(b)));
        Assert.Equal(new Pos(2, 0), s.Units[a].Pos, "a takes b's place");
        Assert.Equal(new Pos(1, 0), s.Units[b].Pos, "and b is next to the hero now");
        Invariants.Check(s);
    }

    // ---- The Plinker ----

    [Test]
    public void PlinkReachesFurtherFromHighGround()
    {
        var s = Start("""
            arena: rise
            size: 7 x 2

            [height]
            3222222
            2222222

            [terrain]
            .......
            .......

            [units]
            c....H.
            .......
            """, enemies: new() { ['c'] = "plinker" });
        End(s);
        int c = U(s, "c");
        var plinks = Legal(s).Where(x => x.Move == s.Data.Move("plink").Index).ToList();
        Assert.Equal(1, plinks.Count, "5 tiles away: range 4, plus 1 for standing a level higher");
        Rules.Apply(s, plinks[0].WithTiming(Timing.Fumble));
        Assert.Equal(9, s.Units[0].Hp, "1 damage");
    }

    [Test]
    public void PocketDirtSpoilsTheHerosNextStrike()
    {
        foreach (var (grade, hp, what) in new[] { (Timing.Fumble, 4, "a fumbled command misses"), (Timing.Good, 2, "a good one only hits: 2, no bonus") })
        {
            var s = Start(TestBattles.Field(4, 2, "Ha.c", "...."), enemies: new() { ['a'] = "cutter", ['c'] = "plinker" });
            End(s); // the hero
            End(s); // the Cutter
            Rules.Apply(s, Use(s, U(s, "c"), "pocket_dirt", Target.OfUnit(0)));
            Assert.True(s.Units[0].Blinded > 0, "blinded");
            End(s); // round 2
            Assert.True(s.Units[0].Blinded > 0, "still blinded on his turn");
            int a = U(s, "a");
            Rules.Apply(s, Use(s, 0, "strike", Target.OfUnit(a)).WithTiming(grade));
            Assert.Equal(hp, s.Units[a].Hp, what);
        }
    }

    [Test]
    public void ASnapTrapBitesAndSlowsThroughTheVictimsNextTurn()
    {
        var s = Start(TestBattles.Field(6, 2, "H....c", "......"), enemies: new() { ['c'] = "plinker" });
        End(s); // the hero
        int c = U(s, "c");
        Rules.Apply(s, Use(s, c, "snap_trap", Target.OfTile(new Pos(2, 0))));
        Assert.Equal(TrapKind.Snap, s.Trap[s.Idx(new Pos(2, 0))], "the trap is down");
        End(s); // round 2: the hero

        Assert.True(Rules.MoveTo(s, 0, new Pos(2, 0)), "the hero walks onto it");
        Assert.Equal(9, s.Units[0].Hp, "it bites for 1");
        Assert.Equal(0, s.Units[0].MoveLeft, "and stops him there");
        Assert.Equal(TrapKind.None, s.Trap[s.Idx(new Pos(2, 0))], "used up");
        End(s); // the hero
        End(s); // the Plinker; round 3
        Assert.Equal(0, Rules.CurrentActor(s), "the hero's next turn");
        Assert.Equal(8 - 4, s.Units[0].MoveLeft, "still slowed: 2 tiles fewer");
    }

    [Test]
    public void SnapTrapsCatchGoblinsTooAndThereIsOnlyOnePerPlinker()
    {
        var s = Start(TestBattles.Field(7, 2, "H.a...c", "......."), enemies: new() { ['a'] = "cutter", ['c'] = "plinker" });
        End(s); // the hero
        End(s); // the Cutter
        int a = U(s, "a"), c = U(s, "c");
        Rules.Apply(s, Use(s, c, "snap_trap", Target.OfTile(new Pos(4, 0))));
        End(s);
        End(s); // round 2: the hero
        End(s); // the Cutter
        Rules.Apply(s, Use(s, c, "snap_trap", Target.OfTile(new Pos(3, 0))));
        Assert.Equal(TrapKind.None, s.Trap[s.Idx(new Pos(4, 0))], "the old trap is taken up");
        End(s);
        End(s); // round 3: the hero
        End(s); // the Cutter steps on its mate's trap
        Assert.True(Rules.MoveTo(s, a, new Pos(3, 0)));
        Assert.Equal(3, s.Units[a].Hp, "goblins get bitten too");
        Assert.True(s.Units[a].Slowed > 0, "and slowed");
    }

    // ---- The Beastmaster and the Snuffler ----

    [Test]
    public void TheWhipLastsUntilTheBeastsNextTurn()
    {
        var s = Start(TestBattles.Field(6, 2, ".H....", ".sd..."), enemies: Camp, handlers: Handled);
        int sn = U(s, "s"), d = U(s, "d");
        End(s); // the hero
        End(s); // the Snuffler acts before its handler: it's stronger
        Rules.Apply(s, Use(s, d, "whip", Target.OfUnit(sn)));
        Assert.Equal(1, s.Units[sn].BuffAmount, "spurred on");
        End(s);
        End(s); // round 2: the hero
        Assert.Equal(sn, Rules.CurrentActor(s), "the Snuffler's next turn");
        Assert.Equal(1, s.Units[sn].BuffAmount, "still spurred on");
        Rules.Apply(s, Use(s, sn, "gnash", Target.OfUnit(0)).WithTiming(Timing.Fumble));
        Assert.Equal(10 - 4, s.Units[0].Hp, "Gnash's 3 plus 1");
        Assert.Equal(0, s.Units[sn].BuffAmount, "used up");
    }

    [Test]
    public void BurrowGoesUndergroundAndBurstsOutNextTurn()
    {
        var s = Start(TestBattles.Field(8, 3, "H.a.....", "......s.", "........"), enemies: new() { ['a'] = "cutter", ['s'] = "snuffler" });
        End(s); // the hero
        int sn = U(s, "s"), a = U(s, "a");
        var rumble = new Pos(1, 0);
        Rules.Apply(s, Use(s, sn, "burrow", Target.OfTile(rumble)));
        Assert.False(s.Units[sn].OnBoard, "underground");
        Assert.Equal(sn, s.Rumble[s.Idx(rumble)], "the ground rumbles where it's heading");
        var danger = Observation.DangerOf(s, sn);
        Assert.True(danger[s.Idx(new Pos(0, 0))], "its danger zone is the blast around the rumbling tile");
        Assert.False(danger[s.Idx(new Pos(0, 2))], "and only that");
        End(s); // the Snuffler
        End(s); // the Cutter; round 2
        End(s); // the hero stays put

        Assert.Equal(sn, Rules.CurrentActor(s), "the Snuffler's turn");
        var legal = Legal(s);
        Assert.Equal(1, legal.Count, "bursting out is all it can do");
        Assert.Throws<IllegalActionException>(() => Rules.Apply(s, BattleAction.End(sn)), "no ending its turn underground");
        Rules.Apply(s, legal[0].WithTiming(Timing.Fumble));
        Assert.Equal(rumble, s.Units[sn].Pos, "out at the rumbling tile");
        Assert.Equal(10 - 4, s.Units[0].Hp, "the hero takes 4");
        Assert.True(s.Units[0].Has(UnitFlags.KnockedOver), "and is knocked over");
        Assert.Equal(4 - 4, s.Units[a].Hp, "anything in the blast is hit, goblins too");
        Assert.Equal(1, (int)s.Heights[s.Idx(rumble)], "the ground caves into a crater");
        Invariants.Check(s);
    }

    [Test]
    public void ARootOnTheRumblingGroundCatchesTheBeastAsItBurstsOut()
    {
        var s = Start(Grove(".H......", ".......s", "........"), enemies: new() { ['s'] = "snuffler" });
        End(s); // the hero
        int sn = U(s, "s");
        var rumble = new Pos(3, 1);
        Rules.Apply(s, Use(s, sn, "burrow", Target.OfTile(rumble)));
        End(s); // round 2
        Rules.Apply(s, Use(s, 0, "bind", Target.OfTile(rumble)));
        Assert.Equal(TrapKind.Bind, s.Trap[s.Idx(rumble)], "a full-strength root on the rumbling ground");
        End(s);
        Rules.Apply(s, Legal(s)[0]);
        Assert.True(s.Units[sn].Rooted > 0, "caught as it comes up");
        End(s); // round 3
        End(s); // the hero
        Assert.Equal(sn, Rules.CurrentActor(s), "the Snuffler's next turn");
        Assert.Equal(0, s.Units[sn].MoveLeft, "still held: it can't walk");
        Assert.False(Legal(s).Any(x => x.Move == s.Data.Move("burrow").Index), "or dig");
    }

    [Test]
    public void AWildBeastTurnsOnEveryone()
    {
        var s = Start(TestBattles.Field(5, 2, "H....", "..sad"), enemies: new() { ['s'] = "snuffler", ['a'] = "cutter", ['d'] = "beastmaster" }, handlers: Handled);
        int sn = U(s, "s"), a = U(s, "a"), d = U(s, "d");
        Assert.False(Rules.AreEnemies(s, sn, a), "with its handler, the beast leaves goblins alone");
        Rules.DealDamage(s, 0, d, 10);
        Assert.True(Rules.IsWild(s, sn), "its handler is gone");
        End(s); // the hero
        var bites = Legal(s).Where(x => x.Move == s.Data.Move("gnash").Index).Select(x => x.Target.Unit).ToList();
        Assert.Equal(a.ToString(), string.Join(",", bites), "the Cutter next to it is fair game now");
    }

    [Test]
    public void TheSnufflerGoesForWhatItsHandlerPointsAt()
    {
        var s = Start(TestBattles.Field(8, 4, "........", ".H.s.d..", "........", "L......."), enemies: Camp, handlers: Handled);
        int sn = U(s, "s"), d = U(s, "d"), loper = s.Info.LoperIndex;
        End(s); // the hero
        End(s); // the Loper
        End(s); // the Snuffler waits
        Rules.Apply(s, Use(s, d, "point", Target.OfUnit(loper)));
        Assert.Equal(OrderKind.Unit, s.Units[sn].Order, "an order");
        Assert.Equal(loper, s.Units[sn].OrderUnit, "to go for the Loper");
        End(s);
        End(s); // round 2: the hero
        End(s); // the Loper

        var choice = new EnemyScripts(1).Choose(s, sn, Legal(s));
        Assert.Equal(s.Data.Move("burrow").Index, choice.Move, "the Loper is out of reach this turn, so it digs");
        Assert.Equal(1, choice.Target.Tile.Manhattan(s.Units[loper].Pos), "next to the Loper, though the hero is nearer");
    }

    // ---- Nerve ----

    [Test]
    public void TheLastGoblinFleesFromTwoAndGetsAwayAtTheEdge()
    {
        var s = Start(TestBattles.Field(5, 4, "H....", ".....", ".La..", "....."), flee: true);
        int a = U(s, "a");
        End(s); // the hero
        End(s); // the Loper
        Assert.True(s.Units[a].Has(UnitFlags.Fleeing), "alone against two, its nerve breaks");
        var legal = Legal(s);
        Assert.False(legal.Any(x => x.Move == s.Data.Move("shiv").Index), "it won't fight, even with the Loper in reach");
        Assert.True(legal.Any(x => x.Move == s.Data.Move("scamper").Index), "but it can scamper");
        Rules.Apply(s, BattleAction.MoveTo(a, new Pos(2, 3)));
        End(s);
        Assert.True(s.Units[a].Has(UnitFlags.Fled), "it gets away from the edge");
        Assert.Equal(Outcome.PartyWon, s.Outcome, "and the party has won");
    }

    [Test]
    public void NoGoblinFleesAnEvenFight()
    {
        var s = Start(TestBattles.Field(5, 3, "H....", "...a.", "....."), flee: true);
        End(s);
        Assert.False(s.Units[U(s, "a")].Has(UnitFlags.Fleeing), "one against one: it stays");
    }

    [Test]
    public void TheBeastDoesntCountForNerve()
    {
        var s = Start(TestBattles.Field(6, 3, "H.....", "...s.d", "L....."), enemies: Camp, handlers: Handled, flee: true);
        int sn = U(s, "s"), d = U(s, "d");
        End(s); // the hero
        End(s); // the Loper
        Assert.False(s.Units[sn].Has(UnitFlags.Fleeing), "beasts don't run");
        End(s); // the Snuffler
        Assert.True(s.Units[d].Has(UnitFlags.Fleeing), "its handler, the last goblin, does");
    }

    [Test]
    public void AFleeingGoblinsScriptRunsForTheEdge()
    {
        var s = Start(TestBattles.Field(7, 5, "H......", ".......", "...a...", ".......", "L......"), flee: true);
        int a = U(s, "a");
        End(s);
        End(s);
        var scripts = new EnemyScripts(1);
        var legal = new List<BattleAction>();
        while (Rules.CurrentActor(s) == a)
        {
            Rules.LegalActions(s, legal);
            Rules.Apply(s, scripts.Choose(s, a, legal));
        }
        Assert.True(s.Units[a].Has(UnitFlags.Fled), "away it goes");
    }

    // ---- The party's script ----

    [Test]
    public void ThePartyScriptRootsRumblingGround()
    {
        var s = Start(Grove(".H......", ".......s", "........"), enemies: new() { ['s'] = "snuffler" });
        End(s);
        int sn = U(s, "s");
        var rumble = new Pos(3, 1);
        Rules.Apply(s, Use(s, sn, "burrow", Target.OfTile(rumble)));
        End(s); // round 2: the hero
        var choice = new PartyBot(1).Choose(s, 0, Legal(s));
        Assert.Equal(s.Data.Move("bind").Index, choice.Move, "Bind");
        Assert.Equal(rumble, choice.Target.Tile, "on the rumbling ground");
    }
}

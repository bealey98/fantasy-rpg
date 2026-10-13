using Battle.Content;
using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Tests;

public class FuzzTests
{
    /// <summary>
    /// Battles where both sides pick random legal actions, with every invariant checked after every
    /// action. This pokes rules in combinations nobody would think to test. For a longer run:
    /// dotnet run --project src/Battle.Sim -c Release -- fuzz --battles 20000
    /// </summary>
    [Test]
    public void RandomBattlesKeepEveryInvariant()
    {
        bool was = Invariants.Enabled;
        Invariants.Enabled = true;
        try
        {
            var encounters = EncounterLoader.All(TestContent.Dir).ToList();
            for (int i = 0; i < 240; i++)
            {
                ulong seed = 1000 + (ulong)i;
                string encounter = encounters[i % encounters.Count];
                var state = EncounterLoader.CreateBattle(TestContent.Dir, TestContent.Data, encounter, seed);
                var runner = new BattleRunner(state, new RandomController(seed), new RandomController(seed * 31),
                    new TimingModel(TestContent.Data, "average", seed));
                try { runner.Run(); }
                catch (Exception e)
                {
                    throw new AssertionException($"{encounter} seed {seed} round {state.Round}: {e.GetType().Name}: {e.Message}");
                }
                Assert.True(state.Outcome != Outcome.Ongoing, "every battle ends");
            }
        }
        finally { Invariants.Enabled = was; }
    }

    [Test]
    public void ScriptedBattlesKeepEveryInvariant()
    {
        bool was = Invariants.Enabled;
        Invariants.Enabled = true;
        try
        {
            foreach (string encounter in EncounterLoader.All(TestContent.Dir))
                for (ulong seed = 1; seed <= 20; seed++)
                {
                    var state = EncounterLoader.CreateBattle(TestContent.Dir, TestContent.Data, encounter, seed);
                    new BattleRunner(state, Scripts.For(Side.Party), Scripts.For(Side.Enemy),
                        new TimingModel(TestContent.Data, "average", seed)).Run();
                }
        }
        finally { Invariants.Enabled = was; }
    }
}

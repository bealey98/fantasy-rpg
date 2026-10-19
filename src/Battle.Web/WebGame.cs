using Battle.Content;
using Battle.Core;
using Battle.Core.Ai;
using InputLogFile = Battle.Content.Logs.InputLog;

namespace Battle.Web;

/// <summary>
/// One battle being played in the page: the state, every input so far and the goblins' scripts.
/// It plays exactly like the terminal game (the same seed, timing model and scripts), so its input
/// log replays in battle-sim. Inputs arrive one at a time: the page sends the player's choices and
/// asks for the enemies' actions one by one, so it can show each as it happens.
/// </summary>
internal sealed class WebGame
{
    private readonly IController _enemies;
    private readonly ITimingSource? _timing;
    private readonly List<BattleAction> _legal = new();
    private readonly EventList _events = new();

    public BattleState S { get; }
    public ulong Seed { get; }

    /// <summary>"novice", "average" or "expert" for a simulated player's timing, or "ask" to grade each one.</summary>
    public string TimingMode { get; }

    public List<(int Round, BattleAction Action)> Inputs { get; } = new();
    public IReadOnlyList<BattleEvent> Events => _events.Events;

    /// <summary>An action waiting for the player to grade its action command (or their guard against it).</summary>
    public BattleAction? Pending { get; private set; }
    public TimingNeed PendingNeed { get; private set; }

    public WebGame(string contentDir, GameData data, string encounter, ulong seed, string timing)
    {
        Seed = seed;
        TimingMode = timing;
        _timing = timing == "ask" ? null : new TimingModel(data, timing, seed);
        _enemies = Scripts.For(Side.Enemy, seed);
        S = EncounterLoader.CreateBattle(contentDir, data, encounter, seed, battleId: (int)(seed % int.MaxValue));
        S.Sink = _events;
        Rules.Start(S);
    }

    public int Actor => Rules.CurrentActor(S);
    public bool PartyToAct => Pending == null && Actor >= 0 && S.Units[Actor].Side == Side.Party;
    public bool EnemyToAct => Pending == null && Actor >= 0 && S.Units[Actor].Side == Side.Enemy;

    /// <summary>Every legal action for whoever acts now. The list is reused, so read it before the next call.</summary>
    public List<BattleAction> Legal()
    {
        Rules.LegalActions(S, _legal);
        return _legal;
    }

    /// <summary>The player's choice, as its index in <see cref="Legal"/>.</summary>
    public void Act(int index)
    {
        if (!PartyToAct) throw new IllegalActionException("it isn't the party's turn");
        var legal = Legal();
        if (index < 0 || index >= legal.Count) throw new IllegalActionException("that choice isn't on the list any more");
        Submit(legal[index]);
    }

    /// <summary>The enemy whose turn it is picks its next action with its script, and plays it.</summary>
    public void EnemyStep()
    {
        if (!EnemyToAct) throw new IllegalActionException("it isn't an enemy's turn");
        int actor = Actor;
        Submit(_enemies.Choose(S, actor, Legal()));
    }

    /// <summary>The player's grade for the waiting action command or guard.</summary>
    public void Grade(Timing grade)
    {
        if (Pending is not BattleAction action) throw new IllegalActionException("nothing is waiting for a grade");
        if (grade == Timing.None) throw new IllegalActionException("a grade is fumble, good or perfect");
        Pending = null;
        Apply(action.WithTiming(grade));
    }

    private void Submit(BattleAction action)
    {
        var need = Rules.TimingFor(S, action);
        if (need != TimingNeed.None && action.Timing == Timing.None)
        {
            if (_timing == null)
            {
                Pending = action;
                PendingNeed = need;
                return;
            }
            action = action.WithTiming(_timing.Grade(S, action, need));
        }
        Apply(action);
    }

    private void Apply(BattleAction action)
    {
        int round = S.Round;
        Rules.Apply(S, action);
        Inputs.Add((round, action));
        // Playtests are where rule bugs turn up, so the page always checks.
        Invariants.Check(S);
    }

    /// <summary>The input log, in the terminal game's format: battle-sim replays it exactly.</summary>
    public string InputLog()
    {
        var writer = new StringWriter { NewLine = "\n" };
        InputLogFile.Write(writer, S, "human", "scripted", Inputs, TimingMode);
        return writer.ToString();
    }

    /// <summary>
    /// Rebuilds a battle from its input log, as the page does after a reload. The goblins' scripts
    /// choose again at each of their turns (and the log's choice is played), so they carry on just as
    /// they would have without the reload.
    /// </summary>
    public static WebGame Replay(string contentDir, GameData data, string logPath)
    {
        var (header, lines) = InputLogFile.Read(logPath);
        if (header.Rules != RulesVersion.Current || header.Content != data.ContentHash)
            throw new ContentException("this battle was saved by an older version of the game");
        var game = new WebGame(contentDir, data, header.Encounter, header.Seed, header.Timing.Length > 0 ? header.Timing : "average");
        foreach (var action in InputLogFile.ToActions(game.S, lines))
        {
            if (game.EnemyToAct) game._enemies.Choose(game.S, game.Actor, game.Legal());
            game.Apply(action);
        }
        return game;
    }
}

# Fantasy RPG: battle core (M1)

The headless battle core for the game, built step by step from the battle infrastructure plan in the design doc.
M1's question is: **are the fights interesting decisions?**

## What you need

- The .NET 8 SDK (`dotnet --version` should print 8.x or later).
- Python 3.10 or later with `rich`, for the replay viewer (from step 4).
- R, optionally, for the analysis scripts (from step 8).

## Playing M1

```
dotnet run --project src/Battle.Play -- first-goblins                 # two Cutters and a Plinker in a glade
dotnet run --project src/Battle.Play -- goblin-camp                   # adds the Beastmaster and its Snuffler
dotnet run --project src/Battle.Play -- goblin-camp-tier2             # the camp again, with the Elderwood at Tier 2
dotnet run --project src/Battle.Play -- goblin-camp --timing ask      # you grade each action command and guard yourself
```

You play the hero and the Loper; the goblins use their scripts. The hero's and the Loper's turns
run side by side: hand over to the other at any point with the "Swap to" action, as often as you
like, until one of them ends its turn. With the whole party on the arena's edge and no enemy within
3 tiles, "Flee the battle" ends it. Type `?` in a battle for the commands, `i a` to inspect a goblin
(its moves and danger zone), `d` for every enemy's danger zone. `river-crossing` is the small test
fight from the plan.

The rules M1 needed beyond the design doc are listed in the doc's M1 audit tab, with which are
decided. `docs/placeholders.md` lists what's still a stand-in: every number, and the few rules not
yet decided.

## Layout

| Folder | Language | What it holds |
| --- | --- | --- |
| `src/Battle.Core` | C# | The rules: battle state, legal actions, events and seeded randomness. No engine, no files, no clock. |
| `src/Battle.Content` | C# | Content files in (arenas, units, moves, rules) and log files out. |
| `src/Battle.Sim` | C# | The command-line simulator: batches, logs, arena renders and the benchmark. |
| `src/Battle.Play` | C# | Play a battle in the terminal by picking from the legal actions. |
| `tests/Battle.Tests` | C# | Rule tests, golden replays, determinism checks and fuzzing. |
| `viewer/` | Python | The replay viewer. It reads logs only, so change it freely. |
| `content/` | CSV, JSON, text | Everything you can rebalance without touching code. |
| `analysis/` | R | `summary.R`: tables and charts from the simulator's logs. |
| `bench/` | CSV | Benchmark results, one row per run, kept to follow speed between steps and machines. |
| `docs/` | Markdown | `placeholders.md`: every stand-in rule and number, and the open questions. |
| `scripts/` | shell | `check.sh` / `check.ps1`: build, test and benchmark in one go. |

## Everyday commands

```
scripts/check.sh                                          # build, every test, short benchmark (check.ps1 on Windows)
dotnet run --project src/Battle.Play -- first-goblins     # play a battle in the terminal
python viewer/replay.py logs/first-goblins-7.events.csv   # watch a logged battle back
dotnet run --project src/Battle.Sim -c Release -- run goblin-camp --battles 100      # simulate; logs and a summary CSV in logs/
dotnet run --project src/Battle.Sim -c Release -- balance --battles 500              # win rates: every encounter at every timing skill
dotnet run --project src/Battle.Sim -c Release -- balance goblin-camp --party simple # ...against a naive player instead
Rscript analysis/summary.R logs                           # tables and charts (logs/summary.pdf) from whatever is in logs/
dotnet run --project src/Battle.Sim -- replay logs/goblin-camp-1.inputs.jsonl        # check a log replays exactly
dotnet run --project src/Battle.Sim -c Release -- fuzz --battles 5000                # random battles, every invariant checked
dotnet run --project src/Battle.Sim -c Release -- bench                              # inputs per second; appends to bench/results.csv
dotnet run --project src/Battle.Sim -- render goblin-camp                 # draw an arena
dotnet run --project tests/Battle.Tests -c Release                        # just the tests
```

The viewer needs `pip install -r viewer/requirements.txt` once. `summary.R` uses base R only.

## Scripts and the simulated player

The simulator and terminal play need someone to make the choices. Until trained enemies arrive
(M3), these are hand-written, in `src/Battle.Core/Ai`:

| Script | Plays |
| --- | --- |
| `EnemyScripts` | Each goblin type in character (Cutters gang up and headbutt the hero, Plinkers keep their distance and throw dirt, the Beastmaster hangs back and points), and any goblin whose nerve breaks runs for the edge. The Snuffler has no script: it follows its handler's orders by fixed rules in the core (`Rules.Beasts.cs`), so it only ever has one legal action. |
| `PartyBot` | A straightforward simulated player: it tries each attack from each tile on a copy of the battle and takes the best, then spends its bonus action by simple rules. It plays the hero's whole turn, then the Loper's. It never Tethers, mounts, hands over mid-turn or flees, so a real player should do better. |
| `SimpleScript`, `RandomController` | Baselines: hit the weakest thing in reach, or pick at random (for fuzzing). |

Pick them with `--party` and `--enemies`: `scripted` (the default), `simple` or `random`.

## Logs

Every battle, played or simulated, writes three files with the same name in `logs/`:

| File | What it holds |
| --- | --- |
| `.inputs.jsonl` | The seed and every input. Replays the battle exactly |
| `.events.csv` | One row per event, with HP afterwards and a checksum at the end of each turn. Ready for R's `read.csv` |
| `.snapshots.jsonl` | The whole board at the start of each round, so the viewer can jump around |

`tests/golden` holds a few stored battles whose event logs must not change unless a rule change is
intended. After one, regenerate them with `GOLDEN_UPDATE=1 dotnet run --project tests/Battle.Tests -c Release -- Golden`
and review the diff.

## Build steps

1. The repository, the C# solution and one check script. **Done.**
2. Arena files: the parser, its checks and a static ASCII render. **Done.**
3. Battle state, movement and pathfinding with heights, ladders and movement costs. **Done.**
4. The turn loop, both logs, the replay viewer and terminal play. Fuzzing starts. **Done.**
5. Weapon moves, weight classes, collisions, falling and no ground. **Done.**
6. Features and sources, then the Elderwood's Tiers 1 and 2. **Done.**
7. The Loper, including mounting. **Done.**
8. The four goblin types and their scripts, the balance runner, the R summary and the first real benchmark. **Done.**

M1's build is complete. Next is M2: the Godot battle with real timing.

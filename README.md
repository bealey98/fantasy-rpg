# Fantasy RPG: battle core (M1)

The headless battle core for the game, built step by step from the battle infrastructure plan in the design doc.
M1's question is: **are the fights interesting decisions?**

## What you need

- The .NET 8 SDK (`dotnet --version` should print 8.x or later).
- Python 3.10 or later with `rich`, for the replay viewer (from step 4).
- R, optionally, for the analysis scripts (from step 8).

## Layout

| Folder | Language | What it holds |
| --- | --- | --- |
| `src/Battle.Core` | C# | The rules: battle state, legal actions, events and seeded randomness. No engine, no files, no clock. |
| `src/Battle.Content` | C# | Content files in (arenas, units, moves, rules) and log files out. |
| `src/Battle.Sim` | C# | The command-line simulator: batches, logs, arena renders and the benchmark. |
| `src/Battle.Play` | C# | Play a battle in the terminal by picking from the legal actions. |
| `tests/Battle.Tests` | C# | Rule tests, golden replays, determinism checks and fuzzing. |
| `content/` | CSV, JSON, text | Everything you can rebalance without touching code. |
| `scripts/` | shell | `check.sh` / `check.ps1`: build, test and benchmark in one go. |

## Everyday commands

```
scripts/check.sh                                   # build, run every test, short benchmark (check.ps1 on Windows)
dotnet run --project tests/Battle.Tests -c Release # just the tests
dotnet run --project src/Battle.Sim -- render river-crossing   # draw an arena
```

## Build steps

1. The repository, the C# solution and one check script. **Done.**
2. Arena files: the parser, its checks and a static ASCII render. **Done.**
3. Battle state, movement and pathfinding with heights, ladders and movement costs. **Done.**
4. The turn loop, both logs, the replay viewer and terminal play. Fuzzing starts.
5. Weapon moves, weight classes, collisions, falling and no ground.
6. Features and sources, then the Elderwood's Tiers 1 and 2.
7. The Loper, including mounting.
8. The four goblin types, the batch runner and the first real benchmark.

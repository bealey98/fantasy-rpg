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
| `viewer/` | Python | The replay viewer. It reads logs only, so change it freely. |
| `content/` | CSV, JSON, text | Everything you can rebalance without touching code. |
| `scripts/` | shell | `check.sh` / `check.ps1`: build, test and benchmark in one go. |

## Everyday commands

```
scripts/check.sh                                          # build, every test, short benchmark (check.ps1 on Windows)
dotnet run --project src/Battle.Play -- river-crossing    # play a battle in the terminal
python viewer/replay.py logs/river-crossing-7.events.csv  # watch a logged battle back
dotnet run --project src/Battle.Sim -- run river-crossing --battles 100   # simulate; logs and a summary CSV in logs/
dotnet run --project src/Battle.Sim -- replay logs/river-crossing-1.inputs.jsonl   # check a log replays exactly
dotnet run --project src/Battle.Sim -- fuzz --battles 5000                # random battles, every invariant checked
dotnet run --project src/Battle.Sim -- render river-crossing              # draw an arena
dotnet run --project tests/Battle.Tests -c Release                        # just the tests
```

The viewer needs `pip install -r viewer/requirements.txt` once.

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
8. The four goblin types, the batch runner and the first real benchmark.

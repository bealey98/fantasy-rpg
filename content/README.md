# Content

Everything here can be changed without touching code. The loader checks every file and reports
mistakes with the file name and line. Distances and movement are in **tiles**, and halves are
allowed (`1.5`). Inside the core they're counted in half-points so all the maths stays whole numbers.

## terrain.csv

One row per kind of ground. Every tile has exactly one.

| Column | Meaning |
| --- | --- |
| glyph | The character used in arena files |
| id, name | Code name and display name |
| move_cost | Tiles of movement to step onto it (ground 1, water 1.5) |
| walkable | Whether units can walk onto it (rock and no ground can't be walked onto) |
| no_ground | Anything that enters it dies at once. Must go with height 0 in arenas |
| ladder | Lets units climb two or more levels to or from a neighbouring tile |
| water | Water tiles |
| cover | none, low or high: how much it blocks sight. Higher ground sees over low cover |
| object | Objects never move. Units pushed into one take collision damage, and Tether can anchor to one |

## features.csv

Natural features: what magic draws on. One feature per tile at most.

| Column | Meaning |
| --- | --- |
| glyph, id, name | As above |
| feeds | Elements it feeds, joined with `+`: root, echo, kinship, wind, light, darkness |
| blocks | Blocks movement, like a tree |
| cover, object | As for terrain |
| extra_move_cost | Extra tiles of movement to step onto its tile, like dense growth |
| uses | Uses for light spells, regained each round up to this number |
| regen | Uses regained at the end of each round |
| hp | HP that dark spells drain; a drained feature dies. 0 means dark spells can't use it |
| creature | none, or how it moves on its own: drift (fireflies), flee_attack (birds), flee_near (deer) |

## units.csv

| Column | Meaning |
| --- | --- |
| id, name | Code name and display name. `hero` and `loper` must exist |
| side | party or enemy |
| hp | Starting and maximum HP |
| move | Movement per turn, in tiles |
| weight | light, medium or heavy. Heavier units push and drag lighter ones |
| defence | Subtracted from damage taken |
| power | Enemies act in order of power, strongest first. A beast acts straight after its handler, and the pair goes where the stronger of the two would |
| tags | Joined with `+`: hero, companion, humanoid, beast, goblin |
| moves | Move ids from moves.json, joined with `+` (the hero's come from the encounter instead) |

## moves.json

Every move, as a list. Each move's `kind` picks the rule that handles it (melee, ranged, charge,
push, parry, bind, tether, burrow, swap, scamper, trap, point, whip, taunt, brace); the rest are
numbers for that rule. The file's opening comment lists every key. Unknown keys are errors.

The hero's moves come from the encounter's loadout (two main weapon moves, one bonus weapon move,
and the Elderwood's moves for his tier). Everyone else's come from the `moves` column of units.csv.

## encounters/*.json

One fight: the arena, who each enemy letter is, which goblin handles which beast, the hero's
loadout and whether the enemies won the first strike. See any of the files for the layout.

| Encounter | Arena | Who |
| --- | --- | --- |
| `first-goblins` | `goblin-glade` | Chapter 1's first fight: two Cutters and a Plinker on a rocky rise |
| `goblin-camp` | `goblin-camp` | The chapter's first bigger fight: adds the Beastmaster and its Snuffler; a lookout reached by ladders |
| `goblin-camp-tier2` | `goblin-camp` | The same, with the Elderwood at Tier 2 and Parry instead of Shove |
| `river-crossing` | `river-crossing` | The plan's small test fight: two Cutters |

## rules.json

Rule numbers that aren't tied to one unit or move. Comments (`// ...`) are allowed, and an unknown
key is an error, so typos can't slip through. Each key is explained in the file.

## arenas/*.txt

A few `key: value` lines, then one layer per section. Text after a section name is ignored, so
it can hold a legend.

```
arena: river-crossing
size: 10 x 6
time: day

[height]    0 no ground, 1 low, 2 base, 3 high, 4 very high
2222233344
...
[terrain]   characters from terrain.csv
[features]  characters from features.csv, '.' for none
[units]     H hero, L Loper, lowercase letters for enemies, '.' for none
```

`[height]` and `[terrain]` are required. Height 0 and no-ground terrain must go together. Units
can't start on rock, no ground or inside a tree, and every ladder must sit next to a drop of two
or more levels.

Check an arena with `dotnet run --project src/Battle.Sim -- render <name>`.

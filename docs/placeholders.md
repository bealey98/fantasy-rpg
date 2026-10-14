# M1's stand-ins and open questions

M1 needed an answer to every rule question before a battle could run. The design doc's **M1 audit**
tab lists each rule M1 needed beyond the doc, and Sam has decided nearly all of them. This file
lists what's still a stand-in:

- the few rules still marked Proposed in the audit tab, which came out of the decisions of
  14 October;
- every number, all of which live in `content/` and can be changed without touching code.

Rules live in `src/Battle.Core/Rules/`, one file per topic.

## Rules still to decide

These follow from the decisions of 14 October. Each is what M1 does now, and the audit tab marks
each one Proposed.

### Party turns

| Rule | M1 | Where |
| --- | --- | --- |
| Both turns start together | The hero's and the Loper's turns both start with the party phase, so a daze or knock-over on either takes effect before either acts. The hero has the controls first | `Rules.Turns.cs` |
| Mounting with free swapping | Mounting takes away whatever movement the Loper had left. Dismounting gives the Loper its own movement for the turn, since it couldn't walk while carrying him | `Rules.Loper.cs` |

### The joint turn

| Rule | M1 | Where |
| --- | --- | --- |
| Its place in the order | The Beastmaster and its Snuffler go where the stronger of the two would: with power 3 and 4, they act first | `Rules.Turns.cs` |
| Pointing at a tile | It sends the Snuffler digging there that turn only, and doesn't change its target | `Rules.Goblins.cs` |
| Traps | The Snuffler never chooses to stop on a trap | `Rules.Beasts.cs` |

### Slowed

| Rule | M1 | Where |
| --- | --- | --- |
| Rounding | Half the turn's movement, rounded down to a half-tile: the Snuffler's 3 tiles become 1.5 | `Rules.Turns.cs` |
| Scamper | Scamper's 2 extra tiles aren't halved | `Rules.Goblins.cs` |

### A thrown rider

| Rule | M1 | Where |
| --- | --- | --- |
| Where he lands | The nearest free tile. Among the nearest, first one within a level of the Loper's, then behind the Loper (away from what knocked it over), then beside it, then in front | `Rules.Loper.cs` |
| What it costs him | He's knocked over too, so he loses 3 tiles getting up. A trap where he lands springs on him | `Rules.Loper.cs` |
| Nowhere to land | Only if every free tile in the arena is taken: then he stays in the saddle | `Rules.Loper.cs` |
| A rider knocked over alone | Nothing special. No M1 move can do it: the burrow blast hits both of the pair | `Rules.Status.cs` |

### The party fleeing

| Rule | M1 | Where |
| --- | --- | --- |
| Range | Counted in straight steps, ignoring walls and height | `Rules.Legal.cs` |
| Who counts | Enemies, including a wild Snuffler. A burrowed Snuffler counts where it will come up | `Rules.Legal.cs` |
| A knocked-out Loper | Doesn't have to be on the edge | `Rules.Legal.cs` |
| When | Either of the pair can call it at any point in its turn. The battle ends at once, as "fled" | `Rules.Legal.cs` |

## Numbers and the open question about them

### What timing earns

**Now:** an action command adds 0, 1 or 2 damage (fumble, good, perfect), and a guard takes off
0, 1 or 2 (`rules.json`).

**Why it matters:** the goblins' 1-damage attacks rarely do anything against a guard. In 100
simulated camp battles, 91% of Headbutts, 83% of Plinks and 85% of Lashes did no damage. Damage of
0 is allowed, so this isn't a bug; the question is whether it's the fight you want.

**Options:**
- Guard less.
- Hit harder.
- Accept it: those moves exist for their effects, such as Headbutt's daze and the dirt's blinding.

### The latest balance runs

From `battle-sim balance`, with 300 battles per row, after the decisions of 14 October:

| Encounter | Simulated player, by timing skill (novice / average / expert) | Naive player (average) |
| --- | --- | --- |
| first-goblins | 99% / 100% / 100% wins, about 2.5 rounds | 89% (the rest are draws) |
| goblin-camp | 99% / 100% / 100%, about 5 rounds | 0% |
| goblin-camp-tier2 | 98% / 100% / 100%, about 5–6 rounds | 5% |

The simulated player tries each attack on a copy of the battle before choosing. The naive player
hits the weakest enemy in reach, or walks to the nearest. Neither hands over mid-turn, mounts or
flees.

Two changes show in these numbers:

- **Headbutt as a bonus move:** a Cutter now stabs and headbutts in the same turn. In 100 camp
  battles there were 132 Headbutt hits beside 332 Shiv hits.
- **The joint turn:** the Beastmaster points before its beast acts, and it prefers the hero, who
  is usually out of walking reach. So the Snuffler digs far more: 128 times in 100 camp battles,
  where before it dug in only 3 of 100. The simulated player roots the rumbling ground or steps
  away, so only 28 of those bursts hit the party. The Snuffler died in all 100.

Whether that's the right difficulty is for you to judge by playing.

## Every number, by area

### The grid and movement

| What | M1 | Where |
| --- | --- | --- |
| Climbing | A slope costs 1 extra tile. Ladders cost 1 tile per level | `rules.json` |
| Terrain | Water costs 1.5 tiles to enter, a thicket 2 | `terrain.csv`, `features.csv` |
| Melee and height | Melee reaches at most 1 level up or down | `rules.json` |

### Turns

| What | M1 | Where |
| --- | --- | --- |
| Round cap | A battle still going after 30 rounds is a draw | `rules.json` |
| Fleeing | The party can flee with no enemy within 3 tiles | `rules.json` |

### Timing and statuses

| What | M1 | Where |
| --- | --- | --- |
| Action commands | Strike, Bull Rush and Gore have them. Enemies have none in M1 | `moves.json` |
| Simulated skill | Chances of fumble, good and perfect: novice 40/50/10, average 20/60/20, expert 5/45/50 | `rules.json` |
| Knocked over | Loses 3 tiles of movement getting up | `rules.json` |
| Status lengths | Each move's `rounds` and `status_rounds` | `moves.json` |

### Weapons and force

| What | M1 | Where |
| --- | --- | --- |
| Parry | A good or perfect guard strikes back for 2 | `moves.json` |
| Push distance | Base distance plus the weight difference. Bull Rush's base is 0, Shove's 1 | `moves.json` |
| Collisions | Driven into an object or the arena's edge: 2 damage. Into another unit: none | `rules.json` |
| Falls | Each level beyond the first deals 2 damage | `rules.json` |

### Magic

| What | M1 | Where |
| --- | --- | --- |
| Source range | A caster can draw on features within 3 steps | `rules.json` |
| Tether | Range 4, longest length 4. Pull 3, sap 1 a round | `moves.json`, `rules.json` |
| Dark moves | The source pays 3 HP (the sapping Tether's `dark_cost`) | `moves.json` |
| Features | Uses, regeneration and HP for each feature | `features.csv` |

### The Loper

| What | M1 | Where |
| --- | --- | --- |
| Stats | 8 HP, 6 tiles, heavy. The hero: 10 HP, 4 tiles, medium | `units.csv` |
| Gore | 2 damage after a charge of up to 3 | `moves.json` |
| Taunt | Range 3, for 1 round | `moves.json` |
| Brace | +1 defence for 1 round | `moves.json` |

### Goblins

| What | M1 | Where |
| --- | --- | --- |
| Stats | Cutter: 4 HP, 4 tiles. Plinker: 3 HP, 4 tiles. Beastmaster: 4 HP, 4 tiles. Snuffler: 8 HP, 3 tiles. Power, which sets the turn order: Snuffler 4, Beastmaster 3, Cutter 2, Plinker 1 | `units.csv` |
| Scamper | +2 tiles of movement | `moves.json` |
| Cutter | Shiv 2. Headbutt 1. Gang up +1 on both | `moves.json` |
| Plinker | Plink: 1 damage, range 4. Pocket dirt: range 3, blinds for 1 round. Snap trap: range 3, 1 damage, slows for 1 round | `moves.json` |
| Beastmaster | Lash: 1 damage, range 2. Point: range 8. Whip: range 2, +1 | `moves.json` |
| Snuffler | Gnash 3. Burrow: range 6, blast radius 1, 4 damage, knocks over | `moves.json` |

## The scripts

The goblins' scripts and the simulated party player in `src/Battle.Core/Ai` stand in for players
and trained enemies. Their character is Claude's reading of the kits:

| Script | How it plays |
| --- | --- |
| Cutter | Gangs up, stabs, then headbutts. Once wounded, it swaps a mate into its place instead |
| Plinker | Keeps out of the party's reach, high up when it can |
| Beastmaster | Points its beast before moving, preferring the hero, and hangs back near it |
| Every goblin | Keeps out of its own beast's blast |

The Snuffler has no script: the rules decide what it does (the joint turn, in the audit tab). The
scripts are written to be beaten. They aren't a statement of how goblins should fight.

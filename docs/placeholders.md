# M1's stand-ins and open questions

M1 needed an answer to every rule question before a battle could run, and the design doc settles
only some of them. Everything below is a stand-in that Claude picked so M1 could run. None of it is
decided. Numbers live in `content/` and can be changed without touching code. Rules live in
`src/Battle.Core/Rules/`, one file per topic.

The first section lists the questions that came up during the build and most affect how a fight
feels. The rest is a reference list of every stand-in, by area.

## Questions for you

### 1. What timing earns

**Now:** an action command adds 0, 1 or 2 damage (fumble, good, perfect), and a guard takes off
0, 1 or 2 (`rules.json`).

**Why it matters:** the goblins' 1-damage attacks do nothing against a good guard. In 100 simulated
camp battles, 82% of Plinks, 86% of Lashes and 91% of Headbutts did no damage.

**Options:**
- Guard less.
- Hit harder.
- Accept it: those moves exist for their effects, such as Headbutt's daze and the dirt's blinding.

### 2. Taunt

**Now:** a taunted enemy must attack the Loper whenever the Loper is a valid target for that move.
An enemy that steps to where the Loper is out of reach can hit the hero instead. The goblin scripts
go for the Loper anyway, but trained enemies will learn whatever the rule allows.

**Options:**
- **(a)** Keep it.
- **(b)** A taunted enemy may attack only the Loper.
- **(c)** It must also move towards the Loper.

### 3. How long a status lasts when it lands mid-turn

**Now:** a status that lands during the victim's own phase, such as a trap stepped on mid-move,
lasts through the victim's next turn too. Statuses a unit gives itself, such as Brace, don't get
this.

**Why it matters:** without this rule, a snap trap's slow would wear off before it mattered, and a
root trap would only end the move it caught. It also makes the Tier 2 Bind's "slowed once the
root ends" reach the victim's turn.

**Confirm it or change it.**

### 4. The Snuffler acts before its handler

**Now:** enemies act strongest first. The Snuffler has power 4 and the Beastmaster 3, so a Point
or Whip takes effect on the Snuffler's turn next round.

**Why it matters:** the player always gets a phase to react to an order. The Whip's +1 lasts until
the end of the next enemy phase, so it isn't wasted.

**Alternative:** beasts act right after their handler.

### 5. How the Snuffler decides

This is Claude's proposal. The doc only says that it attacks what its handler points at.

- **Pointed at a unit:**
  - if it can reach the unit this turn, it walks up and bites;
  - otherwise, if a tile next to the unit is within Burrow's range, it burrows there;
  - otherwise, it walks towards the unit.
- **Pointed at a tile:** Point can mark a tile as well as a unit, and the Snuffler burrows there.
  Point marking a tile is also Claude's addition. The scripts never use it.
- **Before any order:** it goes for the nearest of the party.
- **Wild:** it goes for the nearest unit of either side.
- **Taunted:** it bites the Loper whenever it can.

A Burrow can follow a walk in the same turn, as any main action can. Its reach is therefore 3
tiles of walking plus 6 of digging.

### 6. The burrow blast

**Now:** the blast hits every unit near the rumbling tile, goblins included, since the doc says it
knocks over everything nearby.

A root on the rumbling tile catches the Snuffler after the blast, not instead of it. By question 3,
the root then holds it through its next turn.

The crater is one level lower, but never lower than low ground. On base ground that gives the
doc's crater of low ground; on high ground the tile only drops one level.

### 7. Fleeing

The doc leaves two things open. M1's stand-ins:

- **Whether the Snuffler counts:** a living Snuffler doesn't count on the goblins' side.
- **How a goblin gets away:**
  - a goblin that runs can only move and Scamper;
  - it gets away by ending a turn on the arena's edge;
  - a goblin that gets away counts as out of the fight.
- **Once it runs, it keeps running,** even if the odds even up.

**Why it matters:** a fight against two goblins ends as soon as one dies.

### 8. The arena's edge

**Now:** a push into the edge stops there, like a push into a wall, and deals the collision damage
of 2. A Bull Rush and a Shove into the edge can kill a Cutter in the first round (see
`river-crossing`).

**Alternatives:**
- Pushes off the edge go into no ground.
- The edge doesn't hurt.

### 9. The first balance runs

These come from `battle-sim balance`, with 300 battles per row and every number a stand-in:

| Encounter | Simulated player, by timing skill (novice / average / expert) | Naive player (average) |
| --- | --- | --- |
| first-goblins | 100% / 100% / 100% wins, about 2.5 rounds | 97% |
| goblin-camp | 97% / 100% / 100%, about 5 rounds | 0% |
| goblin-camp-tier2 | 98% / 100% / 100%, about 5–8 rounds | 5% |

The simulated player tries each attack on a copy of the battle before choosing. The naive player
hits the weakest enemy in reach, or walks to the nearest. First-goblins is easy either way. The camp
punishes careless play and rewards careful play: rooting the rumbling ground, and keeping out of
the blast. Whether that's the right difficulty is for you to judge by playing.

The simulated player kills the Snuffler first, and it died in all 100 camp battles. Because the
party comes to it, it can almost always bite by walking, so it burrowed in only 3 of them. Burrow
shows up when the party hangs back.

## Stand-ins by area

### The grid and movement

| Rule | M1 | Where |
| --- | --- | --- |
| Directions | 4-way movement. Ranges count orthogonal steps | `Geometry.cs` |
| Split movement | A unit can walk, act, then walk again with what's left | `Rules.Movement.cs` |
| Allies and enemies | Allies can be walked through but not stopped on. Enemies block | `Rules.Movement.cs` |
| Traps | A unit can step onto a trap, never through one. Scripts avoid traps, but the party's will step on one when there's no other way | `Rules.Movement.cs` |
| Climbing | A slope costs 1 extra tile. Ladders cost 1 tile per level | `rules.json` |
| Melee and height | Melee reaches at most 1 level up or down | `rules.json` |

### Turns

| Rule | M1 | Where |
| --- | --- | --- |
| First strike | Only decides which side has the first phase | `Rules.Turns.cs` |
| Losing the Loper | A knocked-out companion sits out the rest of the battle | `Rules.Combat.cs` |
| Losing the hero | The hero falling loses the battle | `Rules.Turns.cs` |
| Round cap | A battle still going after 30 rounds is a draw | `rules.json` |
| Swapping order | Allowed once per party phase, before anyone acts | `Rules.Legal.cs` |

### Timing and statuses

| Rule | M1 | Where |
| --- | --- | --- |
| Action commands | Strike, Bull Rush and Gore have them. Enemies have none in M1 | `moves.json` |
| Guards | Any enemy move that could hurt the party takes a guard | `Rules.Combat.cs` |
| Simulated skill | Chances of fumble, good and perfect: novice 40/50/10, average 20/60/20, expert 5/45/50. Undoing an action in terminal play doesn't re-roll it | `rules.json` |
| Blind | Only spoils the party's command attacks, since enemies have no commands yet | `Rules.Combat.cs` |
| Knocked over | Loses 3 tiles of movement getting up | `rules.json` |
| Slowed | Loses 2 tiles of movement each turn | `rules.json` |

### Weapons and force

| Rule | M1 | Where |
| --- | --- | --- |
| Parry | Blocks the next melee attack completely. A good or perfect guard strikes back for 2 | `moves.json` |
| Push distance | Base distance plus the weight difference. Bull Rush's base is 0, so only lighter targets move. Shove's base is 1 | `moves.json` |
| Collisions | Driven into an object or the arena's edge: 2 damage. Into another unit: none | `rules.json` |
| Banks | A push can't go uphill: even a one-level rise stops it like a wall | `Rules.Force.cs` |
| Falls | A drop of one level is free. Each further level deals 2 damage and ends the push | `rules.json` |
| Held units | Rooted or braced units can't be pushed | `Rules.Force.cs` |

### Magic

| Rule | M1 | Where |
| --- | --- | --- |
| Source range | A caster can draw on features within 3 steps | `rules.json` |
| Weak casts | A light move with no source left in range is cast weak: Bind only slows, and Tether lasts 1 round | `Rules.Elderwood.cs` |
| Paying for dark moves | With no living source, the hero pays in HP first, then the companion | `Rules.Sources.cs` |
| Root traps | Only catch the caster's enemies, and crumble after a round | `Rules.Elderwood.cs` |
| Tether | Range 4, longest length 4, one tether per unit, no tethering a mounted pair. Pull 3, sap 1 a round | `moves.json`, `rules.json` |
| Features | Uses, regeneration and HP for each feature | `features.csv` |
| Creatures | Fireflies drift (even chance each round). Birds flee an attack within 2. Deer flee anyone within 2 | `Rules.Board.cs` |

### The Loper and mounting

| Rule | M1 | Where |
| --- | --- | --- |
| Gore | 2 damage and knocks over, after a charge of up to 3. Dismounted only | `moves.json` |
| Taunt | Range 3, for 1 round (see question 2) | `moves.json` |
| Brace | +1 defence for 1 round. Dismounted only | `moves.json` |
| Riding | The hero rides at the Loper's speed. The Loper can't walk on its own turn while carrying him | `Rules.Loper.cs` |
| Being hit | Enemies can target either of the pair | `Rules.Loper.cs` |
| Being pushed | The pair counts as the heavier of the two | `Rules.Force.cs` |
| Knocked over while mounted | Nothing special happens | `Rules.Turns.cs` |

### Goblins

| Rule | M1 | Where |
| --- | --- | --- |
| Stats | Cutter: 4 HP, 4 tiles. Plinker: 3 HP, 4 tiles. Beastmaster: 4 HP, 4 tiles. Snuffler: 8 HP, 3 tiles. Power, which sets the turn order: Snuffler 4, Beastmaster 3, Cutter 2, Plinker 1 | `units.csv` |
| Scamper | +2 tiles of movement | `moves.json` |
| Cutter | Shiv 2. Headbutt 1. Gang up +1 on both | `moves.json` |
| Gang up | Counts any other goblin next to the target, not the Snuffler | `Rules.Combat.cs` |
| Plinker | Plink: 1 damage, range 4. Pocket dirt: range 3, blinds for 1 round. Snap trap: range 3, 1 damage, slows for 1 round, one per Plinker, stays until something steps on it | `moves.json` |
| Beastmaster | Lash: 1 damage, range 2. Point: range 8. Whip: range 2, +1 | `moves.json` |
| Snuffler | Gnash 3. Burrow: range 6, blast radius 1, 4 damage, knocks over | `moves.json` |

### The scripts

The goblins' scripts and the simulated party player in `src/Battle.Core/Ai` stand in for players
and trained enemies. Their character is Claude's reading of the kits:

| Script | How it plays |
| --- | --- |
| Cutter | Gangs up |
| Plinker | Keeps out of the party's reach, high up when it can |
| Beastmaster | Hangs back near its beast |

The scripts are written to be beaten. They aren't a statement of how goblins should fight.

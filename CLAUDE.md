# Notes for Claude

Sam is designing this game: a turn-based tactical RPG in the style of Paper Mario 64 and TTYD, in an
original world with a Tolkien feel, where strategy is the main skill and the enemies will
eventually be neural networks. Claude writes most of the code.

## The working rule

**Don't decide mechanics without Sam.** When the code needs a rule the design doc doesn't give:

- bring Sam the options rather than picking one;
- record only his decisions in the design doc (Claude Docs);
- label anything not yet decided as a proposal or a placeholder, in the doc and in the code.

Every number in `content/` is a stand-in until Sam says otherwise. `docs/placeholders.md` lists
them, and the open questions about them.

## Checking work

```
scripts/check.sh                                     # build, every test, short benchmark
dotnet run --project tests/Battle.Tests -c Release   # just the tests
```

The README covers the layout and the other commands.

## Things the core relies on

- `src/Battle.Core` is deterministic: integer maths in half points, the random number generator
  kept in the battle state, and a checksum at the end of every turn. It reads no files and no
  clock.
- `Rules.Apply` is the only way to change a battle. `LegalActions` and `Apply` share their target
  lists, so whatever is listed as legal can be applied.
- The golden replays in `tests/golden` fail when a rule changes what happens in a battle. Re-record
  them only for a rule change Sam asked for, and say so in the commit.

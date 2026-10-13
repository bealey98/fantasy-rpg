namespace Battle.Core;

/// <summary>
/// The battle's only source of randomness: a small seeded generator (SplitMix64) that lives
/// inside the state. Copying the state copies the generator, so a copy replays the same rolls.
/// It uses whole-number maths only, so every machine gets the same sequence.
/// </summary>
public struct Rng
{
    public ulong State;

    public Rng(ulong seed) => State = seed;

    public ulong NextU64()
    {
        ulong z = State += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A whole number from 0 up to, but not including, <paramref name="maxExclusive"/>.</summary>
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(NextU64() % (ulong)maxExclusive);
    }

    /// <summary>True with a chance of <paramref name="percent"/> in 100.</summary>
    public bool Chance(int percent) => Next(100) < percent;
}

namespace Battle.Core.Ai;

/// <summary>
/// The hand-written scripts: the baseline enemies have to beat, the fallback, and how early content
/// ships. Until each unit type's own script lands, everyone uses the simple script.
/// </summary>
public static class Scripts
{
    public static IController For(Side side) => new SimpleScript();
}

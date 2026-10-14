namespace Battle.Core;

/// <summary>
/// The version of the battle rules. Every input log records it, so a replay can tell
/// when the rules have changed since the battle was played.
/// Bump the last number for balance tweaks and the middle one for rule changes.
/// </summary>
public static class RulesVersion
{
    public const string Current = "0.2.0";
}

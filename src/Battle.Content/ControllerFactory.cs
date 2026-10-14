using Battle.Core;
using Battle.Core.Ai;

namespace Battle.Content;

/// <summary>Builds controllers by name, for the command-line tools.</summary>
public static class ControllerFactory
{
    public static readonly string[] Names = ["scripted", "simple", "random"];

    public static IController Create(string name, Side side, ulong seed) => name switch
    {
        "scripted" => Scripts.For(side, seed),
        "simple" => new SimpleScript(),
        "random" => new RandomController(seed + (side == Side.Party ? 1UL : 2UL)),
        _ => throw new ContentException($"unknown controller '{name}' (choose {string.Join(", ", Names)})"),
    };
}

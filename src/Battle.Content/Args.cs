using System.Globalization;

namespace Battle.Content;

/// <summary>Minimal command-line parsing: a command, positional values, --options with values and --flags.</summary>
public sealed class Args
{
    private readonly List<string> _positional = new();
    private readonly Dictionary<string, string?> _options = new(StringComparer.Ordinal);

    public string Command { get; }

    /// <param name="hasCommand">When true, the first argument is a command word, like "run".</param>
    public Args(string[] args, bool hasCommand = true)
    {
        Command = hasCommand ? (args.Length > 0 ? args[0] : "help") : "";
        for (int i = hasCommand ? 1 : 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                string key = args[i][2..];
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                _options[key] = hasValue ? args[++i] : null;
            }
            else _positional.Add(args[i]);
        }
    }

    public string? Positional(int index) => index < _positional.Count ? _positional[index] : null;

    public string? Option(string key) => _options.TryGetValue(key, out var v) ? v : null;

    public bool Flag(string key) => _options.ContainsKey(key);

    public int Int(string key, int fallback) =>
        _options.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
}

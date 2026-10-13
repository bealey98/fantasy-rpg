using System.Text;

namespace Battle.Content;

/// <summary>One data row of a CSV file, with its line number for error messages.</summary>
public sealed class CsvRow
{
    private readonly Dictionary<string, string> _values;

    public string File { get; }
    public int Line { get; }

    public CsvRow(string file, int line, Dictionary<string, string> values)
    {
        File = file;
        Line = line;
        _values = values;
    }

    public string Where => $"{File}:{Line}";

    public string Get(string column) =>
        _values.TryGetValue(column, out var v) ? v : throw new ContentException($"{Where}: missing column '{column}'");

    public string GetOr(string column, string fallback) =>
        _values.TryGetValue(column, out var v) && v.Length > 0 ? v : fallback;

    public int Int(string column)
    {
        string raw = Get(column);
        return int.TryParse(raw, out int n) ? n : throw new ContentException($"{Where}: '{column}' should be a whole number, got '{raw}'");
    }

    public bool Bool(string column)
    {
        string raw = Get(column).ToLowerInvariant();
        return raw switch
        {
            "yes" or "true" or "1" => true,
            "no" or "false" or "0" or "" => false,
            _ => throw new ContentException($"{Where}: '{column}' should be yes or no, got '{raw}'"),
        };
    }

    public char Char(string column)
    {
        string raw = Get(column);
        return raw.Length == 1 ? raw[0] : throw new ContentException($"{Where}: '{column}' should be one character, got '{raw}'");
    }
}

/// <summary>
/// A small CSV reader: a header row, then data rows. Fields may be quoted with double quotes.
/// Blank lines are skipped. Kept simple on purpose so the files stay readable in R and Excel too.
/// </summary>
public static class Csv
{
    public static List<CsvRow> Read(string path)
    {
        string file = Path.GetFileName(path);
        string[] lines = File.ReadAllLines(path);
        var rows = new List<CsvRow>();
        string[]? header = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            List<string> fields = SplitLine(line);
            if (header == null)
            {
                header = fields.Select(f => f.Trim()).ToArray();
                continue;
            }
            if (fields.Count != header.Length)
                throw new ContentException($"{file}:{i + 1}: expected {header.Length} fields, found {fields.Count}");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int c = 0; c < header.Length; c++) values[header[c]] = fields[c].Trim();
            rows.Add(new CsvRow(file, i + 1, values));
        }
        return rows;
    }

    private static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}

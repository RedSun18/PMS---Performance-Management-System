using System.Text;

namespace Aic.Pm.Core.Import;

/// <summary>
/// Minimal RFC-4180 CSV reader: quoted fields, embedded commas, quotes and newlines.
/// The legacy exports contain all three (Arabic text, formulas with commas, multi-line remarks).
/// </summary>
public static class Csv
{
    public static List<string[]> ReadFile(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        return Read(reader);
    }

    public static List<string[]> Read(TextReader reader)
    {
        var rows = new List<string[]>();
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        int c;

        void EndField() { row.Add(field.ToString()); field.Clear(); }
        void EndRow()
        {
            EndField();
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
            row.Clear();
        }

        while ((c = reader.Read()) != -1)
        {
            var ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { field.Append('"'); reader.Read(); }
                    else inQuotes = false;
                }
                else field.Append(ch);
            }
            else switch (ch)
            {
                case '"': inQuotes = true; break;
                case ',': EndField(); break;
                case '\r': break;
                case '\n': EndRow(); break;
                default: field.Append(ch); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) EndRow();
        return rows;
    }

    /// <summary>Header-indexed accessor with legacy trimming semantics.</summary>
    public class Table
    {
        private readonly Dictionary<string, int> _index;
        public IReadOnlyList<string[]> Rows { get; }

        public Table(List<string[]> raw)
        {
            var header = raw[0];
            _index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Length; i++) _index[header[i].Trim()] = i;
            Rows = raw.Skip(1).ToList();
        }

        public static Table Load(string path) => new(ReadFile(path));

        public string Get(string[] row, string col)
        {
            if (!_index.TryGetValue(col, out var i) || i >= row.Length) return "";
            return row[i].Trim();
        }

        public int GetInt(string[] row, string col, int fallback = 0) =>
            int.TryParse(Get(row, col), out var v) ? v : fallback;

        public decimal GetDecimal(string[] row, string col, decimal fallback = 0m) =>
            decimal.TryParse(Get(row, col), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

        /// <summary>Legacy exports use dd/MM/yyyy.</summary>
        public DateOnly? GetDate(string[] row, string col)
        {
            var s = Get(row, col);
            if (s.Length == 0) return null;
            if (DateOnly.TryParseExact(s, "dd/MM/yyyy", out var d)) return d;
            if (DateOnly.TryParse(s, out var d2)) return d2;
            return null;
        }
    }
}

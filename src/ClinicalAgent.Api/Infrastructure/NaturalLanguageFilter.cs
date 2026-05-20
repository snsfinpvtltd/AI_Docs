using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ClinicalAgent.Api.Infrastructure;

/// <summary>
/// Parses natural-language filter conditions from the free-text prompt and applies them
/// as row predicates. Multiple conditions compose with AND.
///
/// Numeric:
///   age less than 40  |  age &lt; 40  |  age under/below 40
///   score greater than 80  |  score &gt; 80  |  score over/above 80
///   age between 30 and 50  |  age between 30 to 50  |  age from 30 to 50  |  age 30-50
///   score should be 43.7  |  score must be 43.7  |  score = 43.7  |  score equal to 43.7
///   dosage at most 100  |  dosage at least 75
///
/// Date:
///   visitdate from 20-Jan-2024 to 30-Jan-2024
///   from 20-Jan-2024 to 30-Jan-2024          (auto-detects date column)
///   date after 2024-01-20  |  before 30/01/2024  |  on 25-Jan-2024
///   between Jan 20 2024 and Jan 30 2024
///
/// Text:
///   gender is Female  |  outcome = Completed  |  result equals Improved
///   outcome is not Completed  |  result != Improved
///   treatment contains A  |  group includes Placebo
///   only Female  |  show only Treatment-A
///
/// Performance notes:
///   - WordBoundary regex objects are compiled once and cached per header name.
///   - Column values are pre-parsed into typed arrays before predicates run,
///     eliminating repeated TryParseNum / TryParseDate calls per row.
///   - Row filtering uses PLINQ for datasets larger than 500 rows.
///   - TryParseDate fast-paths the ISO yyyy-MM-dd format (common Excel export)
///     before trying the full 17-format list.
/// </summary>
internal static class NaturalLanguageFilter
{
    // ── Regex cache — compiled once per unique header name ────────────────────
    private static readonly ConcurrentDictionary<string, Regex> _wbCache =
        new(StringComparer.OrdinalIgnoreCase);

    // ── Date token — matches common date literal formats in user prompts ───────
    private const string DateTok =
        @"(?:\d{1,2}[-\s/][A-Za-z]{3,9}[-\s/]\d{2,4}" +
        @"|\d{4}[-/]\d{1,2}[-/]\d{1,2}" +
        @"|\d{1,2}[-/]\d{1,2}[-/]\d{2,4}" +
        @"|[A-Za-z]{3,9}[\s,]+\d{1,2}[\s,]+\d{4})";

    private static readonly Regex DateClause = new(
        @"^\s*(?<op>from|between|after|since|on|before|until|>=|<=|>|<|=)" +
        @"\s+(?<d1>" + DateTok + @")(?:\s+(?:to|and)\s+(?<d2>" + DateTok + @"))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GlobalDateRange = new(
        @"\b(?:from|between)\s+(?<d1>" + DateTok + @")\s+(?:to|and)\s+(?<d2>" + DateTok + @")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Date formats tried when parsing cell values.
    // yyyy-MM-dd is tried inline (fast path) before this list.
    private static readonly string[] CellDateFormats =
    [
        "dd-MMM-yyyy", "d-MMM-yyyy",
        "yyyy-MM-dd",  "yyyy/MM/dd",
        "dd/MM/yyyy",  "d/M/yyyy",
        "MM/dd/yyyy",  "M/d/yyyy",
        "dd-MM-yyyy",  "d-M-yyyy",
        "dd MMM yyyy", "d MMM yyyy",
        "MMM dd, yyyy","MMM d, yyyy",
        "MMMM dd, yyyy","MMMM d, yyyy",
        "dd MMM yyyy HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
    ];

    private static readonly Regex NumericClause = new(
        @"^\s*(?<op>less\s+than|greater\s+than|more\s+than|no\s+more\s+than|no\s+less\s+than" +
        @"|at\s+least|at\s+most|younger\s+than|older\s+than|equal\s+to|is\s+equal\s+to" +
        @"|should\s+be|must\s+be|needs?\s+to\s+be|supposed\s+to\s+be" +
        @"|from|lt|gt|<=|>=|<|>|under|below|above|over|between|minimum|maximum|min|max)" +
        @"\s+(?<v1>-?\d+(?:\.\d+)?)(?:\s+(?:and|to)\s+(?<v2>\d+(?:\.\d+)?))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RangeShorthand = new(
        @"^\s+(?<v1>\d+(?:\.\d+)?)-(?<v2>\d+(?:\.\d+)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BareValue = new(
        @"^\s+(?<val>[\w][\w\-]*)(?:\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","in","for","of","on","to","by","and","or","but",
        "not","only","all","any","some","with","from","that","this","where",
        "when","show","filter","report","data","patients","patient","records",
        "rows","group","groups","between","above","below","over","under",
        "after","before","since","until",
        "should","must","be","been","have","has","had","need","needs",
        "will","would","can","could","shall","may","might","do","does",
        "is","are","was","were","get","got","set","use","used",
    };

    private static readonly Regex TextNeqClause = new(
        @"^\s*(?:is\s+not|not\s+equal(?:\s+to)?|!=|(?:not|excluding|except))\s+(?<val>[\w][\w\-]*(?:\s+[\w][\w\-]*)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TextEqClause = new(
        @"^\s*(?:is|equals?(?:\s+to)?|==?)\s+(?<val>[\w][\w\-]*(?:\s+[\w][\w\-]*)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ContainsClause = new(
        @"^\s*(?:contains?|includes?)\s+(?<val>\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OnlyClause = new(
        @"\b(?:show\s+)?only\s+(?<val>[\w][\w\-]*(?:\s+[\w][\w\-]*)?)(?:\s+(?:patients?|records?|rows?|data|groups?))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Applies all parseable filter conditions from <paramref name="prompt"/> to
    /// <paramref name="rows"/> and returns the filtered list with a human-readable description.
    /// </summary>
    public static (IReadOnlyList<IReadOnlyList<string>> Rows, string? FilterNote) Apply(
        string? prompt,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (string.IsNullOrWhiteSpace(prompt) || headers.Count == 0)
            return (rows, null);

        // Row-index predicates reference pre-computed column arrays,
        // eliminating per-row TryParseNum / TryParseDate calls during filtering.
        var predicates   = new List<Func<int, bool>>();
        var descriptions = new List<string>();

        // ── Per-column filters ────────────────────────────────────────────────
        for (int colIdx = 0; colIdx < headers.Count; colIdx++)
        {
            var header = headers[colIdx];
            var match  = FindHeaderInPrompt(prompt, header);
            if (match is null) continue;

            var after = prompt[(match.Index + match.Length)..];
            int idx   = colIdx;

            // ── Date comparison (checked before numeric) ──────────────────────
            var dc = DateClause.Match(after);
            if (dc.Success && TryParseDate(dc.Groups["d1"].Value, out var d1))
            {
                var op = dc.Groups["op"].Value.Trim().ToLowerInvariant();
                DateTime d2raw = default;
                var hasD2 = dc.Groups["d2"].Success && TryParseDate(dc.Groups["d2"].Value, out d2raw);
                var d2 = hasD2 ? (DateTime?)d2raw : null;

                // Pre-parse all date values for this column once.
                var dates = PrecomputeDates(rows, idx);

                switch (op)
                {
                    case "from":
                    case "between":
                        if (d2.HasValue)
                        {
                            var lo = d1 < d2.Value ? d1 : d2.Value;
                            var hi = d1 < d2.Value ? d2.Value : d1;
                            predicates.Add(i => dates[i] is DateTime d && d.Date >= lo.Date && d.Date <= hi.Date);
                            descriptions.Add($"{header} from {lo:dd-MMM-yyyy} to {hi:dd-MMM-yyyy}");
                        }
                        break;

                    case "after":
                    case "since":
                    case ">":
                        predicates.Add(i => dates[i] is DateTime d && d.Date > d1.Date);
                        descriptions.Add($"{header} after {d1:dd-MMM-yyyy}");
                        break;

                    case ">=":
                        predicates.Add(i => dates[i] is DateTime d && d.Date >= d1.Date);
                        descriptions.Add($"{header} >= {d1:dd-MMM-yyyy}");
                        break;

                    case "before":
                    case "until":
                    case "<":
                        predicates.Add(i => dates[i] is DateTime d && d.Date < d1.Date);
                        descriptions.Add($"{header} before {d1:dd-MMM-yyyy}");
                        break;

                    case "<=":
                        predicates.Add(i => dates[i] is DateTime d && d.Date <= d1.Date);
                        descriptions.Add($"{header} <= {d1:dd-MMM-yyyy}");
                        break;

                    case "on":
                    case "=":
                        predicates.Add(i => dates[i] is DateTime d && d.Date == d1.Date);
                        descriptions.Add($"{header} on {d1:dd-MMM-yyyy}");
                        break;
                }
                continue;
            }

            // ── Numeric comparison ────────────────────────────────────────────
            var cmp = NumericClause.Match(after);
            if (cmp.Success && TryParseNum(cmp.Groups["v1"].Value, out var v1))
            {
                var op = CollapseSpaces(cmp.Groups["op"].Value).ToLowerInvariant();
                double? v2 = cmp.Groups["v2"].Success && TryParseNum(cmp.Groups["v2"].Value, out var tv2)
                    ? tv2 : null;

                // Pre-parse all numeric values for this column once.
                var nums = PrecomputeNums(rows, idx);

                switch (op)
                {
                    case "lessthan": case "lt": case "<":
                    case "under": case "below": case "youngerthan":
                        predicates.Add(i => nums[i] is double v && v < v1);
                        descriptions.Add($"{header} < {v1}");
                        break;

                    case "<=": case "atmost": case "nomorethan":
                    case "maximum": case "max":
                        predicates.Add(i => nums[i] is double v && v <= v1);
                        descriptions.Add($"{header} ≤ {v1}");
                        break;

                    case "greaterthan": case "morethan": case "gt": case ">":
                    case "over": case "above": case "olderthan":
                        predicates.Add(i => nums[i] is double v && v > v1);
                        descriptions.Add($"{header} > {v1}");
                        break;

                    case ">=": case "atleast": case "nolessthan":
                    case "minimum": case "min":
                        predicates.Add(i => nums[i] is double v && v >= v1);
                        descriptions.Add($"{header} ≥ {v1}");
                        break;

                    case "equalto": case "isequaltoto": case "shouldbe":
                    case "mustbe":  case "needstobe":   case "supposedtobe":
                        predicates.Add(i => nums[i] is double v && Math.Abs(v - v1) < 0.0001);
                        descriptions.Add($"{header} = {v1}");
                        break;

                    case "between": case "from":
                        if (v2.HasValue)
                        {
                            var lo = Math.Min(v1, v2.Value);
                            var hi = Math.Max(v1, v2.Value);
                            predicates.Add(i => nums[i] is double v && v >= lo && v <= hi);
                            descriptions.Add($"{header} between {lo} and {hi}");
                        }
                        break;
                }
                continue;
            }

            // ── Shorthand numeric range: "age 30-40" ──────────────────────────
            var sh = RangeShorthand.Match(after);
            if (sh.Success
                && TryParseNum(sh.Groups["v1"].Value, out var sv1)
                && TryParseNum(sh.Groups["v2"].Value, out var sv2))
            {
                var lo = Math.Min(sv1, sv2);
                var hi = Math.Max(sv1, sv2);
                var nums = PrecomputeNums(rows, idx);
                predicates.Add(i => nums[i] is double v && v >= lo && v <= hi);
                descriptions.Add($"{header} between {lo} and {hi}");
                continue;
            }

            // ── Text inequality ───────────────────────────────────────────────
            var tneq = TextNeqClause.Match(after);
            if (tneq.Success)
            {
                var value  = TrimTextValue(tneq.Groups["val"].Value);
                var strs   = PrecomputeStrings(rows, idx);
                predicates.Add(i => !string.Equals(strs[i], value, StringComparison.OrdinalIgnoreCase));
                descriptions.Add($"{header} != '{value}'");
                continue;
            }

            // ── Text equality ─────────────────────────────────────────────────
            var teq = TextEqClause.Match(after);
            if (teq.Success)
            {
                var value = TrimTextValue(teq.Groups["val"].Value);
                var strs  = PrecomputeStrings(rows, idx);
                predicates.Add(i => string.Equals(strs[i], value, StringComparison.OrdinalIgnoreCase));
                descriptions.Add($"{header} = '{value}'");
                continue;
            }

            // ── Contains ──────────────────────────────────────────────────────
            var tc = ContainsClause.Match(after);
            if (tc.Success)
            {
                var value = tc.Groups["val"].Value.Trim();
                var strs  = PrecomputeStrings(rows, idx);
                predicates.Add(i => strs[i]?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
                descriptions.Add($"{header} contains '{value}'");
                continue;
            }

            // ── Bare-value implicit equality (last resort): "TrialID TRIAL-001" ─
            var bare = BareValue.Match(after);
            if (bare.Success)
            {
                var value = bare.Groups["val"].Value.Trim();
                if (!CommonWords.Contains(value))
                {
                    var strs = PrecomputeStrings(rows, idx);
                    predicates.Add(i => string.Equals(strs[i], value, StringComparison.OrdinalIgnoreCase));
                    descriptions.Add($"{header} = '{value}'");
                }
            }
        }

        // ── Global date range (no column name in prompt) ──────────────────────
        var gdr = GlobalDateRange.Match(prompt);
        if (gdr.Success
            && TryParseDate(gdr.Groups["d1"].Value, out var gd1)
            && TryParseDate(gdr.Groups["d2"].Value, out var gd2))
        {
            var gLo = gd1 < gd2 ? gd1 : gd2;
            var gHi = gd1 < gd2 ? gd2 : gd1;
            var rangeDesc = $"from {gLo:dd-MMM-yyyy} to {gHi:dd-MMM-yyyy}";
            var gLoStr    = gLo.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);

            if (!descriptions.Any(d => d.Contains(gLoStr)))
            {
                int dateColIdx = FindDateColumnByHeader(headers);
                if (dateColIdx < 0)
                    dateColIdx = FindDateColumnBySampling(rows, headers.Count);

                if (dateColIdx >= 0)
                {
                    var dates = PrecomputeDates(rows, dateColIdx);
                    predicates.Add(i => dates[i] is DateTime d && d.Date >= gLo.Date && d.Date <= gHi.Date);
                    descriptions.Add($"{headers[dateColIdx]} {rangeDesc}");
                }
            }
        }

        // ── "only X" — cross-column value filter ─────────────────────────────
        var only = OnlyClause.Match(prompt);
        if (only.Success)
        {
            var value = TrimTextValue(only.Groups["val"].Value);
            if (!descriptions.Any(d => d.Contains($"'{value}'")))
            {
                // Pre-compute per-row match to avoid repeating Any() per row.
                var matches = new bool[rows.Count];
                for (int r = 0; r < rows.Count; r++)
                    matches[r] = rows[r].Any(c =>
                        string.Equals(c?.Trim(), value, StringComparison.OrdinalIgnoreCase));
                predicates.Add(i => matches[i]);
                descriptions.Add($"any column = '{value}'");
            }
        }

        if (predicates.Count == 0)
            return (rows, null);

        // Parallel filtering for large datasets; ordered to preserve row sequence.
        var allIndices = Enumerable.Range(0, rows.Count);
        List<IReadOnlyList<string>> filtered;
        if (rows.Count > 500)
        {
            filtered = allIndices
                .AsParallel().AsOrdered()
                .Where(i => predicates.All(p => p(i)))
                .Select(i => rows[i])
                .ToList();
        }
        else
        {
            filtered = allIndices
                .Where(i => predicates.All(p => p(i)))
                .Select(i => rows[i])
                .ToList();
        }

        return (filtered, string.Join(" AND ", descriptions));
    }

    // ── Column pre-computation helpers ────────────────────────────────────────

    /// <summary>Parses every value in the column as a double once; null where unparseable.</summary>
    private static double?[] PrecomputeNums(IReadOnlyList<IReadOnlyList<string>> rows, int colIdx)
    {
        var result = new double?[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (colIdx < row.Count && TryParseNum(row[colIdx], out var v))
                result[i] = v;
        }
        return result;
    }

    /// <summary>Parses every value in the column as a DateTime once; null where unparseable.</summary>
    private static DateTime?[] PrecomputeDates(IReadOnlyList<IReadOnlyList<string>> rows, int colIdx)
    {
        var result = new DateTime?[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (colIdx < row.Count && TryParseDate(row[colIdx], out var d))
                result[i] = d;
        }
        return result;
    }

    /// <summary>Returns the trimmed string for every value in the column.</summary>
    private static string?[] PrecomputeStrings(IReadOnlyList<IReadOnlyList<string>> rows, int colIdx)
    {
        var result = new string?[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (colIdx < row.Count) result[i] = row[colIdx]?.Trim();
        }
        return result;
    }

    // ── Date column discovery ─────────────────────────────────────────────────

    private static int FindDateColumnByHeader(IReadOnlyList<string> headers)
    {
        for (int ci = 0; ci < headers.Count; ci++)
        {
            var h = headers[ci];
            if (h.Contains("date",      StringComparison.OrdinalIgnoreCase) ||
                h.Contains("visit",     StringComparison.OrdinalIgnoreCase) ||
                h.Contains("enroll",    StringComparison.OrdinalIgnoreCase) ||
                h.Contains("collect",   StringComparison.OrdinalIgnoreCase) ||
                h.Contains("admit",     StringComparison.OrdinalIgnoreCase) ||
                h.Contains("discharge", StringComparison.OrdinalIgnoreCase) ||
                h.Contains("birth",     StringComparison.OrdinalIgnoreCase) ||
                h.Contains("sample",    StringComparison.OrdinalIgnoreCase) ||
                h.Contains("time",      StringComparison.OrdinalIgnoreCase))
                return ci;
        }
        return -1;
    }

    private static int FindDateColumnBySampling(IReadOnlyList<IReadOnlyList<string>> rows, int colCount)
    {
        for (int ci = 0; ci < colCount; ci++)
        {
            var sample = rows.Take(20)
                             .Where(r => ci < r.Count && !string.IsNullOrWhiteSpace(r[ci]))
                             .ToList();
            if (sample.Count == 0) continue;
            var dateCount = sample.Count(r => TryParseDate(r[ci], out _));
            if ((double)dateCount / sample.Count >= 0.7) return ci;
        }
        return -1;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Match? FindHeaderInPrompt(string prompt, string header)
    {
        var m = WordBoundary(header).Match(prompt);
        if (m.Success) return m;

        foreach (var part in header.Split(['_', ' ', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length < 3) continue;
            m = WordBoundary(part).Match(prompt);
            if (m.Success) return m;
        }
        return null;
    }

    private static bool TryParseDate(string? s, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();

        // Quick guard: must be at least 6 chars and contain a digit.
        if (t.Length < 6) return false;
        var hasDigit = false;
        foreach (var c in t) { if (char.IsDigit(c)) { hasDigit = true; break; } }
        if (!hasDigit) return false;

        // Fast path: yyyy-MM-dd (most common Excel export / our own normalised format).
        if (t.Length == 10 && t[4] == '-' && t[7] == '-' &&
            DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            return true;

        // Full format list.
        if (DateTime.TryParseExact(t, CellDateFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value))
            return true;

        return DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value);
    }

    private static bool TryParseNum(string? s, out double value)
        => double.TryParse(s?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    /// <summary>Returns a compiled, cached Regex for whole-word matching of <paramref name="word"/>.</summary>
    private static Regex WordBoundary(string word)
        => _wbCache.GetOrAdd(word, static w =>
            new Regex($@"\b{Regex.Escape(w)}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled));

    private static string TrimTextValue(string raw)
    {
        var s = raw.Trim();
        foreach (var sep in new string[] { " and ", " or ", " but ", " where ", " when ", " with " })
        {
            var i = s.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (i > 0) s = s[..i];
        }
        foreach (var noise in new string[] { " patients", " patient", " records", " record",
                                              " rows", " row", " data", " group", " groups", " only" })
        {
            if (s.EndsWith(noise, StringComparison.OrdinalIgnoreCase) && s.Length > noise.Length)
                s = s[..^noise.Length];
        }
        return s.Trim();
    }

    private static string CollapseSpaces(string s)
        => Regex.Replace(s.Trim(), @"\s+", "");
}

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
/// </summary>
internal static class NaturalLanguageFilter
{
    // ── Date token — matches common date literal formats in user prompts ───────
    // e.g. "20-Jan-2024", "2024-01-20", "20/01/2024", "Jan 20, 2024", "January 20 2024"
    private const string DateTok =
        @"(?:\d{1,2}[-\s/][A-Za-z]{3,9}[-\s/]\d{2,4}" +   // 20-Jan-2024
        @"|\d{4}[-/]\d{1,2}[-/]\d{1,2}" +                  // 2024-01-20
        @"|\d{1,2}[-/]\d{1,2}[-/]\d{2,4}" +                // 20/01/2024
        @"|[A-Za-z]{3,9}[\s,]+\d{1,2}[\s,]+\d{4})";        // Jan 20, 2024

    // Date comparison operator immediately after a column name.
    // "from DATE to DATE" / "between DATE and DATE" / "after DATE" / "before DATE" / "on DATE"
    private static readonly Regex DateClause = new(
        @"^\s*(?<op>from|between|after|since|on|before|until|>=|<=|>|<|=)" +
        @"\s+(?<d1>" + DateTok + @")(?:\s+(?:to|and)\s+(?<d2>" + DateTok + @"))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Cross-column date range without a column name prefix.
    // Matches "from 20-Jan-2024 to 30-Jan-2024" anywhere in the prompt.
    private static readonly Regex GlobalDateRange = new(
        @"\b(?:from|between)\s+(?<d1>" + DateTok + @")\s+(?:to|and)\s+(?<d2>" + DateTok + @")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Date formats tried when parsing cell values (covers common Excel export formats).
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

    // ── Numeric clause ────────────────────────────────────────────────────────
    private static readonly Regex NumericClause = new(
        @"^\s*(?<op>less\s+than|greater\s+than|more\s+than|no\s+more\s+than|no\s+less\s+than" +
        @"|at\s+least|at\s+most|younger\s+than|older\s+than|equal\s+to|is\s+equal\s+to" +
        @"|should\s+be|must\s+be|needs?\s+to\s+be|supposed\s+to\s+be" +
        @"|from|lt|gt|<=|>=|<|>|under|below|above|over|between|minimum|maximum|min|max)" +
        @"\s+(?<v1>-?\d+(?:\.\d+)?)(?:\s+(?:and|to)\s+(?<v2>\d+(?:\.\d+)?))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Shorthand numeric range: "age 30-40"
    private static readonly Regex RangeShorthand = new(
        @"^\s+(?<v1>\d+(?:\.\d+)?)-(?<v2>\d+(?:\.\d+)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Bare-value implicit equality: "TrialID TRIAL-001"
    private static readonly Regex BareValue = new(
        @"^\s+(?<val>[\w][\w\-]*)(?:\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Words that must never become implicit bare values.
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","in","for","of","on","to","by","and","or","but",
        "not","only","all","any","some","with","from","that","this","where",
        "when","show","filter","report","data","patients","patient","records",
        "rows","group","groups","between","above","below","over","under",
        "after","before","since","until",
        // Modal / auxiliary verbs
        "should","must","be","been","have","has","had","need","needs",
        "will","would","can","could","shall","may","might","do","does",
        "is","are","was","were","get","got","set","use","used",
    };

    // Text inequality (checked before equality so "is not" is handled first).
    private static readonly Regex TextNeqClause = new(
        @"^\s*(?:is\s+not|not\s+equal(?:\s+to)?|!=|(?:not|excluding|except))\s+(?<val>[\w][\w\-]*(?:\s+[\w][\w\-]*)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Text equality: "is X" | "= X" | "equals X" | "equal to X"
    private static readonly Regex TextEqClause = new(
        @"^\s*(?:is|equals?(?:\s+to)?|==?)\s+(?<val>[\w][\w\-]*(?:\s+[\w][\w\-]*)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Contains / includes
    private static readonly Regex ContainsClause = new(
        @"^\s*(?:contains?|includes?)\s+(?<val>\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "only X" / "show only X" — cross-column value filter
    private static readonly Regex OnlyClause = new(
        @"\b(?:show\s+)?only\s+(?<val>[\w][\w\-]*(?:\s+[\w][\w\-]*)?)(?:\s+(?:patients?|records?|rows?|data|groups?))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Public entry point ────────────────────────────────────────────────────

    public static (IReadOnlyList<IReadOnlyList<string>> Rows, string? FilterNote) Apply(
        string? prompt,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (string.IsNullOrWhiteSpace(prompt) || headers.Count == 0)
            return (rows, null);

        var predicates   = new List<Func<IReadOnlyList<string>, bool>>();
        var descriptions = new List<string>();

        // ── Per-column filters ────────────────────────────────────────────────
        for (int colIdx = 0; colIdx < headers.Count; colIdx++)
        {
            var header = headers[colIdx];
            var match  = FindHeaderInPrompt(prompt, header);
            if (match is null) continue;

            var after = prompt[(match.Index + match.Length)..];
            int idx   = colIdx;

            // ── Date comparison (must be tried before numeric) ────────────────
            var dc = DateClause.Match(after);
            if (dc.Success && TryParseDate(dc.Groups["d1"].Value, out var d1))
            {
                var op = dc.Groups["op"].Value.Trim().ToLowerInvariant();
                DateTime d2raw = default;
                var hasD2 = dc.Groups["d2"].Success && TryParseDate(dc.Groups["d2"].Value, out d2raw);
                var d2 = hasD2 ? (DateTime?)d2raw : null;

                switch (op)
                {
                    case "from":
                    case "between":
                        if (d2.HasValue)
                        {
                            var lo = d1 < d2.Value ? d1 : d2.Value;
                            var hi = d1 < d2.Value ? d2.Value : d1;
                            predicates.Add(row => idx < row.Count
                                && TryParseDate(row[idx], out var d) && d.Date >= lo.Date && d.Date <= hi.Date);
                            descriptions.Add($"{header} from {lo:dd-MMM-yyyy} to {hi:dd-MMM-yyyy}");
                        }
                        break;

                    case "after":
                    case "since":
                    case ">":
                        predicates.Add(row => idx < row.Count
                            && TryParseDate(row[idx], out var d) && d.Date > d1.Date);
                        descriptions.Add($"{header} after {d1:dd-MMM-yyyy}");
                        break;

                    case ">=":
                        predicates.Add(row => idx < row.Count
                            && TryParseDate(row[idx], out var d) && d.Date >= d1.Date);
                        descriptions.Add($"{header} >= {d1:dd-MMM-yyyy}");
                        break;

                    case "before":
                    case "until":
                    case "<":
                        predicates.Add(row => idx < row.Count
                            && TryParseDate(row[idx], out var d) && d.Date < d1.Date);
                        descriptions.Add($"{header} before {d1:dd-MMM-yyyy}");
                        break;

                    case "<=":
                        predicates.Add(row => idx < row.Count
                            && TryParseDate(row[idx], out var d) && d.Date <= d1.Date);
                        descriptions.Add($"{header} <= {d1:dd-MMM-yyyy}");
                        break;

                    case "on":
                    case "=":
                        predicates.Add(row => idx < row.Count
                            && TryParseDate(row[idx], out var d) && d.Date == d1.Date);
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

                switch (op)
                {
                    case "lessthan": case "lt": case "<":
                    case "under": case "below": case "youngerthan":
                        predicates.Add(row => idx < row.Count && TryParseNum(row[idx], out var v) && v < v1);
                        descriptions.Add($"{header} < {v1}");
                        break;

                    case "<=": case "atmost": case "nomorethan":
                    case "maximum": case "max":
                        predicates.Add(row => idx < row.Count && TryParseNum(row[idx], out var v) && v <= v1);
                        descriptions.Add($"{header} ≤ {v1}");
                        break;

                    case "greaterthan": case "morethan": case "gt": case ">":
                    case "over": case "above": case "olderthan":
                        predicates.Add(row => idx < row.Count && TryParseNum(row[idx], out var v) && v > v1);
                        descriptions.Add($"{header} > {v1}");
                        break;

                    case ">=": case "atleast": case "nolessthan":
                    case "minimum": case "min":
                        predicates.Add(row => idx < row.Count && TryParseNum(row[idx], out var v) && v >= v1);
                        descriptions.Add($"{header} ≥ {v1}");
                        break;

                    case "equalto": case "isequaltoto": case "shouldbe":
                    case "mustbe":  case "needstobe":   case "supposedtobe":
                        predicates.Add(row => idx < row.Count && TryParseNum(row[idx], out var v) && Math.Abs(v - v1) < 0.0001);
                        descriptions.Add($"{header} = {v1}");
                        break;

                    case "between": case "from":
                        if (v2.HasValue)
                        {
                            var lo = Math.Min(v1, v2.Value);
                            var hi = Math.Max(v1, v2.Value);
                            predicates.Add(row => idx < row.Count && TryParseNum(row[idx], out var v) && v >= lo && v <= hi);
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
                predicates.Add(row => idx < row.Count && TryParseNum(row[idx], out var v) && v >= lo && v <= hi);
                descriptions.Add($"{header} between {lo} and {hi}");
                continue;
            }

            // ── Text inequality ───────────────────────────────────────────────
            var tneq = TextNeqClause.Match(after);
            if (tneq.Success)
            {
                var value = TrimTextValue(tneq.Groups["val"].Value);
                predicates.Add(row => idx < row.Count &&
                    !string.Equals(row[idx]?.Trim(), value, StringComparison.OrdinalIgnoreCase));
                descriptions.Add($"{header} != '{value}'");
                continue;
            }

            // ── Text equality ─────────────────────────────────────────────────
            var teq = TextEqClause.Match(after);
            if (teq.Success)
            {
                var value = TrimTextValue(teq.Groups["val"].Value);
                predicates.Add(row => idx < row.Count &&
                    string.Equals(row[idx]?.Trim(), value, StringComparison.OrdinalIgnoreCase));
                descriptions.Add($"{header} = '{value}'");
                continue;
            }

            // ── Contains ──────────────────────────────────────────────────────
            var tc = ContainsClause.Match(after);
            if (tc.Success)
            {
                var value = tc.Groups["val"].Value.Trim();
                predicates.Add(row => idx < row.Count &&
                    row[idx]?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
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
                    predicates.Add(row => idx < row.Count &&
                        string.Equals(row[idx]?.Trim(), value, StringComparison.OrdinalIgnoreCase));
                    descriptions.Add($"{header} = '{value}'");
                }
            }
        }

        // ── Global date range (no column name in prompt) ──────────────────────
        // e.g. "from 20-Jan-2024 to 30-Jan-2024" — applies to the first date column found.
        var gdr = GlobalDateRange.Match(prompt);
        if (gdr.Success
            && TryParseDate(gdr.Groups["d1"].Value, out var gd1)
            && TryParseDate(gdr.Groups["d2"].Value, out var gd2))
        {
            var gLo = gd1 < gd2 ? gd1 : gd2;
            var gHi = gd1 < gd2 ? gd2 : gd1;
            var rangeDesc = $"from {gLo:dd-MMM-yyyy} to {gHi:dd-MMM-yyyy}";

            // Only add if no column-specific date filter already covers this range.
            var gLoStr = gLo.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
            if (!descriptions.Any(d => d.Contains(gLoStr)))
            {
                // Priority 1: header name contains a common date keyword — reliable, no sampling.
                int dateColIdx = -1;
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
                    {
                        dateColIdx = ci;
                        break;
                    }
                }
                // Priority 2: fall back to row-sampling heuristic.
                if (dateColIdx < 0)
                {
                    for (int ci = 0; ci < headers.Count; ci++)
                    {
                        if (IsDateColumn(rows, ci)) { dateColIdx = ci; break; }
                    }
                }

                if (dateColIdx >= 0)
                {
                    int idx = dateColIdx;
                    predicates.Add(row => idx < row.Count
                        && TryParseDate(row[idx], out var d)
                        && d.Date >= gLo.Date && d.Date <= gHi.Date);
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
                predicates.Add(row => row.Any(cell =>
                    string.Equals(cell?.Trim(), value, StringComparison.OrdinalIgnoreCase)));
                descriptions.Add($"any column = '{value}'");
            }
        }

        if (predicates.Count == 0)
            return (rows, null);

        var filtered = rows.Where(row => predicates.All(p => p(row))).ToList();
        return (filtered, string.Join(" AND ", descriptions));
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

    /// <summary>
    /// Returns true if at least 70% of non-empty sampled cells in this column parse as dates.
    /// Used to auto-detect the date column for global date range filters.
    /// </summary>
    private static bool IsDateColumn(IReadOnlyList<IReadOnlyList<string>> rows, int colIdx)
    {
        var sample = rows.Take(20)
                         .Where(r => colIdx < r.Count && !string.IsNullOrWhiteSpace(r[colIdx]))
                         .ToList();
        if (sample.Count == 0) return false;
        var dateCount = sample.Count(r => TryParseDate(r[colIdx], out _));
        return (double)dateCount / sample.Count >= 0.7;
    }

    private static bool TryParseDate(string? s, out DateTime value)
    {
        if (string.IsNullOrWhiteSpace(s)) { value = default; return false; }
        var t = s.Trim();

        // Try known formats with invariant culture first (covers Excel export patterns).
        if (DateTime.TryParseExact(t, CellDateFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value))
            return true;

        // Fallback: let the runtime try to parse it (handles locale-aware formats).
        return DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value);
    }

    private static bool TryParseNum(string? s, out double value)
        => double.TryParse(s?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static Regex WordBoundary(string word)
        => new($@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);

    private static string TrimTextValue(string raw)
    {
        var s = raw.Trim();
        foreach (var sep in new[] { " and ", " or ", " but ", " where ", " when ", " with " })
        {
            var i = s.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (i > 0) s = s[..i];
        }
        foreach (var noise in new[] { " patients", " patient", " records", " record",
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

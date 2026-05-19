using System.Text.RegularExpressions;

namespace ClinicalAgent.Api.Infrastructure;

/// <summary>
/// Parses natural-language filter conditions from the free-text prompt and applies them
/// as row predicates. Multiple conditions compose with AND.
///
/// Supported patterns (case-insensitive):
///
///   Ranges    : age between 30 and 40  |  age between 30 to 40  |  age from 30 to 40  |  age 30-40
///   Less-than : age less than 40  |  age &lt; 40  |  age under/below 40  |  age younger than 40
///   Lte       : dosage at most 100  |  dosage &lt;= 100  |  dosage no more than 100  |  dosage maximum 100
///   Gt        : score greater than 80  |  score &gt; 80  |  score over/above 80  |  score older than 60
///   Gte       : dosage at least 75  |  dosage &gt;= 75  |  dosage no less than 75  |  dosage minimum 75
///   Eq num    : dosage equal to 75  |  score = 75
///   Equality  : gender is Female  |  outcome = Completed  |  result equals Improved
///   Inequality: outcome is not Completed  |  result != Improved  |  excluding Placebo
///   Contains  : treatment contains A  |  group includes Placebo
///   Only      : only Female  |  show only Treatment-A  |  only Completed patients
/// </summary>
internal static class NaturalLanguageFilter
{
    // Numeric comparison operator immediately after a column name.
    // Longer alternatives listed before shorter ones so the regex engine tries the longest match first.
    // Separators "and" / "to" both accepted for ranges.
    private static readonly Regex NumericClause = new(
        @"^\s*(?<op>less\s+than|greater\s+than|more\s+than|no\s+more\s+than|no\s+less\s+than" +
        @"|at\s+least|at\s+most|younger\s+than|older\s+than|equal\s+to" +
        @"|from|lt|gt|<=|>=|<|>|under|below|above|over|between|minimum|maximum|min|max)" +
        @"\s+(?<v1>-?\d+(?:\.\d+)?)(?:\s+(?:and|to)\s+(?<v2>\d+(?:\.\d+)?))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Shorthand range with no keyword: "age 30-40"
    private static readonly Regex RangeShorthand = new(
        @"^\s+(?<v1>\d+(?:\.\d+)?)-(?<v2>\d+(?:\.\d+)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Bare-value implicit equality: "TrialID TRIAL-001" (no operator keyword).
    // Only matches alphanumeric-with-hyphen tokens that look like data values, not prose.
    private static readonly Regex BareValue = new(
        @"^\s+(?<val>[\w][\w\-]*)(?:\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Common English words that should never be treated as implicit data values.
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "in", "for", "of", "on", "to", "by", "and", "or", "but",
        "not", "only", "all", "any", "some", "with", "from", "that", "this", "where",
        "when", "show", "filter", "report", "data", "patients", "patient", "records",
        "rows", "group", "groups", "between", "above", "below", "over", "under",
    };

    // Text inequality — must be checked before equality to handle "is not" before "is".
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

    // "only X" / "show only X" — cross-column value filter when no column name is given
    private static readonly Regex OnlyClause = new(
        @"\b(?:show\s+)?only\s+(?<val>[\w][\w\-]*(?:\s+[\w][\w\-]*)?)(?:\s+(?:patients?|records?|rows?|data|groups?))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

            // Everything in the prompt after the matched column name token.
            var after = prompt[(match.Index + match.Length)..];
            int idx   = colIdx; // per-iteration capture for lambda

            // ── Numeric comparison ────────────────────────────────────────────
            var cmp = NumericClause.Match(after);
            if (cmp.Success && TryParse(cmp.Groups["v1"].Value, out var v1))
            {
                var op = CollapseSpaces(cmp.Groups["op"].Value).ToLowerInvariant();
                double? v2 = cmp.Groups["v2"].Success && TryParse(cmp.Groups["v2"].Value, out var tv2)
                    ? tv2 : null;

                switch (op)
                {
                    // Strictly less than
                    case "lessthan":
                    case "lt":
                    case "<":
                    case "under":
                    case "below":
                    case "youngerthan":
                        predicates.Add(row => idx < row.Count && TryParse(row[idx], out var v) && v < v1);
                        descriptions.Add($"{header} < {v1}");
                        break;

                    // Less than or equal
                    case "<=":
                    case "atmost":
                    case "nomorethan":
                    case "maximum":
                    case "max":
                        predicates.Add(row => idx < row.Count && TryParse(row[idx], out var v) && v <= v1);
                        descriptions.Add($"{header} ≤ {v1}");
                        break;

                    // Strictly greater than
                    case "greaterthan":
                    case "morethan":
                    case "gt":
                    case ">":
                    case "over":
                    case "above":
                    case "olderthan":
                        predicates.Add(row => idx < row.Count && TryParse(row[idx], out var v) && v > v1);
                        descriptions.Add($"{header} > {v1}");
                        break;

                    // Greater than or equal
                    case ">=":
                    case "atleast":
                    case "nolessthan":
                    case "minimum":
                    case "min":
                        predicates.Add(row => idx < row.Count && TryParse(row[idx], out var v) && v >= v1);
                        descriptions.Add($"{header} ≥ {v1}");
                        break;

                    // Numeric equality
                    case "equalto":
                        predicates.Add(row => idx < row.Count && TryParse(row[idx], out var v) && v == v1);
                        descriptions.Add($"{header} = {v1}");
                        break;

                    // Inclusive range: "between X and/to Y"  or  "from X to/and Y"
                    case "between":
                    case "from":
                        if (v2.HasValue)
                        {
                            var lo = Math.Min(v1, v2.Value);
                            var hi = Math.Max(v1, v2.Value);
                            predicates.Add(row => idx < row.Count && TryParse(row[idx], out var v) && v >= lo && v <= hi);
                            descriptions.Add($"{header} between {lo} and {hi}");
                        }
                        break;
                }
                continue;
            }

            // ── Shorthand range: "age 30-40" ──────────────────────────────────
            var sh = RangeShorthand.Match(after);
            if (sh.Success
                && TryParse(sh.Groups["v1"].Value, out var sv1)
                && TryParse(sh.Groups["v2"].Value, out var sv2))
            {
                var lo = Math.Min(sv1, sv2);
                var hi = Math.Max(sv1, sv2);
                predicates.Add(row => idx < row.Count && TryParse(row[idx], out var v) && v >= lo && v <= hi);
                descriptions.Add($"{header} between {lo} and {hi}");
                continue;
            }

            // ── Text inequality (must precede equality — "is not" starts with "is") ──
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

            // ── Bare-value implicit equality (last resort): "TrialID TRIAL-001" ──
            // Handles cases where the user types the column name then value with no operator.
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

        // ── "only X" — cross-column value filter (when no column name matched) ──
        var only = OnlyClause.Match(prompt);
        if (only.Success)
        {
            var value = TrimTextValue(only.Groups["val"].Value);
            // Skip if a column-specific predicate already covers this value
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

    // Find the column header as a whole word in the prompt.
    // Falls back to individual parts of compound headers (e.g. "dosage" for "Dosage_mg").
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

    private static Regex WordBoundary(string word)
        => new($@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);

    // Trim trailing stop-words/conjunctions from a captured text value so that
    // "Female and age > 30" produces "Female", and "Female patients" produces "Female".
    private static string TrimTextValue(string raw)
    {
        var s = raw.Trim();

        // Stop at common clause separators
        foreach (var sep in new[] { " and ", " or ", " but ", " where ", " when ", " with " })
        {
            var i = s.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (i > 0) s = s[..i];
        }

        // Strip trailing noise words
        foreach (var noise in new[] { " patients", " patient", " records", " record",
                                       " rows", " row", " data", " group", " groups", " only" })
        {
            if (s.EndsWith(noise, StringComparison.OrdinalIgnoreCase) && s.Length > noise.Length)
                s = s[..^noise.Length];
        }

        return s.Trim();
    }

    // Invariant-culture parse so decimal values work regardless of server locale.
    private static bool TryParse(string? s, out double value)
        => double.TryParse(s?.Trim(), System.Globalization.NumberStyles.Any,
               System.Globalization.CultureInfo.InvariantCulture, out value);

    private static string CollapseSpaces(string s)
        => Regex.Replace(s.Trim(), @"\s+", "");
}

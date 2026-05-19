using System.Text.Json.Serialization;

namespace ClinicalAgent.Api.Infrastructure;

// ── AI filter parsing types ───────────────────────────────────────────────────

/// <summary>A single structured filter condition extracted from the user's natural-language prompt.</summary>
internal sealed record AiFilterCondition
{
    /// <summary>Exact header name from the dataset (case-insensitive match).</summary>
    [JsonPropertyName("col")]  public string Col { get; init; } = string.Empty;

    /// <summary>Operator: eq | neq | lt | lte | gt | gte | between | contains</summary>
    [JsonPropertyName("op")]   public string Op  { get; init; } = string.Empty;

    /// <summary>Text value for eq / neq / contains operations.</summary>
    [JsonPropertyName("val")]  public string? Val  { get; init; }

    /// <summary>Numeric value for lt / lte / gt / gte / eq on a numeric column.</summary>
    [JsonPropertyName("num")]  public double  Num  { get; init; }

    /// <summary>Lower bound for "between".</summary>
    [JsonPropertyName("low")]  public double  Low  { get; init; }

    /// <summary>Upper bound for "between".</summary>
    [JsonPropertyName("high")] public double  High { get; init; }
}

/// <summary>Wrapper returned by the LLM when parsing filter conditions.</summary>
internal sealed record AiFilterResult
{
    [JsonPropertyName("filters")]
    public AiFilterCondition[] Filters { get; init; } = [];
}

// ── AI report content ─────────────────────────────────────────────────────────

/// <summary>Structured report content returned by GPT-4o. All fields are AI-generated.</summary>
internal sealed record AiReportContent
{
    [JsonPropertyName("reportTitle")]
    public string ReportTitle { get; init; } = string.Empty;

    [JsonPropertyName("executiveSummary")]
    public string ExecutiveSummary { get; init; } = string.Empty;

    [JsonPropertyName("keyFindings")]
    public IReadOnlyList<string> KeyFindings { get; init; } = [];

    [JsonPropertyName("dataOverview")]
    public string DataOverview { get; init; } = string.Empty;

    [JsonPropertyName("patientAnalysis")]
    public string PatientAnalysis { get; init; } = string.Empty;

    [JsonPropertyName("adverseEventsAnalysis")]
    public string AdverseEventsAnalysis { get; init; } = string.Empty;

    [JsonPropertyName("statisticalInsights")]
    public string StatisticalInsights { get; init; } = string.Empty;

    [JsonPropertyName("recommendations")]
    public string Recommendations { get; init; } = string.Empty;

    [JsonPropertyName("limitations")]
    public string Limitations { get; init; } = string.Empty;
}

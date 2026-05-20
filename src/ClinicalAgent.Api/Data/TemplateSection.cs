using System.Text.Json.Serialization;

namespace ClinicalAgent.Api.Data;

/// <summary>
/// One configurable section within a custom report template.
/// Instances are JSON-serialised into <see cref="ReportTemplateRecord.SectionsJson"/>.
/// </summary>
public sealed record TemplateSection
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Section type key. One of: ExecutiveSummary, KeyFindings, DataOverview,
    /// PatientAnalysis, AdverseEvents, StatisticalInsights, Recommendations,
    /// Limitations, Charts, DataTable.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>Heading displayed in the generated document. Fully user-customisable.</summary>
    [JsonPropertyName("heading")]
    public string Heading { get; init; } = string.Empty;

    /// <summary>
    /// Optional per-section instruction injected into the AI user prompt.
    /// Null means the default system-level instruction for this section type applies.
    /// </summary>
    [JsonPropertyName("aiInstruction")]
    public string? AiInstruction { get; init; }

    /// <summary>When false the section is silently skipped during document generation.</summary>
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; init; } = true;

    /// <summary>0-based render position (ascending).</summary>
    [JsonPropertyName("order")]
    public int Order { get; init; }
}

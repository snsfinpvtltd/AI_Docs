namespace ClinicalAgent.Core.Models;

/// <summary>Report generation request. When BlobName is null the orchestrator uses all uploads for the user.</summary>
public record ReportRequest(
    string? BlobName,
    string TemplateType,
    OutputFormat OutputFormat,
    string? FreeTextPrompt,
    DateOnly? DateRangeFrom,
    DateOnly? DateRangeTo,
    string? TrialId)
{
    /// <summary>Populated by the controller before calling the orchestrator. Always non-empty.</summary>
    public IReadOnlyList<string> BlobNames { get; init; } = [];
}

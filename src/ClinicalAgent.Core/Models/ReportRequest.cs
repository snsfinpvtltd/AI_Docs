namespace ClinicalAgent.Core.Models;

/// <summary>Report generation request submitted by the user after uploading an Excel file.</summary>
public record ReportRequest(
    string BlobName,
    string TemplateType,
    OutputFormat OutputFormat,
    string? FreeTextPrompt,
    DateOnly? DateRangeFrom,
    DateOnly? DateRangeTo,
    string? TrialId
);

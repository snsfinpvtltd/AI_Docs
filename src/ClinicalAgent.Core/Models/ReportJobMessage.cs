namespace ClinicalAgent.Core.Models;

/// <summary>Message envelope published to the Service Bus report-requests queue for async processing.</summary>
public record ReportJobMessage(
    string JobId,
    string UserId,
    string BlobName,
    string TemplateType,
    OutputFormat OutputFormat,
    string? FreeTextPrompt,
    DateOnly? DateRangeFrom,
    DateOnly? DateRangeTo,
    string? TrialId,
    DateTimeOffset SubmittedAt
);

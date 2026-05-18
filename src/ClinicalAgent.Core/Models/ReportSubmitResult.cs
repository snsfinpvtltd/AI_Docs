namespace ClinicalAgent.Core.Models;

/// <summary>Returned after a report request is accepted — synchronous requests include the download URL immediately.</summary>
public record ReportSubmitResult(
    string JobId,
    string? DownloadUrl,
    bool IsAsync
);

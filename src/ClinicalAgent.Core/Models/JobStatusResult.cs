namespace ClinicalAgent.Core.Models;

/// <summary>Async job status returned when polling GET /api/reports/{jobId}/status.</summary>
public record JobStatusResult(
    string JobId,
    ReportStatus Status,
    string? DownloadUrl,
    int? ProgressPercent,
    string? Error,
    string? StageName = null
);

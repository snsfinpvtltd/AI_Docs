using ClinicalAgent.Core.Models;

namespace ClinicalAgent.Core.Interfaces;

/// <summary>
/// Coordinates report generation — decides sync vs async based on data volume,
/// invokes Semantic Kernel plugins, and delegates to the document generator.
/// </summary>
public interface IReportOrchestrator
{
    /// <summary>Submits a report request. Runs synchronously for small payloads, enqueues otherwise.</summary>
    Task<Result<ReportSubmitResult>> SubmitAsync(
        ReportRequest request,
        string userId,
        CancellationToken ct = default);

    /// <summary>Returns the current status and download URL (if complete) for an async job.</summary>
    Task<Result<JobStatusResult>> GetStatusAsync(
        string jobId,
        CancellationToken ct = default);
}

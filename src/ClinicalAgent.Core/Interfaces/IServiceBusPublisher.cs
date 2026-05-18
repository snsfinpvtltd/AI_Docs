using ClinicalAgent.Core.Models;

namespace ClinicalAgent.Core.Interfaces;

/// <summary>Publishes report job messages to the Azure Service Bus report-requests queue.</summary>
public interface IServiceBusPublisher
{
    /// <summary>Enqueues a report generation job. Returns the job ID on success.</summary>
    Task<Result<string>> PublishReportJobAsync(
        ReportJobMessage message,
        CancellationToken ct = default);
}

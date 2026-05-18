namespace ClinicalAgent.Api.Infrastructure;

// Temporary no-op implementations registered until ClinicalAgent.Plugins provides real ones.
// Replace each registration in Program.cs as the corresponding plugin is implemented.

internal sealed class StubBlobStorageService : IBlobStorageService
{
    public Task<Result<string>> UploadAsync(Stream content, string containerName, string blobPath, string contentType, CancellationToken ct = default)
        => Task.FromResult(Result<string>.Fail("BlobStorageService not yet implemented."));

    public Task<Result<Stream>> DownloadAsync(string containerName, string blobPath, CancellationToken ct = default)
        => Task.FromResult(Result<Stream>.Fail("BlobStorageService not yet implemented."));

    public Task<Result<Uri>> GenerateSasUrlAsync(string containerName, string blobPath, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult(Result<Uri>.Fail("BlobStorageService not yet implemented."));

    public Task<IReadOnlyList<string>> ListBlobsAsync(string containerName, string prefix, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}

internal sealed class StubServiceBusPublisher : IServiceBusPublisher
{
    public Task<Result<string>> PublishReportJobAsync(ReportJobMessage message, CancellationToken ct = default)
        => Task.FromResult(Result<string>.Fail("ServiceBusPublisher not yet implemented."));
}

internal sealed class StubReportOrchestrator : IReportOrchestrator
{
    public Task<Result<ReportSubmitResult>> SubmitAsync(ReportRequest request, string userId, CancellationToken ct = default)
        => Task.FromResult(Result<ReportSubmitResult>.Fail("ReportOrchestrator not yet implemented."));

    public Task<Result<JobStatusResult>> GetStatusAsync(string jobId, CancellationToken ct = default)
        => Task.FromResult(Result<JobStatusResult>.Fail("ReportOrchestrator not yet implemented."));
}

internal sealed class StubTemplateRepository : ITemplateRepository
{
    private static readonly TemplateInfo[] _templates =
    [
        new(TemplateTypes.PatientSummary, "Patient Summary",    "One-page per-patient summary report.",         [OutputFormat.Docx, OutputFormat.Pdf]),
        new(TemplateTypes.OutcomeData,    "Outcome Data",       "Aggregated trial outcome statistics.",         [OutputFormat.Docx, OutputFormat.Pdf, OutputFormat.Excel]),
        new(TemplateTypes.FullReport,     "Full Trial Report",  "Complete study report with all patient data.", [OutputFormat.Docx, OutputFormat.Pdf]),
    ];

    public Task<IReadOnlyList<TemplateInfo>> ListTemplatesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TemplateInfo>>(_templates);
}

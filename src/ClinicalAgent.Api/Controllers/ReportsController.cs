using System.Text.RegularExpressions;
using ClinicalAgent.Api.Data;
using ClinicalAgent.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ClinicalAgent.Api.Controllers;

/// <summary>Submits report generation jobs and polls their status.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportOrchestrator _orchestrator;
    private readonly IBlobStorageService _storage;
    private readonly AppDbContext        _db;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        IReportOrchestrator orchestrator,
        IBlobStorageService storage,
        AppDbContext db,
        ILogger<ReportsController> logger)
    {
        _orchestrator = orchestrator;
        _storage      = storage;
        _db           = db;
        _logger       = logger;
    }

    /// <summary>
    /// Submits a report generation request. Returns the download URL immediately for
    /// small synchronous jobs; returns only the job ID for large async jobs.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ReportSubmitResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReportSubmitResult>> SubmitReport(
        [FromBody] ReportRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TemplateType))
            return Problem("TemplateType is required.", statusCode: 400);

        var userId = User.FindFirst("oid")?.Value ?? User.FindFirst("sub")?.Value ?? "anonymous";

        // Resolve which blobs to include: a specific file or every upload by this user.
        List<string> blobNames;
        if (!string.IsNullOrWhiteSpace(request.BlobName))
        {
            blobNames = [request.BlobName];
        }
        else
        {
            blobNames = await _db.Uploads
                .Where(u => u.UserId == userId)
                .OrderByDescending(u => u.UploadedAt)
                .Select(u => u.BlobName)
                .ToListAsync(cancellationToken);

            if (blobNames.Count == 0)
                return Problem(
                    "No uploaded files found. Please upload an Excel file first.", statusCode: 400);
        }

        var enrichedRequest = request with { BlobNames = blobNames };

        try
        {
            var result = await _orchestrator.SubmitAsync(enrichedRequest, userId, cancellationToken);

            return result.Match<ActionResult<ReportSubmitResult>>(
                onSuccess: v => Ok(v),
                onFailure: err =>
                {
                    _logger.LogWarning("Report submission failed for user {UserId}: {Error}", userId, err);
                    return Problem(err, statusCode: 422);
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error submitting report for user {UserId}", userId);
            return Problem("An unexpected error occurred. Please try again.", statusCode: 500);
        }
    }

    /// <summary>Downloads the generated report file for the given job ID.</summary>
    [HttpGet("{jobId}/download")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadReport(
        string jobId,
        [FromQuery] string format = "docx",
        CancellationToken cancellationToken = default)
    {
        var (mime, ext) = format switch
        {
            "xlsx" => ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
            "pdf"  => ("application/pdf", "pdf"),
            _      => ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx"),
        };

        var result = await _storage.DownloadAsync(BlobContainerNames.Generated, $"{jobId}.{ext}", cancellationToken);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Download requested for missing report {JobId}.{Ext}", jobId, ext);
            return Problem($"Report '{jobId}' not found or has expired.", statusCode: 404);
        }

        var job      = await _db.ReportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        var fileName = job?.ReportName is not null
            ? Regex.Replace(job.ReportName, @"[^a-zA-Z0-9]+", "-").Trim('-') + $".{ext}"
            : $"clinical-report-{jobId}.{ext}";

        return File(result.Value, mime, fileName);
    }

    /// <summary>Returns report job history for the authenticated user, newest first.</summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(IReadOnlyList<ReportJobSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ReportJobSummary>>> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst("oid")?.Value ?? User.FindFirst("sub")?.Value ?? "anonymous";
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(page, 1);

        var records = await _db.ReportJobs
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var result = records.Select(j => new ReportJobSummary(
            j.Id, j.TemplateType, j.OutputFormat, j.Status,
            j.CreatedAt, j.CompletedAt, j.DownloadUrl, j.RowCount, j.PromptText, j.ReportName)).ToList();

        return Ok(result);
    }

    /// <summary>Polls the status of an async report job. Returns the download URL once complete.</summary>
    [HttpGet("{jobId}/status")]
    [ProducesResponseType(typeof(JobStatusResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobStatusResult>> GetReportStatus(
        string jobId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return Problem("jobId is required.", statusCode: 400);

        try
        {
            var result = await _orchestrator.GetStatusAsync(jobId, cancellationToken);

            return result.Match<ActionResult<JobStatusResult>>(
                onSuccess: v => Ok(v),
                onFailure: err =>
                {
                    _logger.LogWarning("Status lookup failed for job {JobId}: {Error}", jobId, err);
                    return Problem($"Job '{jobId}' not found.", statusCode: 404);
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error polling status for job {JobId}", jobId);
            return Problem("An unexpected error occurred. Please try again.", statusCode: 500);
        }
    }
}

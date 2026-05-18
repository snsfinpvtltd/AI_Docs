using Microsoft.AspNetCore.Authorization;

namespace ClinicalAgent.Api.Controllers;

/// <summary>Submits report generation jobs and polls their status.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportOrchestrator _orchestrator;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(IReportOrchestrator orchestrator, ILogger<ReportsController> logger)
    {
        _orchestrator = orchestrator;
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
        if (string.IsNullOrWhiteSpace(request.BlobName))
            return BadRequest(Problem("BlobName is required.", statusCode: 400));

        if (string.IsNullOrWhiteSpace(request.TemplateType))
            return BadRequest(Problem("TemplateType is required.", statusCode: 400));

        var userId = User.FindFirst("oid")?.Value ?? User.FindFirst("sub")?.Value ?? "anonymous";

        try
        {
            var result = await _orchestrator.SubmitAsync(request, userId, cancellationToken);

            return result.Match<ActionResult<ReportSubmitResult>>(
                onSuccess: v => Ok(v),
                onFailure: err =>
                {
                    _logger.LogWarning("Report submission failed for user {UserId}: {Error}", userId, err);
                    return UnprocessableEntity(Problem(err, statusCode: 422));
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error submitting report for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                Problem("An unexpected error occurred. Please try again.", statusCode: 500));
        }
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
            return BadRequest(Problem("jobId is required.", statusCode: 400));

        try
        {
            var result = await _orchestrator.GetStatusAsync(jobId, cancellationToken);

            return result.Match<ActionResult<JobStatusResult>>(
                onSuccess: v => Ok(v),
                onFailure: err =>
                {
                    _logger.LogWarning("Status lookup failed for job {JobId}: {Error}", jobId, err);
                    return NotFound(Problem($"Job '{jobId}' not found.", statusCode: 404));
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error polling status for job {JobId}", jobId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                Problem("An unexpected error occurred. Please try again.", statusCode: 500));
        }
    }
}

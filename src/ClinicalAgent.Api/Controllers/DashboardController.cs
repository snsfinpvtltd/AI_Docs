using ClinicalAgent.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ClinicalAgent.Api.Controllers;

/// <summary>Provides dashboard statistics and recent-activity summaries for the current user.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(AppDbContext db, ILogger<DashboardController> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>Returns aggregate stats, chart breakdowns, and the 5 most recent uploads and reports for the current user.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(DashboardStats), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardStats>> GetDashboard(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("oid")?.Value ?? User.FindFirst("sub")?.Value ?? "anonymous";

        var uploads = await _db.Uploads
            .Where(u => u.UserId == userId)
            .ToListAsync(cancellationToken);

        var jobs = await _db.ReportJobs
            .Where(j => j.UserId == userId)
            .ToListAsync(cancellationToken);

        var totalPatients = uploads.Sum(u => u.RowCount);

        var recentUploads = uploads
            .OrderByDescending(u => u.UploadedAt)
            .Take(5)
            .Select(u => new UploadSummary(
                u.Id, u.FileName, u.RowCount, u.UploadedAt, u.FileSizeBytes, u.BlobName,
                System.Text.Json.JsonSerializer.Deserialize<List<string>>(u.HeadersJson) ?? []))
            .ToList();

        var recentReports = jobs
            .OrderByDescending(j => j.CreatedAt)
            .Take(5)
            .Select(j => new ReportJobSummary(
                j.Id, j.TemplateType, j.OutputFormat, j.Status,
                j.CreatedAt, j.CompletedAt, j.DownloadUrl, j.RowCount, j.PromptText, j.ReportName))
            .ToList();

        // ── Chart data ─────────────────────────────────────────────────────────
        var reportsByStatus = jobs
            .GroupBy(j => j.Status)
            .Select(g => new ChartCount(g.Key, g.Count()))
            .OrderByDescending(s => s.Count)
            .ToList();

        var reportsByTemplate = jobs
            .GroupBy(j => j.TemplateType)
            .Select(g => new ChartCount(g.Key, g.Count()))
            .OrderByDescending(t => t.Count)
            .ToList();

        var reportsByFormat = jobs
            .GroupBy(j => j.OutputFormat)
            .Select(g => new ChartCount(g.Key, g.Count()))
            .OrderByDescending(f => f.Count)
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var uploadsLast7Days = Enumerable.Range(0, 7)
            .Select(i => today.AddDays(-6 + i))
            .Select(d => new DailyCount(
                d.ToString("MMM dd"),
                uploads.Count(u => DateOnly.FromDateTime(u.UploadedAt) == d)))
            .ToList();

        return Ok(new DashboardStats(
            TotalUploads:      uploads.Count,
            TotalPatients:     totalPatients,
            TotalReports:      jobs.Count,
            CompletedReports:  jobs.Count(j => j.Status == "Completed"),
            RecentUploads:     recentUploads,
            RecentReports:     recentReports,
            ReportsByStatus:   reportsByStatus,
            ReportsByTemplate: reportsByTemplate,
            ReportsByFormat:   reportsByFormat,
            UploadsLast7Days:  uploadsLast7Days));
    }
}

/// <summary>Dashboard stats response.</summary>
public record DashboardStats(
    int TotalUploads,
    int TotalPatients,
    int TotalReports,
    int CompletedReports,
    IReadOnlyList<UploadSummary> RecentUploads,
    IReadOnlyList<ReportJobSummary> RecentReports,
    IReadOnlyList<ChartCount> ReportsByStatus,
    IReadOnlyList<ChartCount> ReportsByTemplate,
    IReadOnlyList<ChartCount> ReportsByFormat,
    IReadOnlyList<DailyCount> UploadsLast7Days);

/// <summary>A labelled count used for all chart breakdowns (status / template / format).</summary>
public record ChartCount(string Label, int Count);

/// <summary>A single day's upload count for the activity sparkline.</summary>
public record DailyCount(string Date, int Count);

/// <summary>Report job summary returned by history and dashboard endpoints.</summary>
public record ReportJobSummary(
    string Id,
    string TemplateType,
    string OutputFormat,
    string Status,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? DownloadUrl,
    int RowCount,
    string? PromptText,
    string? ReportName = null);

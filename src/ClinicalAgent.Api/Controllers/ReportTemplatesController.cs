using System.Text.Json;
using System.Text.Json.Serialization;
using ClinicalAgent.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ClinicalAgent.Api.Controllers;

// ── Request / response DTOs ───────────────────────────────────────────────────

/// <summary>Payload for creating or replacing a custom report template.</summary>
public record CustomTemplateRequest(
    string Name,
    string? Description,
    bool IsPublic,
    string? DefaultOutputFormat,
    string? DefaultFilterPrompt,
    TemplateSection[] Sections);

/// <summary>Compact summary used in list views.</summary>
public record CustomTemplateSummary(
    string Id,
    string Name,
    string? Description,
    int SectionCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsPublic,
    string? DefaultOutputFormat,
    string? DefaultFilterPrompt,
    bool IsOwner);

/// <summary>Full template detail including all section definitions.</summary>
public record CustomTemplateDetail(
    string Id,
    string Name,
    string? Description,
    TemplateSection[] Sections,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsPublic,
    string? DefaultOutputFormat,
    string? DefaultFilterPrompt,
    bool IsOwner);

// ── Controller ────────────────────────────────────────────────────────────────

/// <summary>CRUD for user-created report templates stored in SQLite.</summary>
[ApiController]
[Route("api/report-templates")]
[Authorize]
public class ReportTemplatesController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive      = true,
        DefaultIgnoreCondition           = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy             = JsonNamingPolicy.CamelCase,
    };

    private readonly AppDbContext _db;
    private readonly ILogger<ReportTemplatesController> _logger;

    public ReportTemplatesController(AppDbContext db, ILogger<ReportTemplatesController> logger)
    {
        _db     = db;
        _logger = logger;
    }

    private string UserId =>
        User.FindFirst("oid")?.Value ??
        User.FindFirst("sub")?.Value ??
        "anonymous";

    // ── GET /api/report-templates ─────────────────────────────────────────────

    /// <summary>Lists templates owned by the authenticated user plus any public templates.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomTemplateSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CustomTemplateSummary>>> ListAsync(CancellationToken ct)
    {
        var uid     = UserId;
        var records = await _db.ReportTemplates
            .Where(t => t.UserId == uid || t.IsPublic)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct);

        var summaries = records
            .Select(t => new CustomTemplateSummary(
                t.Id, t.Name, t.Description,
                TryDeserializeSections(t.SectionsJson).Length,
                t.CreatedAt, t.UpdatedAt,
                t.IsPublic, t.DefaultOutputFormat, t.DefaultFilterPrompt,
                t.UserId == uid))
            .ToList();

        return Ok(summaries);
    }

    // ── POST /api/report-templates ────────────────────────────────────────────

    /// <summary>Creates a new custom template owned by the authenticated user.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CustomTemplateDetail), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomTemplateDetail>> CreateAsync(
        [FromBody] CustomTemplateRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Problem("Template name is required.", statusCode: 400);

        var id  = Guid.NewGuid().ToString("N")[..12];
        var now = DateTime.UtcNow;

        var record = new ReportTemplateRecord
        {
            Id                  = id,
            Name                = req.Name.Trim(),
            Description         = req.Description?.Trim(),
            UserId              = UserId,
            CreatedAt           = now,
            UpdatedAt           = now,
            SectionsJson        = JsonSerializer.Serialize(req.Sections, JsonOpts),
            IsPublic            = req.IsPublic,
            DefaultOutputFormat = req.DefaultOutputFormat,
            DefaultFilterPrompt = req.DefaultFilterPrompt,
        };

        _db.ReportTemplates.Add(record);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Custom template {Id} '{Name}' created by {UserId}", id, record.Name, UserId);
        return CreatedAtAction("Get", new { id }, ToDetail(record));
    }

    // ── GET /api/report-templates/{id} ───────────────────────────────────────

    /// <summary>Returns the full template detail including all section definitions.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CustomTemplateDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomTemplateDetail>> GetAsync(string id, CancellationToken ct)
    {
        var record = await _db.ReportTemplates.FindAsync([id], ct);
        if (record is null) return NotFound();
        if (record.UserId != UserId && !record.IsPublic) return Forbid();
        return Ok(ToDetail(record));
    }

    // ── PUT /api/report-templates/{id} ───────────────────────────────────────

    /// <summary>Replaces a template's content. Only the owner can update.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(CustomTemplateDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomTemplateDetail>> UpdateAsync(
        string id,
        [FromBody] CustomTemplateRequest req,
        CancellationToken ct)
    {
        var record = await _db.ReportTemplates.FindAsync([id], ct);
        if (record is null) return NotFound();
        if (record.UserId != UserId) return Forbid();

        record.Name                = req.Name.Trim();
        record.Description         = req.Description?.Trim();
        record.SectionsJson        = JsonSerializer.Serialize(req.Sections, JsonOpts);
        record.IsPublic            = req.IsPublic;
        record.DefaultOutputFormat = req.DefaultOutputFormat;
        record.DefaultFilterPrompt = req.DefaultFilterPrompt;
        record.UpdatedAt           = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Custom template {Id} updated by {UserId}", id, UserId);
        return Ok(ToDetail(record));
    }

    // ── DELETE /api/report-templates/{id} ────────────────────────────────────

    /// <summary>Deletes a template. Only the owner can delete.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(string id, CancellationToken ct)
    {
        var record = await _db.ReportTemplates.FindAsync([id], ct);
        if (record is null) return NotFound();
        if (record.UserId != UserId) return Forbid();

        _db.ReportTemplates.Remove(record);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Custom template {Id} deleted by {UserId}", id, UserId);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private CustomTemplateDetail ToDetail(ReportTemplateRecord r) => new(
        r.Id, r.Name, r.Description,
        TryDeserializeSections(r.SectionsJson),
        r.CreatedAt, r.UpdatedAt,
        r.IsPublic, r.DefaultOutputFormat, r.DefaultFilterPrompt,
        r.UserId == UserId);

    private static TemplateSection[] TryDeserializeSections(string json)
    {
        try { return JsonSerializer.Deserialize<TemplateSection[]>(json, JsonOpts) ?? []; }
        catch { return []; }
    }
}

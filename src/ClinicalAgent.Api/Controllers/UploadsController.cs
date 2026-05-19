using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using ClinicalAgent.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ClinicalAgent.Api.Controllers;

/// <summary>Handles Excel file uploads and upload history.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UploadsController : ControllerBase
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".xlsx"];

    private readonly IBlobStorageService _storage;
    private readonly AppDbContext        _db;
    private readonly ILogger<UploadsController> _logger;

    public UploadsController(
        IBlobStorageService storage,
        AppDbContext db,
        ILogger<UploadsController> logger)
    {
        _storage = storage;
        _db      = db;
        _logger  = logger;
    }

    /// <summary>
    /// Uploads an Excel (.xlsx) file, persists metadata, and returns its blob path.
    /// Returns 409 Conflict if the same file (by SHA-256 hash) was already uploaded by this user.
    /// Pass <c>?force=true</c> to bypass the duplicate check and upload regardless.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DuplicateUploadResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<UploadResult>> UploadExcelFile(
        IFormFile file,
        [FromQuery] bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return Problem("File is required.", statusCode: 400);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return Problem($"Only {string.Join(", ", AllowedExtensions)} files are accepted.", statusCode: 400);

        if (file.Length > MaxFileSizeBytes)
            return Problem("File exceeds the 10 MB size limit.", statusCode: 400);

        var userId = User.FindFirst("oid")?.Value ?? User.FindFirst("sub")?.Value ?? "anonymous";

        // Buffer the file so we can hash it, count rows, and upload — all from memory.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        // Compute SHA-256 for duplicate detection.
        var hashBytes = await SHA256.HashDataAsync(buffer, cancellationToken);
        var fileHash  = Convert.ToHexString(hashBytes).ToLowerInvariant();
        buffer.Position = 0;

        // Duplicate check — skipped when force=true.
        if (!force)
        {
            var existing = await _db.Uploads
                .Where(u => u.UserId == userId && u.FileHash == fileHash)
                .OrderByDescending(u => u.UploadedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Duplicate upload detected for user {UserId}: existing record {UploadId} has same SHA-256",
                    userId, existing.Id);

                var existingHeaders = JsonSerializer.Deserialize<List<string>>(existing.HeadersJson) ?? [];
                return Conflict(new DuplicateUploadResponse(
                    IsDuplicate: true,
                    ExistingUpload: new UploadSummary(
                        existing.Id, existing.FileName, existing.RowCount,
                        existing.UploadedAt, existing.FileSizeBytes, existing.BlobName,
                        existingHeaders)));
            }
        }

        // Quick parse to capture row count + headers for the DB record.
        var (rowCount, headers) = QuickParseExcel(buffer);
        buffer.Position = 0;

        var uploadId = Guid.NewGuid().ToString("N")[..12];
        var blobPath = $"{userId}/{uploadId}.xlsx";

        try
        {
            var result = await _storage.UploadAsync(
                buffer,
                BlobContainerNames.Uploads,
                blobPath,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogError("Blob upload failed for user {UserId}: {Error}", userId, result.Error);
                return Problem(result.Error, statusCode: 502);
            }

            var record = new UploadRecord
            {
                Id            = uploadId,
                FileName      = file.FileName,
                BlobName      = blobPath,
                UserId        = userId,
                UploadedAt    = DateTime.UtcNow,
                RowCount      = rowCount,
                FileSizeBytes = file.Length,
                HeadersJson   = JsonSerializer.Serialize(headers),
                FileHash      = fileHash,
            };
            _db.Uploads.Add(record);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Uploaded Excel file {BlobPath} for user {UserId} ({Rows} rows, {Columns} columns, hash={Hash})",
                blobPath, userId, rowCount, headers.Count, fileHash[..8]);

            return Ok(new UploadResult(blobPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error uploading file for user {UserId}", userId);
            return Problem("An unexpected error occurred. Please try again.", statusCode: 500);
        }
    }

    /// <summary>Returns the upload history for the authenticated user, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UploadSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UploadSummary>>> GetUploads(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst("oid")?.Value ?? User.FindFirst("sub")?.Value ?? "anonymous";
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(page, 1);

        var records = await _db.Uploads
            .Where(u => u.UserId == userId)
            .OrderByDescending(u => u.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var result = records.Select(r => new UploadSummary(
            r.Id, r.FileName, r.RowCount, r.UploadedAt, r.FileSizeBytes, r.BlobName,
            JsonSerializer.Deserialize<List<string>>(r.HeadersJson) ?? [])).ToList();

        return Ok(result);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (int rowCount, List<string> headers) QuickParseExcel(Stream stream)
    {
        try
        {
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            var headers = ws.Row(1).CellsUsed()
                .Select(c => c.GetValue<string>())
                .ToList();
            var rowCount = ws.RowsUsed().Count() - 1;
            return (Math.Max(rowCount, 0), headers);
        }
        catch
        {
            return (0, []);
        }
    }
}

/// <summary>Summary of an uploaded file, returned by GET /api/uploads.</summary>
public record UploadSummary(
    string Id,
    string FileName,
    int RowCount,
    DateTime UploadedAt,
    long FileSizeBytes,
    string BlobName,
    IReadOnlyList<string> Headers);

/// <summary>Returned as 409 Conflict when the uploaded file is a duplicate.</summary>
public record DuplicateUploadResponse(bool IsDuplicate, UploadSummary ExistingUpload);

using Microsoft.AspNetCore.Authorization;

namespace ClinicalAgent.Api.Controllers;

/// <summary>Handles Excel file uploads into blob storage prior to report generation.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UploadsController : ControllerBase
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".xlsx"];

    private readonly IBlobStorageService _storage;
    private readonly ILogger<UploadsController> _logger;

    public UploadsController(IBlobStorageService storage, ILogger<UploadsController> logger)
    {
        _storage = storage;
        _logger  = logger;
    }

    /// <summary>Uploads an Excel (.xlsx) file and returns its blob path for use in a report request.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxFileSizeBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<UploadResult>> UploadExcelFile(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(Problem("File is required.", statusCode: 400));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(Problem($"Only {string.Join(", ", AllowedExtensions)} files are accepted.", statusCode: 400));

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(Problem("File exceeds the 10 MB size limit.", statusCode: 400));

        var userId   = User.FindFirst("oid")?.Value ?? User.FindFirst("sub")?.Value ?? "anonymous";
        var blobPath = $"{userId}/{Guid.NewGuid()}.xlsx";

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _storage.UploadAsync(
                stream,
                BlobContainerNames.Uploads,
                blobPath,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogError("Blob upload failed for user {UserId}: {Error}", userId, result.Error);
                return StatusCode(StatusCodes.Status502BadGateway, Problem(result.Error, statusCode: 502));
            }

            _logger.LogInformation("Uploaded Excel file {BlobPath} for user {UserId}", blobPath, userId);
            return Ok(new UploadResult(blobPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error uploading file for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                Problem("An unexpected error occurred. Please try again.", statusCode: 500));
        }
    }
}

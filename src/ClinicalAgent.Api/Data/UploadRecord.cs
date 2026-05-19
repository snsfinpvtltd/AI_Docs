namespace ClinicalAgent.Api.Data;

/// <summary>Persisted metadata for each Excel file uploaded by a user.</summary>
public class UploadRecord
{
    public string Id            { get; set; } = string.Empty;
    public string FileName      { get; set; } = string.Empty;
    public string BlobName      { get; set; } = string.Empty;
    public string UserId        { get; set; } = string.Empty;
    public DateTime UploadedAt  { get; set; } = DateTime.UtcNow;
    public int RowCount         { get; set; }
    public long FileSizeBytes   { get; set; }
    /// <summary>JSON-serialised array of column header names.</summary>
    public string HeadersJson   { get; set; } = "[]";
    /// <summary>SHA-256 hex digest of the file content, used for duplicate detection.</summary>
    public string? FileHash     { get; set; }
}

namespace ClinicalAgent.Api.Data;

/// <summary>Persisted record of every AI report generation job.</summary>
public class ReportJobRecord
{
    public string   Id           { get; set; } = string.Empty;
    public string?  UploadId     { get; set; }       // FK → UploadRecord.Id (nullable — pre-DB uploads won't have one)
    public string?  BlobName     { get; set; }       // null when report was generated from all uploads
    public string   TemplateType { get; set; } = string.Empty;
    public string   OutputFormat { get; set; } = string.Empty;
    public string   Status       { get; set; } = "Completed";
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string?  DownloadUrl  { get; set; }
    public string   UserId       { get; set; } = string.Empty;
    public string?  PromptText   { get; set; }
    public int      RowCount     { get; set; }
    public string?  ReportName   { get; set; }
}

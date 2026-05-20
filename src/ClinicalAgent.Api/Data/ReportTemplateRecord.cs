namespace ClinicalAgent.Api.Data;

/// <summary>User-created report template persisted in SQLite.</summary>
public class ReportTemplateRecord
{
    /// <summary>12-character random hex ID.</summary>
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string UserId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>JSON-serialised <see cref="TemplateSection"/> array.</summary>
    public string SectionsJson { get; set; } = "[]";

    /// <summary>When true the template appears in every user's template list.</summary>
    public bool IsPublic { get; set; }

    /// <summary>Default output format hint: "Docx" | "Pdf" | "Excel".</summary>
    public string? DefaultOutputFormat { get; set; }

    /// <summary>Pre-filled NL filter prompt suggested when using this template.</summary>
    public string? DefaultFilterPrompt { get; set; }
}

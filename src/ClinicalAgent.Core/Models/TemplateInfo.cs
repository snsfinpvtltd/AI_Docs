namespace ClinicalAgent.Core.Models;

/// <summary>Describes a report template available for selection.</summary>
public record TemplateInfo(
    string Id,
    string DisplayName,
    string Description,
    OutputFormat[] SupportedFormats
);

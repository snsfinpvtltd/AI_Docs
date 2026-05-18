using ClinicalAgent.Core.Models;

namespace ClinicalAgent.Core.Interfaces;

/// <summary>Retrieves available report template metadata from Blob Storage.</summary>
public interface ITemplateRepository
{
    /// <summary>Returns all available templates the agent can use for report generation.</summary>
    Task<IReadOnlyList<TemplateInfo>> ListTemplatesAsync(CancellationToken ct = default);
}

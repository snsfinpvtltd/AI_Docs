using Microsoft.AspNetCore.Authorization;

namespace ClinicalAgent.Api.Controllers;

/// <summary>Returns the list of available report templates for the client to display.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TemplatesController : ControllerBase
{
    private readonly ITemplateRepository _templates;
    private readonly ILogger<TemplatesController> _logger;

    public TemplatesController(ITemplateRepository templates, ILogger<TemplatesController> logger)
    {
        _templates = templates;
        _logger    = logger;
    }

    /// <summary>Returns all available report templates and the output formats each supports.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TemplateInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<TemplateInfo>>> GetTemplates(
        CancellationToken cancellationToken)
    {
        try
        {
            var templates = await _templates.ListTemplatesAsync(cancellationToken);
            _logger.LogInformation("Returned {Count} templates", templates.Count);
            return Ok(templates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching templates");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Problem("An unexpected error occurred. Please try again.", statusCode: 500));
        }
    }
}

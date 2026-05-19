using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ClinicalAgent.Api.Infrastructure;

/// <summary>
/// Issues signed JWTs for social sign-in users (Google, GitHub).
/// Validated by the "Social" JWT-bearer scheme registered in Program.cs.
/// </summary>
public sealed class LocalJwtService
{
    private const string Issuer   = "ClinicalAgent";
    private const string Audience = "ClinicalAgent";

    private readonly IConfiguration         _config;
    private readonly ILogger<LocalJwtService> _logger;

    public LocalJwtService(IConfiguration config, ILogger<LocalJwtService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>Issue a signed JWT valid for 8 hours for a social sign-in user.</summary>
    public string Issue(string userId, string email, string name, string provider, CancellationToken ct = default)
    {
        var secret = _config["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured. Add it to appsettings.Development.json.");

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   userId),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Name,  name),
            new Claim("provider", provider),
        };

        var token = new JwtSecurityToken(
            issuer:             Issuer,
            audience:           Audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        _logger.LogInformation("Issued local JWT for provider {Provider}, userId {UserId}", provider, userId);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Build the <see cref="TokenValidationParameters"/> used by the "Social" bearer scheme.</summary>
    public TokenValidationParameters BuildValidationParameters()
    {
        var secret = _config["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

        return new TokenValidationParameters
        {
            ValidIssuer              = Issuer,
            ValidAudience            = Audience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuerSigningKey = true,
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ClockSkew                = TimeSpan.FromMinutes(5),
        };
    }
}

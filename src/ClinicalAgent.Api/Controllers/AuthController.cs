using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using ClinicalAgent.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace ClinicalAgent.Api.Controllers;

/// <summary>
/// Social OAuth endpoints.
/// POST /api/auth/google       — exchange Google access token for a local JWT.
/// GET  /api/auth/github       — redirect to GitHub OAuth authorization page.
/// GET  /api/auth/github/callback — exchange GitHub code, issue JWT, redirect to frontend.
/// GET  /api/auth/providers    — list configured sign-in providers.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly IConfiguration          _config;
    private readonly LocalJwtService         _jwt;
    private readonly IHttpClientFactory      _http;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IConfiguration          config,
        LocalJwtService         jwt,
        IHttpClientFactory      http,
        ILogger<AuthController> logger)
    {
        _config = config;
        _jwt    = jwt;
        _http   = http;
        _logger = logger;
    }

    // ── Google ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validate a Google OAuth access token, fetch user info, and return a local JWT.
    /// The frontend sends the token it obtained from <c>useGoogleLogin</c>.
    /// </summary>
    [HttpPost("google")]
    public async Task<IActionResult> GoogleSignIn(
        [FromBody] SocialTokenRequest req,
        CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", req.Token);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ClinicalAgent/1.0");

            var userRes = await client.GetAsync("https://www.googleapis.com/oauth2/v3/userinfo", ct);
            if (!userRes.IsSuccessStatusCode)
                return Unauthorized(new { error = "Invalid Google access token" });

            var info = await userRes.Content.ReadFromJsonAsync<GoogleUserInfo>(cancellationToken: ct);
            if (info is null || string.IsNullOrWhiteSpace(info.Sub))
                return Unauthorized(new { error = "Could not retrieve Google user info" });

            var token = _jwt.Issue(info.Sub, info.Email ?? $"{info.Sub}@google.local", info.Name ?? info.Email ?? info.Sub, "google", ct);
            return Ok(new SocialAuthResponse(token, "google", info.Email ?? "", info.Name ?? ""));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google sign-in failed");
            return Problem(title: "Google sign-in failed", statusCode: 500);
        }
    }

    // ── GitHub ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Redirect the browser to GitHub's OAuth authorization page.
    /// Stores a CSRF state token in an HttpOnly cookie before redirecting.
    /// </summary>
    [HttpGet("github")]
    public IActionResult GitHubSignIn()
    {
        var clientId = _config["GitHub:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return NotFound(new { error = "GitHub sign-in is not configured on this server." });

        var state       = Guid.NewGuid().ToString("N");
        var callbackUri = $"{Request.Scheme}://{Request.Host}/api/auth/github/callback";

        Response.Cookies.Append("github_oauth_state", state, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            MaxAge   = TimeSpan.FromMinutes(10),
            Secure   = !HttpContext.Request.IsHttps ? false : true,
        });

        var authUrl = QueryHelpers.AddQueryString("https://github.com/login/oauth/authorize", new Dictionary<string, string?>
        {
            ["client_id"]    = clientId,
            ["redirect_uri"] = callbackUri,
            ["scope"]        = "read:user user:email",
            ["state"]        = state,
        });

        return Redirect(authUrl);
    }

    /// <summary>
    /// GitHub OAuth callback.  Exchanges the authorization code for a GitHub access
    /// token, fetches the user profile, issues a local JWT, and redirects the browser
    /// back to the React frontend <c>/auth/callback</c> route with the JWT in the query string.
    /// </summary>
    [HttpGet("github/callback")]
    public async Task<IActionResult> GitHubCallback(
        [FromQuery] string code,
        [FromQuery] string state,
        CancellationToken ct)
    {
        var frontendBase = _config["Frontend:BaseUrl"] ?? "http://localhost:5173";

        var savedState = Request.Cookies["github_oauth_state"];
        Response.Cookies.Delete("github_oauth_state");

        if (savedState != state)
        {
            _logger.LogWarning("GitHub OAuth: CSRF state mismatch");
            return Redirect($"{frontendBase}/?auth_error=csrf");
        }

        var clientId     = _config["GitHub:ClientId"];
        var clientSecret = _config["GitHub:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return Redirect($"{frontendBase}/?auth_error=github_not_configured");

        try
        {
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ClinicalAgent/1.0");

            // Exchange code for GitHub access token (JSON response because of Accept header above).
            var tokenRes = await http.PostAsJsonAsync("https://github.com/login/oauth/access_token", new
            {
                client_id     = clientId,
                client_secret = clientSecret,
                code,
            }, ct);
            tokenRes.EnsureSuccessStatusCode();

            var tokenPayload = await tokenRes.Content.ReadFromJsonAsync<GitHubTokenResponse>(cancellationToken: ct);
            var accessToken  = tokenPayload?.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
                return Redirect($"{frontendBase}/?auth_error=github_token_missing");

            // Fetch user profile.
            var userReq = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var userRes = await http.SendAsync(userReq, ct);
            userRes.EnsureSuccessStatusCode();

            var ghUser = await userRes.Content.ReadFromJsonAsync<GitHubUser>(cancellationToken: ct);
            if (ghUser is null)
                return Redirect($"{frontendBase}/?auth_error=github_user_missing");

            // Fetch primary email if the profile doesn't expose one publicly.
            var email = ghUser.Email;
            if (string.IsNullOrWhiteSpace(email))
            {
                var emailReq = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
                emailReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var emailRes = await http.SendAsync(emailReq, ct);
                if (emailRes.IsSuccessStatusCode)
                {
                    var emails = await emailRes.Content.ReadFromJsonAsync<GitHubEmail[]>(cancellationToken: ct);
                    email = emails?.FirstOrDefault(e => e.Primary)?.Email;
                }
            }

            var jwt = _jwt.Issue(
                userId:   ghUser.Id.ToString(),
                email:    email ?? $"{ghUser.Login}@github.local",
                name:     ghUser.Name ?? ghUser.Login,
                provider: "github",
                ct:       ct);

            return Redirect(QueryHelpers.AddQueryString($"{frontendBase}/auth/callback", new Dictionary<string, string?>
            {
                ["token"]    = jwt,
                ["provider"] = "github",
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub OAuth callback failed");
            return Redirect($"{frontendBase}/?auth_error=github_error");
        }
    }

    // ── Providers list ────────────────────────────────────────────────────────

    /// <summary>Return the list of sign-in providers configured on this server.</summary>
    [HttpGet("providers")]
    public IActionResult GetProviders()
    {
        var providers = new List<string> { "microsoft" };
        if (!string.IsNullOrWhiteSpace(_config["Google:ClientId"])) providers.Add("google");
        if (!string.IsNullOrWhiteSpace(_config["GitHub:ClientId"])) providers.Add("github");
        return Ok(providers);
    }
}

// ── Request / response DTOs ───────────────────────────────────────────────────

/// <summary>Body payload for <c>POST /api/auth/google</c>.</summary>
public sealed record SocialTokenRequest(string Token);

/// <summary>Response returned after a successful social sign-in exchange.</summary>
public sealed record SocialAuthResponse(string Token, string Provider, string Email, string Name);

// ── Internal response models (file-scoped — not part of the public API) ───────

file sealed record GoogleUserInfo(
    [property: JsonPropertyName("sub")]   string  Sub,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("name")]  string? Name);

file sealed record GitHubTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken);

file sealed record GitHubUser(
    [property: JsonPropertyName("id")]    int     Id,
    [property: JsonPropertyName("login")] string  Login,
    [property: JsonPropertyName("name")]  string? Name,
    [property: JsonPropertyName("email")] string? Email);

file sealed record GitHubEmail(
    [property: JsonPropertyName("email")]   string Email,
    [property: JsonPropertyName("primary")] bool   Primary);

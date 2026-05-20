using System.Text;
using System.Text.Json.Serialization;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using ClinicalAgent.Api.Data;
using ClinicalAgent.Api.Infrastructure;
using ClinicalAgent.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    // ── Auth — Microsoft Entra ID (primary) ──────────────────────────────────
    // Requires AzureAd:TenantId + AzureAd:ClientId in appsettings.Development.json.
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);

    // ── Auth — Social providers (Google / GitHub token exchange) ─────────────
    // LocalJwtService issues HS256 tokens when Jwt:Secret is configured.
    // These are validated by the "Social" bearer scheme below.
    builder.Services.AddSingleton<LocalJwtService>();

    var jwtSecret = builder.Configuration["Jwt:Secret"];
    if (!string.IsNullOrWhiteSpace(jwtSecret))
    {
        builder.Services.AddAuthentication()
            .AddJwtBearer("Social", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer              = "ClinicalAgent",
                    ValidAudience            = "ClinicalAgent",
                    IssuerSigningKey         = new SymmetricSecurityKey(
                                                  Encoding.UTF8.GetBytes(jwtSecret)),
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ClockSkew                = TimeSpan.FromMinutes(5),
                };
            });

        // Accept tokens from either Azure AD or the social-login scheme.
        builder.Services.AddAuthorization(opts =>
        {
            var multiSchemePolicy = new AuthorizationPolicyBuilder(
                    JwtBearerDefaults.AuthenticationScheme, "Social")
                .RequireAuthenticatedUser()
                .Build();

            opts.DefaultPolicy  = multiSchemePolicy;
            opts.FallbackPolicy = multiSchemePolicy;
        });
    }
    else
    {
        builder.Services.AddAuthorization();
        Log.Warning("Jwt:Secret not configured — social sign-in (Google/GitHub) will return 500. " +
                    "Add Jwt:Secret to appsettings.Development.json.");
    }

    // ── Application Insights ─────────────────────────────────────────────────
    // Telemetry is shipped via Serilog.Sinks.ApplicationInsights configured in appsettings.json.
    // To also enable the ASP.NET Core SDK (request tracking, dependency tracking), add:
    //   dotnet add package Microsoft.ApplicationInsights.AspNetCore
    // and uncomment: builder.Services.AddApplicationInsightsTelemetry();

    // ── Azure Blob Storage ───────────────────────────────────────────────────
    // Dev:  Storage:ConnectionString = "UseDevelopmentStorage=true"  (Azurite)
    // Prod: Storage:ServiceUri       = "https://{account}.blob.core.windows.net"
    var storageConnectionString = builder.Configuration["Storage:ConnectionString"];
    var storageServiceUri       = builder.Configuration["Storage:ServiceUri"];

    if (!string.IsNullOrWhiteSpace(storageConnectionString))
        builder.Services.AddSingleton(new BlobServiceClient(storageConnectionString));
    else if (!string.IsNullOrWhiteSpace(storageServiceUri))
        builder.Services.AddSingleton(new BlobServiceClient(new Uri(storageServiceUri), new DefaultAzureCredential()));

    // ── Azure Service Bus ────────────────────────────────────────────────────
    // Dev:  ServiceBus:ConnectionString = "<emulator connection string>"
    // Prod: ServiceBus:FullyQualifiedNamespace = "{namespace}.servicebus.windows.net"
    var sbConnectionString = builder.Configuration["ServiceBus:ConnectionString"];
    var sbNamespace        = builder.Configuration["ServiceBus:FullyQualifiedNamespace"];

    if (!string.IsNullOrWhiteSpace(sbConnectionString))
        builder.Services.AddSingleton(new ServiceBusClient(sbConnectionString));
    else if (!string.IsNullOrWhiteSpace(sbNamespace))
        builder.Services.AddSingleton(new ServiceBusClient(sbNamespace, new DefaultAzureCredential()));

    // ── Semantic Kernel + AI provider ────────────────────────────────────────
    // Priority: Groq (free dev) → Azure OpenAI (production) → none (raw data only)
    var kernelBuilder  = builder.Services.AddKernel();
    var groqApiKey     = builder.Configuration["Groq:ApiKey"];
    var aoaiEndpoint   = builder.Configuration["AzureOpenAI:Endpoint"];
    var aoaiDeployment = builder.Configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
    var aoaiApiKey     = builder.Configuration["AzureOpenAI:ApiKey"];

    var aiConfigured = false;

    if (!string.IsNullOrWhiteSpace(groqApiKey))
    {
        // Groq: OpenAI-compatible, free tier — ideal for dev/POC.
        kernelBuilder.AddOpenAIChatCompletion(
            modelId:    "llama-3.3-70b-versatile",
            apiKey:     groqApiKey,
            httpClient: new HttpClient { BaseAddress = new Uri("https://api.groq.com/openai/v1/") });
        aiConfigured = true;
        Log.Information("AI provider: Groq (llama-3.3-70b-versatile)");
    }
    else if (!string.IsNullOrWhiteSpace(aoaiEndpoint))
    {
        // Azure OpenAI: production path — API key for dev convenience, DefaultAzureCredential for prod.
        if (!string.IsNullOrWhiteSpace(aoaiApiKey))
            kernelBuilder.AddAzureOpenAIChatCompletion(aoaiDeployment, aoaiEndpoint, aoaiApiKey);
        else
            kernelBuilder.AddAzureOpenAIChatCompletion(aoaiDeployment, aoaiEndpoint, new DefaultAzureCredential());
        aiConfigured = true;
        Log.Information("AI provider: Azure OpenAI ({Deployment})", aoaiDeployment);
    }
    else
    {
        Log.Warning("No AI provider configured — reports will use local data formatting. " +
                    "Set Groq:ApiKey in appsettings.Development.json to enable AI-generated reports.");
    }

    // ── Domain services ───────────────────────────────────────────────────────
    // Singleton so the in-memory blob store and job dictionary survive across requests.
    builder.Services.AddSingleton<IBlobStorageService,  StubBlobStorageService>();
    builder.Services.AddSingleton<IServiceBusPublisher, StubServiceBusPublisher>();
    builder.Services.AddSingleton<ITemplateRepository,  StubTemplateRepository>();

    // Use AI orchestrator when a provider is configured; fall back to raw data formatting.
    if (aiConfigured)
        builder.Services.AddSingleton<IReportOrchestrator, SemanticKernelOrchestrator>();
    else
        builder.Services.AddSingleton<IReportOrchestrator, LocalReportOrchestrator>();

    // ── HTTP resilience (Polly) ───────────────────────────────────────────────
    // Provides 3 retries with exponential backoff on all named HttpClient usages.
    builder.Services.AddHttpClient("default")
        .AddStandardResilienceHandler();

    // ── MVC / Swagger ─────────────────────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
            opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "ClinicalAgent API", Version = "v1" });
    });
    builder.Services.AddProblemDetails();

    // ── SQLite database ──────────────────────────────────────────────────────
    // Dev:  Database:Path = "clinicalagent.db"  (SQLite file in working dir)
    // Prod: swap UseSqlite for UseSqlServer / UseNpgsql + add the matching NuGet package.
    var dbPath = builder.Configuration["Database:Path"] ?? "clinicalagent.db";

    builder.Services.AddDbContextFactory<AppDbContext>(opts =>
        opts.UseSqlite($"Data Source={dbPath}"));

    // Also register as scoped so controllers can inject AppDbContext directly.
    builder.Services.AddDbContext<AppDbContext>(opts =>
        opts.UseSqlite($"Data Source={dbPath}"));

    // ── Health checks ────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ── CORS ─────────────────────────────────────────────────────────────────
    builder.Services.AddCors(opt => opt.AddPolicy("LocalDev", policy =>
        policy.SetIsOriginAllowed(origin =>
                  Uri.TryCreate(origin, UriKind.Absolute, out var u) &&
                  u.Host == "localhost" &&
                  (u.Scheme == "http" || u.Scheme == "https"))
              .AllowAnyHeader()
              .AllowAnyMethod()));

    // ─────────────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // Auto-create SQLite schema on startup (no migrations needed for POC).
    // Also applies any additive ALTER TABLE changes so new columns don't
    // require dropping the database when the model evolves.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        // Idempotent column additions — safe to run on every startup.
        var addColumns = new[]
        {
            "ALTER TABLE ReportJobs ADD COLUMN ReportName TEXT NULL",
        };
        foreach (var sql in addColumns)
        {
            try { db.Database.ExecuteSqlRaw(sql); }
            catch { /* column already exists — ignore */ }
        }

        Log.Information("Database ready: {DbPath}", dbPath);
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ClinicalAgent API v1"));
        app.UseCors("LocalDev");
    }

    app.UseSerilogRequestLogging();
    if (!app.Environment.IsDevelopment())
        app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/health").AllowAnonymous();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application start-up failed");
}
finally
{
    Log.CloseAndFlush();
}

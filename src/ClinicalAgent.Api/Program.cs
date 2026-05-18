using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using ClinicalAgent.Api.Infrastructure;
using ClinicalAgent.Core.Interfaces;
using Microsoft.Identity.Web;
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

    // ── Auth ─────────────────────────────────────────────────────────────────
    // Requires AzureAd:TenantId + AzureAd:ClientId in appsettings.Development.json.
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);
    builder.Services.AddAuthorization();

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

    // ── Semantic Kernel ──────────────────────────────────────────────────────
    var kernelBuilder = builder.Services.AddKernel();
    var aoaiEndpoint  = builder.Configuration["AzureOpenAI:Endpoint"];

    if (!string.IsNullOrWhiteSpace(aoaiEndpoint))
    {
        kernelBuilder.AddAzureOpenAIChatCompletion(
            deploymentName: builder.Configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o",
            endpoint:       aoaiEndpoint,
            credentials:    new DefaultAzureCredential());
    }

    // ── Domain services ───────────────────────────────────────────────────────
    // TODO: replace stubs with real implementations from ClinicalAgent.Plugins
    builder.Services.AddScoped<IBlobStorageService,  StubBlobStorageService>();
    builder.Services.AddScoped<IServiceBusPublisher, StubServiceBusPublisher>();
    builder.Services.AddScoped<IReportOrchestrator,  StubReportOrchestrator>();
    builder.Services.AddScoped<ITemplateRepository,  StubTemplateRepository>();

    // ── HTTP resilience (Polly) ───────────────────────────────────────────────
    // Provides 3 retries with exponential backoff on all named HttpClient usages.
    builder.Services.AddHttpClient("default")
        .AddStandardResilienceHandler();

    // ── MVC / Swagger ─────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "ClinicalAgent API", Version = "v1" });
    });
    builder.Services.AddProblemDetails();

    // ── Health checks ────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ── CORS ─────────────────────────────────────────────────────────────────
    builder.Services.AddCors(opt => opt.AddPolicy("LocalDev", policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()));

    // ─────────────────────────────────────────────────────────────────────────
    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ClinicalAgent API v1"));
        app.UseCors("LocalDev");
    }

    app.UseSerilogRequestLogging();
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

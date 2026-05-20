using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ClinicalAgent.Api.Data;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using OxDocument = DocumentFormat.OpenXml.Wordprocessing.Document;
using OxColor    = DocumentFormat.OpenXml.Wordprocessing.Color;
using QDocument  = QuestPDF.Fluent.Document;

namespace ClinicalAgent.Api.Infrastructure;

/// <summary>
/// AI-powered report orchestrator. Parses the uploaded Excel with ClosedXML,
/// sends the data to GPT-4o via Semantic Kernel, and generates a rich Word / PDF / Excel
/// report from the structured AI analysis.
/// </summary>
internal sealed class SemanticKernelOrchestrator : IReportOrchestrator
{
    private readonly IBlobStorageService _storage;
    private readonly Kernel _kernel;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<SemanticKernelOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, JobStatusResult> _jobs = new();

    // Caches the LLM-parsed filter conditions so repeated requests with the same
    // prompt + column layout skip the extra Groq API call entirely.
    private readonly ConcurrentDictionary<string, AiFilterResult?> _aiFilterCache = new(StringComparer.OrdinalIgnoreCase);

    private const int MaxRowsToAi    = 30;    // rows sent to LLM — kept low for Groq 12k TPM free tier
    private const int MaxDocRows      = 500;   // rows in Word appendix table
    private const int MaxPdfRows      = 1000;  // rows in PDF data table

    // Default section order used when no custom template is selected.
    private static readonly IReadOnlyList<ClinicalAgent.Api.Data.TemplateSection> DefaultSections =
    [
        new() { Id="d1",  Type="ExecutiveSummary",    Heading="Executive Summary",    IsEnabled=true, Order=0  },
        new() { Id="d2",  Type="KeyFindings",          Heading="Key Findings",         IsEnabled=true, Order=1  },
        new() { Id="d3",  Type="Charts",               Heading="Data Visualisations",  IsEnabled=true, Order=2  },
        new() { Id="d4",  Type="DataOverview",         Heading="Data Overview",        IsEnabled=true, Order=3  },
        new() { Id="d5",  Type="PatientAnalysis",      Heading="Patient Analysis",     IsEnabled=true, Order=4  },
        new() { Id="d6",  Type="AdverseEvents",        Heading="Adverse Events",       IsEnabled=true, Order=5  },
        new() { Id="d7",  Type="StatisticalInsights",  Heading="Statistical Insights", IsEnabled=true, Order=6  },
        new() { Id="d8",  Type="Recommendations",      Heading="Recommendations",      IsEnabled=true, Order=7  },
        new() { Id="d9",  Type="Limitations",          Heading="Limitations",          IsEnabled=true, Order=8  },
        new() { Id="d10", Type="DataTable",            Heading="Appendix — Raw Data",  IsEnabled=true, Order=9  },
    ];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    static SemanticKernelOrchestrator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public SemanticKernelOrchestrator(
        IBlobStorageService storage,
        Kernel kernel,
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<SemanticKernelOrchestrator> logger)
    {
        _storage   = storage;
        _kernel    = kernel;
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    // ── IReportOrchestrator ───────────────────────────────────────────────────

    /// <summary>
    /// Queues an AI report job, persists a Processing record immediately, and returns
    /// as soon as the background task is fired. Progress is tracked via GetStatusAsync.
    /// </summary>
    public async Task<Result<ReportSubmitResult>> SubmitAsync(
        ReportRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var jobId      = Guid.NewGuid().ToString("N")[..12];
        var ext        = request.OutputFormat switch
        {
            OutputFormat.Excel => "xlsx",
            OutputFormat.Pdf   => "pdf",
            _                  => "docx"
        };
        var downloadUrl = $"http://localhost:7001/api/reports/{jobId}/download?format={ext}";
        var reportName  = BuildReportName(request);

        // Register immediately so the first status poll succeeds.
        _jobs[jobId] = new JobStatusResult(jobId, ReportStatus.Processing, null, 5, null, "Preparing…");

        // Persist a Processing record so history shows the job while it runs.
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            var singleBlob = request.BlobNames.Count == 1 ? request.BlobNames[0] : null;
            var uploadId   = singleBlob is not null
                ? await db.Uploads.Where(u => u.BlobName == singleBlob).Select(u => u.Id).FirstOrDefaultAsync(ct)
                : null;

            db.ReportJobs.Add(new ReportJobRecord
            {
                Id           = jobId,
                UploadId     = uploadId,
                BlobName     = singleBlob,
                TemplateType = request.TemplateType,
                OutputFormat = ext,
                Status       = "Processing",
                CreatedAt    = DateTime.UtcNow,
                CompletedAt  = null,
                DownloadUrl  = null,
                UserId       = userId,
                PromptText   = request.FreeTextPrompt,
                RowCount     = 0,
                ReportName   = reportName,
            });
            await db.SaveChangesAsync(ct);
        }

        // Fire background task — intentionally not awaited.
        _ = Task.Run(() => RunJobAsync(jobId, ext, downloadUrl, reportName, request, userId));

        _logger.LogInformation("AI report job {JobId} queued for user {UserId}", jobId, userId);
        return Result<ReportSubmitResult>.Ok(new ReportSubmitResult(jobId, null, IsAsync: true));
    }

    /// <summary>Returns the current job status, with DB fallback for page-refresh resilience.</summary>
    public async Task<Result<JobStatusResult>> GetStatusAsync(string jobId, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(jobId, out var cached))
            return Result<JobStatusResult>.Ok(cached);

        // DB fallback: survives page refreshes and server restarts.
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var job = await db.ReportJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job is null)
            return Result<JobStatusResult>.Fail($"Job '{jobId}' not found.");

        var rs = job.Status switch
        {
            "Completed"  => ReportStatus.Completed,
            "Failed"     => ReportStatus.Failed,
            "Processing" => ReportStatus.Processing,
            _            => ReportStatus.Queued,
        };
        return Result<JobStatusResult>.Ok(new JobStatusResult(
            jobId, rs, job.DownloadUrl,
            rs == ReportStatus.Completed ? 100 : null,
            rs == ReportStatus.Failed ? "Generation failed — please try again." : null,
            rs == ReportStatus.Completed ? "Report ready" : rs.ToString()));
    }

    // ── Background job ────────────────────────────────────────────────────────

    private async Task RunJobAsync(
        string jobId,
        string ext,
        string downloadUrl,
        string reportName,
        ReportRequest request,
        string userId)
    {
        try
        {
            // Stage 1 — load & merge Excel data (10 %)
            SetProgress(jobId, 10, "Loading data files");
            ParsedData data;
            try
            {
                data = await DownloadAndMergeAsync(request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load data for job {JobId}", jobId);
                await FinishJobAsync(jobId, "Failed", 0, null, $"Could not load data: {ex.Message}");
                return;
            }

            // Stage 2 — apply smart filter (25 %)
            SetProgress(jobId, 25, "Applying filters");
            var (filteredRows, filterNote) = await ParseAndApplyFiltersAsync(
                request.FreeTextPrompt, data.Headers, data.Rows, jobId, CancellationToken.None);
            if (filterNote is not null)
            {
                _logger.LogInformation("Filter applied for job {JobId}: {Filter} → {Count}/{Total} rows",
                    jobId, filterNote, filteredRows.Count, data.Rows.Count);
                data = data with { Rows = filteredRows };
            }

            // Stage 3 — load custom template (35 %)
            SetProgress(jobId, 35, "Loading template");
            IReadOnlyList<ClinicalAgent.Api.Data.TemplateSection>? customSections = null;
            if (!string.IsNullOrWhiteSpace(request.CustomTemplateId))
            {
                await using var tplDb = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
                var tpl = await tplDb.ReportTemplates.FindAsync([request.CustomTemplateId], CancellationToken.None);
                if (tpl is not null)
                {
                    var parsed = JsonSerializer.Deserialize<ClinicalAgent.Api.Data.TemplateSection[]>(
                        tpl.SectionsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                    customSections = parsed.Where(s => s.IsEnabled).OrderBy(s => s.Order).ToArray();
                    _logger.LogInformation("Custom template '{Name}' ({Count} sections) applied to job {JobId}",
                        tpl.Name, customSections.Count, jobId);
                }
            }

            // Stage 4 — charts + AI in parallel (40 → 65 %)
            // Charts are CPU-bound and fast; AI is the network bottleneck.
            // Starting both concurrently means charts are ready when AI returns.
            // A separate ticker task increments the progress bar while Groq is thinking
            // so the UI never stalls at a fixed percentage.
            SetProgress(jobId, 40, "Generating charts & running AI analysis");
            var chartsTask = Task.Run(() => ChartGenerator.GenerateCharts(data.Headers, data.Rows));
            var aiTask     = CallAiAsync(data, request, customSections, CancellationToken.None);

            using var tickCts = new CancellationTokenSource();
            var tickTask = Task.Run(async () =>
            {
                int pct = 42;
                while (!tickCts.Token.IsCancellationRequested && pct < 63)
                {
                    try { await Task.Delay(2500, tickCts.Token); } catch { break; }
                    pct = Math.Min(pct + 3, 63);
                    SetProgress(jobId, pct, "Analysing patient data with AI…");
                }
            }, tickCts.Token);

            AiReportContent aiContent;
            try
            {
                aiContent = await aiTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI analysis failed for job {JobId}", jobId);
                await FinishJobAsync(jobId, "Failed", data.Rows.Count, null,
                    $"AI report generation failed: {ex.Message}");
                return;
            }
            finally
            {
                await tickCts.CancelAsync();
            }

            var charts = await chartsTask; // almost certainly done while AI was running
            _logger.LogInformation("Generated {Count} chart(s) for job {JobId}", charts.Count, jobId);
            SetProgress(jobId, 65, "AI analysis complete");

            // Stage 5 — build output document (80 %)
            SetProgress(jobId, 80, "Building document");
            byte[] bytes;
            string contentType;
            try
            {
                (bytes, contentType) = BuildDocument(aiContent, data, request.OutputFormat,
                    filterNote, charts, customSections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Document build failed for job {JobId}", jobId);
                await FinishJobAsync(jobId, "Failed", data.Rows.Count, null,
                    $"Document generation failed: {ex.Message}");
                return;
            }

            // Stage 6 — upload to blob storage (95 %)
            SetProgress(jobId, 95, "Uploading document");
            using var docStream = new MemoryStream(bytes);
            await _storage.UploadAsync(docStream, BlobContainerNames.Generated,
                $"{jobId}.{ext}", contentType, CancellationToken.None);

            _logger.LogInformation("AI report {JobId} ({Format}, {Rows} rows) complete for user {UserId}",
                jobId, ext, data.Rows.Count, userId);
            await FinishJobAsync(jobId, "Completed", data.Rows.Count, downloadUrl, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in background job {JobId}", jobId);
            await FinishJobAsync(jobId, "Failed", 0, null, $"Unexpected error: {ex.Message}");
        }
    }

    private void SetProgress(string jobId, int percent, string stageName)
        => _jobs[jobId] = new JobStatusResult(jobId, ReportStatus.Processing, null, percent, null, stageName);

    private async Task FinishJobAsync(
        string jobId, string dbStatus, int rowCount, string? downloadUrl, string? error)
    {
        var rs = dbStatus == "Completed" ? ReportStatus.Completed : ReportStatus.Failed;
        _jobs[jobId] = new JobStatusResult(
            jobId, rs, downloadUrl,
            dbStatus == "Completed" ? 100 : null,
            error,
            dbStatus == "Completed" ? "Report ready" : "Failed");

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
            var job = await db.ReportJobs.FindAsync([jobId]);
            if (job is null) return;
            job.Status      = dbStatus;
            job.CompletedAt = DateTime.UtcNow;
            job.DownloadUrl = downloadUrl;
            job.RowCount    = rowCount;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist job completion for {JobId}", jobId);
        }
    }

    private static string BuildReportName(ReportRequest request)
    {
        var template = request.TemplateType switch
        {
            "patient-summary" => "Patient Summary",
            "outcome-data"    => "Outcome Data",
            "full-report"     => "Full Trial Report",
            _                 => "Clinical Report",
        };
        var trial = string.IsNullOrWhiteSpace(request.TrialId) ? "All Trials" : request.TrialId.Trim();
        return $"{template} – {trial} – {DateTime.UtcNow:dd MMM yyyy}";
    }

    internal static string BuildReportFileName(string reportName, string ext)
    {
        var slug = Regex.Replace(reportName, @"[^a-zA-Z0-9]+", "-").Trim('-');
        return $"{slug}.{ext}";
    }

    // ── Excel loading & merging ───────────────────────────────────────────────

    private async Task<ParsedData> DownloadAndMergeAsync(ReportRequest request, CancellationToken ct)
    {
        List<string>? headers = null;
        int trialColIdx = -1;
        var allRows = new List<IReadOnlyList<string>>();

        foreach (var blobName in request.BlobNames)
        {
            var fetchResult = await _storage.DownloadAsync(BlobContainerNames.Uploads, blobName, ct);
            if (!fetchResult.IsSuccess)
            {
                _logger.LogWarning("Blob {BlobName} not found, skipping: {Error}", blobName, fetchResult.Error);
                continue;
            }

            using var workbook = new XLWorkbook(fetchResult.Value);
            var ws = workbook.Worksheets.First();

            var fileHeaders = ws.Row(1).CellsUsed().Select(c => c.GetValue<string>()).ToList(); // headers are always text

            if (headers is null)
            {
                headers = fileHeaders;
                trialColIdx = headers.FindIndex(h =>
                    h.Contains("TrialID",  StringComparison.OrdinalIgnoreCase) ||
                    h.Contains("Trial_ID", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("TrialId",    StringComparison.OrdinalIgnoreCase));
            }

            // Pre-allocate one reusable column count so we don't re-query Count per row.
            int colCount = headers.Count;

            foreach (var row in ws.RowsUsed().Skip(1))
            {
                if (allRows.Count >= 2000) break;

                if (!string.IsNullOrWhiteSpace(request.TrialId) && trialColIdx >= 0)
                {
                    var v = row.Cell(trialColIdx + 1).GetValue<string>();
                    if (!v.Equals(request.TrialId, StringComparison.OrdinalIgnoreCase)) continue;
                }

                // Pre-allocated string array + for loop avoids per-row LINQ overhead.
                var rowData = new string[colCount];
                for (int c = 0; c < colCount; c++)
                    rowData[c] = GetCellString(row.Cell(c + 1));
                allRows.Add(rowData);
            }
        }

        if (headers is null || headers.Count == 0)
            throw new InvalidOperationException(
                "No data files could be loaded. Your uploaded files may have been cleared after an API restart — " +
                "please re-upload your Excel file(s) on the Upload Data page and try again.");

        return new ParsedData(request.TemplateType, headers, allRows, request.FreeTextPrompt);
    }

    // ── Smart filter: AI parsing + regex fallback ────────────────────────────

    /// <summary>
    /// Extracts filter conditions from the prompt using the LLM (handles any natural language),
    /// then applies them programmatically. Falls back to regex if the prompt is already handled
    /// by the fast regex parser or if the AI call fails.
    /// </summary>
    private async Task<(IReadOnlyList<IReadOnlyList<string>> Rows, string? FilterNote)> ParseAndApplyFiltersAsync(
        string? prompt,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        string jobId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return (rows, null);

        // Fast path: if the regex parser already handles this prompt, use it.
        var (regexRows, regexNote) = NaturalLanguageFilter.Apply(prompt, headers, rows);
        if (regexNote is not null)
            return (regexRows, regexNote);

        // Slow path: ask the LLM to understand the filter intent.
        // Key on column names + prompt so repeated requests with the same filter skip the LLM call.
        var cacheKey = $"{string.Join("|", headers)}§{prompt.Trim()}";
        try
        {
            AiFilterResult? parsed;
            if (!_aiFilterCache.TryGetValue(cacheKey, out parsed))
            {
                var chatService = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                history.AddSystemMessage("""
                    Extract every filter condition from the user's prompt and return ONLY a JSON object.
                    No markdown fences, no explanation — just the JSON.

                    Schema:
                    {
                      "filters": [
                        { "col": "<exact column name>", "op": "eq",      "val": "<text>" },
                        { "col": "<exact column name>", "op": "neq",     "val": "<text>" },
                        { "col": "<exact column name>", "op": "contains","val": "<text>" },
                        { "col": "<exact column name>", "op": "lt",      "num": 40 },
                        { "col": "<exact column name>", "op": "lte",     "num": 40 },
                        { "col": "<exact column name>", "op": "gt",      "num": 50 },
                        { "col": "<exact column name>", "op": "gte",     "num": 50 },
                        { "col": "<exact column name>", "op": "between", "low": 30, "high": 40 }
                      ]
                    }

                    Rules:
                    - "col" must be one of the available column names listed by the user.
                    - Use "eq" for exact text match (case-insensitive).
                    - Use "between" for any range (inclusive).
                    - Multiple conditions = multiple objects in the array; they are ANDed together.
                    - If no filter is expressed, return: {"filters":[]}
                    """);

                history.AddUserMessage(
                    $"Available columns: {string.Join(", ", headers)}\n\nUser filter prompt: {prompt}");

                var settings = new OpenAIPromptExecutionSettings { Temperature = 0, MaxTokens = 300 };
                var response  = await chatService.GetChatMessageContentAsync(
                    history, settings, kernel: _kernel, cancellationToken: ct);

                parsed = JsonSerializer.Deserialize<AiFilterResult>(
                    ExtractJson(response.Content ?? "{}"), JsonOpts);

                _aiFilterCache[cacheKey] = parsed;
                _logger.LogInformation("AI filter conditions parsed and cached for job {JobId}", jobId);
            }
            else
            {
                _logger.LogInformation("AI filter cache hit for job {JobId}", jobId);
            }

            if (parsed?.Filters is { Length: > 0 })
            {
                var result = ApplyAiFilters(parsed.Filters, headers, rows);
                if (result.FilterNote is not null)
                {
                    _logger.LogInformation(
                        "AI filter applied for job {JobId}: {Filter}", jobId, result.FilterNote);
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI filter parsing failed for job {JobId}, returning all rows", jobId);
        }

        return (rows, null);
    }

    /// <summary>Applies structured filter conditions returned by the LLM to the data rows.</summary>
    private static (IReadOnlyList<IReadOnlyList<string>> Rows, string? FilterNote) ApplyAiFilters(
        AiFilterCondition[] conditions,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var predicates   = new List<Func<IReadOnlyList<string>, bool>>();
        var descriptions = new List<string>();

        foreach (var cond in conditions)
        {
            // Fuzzy column lookup: exact name, then strip underscores/hyphens
            int colIdx = headers.Select((h, i) => (h, i))
                .Where(t =>
                    t.h.Equals(cond.Col, StringComparison.OrdinalIgnoreCase) ||
                    Normalize(t.h).Equals(Normalize(cond.Col), StringComparison.OrdinalIgnoreCase))
                .Select(t => (int?)t.i)
                .FirstOrDefault() ?? -1;

            if (colIdx < 0)
            {
                // Partial header match: header contains the col word or vice-versa
                colIdx = headers.Select((h, i) => (h, i))
                    .Where(t =>
                        t.h.Contains(cond.Col, StringComparison.OrdinalIgnoreCase) ||
                        cond.Col.Contains(t.h, StringComparison.OrdinalIgnoreCase))
                    .Select(t => (int?)t.i)
                    .FirstOrDefault() ?? -1;
            }

            if (colIdx < 0) continue;

            int i         = colIdx;
            string header = headers[i];
            var op        = cond.Op?.ToLowerInvariant() ?? string.Empty;

            switch (op)
            {
                case "eq":
                    predicates.Add(row => i < row.Count && string.Equals(row[i]?.Trim(), cond.Val, StringComparison.OrdinalIgnoreCase));
                    descriptions.Add($"{header} = '{cond.Val}'");
                    break;

                case "neq":
                    predicates.Add(row => i < row.Count && !string.Equals(row[i]?.Trim(), cond.Val, StringComparison.OrdinalIgnoreCase));
                    descriptions.Add($"{header} != '{cond.Val}'");
                    break;

                case "contains":
                    predicates.Add(row => i < row.Count && row[i]?.IndexOf(cond.Val ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
                    descriptions.Add($"{header} contains '{cond.Val}'");
                    break;

                case "lt":
                    predicates.Add(row => i < row.Count && TryParseNum(row[i], out var v) && v < cond.Num);
                    descriptions.Add($"{header} < {cond.Num}");
                    break;

                case "lte":
                    predicates.Add(row => i < row.Count && TryParseNum(row[i], out var v) && v <= cond.Num);
                    descriptions.Add($"{header} ≤ {cond.Num}");
                    break;

                case "gt":
                    predicates.Add(row => i < row.Count && TryParseNum(row[i], out var v) && v > cond.Num);
                    descriptions.Add($"{header} > {cond.Num}");
                    break;

                case "gte":
                    predicates.Add(row => i < row.Count && TryParseNum(row[i], out var v) && v >= cond.Num);
                    descriptions.Add($"{header} ≥ {cond.Num}");
                    break;

                case "between":
                    var lo = Math.Min(cond.Low, cond.High);
                    var hi = Math.Max(cond.Low, cond.High);
                    predicates.Add(row => i < row.Count && TryParseNum(row[i], out var v) && v >= lo && v <= hi);
                    descriptions.Add($"{header} between {lo} and {hi}");
                    break;
            }
        }

        if (predicates.Count == 0)
            return (rows, null);

        var filtered = rows.Where(row => predicates.All(p => p(row))).ToList();
        return (filtered, string.Join(" AND ", descriptions));
    }

    private static string Normalize(string s)
        => Regex.Replace(s, @"[_\-\s]", string.Empty);

    private static bool TryParseNum(string? s, out double value)
        => double.TryParse(s?.Trim(), System.Globalization.NumberStyles.Any,
               System.Globalization.CultureInfo.InvariantCulture, out value);

    // ── AI call ───────────────────────────────────────────────────────────────

    private async Task<AiReportContent> CallAiAsync(
        ParsedData data,
        ReportRequest request,
        IReadOnlyList<ClinicalAgent.Api.Data.TemplateSection>? customSections,
        CancellationToken ct)
    {
        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddSystemMessage(BuildSystemPrompt());
        history.AddUserMessage(BuildUserPrompt(data, request, customSections));

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.2,
            MaxTokens   = 2000,  // keeps total request under Groq free-tier 12k TPM limit
        };

        _logger.LogInformation(
            "Calling GPT-4o for {Template} report with {Rows} data rows",
            request.TemplateType, Math.Min(data.Rows.Count, MaxRowsToAi));

        var response = await chatService.GetChatMessageContentAsync(
            history, settings, kernel: _kernel, cancellationToken: ct);

        var json = ExtractJson(response.Content ?? string.Empty);
        var content = JsonSerializer.Deserialize<AiReportContent>(json, JsonOpts)
            ?? throw new InvalidOperationException("GPT-4o returned an empty or unparseable response.");

        return content;
    }

    private static string BuildSystemPrompt() => """
        You are a clinical data analyst AI specialised in clinical trial reporting.
        Analyse the patient data provided and produce a comprehensive, professional medical report.

        IMPORTANT — Return ONLY a single valid JSON object with EXACTLY these keys (no markdown fences, no extra text):
        {
          "reportTitle":           "<descriptive title including template and trial info>",
          "executiveSummary":      "<2-4 sentences: overall trial status, cohort size, primary outcome>",
          "keyFindings":           ["<finding 1>", "<finding 2>", "..."],
          "dataOverview":          "<data quality, column completeness, date ranges, truncation if any>",
          "patientAnalysis":       "<cohort demographics, treatment response patterns, subgroup observations>",
          "adverseEventsAnalysis": "<adverse event frequency, severity, patterns; or 'No AE data present' if absent>",
          "statisticalInsights":   "<key statistics: ranges, means, distributions, notable trends>",
          "recommendations":       "<clinical and operational recommendations based on the data>",
          "limitations":           "<data gaps, truncation notes, missing columns, confidence caveats>"
        }

        Rules:
        - Write in professional medical/scientific language.
        - Base every statement strictly on the data provided — do not invent values.
        - keyFindings must be a JSON array of 3–6 concise strings.
        - All other fields are plain strings (no nested objects, no lists).
        - If a field is not applicable, write a short explanatory sentence; never leave it blank.
        """;

    private static string BuildUserPrompt(
        ParsedData data,
        ReportRequest request,
        IReadOnlyList<ClinicalAgent.Api.Data.TemplateSection>? customSections = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"TEMPLATE TYPE : {data.TemplateType}");
        sb.AppendLine($"TRIAL ID      : {(string.IsNullOrWhiteSpace(request.TrialId) ? "All trials" : request.TrialId)}");
        sb.AppendLine($"DATE RANGE    : {FormatDateRange(request.DateRangeFrom, request.DateRangeTo)}");
        if (!string.IsNullOrWhiteSpace(data.UserPrompt))
            sb.AppendLine($"USER FOCUS    : {data.UserPrompt}");

        // Per-section AI instructions from the custom template.
        if (customSections is { Count: > 0 })
        {
            var withInstructions = customSections
                .Where(s => s.IsEnabled
                         && !string.IsNullOrWhiteSpace(s.AiInstruction)
                         && s.Type != "DataTable"
                         && s.Type != "Charts")
                .ToList();
            if (withInstructions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("CUSTOM SECTION FOCUS (apply these specific instructions to each section):");
                foreach (var sec in withInstructions)
                    sb.AppendLine($"  {sec.Heading}: {sec.AiInstruction}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"COLUMNS ({data.Headers.Count}): {string.Join(" | ", data.Headers)}");
        sb.AppendLine($"TOTAL RECORDS : {data.Rows.Count}{(data.Rows.Count >= 2000 ? " (truncated at 2000)" : "")}");

        var sampleRows = data.Rows.Take(MaxRowsToAi).ToList();
        sb.AppendLine($"\nDATA ({sampleRows.Count} rows{(data.Rows.Count > MaxRowsToAi ? $" of {data.Rows.Count} — first {MaxRowsToAi} shown" : "")}):");
        sb.AppendLine(string.Join(",", data.Headers));
        foreach (var row in sampleRows)
            sb.AppendLine(string.Join(",", row.Select(v => v.Contains(',') ? $"\"{v}\"" : v)));

        return sb.ToString();
    }

    /// <summary>Maps a section type key to the corresponding AI-generated content field.</summary>
    private static string? GetAiContent(AiReportContent ai, string sectionType) => sectionType switch
    {
        "ExecutiveSummary"    => ai.ExecutiveSummary,
        "DataOverview"        => ai.DataOverview,
        "PatientAnalysis"     => ai.PatientAnalysis,
        "AdverseEvents"       => ai.AdverseEventsAnalysis,
        "StatisticalInsights" => ai.StatisticalInsights,
        "Recommendations"     => ai.Recommendations,
        "Limitations"         => ai.Limitations,
        _                     => null,
    };

    private static string FormatDateRange(DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null) return "All dates";
        if (from is null) return $"up to {to:yyyy-MM-dd}";
        if (to is null)   return $"from {from:yyyy-MM-dd}";
        return $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}";
    }

    /// <summary>
    /// Returns the string representation of a cell value, normalising date-typed cells
    /// to "yyyy-MM-dd" so NaturalLanguageFilter can reliably parse them.
    /// </summary>
    private static string GetCellString(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime)
        {
            try { return cell.GetValue<DateTime>().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); }
            catch { }
        }
        return cell.GetValue<string>();
    }

    private static string ExtractJson(string raw)
    {
        // Strip markdown code fences if present: ```json ... ```
        var match = Regex.Match(raw, @"```(?:json)?\s*([\s\S]*?)\s*```");
        if (match.Success) return match.Groups[1].Value.Trim();

        // Find first { … last }
        var start = raw.IndexOf('{');
        var end   = raw.LastIndexOf('}');
        if (start >= 0 && end > start)
            return raw[start..(end + 1)];

        return raw.Trim();
    }

    // ── Document dispatch ─────────────────────────────────────────────────────

    private static (byte[] bytes, string contentType) BuildDocument(
        AiReportContent ai, ParsedData data, OutputFormat format, string? filterNote,
        IReadOnlyList<ChartSpec> charts,
        IReadOnlyList<ClinicalAgent.Api.Data.TemplateSection>? customSections = null)
        => format switch
        {
            OutputFormat.Excel => (BuildExcel(ai, data, filterNote, charts, customSections), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            OutputFormat.Pdf   => (BuildPdf(ai, data, filterNote, charts, customSections),   "application/pdf"),
            _                  => (BuildWord(ai, data, filterNote, charts, customSections),   "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        };

    // ── Word (AI-authored) ────────────────────────────────────────────────────

    private static byte[] BuildWord(AiReportContent ai, ParsedData data, string? filterNote,
        IReadOnlyList<ChartSpec> charts,
        IReadOnlyList<ClinicalAgent.Api.Data.TemplateSection>? customSections)
    {
        var sections = (customSections ?? DefaultSections)
            .Where(s => s.IsEnabled).OrderBy(s => s.Order).ToList();

        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new OxDocument(new Body());
            var body = main.Document.Body!;

            // Cover block
            body.Append(WordPara(ai.ReportTitle, bold: true, size: 36));
            body.Append(WordPara(
                $"Generated by Clinical Trial AI Agent  |  {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC  |  {data.Rows.Count} patient records",
                size: 18, color: "555555"));
            body.Append(new Paragraph());

            uint imgId = 1U;

            foreach (var sec in sections)
            {
                var heading = sec.Heading.ToUpperInvariant();

                switch (sec.Type)
                {
                    case "KeyFindings":
                        body.Append(WordPara(heading, bold: true, size: 26, color: "1e3a8a"));
                        foreach (var f in ai.KeyFindings)
                            body.Append(WordBullet(f));
                        body.Append(new Paragraph());
                        break;

                    case "Charts":
                        if (charts.Count > 0)
                        {
                            body.Append(WordPara(heading, bold: true, size: 26, color: "1e3a8a"));
                            body.Append(WordPara(
                                "The following charts are derived directly from the patient dataset.",
                                size: 18, color: "555555"));
                            body.Append(new Paragraph());
                            foreach (var chart in charts)
                            {
                                body.Append(WordPara(chart.Title, size: 20, color: "333333"));
                                ChartGenerator.AddToWordDocument(main, body, chart.PngBytes, chart.Title, imgId++);
                                body.Append(new Paragraph());
                            }
                        }
                        break;

                    case "DataTable":
                        body.Append(WordPara(heading, bold: true, size: 26, color: "1e3a8a"));
                        if (filterNote is not null)
                            body.Append(WordPara($"Filter applied: {filterNote} — {data.Rows.Count} row(s) shown.", size: 18, color: "b45309"));
                        if (data.Rows.Count > MaxDocRows)
                            body.Append(WordPara($"First {MaxDocRows} of {data.Rows.Count} rows shown.", size: 18, color: "888888"));
                        body.Append(BuildWordTable(data, MaxDocRows));
                        break;

                    default:
                        var content = GetAiContent(ai, sec.Type);
                        if (content is not null)
                        {
                            body.Append(WordPara(heading, bold: true, size: 26, color: "1e3a8a"));
                            body.Append(WordPara(content, size: 20));
                            body.Append(new Paragraph());
                        }
                        break;
                }
            }

            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static Table BuildWordTable(ParsedData data, int maxRows)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableWidth { Width = "9360", Type = TableWidthUnitValues.Dxa },
            new TableBorders(
                new TopBorder              { Val = BorderValues.Single, Size = 4 },
                new BottomBorder           { Val = BorderValues.Single, Size = 4 },
                new LeftBorder             { Val = BorderValues.Single, Size = 4 },
                new RightBorder            { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4 })));

        // Header row
        var hrow = new TableRow();
        foreach (var h in data.Headers)
        {
            var tc = new TableCell();
            tc.AppendChild(new TableCellProperties(
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "1e3a8a" }));
            tc.AppendChild(new Paragraph(new Run(
                new RunProperties(new Bold(), new OxColor { Val = "FFFFFF" }, new FontSize { Val = "16" }),
                new Text(h))));
            hrow.Append(tc);
        }
        table.Append(hrow);

        bool odd = true;
        foreach (var row in data.Rows.Take(maxRows))
        {
            var tr   = new TableRow();
            var fill = odd ? "FFFFFF" : "EFF6FF";
            foreach (var cell in row)
            {
                var tc = new TableCell();
                tc.AppendChild(new TableCellProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill }));
                tc.AppendChild(new Paragraph(new Run(
                    new RunProperties(new FontSize { Val = "16" }),
                    new Text(ChartGenerator.FormatCell(cell)))));
                tr.Append(tc);
            }
            table.Append(tr);
            odd = !odd;
        }
        return table;
    }

    private static Paragraph WordPara(string text, bool bold = false, int size = 20, string color = "000000")
    {
        var rp = new RunProperties();
        if (bold)        rp.AppendChild(new Bold());
        rp.AppendChild(new FontSize { Val = size.ToString() });
        if (color != "000000") rp.AppendChild(new OxColor { Val = color });
        return new Paragraph(new Run(rp, new Text(text)));
    }

    private static Paragraph WordBullet(string text)
    {
        var pp = new ParagraphProperties(new NumberingProperties(
            new NumberingLevelReference { Val = 0 },
            new NumberingId { Val = 1 }));
        return new Paragraph(pp, new Run(
            new RunProperties(new FontSize { Val = "20" }),
            new Text($"• {text}")));
    }

    // ── Excel (AI analysis + data) ────────────────────────────────────────────

    private static byte[] BuildExcel(AiReportContent ai, ParsedData data, string? filterNote,
        IReadOnlyList<ChartSpec> charts,
        IReadOnlyList<ClinicalAgent.Api.Data.TemplateSection>? customSections)
    {
        var sections = (customSections ?? DefaultSections)
            .Where(s => s.IsEnabled && s.Type != "DataTable" && s.Type != "Charts")
            .OrderBy(s => s.Order).ToList();

        using var wb = new XLWorkbook();

        // Sheet 1: AI Analysis
        var ws = wb.Worksheets.Add("AI Analysis");

        void WriteSection(ref int row, string heading, string content)
        {
            var hCell = ws.Cell(row, 1);
            hCell.Value = heading;
            hCell.Style.Font.Bold            = true;
            hCell.Style.Font.FontSize        = 13;
            hCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
            hCell.Style.Font.FontColor       = XLColor.White;
            ws.Range(row, 1, row, 4).Merge();
            row++;

            var cCell = ws.Cell(row, 1);
            cCell.Value = content;
            cCell.Style.Alignment.WrapText = true;
            ws.Range(row, 1, row, 4).Merge();
            row++;
            row++; // spacer
        }

        void WriteFindings(ref int row, string heading)
        {
            var hCell = ws.Cell(row, 1);
            hCell.Value = heading.ToUpperInvariant();
            hCell.Style.Font.Bold            = true;
            hCell.Style.Font.FontSize        = 13;
            hCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
            hCell.Style.Font.FontColor       = XLColor.White;
            ws.Range(row, 1, row, 4).Merge();
            row++;
            foreach (var f in ai.KeyFindings)
            {
                ws.Cell(row, 1).Value = $"• {f}";
                ws.Range(row, 1, row, 4).Merge();
                row++;
            }
            row++; // spacer
        }

        // Title
        ws.Cell(1, 1).Value = ai.ReportTitle;
        ws.Cell(1, 1).Style.Font.Bold     = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Range(1, 1, 1, 4).Merge();

        ws.Cell(2, 1).Value = $"Generated by Clinical Trial AI Agent  |  {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC  |  {data.Rows.Count} records";
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#555555");
        ws.Range(2, 1, 2, 4).Merge();

        int r = 4;
        foreach (var sec in sections)
        {
            if (sec.Type == "KeyFindings")
                WriteFindings(ref r, sec.Heading);
            else
            {
                var content = GetAiContent(ai, sec.Type);
                if (content is not null)
                    WriteSection(ref r, sec.Heading.ToUpperInvariant(), content);
            }
        }

        ws.Column(1).Width = 120;

        // Sheet 2: Patient Data
        var ds = wb.Worksheets.Add("Patient Data");

        int dsStartRow = 1;
        if (filterNote is not null)
        {
            var fnCell = ds.Cell(1, 1);
            fnCell.Value = $"Filter applied: {filterNote} — {data.Rows.Count} row(s) shown.";
            fnCell.Style.Font.Bold      = true;
            fnCell.Style.Font.FontColor = XLColor.FromHtml("#b45309");
            ds.Range(1, 1, 1, Math.Max(data.Headers.Count, 1)).Merge();
            dsStartRow = 2;
        }

        for (int c = 1; c <= data.Headers.Count; c++)
        {
            var cell = ds.Cell(dsStartRow, c);
            cell.Value = data.Headers[c - 1];
            cell.Style.Font.Bold            = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
            cell.Style.Font.FontColor       = XLColor.White;
        }

        for (int row = 0; row < data.Rows.Count; row++)
        {
            for (int col = 0; col < data.Rows[row].Count; col++)
                ds.Cell(dsStartRow + 1 + row, col + 1).Value = data.Rows[row][col];
            if (row % 2 == 1)
                ds.Row(dsStartRow + 1 + row).Style.Fill.BackgroundColor = XLColor.FromHtml("#eff6ff");
        }

        if (data.Headers.Count > 0 && data.Rows.Count > 0)
        {
            var tbl = ds.Range(dsStartRow, 1, dsStartRow + data.Rows.Count, data.Headers.Count).CreateTable();
            tbl.Theme = XLTableTheme.TableStyleMedium2;
        }

        ds.ColumnsUsed().AdjustToContents(8d, 50d);
        ds.SheetView.FreezeRows(1);

        // ── Charts sheet ──────────────────────────────────────────────────────
        if (charts.Count > 0)
        {
            var chartWs = wb.Worksheets.Add("Charts");
            int row = 1;
            foreach (var chart in charts)
            {
                chartWs.Cell(row, 1).Value = chart.Title;
                chartWs.Cell(row, 1).Style.Font.Bold     = true;
                chartWs.Cell(row, 1).Style.Font.FontSize = 13;
                row++;
                using var imgStream = new MemoryStream(chart.PngBytes);
                var pic = chartWs.AddPicture(imgStream);
                pic.MoveTo(chartWs.Cell(row, 1));
                pic.Width  = 480;
                pic.Height = 280;
                row += 22;
            }
            chartWs.Column(1).Width = 70;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── PDF (AI-authored) ─────────────────────────────────────────────────────

    private static byte[] BuildPdf(AiReportContent ai, ParsedData data, string? filterNote,
        IReadOnlyList<ChartSpec> charts,
        IReadOnlyList<ClinicalAgent.Api.Data.TemplateSection>? customSections)
    {
        var sections = (customSections ?? DefaultSections)
            .Where(s => s.IsEnabled).OrderBy(s => s.Order).ToList();

        var dataTableSection = sections.FirstOrDefault(s => s.Type == "DataTable");
        var dataTableHeading = dataTableSection?.Heading ?? "Appendix — Raw Patient Data";
        var showDataTable    = (dataTableSection is null || dataTableSection.IsEnabled) && data.Rows.Count > 0;
        var colWeights       = showDataTable ? ChartGenerator.CalcColWeights(data.Headers, data.Rows) : [];

        return QDocument.Create(container =>
        {
            // ── Content pages — A4 portrait ───────────────────────────────────
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(ai.ReportTitle)
                        .FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
                    col.Item().PaddingTop(4).Text(
                        $"Generated by Clinical Trial AI Agent  |  {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC  |  {data.Rows.Count} records")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Blue.Darken3);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    void PdfSection(string heading, string bodyText)
                    {
                        col.Item().PaddingTop(10).Text(heading)
                            .FontSize(12).Bold().FontColor(Colors.Blue.Darken3);
                        col.Item().PaddingTop(4).Text(bodyText).FontSize(10);
                    }

                    foreach (var sec in sections)
                    {
                        switch (sec.Type)
                        {
                            case "KeyFindings":
                                col.Item().PaddingTop(10).Text(sec.Heading)
                                    .FontSize(12).Bold().FontColor(Colors.Blue.Darken3);
                                foreach (var f in ai.KeyFindings)
                                    col.Item().PaddingLeft(10).Text($"• {f}").FontSize(10);
                                break;

                            case "Charts":
                                if (charts.Count > 0)
                                {
                                    col.Item().PaddingTop(12).Text(sec.Heading)
                                        .FontSize(12).Bold().FontColor(Colors.Blue.Darken3);
                                    col.Item().PaddingTop(6).Row(row =>
                                    {
                                        for (int i = 0; i < charts.Count; i++)
                                        {
                                            var chart = charts[i];
                                            row.RelativeItem().Column(c =>
                                            {
                                                c.Item().Text(chart.Title).FontSize(9).Bold();
                                                // Fixed height + FitArea avoids min-size conflicts
                                                c.Item().PaddingTop(3).Height(150)
                                                    .Image(chart.PngBytes).FitArea();
                                            });
                                            if (i < charts.Count - 1)
                                                row.ConstantItem(10); // spacer between charts only
                                        }
                                    });
                                    col.Item().PaddingTop(10).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                                }
                                break;

                            case "DataTable":
                                break; // rendered on separate landscape page below

                            default:
                                var content = GetAiContent(ai, sec.Type);
                                if (content is not null) PdfSection(sec.Heading, content);
                                break;
                        }
                    }
                });

                page.Footer().Row(r =>
                {
                    r.RelativeItem()
                        .Text($"Clinical Trial Agent POC  |  AI-Generated Report  |  {DateTime.UtcNow:yyyy-MM-dd}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    r.ConstantItem(80).AlignRight().Text(x =>
                    {
                        x.Span("Page ").FontSize(8);
                        x.CurrentPageNumber().FontSize(8);
                        x.Span(" of ").FontSize(8);
                        x.TotalPages().FontSize(8);
                    });
                });
            });

            // ── Data table pages — A4 landscape (avoids portrait column overflow) ──
            if (showDataTable)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Header().Column(col =>
                    {
                        col.Item().Text(dataTableHeading)
                            .FontSize(14).Bold().FontColor(Colors.Blue.Darken3);
                        if (filterNote is not null)
                            col.Item().PaddingTop(2)
                                .Text($"Filter applied: {filterNote} — {data.Rows.Count} row(s) shown.")
                                .FontSize(8).Bold().FontColor(Colors.Orange.Darken3);
                        if (data.Rows.Count > MaxPdfRows)
                            col.Item().Text($"First {MaxPdfRows} of {data.Rows.Count} rows shown.")
                                .FontSize(7).FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Blue.Darken3);
                    });

                    page.Content().PaddingTop(8).Table(tbl =>
                    {
                        // ConstantColumn gives QuestPDF an exact width per column so it can
                        // wrap text correctly. RelativeColumn still enforces unbreakable-token
                        // minimum widths which overflow when there are many columns.
                        // A4 landscape available ≈ 756.85pt (267mm). Use 750pt budget to stay
                        // safely under that limit regardless of column count.
                        var colWidthPt = (float)Math.Floor(750f / Math.Max(1, data.Headers.Count));
                        tbl.ColumnsDefinition(cd =>
                        {
                            foreach (var _ in data.Headers)
                                cd.ConstantColumn(colWidthPt);
                        });

                        tbl.Header(header =>
                        {
                            foreach (var h in data.Headers)
                                header.Cell()
                                    .Background(Colors.Blue.Darken3)
                                    .Padding(4)
                                    .Text(h).Bold().FontColor(Colors.White).FontSize(8);
                        });

                        bool odd = true;
                        foreach (var row in data.Rows.Take(MaxPdfRows))
                        {
                            var bg = odd ? Colors.White : Colors.Blue.Lighten5;
                            foreach (var cell in row)
                                tbl.Cell().Background(bg).Padding(3)
                                    .Text(ChartGenerator.FormatCell(cell)).FontSize(7);
                            odd = !odd;
                        }
                    });

                    page.Footer().Row(r =>
                    {
                        r.RelativeItem()
                            .Text($"Clinical Trial Agent POC  |  AI-Generated Report  |  {DateTime.UtcNow:yyyy-MM-dd}")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                        r.ConstantItem(80).AlignRight().Text(x =>
                        {
                            x.Span("Page ").FontSize(8);
                            x.CurrentPageNumber().FontSize(8);
                            x.Span(" of ").FontSize(8);
                            x.TotalPages().FontSize(8);
                        });
                    });
                });
            }
        }).GeneratePdf();
    }
}

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ClinicalAgent.Api.Data;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// Aliases to resolve name collisions between DocumentFormat.OpenXml.Wordprocessing and QuestPDF
using OxDocument = DocumentFormat.OpenXml.Wordprocessing.Document;
using OxColor    = DocumentFormat.OpenXml.Wordprocessing.Color;
using QDocument  = QuestPDF.Fluent.Document;

namespace ClinicalAgent.Api.Infrastructure;

internal sealed record ParsedData(
    string TemplateType,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? UserPrompt);

/// <summary>
/// Local (POC) report orchestrator. Downloads the uploaded Excel from the in-memory blob store,
/// parses it with ClosedXML, generates a real document (Word / PDF / Excel), stores it, and
/// returns a download URL. No Azure OpenAI or Service Bus required.
/// </summary>
internal sealed class LocalReportOrchestrator : IReportOrchestrator
{
    private readonly IBlobStorageService _storage;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<LocalReportOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, JobStatusResult> _jobs = new();

    static LocalReportOrchestrator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public LocalReportOrchestrator(
        IBlobStorageService storage,
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<LocalReportOrchestrator> logger)
    {
        _storage   = storage;
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    /// <summary>
    /// Queues a local (no-AI) report job and returns immediately.
    /// Progress is tracked via GetStatusAsync.
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

        _jobs[jobId] = new JobStatusResult(jobId, ReportStatus.Processing, null, 5, null, "Preparing…");

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

        _ = Task.Run(() => RunLocalJobAsync(jobId, ext, downloadUrl, request, userId));

        _logger.LogInformation("Local report job {JobId} queued for user {UserId}", jobId, userId);
        return Result<ReportSubmitResult>.Ok(new ReportSubmitResult(jobId, null, IsAsync: true));
    }

    /// <summary>Returns the current job status, with DB fallback for page-refresh resilience.</summary>
    public async Task<Result<JobStatusResult>> GetStatusAsync(string jobId, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(jobId, out var cached))
            return Result<JobStatusResult>.Ok(cached);

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

    private async Task RunLocalJobAsync(
        string jobId,
        string ext,
        string downloadUrl,
        ReportRequest request,
        string userId)
    {
        try
        {
            // Stage 1 — load, merge, filter (15 %)
            SetProgress(jobId, 15, "Loading data files");
            ParsedData data;
            try
            {
                data = await DownloadAndMergeAsync(request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load data for job {JobId}", jobId);
                await FinishLocalJobAsync(jobId, "Failed", 0, null, $"Could not load data: {ex.Message}");
                return;
            }

            SetProgress(jobId, 30, "Applying filters");
            var (filteredRows, filterNote) = NaturalLanguageFilter.Apply(
                request.FreeTextPrompt, data.Headers, data.Rows);
            if (filterNote is not null)
            {
                _logger.LogInformation("NL filter applied for job {JobId}: {Filter} → {Count}/{Total} rows",
                    jobId, filterNote, filteredRows.Count, data.Rows.Count);
                data = data with { Rows = filteredRows };
            }

            // Stage 2 — charts (50 %)
            SetProgress(jobId, 50, "Generating charts");
            var charts = ChartGenerator.GenerateCharts(data.Headers, data.Rows);
            _logger.LogInformation("Generated {Count} chart(s) for job {JobId}", charts.Count, jobId);

            // Stage 3 — build document (75 %)
            SetProgress(jobId, 75, "Building document");
            byte[] bytes;
            string contentType;
            try
            {
                (bytes, contentType) = BuildDocument(data, request.OutputFormat, charts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Document generation failed for job {JobId}", jobId);
                await FinishLocalJobAsync(jobId, "Failed", data.Rows.Count, null,
                    $"Document generation failed: {ex.Message}");
                return;
            }

            // Stage 4 — upload (92 %)
            SetProgress(jobId, 92, "Uploading document");
            using var docStream = new MemoryStream(bytes);
            await _storage.UploadAsync(docStream, BlobContainerNames.Generated,
                $"{jobId}.{ext}", contentType, CancellationToken.None);

            _logger.LogInformation("Report {JobId} ({Format}, {Rows} rows) complete for user {UserId}",
                jobId, ext, data.Rows.Count, userId);
            await FinishLocalJobAsync(jobId, "Completed", data.Rows.Count, downloadUrl, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in local job {JobId}", jobId);
            await FinishLocalJobAsync(jobId, "Failed", 0, null, $"Unexpected error: {ex.Message}");
        }
    }

    private void SetProgress(string jobId, int percent, string stageName)
        => _jobs[jobId] = new JobStatusResult(jobId, ReportStatus.Processing, null, percent, null, stageName);

    private async Task FinishLocalJobAsync(
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

            var fileHeaders = ws.Row(1).CellsUsed().Select(c => c.GetValue<string>()).ToList();

            if (headers is null)
            {
                headers = fileHeaders;
                trialColIdx = headers.FindIndex(h =>
                    h.Contains("TrialID",  StringComparison.OrdinalIgnoreCase) ||
                    h.Contains("Trial_ID", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("TrialId",    StringComparison.OrdinalIgnoreCase));
            }

            foreach (var row in ws.RowsUsed().Skip(1))
            {
                if (allRows.Count >= 2000) break;

                if (!string.IsNullOrWhiteSpace(request.TrialId) && trialColIdx >= 0)
                {
                    var v = row.Cell(trialColIdx + 1).GetValue<string>();
                    if (!v.Equals(request.TrialId, StringComparison.OrdinalIgnoreCase)) continue;
                }

                allRows.Add(Enumerable.Range(1, headers.Count)
                    .Select(c => GetCellString(row.Cell(c)))
                    .ToList());
            }
        }

        if (headers is null || headers.Count == 0)
            throw new InvalidOperationException(
                "No data files could be loaded. Your uploaded files may have been cleared after an API restart — " +
                "please re-upload your Excel file(s) on the Upload Data page and try again.");

        return new ParsedData(request.TemplateType, headers, allRows, request.FreeTextPrompt);
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

    // ── Document dispatch ─────────────────────────────────────────────────────

    private static (byte[] bytes, string contentType) BuildDocument(
        ParsedData data, OutputFormat format, IReadOnlyList<ChartSpec> charts)
        => format switch
        {
            OutputFormat.Excel => (BuildExcel(data, charts), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            OutputFormat.Pdf   => (BuildPdf(data, charts),   "application/pdf"),
            _                  => (BuildWord(data, charts),   "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        };

    // ── Excel generation (ClosedXML) ──────────────────────────────────────────

    private static byte[] BuildExcel(ParsedData data, IReadOnlyList<ChartSpec> charts)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Patient Report");

        // Header row
        for (int c = 1; c <= data.Headers.Count; c++)
        {
            var cell = ws.Cell(1, c);
            cell.Value = data.Headers[c - 1];
            cell.Style.Font.Bold                = true;
            cell.Style.Fill.BackgroundColor     = XLColor.FromHtml("#1e40af");
            cell.Style.Font.FontColor           = XLColor.White;
            cell.Style.Alignment.Horizontal     = XLAlignmentHorizontalValues.Center;
        }

        // Data rows
        for (int r = 0; r < data.Rows.Count; r++)
        {
            for (int c = 0; c < data.Rows[r].Count; c++)
                ws.Cell(r + 2, c + 1).Value = data.Rows[r][c];

            if (r % 2 == 1)
                ws.Row(r + 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#eff6ff");
        }

        if (data.Headers.Count > 0 && data.Rows.Count > 0)
        {
            var tbl = ws.Range(1, 1, data.Rows.Count + 1, data.Headers.Count).CreateTable();
            tbl.Theme = XLTableTheme.TableStyleMedium2;
        }

        ws.ColumnsUsed().AdjustToContents(8d, 60d);
        ws.SheetView.FreezeRows(1);

        // Summary sheet
        var sum = wb.Worksheets.Add("Summary");
        sum.Cell("A1").Value = "Clinical Trial Report Summary";
        sum.Cell("A1").Style.Font.Bold     = true;
        sum.Cell("A1").Style.Font.FontSize = 14;
        sum.Cell("A2").Value = "Template Type";   sum.Cell("B2").Value = data.TemplateType;
        sum.Cell("A3").Value = "Total Rows";      sum.Cell("B3").Value = data.Rows.Count;
        sum.Cell("A4").Value = "Columns";         sum.Cell("B4").Value = data.Headers.Count;
        sum.Cell("A5").Value = "Generated (UTC)"; sum.Cell("B5").Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
        if (!string.IsNullOrWhiteSpace(data.UserPrompt))
        {
            sum.Cell("A6").Value = "Notes";
            sum.Cell("B6").Value = data.UserPrompt;
        }
        sum.ColumnsUsed().AdjustToContents();

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

    // ── Word generation (DocumentFormat.OpenXml) ──────────────────────────────

    private static byte[] BuildWord(ParsedData data, IReadOnlyList<ChartSpec> charts)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new OxDocument(new Body());
            var body = mainPart.Document.Body!;

            body.Append(MakeParagraph(
                $"Clinical Trial Patient Report — {data.TemplateType}",
                bold: true, fontSize: 32));
            body.Append(MakeParagraph(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC  |  Rows: {data.Rows.Count}  |  Columns: {data.Headers.Count}",
                fontSize: 18, color: "555555"));

            if (!string.IsNullOrWhiteSpace(data.UserPrompt))
                body.Append(MakeParagraph($"Notes: {data.UserPrompt}", fontSize: 18, color: "444444"));

            body.Append(new Paragraph());

            // ── Charts section ────────────────────────────────────────────────
            if (charts.Count > 0)
            {
                body.Append(MakeParagraph("DATA VISUALISATIONS", bold: true, fontSize: 24, color: "1e3a8a"));
                uint imgId = 1U;
                foreach (var chart in charts)
                {
                    body.Append(MakeParagraph(chart.Title, fontSize: 20, color: "333333"));
                    ChartGenerator.AddToWordDocument(mainPart, body, chart.PngBytes, chart.Title, imgId++);
                    body.Append(new Paragraph());
                }
            }

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
            var headerRow = new TableRow();
            foreach (var h in data.Headers)
            {
                var tc = new TableCell();
                tc.AppendChild(new TableCellProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "1e3a8a" }));
                tc.AppendChild(new Paragraph(new Run(
                    new RunProperties(new Bold(), new OxColor { Val = "FFFFFF" }, new FontSize { Val = "16" }),
                    new Text(h))));
                headerRow.Append(tc);
            }
            table.Append(headerRow);

            // Data rows (cap at 500 for Word readability)
            bool odd = true;
            foreach (var row in data.Rows.Take(500))
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

            body.Append(table);

            if (data.Rows.Count > 500)
                body.Append(MakeParagraph(
                    $"Note: {data.Rows.Count - 500} additional rows omitted. Download as Excel for the full dataset.",
                    fontSize: 16, color: "888888"));

            mainPart.Document.Save();
        }
        return ms.ToArray();
    }

    private static Paragraph MakeParagraph(string text, bool bold = false, int fontSize = 20, string color = "000000")
    {
        var rp = new RunProperties();
        if (bold)           rp.AppendChild(new Bold());
        if (fontSize != 20) rp.AppendChild(new FontSize { Val = fontSize.ToString() });
        if (color != "000000") rp.AppendChild(new OxColor { Val = color });
        return new Paragraph(new Run(rp, new Text(text)));
    }

    // ── PDF generation (QuestPDF) ─────────────────────────────────────────────

    private static byte[] BuildPdf(ParsedData data, IReadOnlyList<ChartSpec> charts)
    {
        return QDocument.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text($"Clinical Trial Patient Report — {data.TemplateType}")
                        .FontSize(16).Bold().FontColor(Colors.Blue.Darken3);
                    col.Item().PaddingTop(3).Text(
                        $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC  |  {data.Rows.Count} rows  |  {data.Headers.Count} columns")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(data.UserPrompt))
                        col.Item().PaddingTop(2).Text($"Notes: {data.UserPrompt}")
                            .FontSize(8).Italic().FontColor(Colors.Grey.Darken2);
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Blue.Darken3);
                });

                page.Content().PaddingTop(8).Column(content =>
                {
                    // ── Charts ────────────────────────────────────────────────
                    if (charts.Count > 0)
                    {
                        content.Item().Text("Data Visualisations")
                            .FontSize(13).Bold().FontColor(Colors.Blue.Darken3);
                        content.Item().PaddingTop(6).Row(row =>
                        {
                            for (int ci = 0; ci < charts.Count; ci++)
                            {
                                var chart = charts[ci];
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Text(chart.Title).FontSize(9).Bold();
                                    col.Item().PaddingTop(3).Height(150)
                                        .Image(chart.PngBytes).FitArea();
                                });
                                if (ci < charts.Count - 1)
                                    row.ConstantItem(10); // gap between charts only
                            }
                        });
                        content.Item().PaddingTop(12).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                        content.Item().PaddingBottom(8);
                    }

                    content.Item().Table(tbl =>
                    {
                        tbl.ColumnsDefinition(cd =>
                        {
                            // A4 landscape available ≈ 756.85pt. Use 750pt budget to stay
                            // safely under that limit regardless of column count.
                            var colWidth = (float)Math.Floor(750f / Math.Max(1, data.Headers.Count));
                            foreach (var _ in data.Headers)
                                cd.ConstantColumn(colWidth);
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
                        foreach (var row in data.Rows.Take(1000))
                        {
                            var bg = odd ? Colors.White : Colors.Blue.Lighten5;
                            foreach (var cell in row)
                                tbl.Cell().Background(bg).Padding(3)
                                    .Text(ChartGenerator.FormatCell(cell)).FontSize(8);
                            odd = !odd;
                        }
                    });  // close Table
                });  // close Column

                page.Footer().Row(r =>
                {
                    r.RelativeItem()
                        .Text($"Clinical Trial Agent POC  |  {DateTime.UtcNow:yyyy-MM-dd}")
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
        }).GeneratePdf();
    }
}

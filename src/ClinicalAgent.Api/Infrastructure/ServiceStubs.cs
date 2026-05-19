namespace ClinicalAgent.Api.Infrastructure;

// Temporary no-op implementations registered until ClinicalAgent.Plugins provides real ones.

/// <summary>
/// File-system backed blob store for local dev. Blobs are persisted in a .blobstore/ folder
/// next to the database so uploads survive API restarts without requiring Azurite.
/// </summary>
internal sealed class StubBlobStorageService : IBlobStorageService
{
    private readonly string _root;

    public StubBlobStorageService(IConfiguration configuration)
    {
        // Store blobs alongside the SQLite database so both survive restarts.
        var dbPath = configuration["Database:Path"] ?? "clinicalagent.db";
        var dbDir  = Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? Directory.GetCurrentDirectory();
        _root = Path.Combine(dbDir, ".blobstore");
        Directory.CreateDirectory(_root);
    }

    public async Task<Result<string>> UploadAsync(Stream content, string containerName, string blobPath, string contentType, CancellationToken ct = default)
    {
        var filePath = GetFilePath(containerName, blobPath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(fs, ct);
        return Result<string>.Ok(blobPath);
    }

    public Task<Result<Stream>> DownloadAsync(string containerName, string blobPath, CancellationToken ct = default)
    {
        var filePath = GetFilePath(containerName, blobPath);
        if (!File.Exists(filePath))
            return Task.FromResult(Result<Stream>.Fail($"Blob not found: {containerName}/{blobPath}"));
        Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(Result<Stream>.Ok(stream));
    }

    public Task<Result<Uri>> GenerateSasUrlAsync(string containerName, string blobPath, TimeSpan expiry, CancellationToken ct = default)
    {
        var fakeUrl = $"https://stub-storage.local/{containerName}/{blobPath}?sv=stub&se={DateTime.UtcNow.Add(expiry):O}";
        return Task.FromResult(Result<Uri>.Ok(new Uri(fakeUrl)));
    }

    public Task<IReadOnlyList<string>> ListBlobsAsync(string containerName, string prefix, CancellationToken ct = default)
    {
        var containerDir = Path.Combine(_root, Sanitise(containerName));
        if (!Directory.Exists(containerDir))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var results = Directory.EnumerateFiles(containerDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(containerDir, f).Replace('\\', '/'))
            .Where(rel => rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(results);
    }

    private string GetFilePath(string containerName, string blobPath)
        => Path.Combine(_root, Sanitise(containerName), blobPath.Replace('/', Path.DirectorySeparatorChar));

    private static string Sanitise(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

internal sealed class StubServiceBusPublisher : IServiceBusPublisher
{
    public Task<Result<string>> PublishReportJobAsync(ReportJobMessage message, CancellationToken ct = default)
        => Task.FromResult(Result<string>.Fail("ServiceBusPublisher not yet implemented."));
}

// Builds minimal valid OOXML/PDF bytes at runtime — avoids fragile hardcoded base64.
internal static class StubFileContent
{
    public static readonly byte[] Docx = BuildDocx();
    public static readonly byte[] Xlsx = BuildXlsx();
    public static readonly byte[] Pdf  = BuildPdf();

    private static byte[] BuildOoxml(IEnumerable<(string path, string xml)> entries)
    {
        using var ms  = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, xml) in entries)
            {
                var entry = zip.CreateEntry(path, System.IO.Compression.CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), System.Text.Encoding.UTF8, leaveOpen: false);
                writer.Write(xml);
            }
        }
        return ms.ToArray();
    }

    private static byte[] BuildDocx() => BuildOoxml(
    [
        ("[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>"""),
        ("_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>"""),
        ("word/document.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Clinical Trial Agent - Stub Report (POC)</w:t></w:r></w:p><w:sectPr/></w:body></w:document>"""),
        ("word/_rels/document.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>"""),
    ]);

    private static byte[] BuildXlsx() => BuildOoxml(
    [
        ("[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>"""),
        ("_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>"""),
        ("xl/workbook.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Report" sheetId="1" r:id="rId1"/></sheets></workbook>"""),
        ("xl/_rels/workbook.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>"""),
        ("xl/worksheets/sheet1.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Clinical Trial Agent - Stub Report (POC)</t></is></c></row></sheetData></worksheet>"""),
    ]);

    private static byte[] BuildPdf() => System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n" +
        "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
        "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
        "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792]\n" +
        "   /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n" +
        "4 0 obj\n<< /Length 44 >>\nstream\nBT /F1 12 Tf 100 700 Td (Stub Report - POC) Tj ET\nendstream\nendobj\n" +
        "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n" +
        "xref\n0 6\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \n" +
        "0000000115 00000 n \n0000000266 00000 n \n0000000360 00000 n \n" +
        "trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n441\n%%EOF");
}

internal sealed class StubTemplateRepository : ITemplateRepository
{
    private static readonly TemplateInfo[] _templates =
    [
        new(TemplateTypes.PatientSummary, "Patient Summary",    "One-page per-patient summary report.",         [OutputFormat.Docx, OutputFormat.Pdf]),
        new(TemplateTypes.OutcomeData,    "Outcome Data",       "Aggregated trial outcome statistics.",         [OutputFormat.Docx, OutputFormat.Pdf, OutputFormat.Excel]),
        new(TemplateTypes.FullReport,     "Full Trial Report",  "Complete study report with all patient data.", [OutputFormat.Docx, OutputFormat.Pdf]),
    ];

    public Task<IReadOnlyList<TemplateInfo>> ListTemplatesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TemplateInfo>>(_templates);
}

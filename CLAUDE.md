# Clinical Trial Agent — POC
## Master context for Claude Code — read this before every session

---

## Project Overview

A POC AI agent on Microsoft Azure that:
1. Accepts uploaded Excel files containing clinical trial patient data
2. Parses and interprets the data using an AI agent (Semantic Kernel + GPT-4o)
3. Generates Patient Data Reports as Word (.docx), PDF, or Excel (.xlsx)
   using pre-defined templates stored in Azure Blob Storage
4. Delivers the report as a secure download link (Azure Blob SAS URL)

**Frontend:** React + TypeScript (Azure Static Web Apps)
**Backend:** .NET 8 ASP.NET Core Web API (Azure Container Apps)
**Agent framework:** Microsoft Semantic Kernel (C#) — NOT LangChain, NOT Python
**LLM:** Azure OpenAI GPT-4o
**Auth:** Azure Entra ID (Azure AD) via Microsoft.Identity.Web + MSAL.js
**CI/CD:** GitHub Actions → Azure (dev auto-deploy, prod requires approval)

---

## Folder Structure

```
/src
  /ClinicalAgent.Api              → ASP.NET Core Web API (agent host)
  /ClinicalAgent.Plugins          → Semantic Kernel KernelFunction plugins
  /ClinicalAgent.Functions        → Azure Functions (document generation)
  /ClinicalAgent.Core             → Shared models, interfaces, constants
/frontend                         → React + TypeScript (Vite)
/infra                            → Bicep templates (Azure infrastructure)
/tests
  /ClinicalAgent.Plugins.Tests    → xUnit unit tests for plugins
  /ClinicalAgent.Api.Tests        → xUnit integration tests for API
  /ClinicalAgent.E2E              → Playwright end-to-end tests
/.github/workflows                → GitHub Actions CI/CD pipelines
docker-compose.yml                → Local dev: Azurite + Service Bus + SQL
CLAUDE.md                         → This file
```

---

## Tech Stack — Exact Packages

### Backend (.NET 8)
```xml
<!-- Semantic Kernel -->
<PackageReference Include="Microsoft.SemanticKernel" Version="1.*" />
<PackageReference Include="Microsoft.SemanticKernel.Connectors.AzureOpenAI" Version="1.*" />

<!-- Azure SDKs — always use DefaultAzureCredential, never connection strings in prod -->
<PackageReference Include="Azure.Storage.Blobs" Version="12.*" />
<PackageReference Include="Azure.Messaging.ServiceBus" Version="7.*" />
<PackageReference Include="Azure.Identity" Version="1.*" />
<PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.*" />

<!-- Excel parsing -->
<PackageReference Include="ClosedXML" Version="0.102.*" />

<!-- Document generation -->
<PackageReference Include="DocumentFormat.OpenXml" Version="3.*" />
<PackageReference Include="QuestPDF" Version="2024.*" />

<!-- Auth -->
<PackageReference Include="Microsoft.Identity.Web" Version="3.*" />

<!-- Resilience -->
<PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="8.*" />

<!-- Logging -->
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />
<PackageReference Include="Serilog.Sinks.ApplicationInsights" Version="4.*" />

<!-- Testing -->
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="Moq" Version="4.*" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.*" />
<PackageReference Include="FluentAssertions" Version="6.*" />
```

### Frontend (React)
```json
{
  "dependencies": {
    "@azure/msal-browser": "^3.*",
    "@azure/msal-react": "^2.*",
    "axios": "^1.*",
    "react-hook-form": "^7.*",
    "react-dropzone": "^14.*",
    "react-router-dom": "^6.*"
  },
  "devDependencies": {
    "typescript": "^5.*",
    "vite": "^5.*",
    "tailwindcss": "^3.*",
    "@playwright/test": "^1.*",
    "vitest": "^1.*"
  }
}
```

---

## Azure Services & Local Equivalents

| Service | Purpose | Local substitute |
|---|---|---|
| Azure Blob Storage | Uploads, templates, output docs | Azurite (localhost:10000) |
| Azure Service Bus | Async report job queue | Service Bus Emulator (localhost:5672) |
| Azure SQL / Cosmos DB | Trial metadata (Phase 2) | SQL Server Docker (localhost:1433) |
| Azure OpenAI (GPT-4o) | LLM inference | Real dev-tier endpoint (no local sub) |
| Azure Container Apps | API host | dotnet run (localhost:7001) |
| Azure Functions | Document generation | func start (localhost:7071) |
| Azure Static Web Apps | React host | npm run dev (localhost:5173) |
| Azure API Management | Gateway, JWT, rate limiting | Bypassed locally |
| Azure Key Vault | Secrets | appsettings.Development.json |

### Local connection strings (appsettings.Development.json only)
```json
{
  "Storage:ConnectionString": "UseDevelopmentStorage=true",
  "ServiceBus:ConnectionString": "Endpoint=sb://localhost;...",
  "SqlServer:ConnectionString": "Server=localhost,1433;Database=ClinicalTrialDev;..."
}
```

**Never put real secrets in appsettings.json or any file committed to Git.**
In production, all config comes from Azure App Configuration with Key Vault references.

---

## Coding Rules — Follow These Exactly

### General
- **C# only for backend.** Never suggest Python, Node.js, or any other language for backend code.
- **No hardcoded secrets, keys, connection strings, or endpoints** anywhere in code.
- All configuration via `IConfiguration` / `IOptions<T>` pattern, values from environment variables or Azure App Configuration.
- Every public method must have XML doc comments (`/// <summary>`).
- Use `CancellationToken` on every async method signature.
- Use `record` types for immutable DTOs and result objects.
- Use `Result<T>` pattern (or `OneOf`) for error handling — no throwing exceptions for expected failures.

### Semantic Kernel plugins
- Every plugin class must be decorated with `[Description("...")]` — clear description of what it does.
- Every `[KernelFunction]` must have a `[Description("...")]` on itself and on every parameter.
- Never call Azure services directly from a plugin — inject interfaces, use DI.
- Plugin method names must be PascalCase verbs: `ParseExcelFile`, `FetchTemplate`, `GenerateReport`.
- Always return a typed record — never return raw `string` or `object` from a plugin.

```csharp
// CORRECT plugin pattern
[Description("Parses an uploaded Excel file and extracts patient data rows")]
public class ExcelParserPlugin
{
    private readonly IBlobStorageService _storage;

    public ExcelParserPlugin(IBlobStorageService storage) => _storage = storage;

    [KernelFunction, Description("Parse an Excel file from blob storage and return structured patient data")]
    public async Task<ParsedExcelResult> ParseExcelFile(
        [Description("Name of the blob in the uploads container")] string blobName,
        CancellationToken cancellationToken = default)
    {
        // implementation
    }
}
```

### Azure SDK usage
- Always use `DefaultAzureCredential` in production code paths.
- Use `UseDevelopmentStorage=true` only via configuration — never hardcoded.
- Never use `BlobServiceClient` with a SAS URL as a constructor argument directly — use `BlobServiceClient(Uri, DefaultAzureCredential)`.

```csharp
// CORRECT
var client = new BlobServiceClient(
    new Uri(config["Storage:ServiceUri"]),
    new DefaultAzureCredential());

// WRONG — never do this
var client = new BlobServiceClient("DefaultEndpointsProtocol=https;AccountName=...");
```

### Error handling
- Use `ILogger<T>` for all logging — never `Console.WriteLine`.
- Log with structured properties, not string interpolation:
```csharp
// CORRECT
_logger.LogInformation("Parsed Excel file {BlobName} with {RowCount} rows", blobName, result.RowCount);

// WRONG
_logger.LogInformation($"Parsed {blobName} with {result.RowCount} rows");
```
- Every controller action must catch exceptions and return `ProblemDetails` — never expose stack traces.
- Polly retry policy on all outbound HTTP calls: 3 retries, exponential backoff (2s, 4s, 8s).

### Document generation rules
- Word templates use `{{PLACEHOLDER_NAME}}` tokens — uppercase, underscores, double braces.
- Excel templates use Named Ranges — `PatientTable`, `SummaryStats`, `ReportHeader`.
- Never use `Interop` libraries — Open XML SDK only for Word, ClosedXML only for Excel.
- QuestPDF for PDF conversion — do not shell out to LibreOffice or any external process.
- Template path convention: `clinical-templates/{reportType}/v{version}/template.{ext}`

### Frontend rules
- No component libraries (no MUI, no Ant Design, no Chakra) — Tailwind CSS utility classes only.
- No `any` TypeScript — every variable, prop, and return type must be explicitly typed.
- All API calls via a central `apiClient.ts` (axios instance with interceptor that attaches bearer token).
- MSAL token acquired silently first; interactive login only as fallback.
- Never store tokens in `localStorage` — MSAL handles token cache internally.
- Form validation with `react-hook-form` — no manual state management for forms.
- All environment variables prefixed `VITE_` and read from `.env.local` (not committed to Git).

---

## Blob Storage Container Layout

```
Storage account: clinicaltrialpoc{env}
│
├── uploads/                    ← Excel files uploaded by users
│   └── {userId}/{guid}.xlsx
│
├── clinical-templates/         ← Report templates (read-only by agent)
│   ├── patient-summary/
│   │   └── v1/template.docx
│   │   └── v1/template.xlsx
│   └── outcome-data/
│       └── v1/template.docx
│
└── generated-docs/             ← Completed reports (7-day lifecycle policy)
    └── {jobId}.{docx|pdf|xlsx}
```

---

## Report Request Flow

```
1. User uploads Excel → POST /api/uploads → blob saved to uploads/{userId}/{guid}.xlsx
2. User submits form  → POST /api/reports → ReportRequest { blobName, templateType, outputFormat, prompt }
3. API decides mode:
   - rows < 500  AND simple template → SYNC  → respond within 30s
   - rows >= 500 OR complex template → ASYNC → enqueue to Service Bus, return { jobId }
4. Agent orchestrator:
   a. ExcelParserPlugin.ParseExcelFile(blobName)
   b. TemplateFetchPlugin.FetchTemplate(templateType)
   c. GPT-4o: system = template schema, user = parsed data JSON → returns ReportContent JSON
5. DocumentGeneratorFunction:
   a. Fill template placeholders with ReportContent
   b. Convert to requested format (docx / pdf / xlsx)
   c. Upload to generated-docs/{jobId}.{ext}
   d. Generate SAS URL (1-hour expiry)
6. Return { downloadUrl, jobId, format, generatedAt }
```

---

## API Endpoints

```
POST   /api/uploads                    Upload Excel file → { blobName }
POST   /api/reports                    Submit report request → { jobId, downloadUrl? }
GET    /api/reports/{jobId}/status     Poll async job → { status, downloadUrl?, progress }
GET    /api/templates                  List available report templates
GET    /health                         Health check (no auth required)
```

### ReportRequest model
```csharp
public record ReportRequest(
    string BlobName,           // from upload step
    string TemplateType,       // "patient-summary" | "outcome-data" | "full-report"
    OutputFormat OutputFormat, // Docx | Pdf | Excel
    string? FreeTextPrompt,    // optional user prompt
    DateOnly? DateRangeFrom,
    DateOnly? DateRangeTo,
    string? TrialId
);

public enum OutputFormat { Docx, Pdf, Excel }
```

---

## Excel Parsing Rules

- Parse using ClosedXML — open as stream from Blob Storage.
- Read first worksheet only (unless sheet name specified in request).
- Row 1 is always the header row.
- Maximum 2,000 rows — if more, set `IsTruncated = true`, parse first 2,000 only.
- For each column, detect type: `Text | Numeric | Date | Boolean | Mixed`.
- Count null/empty cells per column.
- Compute summary stats for numeric columns: min, max, mean, nullCount.
- Flag columns whose name contains: PatientID, SubjectID, TrialID, VisitDate,
  AE, AdverseEvent, Score, Result, Outcome — these are "known clinical columns".
- Return `ParsedExcelResult` record (see Core models).

---

## Document Generation Rules

### Word (.docx) — Open XML SDK
- Open template as `WordprocessingDocument` from stream (do not write to disk).
- Walk all `Text` elements in Body, Headers, Footers, and TableCells.
- Replace `{{TOKEN}}` with value from `ReportContent` dictionary.
- If token not found in ReportContent, replace with `[DATA NOT AVAILABLE]`.
- Save to `MemoryStream` and return bytes.

### PDF — QuestPDF
- Generate Word first, then convert via QuestPDF fluent API.
- Do not shell out to LibreOffice or any external process.

### Excel (.xlsx) — ClosedXML
- Open template as `XLWorkbook` from stream.
- Find Named Ranges: `PatientTable`, `SummaryStats`, `ReportHeader`.
- Write `ReportContent.PatientRows` into `PatientTable` range (expand rows as needed).
- Write summary stats into `SummaryStats` range.
- Write header values into `ReportHeader` cells.
- Save to `MemoryStream` and return bytes.

---

## Test Requirements

### Every plugin must have unit tests that cover:
- Happy path with a real fixture file
- Empty file / zero rows
- File with missing "known clinical columns"
- File exceeding 2,000 rows (truncation behaviour)
- Blob not found (storage exception handling)

### Every API endpoint must have integration tests that cover:
- Valid request → 200 with expected response shape
- Missing auth token → 401
- Invalid file type upload → 400 with `ProblemDetails`
- Job not found → 404

### Fixture files location: `/tests/Fixtures/`
- `sample-patient-data.xlsx` — 50 rows, all known clinical columns present
- `large-patient-data.xlsx` — 2,100 rows (tests truncation)
- `missing-columns.xlsx` — no PatientID or TrialID columns
- `patient-summary-template.docx` — Word template with placeholders
- `patient-summary-template.xlsx` — Excel template with named ranges

---

## Environment Variables Reference

### Container App (set in Azure App Configuration)
```
AzureOpenAI__Endpoint
AzureOpenAI__DeploymentName          (= gpt-4o)
Storage__ServiceUri                  (= https://{account}.blob.core.windows.net)
ServiceBus__FullyQualifiedNamespace  (= {namespace}.servicebus.windows.net)
ApplicationInsights__ConnectionString
```

### Azure Functions (same App Configuration)
```
Storage__ServiceUri
ApplicationInsights__ConnectionString
ReportJobs__QueueName                (= report-requests)
GeneratedDocs__ContainerName         (= generated-docs)
SasUrl__ExpiryHours                  (= 1)
```

### React (.env.local — never committed to Git)
```
VITE_AZURE_CLIENT_ID="69ec6f2f-a1ce-4f98-93b0-ec20e3c6a0b3"
VITE_AZURE_TENANT_ID="073de3bd-897f-43f4-94d0-27460ef0a774"
VITE_API_BASE_URL=http://localhost:7001
```

---

## GitHub Actions Workflows

Four workflow files in `.github/workflows/`:

| File | Trigger | What it does |
|---|---|---|
| `backend-ci-cd.yml` | push to main, paths: src/** | dotnet build → test → docker push ACR → deploy Container App |
| `functions-deploy.yml` | push to main, paths: src/ClinicalAgent.Functions/** | dotnet publish → deploy Azure Functions |
| `frontend-deploy.yml` | push to main, paths: frontend/** | npm build → deploy Static Web Apps |
| `infra-deploy.yml` | manual workflow_dispatch | az deployment group create with /infra/main.bicep |

- Use OIDC federated credentials — no stored Azure credentials in GitHub secrets.
- `environment: dev` for automatic deploys.
- `environment: prod` with `required-reviewers` gate for production.

---

## Local Development Checklist

Before starting a new session, confirm:
- [ ] `docker compose up -d` — Azurite, Service Bus Emulator, SQL Server running
- [ ] `appsettings.Development.json` has real Azure OpenAI endpoint + key
- [ ] `frontend/.env.local` has correct VITE_ values
- [ ] `dotnet restore` completed
- [ ] `npm install` in /frontend completed

Run all tests before pushing:
```bash
dotnet test                          # unit + integration
cd frontend && npm test              # React unit tests
npx playwright test                  # E2E tests
```

---

## Clinical Trial Data — Important Notes

- **POC environment uses SYNTHETIC / ANONYMISED data only.**
- Never process real patient data (PHI/PII) in the POC environment.
- Excel files are deleted from Blob Storage after report generation or within 24 hours.
- Generated reports are retained for 7 days then auto-deleted via Blob lifecycle policy.
- SAS URLs expire after 1 hour.

---

## What Claude Code Should Never Do

- Never write Python for backend logic.
- Never use `Console.WriteLine` — always `ILogger<T>`.
- Never hardcode any secret, key, connection string, or endpoint.
- Never use `Thread.Sleep` — always `await Task.Delay` with CancellationToken.
- Never use `dynamic` type in C#.
- Never use jQuery or vanilla JS in the React frontend.
- Never use `localStorage` for auth tokens.
- Never shell out to external processes (LibreOffice, pandoc, etc.) for document conversion.
- Never write Terraform — Bicep only for infrastructure.
- Never generate migration files without confirming the EF Core model is finalised.

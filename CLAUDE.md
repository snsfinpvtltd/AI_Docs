# Clinical Trial Agent — POC
## Master context for Claude Code — read this before every session

---

## Project Overview

A POC AI agent on Microsoft Azure that:
1. Accepts uploaded Excel files containing clinical trial patient data
2. Parses and interprets the data using an AI agent (Semantic Kernel + Groq/GPT-4o)
3. Generates AI-authored Patient Data Reports as Word (.docx), PDF, or Excel (.xlsx)
4. Delivers the report as a secure download link

**Frontend:** React + TypeScript (Azure Static Web Apps)
**Backend:** .NET 8 ASP.NET Core Web API (Azure Container Apps)
**Agent framework:** Microsoft Semantic Kernel (C#) — NOT LangChain, NOT Python
**LLM (dev):** Groq — llama-3.3-70b-versatile (free tier, OpenAI-compatible)
**LLM (prod):** Azure OpenAI GPT-4o
**Auth:** Azure Entra ID (Azure AD) via Microsoft.Identity.Web + MSAL.js
**CI/CD:** GitHub Actions → Azure (dev auto-deploy, prod requires approval)

---

## Folder Structure

```
/src
  /ClinicalAgent.Api              → ASP.NET Core Web API (agent host)
    /Controllers
      UploadsController.cs        → POST /api/uploads — Excel upload + persist to DB
                                    GET  /api/uploads — paginated upload history
      ReportsController.cs        → POST /api/reports, GET status, GET download
                                    GET  /api/reports/history — paginated report history
      DashboardController.cs      → GET /api/dashboard — stats + recent activity
      TemplatesController.cs      → GET /api/templates
    /Data
      AppDbContext.cs             → EF Core DbContext (SQLite dev / Azure SQL prod)
      UploadRecord.cs             → DB entity: file metadata + row count + headers JSON
      ReportJobRecord.cs          → DB entity: report job metadata + status + download URL
    /Infrastructure
      ServiceStubs.cs             → StubBlobStorageService (ConcurrentDictionary, Singleton)
                                    StubServiceBusPublisher, StubTemplateRepository
      LocalReportOrchestrator.cs  → Fallback: parses Excel → generates formatted doc
                                    with ClosedXML/OpenXML/QuestPDF (no AI)
      SemanticKernelOrchestrator.cs → AI orchestrator: Excel → Groq/GPT-4o → AI report
                                    Includes ParseAndApplyFiltersAsync (AI filter parsing)
                                    and ApplyAiFilters (structured predicate application)
      NaturalLanguageFilter.cs    → Regex-based NL filter parser (fast path).
                                    Supports ranges, comparisons, equality, inequality,
                                    contains, "only X", bare-value implicit equality.
                                    Used by both orchestrators; SK orchestrator also has
                                    AI-assisted fallback for unrecognised patterns.
      AiReportContent.cs          → AiReportContent record (GPT-4o/Groq JSON schema)
                                    AiFilterCondition + AiFilterResult records (filter parsing)
    GlobalUsings.cs               → global using ClinicalAgent.Core[.Interfaces|.Models]
    Program.cs                    → DI wiring, AI provider selection, EF Core, middleware
    appsettings.json
    appsettings.Development.json  → local secrets (never commit real values)
  /ClinicalAgent.Plugins          → Semantic Kernel KernelFunction plugins (Phase 2)
  /ClinicalAgent.Functions        → Azure Functions document generation (Phase 2)
  /ClinicalAgent.Core             → Shared models, interfaces, constants
    Result.cs                     → Result<T> discriminated union (Ok/Fail)
    Constants.cs                  → BlobContainerNames, TemplateTypes
    /Models                       → ReportRequest, JobStatusResult, ReportSubmitResult, etc.
    /Interfaces                   → IBlobStorageService, IReportOrchestrator,
                                    IServiceBusPublisher, ITemplateRepository
/frontend                         → React + TypeScript (Vite)
  /src
    /api
      apiClient.ts                → Axios instance with MSAL bearer token interceptor
      reports.ts                  → uploadFile, submitReport, getTemplates, getStatus,
                                    getUploads, getReportsHistory, getDashboard
    /components
      Layout.tsx                  → Sidebar navigation shell (dark navy) + topbar
                                    Nav: Dashboard / Upload Data (/upload) /
                                         New Report / Upload History / Reports History
      FileDropzone.tsx            → react-dropzone Excel upload with progress states
    /pages
      LoginPage.tsx               → MSAL redirect login (split-panel clinical SaaS design)
      DashboardPage.tsx           → Stats cards + charts + recent uploads + recent reports
      UploadDataPage.tsx          → Dedicated upload page (/upload) with SHA-256 duplicate
                                    detection — 5-phase state machine (idle/uploading/
                                    duplicate/success/error); duplicate warning card with
                                    "Use existing" / "Upload as new copy" / "Cancel" actions
      ReportRequestPage.tsx       → UploadPicker (select from history) + template + format
                                    + filters form; pre-selects upload when navigated from
                                    UploadDataPage via location.state
      UploadsHistoryPage.tsx      → Paginated upload history table
      ReportsHistoryPage.tsx      → Paginated reports history table with download links
      StatusPage.tsx              → Async job polling
      DownloadPage.tsx            → Download link + format info
    /types
      api.ts                      → TypeScript types matching API response shapes
    msalConfig.ts                 → MSAL PublicClientApplication config
    main.tsx                      → MsalProvider root
    App.tsx                       → React Router with protected routes
  .env.local                      → VITE_ env vars (never committed)
/infra                            → Bicep templates (Azure infrastructure)
/tests
  /ClinicalAgent.Plugins.Tests    → xUnit unit tests for plugins
  /ClinicalAgent.Api.Tests        → xUnit integration tests for API
  /ClinicalAgent.E2E              → Playwright end-to-end tests
  /Fixtures                       → Test Excel/Word fixture files
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

## AI Provider Configuration

### Priority order (Program.cs auto-selects at startup)
```
Groq:ApiKey set          → SemanticKernelOrchestrator  (Groq llama-3.3-70b-versatile)
AzureOpenAI:Endpoint set → SemanticKernelOrchestrator  (Azure OpenAI GPT-4o)
Neither set              → LocalReportOrchestrator     (raw data formatting, no AI)
```

API startup log confirms which path was taken:
```
[INF] AI provider: Groq (llama-3.3-70b-versatile)        ← Groq active
[INF] AI provider: Azure OpenAI (gpt-4o)                 ← Azure OpenAI active
[WRN] No AI provider configured — reports will use local data formatting.
```

### Dev — Groq (free, recommended)
1. Sign up at https://console.groq.com → create a free API key (no credit card)
2. Fill in `appsettings.Development.json`:
```json
"Groq": { "ApiKey": "gsk_your_key_here" }
```
- Model: `llama-3.3-70b-versatile`
- Free tier: 14,400 requests/day, 6,000 tokens/min
- No code change needed when switching to production Azure OpenAI

### Production — Azure OpenAI
```json
"AzureOpenAI": {
  "Endpoint":       "https://YOUR-RESOURCE.openai.azure.com/",
  "DeploymentName": "gpt-4o",
  "ApiKey":         "optional — omit to use DefaultAzureCredential"
}
```

---

## Azure Services & Local Equivalents

| Service | Purpose | Local substitute |
|---|---|---|
| Azure Blob Storage | Uploads, templates, output docs | In-memory ConcurrentDictionary (StubBlobStorageService) |
| Azure Service Bus | Async report job queue | Stubbed (returns not-implemented) |
| Azure OpenAI (GPT-4o) | LLM inference (prod) | Groq API (dev) |
| Azure Container Apps | API host | dotnet run (localhost:7001) |
| Azure Static Web Apps | React host | npm run dev (localhost:5173) |
| Azure Key Vault | Secrets | appsettings.Development.json |

---

## Coding Rules — Follow These Exactly

### General
- **C# only for backend.** Never suggest Python, Node.js, or any other language for backend code.
- **No hardcoded secrets, keys, connection strings, or endpoints** anywhere in code.
- All configuration via `IConfiguration` / `IOptions<T>`, values from environment or Azure App Configuration.
- Every public method must have XML doc comments (`/// <summary>`).
- Use `CancellationToken` on every async method signature.
- Use `record` types for immutable DTOs and result objects.
- Use `Result<T>` pattern for error handling — no throwing exceptions for expected failures.

### Semantic Kernel & AI rules
- Always use `OpenAIPromptExecutionSettings` (not `AzureOpenAIPromptExecutionSettings`) in `SemanticKernelOrchestrator` — this makes the orchestrator work with both Groq and Azure OpenAI without code changes.
- Max **100 rows** sent to the LLM per request (token budget). Full dataset is still written to the output document.
- Temperature = 0.2, MaxTokens = 3000 for consistent, factual medical language.
- System prompt must instruct the model to return ONLY a JSON object — no markdown fences, no extra text.
- `ExtractJson()` strips markdown code fences and extracts the first `{…}` block as a safety net for non-compliant responses.

### Service lifetime — critical
- `StubBlobStorageService`, `StubServiceBusPublisher`, `StubTemplateRepository`, and the report orchestrator are all **Singleton**.
- Never use `Scoped` for these — blobs uploaded in one request are lost by the next request if the store is scoped.
- `StubBlobStorageService` uses `ConcurrentDictionary<string, byte[]>` for thread safety.

### Document generation rules
- Never use `Interop` libraries — Open XML SDK only for Word, ClosedXML only for Excel.
- QuestPDF for PDF — set `QuestPDF.Settings.License = LicenseType.Community` in a static constructor.
- When using both `DocumentFormat.OpenXml.Wordprocessing` and `QuestPDF.Fluent` in the same file, always declare these aliases to resolve name collisions:
  ```csharp
  using OxDocument = DocumentFormat.OpenXml.Wordprocessing.Document;
  using OxColor    = DocumentFormat.OpenXml.Wordprocessing.Color;
  using QDocument  = QuestPDF.Fluent.Document;
  ```

### Azure SDK usage
- Always use `DefaultAzureCredential` in production code paths.
- API key path (`AzureOpenAI:ApiKey`) is for local dev convenience only — never in prod config.

### Error handling
- Use `ILogger<T>` for all logging — never `Console.WriteLine`.
- Log with structured properties, not string interpolation.
- Every controller action must catch exceptions and return `ProblemDetails`.

### Frontend rules
- No component libraries (no MUI, no Ant Design, no Chakra) — Tailwind CSS utility classes only.
- No `any` TypeScript — every variable, prop, and return type must be explicitly typed.
- All API calls via `apiClient.ts` (axios instance with MSAL bearer token interceptor).
- MSAL token acquired silently first; interactive login only as fallback.
- Never store tokens in `localStorage` — MSAL handles token cache internally.
- Form validation with `react-hook-form` — no manual state management for forms.
- All environment variables prefixed `VITE_` and read from `.env.local` (not committed to Git).

---

## Report Generation Flow (Full Implementation)

```
1. User uploads Excel  → POST /api/uploads (via UploadDataPage at /upload)
                       → SHA-256 hash of file bytes computed server-side
                       → Duplicate check: if same hash + userId exists → HTTP 409
                         { isDuplicate: true, existingUpload: UploadSummary }
                         Frontend shows duplicate warning; user can choose to use
                         existing file, force re-upload (?force=true), or cancel.
                       → StubBlobStorageService.UploadAsync("uploads", "{userId}/{guid}.xlsx")
                       → returns { blobName: "{userId}/{guid}.xlsx" }

2. User submits form   → POST /api/reports
                       → ReportRequest { blobName, templateType, outputFormat, prompt, trialId, ... }

3. SemanticKernelOrchestrator.SubmitAsync():
   a. DownloadAsync("uploads", blobName)        ← in-memory blob store
   b. ParseExcel() with ClosedXML:
      - read headers from row 1
      - iterate rows (max 2000), apply TrialId filter
   c. CallAiAsync():
      - BuildSystemPrompt() — JSON schema instruction, clinical analyst persona
      - BuildUserPrompt()   — templateType, filters, headers, up to 100 rows as CSV
      - IChatCompletionService.GetChatMessageContentAsync() via Groq or Azure OpenAI
      - ExtractJson() → deserialize to AiReportContent
   d. BuildDocument(aiContent, data, format):
      Word  → OpenXML: title, executive summary, key findings bullets,
               analysis sections, data appendix table (max 500 rows)
      PDF   → QuestPDF: same content, paginated, A4 landscape for data table (max 1000 rows)
      Excel → ClosedXML: "AI Analysis" sheet (all text sections) +
                          "Patient Data" sheet (full data table with formatting)
   e. UploadAsync("generated-docs", "{jobId}.{ext}")
   f. returns { jobId, downloadUrl: "http://localhost:7001/api/reports/{jobId}/download?format={ext}", isAsync: false }

4. User downloads      → GET /api/reports/{jobId}/download?format={ext}
                       → DownloadAsync("generated-docs", "{jobId}.{ext}")
                       → File(stream, mimeType, "report-{jobId}.{ext}")

Fallback (no AI key set):
   LocalReportOrchestrator — identical flow but skips step (c);
   writes raw Excel rows directly into the document with no AI analysis.
```

---

## AI Report Content (AiReportContent)

GPT-4o / Groq returns a JSON object matching this record:

| Field | Type | Description |
|---|---|---|
| `reportTitle` | string | Descriptive title including template and trial info |
| `executiveSummary` | string | 2–4 sentences: cohort size, primary outcome, trial status |
| `keyFindings` | string[] | 3–6 concise clinical findings |
| `dataOverview` | string | Column completeness, date ranges, data quality |
| `patientAnalysis` | string | Demographics, treatment response, subgroup observations |
| `adverseEventsAnalysis` | string | AE frequency, severity, patterns (or note if absent) |
| `statisticalInsights` | string | Ranges, distributions, key trends |
| `recommendations` | string | Clinical and operational recommendations |
| `limitations` | string | Data gaps, truncation, missing columns, caveats |

---

## API Endpoints

```
POST   /api/uploads                    Upload Excel (.xlsx) → persists UploadRecord → { blobName }
GET    /api/uploads?page=&pageSize=    Paginated upload history → UploadSummary[]
POST   /api/reports                    Submit report → persists ReportJobRecord → { jobId, downloadUrl, isAsync }
GET    /api/reports/history?page=      Paginated report job history → ReportJobSummary[]
GET    /api/reports/{jobId}/status     Poll async job → { status, downloadUrl?, progressPercent }
GET    /api/reports/{jobId}/download   Download file (AllowAnonymous) → file stream
GET    /api/dashboard                  Stats + 5 recent uploads + 5 recent reports → DashboardStats
GET    /api/templates                  List templates → TemplateInfo[]
GET    /health                         Health check (no auth)
```

## Database (SQLite / EF Core)

### Schema
| Table        | Key fields |
|---|---|
| `Uploads`    | Id (12-char), FileName, BlobName, UserId, UploadedAt, RowCount, FileSizeBytes, HeadersJson (JSON array), FileHash (SHA-256 hex, nullable) |
| `ReportJobs` | Id (jobId), UploadId (FK, nullable), BlobName, TemplateType, OutputFormat, Status, CreatedAt, CompletedAt, DownloadUrl, UserId, PromptText, RowCount |

### Configuration
```json
"Database": { "Path": "clinicalagent.db" }
```
- Dev: SQLite file `clinicalagent.db` in working directory, created automatically via `EnsureCreated()`.
- Prod: swap `UseSqlite` for `UseSqlServer`/`UseNpgsql` + add the matching NuGet package.

### Patterns
- Controllers are scoped → inject `AppDbContext` directly.
- Orchestrators are **Singleton** → inject `IDbContextFactory<AppDbContext>`, call `CreateDbContextAsync()` per operation.
- Never register orchestrators as Scoped — the in-memory blob store would lose data between requests.

### ReportRequest model
```csharp
public record ReportRequest(
    string BlobName,           // "{userId}/{guid}.xlsx"
    string TemplateType,       // "patient-summary" | "outcome-data" | "full-report"
    OutputFormat OutputFormat, // Docx | Pdf | Excel
    string? FreeTextPrompt,    // forwarded to AI as "user focus" instruction
    DateOnly? DateRangeFrom,
    DateOnly? DateRangeTo,
    string? TrialId            // filters Excel rows by TrialID/Trial_ID column
);
```

---

## Available Templates (StubTemplateRepository)

| ID | Display Name | Formats |
|---|---|---|
| `patient-summary` | Patient Summary | Docx, Pdf |
| `outcome-data` | Outcome Data | Docx, Pdf, Excel |
| `full-report` | Full Trial Report | Docx, Pdf |

---

## Blob Storage Layout

```
uploads/            ← Excel files (key: "{userId}/{guid}.xlsx")
clinical-templates/ ← Report templates — Phase 2 (not yet implemented)
generated-docs/     ← Completed AI reports (key: "{jobId}.{ext}")
```

---

## Environment Variables Reference

### appsettings.Development.json (local only — never commit real values)
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "073de3bd-897f-43f4-94d0-27460ef0a774",
    "ClientId": "69ec6f2f-a1ce-4f98-93b0-ec20e3c6a0b3",
    "Scopes":   "access_as_user"
  },
  "Groq": {
    "ApiKey": "gsk_..."
  },
  "AzureOpenAI": {
    "Endpoint":       "",
    "DeploymentName": "gpt-4o",
    "ApiKey":         ""
  },
  "Storage": {
    "ConnectionString": "UseDevelopmentStorage=true"
  }
}
```

### Container App — Azure App Configuration (production)
```
AzureOpenAI__Endpoint
AzureOpenAI__DeploymentName          (= gpt-4o)
Storage__ServiceUri                  (= https://{account}.blob.core.windows.net)
ServiceBus__FullyQualifiedNamespace  (= {namespace}.servicebus.windows.net)
ApplicationInsights__ConnectionString
```

### React — frontend/.env.local (never committed)
```
VITE_AZURE_CLIENT_ID="69ec6f2f-a1ce-4f98-93b0-ec20e3c6a0b3"
VITE_AZURE_TENANT_ID="073de3bd-897f-43f4-94d0-27460ef0a774"
VITE_API_BASE_URL=http://localhost:7001
VITE_AZURE_API_SCOPE="api://69ec6f2f-a1ce-4f98-93b0-ec20e3c6a0b3/access_as_user"
```

---

## GitHub Actions Workflows

| File | Trigger | What it does |
|---|---|---|
| `backend-ci-cd.yml` | push to main, paths: src/** | dotnet build → test → docker push ACR → deploy Container App |
| `functions-deploy.yml` | push to main, paths: src/ClinicalAgent.Functions/** | dotnet publish → deploy Azure Functions |
| `frontend-deploy.yml` | push to main, paths: frontend/** | npm build → deploy Static Web Apps |
| `infra-deploy.yml` | manual workflow_dispatch | az deployment group create with /infra/main.bicep |

- OIDC federated credentials — no stored Azure credentials in GitHub secrets.
- `environment: dev` for automatic deploys.
- `environment: prod` with `required-reviewers` gate for production.

---

## Local Development Checklist

Before starting a new session:
- [ ] `dotnet run --project src/ClinicalAgent.Api --launch-profile http` → API on localhost:7001
- [ ] `cd frontend && npm run dev` → frontend on localhost:5173
- [ ] `appsettings.Development.json` has `Groq:ApiKey` filled in
- [ ] `frontend/.env.local` has correct `VITE_` values
- [ ] `curl http://localhost:7001/health` returns `Healthy`

Confirm AI is active in the API startup log:
```
[INF] AI provider: Groq (llama-3.3-70b-versatile)
```

Run all tests before pushing:
```bash
dotnet test
cd frontend && npm test
npx playwright test
```

---

## Clinical Trial Data — Important Notes

- **POC uses SYNTHETIC / ANONYMISED data only.**
- Groq API is acceptable for POC because all data is synthetic — do not send real PHI/PII to Groq.
- In production, switch to Azure OpenAI (data stays within your Azure tenant).
- Excel files are deleted from Blob Storage after report generation or within 24 hours.
- Generated reports retained for 7 days, then auto-deleted by Blob lifecycle policy.
- SAS URLs expire after 1 hour (production).

---

## What Claude Code Should Never Do

- Never write Python for backend logic.
- Never use `Console.WriteLine` — always `ILogger<T>`.
- Never hardcode any secret, key, connection string, or endpoint.
- Never use `Thread.Sleep` — always `await Task.Delay` with CancellationToken.
- Never use `dynamic` type in C#.
- Never use jQuery or vanilla JS in the React frontend.
- Never use `localStorage` for auth tokens.
- Never shell out to external processes for document conversion.
- Never write Terraform — Bicep only for infrastructure.
- Never generate EF Core migration files without confirming the model is finalised.
- Never register `StubBlobStorageService` or the orchestrator as `Scoped` — always `Singleton`.
- Never use `AzureOpenAIPromptExecutionSettings` in `SemanticKernelOrchestrator` — use `OpenAIPromptExecutionSettings` so both Groq and Azure OpenAI work without code changes.
- Never send more than 100 rows to the LLM in a single prompt.

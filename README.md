# Clinical Trial Agent — POC

An AI-powered clinical trial reporting platform built on Microsoft Azure. Upload Excel patient data, generate professional Word / PDF / Excel reports authored by an LLM, and download them instantly.

---

## What It Does

1. **Upload** an Excel (.xlsx) file containing clinical trial patient data
2. **Filter** rows using natural-language instructions (e.g. "age less than 40", "gender is Female")
3. **Analyse** the data with an AI agent (Groq llama-3.3-70b / Azure OpenAI GPT-4o via Semantic Kernel)
4. **Generate** a structured report — Word (.docx), PDF, or Excel — with executive summary, key findings, patient analysis, adverse events, statistical insights, and recommendations
5. **Download** via a secure link

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React + TypeScript, Vite, Tailwind CSS, MSAL.js |
| Backend | .NET 8 ASP.NET Core Web API |
| AI agent | Microsoft Semantic Kernel (C#) |
| LLM (dev) | Groq — llama-3.3-70b-versatile (free tier) |
| LLM (prod) | Azure OpenAI GPT-4o |
| Auth | Azure Entra ID + Social (Google / GitHub) via LocalJwtService |
| Storage | Azure Blob Storage (local: file-system stub) |
| Database | SQLite (dev) / Azure SQL (prod) via EF Core |
| Docs | OpenXML SDK (Word), QuestPDF (PDF), ClosedXML (Excel) |
| Charts | ScottPlot (PNG charts embedded in reports) |

---

## Project Structure

```
/src
  /ClinicalAgent.Api            → ASP.NET Core Web API
    /Controllers
      UploadsController.cs      → POST /api/uploads, GET /api/uploads
      ReportsController.cs      → POST /api/reports, GET status/download/history
      DashboardController.cs    → GET /api/dashboard
      AuthController.cs         → Social OAuth (Google / GitHub)
    /Data
      AppDbContext.cs            → EF Core (SQLite dev / Azure SQL prod)
      UploadRecord.cs            → Upload metadata entity
      ReportJobRecord.cs         → Report job entity
    /Infrastructure
      SemanticKernelOrchestrator.cs  → AI pipeline: Excel → Groq/GPT-4o → document
      LocalReportOrchestrator.cs     → Fallback: no AI, raw data formatting only
      NaturalLanguageFilter.cs       → Parses free-text prompt → row predicates
      ChartGenerator.cs              → ScottPlot PNG charts + table helpers
      AiReportContent.cs             → C# record matching LLM JSON response schema
      LocalJwtService.cs             → Issues HS256 JWTs for social sign-in
      ServiceStubs.cs                → File-system blob store, stub bus/templates
/frontend
  /src
    /api          → apiClient.ts (Axios + MSAL), reports.ts
    /components   → Layout.tsx, FileDropzone.tsx
    /pages        → Dashboard, Upload, Report Request, History, Status, Download
    /auth         → MSAL config, AuthContext, social token store
    /types        → TypeScript types matching API shapes
/infra            → Bicep templates (Azure infrastructure)
/tests
  /Fixtures       → Sample Excel files, generated report files
  /ClinicalAgent.Api.Tests    → xUnit integration tests
  /ClinicalAgent.E2E          → Playwright end-to-end tests
```

---

## How the AI Report Works

```
Excel upload (.xlsx)
      │
      ▼  ClosedXML — parse rows + headers (max 2000 rows)
      │
      ▼  NaturalLanguageFilter — apply free-text prompt conditions
         e.g. "age less than 40" drops patients aged 40+
      │
      ▼  BuildUserPrompt — serialize headers + first 100 rows as CSV text
      │
      ▼  Groq / GPT-4o (via Semantic Kernel)
         System: clinical analyst persona + strict JSON schema
         User:   template type, filters, CSV data
         Temp=0.2, MaxTokens=3000
      │
      ▼  AiReportContent (9 fields: title, summary, findings, analysis…)
      │
      ▼  BuildDocument — write AI text sections + raw data appendix table
         Word  → OpenXML SDK  (AI sections + table, max 500 rows)
         PDF   → QuestPDF     (AI sections + table, max 1000 rows)
         Excel → ClosedXML    ("AI Analysis" sheet + "Patient Data" sheet)
      │
      ▼  Stored in .blobstore/generated-docs/{jobId}.{ext}
      │
      ▼  GET /api/reports/{jobId}/download  → file stream to browser
```

---

## Local Development

### Prerequisites

- .NET 8 SDK
- Node.js 20+
- A free [Groq API key](https://console.groq.com) (no credit card required)

### 1 — Configure secrets

Edit `src/ClinicalAgent.Api/appsettings.Development.json`:

```json
{
  "Groq": {
    "ApiKey": "gsk_your_key_here"
  },
  "Jwt": {
    "Secret": "any-string-32-chars-or-longer!!!!"
  },
  "Database": {
    "Path": "clinicalagent.db"
  }
}
```

Edit `frontend/.env.local`:

```
VITE_AZURE_CLIENT_ID="69ec6f2f-a1ce-4f98-93b0-ec20e3c6a0b3"
VITE_AZURE_TENANT_ID="073de3bd-897f-43f4-94d0-27460ef0a774"
VITE_API_BASE_URL=http://localhost:7001
VITE_AZURE_API_SCOPE="api://69ec6f2f-a1ce-4f98-93b0-ec20e3c6a0b3/access_as_user"
```

### 2 — Start the API

```bash
dotnet run --project src/ClinicalAgent.Api --launch-profile http
# API available at http://localhost:7001
# Swagger UI at http://localhost:7001/swagger
```

Confirm AI is active in the startup log:
```
[INF] AI provider: Groq (llama-3.3-70b-versatile)
```

### 3 — Start the frontend

```bash
cd frontend
npm install
npm run dev
# App available at http://localhost:5173
```

### 4 — Health check

```bash
curl http://localhost:7001/health
# → Healthy
```

---

## API Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/uploads` | Upload Excel (.xlsx) — returns `{ blobName }` |
| `GET` | `/api/uploads` | Paginated upload history |
| `POST` | `/api/reports` | Submit report job — returns `{ jobId, downloadUrl }` |
| `GET` | `/api/reports/history` | Paginated report history |
| `GET` | `/api/reports/{jobId}/status` | Poll async job status |
| `GET` | `/api/reports/{jobId}/download` | Download file (anonymous) |
| `GET` | `/api/dashboard` | Stats + recent activity |
| `GET` | `/api/templates` | Available report templates |
| `GET` | `/health` | Health check (no auth) |

### Report request body

```json
{
  "blobNames":      ["userId/guid.xlsx"],
  "templateType":   "patient-summary",
  "outputFormat":   0,
  "freeTextPrompt": "age less than 40",
  "trialId":        null,
  "dateRangeFrom":  null,
  "dateRangeTo":    null
}
```

`outputFormat`: `0` = Word (.docx), `1` = PDF, `2` = Excel (.xlsx)

---

## Natural Language Filtering

The `freeTextPrompt` field supports column-level filter conditions applied **before** both the AI call and the document appendix table:

| Prompt example | Effect |
|---|---|
| `age less than 40` | Keeps rows where Age < 40 |
| `score greater than 80` | Keeps rows where Score > 80 |
| `age between 18 and 40` | Keeps rows where 18 ≤ Age ≤ 40 |
| `gender is Female` | Keeps rows where Gender = "Female" (case-insensitive) |
| `outcome = Completed` | Keeps rows where Outcome = "Completed" |

Multiple conditions in one prompt are combined with AND.
The filter note (e.g. `Age < 40 — 63 row(s) shown`) is printed in the report appendix.

---

## AI Provider Configuration

The API auto-selects at startup:

```
Groq:ApiKey set           →  SemanticKernelOrchestrator  (Groq llama-3.3-70b-versatile)
AzureOpenAI:Endpoint set  →  SemanticKernelOrchestrator  (Azure OpenAI GPT-4o)
Neither set               →  LocalReportOrchestrator     (raw data only, no AI)
```

### Switching to Azure OpenAI (production)

```json
"AzureOpenAI": {
  "Endpoint":       "https://YOUR-RESOURCE.openai.azure.com/",
  "DeploymentName": "gpt-4o"
}
```

Leave `ApiKey` empty to use `DefaultAzureCredential` (managed identity).

---

## Available Report Templates

| ID | Name | Formats |
|---|---|---|
| `patient-summary` | Patient Summary | Word, PDF |
| `outcome-data` | Outcome Data | Word, PDF, Excel |
| `full-report` | Full Trial Report | Word, PDF |

---

## Running Tests

```bash
# Backend unit + integration tests
dotnet test

# Frontend unit tests
cd frontend && npm test

# End-to-end (Playwright) — requires both servers running
npx playwright test
```

---

## Deployment (Azure)

Infrastructure is defined in `/infra/main.bicep`. Deploy with:

```bash
az deployment group create \
  --resource-group rg-clinical-agent \
  --template-file infra/main.bicep
```

CI/CD via GitHub Actions (`.github/workflows/`):

| Workflow | Trigger | Target |
|---|---|---|
| `backend-ci-cd.yml` | push `src/**` | Azure Container Apps |
| `frontend-deploy.yml` | push `frontend/**` | Azure Static Web Apps |
| `infra-deploy.yml` | manual | Azure (Bicep) |

Production deployments require approval via GitHub Environments (`prod`).

---

## Important Notes

- **POC uses synthetic / anonymised data only.** Do not upload real patient data to Groq.
- In production, switch to Azure OpenAI — data stays within your Azure tenant.
- The LLM receives only the **first 100 rows** per request (token budget). The full dataset goes into the document appendix.
- Uploaded Excel files and generated reports are stored in `.blobstore/` locally (disk-backed, survives restarts).
- Never commit `appsettings.Development.json` or `frontend/.env.local` — both are in `.gitignore`.

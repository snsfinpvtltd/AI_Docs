export type OutputFormat = 'Docx' | 'Pdf' | 'Excel'

export interface SocialAuthResponse {
  token:    string
  provider: string
  email:    string
  name:     string
}


export type ReportStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed'

export interface TemplateInfo {
  id: string
  displayName: string
  description: string
  supportedFormats: OutputFormat[]
}

export interface UploadResult {
  blobName: string
}

export interface DuplicateUploadResponse {
  isDuplicate: true
  existingUpload: UploadSummary
}

export type UploadResponse =
  | { kind: 'success'; blobName: string }
  | { kind: 'duplicate'; existingUpload: UploadSummary }

export interface ReportRequest {
  templateType: string
  outputFormat: OutputFormat
  freeTextPrompt?: string
  dateRangeFrom?: string
  dateRangeTo?: string
  trialId?: string
}

export interface ReportSubmitResult {
  jobId: string
  downloadUrl?: string
  isAsync: boolean
}

export interface JobStatusResult {
  jobId: string
  status: ReportStatus
  downloadUrl?: string
  progressPercent?: number
  error?: string
}

export interface UploadSummary {
  id: string
  fileName: string
  rowCount: number
  uploadedAt: string
  fileSizeBytes: number
  blobName: string
  headers: string[]
}

export interface ReportJobSummary {
  id: string
  templateType: string
  outputFormat: string
  status: string
  createdAt: string
  completedAt?: string
  downloadUrl?: string
  rowCount: number
  promptText?: string
}

export interface ChartCount {
  label: string
  count: number
}

export interface DailyCount {
  date: string
  count: number
}

export interface DashboardStats {
  totalUploads: number
  totalPatients: number
  totalReports: number
  completedReports: number
  recentUploads: UploadSummary[]
  recentReports: ReportJobSummary[]
  reportsByStatus: ChartCount[]
  reportsByTemplate: ChartCount[]
  reportsByFormat: ChartCount[]
  uploadsLast7Days: DailyCount[]
}

import type { AxiosError } from 'axios'
import apiClient from './apiClient'
import type {
  CustomTemplateDetail,
  CustomTemplateRequest,
  CustomTemplateSummary,
  DashboardStats,
  DuplicateUploadResponse,
  JobStatusResult,
  ReportJobSummary,
  ReportRequest,
  ReportSubmitResult,
  TemplateInfo,
  UploadResponse,
  UploadResult,
  UploadSummary,
} from '../types/api'

export async function getTemplates(): Promise<TemplateInfo[]> {
  const { data } = await apiClient.get<TemplateInfo[]>('/api/templates')
  return data
}

/**
 * Upload an Excel file.
 * Returns `{ kind: 'success' }` on a new upload or
 * `{ kind: 'duplicate' }` when the same file (by SHA-256) already exists.
 * Pass `force = true` to bypass the duplicate check and upload anyway.
 */
export async function uploadFile(
  file: File,
  force = false,
  onProgress?: (pct: number) => void,
): Promise<UploadResponse> {
  const form = new FormData()
  form.append('file', file)
  try {
    const { data } = await apiClient.post<UploadResult>(
      `/api/uploads${force ? '?force=true' : ''}`,
      form,
      {
        onUploadProgress: (evt) => {
          if (onProgress && evt.total) {
            onProgress(Math.round((evt.loaded * 100) / evt.total))
          }
        },
      },
    )
    return { kind: 'success', blobName: data.blobName }
  } catch (err) {
    const axiosErr = err as AxiosError<DuplicateUploadResponse>
    if (axiosErr.response?.status === 409 && axiosErr.response.data?.isDuplicate) {
      return { kind: 'duplicate', existingUpload: axiosErr.response.data.existingUpload }
    }
    throw err
  }
}

export async function getUploads(page = 1, pageSize = 20): Promise<UploadSummary[]> {
  const { data } = await apiClient.get<UploadSummary[]>('/api/uploads', {
    params: { page, pageSize },
  })
  return data
}

export async function submitReport(request: ReportRequest): Promise<ReportSubmitResult> {
  const { data } = await apiClient.post<ReportSubmitResult>('/api/reports', request)
  return data
}

export async function getJobStatus(jobId: string): Promise<JobStatusResult> {
  const { data } = await apiClient.get<JobStatusResult>(`/api/reports/${jobId}/status`)
  return data
}

export async function getReportsHistory(page = 1, pageSize = 20): Promise<ReportJobSummary[]> {
  const { data } = await apiClient.get<ReportJobSummary[]>('/api/reports/history', {
    params: { page, pageSize },
  })
  return data
}

export async function getDashboard(): Promise<DashboardStats> {
  const { data } = await apiClient.get<DashboardStats>('/api/dashboard')
  return data
}

// ── Custom report template API ────────────────────────────────────────────────

export async function getCustomTemplates(): Promise<CustomTemplateSummary[]> {
  const { data } = await apiClient.get<CustomTemplateSummary[]>('/api/report-templates')
  return data
}

export async function getCustomTemplate(id: string): Promise<CustomTemplateDetail> {
  const { data } = await apiClient.get<CustomTemplateDetail>(`/api/report-templates/${id}`)
  return data
}

export async function createCustomTemplate(req: CustomTemplateRequest): Promise<CustomTemplateDetail> {
  const { data } = await apiClient.post<CustomTemplateDetail>('/api/report-templates', req)
  return data
}

export async function updateCustomTemplate(id: string, req: CustomTemplateRequest): Promise<CustomTemplateDetail> {
  const { data } = await apiClient.put<CustomTemplateDetail>(`/api/report-templates/${id}`, req)
  return data
}

export async function deleteCustomTemplate(id: string): Promise<void> {
  await apiClient.delete(`/api/report-templates/${id}`)
}

export function getApiErrorMessage(err: unknown): string {
  const axiosErr = err as AxiosError<{ title?: string; detail?: string }>
  if (axiosErr.response?.data?.detail) return axiosErr.response.data.detail
  if (axiosErr.response?.data?.title)  return axiosErr.response.data.title
  if (axiosErr.message) return axiosErr.message
  return 'An unexpected error occurred.'
}

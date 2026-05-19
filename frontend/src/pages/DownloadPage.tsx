import { type ReactElement, useEffect, useState } from 'react'
import { useParams, useLocation, Link } from 'react-router-dom'
import { getJobStatus, getApiErrorMessage } from '../api/reports'
import Layout from '../components/Layout'

interface LocationState {
  downloadUrl?: string
}

export default function DownloadPage(): ReactElement {
  const { jobId } = useParams<{ jobId: string }>()
  const location = useLocation()
  const state = location.state as LocationState | null

  const [downloadUrl, setDownloadUrl] = useState<string>(state?.downloadUrl ?? '')
  const [fetchError, setFetchError] = useState<string>('')
  const [isLoading, setIsLoading] = useState(!state?.downloadUrl)

  useEffect(() => {
    if (state?.downloadUrl || !jobId) return

    getJobStatus(jobId)
      .then((result) => {
        if (result.downloadUrl) {
          setDownloadUrl(result.downloadUrl)
        } else {
          setFetchError('Report URL not available — the job may still be processing.')
        }
      })
      .catch((err: unknown) => setFetchError(getApiErrorMessage(err)))
      .finally(() => setIsLoading(false))
  }, [jobId, state?.downloadUrl])

  function handleDownload(): void {
    if (!downloadUrl) return
    window.open(downloadUrl, '_blank', 'noopener,noreferrer')
  }

  const ext = downloadUrl.split('.').pop()?.toLowerCase() ?? ''
  const formatInfo: Record<string, { label: string; desc: string }> = {
    docx: { label: 'Word Document',  desc: 'Open with Microsoft Word or Google Docs' },
    pdf:  { label: 'PDF Document',   desc: 'Open with any PDF reader or browser' },
    xlsx: { label: 'Excel Workbook', desc: 'Open with Microsoft Excel or Google Sheets' },
  }
  const fmt = formatInfo[ext] ?? { label: 'Report File', desc: 'Save your file promptly — the link expires after 1 hour.' }

  return (
    <Layout title="Download Report" subtitle="Your AI-generated clinical trial report is ready">
      <div className="max-w-xl mx-auto">
        <div className="mb-6 flex items-center justify-between">
          <p className="text-sm text-brand-muted">Report complete</p>
          <Link to="/request" className="text-sm text-[#1EB5C7] hover:text-[#12b3c5] font-medium transition-colors">
            ← New report
          </Link>
        </div>

        <div className="rounded-[9px] bg-white shadow-card border border-brand-border overflow-hidden">
          {/* Job ID header */}
          <div className="px-6 py-4 border-b border-brand-border bg-brand-surface flex items-center justify-between">
            <span className="text-xs font-semibold text-brand-muted uppercase tracking-wider">Job ID</span>
            <code className="rounded-[5px] bg-white border border-brand-border px-2.5 py-1 font-mono text-xs text-brand-charcoal shadow-sm">
              {jobId}
            </code>
          </div>

          <div className="px-6 py-6 space-y-5">
            {isLoading && (
              <div className="flex items-center gap-3 text-sm text-brand-muted">
                <svg className="w-4 h-4 animate-spin text-[#0072ff]" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                </svg>
                Loading download link…
              </div>
            )}

            {fetchError && (
              <div className="rounded-[9px] bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
                {fetchError}
              </div>
            )}

            {downloadUrl && (
              <>
                {/* Success banner */}
                <div className="flex items-start gap-3 rounded-[9px] bg-[#12E58D]/10 border border-[#12E58D]/30 px-4 py-4">
                  <div className="w-8 h-8 rounded-full bg-[#12E58D]/20 flex items-center justify-center flex-shrink-0 mt-0.5">
                    <svg className="w-4 h-4 text-[#0a7a4c]" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                    </svg>
                  </div>
                  <div>
                    <p className="text-sm font-semibold text-[#0a7a4c] font-heading">Report generated successfully</p>
                    <p className="text-xs text-[#0a7a4c]/70 mt-0.5">{fmt.label} · {fmt.desc}</p>
                  </div>
                </div>

                {/* Format info card */}
                {ext && (
                  <div className="rounded-[9px] bg-brand-surface border border-brand-border px-4 py-3">
                    <div className="flex items-center gap-2 mb-1">
                      <svg className="w-4 h-4 text-brand-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                          d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                      </svg>
                      <span className="text-xs font-semibold text-brand-charcoal">About this file</span>
                    </div>
                    <p className="text-xs text-brand-muted leading-relaxed">
                      This report contains AI-analysed clinical trial data. The download link expires after 1 hour —
                      save your file promptly.
                    </p>
                  </div>
                )}

                <button
                  type="button"
                  onClick={handleDownload}
                  className="btn-brand w-full py-3.5 text-base gap-3"
                >
                  <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                      d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                  </svg>
                  Download {fmt.label}
                </button>

                {/* Quick actions */}
                <div className="flex gap-3 pt-1">
                  <Link
                    to="/request"
                    className="flex-1 text-center py-2.5 rounded-[9px] border border-brand-border text-sm font-semibold text-brand-charcoal bg-white hover:bg-brand-surface transition-colors"
                  >
                    New report
                  </Link>
                  <Link
                    to="/reports"
                    className="flex-1 text-center py-2.5 rounded-[9px] border border-brand-border text-sm font-semibold text-brand-charcoal bg-white hover:bg-brand-surface transition-colors"
                  >
                    View all reports
                  </Link>
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    </Layout>
  )
}

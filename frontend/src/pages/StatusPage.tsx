import { type ReactElement, useEffect, useState, useRef } from 'react'
import { useParams, useNavigate, Link } from 'react-router-dom'
import { getJobStatus, getApiErrorMessage } from '../api/reports'
import type { JobStatusResult, ReportStatus } from '../types/api'
import Layout from '../components/Layout'

const POLL_INTERVAL_MS = 3000

const statusLabel: Record<ReportStatus, string> = {
  Queued: 'Queued — waiting to start',
  Processing: 'Generating your AI-powered report…',
  Completed: 'Report ready',
  Failed: 'Generation failed',
}

const statusColors: Record<ReportStatus, { dot: string; text: string; bg: string; ring: string }> = {
  Queued:     { dot: 'bg-amber-400',                       text: 'text-amber-700',   bg: 'bg-amber-50',           ring: 'ring-amber-200' },
  Processing: { dot: 'bg-[#0072ff] animate-pulse',         text: 'text-[#0072ff]',   bg: 'bg-[#0072ff]/5',        ring: 'ring-[#0072ff]/20' },
  Completed:  { dot: 'bg-[#12E58D]',                       text: 'text-[#0a7a4c]',   bg: 'bg-[#12E58D]/10',       ring: 'ring-[#12E58D]/30' },
  Failed:     { dot: 'bg-red-500',                          text: 'text-red-700',     bg: 'bg-red-50',             ring: 'ring-red-200' },
}

export default function StatusPage(): ReactElement {
  const { jobId } = useParams<{ jobId: string }>()
  const navigate = useNavigate()
  const [jobStatus, setJobStatus] = useState<JobStatusResult | null>(null)
  const [fetchError, setFetchError] = useState<string>('')
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  useEffect(() => {
    if (!jobId) return

    async function poll(): Promise<void> {
      if (!jobId) return
      try {
        const result = await getJobStatus(jobId)
        setJobStatus(result)
        setFetchError('')

        if (result.status === 'Completed') {
          if (intervalRef.current !== null) clearInterval(intervalRef.current)
          navigate(`/download/${jobId}`, { state: { downloadUrl: result.downloadUrl } })
        } else if (result.status === 'Failed') {
          if (intervalRef.current !== null) clearInterval(intervalRef.current)
        }
      } catch (err) {
        setFetchError(getApiErrorMessage(err))
      }
    }

    void poll()
    intervalRef.current = setInterval(() => void poll(), POLL_INTERVAL_MS)

    return () => {
      if (intervalRef.current !== null) clearInterval(intervalRef.current)
    }
  }, [jobId, navigate])

  const isTerminal = jobStatus?.status === 'Completed' || jobStatus?.status === 'Failed'
  const cfg = jobStatus ? statusColors[jobStatus.status] : null

  return (
    <Layout title="Report Status" subtitle="Monitoring AI report generation">
      <div className="max-w-xl mx-auto">
        <div className="mb-6 flex items-center justify-between">
          <p className="text-sm text-brand-muted">Job progress</p>
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
            {fetchError && (
              <div className="rounded-[9px] bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
                {fetchError}
              </div>
            )}

            {!jobStatus && !fetchError && (
              <div className="flex items-center gap-3 text-sm text-brand-muted">
                <svg className="w-4 h-4 animate-spin text-[#0072ff]" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                </svg>
                Connecting to job…
              </div>
            )}

            {jobStatus && cfg && (
              <>
                {/* Status badge */}
                <div className={`flex items-center gap-3 rounded-[9px] px-4 py-3 ring-1 ${cfg.bg} ${cfg.ring}`}>
                  <span className={`inline-block w-2.5 h-2.5 rounded-full flex-shrink-0 ${cfg.dot}`} />
                  <span className={`text-sm font-semibold ${cfg.text}`}>
                    {statusLabel[jobStatus.status]}
                  </span>
                </div>

                {/* Progress bar */}
                {jobStatus.progressPercent !== undefined && jobStatus.progressPercent > 0 && (
                  <div>
                    <div className="flex justify-between text-xs text-brand-muted mb-1.5">
                      <span>Progress</span>
                      <span>{jobStatus.progressPercent}%</span>
                    </div>
                    <div className="w-full bg-brand-surface rounded-full h-2 overflow-hidden">
                      <div
                        className="h-2 rounded-full transition-all duration-500"
                        style={{
                          width: `${jobStatus.progressPercent}%`,
                          backgroundImage: 'linear-gradient(90deg, #0072ff, #8200f4)',
                        }}
                      />
                    </div>
                  </div>
                )}

                {/* Processing animation */}
                {jobStatus.status === 'Processing' && (
                  <div className="flex items-center gap-2.5 text-xs text-brand-muted">
                    <svg className="w-3.5 h-3.5 animate-spin text-[#8200f4]" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                    </svg>
                    AI is analysing patient data and composing the report…
                  </div>
                )}

                {/* Error details */}
                {jobStatus.status === 'Failed' && jobStatus.error && (
                  <div className="rounded-[9px] bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
                    {jobStatus.error}
                  </div>
                )}
              </>
            )}

            {!isTerminal && jobStatus && (
              <p className="text-[11px] text-brand-muted">
                Polling every {POLL_INTERVAL_MS / 1000} seconds — this page will redirect automatically when ready.
              </p>
            )}

            {jobStatus?.status === 'Failed' && (
              <Link to="/request" className="btn-brand inline-flex px-5 py-2.5 text-sm mt-2">
                Try again
              </Link>
            )}
          </div>
        </div>
      </div>
    </Layout>
  )
}

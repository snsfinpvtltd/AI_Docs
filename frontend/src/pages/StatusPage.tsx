import { type ReactElement, useEffect, useState, useRef } from 'react'
import { useParams, useNavigate, Link } from 'react-router-dom'
import { getJobStatus, getApiErrorMessage } from '../api/reports'
import type { JobStatusResult, ReportStatus } from '../types/api'
import Layout from '../components/Layout'

const POLL_INTERVAL_MS = 2000

// Stage labels for each progress band (shown when no live stageName is available)
const stageBands: { min: number; label: string }[] = [
  { min: 90, label: 'Finalising document' },
  { min: 70, label: 'Building document' },
  { min: 55, label: 'AI analysis complete' },
  { min: 30, label: 'Analysing patient data with AI' },
  { min: 15, label: 'Applying filters' },
  { min:  0, label: 'Loading data files' },
]

function getStageFallback(pct: number): string {
  return stageBands.find(b => pct >= b.min)?.label ?? 'Processing'
}

const statusColors: Record<ReportStatus, { dot: string; text: string; bg: string; ring: string }> = {
  Queued:     { dot: 'bg-amber-400',              text: 'text-amber-700',  bg: 'bg-amber-50',    ring: 'ring-amber-200'     },
  Processing: { dot: 'bg-[#0072ff] animate-pulse', text: 'text-[#0072ff]', bg: 'bg-[#0072ff]/5', ring: 'ring-[#0072ff]/20'  },
  Completed:  { dot: 'bg-[#12E58D]',               text: 'text-[#0a7a4c]', bg: 'bg-[#12E58D]/10',ring: 'ring-[#12E58D]/30'  },
  Failed:     { dot: 'bg-red-500',                  text: 'text-red-700',   bg: 'bg-red-50',      ring: 'ring-red-200'       },
}

const statusHeadline: Record<ReportStatus, string> = {
  Queued:     'Queued — waiting to start',
  Processing: 'Generating your AI-powered report',
  Completed:  'Report ready',
  Failed:     'Generation failed',
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
    return () => { if (intervalRef.current !== null) clearInterval(intervalRef.current) }
  }, [jobId, navigate])

  const isTerminal = jobStatus?.status === 'Completed' || jobStatus?.status === 'Failed'
  const cfg        = jobStatus ? statusColors[jobStatus.status] : null
  const pct        = jobStatus?.progressPercent ?? 0
  const stageLabel = jobStatus?.stageName
    ?? (jobStatus?.status === 'Processing' ? getStageFallback(pct) : '')

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
          {/* Header */}
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

            {/* Connecting spinner */}
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
                    {statusHeadline[jobStatus.status]}
                  </span>
                </div>

                {/* Progress bar + stage name */}
                {jobStatus.status === 'Processing' && (
                  <div>
                    <div className="flex items-center justify-between mb-2">
                      {/* Live stage label with spinner */}
                      <div className="flex items-center gap-2 min-w-0">
                        <svg className="w-3.5 h-3.5 animate-spin text-[#8200f4] flex-shrink-0" fill="none" viewBox="0 0 24 24">
                          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                        </svg>
                        <span className="text-xs text-brand-muted truncate">{stageLabel}</span>
                      </div>
                      <span className="text-xs font-semibold text-[#0072ff] flex-shrink-0 ml-3">{pct}%</span>
                    </div>

                    {/* Gradient progress bar */}
                    <div className="w-full bg-brand-surface rounded-full h-2.5 overflow-hidden">
                      <div
                        className="h-2.5 rounded-full transition-all duration-700 ease-out"
                        style={{
                          width: `${pct}%`,
                          backgroundImage: 'linear-gradient(90deg, #0072ff, #8200f4)',
                        }}
                      />
                    </div>

                    {/* Stage steps */}
                    <div className="mt-4 space-y-1.5">
                      {[
                        { label: 'Load data',        threshold: 10 },
                        { label: 'Apply filters',    threshold: 25 },
                        { label: 'Generate charts',  threshold: 40 },
                        { label: 'AI analysis',      threshold: 65 },
                        { label: 'Build document',   threshold: 80 },
                        { label: 'Upload & finalise',threshold: 95 },
                      ].map(({ label, threshold }) => {
                        const done    = pct >= threshold
                        const active  = !done && pct >= threshold - 25
                        return (
                          <div key={label} className="flex items-center gap-2.5">
                            {done ? (
                              <svg className="w-3.5 h-3.5 text-[#12E58D] flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                                <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                              </svg>
                            ) : active ? (
                              <svg className="w-3.5 h-3.5 animate-spin text-[#0072ff] flex-shrink-0" fill="none" viewBox="0 0 24 24">
                                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                              </svg>
                            ) : (
                              <div className="w-3.5 h-3.5 rounded-full border-2 border-slate-200 flex-shrink-0" />
                            )}
                            <span className={`text-xs ${done ? 'text-[#0a7a4c] font-medium' : active ? 'text-[#0072ff] font-medium' : 'text-brand-muted'}`}>
                              {label}
                            </span>
                          </div>
                        )
                      })}
                    </div>
                  </div>
                )}

                {/* Completed — 100% bar */}
                {jobStatus.status === 'Completed' && (
                  <div>
                    <div className="flex justify-between text-xs mb-1.5">
                      <span className="text-[#0a7a4c] font-medium">All stages complete</span>
                      <span className="text-[#0a7a4c] font-semibold">100%</span>
                    </div>
                    <div className="w-full bg-brand-surface rounded-full h-2.5 overflow-hidden">
                      <div className="h-2.5 w-full rounded-full bg-[#12E58D]" />
                    </div>
                  </div>
                )}

                {/* Error detail */}
                {jobStatus.status === 'Failed' && jobStatus.error && (
                  <div className="rounded-[9px] bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
                    {jobStatus.error}
                  </div>
                )}
              </>
            )}

            {!isTerminal && jobStatus && (
              <p className="text-[11px] text-brand-muted">
                Polling every {POLL_INTERVAL_MS / 1000} s — page redirects automatically when ready.
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

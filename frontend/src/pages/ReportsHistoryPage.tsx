import { type ReactElement, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import Layout from '../components/Layout'
import { getReportsHistory, getApiErrorMessage } from '../api/reports'
import type { ReportJobSummary } from '../types/api'

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

const templateLabels: Record<string, string> = {
  'patient-summary': 'Patient Summary',
  'outcome-data':    'Outcome Data',
  'full-report':     'Full Trial Report',
}

const formatLabels: Record<string, { label: string; color: string }> = {
  docx: { label: 'DOCX', color: 'text-[#0072ff] bg-[#0072ff]/10' },
  pdf:  { label: 'PDF',  color: 'text-[#ff6c5f] bg-[#ff6c5f]/10' },
  xlsx: { label: 'XLSX', color: 'text-[#0a7a4c] bg-[#12E58D]/10' },
}

function StatusBadge({ status }: { status: string }): ReactElement {
  const cfg: Record<string, string> = {
    Completed:  'bg-[#12E58D]/10 text-[#0a7a4c] ring-1 ring-[#12E58D]/30',
    Failed:     'bg-red-50 text-red-700 ring-1 ring-red-200',
    Processing: 'bg-[#0072ff]/10 text-[#0072ff] ring-1 ring-[#0072ff]/30',
    Queued:     'bg-amber-50 text-amber-700 ring-1 ring-amber-200',
  }
  const cls = cfg[status] ?? 'bg-brand-surface text-brand-charcoal ring-1 ring-brand-border'
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-semibold ${cls}`}>
      {status}
    </span>
  )
}

export default function ReportsHistoryPage(): ReactElement {
  const [reports, setReports] = useState<ReportJobSummary[]>([])
  const [error, setError]     = useState('')
  const [loading, setLoading] = useState(true)
  const [page, setPage]       = useState(1)
  const [hasMore, setHasMore] = useState(true)
  const pageSize = 20

  useEffect(() => {
    setLoading(true)
    getReportsHistory(page, pageSize)
      .then((data) => {
        setReports(data)
        setHasMore(data.length === pageSize)
      })
      .catch((err: unknown) => setError(getApiErrorMessage(err)))
      .finally(() => setLoading(false))
  }, [page])

  return (
    <Layout title="Reports History" subtitle="AI-generated clinical trial reports">
      <div className="mb-6 flex items-center justify-between">
        <p className="text-sm text-brand-muted">
          {reports.length > 0 ? `${reports.length} report${reports.length !== 1 ? 's' : ''} on this page` : 'All generated reports for your account'}
        </p>
        <Link to="/request" className="btn-brand px-4 py-2 text-sm gap-2">
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          New Report
        </Link>
      </div>

      {error && (
        <div className="mb-4 rounded-[9px] bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      <div className="bg-white rounded-[9px] shadow-card border border-brand-border overflow-hidden">
        {loading ? (
          <div className="animate-pulse divide-y divide-brand-border">
            {[...Array(5)].map((_, i) => (
              <div key={i} className="px-6 py-4 flex gap-4">
                <div className="h-4 bg-brand-surface rounded w-1/4" />
                <div className="h-4 bg-brand-surface rounded w-1/4" />
                <div className="h-4 bg-brand-surface rounded w-1/6" />
              </div>
            ))}
          </div>
        ) : reports.length === 0 ? (
          <div className="px-6 py-16 text-center">
            <div className="w-14 h-14 mx-auto mb-4 rounded-full bg-brand-surface flex items-center justify-center">
              <svg className="w-7 h-7 text-brand-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                  d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
            </div>
            <p className="text-brand-charcoal font-semibold font-heading mb-1">No reports generated yet</p>
            <p className="text-brand-muted text-sm mb-4">Upload patient data and generate your first AI-powered report.</p>
            <Link to="/request" className="text-[#1EB5C7] hover:underline text-sm font-medium">
              Generate your first report →
            </Link>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-brand-surface border-b border-brand-border">
                <tr className="text-left text-[11px] font-semibold text-brand-muted uppercase tracking-wider">
                  <th className="px-6 py-3">Job ID</th>
                  <th className="px-6 py-3">Template</th>
                  <th className="px-6 py-3">Format</th>
                  <th className="px-6 py-3">Patient Rows</th>
                  <th className="px-6 py-3">Status</th>
                  <th className="px-6 py-3">AI Prompt</th>
                  <th className="px-6 py-3">Generated At</th>
                  <th className="px-6 py-3">Download</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-brand-border">
                {reports.map((r) => {
                  const fmtKey = r.outputFormat.toLowerCase()
                  const fmtCfg = formatLabels[fmtKey] ?? { label: r.outputFormat.toUpperCase(), color: 'text-brand-charcoal bg-brand-surface' }
                  return (
                    <tr key={r.id} className="hover:bg-brand-surface transition-colors">
                      <td className="px-6 py-4">
                        <span className="font-mono text-[10px] text-brand-muted bg-brand-surface border border-brand-border px-2 py-1 rounded-[5px]">
                          {r.id.slice(0, 8)}…
                        </span>
                      </td>
                      <td className="px-6 py-4 text-[#222222] font-medium text-xs">
                        {templateLabels[r.templateType] ?? r.templateType}
                      </td>
                      <td className="px-6 py-4">
                        <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold ${fmtCfg.color}`}>
                          {fmtCfg.label}
                        </span>
                      </td>
                      <td className="px-6 py-4">
                        <span className="font-semibold text-[#222222]">{r.rowCount.toLocaleString()}</span>
                      </td>
                      <td className="px-6 py-4">
                        <StatusBadge status={r.status} />
                      </td>
                      <td className="px-6 py-4">
                        {r.promptText ? (
                          <span
                            className="text-brand-charcoal text-xs truncate block max-w-[160px]"
                            title={r.promptText}
                          >
                            {r.promptText}
                          </span>
                        ) : (
                          <span className="text-brand-muted text-xs">—</span>
                        )}
                      </td>
                      <td className="px-6 py-4 text-brand-muted text-xs whitespace-nowrap">
                        {formatDate(r.createdAt)}
                      </td>
                      <td className="px-6 py-4">
                        {r.downloadUrl ? (
                          <a
                            href={r.downloadUrl}
                            download
                            className="inline-flex items-center gap-1.5 text-[#1EB5C7] hover:text-[#12b3c5] font-semibold text-xs whitespace-nowrap transition-colors"
                          >
                            <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                            </svg>
                            Download
                          </a>
                        ) : (
                          <span className="text-brand-muted text-xs">—</span>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination */}
        {!loading && reports.length > 0 && (
          <div className="flex items-center justify-between px-6 py-4 border-t border-brand-border bg-brand-surface">
            <p className="text-xs text-brand-muted">Page {page}</p>
            <div className="flex gap-2">
              <button
                type="button"
                disabled={page <= 1}
                onClick={() => setPage(p => p - 1)}
                className="px-3 py-1.5 text-xs font-medium border border-brand-border rounded-[7px] text-brand-charcoal bg-white hover:bg-brand-surface disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                ← Previous
              </button>
              <button
                type="button"
                disabled={!hasMore}
                onClick={() => setPage(p => p + 1)}
                className="px-3 py-1.5 text-xs font-medium border border-brand-border rounded-[7px] text-brand-charcoal bg-white hover:bg-brand-surface disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                Next →
              </button>
            </div>
          </div>
        )}
      </div>
    </Layout>
  )
}

import { type ReactElement, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import Layout from '../components/Layout'
import { getUploads, getApiErrorMessage } from '../api/reports'
import type { UploadSummary } from '../types/api'

function formatBytes(bytes: number): string {
  if (bytes < 1024)        return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

export default function UploadsHistoryPage(): ReactElement {
  const [uploads, setUploads] = useState<UploadSummary[]>([])
  const [error, setError]     = useState('')
  const [loading, setLoading] = useState(true)
  const [page, setPage]       = useState(1)
  const [hasMore, setHasMore] = useState(true)
  const pageSize = 20

  useEffect(() => {
    setLoading(true)
    getUploads(page, pageSize)
      .then((data) => {
        setUploads(data)
        setHasMore(data.length === pageSize)
      })
      .catch((err: unknown) => setError(getApiErrorMessage(err)))
      .finally(() => setLoading(false))
  }, [page])

  return (
    <Layout title="Upload History" subtitle="Excel files uploaded for report generation">
      <div className="mb-6 flex items-center justify-between">
        <p className="text-sm text-brand-muted">
          {uploads.length > 0 ? `${uploads.length} file${uploads.length !== 1 ? 's' : ''} on this page` : 'All uploaded patient data files'}
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
                <div className="h-4 bg-brand-surface rounded w-1/3" />
                <div className="h-4 bg-brand-surface rounded w-1/6" />
                <div className="h-4 bg-brand-surface rounded w-1/6" />
              </div>
            ))}
          </div>
        ) : uploads.length === 0 ? (
          <div className="px-6 py-16 text-center">
            <div className="w-14 h-14 mx-auto mb-4 rounded-full bg-brand-surface flex items-center justify-center">
              <svg className="w-7 h-7 text-brand-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                  d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
              </svg>
            </div>
            <p className="text-brand-charcoal font-semibold font-heading mb-1">No uploads yet</p>
            <p className="text-brand-muted text-sm mb-4">Upload an Excel file to get started with report generation.</p>
            <Link to="/request" className="text-[#1EB5C7] hover:underline text-sm font-medium">
              Upload your first file →
            </Link>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-brand-surface border-b border-brand-border">
                <tr className="text-left text-[11px] font-semibold text-brand-muted uppercase tracking-wider">
                  <th className="px-6 py-3">File Name</th>
                  <th className="px-6 py-3">Patient Rows</th>
                  <th className="px-6 py-3">Columns</th>
                  <th className="px-6 py-3">Size</th>
                  <th className="px-6 py-3">Uploaded At</th>
                  <th className="px-6 py-3">Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-brand-border">
                {uploads.map((u) => (
                  <tr key={u.id} className="hover:bg-brand-surface transition-colors">
                    <td className="px-6 py-4">
                      <div className="flex items-center gap-2.5">
                        <div className="w-8 h-8 rounded-[7px] bg-[#12E58D]/10 flex items-center justify-center flex-shrink-0">
                          <svg className="w-4 h-4 text-[#0a7a4c]" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                              d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                          </svg>
                        </div>
                        <div>
                          <p className="font-semibold text-[#222222]">{u.fileName}</p>
                          <p className="text-[10px] text-brand-muted font-mono mt-0.5">{u.id.slice(0, 12)}…</p>
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4">
                      <span className="font-semibold text-[#222222]">{u.rowCount.toLocaleString()}</span>
                      <span className="text-brand-muted text-xs ml-1">rows</span>
                    </td>
                    <td className="px-6 py-4">
                      {u.headers.length > 0 ? (
                        <div className="flex flex-wrap gap-1 max-w-xs">
                          {u.headers.slice(0, 4).map((h) => (
                            <span key={h} className="px-1.5 py-0.5 bg-brand-surface border border-brand-border text-brand-charcoal rounded text-[10px] font-medium">
                              {h}
                            </span>
                          ))}
                          {u.headers.length > 4 && (
                            <span className="px-1.5 py-0.5 bg-brand-surface border border-brand-border text-brand-muted rounded text-[10px]">
                              +{u.headers.length - 4}
                            </span>
                          )}
                        </div>
                      ) : (
                        <span className="text-brand-muted text-xs">—</span>
                      )}
                    </td>
                    <td className="px-6 py-4 text-brand-muted text-sm">{formatBytes(u.fileSizeBytes)}</td>
                    <td className="px-6 py-4 text-brand-muted text-xs whitespace-nowrap">{formatDate(u.uploadedAt)}</td>
                    <td className="px-6 py-4">
                      <Link
                        to="/request"
                        className="text-[#1EB5C7] hover:text-[#12b3c5] text-xs font-semibold whitespace-nowrap transition-colors"
                      >
                        Use for report →
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination */}
        {!loading && uploads.length > 0 && (
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

import { type ReactElement, useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useDropzone } from 'react-dropzone'
import Layout from '../components/Layout'
import { uploadFile, getUploads, getApiErrorMessage } from '../api/reports'
import type { UploadSummary } from '../types/api'

type PagePhase =
  | { phase: 'idle' }
  | { phase: 'uploading'; fileName: string }
  | { phase: 'duplicate'; fileName: string; file: File; existing: UploadSummary }
  | { phase: 'success'; fileName: string; blobName: string; rowCount: number; columns: number }
  | { phase: 'error'; fileName: string; message: string }

function formatDate(iso: string | Date): string {
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

function formatBytes(bytes: number): string {
  if (bytes < 1024)        return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

export default function UploadDataPage(): ReactElement {
  const navigate = useNavigate()
  const [state, setState] = useState<PagePhase>({ phase: 'idle' })
  const [recentUploads, setRecentUploads] = useState<UploadSummary[]>([])
  const [loadingRecent, setLoadingRecent] = useState(true)

  useEffect(() => {
    getUploads(1, 8)
      .then(setRecentUploads)
      .finally(() => setLoadingRecent(false))
  }, [state]) // refresh list whenever state changes (after successful upload)

  const handleFile = useCallback(async (file: File) => {
    setState({ phase: 'uploading', fileName: file.name })
    try {
      const response = await uploadFile(file)
      if (response.kind === 'duplicate') {
        setState({ phase: 'duplicate', fileName: file.name, file, existing: response.existingUpload })
      } else {
        setState({
          phase: 'success',
          fileName: file.name,
          blobName: response.blobName,
          rowCount: 0,
          columns: 0,
        })
      }
    } catch (err) {
      setState({ phase: 'error', fileName: file.name, message: getApiErrorMessage(err) })
    }
  }, [])

  const handleForceUpload = async () => {
    if (state.phase !== 'duplicate') return
    const { file } = state
    setState({ phase: 'uploading', fileName: file.name })
    try {
      const response = await uploadFile(file, true)
      if (response.kind === 'success') {
        setState({ phase: 'success', fileName: file.name, blobName: response.blobName, rowCount: 0, columns: 0 })
      }
    } catch (err) {
      setState({ phase: 'error', fileName: file.name, message: getApiErrorMessage(err) })
    }
  }

  const reset = () => setState({ phase: 'idle' })

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop: (accepted) => { if (accepted[0]) void handleFile(accepted[0]) },
    accept: { 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': ['.xlsx'] },
    maxFiles: 1,
    maxSize: 10 * 1024 * 1024,
    disabled: state.phase === 'uploading' || state.phase === 'success',
  })

  return (
    <Layout title="Upload Patient Data" subtitle="Upload an Excel file to start generating AI reports">
      <div className="max-w-2xl mx-auto space-y-5">

        {/* ── Dropzone ────────────────────────────────────────────────────── */}
        {(state.phase === 'idle' || state.phase === 'error') && (
          <div
            {...getRootProps()}
            className={[
              'rounded-[9px] border-2 border-dashed p-10 text-center cursor-pointer transition-all',
              isDragActive
                ? 'border-[#0072ff] bg-[#0072ff]/5'
                : state.phase === 'error'
                  ? 'border-red-300 bg-red-50'
                  : 'border-brand-border hover:border-[#0072ff]/50 hover:bg-[#0072ff]/[0.02]',
            ].join(' ')}
          >
            <input {...getInputProps()} />
            <div className="flex flex-col items-center gap-3">
              <div className="w-14 h-14 rounded-full bg-brand-surface border border-brand-border flex items-center justify-center">
                <svg className={`w-7 h-7 ${isDragActive ? 'text-[#0072ff]' : 'text-brand-muted'} transition-colors`}
                  fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                    d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
                </svg>
              </div>
              {state.phase === 'error' ? (
                <>
                  <p className="text-sm font-semibold text-red-700">Upload failed — {state.message}</p>
                  <p className="text-xs text-brand-muted">Click or drop to try again with a different file.</p>
                </>
              ) : (
                <>
                  <div>
                    <p className="text-sm font-semibold text-[#222222]">
                      {isDragActive ? 'Drop your Excel file here' : 'Drag & drop an Excel file, or click to browse'}
                    </p>
                    <p className="text-xs text-brand-muted mt-1">.xlsx only · max 10 MB · synthetic / anonymised data only</p>
                  </div>
                </>
              )}
            </div>
          </div>
        )}

        {/* ── Uploading ────────────────────────────────────────────────────── */}
        {state.phase === 'uploading' && (
          <div className="rounded-[9px] border border-brand-border bg-white shadow-card p-8 flex flex-col items-center gap-4">
            <div className="w-14 h-14 rounded-full bg-[#0072ff]/10 flex items-center justify-center">
              <svg className="w-7 h-7 animate-spin text-[#0072ff]" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
            </div>
            <div className="text-center">
              <p className="text-sm font-semibold text-[#222222]">Uploading &amp; checking for duplicates…</p>
              <p className="text-xs text-brand-muted mt-1 truncate max-w-sm">{state.fileName}</p>
            </div>
          </div>
        )}

        {/* ── Duplicate detected ───────────────────────────────────────────── */}
        {state.phase === 'duplicate' && (
          <div className="rounded-[9px] border border-amber-200 bg-amber-50 shadow-card overflow-hidden">
            <div className="px-5 py-4 border-b border-amber-200 flex items-center gap-3">
              <div className="w-9 h-9 rounded-full bg-amber-100 flex items-center justify-center flex-shrink-0">
                <svg className="w-5 h-5 text-amber-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                    d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                </svg>
              </div>
              <div>
                <p className="text-sm font-bold text-amber-800 font-heading">Duplicate file detected</p>
                <p className="text-xs text-amber-700 mt-0.5">
                  This exact file was already uploaded — same content (SHA-256 match).
                </p>
              </div>
            </div>

            <div className="px-5 py-4">
              {/* Existing upload info */}
              <div className="rounded-[7px] bg-white border border-amber-200 p-4 mb-4">
                <p className="text-[11px] font-semibold text-brand-muted uppercase tracking-wider mb-2">Existing upload</p>
                <div className="flex items-start gap-3">
                  <div className="w-8 h-8 rounded-lg bg-[#12E58D]/10 flex items-center justify-center flex-shrink-0">
                    <svg className="w-4 h-4 text-[#1BA76D]" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                        d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                    </svg>
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-semibold text-[#222222] truncate">{state.existing.fileName}</p>
                    <p className="text-xs text-brand-muted mt-0.5">
                      {state.existing.rowCount.toLocaleString()} rows &nbsp;·&nbsp;
                      {formatBytes(state.existing.fileSizeBytes)} &nbsp;·&nbsp;
                      Uploaded {formatDate(state.existing.uploadedAt)}
                    </p>
                  </div>
                </div>
              </div>

              <p className="text-xs text-amber-800 mb-4">
                You can use the existing file to generate a report, or upload this file again as a new record.
              </p>

              <div className="flex flex-col sm:flex-row gap-3">
                <button
                  type="button"
                  onClick={() => navigate('/request', { state: { blobName: state.existing.blobName, fileName: state.existing.fileName } })}
                  className="btn-brand flex-1 py-2.5 text-sm"
                >
                  Use existing file
                </button>
                <button
                  type="button"
                  onClick={() => void handleForceUpload()}
                  className="flex-1 py-2.5 text-sm font-semibold border border-amber-300 bg-white text-amber-800 rounded-[9px] hover:bg-amber-50 transition-colors"
                >
                  Upload as new copy
                </button>
                <button
                  type="button"
                  onClick={reset}
                  className="flex-1 py-2.5 text-sm font-semibold border border-brand-border bg-white text-brand-charcoal rounded-[9px] hover:bg-brand-surface transition-colors"
                >
                  Cancel
                </button>
              </div>
            </div>
          </div>
        )}

        {/* ── Success ──────────────────────────────────────────────────────── */}
        {state.phase === 'success' && (
          <div className="rounded-[9px] border border-[#12E58D]/30 bg-[#12E58D]/5 shadow-card overflow-hidden">
            <div className="px-5 py-4 border-b border-[#12E58D]/20 flex items-center gap-3">
              <div className="w-9 h-9 rounded-full bg-[#12E58D]/15 flex items-center justify-center flex-shrink-0">
                <svg className="w-5 h-5 text-[#0a7a4c]" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                </svg>
              </div>
              <div>
                <p className="text-sm font-bold text-[#0a7a4c] font-heading">Upload successful</p>
                <p className="text-xs text-[#0a7a4c]/70 mt-0.5 truncate max-w-sm">{state.fileName}</p>
              </div>
            </div>
            <div className="px-5 py-4">
              <p className="text-xs text-brand-muted mb-4">
                Your file is ready. You can now generate an AI-powered report from this data.
              </p>
              <div className="flex flex-col sm:flex-row gap-3">
                <button
                  type="button"
                  onClick={() => navigate('/request', { state: { blobName: state.blobName, fileName: state.fileName } })}
                  className="btn-brand flex-1 py-2.5 text-sm gap-2"
                >
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                      d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                  </svg>
                  Generate Report
                </button>
                <button
                  type="button"
                  onClick={reset}
                  className="flex-1 py-2.5 text-sm font-semibold border border-brand-border bg-white text-brand-charcoal rounded-[9px] hover:bg-brand-surface transition-colors"
                >
                  Upload another file
                </button>
              </div>
            </div>
          </div>
        )}

        {/* ── Info card ────────────────────────────────────────────────────── */}
        <div className="rounded-[9px] bg-white border border-brand-border shadow-card p-4 flex items-start gap-3">
          <svg className="w-4 h-4 text-[#1EB5C7] flex-shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
              d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
          </svg>
          <p className="text-xs text-brand-charcoal leading-relaxed">
            This platform processes <strong>synthetic and anonymised</strong> clinical trial data only.
            Files are checked for duplicates by SHA-256 hash before storage.
            Uploaded files are retained for 24 hours, then automatically deleted.
          </p>
        </div>

        {/* ── Recent uploads ───────────────────────────────────────────────── */}
        <div className="bg-white rounded-[9px] shadow-card border border-brand-border overflow-hidden">
          <div className="flex items-center justify-between px-5 py-4 border-b border-brand-border">
            <h3 className="text-sm font-bold text-[#222222] font-heading">Recent Uploads</h3>
            <Link to="/uploads" className="text-xs font-semibold text-[#1EB5C7] hover:opacity-75 transition-opacity">
              View all →
            </Link>
          </div>

          {loadingRecent ? (
            <div className="animate-pulse divide-y divide-brand-border">
              {[...Array(3)].map((_, i) => (
                <div key={i} className="px-5 py-3.5 flex gap-4">
                  <div className="h-4 bg-brand-surface rounded flex-1" />
                  <div className="h-4 bg-brand-surface rounded w-20" />
                </div>
              ))}
            </div>
          ) : recentUploads.length === 0 ? (
            <div className="px-5 py-10 text-center text-sm text-brand-muted">
              No uploads yet — drop a file above to get started.
            </div>
          ) : (
            <div className="divide-y divide-brand-border">
              {recentUploads.map((u) => (
                <div key={u.id} className="flex items-center gap-4 px-5 py-3.5 hover:bg-brand-surface transition-colors">
                  <div className="w-8 h-8 rounded-lg bg-[#12E58D]/10 flex items-center justify-center flex-shrink-0">
                    <svg className="w-4 h-4 text-[#1BA76D]" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                        d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                    </svg>
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-semibold text-[#222222] truncate">{u.fileName}</p>
                    <p className="text-xs text-brand-muted">
                      {u.rowCount.toLocaleString()} rows &nbsp;·&nbsp;
                      {formatBytes(u.fileSizeBytes)} &nbsp;·&nbsp;
                      {formatDate(u.uploadedAt)}
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={() => navigate('/request', { state: { blobName: u.blobName, fileName: u.fileName } })}
                    className="text-xs font-semibold text-[#1EB5C7] hover:opacity-75 transition-opacity whitespace-nowrap flex-shrink-0"
                  >
                    Use for report →
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>

      </div>
    </Layout>
  )
}

import { type ReactElement, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import Layout from '../components/Layout'
import { getCustomTemplates, deleteCustomTemplate, getApiErrorMessage } from '../api/reports'
import type { CustomTemplateSummary } from '../types/api'

const SECTION_COUNT_LABEL = (n: number) => `${n} section${n !== 1 ? 's' : ''}`

const FORMAT_BADGE: Record<string, string> = {
  Docx:  'bg-blue-50 text-blue-700 border-blue-200',
  Pdf:   'bg-red-50 text-red-700 border-red-200',
  Excel: 'bg-green-50 text-green-700 border-green-200',
}

function EmptyState({ onNew }: { onNew: () => void }): ReactElement {
  return (
    <div className="flex flex-col items-center justify-center py-24 text-center">
      <div className="w-16 h-16 rounded-2xl bg-[#0072ff]/8 flex items-center justify-center mb-5">
        <svg className="w-8 h-8 text-[#0072ff]" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
            d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
        </svg>
      </div>
      <p className="text-base font-semibold text-[#222222] mb-1">No custom templates yet</p>
      <p className="text-sm text-brand-muted mb-6 max-w-xs">
        Build a template to control exactly which sections appear in your AI-generated reports
        and add custom instructions per section.
      </p>
      <button type="button" onClick={onNew} className="btn-brand px-5 py-2.5 text-sm">
        Create your first template
      </button>
    </div>
  )
}

export default function ReportTemplatesPage(): ReactElement {
  const navigate = useNavigate()
  const [templates, setTemplates]   = useState<CustomTemplateSummary[]>([])
  const [loading, setLoading]       = useState(true)
  const [error, setError]           = useState('')
  const [deletingId, setDeletingId] = useState<string | null>(null)

  function load(): void {
    setLoading(true)
    getCustomTemplates()
      .then(setTemplates)
      .catch((e: unknown) => setError(getApiErrorMessage(e)))
      .finally(() => setLoading(false))
  }

  useEffect(load, [])

  async function handleDelete(id: string, name: string): Promise<void> {
    if (!confirm(`Delete template "${name}"? This cannot be undone.`)) return
    setDeletingId(id)
    try {
      await deleteCustomTemplate(id)
      setTemplates((prev) => prev.filter((t) => t.id !== id))
    } catch (e) {
      alert(getApiErrorMessage(e))
    } finally {
      setDeletingId(null)
    }
  }

  return (
    <Layout title="Report Templates" subtitle="Build and manage custom AI report templates">
      <div className="max-w-5xl mx-auto">

        {/* Header actions */}
        <div className="flex items-center justify-between mb-6">
          <p className="text-sm text-brand-muted">
            Templates let you control section order, headings and per-section AI instructions.
          </p>
          <button
            type="button"
            onClick={() => navigate('/templates/new')}
            className="btn-brand px-4 py-2 text-sm flex items-center gap-2"
          >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
            </svg>
            New template
          </button>
        </div>

        {/* Content */}
        {loading && (
          <div className="flex items-center justify-center py-20 text-brand-muted gap-3">
            <svg className="w-5 h-5 animate-spin text-[#0072ff]" fill="none" viewBox="0 0 24 24">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
            </svg>
            <span className="text-sm">Loading templates…</span>
          </div>
        )}

        {!loading && error && (
          <div className="rounded-[9px] bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
            {error}
          </div>
        )}

        {!loading && !error && templates.length === 0 && (
          <EmptyState onNew={() => navigate('/templates/new')} />
        )}

        {!loading && !error && templates.length > 0 && (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {templates.map((t) => (
              <div
                key={t.id}
                className="relative flex flex-col rounded-[12px] bg-white border border-brand-border shadow-card p-5 hover:shadow-md transition-shadow"
              >
                {/* Public badge */}
                {t.isPublic && (
                  <span className="absolute top-3 right-3 text-[10px] font-semibold px-2 py-0.5 rounded-full bg-purple-50 text-purple-700 border border-purple-200">
                    Shared
                  </span>
                )}

                {/* Icon + name */}
                <div className="flex items-start gap-3 mb-3">
                  <div className="w-9 h-9 rounded-[9px] bg-[#0072ff]/8 flex items-center justify-center flex-shrink-0">
                    <svg className="w-4.5 h-4.5 text-[#0072ff]" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                        d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                    </svg>
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="font-semibold text-sm text-[#222222] truncate">{t.name}</p>
                    {t.description && (
                      <p className="text-xs text-brand-muted mt-0.5 line-clamp-2">{t.description}</p>
                    )}
                  </div>
                </div>

                {/* Metadata chips */}
                <div className="flex flex-wrap gap-1.5 mb-4">
                  <span className="text-[10px] px-2 py-0.5 rounded-full bg-slate-50 text-slate-600 border border-slate-200">
                    {SECTION_COUNT_LABEL(t.sectionCount)}
                  </span>
                  {t.defaultOutputFormat && (
                    <span className={`text-[10px] px-2 py-0.5 rounded-full border ${FORMAT_BADGE[t.defaultOutputFormat] ?? 'bg-slate-50 text-slate-600 border-slate-200'}`}>
                      {t.defaultOutputFormat}
                    </span>
                  )}
                  <span className="text-[10px] px-2 py-0.5 rounded-full bg-slate-50 text-slate-500 border border-slate-200">
                    {new Date(t.updatedAt).toLocaleDateString()}
                  </span>
                </div>

                {/* Actions */}
                <div className="mt-auto flex gap-2">
                  <button
                    type="button"
                    onClick={() => navigate('/request', { state: { customTemplateId: t.id, customTemplateName: t.name } })}
                    className="flex-1 text-xs font-semibold py-2 rounded-[7px] bg-brand-gradient text-white hover:opacity-90 transition-opacity"
                  >
                    Use in report
                  </button>
                  {t.isOwner && (
                    <>
                      <button
                        type="button"
                        onClick={() => navigate(`/templates/${t.id}/edit`)}
                        className="px-3 py-2 rounded-[7px] border border-brand-border text-xs font-semibold text-brand-charcoal hover:bg-slate-50 transition-colors"
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        onClick={() => void handleDelete(t.id, t.name)}
                        disabled={deletingId === t.id}
                        className="px-3 py-2 rounded-[7px] border border-red-200 text-xs font-semibold text-red-600 hover:bg-red-50 transition-colors disabled:opacity-50"
                      >
                        {deletingId === t.id ? '…' : 'Del'}
                      </button>
                    </>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </Layout>
  )
}

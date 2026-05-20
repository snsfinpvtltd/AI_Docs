import { type ReactElement, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import Layout from '../components/Layout'
import {
  createCustomTemplate,
  updateCustomTemplate,
  getCustomTemplate,
  getApiErrorMessage,
} from '../api/reports'
import type { SectionType, TemplateSection } from '../types/api'

// ── Section palette definition ────────────────────────────────────────────────

interface SectionMeta {
  type: SectionType
  defaultHeading: string
  description: string
  color: string
  icon: ReactElement
}

const SECTION_META: SectionMeta[] = [
  {
    type: 'ExecutiveSummary',
    defaultHeading: 'Executive Summary',
    description: 'High-level overview of cohort, status, and primary outcome',
    color: 'blue',
    icon: (
      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
      </svg>
    ),
  },
  {
    type: 'KeyFindings',
    defaultHeading: 'Key Findings',
    description: 'Bullet-list of 3–6 critical clinical insights',
    color: 'indigo',
    icon: (
      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4" />
      </svg>
    ),
  },
  {
    type: 'DataOverview',
    defaultHeading: 'Data Overview',
    description: 'Column completeness, date ranges, data quality notes',
    color: 'cyan',
    icon: (
      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M3 10h18M3 14h18m-9-4v8m-7 0h14a2 2 0 002-2V8a2 2 0 00-2-2H5a2 2 0 00-2 2v8a2 2 0 002 2z" />
      </svg>
    ),
  },
  {
    type: 'PatientAnalysis',
    defaultHeading: 'Patient Analysis',
    description: 'Demographics, treatment response, subgroup observations',
    color: 'violet',
    icon: (
      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0z" />
      </svg>
    ),
  },
  {
    type: 'AdverseEvents',
    defaultHeading: 'Adverse Events',
    description: 'AE frequency, severity patterns, safety signals',
    color: 'rose',
    icon: (
      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
      </svg>
    ),
  },
  {
    type: 'StatisticalInsights',
    defaultHeading: 'Statistical Insights',
    description: 'Distributions, ranges, means, key trends',
    color: 'amber',
    icon: (
      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M7 12l3-3 3 3 4-4M8 21l4-4 4 4M3 4h18M4 4h16v12a1 1 0 01-1 1H5a1 1 0 01-1-1V4z" />
      </svg>
    ),
  },
  {
    type: 'Recommendations',
    defaultHeading: 'Recommendations',
    description: 'Clinical and operational follow-up actions',
    color: 'emerald',
    icon: (
      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z" />
      </svg>
    ),
  },
  {
    type: 'Limitations',
    defaultHeading: 'Limitations',
    description: 'Data gaps, truncation notes, confidence caveats',
    color: 'slate',
    icon: (
      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
    ),
  },
  {
    type: 'Charts',
    defaultHeading: 'Data Visualisations',
    description: 'Auto-generated bar/pie charts from the dataset',
    color: 'teal',
    icon: (
      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M11 3.055A9.001 9.001 0 1020.945 13H11V3.055z" />
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M20.488 9H15V3.512A9.025 9.025 0 0120.488 9z" />
      </svg>
    ),
  },
  {
    type: 'DataTable',
    defaultHeading: 'Appendix — Raw Patient Data',
    description: 'Full filtered data table appended to the report',
    color: 'gray',
    icon: (
      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M3 10h18M3 6h18M3 14h18M3 18h18" />
      </svg>
    ),
  },
]

const META_BY_TYPE = Object.fromEntries(SECTION_META.map((m) => [m.type, m])) as Record<SectionType, SectionMeta>

const COLOR_CLASSES: Record<string, { chip: string; badge: string }> = {
  blue:   { chip: 'bg-blue-50 text-blue-700 border-blue-200',   badge: 'bg-blue-100 text-blue-800' },
  indigo: { chip: 'bg-indigo-50 text-indigo-700 border-indigo-200', badge: 'bg-indigo-100 text-indigo-800' },
  cyan:   { chip: 'bg-cyan-50 text-cyan-700 border-cyan-200',   badge: 'bg-cyan-100 text-cyan-800' },
  violet: { chip: 'bg-violet-50 text-violet-700 border-violet-200', badge: 'bg-violet-100 text-violet-800' },
  rose:   { chip: 'bg-rose-50 text-rose-700 border-rose-200',   badge: 'bg-rose-100 text-rose-800' },
  amber:  { chip: 'bg-amber-50 text-amber-700 border-amber-200', badge: 'bg-amber-100 text-amber-800' },
  emerald:{ chip: 'bg-emerald-50 text-emerald-700 border-emerald-200', badge: 'bg-emerald-100 text-emerald-800' },
  slate:  { chip: 'bg-slate-50 text-slate-700 border-slate-200', badge: 'bg-slate-100 text-slate-800' },
  teal:   { chip: 'bg-teal-50 text-teal-700 border-teal-200',   badge: 'bg-teal-100 text-teal-800' },
  gray:   { chip: 'bg-gray-50 text-gray-700 border-gray-200',   badge: 'bg-gray-100 text-gray-800' },
}

const DEFAULT_SECTIONS: TemplateSection[] = [
  { id: 's1',  type: 'ExecutiveSummary',   heading: 'Executive Summary',    isEnabled: true, order: 0 },
  { id: 's2',  type: 'KeyFindings',         heading: 'Key Findings',         isEnabled: true, order: 1 },
  { id: 's3',  type: 'DataOverview',        heading: 'Data Overview',        isEnabled: true, order: 2 },
  { id: 's4',  type: 'PatientAnalysis',     heading: 'Patient Analysis',     isEnabled: true, order: 3 },
  { id: 's5',  type: 'AdverseEvents',       heading: 'Adverse Events',       isEnabled: true, order: 4 },
  { id: 's6',  type: 'StatisticalInsights', heading: 'Statistical Insights', isEnabled: true, order: 5 },
  { id: 's7',  type: 'Recommendations',     heading: 'Recommendations',      isEnabled: true, order: 6 },
  { id: 's8',  type: 'Limitations',         heading: 'Limitations',          isEnabled: true, order: 7 },
  { id: 's9',  type: 'Charts',              heading: 'Data Visualisations',  isEnabled: true, order: 8 },
  { id: 's10', type: 'DataTable',           heading: 'Appendix — Raw Data',  isEnabled: true, order: 9 },
]

// ── Helpers ───────────────────────────────────────────────────────────────────

function newId(): string {
  return Math.random().toString(36).slice(2, 10)
}

function reorder(sections: TemplateSection[], fromIdx: number, toIdx: number): TemplateSection[] {
  const arr = [...sections]
  const [moved] = arr.splice(fromIdx, 1)
  arr.splice(toIdx, 0, moved)
  return arr.map((s, i) => ({ ...s, order: i }))
}

// ── Sub-components ────────────────────────────────────────────────────────────

interface SectionCardProps {
  section: TemplateSection
  index: number
  total: number
  onUpdate: (updated: TemplateSection) => void
  onRemove: () => void
  onMove: (dir: -1 | 1) => void
}

function SectionCard({ section, index, total, onUpdate, onRemove, onMove }: SectionCardProps): ReactElement {
  const meta   = META_BY_TYPE[section.type]
  const colors = COLOR_CLASSES[meta.color]
  const [expanded, setExpanded] = useState(false)

  return (
    <div className={`rounded-[10px] border transition-all ${section.isEnabled ? 'border-brand-border bg-white' : 'border-dashed border-slate-200 bg-slate-50/60 opacity-60'}`}>
      {/* Card header */}
      <div className="flex items-center gap-2.5 px-3.5 py-2.5">
        {/* Order number */}
        <span className="w-5 h-5 rounded-full bg-slate-100 text-slate-500 text-[10px] font-bold flex items-center justify-center flex-shrink-0">
          {index + 1}
        </span>

        {/* Type badge */}
        <span className={`flex items-center gap-1 text-[10px] font-semibold px-2 py-0.5 rounded-full border flex-shrink-0 ${colors.chip}`}>
          {meta.icon}
          {meta.type}
        </span>

        {/* Heading preview */}
        <span className="flex-1 text-sm text-[#222222] truncate font-medium">{section.heading}</span>

        {/* Controls */}
        <div className="flex items-center gap-1 flex-shrink-0">
          {/* Enable toggle */}
          <button
            type="button"
            onClick={() => onUpdate({ ...section, isEnabled: !section.isEnabled })}
            title={section.isEnabled ? 'Disable section' : 'Enable section'}
            className={`w-8 h-4.5 rounded-full relative transition-colors flex-shrink-0 ${section.isEnabled ? 'bg-[#0072ff]' : 'bg-slate-200'}`}
          >
            <span className={`absolute top-0.5 w-3 h-3 bg-white rounded-full shadow transition-transform ${section.isEnabled ? 'translate-x-4' : 'translate-x-0.5'}`} />
          </button>

          {/* Up / down */}
          <button type="button" disabled={index === 0} onClick={() => onMove(-1)}
            className="p-1 text-slate-400 hover:text-brand-charcoal disabled:opacity-20 transition-colors">
            <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M5 15l7-7 7 7" />
            </svg>
          </button>
          <button type="button" disabled={index === total - 1} onClick={() => onMove(1)}
            className="p-1 text-slate-400 hover:text-brand-charcoal disabled:opacity-20 transition-colors">
            <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M19 9l-7 7-7-7" />
            </svg>
          </button>

          {/* Expand / collapse */}
          <button type="button" onClick={() => setExpanded((e) => !e)}
            className="p-1 text-slate-400 hover:text-brand-charcoal transition-colors">
            <svg className={`w-3.5 h-3.5 transition-transform ${expanded ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M19 9l-7 7-7-7" />
            </svg>
          </button>

          {/* Remove */}
          <button type="button" onClick={onRemove}
            className="p-1 text-slate-300 hover:text-red-500 transition-colors">
            <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
      </div>

      {/* Expanded editor */}
      {expanded && (
        <div className="px-3.5 pb-3.5 space-y-3 border-t border-slate-100 pt-3">
          <div>
            <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wide mb-1">
              Section heading
            </label>
            <input
              type="text"
              value={section.heading}
              onChange={(e) => onUpdate({ ...section, heading: e.target.value })}
              className="w-full rounded-[7px] border border-brand-border px-3 py-1.5 text-sm text-[#222222] focus:border-[#0072ff] focus:outline-none focus:ring-2 focus:ring-[#0072ff]/20 transition-colors"
            />
          </div>
          {section.type !== 'DataTable' && section.type !== 'Charts' && (
            <div>
              <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wide mb-1">
                AI instruction <span className="font-normal text-slate-400">(optional — tells the AI what to focus on in this section)</span>
              </label>
              <textarea
                rows={2}
                value={section.aiInstruction ?? ''}
                onChange={(e) => onUpdate({ ...section, aiInstruction: e.target.value || undefined })}
                placeholder={`e.g. ${meta.description}`}
                className="w-full rounded-[7px] border border-brand-border px-3 py-1.5 text-sm text-[#222222] placeholder:text-slate-300 focus:border-[#0072ff] focus:outline-none focus:ring-2 focus:ring-[#0072ff]/20 transition-colors resize-none"
              />
            </div>
          )}
        </div>
      )}
    </div>
  )
}

// ── Form values ───────────────────────────────────────────────────────────────

interface FormValues {
  name: string
  description: string
  isPublic: boolean
  defaultOutputFormat: string
  defaultFilterPrompt: string
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function ReportTemplateBuilderPage(): ReactElement {
  const navigate  = useNavigate()
  const { id }    = useParams<{ id: string }>()
  const isEditing = Boolean(id)

  const [sections, setSections]   = useState<TemplateSection[]>(DEFAULT_SECTIONS)
  const [saving, setSaving]       = useState(false)
  const [loadError, setLoadError] = useState('')
  const [saveError, setSaveError] = useState('')

  const { register, handleSubmit, reset, formState: { errors } } = useForm<FormValues>({
    defaultValues: { name: '', description: '', isPublic: false, defaultOutputFormat: '', defaultFilterPrompt: '' },
  })

  // Load existing template when editing
  useEffect(() => {
    if (!id) return
    getCustomTemplate(id)
      .then((tpl) => {
        reset({
          name: tpl.name,
          description: tpl.description ?? '',
          isPublic: tpl.isPublic,
          defaultOutputFormat: tpl.defaultOutputFormat ?? '',
          defaultFilterPrompt: tpl.defaultFilterPrompt ?? '',
        })
        setSections(tpl.sections.slice().sort((a, b) => a.order - b.order))
      })
      .catch((e: unknown) => setLoadError(getApiErrorMessage(e)))
  }, [id, reset])

  // Section management helpers
  function addSection(type: SectionType): void {
    const meta = META_BY_TYPE[type]
    setSections((prev) => [
      ...prev,
      { id: newId(), type, heading: meta.defaultHeading, isEnabled: true, order: prev.length },
    ])
  }

  function updateSection(idx: number, updated: TemplateSection): void {
    setSections((prev) => prev.map((s, i) => (i === idx ? updated : s)))
  }

  function removeSection(idx: number): void {
    setSections((prev) => prev.filter((_, i) => i !== idx).map((s, i) => ({ ...s, order: i })))
  }

  function moveSection(idx: number, dir: -1 | 1): void {
    const toIdx = idx + dir
    if (toIdx < 0 || toIdx >= sections.length) return
    setSections((prev) => reorder(prev, idx, toIdx))
  }

  async function onSubmit(values: FormValues): Promise<void> {
    setSaveError('')
    setSaving(true)

    const payload = {
      name: values.name,
      description: values.description || undefined,
      isPublic: values.isPublic,
      defaultOutputFormat: values.defaultOutputFormat || undefined,
      defaultFilterPrompt: values.defaultFilterPrompt || undefined,
      sections: sections.map((s, i) => ({ ...s, order: i })),
    }

    try {
      if (isEditing && id) {
        await updateCustomTemplate(id, payload)
      } else {
        await createCustomTemplate(payload)
      }
      navigate('/templates')
    } catch (e) {
      setSaveError(getApiErrorMessage(e))
    } finally {
      setSaving(false)
    }
  }

  const usedTypes = new Set(sections.map((s) => s.type))

  return (
    <Layout
      title={isEditing ? 'Edit Template' : 'New Template'}
      subtitle="Design the sections and AI instructions for your report"
    >
      {loadError ? (
        <div className="max-w-2xl mx-auto mt-10 rounded-[9px] bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
          {loadError}
        </div>
      ) : (
        <form onSubmit={(e) => void handleSubmit(onSubmit)(e)} className="max-w-6xl mx-auto">
          <div className="flex gap-6 items-start">

            {/* ── Left panel: config + sections ──────────────────────────────── */}
            <div className="flex-1 min-w-0 space-y-5">

              {/* Template metadata */}
              <div className="rounded-[12px] bg-white border border-brand-border shadow-card p-5">
                <h2 className="text-sm font-bold text-[#222222] mb-4">Template details</h2>
                <div className="space-y-3">
                  <div>
                    <label className="block text-xs font-semibold text-brand-charcoal mb-1.5">
                      Template name <span className="text-red-500">*</span>
                    </label>
                    <input
                      type="text"
                      placeholder="e.g. Cardiology Trial — Adverse Event Focus"
                      className="w-full rounded-[7px] border border-brand-border px-3 py-2.5 text-sm text-[#222222] placeholder:text-brand-muted focus:border-[#0072ff] focus:outline-none focus:ring-2 focus:ring-[#0072ff]/20 transition-colors"
                      {...register('name', { required: 'Template name is required.' })}
                    />
                    {errors.name && <p className="mt-1 text-xs text-red-500">{errors.name.message}</p>}
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-brand-charcoal mb-1.5">
                      Description <span className="text-slate-400 font-normal">(optional)</span>
                    </label>
                    <textarea
                      rows={2}
                      placeholder="What is this template for?"
                      className="w-full rounded-[7px] border border-brand-border px-3 py-2 text-sm text-[#222222] placeholder:text-brand-muted focus:border-[#0072ff] focus:outline-none focus:ring-2 focus:ring-[#0072ff]/20 transition-colors resize-none"
                      {...register('description')}
                    />
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-semibold text-brand-charcoal mb-1.5">
                        Default output format
                      </label>
                      <select
                        className="w-full rounded-[7px] border border-brand-border px-3 py-2.5 text-sm text-[#222222] bg-white focus:border-[#0072ff] focus:outline-none focus:ring-2 focus:ring-[#0072ff]/20 transition-colors"
                        {...register('defaultOutputFormat')}
                      >
                        <option value="">No default</option>
                        <option value="Docx">Word (.docx)</option>
                        <option value="Pdf">PDF</option>
                        <option value="Excel">Excel (.xlsx)</option>
                      </select>
                    </div>
                    <div className="flex items-end pb-0.5">
                      <label className="flex items-center gap-2 cursor-pointer">
                        <input type="checkbox" className="w-4 h-4 rounded accent-[#0072ff]"
                          {...register('isPublic')} />
                        <span className="text-sm text-brand-charcoal">Share with team</span>
                      </label>
                    </div>
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-brand-charcoal mb-1.5">
                      Default filter prompt <span className="text-slate-400 font-normal">(pre-filled on New Report page)</span>
                    </label>
                    <input
                      type="text"
                      placeholder="e.g. age greater than 60 and outcome is Completed"
                      className="w-full rounded-[7px] border border-brand-border px-3 py-2.5 text-sm text-[#222222] placeholder:text-brand-muted focus:border-[#0072ff] focus:outline-none focus:ring-2 focus:ring-[#0072ff]/20 transition-colors"
                      {...register('defaultFilterPrompt')}
                    />
                  </div>
                </div>
              </div>

              {/* Section builder */}
              <div className="rounded-[12px] bg-white border border-brand-border shadow-card p-5">
                <div className="flex items-center justify-between mb-4">
                  <div>
                    <h2 className="text-sm font-bold text-[#222222]">Report sections</h2>
                    <p className="text-xs text-brand-muted mt-0.5">
                      Reorder, rename, and add per-section AI focus instructions
                    </p>
                  </div>
                  <span className="text-xs text-slate-500 bg-slate-50 border border-slate-200 px-2.5 py-1 rounded-full">
                    {sections.filter((s) => s.isEnabled).length} / {sections.length} enabled
                  </span>
                </div>

                {/* Section list */}
                <div className="space-y-2">
                  {sections.map((sec, idx) => (
                    <SectionCard
                      key={sec.id}
                      section={sec}
                      index={idx}
                      total={sections.length}
                      onUpdate={(u) => updateSection(idx, u)}
                      onRemove={() => removeSection(idx)}
                      onMove={(dir) => moveSection(idx, dir)}
                    />
                  ))}
                  {sections.length === 0 && (
                    <p className="text-sm text-center text-brand-muted py-8 border-2 border-dashed border-slate-200 rounded-[9px]">
                      Add sections from the palette →
                    </p>
                  )}
                </div>
              </div>

              {/* Save button */}
              {saveError && (
                <div className="rounded-[9px] bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
                  {saveError}
                </div>
              )}

              <button
                type="submit"
                disabled={saving}
                className="btn-brand w-full py-3 text-base disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {saving ? (
                  <span className="flex items-center justify-center gap-2">
                    <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                    </svg>
                    Saving…
                  </span>
                ) : (
                  isEditing ? 'Save changes' : 'Create template'
                )}
              </button>
            </div>

            {/* ── Right panel: section palette + live preview ─────────────────── */}
            <div className="w-72 flex-shrink-0 space-y-5 sticky top-0">

              {/* Palette */}
              <div className="rounded-[12px] bg-white border border-brand-border shadow-card p-4">
                <h3 className="text-xs font-bold text-[#222222] uppercase tracking-wide mb-3">Add section</h3>
                <div className="space-y-1">
                  {SECTION_META.map((meta) => {
                    const alreadyAdded = usedTypes.has(meta.type)
                    const colors = COLOR_CLASSES[meta.color]
                    return (
                      <button
                        key={meta.type}
                        type="button"
                        onClick={() => addSection(meta.type)}
                        className={`w-full flex items-center gap-2.5 px-3 py-2 rounded-[7px] text-left transition-all text-xs font-medium
                          ${alreadyAdded
                            ? 'opacity-40 cursor-default'
                            : 'hover:bg-slate-50 cursor-pointer'
                          }`}
                      >
                        <span className={`flex items-center justify-center w-5 h-5 rounded-full flex-shrink-0 ${colors.badge}`}>
                          {meta.icon}
                        </span>
                        <span className="flex-1 min-w-0">
                          <span className="text-[#222222] block truncate">{meta.defaultHeading}</span>
                        </span>
                        {alreadyAdded ? (
                          <svg className="w-3 h-3 text-slate-400 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clipRule="evenodd" />
                          </svg>
                        ) : (
                          <svg className="w-3 h-3 text-slate-400 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
                          </svg>
                        )}
                      </button>
                    )
                  })}
                </div>
              </div>

              {/* Live preview */}
              <div className="rounded-[12px] bg-white border border-brand-border shadow-card p-4">
                <h3 className="text-xs font-bold text-[#222222] uppercase tracking-wide mb-3">Document preview</h3>
                <div className="space-y-1">
                  <div className="h-2 bg-[#1e3a8a] rounded-sm" />
                  <div className="h-1 w-3/4 bg-slate-200 rounded-sm" />
                  <div className="h-1 bg-slate-100 rounded-sm" />
                  {sections
                    .filter((s) => s.isEnabled)
                    .sort((a, b) => a.order - b.order)
                    .map((sec) => {
                      const meta   = META_BY_TYPE[sec.type]
                      const colors = COLOR_CLASSES[meta.color]
                      return (
                        <div key={sec.id} className="flex items-center gap-1.5 pt-1">
                          <span className={`w-1 h-3 rounded-sm flex-shrink-0 ${colors.badge}`} />
                          <span className="text-[9px] font-semibold text-slate-700 uppercase tracking-wide truncate">
                            {sec.heading}
                          </span>
                          {sec.aiInstruction && (
                            <span className="text-[8px] text-[#0072ff] bg-[#0072ff]/8 px-1 rounded flex-shrink-0">AI</span>
                          )}
                        </div>
                      )
                    })}
                  {sections.filter((s) => !s.isEnabled).length > 0 && (
                    <p className="text-[9px] text-slate-400 pt-1">
                      + {sections.filter((s) => !s.isEnabled).length} disabled
                    </p>
                  )}
                </div>
              </div>
            </div>
          </div>
        </form>
      )}
    </Layout>
  )
}

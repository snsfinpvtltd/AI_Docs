import { type ReactElement, useState, useEffect, useRef } from 'react'
import { useForm, Controller } from 'react-hook-form'
import { useNavigate, useLocation } from 'react-router-dom'
import Layout from '../components/Layout'
import { getTemplates, getCustomTemplates, submitReport, getApiErrorMessage } from '../api/reports'
import type { OutputFormat, TemplateInfo, CustomTemplateSummary } from '../types/api'

interface LocationState {
  customTemplateId?: string
  customTemplateName?: string
}

interface FormValues {
  templateType: string
  outputFormat: OutputFormat
  trialId: string
  dateRangeFrom: string
  dateRangeTo: string
  freeTextPrompt: string
}

function SectionCard({ step, title, children, hasError = false }: {
  step: number
  title: string
  children: ReactElement | ReactElement[]
  hasError?: boolean
}): ReactElement {
  return (
    <section className={[
      'rounded-[9px] bg-white p-6 shadow-card border transition-colors',
      hasError ? 'border-red-400 ring-2 ring-red-100' : 'border-brand-border',
    ].join(' ')}>
      <div className="flex items-center gap-3 mb-5">
        <div className={[
          'w-7 h-7 rounded-full flex items-center justify-center flex-shrink-0 shadow-brand',
          hasError ? 'bg-red-500' : 'bg-brand-gradient',
        ].join(' ')}>
          <span className="text-white text-xs font-bold font-heading">{step}</span>
        </div>
        <h2 className="text-base font-bold text-[#222222] font-heading">{title}</h2>
      </div>
      {children}
    </section>
  )
}

export default function ReportRequestPage(): ReactElement {
  const navigate = useNavigate()
  const location = useLocation()
  const locationStateRef = useRef(location.state as LocationState | null)

  const [templates, setTemplates]           = useState<TemplateInfo[]>([])
  const [templatesError, setTemplatesError] = useState<string>('')
  const [customTemplates, setCustomTemplates]             = useState<CustomTemplateSummary[]>([])
  const [customTemplatesLoading, setCustomTemplatesLoading] = useState(true)
  const [templateMode, setTemplateMode]     = useState<'standard' | 'custom'>(
    locationStateRef.current?.customTemplateId ? 'custom' : 'standard',
  )
  const [customTemplateId, setCustomTemplateId]   = useState<string>(
    locationStateRef.current?.customTemplateId ?? '',
  )
  const [customTemplateError, setCustomTemplateError] = useState<string>('')
  const [submitError, setSubmitError]       = useState<string>('')
  const [isSubmitting, setIsSubmitting]     = useState(false)
  const [validationError, setValidationError] = useState<string>('')
  const pageTopRef = useRef<HTMLDivElement>(null)

  // scrollIntoView scrolls the <main> overflow container (not window) to the top
  useEffect(() => {
    if (validationError) {
      pageTopRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  }, [validationError])

  const {
    register,
    handleSubmit,
    control,
    watch,
    setValue,
    setError,
    formState: { errors },
  } = useForm<FormValues>({
    defaultValues: {
      templateType:   locationStateRef.current?.customTemplateId ? 'full-report' : '',
      outputFormat:   'Docx',
      trialId:        '',
      dateRangeFrom:  '',
      dateRangeTo:    '',
      freeTextPrompt: '',
    },
  })

  const selectedTemplate = watch('templateType')
  const selectedFormat   = watch('outputFormat')

  useEffect(() => {
    getTemplates()
      .then(setTemplates)
      .catch((err: unknown) => setTemplatesError(getApiErrorMessage(err)))
  }, [])

  useEffect(() => {
    getCustomTemplates()
      .then((data) => {
        setCustomTemplates(data)
        const preselectedId = locationStateRef.current?.customTemplateId
        if (preselectedId) {
          const t = data.find((x) => x.id === preselectedId)
          if (t) {
            if (t.defaultFilterPrompt) setValue('freeTextPrompt', t.defaultFilterPrompt)
            if (t.defaultOutputFormat) setValue('outputFormat', t.defaultOutputFormat as OutputFormat)
          }
        }
      })
      .catch(() => {/* custom templates are optional */})
      .finally(() => setCustomTemplatesLoading(false))
  }, [setValue])

  // Clear the error banner as soon as the user picks a valid template
  useEffect(() => {
    if (templateMode === 'standard' && selectedTemplate) {
      setValidationError('')
    }
  }, [selectedTemplate, templateMode])

  function selectCustomTemplate(t: CustomTemplateSummary): void {
    setCustomTemplateId(t.id)
    setCustomTemplateError('')
    setValidationError('')
    setValue('templateType', 'full-report')
    if (t.defaultFilterPrompt) setValue('freeTextPrompt', t.defaultFilterPrompt)
    if (t.defaultOutputFormat) setValue('outputFormat', t.defaultOutputFormat as OutputFormat)
  }

  function switchMode(mode: 'standard' | 'custom'): void {
    setTemplateMode(mode)
    if (mode === 'standard') {
      setCustomTemplateId('')
      setCustomTemplateError('')
      setValue('templateType', '')
    } else {
      setValue('templateType', 'full-report')
    }
  }

  async function onSubmit(values: FormValues): Promise<void> {
    if (templateMode === 'standard' && !values.templateType) {
      setError('templateType', { message: 'Please select a template.' })
      setValidationError('Please select a report template before generating.')
      return
    }
    if (templateMode === 'custom' && !customTemplateId) {
      setCustomTemplateError('Please select a custom template.')
      setValidationError('Please select a custom template before generating.')
      return
    }
    setValidationError('')
    setSubmitError('')
    setIsSubmitting(true)
    try {
      const result = await submitReport({
        templateType:     values.templateType,
        outputFormat:     values.outputFormat,
        trialId:          values.trialId || undefined,
        dateRangeFrom:    values.dateRangeFrom || undefined,
        dateRangeTo:      values.dateRangeTo || undefined,
        freeTextPrompt:   values.freeTextPrompt || undefined,
        customTemplateId: (templateMode === 'custom' && customTemplateId) ? customTemplateId : undefined,
      })
      if (result.isAsync) {
        navigate(`/status/${result.jobId}`)
      } else {
        navigate(`/download/${result.jobId}`, { state: { downloadUrl: result.downloadUrl } })
      }
    } catch (err) {
      setSubmitError(getApiErrorMessage(err))
    } finally {
      setIsSubmitting(false)
    }
  }

  const currentStdTemplate  = templates.find((t) => t.id === selectedTemplate)
  const availableFormats: OutputFormat[] = currentStdTemplate?.supportedFormats ?? ['Docx', 'Pdf', 'Excel']

  const formatLabels: Record<OutputFormat, string> = {
    Docx: 'Word (.docx)', Pdf: 'PDF', Excel: 'Excel (.xlsx)',
  }

  const formatIcons: Record<OutputFormat, ReactElement> = {
    Docx: (
      <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
          d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
      </svg>
    ),
    Pdf: (
      <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
          d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
      </svg>
    ),
    Excel: (
      <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
          d="M3 10h18M3 14h18m-9-4v8m-7 0h14a2 2 0 002-2V8a2 2 0 00-2-2H5a2 2 0 00-2 2v8a2 2 0 002 2z" />
      </svg>
    ),
  }

  return (
    <Layout title="New Report" subtitle="AI-powered clinical trial report generation">
      <div className="max-w-3xl mx-auto">
        {/* Scroll anchor — scrollIntoView targets the <main> overflow container */}
        <div ref={pageTopRef} />

        {/* Validation error banner — scrolled into view automatically */}
        {validationError && (
          <div
            role="alert"
            className="mb-4 rounded-[9px] bg-red-50 border border-red-300 px-4 py-3 flex items-start gap-3"
          >
            <svg className="w-4 h-4 text-red-500 flex-shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
              <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
            </svg>
            <p className="flex-1 text-sm font-semibold text-red-700">{validationError}</p>
            <button
              type="button"
              aria-label="Dismiss"
              onClick={() => setValidationError('')}
              className="text-red-400 hover:text-red-600 transition-colors flex-shrink-0"
            >
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        )}

        {/* Info banner */}
        <div className="mb-5 rounded-[9px] bg-[#0072ff]/5 border border-[#0072ff]/20 px-4 py-3 flex items-start gap-3">
          <svg className="w-4 h-4 text-[#0072ff] flex-shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
            <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clipRule="evenodd" />
          </svg>
          <p className="text-xs text-[#0072ff] leading-relaxed">
            The report will be generated from <strong>all your uploaded patient data files</strong>.
            Use the filters below to narrow the dataset by trial ID, date range, or custom instructions.
          </p>
        </div>

        <form onSubmit={(e) => void handleSubmit(onSubmit)(e)} className="space-y-5">

          {/* ── Step 1: Template ──────────────────────────────────────────── */}
          <SectionCard step={1} title="Select report template" hasError={!!errors.templateType || !!customTemplateError}>
            <>
              {/* Mode tabs */}
              <div className="flex gap-1 mb-5 p-1 bg-slate-100 rounded-[9px] w-fit">
                <button
                  type="button"
                  onClick={() => switchMode('standard')}
                  className={[
                    'px-4 py-1.5 rounded-[7px] text-xs font-semibold transition-all',
                    templateMode === 'standard'
                      ? 'bg-white text-[#0072ff] shadow-sm'
                      : 'text-slate-500 hover:text-slate-700',
                  ].join(' ')}
                >
                  Standard
                </button>
                <button
                  type="button"
                  onClick={() => switchMode('custom')}
                  className={[
                    'flex items-center gap-1.5 px-4 py-1.5 rounded-[7px] text-xs font-semibold transition-all',
                    templateMode === 'custom'
                      ? 'bg-white text-[#0072ff] shadow-sm'
                      : 'text-slate-500 hover:text-slate-700',
                  ].join(' ')}
                >
                  Custom
                  {customTemplates.length > 0 && (
                    <span className={[
                      'text-[10px] px-1.5 py-0.5 rounded-full font-bold',
                      templateMode === 'custom'
                        ? 'bg-[#0072ff]/10 text-[#0072ff]'
                        : 'bg-slate-200 text-slate-500',
                    ].join(' ')}>
                      {customTemplates.length}
                    </span>
                  )}
                </button>
              </div>

              {/* Standard templates */}
              {templateMode === 'standard' && (
                <>
                  {templatesError && (
                    <p className="mb-3 text-sm text-red-600">{templatesError}</p>
                  )}
                  {templates.length === 0 && !templatesError && (
                    <div className="flex items-center gap-2 text-sm text-brand-muted">
                      <svg className="w-4 h-4 animate-spin text-[#0072ff]" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                      </svg>
                      Loading templates…
                    </div>
                  )}
                  <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                    {templates.map((t) => (
                      <label
                        key={t.id}
                        className={[
                          'flex flex-col gap-1.5 rounded-[9px] border p-4 cursor-pointer transition-all',
                          selectedTemplate === t.id
                            ? 'border-[#0072ff] bg-[rgba(0,114,255,0.04)] ring-2 ring-[#0072ff]/30 shadow-sm'
                            : 'border-brand-border hover:border-[#0072ff]/40 hover:shadow-sm',
                        ].join(' ')}
                      >
                        <input type="radio" value={t.id} className="sr-only"
                          {...register('templateType')} />
                        {selectedTemplate === t.id && (
                          <div className="flex justify-end mb-0.5">
                            <svg className="w-4 h-4 text-[#0072ff]" fill="currentColor" viewBox="0 0 20 20">
                              <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                            </svg>
                          </div>
                        )}
                        <span className="font-semibold text-sm text-[#222222] font-heading">{t.displayName}</span>
                        <span className="text-xs text-brand-muted leading-relaxed">{t.description}</span>
                      </label>
                    ))}
                  </div>
                  {errors.templateType && (
                    <p className="mt-2 text-xs text-red-500">{errors.templateType.message}</p>
                  )}
                </>
              )}

              {/* Custom templates */}
              {templateMode === 'custom' && (
                <>
                  {customTemplatesLoading ? (
                    <div className="flex items-center gap-2 text-sm text-brand-muted py-2">
                      <svg className="w-4 h-4 animate-spin text-[#0072ff]" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                      </svg>
                      Loading custom templates…
                    </div>
                  ) : customTemplates.length === 0 ? (
                    <div className="text-center py-8 border-2 border-dashed border-brand-border rounded-[9px]">
                      <div className="w-10 h-10 rounded-xl bg-[#0072ff]/8 flex items-center justify-center mx-auto mb-3">
                        <svg className="w-5 h-5 text-[#0072ff]" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                            d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />
                        </svg>
                      </div>
                      <p className="text-sm text-brand-muted mb-3">No custom templates yet.</p>
                      <button
                        type="button"
                        onClick={() => navigate('/templates/new')}
                        className="text-xs font-semibold text-[#0072ff] hover:underline"
                      >
                        Create your first template →
                      </button>
                    </div>
                  ) : (
                    <div className="rounded-[9px] border border-brand-border overflow-hidden">
                      {/* scrollable list — fixed height regardless of count */}
                      <div className="max-h-72 overflow-y-auto divide-y divide-brand-border">
                      {customTemplates.map((t) => (
                        <button
                          type="button"
                          key={t.id}
                          onClick={() => selectCustomTemplate(t)}
                          className={[
                            'w-full text-left flex items-center gap-3 px-4 py-3 transition-colors',
                            customTemplateId === t.id
                              ? 'bg-[rgba(0,114,255,0.05)]'
                              : 'hover:bg-slate-50',
                          ].join(' ')}
                        >
                          {/* Selected indicator bar */}
                          <div className={[
                            'w-0.5 self-stretch rounded-full flex-shrink-0 transition-colors',
                            customTemplateId === t.id ? 'bg-[#0072ff]' : 'bg-transparent',
                          ].join(' ')} />

                          {/* Content */}
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center gap-2 mb-0.5">
                              <span className="font-semibold text-sm text-[#222222] truncate">{t.name}</span>
                              {t.isPublic && (
                                <span className="flex-shrink-0 text-[9px] px-1.5 py-0.5 rounded-full bg-purple-50 text-purple-600 border border-purple-200 font-semibold">
                                  Shared
                                </span>
                              )}
                            </div>
                            <div className="flex items-center gap-2">
                              {t.description && (
                                <span className="text-xs text-brand-muted truncate">{t.description}</span>
                              )}
                              <span className="flex-shrink-0 text-[10px] text-slate-400">
                                {t.sectionCount} section{t.sectionCount !== 1 ? 's' : ''}
                                {t.defaultOutputFormat ? ` · ${t.defaultOutputFormat}` : ''}
                              </span>
                            </div>
                          </div>

                          {/* Checkmark */}
                          {customTemplateId === t.id ? (
                            <svg className="w-4 h-4 text-[#0072ff] flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                              <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                            </svg>
                          ) : (
                            <div className="w-4 h-4 flex-shrink-0 rounded-full border-2 border-slate-200" />
                          )}
                        </button>
                      ))}
                      </div>
                    </div>
                  )}
                  {customTemplateError && (
                    <p className="mt-2 text-xs text-red-500">{customTemplateError}</p>
                  )}
                  {customTemplateId && (
                    <div className="mt-3 flex items-start gap-2 text-xs text-[#0072ff]">
                      <svg className="w-3.5 h-3.5 flex-shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
                        <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clipRule="evenodd" />
                      </svg>
                      Custom sections and AI instructions from this template will shape the generated report.
                    </div>
                  )}
                </>
              )}
            </>
          </SectionCard>

          {/* ── Step 2: Output format ─────────────────────────────────────── */}
          <SectionCard step={2} title="Output format">
            <Controller
              name="outputFormat" control={control} rules={{ required: true }}
              render={({ field }) => (
                <div className="flex flex-wrap gap-3">
                  {availableFormats.map((fmt) => (
                    <label
                      key={fmt}
                      className={[
                        'flex items-center gap-3 rounded-[9px] border px-4 py-3 cursor-pointer transition-all min-w-[140px]',
                        field.value === fmt
                          ? 'border-[#0072ff] bg-[rgba(0,114,255,0.04)] ring-2 ring-[#0072ff]/30 shadow-sm'
                          : 'border-brand-border hover:border-[#0072ff]/40 hover:shadow-sm',
                      ].join(' ')}
                    >
                      <input type="radio" value={fmt} checked={field.value === fmt}
                        onChange={() => field.onChange(fmt)} className="sr-only" />
                      <span className={field.value === fmt ? 'text-[#0072ff]' : 'text-brand-muted'}>
                        {formatIcons[fmt]}
                      </span>
                      <span className={`text-sm font-semibold ${field.value === fmt ? 'text-[#0072ff]' : 'text-brand-charcoal'}`}>
                        {formatLabels[fmt]}
                      </span>
                    </label>
                  ))}
                </div>
              )}
            />
            <p className="mt-3 text-xs text-brand-muted">
              Selected: <strong className="text-brand-charcoal">{formatLabels[selectedFormat]}</strong>
            </p>
          </SectionCard>

          {/* ── Step 3: Filters & AI instructions ────────────────────────── */}
          <SectionCard step={3} title="Filters & AI instructions">
            <>
              <p className="text-xs text-brand-muted mb-5 -mt-1">
                All filters are optional — leave blank to include the full dataset across all uploads.
              </p>
              <div className="grid gap-5 sm:grid-cols-2">
                <div>
                  <label htmlFor="trialId" className="block text-xs font-semibold text-brand-charcoal mb-1.5">
                    Trial ID
                  </label>
                  <input id="trialId" type="text" placeholder="e.g. TRIAL-001"
                    className="w-full rounded-[7px] border border-brand-border bg-white px-3 py-2.5 text-sm text-[#222222] placeholder:text-brand-muted focus:border-[#0072ff] focus:outline-none focus:ring-2 focus:ring-[#0072ff]/20 transition-colors"
                    {...register('trialId')} />
                </div>
                <div />
                <div>
                  <label htmlFor="dateRangeFrom" className="block text-xs font-semibold text-brand-charcoal mb-1.5">
                    Date range — from
                  </label>
                  <input id="dateRangeFrom" type="date"
                    className="w-full rounded-[7px] border border-brand-border bg-white px-3 py-2.5 text-sm text-[#222222] focus:border-[#0072ff] focus:outline-none focus:ring-2 focus:ring-[#0072ff]/20 transition-colors"
                    {...register('dateRangeFrom')} />
                </div>
                <div>
                  <label htmlFor="dateRangeTo" className="block text-xs font-semibold text-brand-charcoal mb-1.5">
                    Date range — to
                  </label>
                  <input id="dateRangeTo" type="date"
                    className="w-full rounded-[7px] border border-brand-border bg-white px-3 py-2.5 text-sm text-[#222222] focus:border-[#0072ff] focus:outline-none focus:ring-2 focus:ring-[#0072ff]/20 transition-colors"
                    {...register('dateRangeTo')} />
                </div>
                <div className="sm:col-span-2">
                  <label htmlFor="freeTextPrompt" className="block text-xs font-semibold text-brand-charcoal mb-1.5">
                    Additional instructions for the AI
                  </label>
                  <textarea id="freeTextPrompt" rows={3}
                    placeholder="e.g. Focus on adverse events in patients over 60. Explain only about Placebo group."
                    className="w-full rounded-[7px] border border-brand-border bg-white px-3 py-2.5 text-sm text-[#222222] placeholder:text-brand-muted focus:border-[#0072ff] focus:outline-none focus:ring-2 focus:ring-[#0072ff]/20 transition-colors resize-none"
                    {...register('freeTextPrompt')} />
                  <p className="mt-1.5 text-[10px] text-brand-muted">
                    Supports text filters ("only about Placebo") and numeric ranges ("patients above 65").
                  </p>
                </div>
              </div>
            </>
          </SectionCard>

          {/* ── Submit ────────────────────────────────────────────────────── */}
          {submitError && (
            <div className="rounded-[9px] bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700 text-center">
              {submitError}
            </div>
          )}

          <button
            type="submit"
            disabled={isSubmitting}
            className="btn-brand w-full py-3.5 text-base disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {isSubmitting ? (
              <>
                <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                </svg>
                Generating report…
              </>
            ) : (
              <>
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                    d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z" />
                </svg>
                Generate Report
              </>
            )}
          </button>

        </form>
      </div>
    </Layout>
  )
}

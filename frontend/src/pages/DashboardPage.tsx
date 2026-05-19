import { type ReactElement, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import Layout from '../components/Layout'
import { getDashboard, getApiErrorMessage } from '../api/reports'
import type { ChartCount, DailyCount, DashboardStats, ReportJobSummary, UploadSummary } from '../types/api'
import { useMsal } from '@azure/msal-react'

// ── Stat card ─────────────────────────────────────────────────────────────────

function StatCard({ label, value, sub, gradientFrom, gradientTo, icon }: {
  label: string; value: number | string; sub?: string
  gradientFrom: string; gradientTo: string; icon: ReactElement
}): ReactElement {
  return (
    <div className="bg-white rounded-[9px] shadow-card border border-brand-border p-5 flex items-start gap-4 hover:shadow-card-hover transition-shadow">
      <div
        className="flex-shrink-0 w-11 h-11 rounded-[9px] flex items-center justify-center"
        style={{ background: `linear-gradient(135deg, ${gradientFrom}, ${gradientTo})` }}
      >
        {icon}
      </div>
      <div className="flex-1 min-w-0">
        <p className="text-xs text-brand-muted font-medium uppercase tracking-wide">{label}</p>
        <p className="text-2xl font-bold text-[#222222] font-heading mt-0.5 leading-tight">
          {typeof value === 'number' ? value.toLocaleString() : value}
        </p>
        {sub && <p className="text-[11px] text-brand-muted mt-1">{sub}</p>}
      </div>
    </div>
  )
}

// ── Status badge ──────────────────────────────────────────────────────────────

export function StatusBadge({ status }: { status: string }): ReactElement {
  const cfg: Record<string, string> = {
    Completed:  'bg-[#12E58D]/10 text-[#1BA76D] ring-1 ring-[#12E58D]/30',
    Failed:     'bg-red-50 text-red-700 ring-1 ring-red-200',
    Processing: 'bg-[#0072ff]/10 text-[#0072ff] ring-1 ring-[#0072ff]/20',
    Queued:     'bg-amber-50 text-amber-700 ring-1 ring-amber-200',
  }
  const cls = cfg[status] ?? 'bg-gray-50 text-gray-600 ring-1 ring-gray-200'
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-semibold ${cls}`}>
      {status}
    </span>
  )
}

// ── Chart components ──────────────────────────────────────────────────────────

/** Multi-segment SVG donut ring chart. */
function DonutChart({ segments, total, centerLabel, centerSub }: {
  segments: { label: string; value: number; color: string }[]
  total: number
  centerLabel: string
  centerSub: string
}): ReactElement {
  const r = 36
  const circ = 2 * Math.PI * r

  // pre-compute start angles so no mutation inside JSX
  let cumAngle = -90
  const arcs = segments.filter(s => s.value > 0).map(s => {
    const startAngle = cumAngle
    cumAngle += (s.value / total) * 360
    return { ...s, startAngle }
  })

  return (
    <div className="flex items-center gap-5">
      <div className="relative flex-shrink-0 w-[110px] h-[110px]">
        <svg viewBox="0 0 100 100" className="w-full h-full">
          {/* Track */}
          <circle cx="50" cy="50" r={r} fill="none" stroke="#e9ecef" strokeWidth="12" />
          {/* Segments */}
          {total > 0 && arcs.map((arc, i) => (
            <circle
              key={i}
              cx="50" cy="50" r={r} fill="none"
              stroke={arc.color} strokeWidth="12"
              strokeDasharray={`${(arc.value / total) * circ} ${circ}`}
              strokeLinecap="butt"
              transform={`rotate(${arc.startAngle}, 50, 50)`}
            />
          ))}
        </svg>
        <div className="absolute inset-0 flex flex-col items-center justify-center pointer-events-none">
          <span className="text-xl font-bold text-[#222222] font-heading leading-none">{centerLabel}</span>
          <span className="text-[10px] text-brand-muted mt-0.5 text-center">{centerSub}</span>
        </div>
      </div>

      <div className="flex-1 min-w-0 space-y-2.5">
        {segments.map((s, i) => (
          <div key={i} className="flex items-center gap-2 text-xs">
            <span className="w-2.5 h-2.5 rounded-full flex-shrink-0" style={{ backgroundColor: s.color }} />
            <span className="flex-1 text-brand-charcoal truncate">{s.label}</span>
            <span className="font-bold text-[#222222]">{s.value}</span>
          </div>
        ))}
        {total === 0 && (
          <p className="text-xs text-brand-muted italic">No data yet</p>
        )}
      </div>
    </div>
  )
}

/** Horizontal bar chart for category breakdowns. */
function HBarChart({ data, total, emptyText }: {
  data: { label: string; value: number; color: string }[]
  total: number
  emptyText: string
}): ReactElement {
  if (data.length === 0) {
    return <p className="text-xs text-brand-muted italic py-2">{emptyText}</p>
  }
  return (
    <div className="space-y-3.5">
      {data.map((d, i) => (
        <div key={i}>
          <div className="flex justify-between items-center mb-1.5">
            <span className="text-xs text-brand-charcoal font-medium truncate mr-2 max-w-[160px]">{d.label}</span>
            <div className="flex items-center gap-2 flex-shrink-0">
              <span className="text-[11px] text-brand-muted">
                {total > 0 ? `${Math.round((d.value / total) * 100)}%` : '0%'}
              </span>
              <span className="text-xs font-bold text-[#222222] w-5 text-right">{d.value}</span>
            </div>
          </div>
          <div className="h-2 bg-[#f8f9fa] rounded-full overflow-hidden">
            <div
              className="h-full rounded-full transition-all duration-700 ease-out"
              style={{
                width: total > 0 ? `${(d.value / total) * 100}%` : '0%',
                backgroundColor: d.color,
              }}
            />
          </div>
        </div>
      ))}
    </div>
  )
}

/** 7-day bar sparkline. */
function BarSparkline({ data }: { data: DailyCount[] }): ReactElement {
  const max = Math.max(...data.map(d => d.count), 1)
  const total = data.reduce((sum, d) => sum + d.count, 0)
  return (
    <div>
      <div className="flex items-end gap-1 h-16">
        {data.map((d, i) => (
          <div key={i} className="flex-1 flex flex-col items-center justify-end gap-0.5" title={`${d.date}: ${d.count} upload${d.count !== 1 ? 's' : ''}`}>
            <div
              className="w-full rounded-t-[3px] transition-all duration-700 ease-out"
              style={{
                height: `${d.count > 0 ? Math.max((d.count / max) * 100, 12) : 4}%`,
                backgroundColor: d.count > 0 ? '#0072ff' : '#e9ecef',
                opacity: d.count > 0 ? 1 : 0.5,
              }}
            />
          </div>
        ))}
      </div>
      {/* X-axis labels */}
      <div className="flex gap-1 mt-1.5">
        {data.map((d, i) => (
          <div key={i} className="flex-1 text-center">
            <span className="text-[9px] text-brand-muted">{d.date.split(' ')[1]}</span>
          </div>
        ))}
      </div>
      <p className="text-[11px] text-brand-muted mt-3">
        {total} upload{total !== 1 ? 's' : ''} in the last 7 days
      </p>
    </div>
  )
}

/** Segmented horizontal bar for format distribution. */
function SegmentedBar({ data, total }: {
  data: { label: string; value: number; color: string }[]
  total: number
}): ReactElement {
  const filled = data.filter(d => d.value > 0)
  return (
    <div>
      <div className="flex h-4 rounded-full overflow-hidden gap-[2px]">
        {total === 0 ? (
          <div className="flex-1 bg-[#e9ecef] rounded-full" />
        ) : (
          filled.map((d, i) => (
            <div
              key={i}
              className="transition-all duration-700 ease-out first:rounded-l-full last:rounded-r-full"
              style={{ width: `${(d.value / total) * 100}%`, backgroundColor: d.color }}
              title={`${d.label}: ${d.value}`}
            />
          ))
        )}
      </div>
      <div className="flex flex-wrap gap-x-4 gap-y-2 mt-3">
        {data.map((d, i) => (
          <div key={i} className="flex items-center gap-1.5">
            <div className="w-2.5 h-2.5 rounded-sm flex-shrink-0" style={{ backgroundColor: d.color }} />
            <span className="text-xs text-brand-charcoal">{d.label}</span>
            <span className="text-xs font-bold text-[#222222]">{d.value}</span>
          </div>
        ))}
      </div>
      {total === 0 && (
        <p className="text-xs text-brand-muted italic mt-2">No reports yet</p>
      )}
    </div>
  )
}

/** Wrapper card for chart sections. */
function ChartCard({ title, subtitle, children }: {
  title: string; subtitle?: string; children: ReactElement
}): ReactElement {
  return (
    <div className="bg-white rounded-[9px] shadow-card border border-brand-border p-5">
      <div className="mb-4">
        <p className="text-sm font-bold text-[#222222] font-heading">{title}</p>
        {subtitle && <p className="text-[11px] text-brand-muted mt-0.5">{subtitle}</p>}
      </div>
      {children}
    </div>
  )
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function relativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  const mins = Math.floor(diff / 60000)
  if (mins < 1)  return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hrs = Math.floor(mins / 60)
  if (hrs < 24)  return `${hrs}h ago`
  return `${Math.floor(hrs / 24)}d ago`
}

function formatBytes(bytes: number): string {
  if (bytes < 1024)        return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

const templateLabels: Record<string, string> = {
  'patient-summary': 'Patient Summary',
  'outcome-data':    'Outcome Data',
  'full-report':     'Full Trial Report',
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function DashboardPage(): ReactElement {
  const { accounts } = useMsal()
  const userName = accounts[0]?.name?.split(' ')[0] ?? 'there'

  const [stats, setStats]     = useState<DashboardStats | null>(null)
  const [error, setError]     = useState('')
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getDashboard()
      .then(setStats)
      .catch((err: unknown) => setError(getApiErrorMessage(err)))
      .finally(() => setLoading(false))
  }, [])

  const successRate = stats && stats.totalReports > 0
    ? Math.round((stats.completedReports / stats.totalReports) * 100)
    : 0

  // ── Chart data derivations ─────────────────────────────────────────────────

  const statusSegments = stats ? buildStatusSegments(stats.reportsByStatus) : []

  const templateBars = stats
    ? stats.reportsByTemplate.map(t => ({
        label: templateLabels[t.label] ?? t.label,
        value: t.count,
        color: templateColors[t.label] ?? '#6c757d',
      }))
    : []

  const formatSegments = stats
    ? (['Docx', 'Pdf', 'Excel'] as const).map(fmt => ({
        label: fmt === 'Docx' ? 'Word' : fmt,
        value: stats.reportsByFormat.find(f => f.label === fmt)?.count ?? 0,
        color: formatColors[fmt],
      }))
    : []

  return (
    <Layout
      title={`Welcome, ${userName}`}
      subtitle="Clinical Trial Agent — AI-powered patient data reporting platform"
    >
      {error && (
        <div className="mb-5 rounded-[9px] bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {loading ? (
        <div className="space-y-5 animate-pulse">
          <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
            {[...Array(4)].map((_, i) => <div key={i} className="h-28 bg-white rounded-[9px] shadow-card" />)}
          </div>
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
            {[...Array(3)].map((_, i) => <div key={i} className="h-52 bg-white rounded-[9px] shadow-card" />)}
          </div>
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
            {[...Array(2)].map((_, i) => <div key={i} className="h-36 bg-white rounded-[9px] shadow-card" />)}
          </div>
        </div>
      ) : stats ? (
        <>
          {/* ── Stat cards ─────────────────────────────────────────────────── */}
          <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4 mb-6">
            <StatCard label="Total Uploads" value={stats.totalUploads} sub="Excel files processed"
              gradientFrom="#0072ff" gradientTo="#8200f4"
              icon={<svg className="w-5 h-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                  d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
              </svg>}
            />
            <StatCard label="Patient Records" value={stats.totalPatients} sub="Across all uploads"
              gradientFrom="#8200f4" gradientTo="#ff6c5f"
              icon={<svg className="w-5 h-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                  d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0z" />
              </svg>}
            />
            <StatCard label="Reports Generated" value={stats.totalReports} sub="Word · PDF · Excel"
              gradientFrom="#1EB5C7" gradientTo="#12E58D"
              icon={<svg className="w-5 h-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                  d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>}
            />
            <StatCard label="Success Rate" value={`${successRate}%`}
              sub={`${stats.completedReports} of ${stats.totalReports} completed`}
              gradientFrom="#12E58D" gradientTo="#1EB5C7"
              icon={<svg className="w-5 h-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                  d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>}
            />
          </div>

          {/* ── Quick actions ─────────────────────────────────────────────── */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-6">
            <Link to="/request" className="btn-brand py-3.5 text-sm">
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
              </svg>
              Generate New Report
            </Link>
            <Link to="/uploads"
              className="flex items-center justify-center gap-2 bg-white hover:bg-brand-surface border border-brand-border rounded-[9px] px-5 py-3.5 font-semibold text-sm text-brand-charcoal shadow-card hover:shadow-card-hover transition-all">
              <svg className="w-4 h-4 text-brand-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                  d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
              </svg>
              Upload History
            </Link>
            <Link to="/reports"
              className="flex items-center justify-center gap-2 bg-white hover:bg-brand-surface border border-brand-border rounded-[9px] px-5 py-3.5 font-semibold text-sm text-brand-charcoal shadow-card hover:shadow-card-hover transition-all">
              <svg className="w-4 h-4 text-brand-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                  d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
              Browse Reports
            </Link>
          </div>

          {/* ── Charts row 1: Donut · H-Bars · Sparkline ─────────────────── */}
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5 mb-5">

            <ChartCard title="Report Status" subtitle="Breakdown of all generated reports">
              <DonutChart
                segments={statusSegments}
                total={stats.totalReports}
                centerLabel={stats.totalReports > 0 ? `${successRate}%` : '—'}
                centerSub="success rate"
              />
            </ChartCard>

            <ChartCard title="Template Usage" subtitle="Reports by template type">
              <HBarChart
                data={templateBars}
                total={stats.totalReports}
                emptyText="No reports generated yet"
              />
            </ChartCard>

            <ChartCard title="Upload Activity" subtitle="Files uploaded in the last 7 days">
              <BarSparkline data={stats.uploadsLast7Days} />
            </ChartCard>

          </div>

          {/* ── Charts row 2: Format split · Patient volume ───────────────── */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-5 mb-6">

            <ChartCard title="Output Format Distribution" subtitle="Proportion of reports by file format">
              <SegmentedBar data={formatSegments} total={stats.totalReports} />
            </ChartCard>

            <ChartCard title="Patient Volume by Upload" subtitle="File sizes across recent uploads">
              <PatientVolumeChart uploads={stats.recentUploads} />
            </ChartCard>

          </div>

          {/* ── Recent uploads ───────────────────────────────────────────── */}
          <SectionCard title="Recent Uploads" linkTo="/uploads" className="mb-5">
            {stats.recentUploads.length === 0 ? (
              <EmptyState message="No uploads yet. Upload an Excel file to get started." />
            ) : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left border-b border-brand-border">
                    {['File', 'Rows', 'Size', 'Uploaded', ''].map((h) => (
                      <th key={h} className="px-5 py-3 text-[11px] font-semibold text-brand-muted uppercase tracking-wide">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {stats.recentUploads.map((u) => <UploadRow key={u.id} upload={u} />)}
                </tbody>
              </table>
            )}
          </SectionCard>

          {/* ── Recent reports ───────────────────────────────────────────── */}
          <SectionCard title="Recent Reports" linkTo="/reports">
            {stats.recentReports.length === 0 ? (
              <EmptyState message="No reports generated yet." />
            ) : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left border-b border-brand-border">
                    {['Job ID', 'Template', 'Format', 'Rows', 'Status', 'Generated', 'Download'].map((h) => (
                      <th key={h} className="px-5 py-3 text-[11px] font-semibold text-brand-muted uppercase tracking-wide">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {stats.recentReports.map((r) => <ReportRow key={r.id} report={r} />)}
                </tbody>
              </table>
            )}
          </SectionCard>
        </>
      ) : null}
    </Layout>
  )
}

// ── Chart data helpers ────────────────────────────────────────────────────────

const statusColors: Record<string, string> = {
  Completed:  '#12E58D',
  Failed:     '#ff6c5f',
  Processing: '#0072ff',
  Queued:     '#f59e0b',
}

const templateColors: Record<string, string> = {
  'patient-summary': '#0072ff',
  'outcome-data':    '#8200f4',
  'full-report':     '#ff6c5f',
}

const formatColors: Record<string, string> = {
  Docx:  '#0072ff',
  Pdf:   '#ff6c5f',
  Excel: '#12E58D',
}

function buildStatusSegments(data: ChartCount[]): { label: string; value: number; color: string }[] {
  const all = ['Completed', 'Failed', 'Processing', 'Queued']
  return all.map(s => ({
    label: s,
    value: data.find(d => d.label === s)?.count ?? 0,
    color: statusColors[s] ?? '#e9ecef',
  }))
}

/** Mini horizontal bar chart showing row counts per recent upload. */
function PatientVolumeChart({ uploads }: { uploads: UploadSummary[] }): ReactElement {
  if (uploads.length === 0) {
    return <p className="text-xs text-brand-muted italic py-2">No uploads yet</p>
  }
  const max = Math.max(...uploads.map(u => u.rowCount), 1)
  return (
    <div className="space-y-3">
      {uploads.map((u, i) => (
        <div key={i}>
          <div className="flex justify-between items-center mb-1.5">
            <span className="text-xs text-brand-charcoal font-medium truncate mr-2 max-w-[180px]" title={u.fileName}>
              {u.fileName}
            </span>
            <span className="text-xs font-bold text-[#222222] flex-shrink-0">
              {u.rowCount.toLocaleString()} rows
            </span>
          </div>
          <div className="h-2 bg-[#f8f9fa] rounded-full overflow-hidden">
            <div
              className="h-full rounded-full transition-all duration-700 ease-out"
              style={{
                width: `${(u.rowCount / max) * 100}%`,
                background: 'linear-gradient(90deg, #0072ff, #8200f4)',
              }}
            />
          </div>
        </div>
      ))}
    </div>
  )
}

// ── Sub-components ────────────────────────────────────────────────────────────

function SectionCard({ title, linkTo, children, className = '' }: {
  title: string; linkTo: string; children: ReactElement; className?: string
}): ReactElement {
  return (
    <div className={`bg-white rounded-[9px] shadow-card border border-brand-border overflow-hidden ${className}`}>
      <div className="flex items-center justify-between px-5 py-4 border-b border-brand-border">
        <h3 className="font-bold text-[#222222] font-heading text-sm">{title}</h3>
        <Link to={linkTo} className="text-xs font-semibold text-[#1EB5C7] hover:opacity-75 transition-opacity">
          View all →
        </Link>
      </div>
      <div className="overflow-x-auto">{children}</div>
    </div>
  )
}

function UploadRow({ upload }: { upload: UploadSummary }): ReactElement {
  return (
    <tr className="border-b border-brand-border hover:bg-brand-surface transition-colors last:border-0">
      <td className="px-5 py-3.5">
        <div className="flex items-center gap-2">
          <div className="w-7 h-7 rounded-lg bg-[#12E58D]/10 flex items-center justify-center flex-shrink-0">
            <svg className="w-3.5 h-3.5 text-[#1BA76D]" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
            </svg>
          </div>
          <span className="font-medium text-[#222222] truncate max-w-[180px]">{upload.fileName}</span>
        </div>
      </td>
      <td className="px-5 py-3.5 font-semibold text-brand-charcoal">{upload.rowCount.toLocaleString()}</td>
      <td className="px-5 py-3.5 text-brand-muted text-xs">{formatBytes(upload.fileSizeBytes)}</td>
      <td className="px-5 py-3.5 text-brand-muted text-xs">{relativeTime(upload.uploadedAt)}</td>
      <td className="px-5 py-3.5">
        <Link to="/request" className="text-xs font-semibold text-[#1EB5C7] hover:opacity-75 transition-opacity">
          Generate →
        </Link>
      </td>
    </tr>
  )
}

function ReportRow({ report }: { report: ReportJobSummary }): ReactElement {
  return (
    <tr className="border-b border-brand-border hover:bg-brand-surface transition-colors last:border-0">
      <td className="px-5 py-3.5">
        <span className="font-mono text-[11px] text-brand-muted bg-brand-surface px-2 py-0.5 rounded">
          {report.id.slice(0, 8)}…
        </span>
      </td>
      <td className="px-5 py-3.5 text-[#222222] text-xs font-medium">
        {templateLabels[report.templateType] ?? report.templateType}
      </td>
      <td className="px-5 py-3.5">
        <span
          className="text-[10px] font-bold uppercase px-2 py-0.5 rounded-full border"
          style={{
            color: formatColors[report.outputFormat] ?? '#6c757d',
            backgroundColor: `${formatColors[report.outputFormat] ?? '#6c757d'}15`,
            borderColor: `${formatColors[report.outputFormat] ?? '#6c757d'}40`,
          }}
        >
          {report.outputFormat}
        </span>
      </td>
      <td className="px-5 py-3.5 font-semibold text-brand-charcoal text-xs">{report.rowCount.toLocaleString()}</td>
      <td className="px-5 py-3.5"><StatusBadge status={report.status} /></td>
      <td className="px-5 py-3.5 text-brand-muted text-xs">{relativeTime(report.createdAt)}</td>
      <td className="px-5 py-3.5">
        {report.downloadUrl ? (
          <a href={report.downloadUrl} download
            className="inline-flex items-center gap-1 text-[11px] font-semibold text-[#1EB5C7] hover:opacity-75 transition-opacity">
            <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
            </svg>
            Download
          </a>
        ) : <span className="text-brand-muted/40 text-xs">—</span>}
      </td>
    </tr>
  )
}

function EmptyState({ message }: { message: string }): ReactElement {
  return <div className="px-5 py-10 text-center text-sm text-brand-muted">{message}</div>
}

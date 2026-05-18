import { type ReactElement } from 'react'
import { useParams, Link } from 'react-router-dom'

export default function DownloadPage(): ReactElement {
  const { jobId } = useParams<{ jobId: string }>()

  return (
    <div className="min-h-screen bg-gray-50">
      <nav className="bg-white shadow-sm">
        <div className="mx-auto max-w-4xl px-4 py-4 flex items-center justify-between">
          <Link to="/request" className="text-lg font-semibold text-gray-900 hover:text-blue-600">
            ← New Report
          </Link>
          <span className="text-sm text-gray-500">Download</span>
        </div>
      </nav>

      <main className="mx-auto max-w-4xl px-4 py-10">
        <h1 className="text-2xl font-bold text-gray-900 mb-6">Download Report</h1>

        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-gray-200 space-y-3">
          <p className="text-sm text-gray-500">
            Report ready for job:{' '}
            <code className="rounded bg-gray-100 px-1.5 py-0.5 font-mono text-xs text-gray-800">
              {jobId}
            </code>
          </p>
          <p className="text-sm text-gray-400">
            Download button implementation coming in the next task.
          </p>
        </div>
      </main>
    </div>
  )
}

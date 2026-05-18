import { type ReactElement } from 'react'
import { useMsal } from '@azure/msal-react'

export default function ReportRequestPage(): ReactElement {
  const { accounts } = useMsal()
  const userName = accounts[0]?.name ?? accounts[0]?.username ?? 'User'

  return (
    <div className="min-h-screen bg-gray-50">
      <nav className="bg-white shadow-sm">
        <div className="mx-auto max-w-4xl px-4 py-4 flex items-center justify-between">
          <span className="text-lg font-semibold text-gray-900">Clinical Trial Agent</span>
          <span className="text-sm text-gray-500">{userName}</span>
        </div>
      </nav>

      <main className="mx-auto max-w-4xl px-4 py-10">
        <h1 className="text-2xl font-bold text-gray-900 mb-6">Generate Report</h1>

        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-gray-200">
          <p className="text-sm text-gray-500">
            Report request form — upload an Excel file and select a template.
            Implementation coming in the next task.
          </p>
        </div>
      </main>
    </div>
  )
}

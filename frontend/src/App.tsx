import { type ReactElement } from 'react'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useAuth } from './auth/AuthContext'
import LoginPage           from './pages/LoginPage'
import AuthCallbackPage    from './pages/AuthCallbackPage'
import DashboardPage       from './pages/DashboardPage'
import ReportRequestPage   from './pages/ReportRequestPage'
import UploadDataPage      from './pages/UploadDataPage'
import StatusPage          from './pages/StatusPage'
import DownloadPage        from './pages/DownloadPage'
import UploadsHistoryPage  from './pages/UploadsHistoryPage'
import ReportsHistoryPage  from './pages/ReportsHistoryPage'

function ProtectedRoute({ children }: { children: ReactElement }): ReactElement {
  const { isAuthenticated, isLoading } = useAuth()

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-50">
        <div className="flex items-center gap-3 text-gray-400">
          <svg className="animate-spin w-5 h-5" fill="none" viewBox="0 0 24 24">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor"
              d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
          </svg>
          <span className="text-sm">Signing in…</span>
        </div>
      </div>
    )
  }

  return isAuthenticated ? children : <Navigate to="/" replace />
}

export default function App(): ReactElement {
  return (
    <BrowserRouter>
      <Routes>
        {/* Public */}
        <Route path="/"              element={<LoginPage />} />
        <Route path="/auth/callback" element={<AuthCallbackPage />} />

        {/* Protected */}
        <Route path="/dashboard" element={<ProtectedRoute><DashboardPage /></ProtectedRoute>} />
        <Route path="/upload"    element={<ProtectedRoute><UploadDataPage /></ProtectedRoute>} />
        <Route path="/request"   element={<ProtectedRoute><ReportRequestPage /></ProtectedRoute>} />
        <Route path="/uploads"   element={<ProtectedRoute><UploadsHistoryPage /></ProtectedRoute>} />
        <Route path="/reports"   element={<ProtectedRoute><ReportsHistoryPage /></ProtectedRoute>} />
        <Route path="/status/:jobId"   element={<ProtectedRoute><StatusPage /></ProtectedRoute>} />
        <Route path="/download/:jobId" element={<ProtectedRoute><DownloadPage /></ProtectedRoute>} />

        {/* Fallback */}
        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </BrowserRouter>
  )
}

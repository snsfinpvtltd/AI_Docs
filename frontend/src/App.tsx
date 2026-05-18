import { type ReactElement } from 'react'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useIsAuthenticated } from '@azure/msal-react'
import LoginPage        from './pages/LoginPage'
import ReportRequestPage from './pages/ReportRequestPage'
import StatusPage       from './pages/StatusPage'
import DownloadPage     from './pages/DownloadPage'

function ProtectedRoute({ children }: { children: ReactElement }): ReactElement {
  const isAuthenticated = useIsAuthenticated()
  return isAuthenticated ? children : <Navigate to="/" replace />
}

export default function App(): ReactElement {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<LoginPage />} />
        <Route
          path="/request"
          element={
            <ProtectedRoute>
              <ReportRequestPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/status/:jobId"
          element={
            <ProtectedRoute>
              <StatusPage />
            </ProtectedRoute>
          }
        />
        <Route
          path="/download/:jobId"
          element={
            <ProtectedRoute>
              <DownloadPage />
            </ProtectedRoute>
          }
        />
      </Routes>
    </BrowserRouter>
  )
}

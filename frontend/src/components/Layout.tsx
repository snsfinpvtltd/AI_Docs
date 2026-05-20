import { type ReactElement, type ReactNode, useState } from 'react'
import { NavLink } from 'react-router-dom'
import { useMsal } from '@azure/msal-react'
import ClinevoLogo from './ClinevoLogo'

interface LayoutProps {
  children: ReactNode
  title?: string
  subtitle?: string
}

// ── Nav icons ─────────────────────────────────────────────────────────────────

function IconDashboard(): ReactElement {
  return (
    <svg className="w-[18px] h-[18px] flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M4 5a1 1 0 011-1h4a1 1 0 011 1v5a1 1 0 01-1 1H5a1 1 0 01-1-1V5zm10 0a1 1 0 011-1h4a1 1 0 011 1v2a1 1 0 01-1 1h-4a1 1 0 01-1-1V5zM4 15a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1v-4zm10-3a1 1 0 011-1h4a1 1 0 011 1v7a1 1 0 01-1 1h-4a1 1 0 01-1-1v-7z" />
    </svg>
  )
}

function IconPlus(): ReactElement {
  return (
    <svg className="w-[18px] h-[18px] flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
    </svg>
  )
}

function IconUpload(): ReactElement {
  return (
    <svg className="w-[18px] h-[18px] flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
    </svg>
  )
}

function IconCloudUpload(): ReactElement {
  return (
    <svg className="w-[18px] h-[18px] flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
    </svg>
  )
}

function IconDocument(): ReactElement {
  return (
    <svg className="w-[18px] h-[18px] flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
    </svg>
  )
}

function IconTemplate(): ReactElement {
  return (
    <svg className="w-[18px] h-[18px] flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-3 7h3m-3 4h3m-6-4h.01M9 16h.01" />
    </svg>
  )
}

function IconMenu(): ReactElement {
  return (
    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
    </svg>
  )
}

function IconLogout(): ReactElement {
  return (
    <svg className="w-4 h-4 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
        d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
    </svg>
  )
}

function IconChevronLeft(): ReactElement {
  return (
    <svg className="w-4 h-4 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
    </svg>
  )
}

function IconChevronRight(): ReactElement {
  return (
    <svg className="w-4 h-4 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
    </svg>
  )
}

const NAV_ITEMS = [
  { to: '/dashboard', label: 'Dashboard',       icon: <IconDashboard /> },
  { to: '/upload',    label: 'Upload Data',     icon: <IconCloudUpload /> },
  { to: '/request',   label: 'New Report',      icon: <IconPlus /> },
  { to: '/templates', label: 'Templates',       icon: <IconTemplate /> },
  { to: '/uploads',   label: 'Upload History',  icon: <IconUpload /> },
  { to: '/reports',   label: 'Reports History', icon: <IconDocument /> },
]

// ── Shared sidebar content (mobile always expanded) ───────────────────────────

interface SidebarContentProps {
  collapsed: boolean
  userName: string
  userInitial: string
  onNavClick?: () => void
  onToggle?: () => void
  onLogout: () => void
  showToggle?: boolean
}

function SidebarContent({
  collapsed,
  userName,
  userInitial,
  onNavClick,
  onToggle,
  onLogout,
  showToggle = false,
}: SidebarContentProps): ReactElement {
  return (
    <div className="flex flex-col h-full overflow-hidden">
      {/* ── Logo ────────────────────────────────────────────────────────────── */}
      <div className={`flex items-center border-b border-white/10 transition-all duration-300 ${collapsed ? 'justify-center px-2 py-[14px]' : 'px-4 py-4'}`}>
        {collapsed ? (
          <div className="bg-white rounded-lg p-1">
            <ClinevoLogo variant="icon" className="w-8 h-8" />
          </div>
        ) : (
          <div className="bg-white rounded-lg px-2 py-1">
            <ClinevoLogo variant="compact" className="h-8" />
          </div>
        )}
      </div>

      {/* ── Navigation ──────────────────────────────────────────────────────── */}
      <nav className="flex-1 px-2 py-4 overflow-y-auto overflow-x-hidden">
        {!collapsed && (
          <p className="px-2 mb-3 text-[10px] font-semibold text-white/30 uppercase tracking-[2px] whitespace-nowrap">
            Clinical Workspace
          </p>
        )}
        <ul className="space-y-0.5">
          {NAV_ITEMS.map((item) => (
            <li key={item.to}>
              <NavLink
                to={item.to}
                onClick={onNavClick}
                title={collapsed ? item.label : undefined}
                className={({ isActive }) =>
                  `flex items-center rounded-[9px] text-sm font-medium transition-colors overflow-hidden
                  ${collapsed ? 'justify-center p-2.5' : 'gap-3 px-3 py-2.5'}
                  ${isActive
                    ? 'text-white bg-brand-gradient shadow-brand font-semibold'
                    : 'text-white/60 hover:bg-white/[0.06] hover:text-white/90'
                  }`
                }
              >
                {item.icon}
                {!collapsed && (
                  <span className="whitespace-nowrap overflow-hidden text-ellipsis">{item.label}</span>
                )}
              </NavLink>
            </li>
          ))}
        </ul>

        {!collapsed && (
          <>
            <div className="my-4 border-t border-white/10" />
            <div className="px-2 py-3 rounded-[9px] bg-white/[0.04] border border-white/10">
              <p className="text-[11px] font-semibold text-white/70 mb-1">Powered by</p>
              <p className="text-[10px] text-white/40 leading-relaxed">
                Microsoft Semantic Kernel<br />
                Groq LLM · Azure Entra ID
              </p>
            </div>
          </>
        )}
      </nav>

      {/* ── Collapse toggle (desktop only) ──────────────────────────────────── */}
      {showToggle && (
        <div className="px-2 py-1.5 border-t border-white/10">
          <button
            type="button"
            onClick={onToggle}
            title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
            className={`w-full flex items-center rounded-[9px] py-2 px-2 text-white/40 hover:text-white/80 hover:bg-white/[0.06] transition-colors ${collapsed ? 'justify-center' : 'gap-2'}`}
          >
            {collapsed ? <IconChevronRight /> : <><IconChevronLeft /><span className="text-xs">Collapse</span></>}
          </button>
        </div>
      )}

      {/* ── User footer ─────────────────────────────────────────────────────── */}
      <div className={`border-t border-white/10 ${collapsed ? 'px-2 py-3' : 'px-4 py-4'}`}>
        {collapsed ? (
          <div className="flex flex-col items-center gap-2.5">
            <div className="w-8 h-8 rounded-full flex items-center justify-center bg-brand-gradient shadow-brand">
              <span className="text-white font-bold text-sm">{userInitial}</span>
            </div>
            <button
              type="button"
              onClick={onLogout}
              title="Sign out"
              className="text-white/40 hover:text-[#ff6c5f] transition-colors"
            >
              <IconLogout />
            </button>
          </div>
        ) : (
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 rounded-full flex items-center justify-center flex-shrink-0 bg-brand-gradient shadow-brand">
              <span className="text-white font-bold text-sm font-heading">{userInitial}</span>
            </div>
            <div className="flex-1 min-w-0">
              <p className="text-white text-xs font-semibold truncate font-heading">{userName}</p>
              <p className="text-white/40 text-[10px]">Clinical Analyst</p>
            </div>
            <button
              type="button"
              onClick={onLogout}
              title="Sign out"
              className="text-white/40 hover:text-[#ff6c5f] transition-colors flex-shrink-0"
            >
              <IconLogout />
            </button>
          </div>
        )}
      </div>
    </div>
  )
}

// ── Layout ────────────────────────────────────────────────────────────────────

export default function Layout({ children, title, subtitle }: LayoutProps): ReactElement {
  const { accounts, instance } = useMsal()
  const userName    = accounts[0]?.name ?? accounts[0]?.username ?? 'User'
  const userInitial = (accounts[0]?.name ?? accounts[0]?.username ?? 'U')[0].toUpperCase()
  const [collapsed, setCollapsed]   = useState(false)
  const [mobileOpen, setMobileOpen] = useState(false)

  function handleLogout(): void {
    void instance.logoutRedirect({ postLogoutRedirectUri: '/' })
  }

  return (
    <div className="flex h-screen bg-brand-surface overflow-hidden">

      {/* ── Sidebar — desktop (collapsible) ───────────────────────────────── */}
      <aside
        className={`hidden lg:flex lg:flex-col flex-shrink-0 bg-[#212529] transition-[width] duration-300 ease-in-out ${collapsed ? 'w-[56px]' : 'w-60'}`}
      >
        <SidebarContent
          collapsed={collapsed}
          userName={userName}
          userInitial={userInitial}
          onToggle={() => setCollapsed((c) => !c)}
          onLogout={handleLogout}
          showToggle
        />
      </aside>

      {/* ── Sidebar — mobile overlay ───────────────────────────────────────── */}
      {mobileOpen && (
        <div className="fixed inset-0 z-40 flex lg:hidden">
          <div
            className="fixed inset-0 bg-black/60 backdrop-blur-sm"
            onClick={() => setMobileOpen(false)}
          />
          <aside className="relative z-50 flex flex-col w-60 bg-[#212529] shadow-2xl">
            <SidebarContent
              collapsed={false}
              userName={userName}
              userInitial={userInitial}
              onNavClick={() => setMobileOpen(false)}
              onLogout={handleLogout}
            />
          </aside>
        </div>
      )}

      {/* ── Main area ─────────────────────────────────────────────────────── */}
      <div className="flex-1 flex flex-col overflow-hidden">

        {/* Topbar — title only, no user details */}
        <header className="flex-shrink-0 bg-white border-b border-brand-border px-6 py-3.5 flex items-center gap-4 shadow-[0_1px_4px_rgba(0,0,0,0.06)]">
          {/* Mobile hamburger */}
          <button
            type="button"
            className="lg:hidden text-brand-muted hover:text-brand-charcoal transition-colors"
            onClick={() => setMobileOpen(true)}
          >
            <IconMenu />
          </button>

          <div className="flex-1 min-w-0">
            {title && (
              <h1 className="text-base font-bold text-[#222222] font-heading leading-tight">{title}</h1>
            )}
            {subtitle && (
              <p className="text-xs text-brand-muted mt-0.5">{subtitle}</p>
            )}
          </div>
        </header>

        {/* Page content */}
        <main className="flex-1 overflow-y-auto p-6">
          {children}
        </main>
      </div>
    </div>
  )
}

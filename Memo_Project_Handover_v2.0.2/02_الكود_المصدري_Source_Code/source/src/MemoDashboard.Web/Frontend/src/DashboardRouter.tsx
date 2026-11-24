import { useState, useEffect } from 'react'
import type { SetupConfig } from './services/api'
import { CentralDashboard } from './dashboard/central/CentralDashboard'
import { CompanyReportPage } from './dashboard/company/CompanyReportPage'
import { LegacyDashboard } from './dashboard/legacy/LegacyDashboard'
import { MainPage } from './pages/MainPage'
import { MemosPage } from './pages/memos/MemosPage'
import { ReviewQueuePage } from './pages/review/ReviewQueuePage'
import { IncompleteIndicatorsPage } from './pages/review/IncompleteIndicatorsPage'
import { ReportsHubPage } from './pages/reports/ReportsHubPage'
import { MasterDataPage } from './pages/master/MasterDataPage'
import { AdminConsolePage } from './pages/admin/AdminConsolePage'
import { AppLayout, type ActiveNavView } from './components/layout/AppLayout'

// Kept for compatibility with stored app_settings; routing no longer branches on the value.
export const V1 = 1
export const V2 = 2
export const V3 = 3

export function normalizeDashboardVersion(value: number | null | undefined): number {
  if (value === V3) return V3
  if (value === V2) return V2
  return V1
}

interface DashboardRouterProps {
  config: SetupConfig | null
  onEditConnection?: () => void
}

function parseCurrentView(): { view: ActiveNavView | 'companyReport' | 'central' } {
  if (typeof window === 'undefined') return { view: 'dashboard' }
  const params = new URLSearchParams(window.location.search)
  if (params.get('report') === 'company') {
    return { view: 'companyReport' }
  }
  const rawView = params.get('view')
  if (rawView === 'memos') return { view: 'memos' }
  if (rawView === 'review') return { view: 'review' }
  if (rawView === 'incomplete') return { view: 'incomplete' }
  if (rawView === 'reports') return { view: 'reports' }
  if (rawView === 'master') return { view: 'master' }
  if (rawView === 'admin') return { view: 'admin' }
  if (rawView === 'analytics') return { view: 'analytics' }
  if (rawView === 'central') return { view: 'central' }
  return { view: 'dashboard' }
}

export function DashboardRouter({ config, onEditConnection }: DashboardRouterProps) {
  const [current, setCurrent] = useState(() => parseCurrentView())

  useEffect(() => {
    const handlePopState = () => {
      setCurrent(parseCurrentView())
    }
    window.addEventListener('popstate', handlePopState)
    return () => window.removeEventListener('popstate', handlePopState)
  }, [])

  const navigateTo = (nextView: ActiveNavView) => {
    setCurrent({ view: nextView })
    const params = new URLSearchParams(window.location.search)
    params.delete('report')
    if (nextView === 'dashboard') {
      params.delete('view')
    } else {
      params.set('view', nextView)
    }
    const query = params.toString()
    window.history.pushState(null, '', `${window.location.pathname}${query ? `?${query}` : ''}`)
  }

  // 1. Company Report
  if (current.view === 'companyReport') {
    return <CompanyReportPage config={config} onEditConnection={onEditConnection} />
  }

  // 2. Memos Management
  if (current.view === 'memos') {
    return (
      <AppLayout currentView="memos" onNavigate={navigateTo}>
        <MemosPage />
      </AppLayout>
    )
  }

  // 3. Review Queue
  if (current.view === 'review') {
    return (
      <AppLayout currentView="review" onNavigate={navigateTo}>
        <ReviewQueuePage />
      </AppLayout>
    )
  }

  // 4. Incomplete Indicators Queue
  if (current.view === 'incomplete') {
    return (
      <AppLayout currentView="incomplete" onNavigate={navigateTo}>
        <IncompleteIndicatorsPage />
      </AppLayout>
    )
  }

  // 5. Reports Hub
  if (current.view === 'reports') {
    return (
      <AppLayout currentView="reports" onNavigate={navigateTo}>
        <ReportsHubPage />
      </AppLayout>
    )
  }

  // 6. Master Data
  if (current.view === 'master') {
    return (
      <AppLayout currentView="master" onNavigate={navigateTo}>
        <MasterDataPage />
      </AppLayout>
    )
  }

  // 7. Admin Console
  if (current.view === 'admin') {
    return (
      <AppLayout currentView="admin" onNavigate={navigateTo}>
        <AdminConsolePage />
      </AppLayout>
    )
  }

  // 8. Advanced Analytics Dashboard
  if (current.view === 'analytics') {
    return <LegacyDashboard config={config} onEditConnection={onEditConnection} />
  }

  // 9. Central Dashboard
  if (current.view === 'central') {
    return <CentralDashboard config={config} onEditConnection={onEditConnection} />
  }

  // 10. Default Landing View: Simple, Elegant MainPage
  return (
    <AppLayout currentView="dashboard" onNavigate={navigateTo}>
      <MainPage onNavigate={navigateTo} />
    </AppLayout>
  )
}
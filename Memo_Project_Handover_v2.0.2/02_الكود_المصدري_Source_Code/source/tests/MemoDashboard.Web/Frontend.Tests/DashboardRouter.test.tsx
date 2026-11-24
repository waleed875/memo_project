import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { LanguageProvider } from '../../../src/MemoDashboard.Web/Frontend/src/i18n/LanguageProvider'
import { ThemeProvider } from '../../../src/MemoDashboard.Web/Frontend/src/theme/ThemeProvider'
import { V1, V2, V3, normalizeDashboardVersion } from '../../../src/MemoDashboard.Web/Frontend/src/DashboardRouter'

const { mainMock, legacyMock, centralMock, reportMock } = vi.hoisted(() => ({
  mainMock: vi.fn(),
  legacyMock: vi.fn(),
  centralMock: vi.fn(),
  reportMock: vi.fn(),
}))

vi.mock('../../../src/MemoDashboard.Web/Frontend/src/pages/MainPage', () => ({
  MainPage: (props: unknown) => {
    mainMock(props)
    return <div>main page</div>
  },
}))

vi.mock('../../../src/MemoDashboard.Web/Frontend/src/dashboard/legacy/LegacyDashboard', () => ({
  LegacyDashboard: (props: unknown) => {
    legacyMock(props)
    return <div>legacy dashboard</div>
  },
}))

vi.mock('../../../src/MemoDashboard.Web/Frontend/src/dashboard/central/CentralDashboard', () => ({
  CentralDashboard: (props: unknown) => {
    centralMock(props)
    return <div>central dashboard</div>
  },
}))

vi.mock('../../../src/MemoDashboard.Web/Frontend/src/dashboard/company/CompanyReportPage', () => ({
  CompanyReportPage: (props: unknown) => {
    reportMock(props)
    return <div>company report</div>
  },
}))

const { DashboardRouter } = await import('../../../src/MemoDashboard.Web/Frontend/src/DashboardRouter')

describe('normalizeDashboardVersion', () => {
  it('recognizes stored compatibility versions without selecting a visual route', () => {
    expect(normalizeDashboardVersion(2)).toBe(V2)
    expect(normalizeDashboardVersion(3)).toBe(V3)
    expect(normalizeDashboardVersion(undefined)).toBe(V1)
    expect(normalizeDashboardVersion(null)).toBe(V1)
    expect(normalizeDashboardVersion(99)).toBe(V1)
  })
})

describe('DashboardRouter', () => {
  beforeEach(() => {
    window.history.replaceState({}, '', '/')
    mainMock.mockReset().mockImplementation(() => <div>main page</div>)
    legacyMock.mockReset().mockImplementation(() => <div>legacy dashboard</div>)
    centralMock.mockReset()
    reportMock.mockReset()
  })

  it('routes authenticated dashboard traffic to the clean main page by default', () => {
    const versions: Array<number | null> = [null, 1, 2, 3, 99]
    for (const version of versions) {
      const config = version === null ? null : { host: 'h', port: 5432, database: 'd', username: 'u', dashboardVersion: version }
      const { unmount } = render(
        <ThemeProvider>
          <LanguageProvider>
            <DashboardRouter config={config} onEditConnection={vi.fn()} />
          </LanguageProvider>
        </ThemeProvider>
      )
      expect(screen.getByText('main page')).toBeInTheDocument()
      unmount()
    }
    expect(mainMock).toHaveBeenCalledTimes(versions.length)
  })

  it('routes ?view=analytics to the legacy dashboard', () => {
    window.history.replaceState({}, '', '/?view=analytics')
    render(
      <ThemeProvider>
        <LanguageProvider>
          <DashboardRouter config={null} onEditConnection={vi.fn()} />
        </LanguageProvider>
      </ThemeProvider>
    )
    expect(screen.getByText('legacy dashboard')).toBeInTheDocument()
    expect(mainMock).not.toHaveBeenCalled()
  })

  it('routes ?view=central to the central board without touching legacy', () => {
    window.history.replaceState({}, '', '/?view=central&centralTab=review')
    render(
      <ThemeProvider>
        <LanguageProvider>
          <DashboardRouter config={null} onEditConnection={vi.fn()} />
        </LanguageProvider>
      </ThemeProvider>
    )
    expect(screen.getByText('central dashboard')).toBeInTheDocument()
    expect(legacyMock).not.toHaveBeenCalled()
  })

  it('keeps routing ?report=company to the company report page', () => {
    window.history.replaceState({}, '', '/?report=company&companyId=company-a')
    render(
      <ThemeProvider>
        <LanguageProvider>
          <DashboardRouter config={null} onEditConnection={vi.fn()} />
        </LanguageProvider>
      </ThemeProvider>
    )
    expect(screen.getByText('company report')).toBeInTheDocument()
    expect(legacyMock).not.toHaveBeenCalled()
    expect(centralMock).not.toHaveBeenCalled()
  })

  it('prefers the company report when both view=central and report=company are present', () => {
    window.history.replaceState({}, '', '/?view=central&centralTab=companies&report=company&companyId=company-a')
    render(
      <ThemeProvider>
        <LanguageProvider>
          <DashboardRouter config={null} onEditConnection={vi.fn()} />
        </LanguageProvider>
      </ThemeProvider>
    )
    expect(screen.getByText('company report')).toBeInTheDocument()
    expect(centralMock).not.toHaveBeenCalled()
    expect(legacyMock).not.toHaveBeenCalled()
  })
})

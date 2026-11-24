import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LegacyDashboard } from '../../../src/MemoDashboard.Web/Frontend/src/dashboard/legacy/LegacyDashboard'
import { LanguageProvider } from '../../../src/MemoDashboard.Web/Frontend/src/i18n/LanguageProvider'
import { ThemeProvider } from '../../../src/MemoDashboard.Web/Frontend/src/theme/ThemeProvider'
import { EMPTY_FILTERS } from '../../../src/MemoDashboard.Web/Frontend/src/components/FilterBar'
import type { DashboardSnapshot } from '../../../src/MemoDashboard.Web/Frontend/src/types/dashboard'

const { apiMock, KpiSectionsMock, ChartSectionsMock, RecentNotesTableMock } = vi.hoisted(() => ({
  apiMock: {
    getSnapshot: vi.fn(),
    getLookups: vi.fn(),
  },
  KpiSectionsMock: vi.fn(),
  ChartSectionsMock: vi.fn(),
  RecentNotesTableMock: vi.fn(),
}))

vi.mock('../../../src/MemoDashboard.Web/Frontend/src/services/api', () => ({ api: apiMock }))

vi.mock('../../../src/MemoDashboard.Web/Frontend/src/dashboard/legacy/LegacyKpiSections', () => ({
  LegacyKpiSections: (props: unknown) => {
    KpiSectionsMock(props)
    return null
  },
}))

vi.mock('../../../src/MemoDashboard.Web/Frontend/src/dashboard/legacy/LegacyChartSections', () => ({
  LegacyChartSections: (props: unknown) => {
    ChartSectionsMock(props)
    return null
  },
}))

vi.mock('../../../src/MemoDashboard.Web/Frontend/src/components/RecentNotesTable', () => ({
  RecentNotesTable: (props: unknown) => {
    RecentNotesTableMock(props)
    return null
  },
}))

const emptyLookups = { companies: [], factories: [], productionLines: [], noteTypes: [] }

function makeHealthySnapshot(): DashboardSnapshot {
  return {
    summary: {
      totalNotes: 42,
      notesToday: 3,
      notesCurrentMonth: 20,
      activeCompaniesTotal: 7, companiesWithNotes: 1,
      approved: 10,
      pendingReview: 2,
      returnedForCorrection: 1,
      companyResend: 4,
      duplicateNumbers: 6,
      lateTotal: 2,
      lateSameMonth: 1,
      lateCrossMonth: 2,
      avgLateDays: 1,
      maxLateDays: 2,
      latestArrivalDate: null,
    },
    companies: [],
    monthlyActivity: [],
    statusDistribution: [],
    duplicateBreakdown: [],
    recentNotes: [],
    isHealthy: true,
    serverTime: '',
    generatedAt: '',
    minSample: 0,
    sla: { eligibleNotes: 0, onTimeNotes: 0, lateNotes: 0, onTimeRate: null, invalidDateOrderNotes: 0 },
    backlogByAge: [],
    incomingVsApproved: [],
    qualityByCompany: [],
    topErrorFields: [], fieldQuality: null,
    indicatorStateDistribution: [],
    performanceByFactory: [],
    performanceByLine: [],
    duplicateTrend: [],
    dataCompleteness: [],
  }
}

function renderDashboard() {
  return render(
    <ThemeProvider>
      <LanguageProvider>
        <LegacyDashboard
          config={{
            host: 'h',
            port: 5432,
            database: 'd',
            username: 'u',
            organizationName: 'ORG',
            subOrganizationName: 'SUB',
            dashboardVersion: 1,
          }}
          onEditConnection={vi.fn()}
        />
      </LanguageProvider>
    </ThemeProvider>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  window.history.replaceState(null, '', '/')
  apiMock.getLookups.mockResolvedValue(emptyLookups)
})

describe('LegacyDashboard orchestration', () => {
  it('fetches an unfiltered snapshot and feeds the extracted V1 sections', async () => {
    const snapshot = makeHealthySnapshot()
    apiMock.getSnapshot.mockResolvedValue(snapshot)

    renderDashboard()

    await waitFor(() => expect(apiMock.getSnapshot).toHaveBeenCalledWith(EMPTY_FILTERS, expect.any(AbortSignal)))
    expect(await screen.findByText('ORG')).toBeInTheDocument()
    expect(screen.getByText('SUB')).toBeInTheDocument()

    await waitFor(() => expect(KpiSectionsMock).toHaveBeenCalledTimes(1))
    const kpiProps = KpiSectionsMock.mock.calls[0][0] as { group: string; summary: DashboardSnapshot['summary']; compact: boolean }
    expect(kpiProps.group).toBe('summary')
    expect(kpiProps.summary.totalNotes).toBe(42)
    expect(kpiProps.compact).toBe(false)

    expect(ChartSectionsMock).toHaveBeenCalledWith(expect.objectContaining({ group: 'summary', snapshot, compact: false }))
    expect(RecentNotesTableMock).not.toHaveBeenCalled()
    expect(apiMock.getLookups).toHaveBeenCalledTimes(1)
  })

  it('waits for lookups and clears lookup-dependent filters when lookups fail in display mode', async () => {
    let rejectLookups!: (reason?: unknown) => void
    apiMock.getLookups.mockReturnValue(new Promise<never>((_, reject) => { rejectLookups = reject }))
    apiMock.getSnapshot.mockResolvedValue(makeHealthySnapshot())
    window.history.replaceState(null, '', '/?display=1&companyId=99999999-9999-4999-8999-999999999999&factoryId=88888888-8888-4888-8888-888888888888&productionLineId=77777777-7777-4777-8777-777777777777&noteTypeId=66666666-6666-4666-8666-666666666666')

    renderDashboard()

    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(apiMock.getSnapshot).not.toHaveBeenCalled()

    rejectLookups(new Error('lookups unavailable'))

    await waitFor(() => expect(apiMock.getSnapshot).toHaveBeenCalledWith(EMPTY_FILTERS, expect.any(AbortSignal)))
    expect(window.location.search).not.toContain('companyId=')
    expect(window.location.search).not.toContain('factoryId=')
    expect(window.location.search).not.toContain('productionLineId=')
    expect(window.location.search).not.toContain('noteTypeId=')
  })
  it('defaults to the Summary section and marks it selected', async () => {
    apiMock.getSnapshot.mockResolvedValue(makeHealthySnapshot())

    renderDashboard()

    await waitFor(() => expect(KpiSectionsMock).toHaveBeenCalledTimes(1))
    const summaryTab = screen.getByRole('tab', { name: 'ملخص' })
    expect(summaryTab).toHaveAttribute('aria-selected', 'true')
    expect(screen.getAllByRole('tab')).toHaveLength(5)
    expect(KpiSectionsMock).toHaveBeenCalledWith(expect.objectContaining({ group: 'summary' }))
    expect(ChartSectionsMock).toHaveBeenCalledWith(expect.objectContaining({ group: 'summary' }))
  })

  it('switches sections through accessible tabs without re-fetching the snapshot', async () => {
    apiMock.getSnapshot.mockResolvedValue(makeHealthySnapshot())

    renderDashboard()
    await waitFor(() => expect(KpiSectionsMock).toHaveBeenCalledTimes(1))
    const snapshotCalls = apiMock.getSnapshot.mock.calls.length

    fireEvent.click(screen.getByRole('tab', { name: 'التأخيرات' }))

    await waitFor(() => expect(KpiSectionsMock).toHaveBeenCalledWith(expect.objectContaining({ group: 'delays' })))
    expect(ChartSectionsMock).toHaveBeenCalledWith(expect.objectContaining({ group: 'delays' }))
    expect(screen.getByRole('tab', { name: 'التأخيرات' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'ملخص' })).toHaveAttribute('aria-selected', 'false')
    expect(apiMock.getSnapshot.mock.calls.length).toBe(snapshotCalls)

    fireEvent.click(screen.getByRole('tab', { name: 'أحدث المذكرات' }))
    await waitFor(() => expect(RecentNotesTableMock).toHaveBeenCalledTimes(1))
    expect(RecentNotesTableMock).toHaveBeenCalledWith(expect.objectContaining({ notes: [] }))
    expect(apiMock.getSnapshot.mock.calls.length).toBe(snapshotCalls)
  })

  it('supports keyboard tab navigation with arrow keys', async () => {
    apiMock.getSnapshot.mockResolvedValue(makeHealthySnapshot())

    renderDashboard()
    await waitFor(() => expect(KpiSectionsMock).toHaveBeenCalledTimes(1))

    fireEvent.keyDown(screen.getByRole('tab', { name: 'ملخص' }), { key: 'ArrowRight' })

    // RTL: السهم الأيمن يتحرك بصريًا لليمين (القسم السابق = الأحدث).
    await waitFor(() => expect(RecentNotesTableMock).toHaveBeenCalled())
    expect(screen.getByRole('tab', { name: 'أحدث المذكرات' })).toHaveAttribute('aria-selected', 'true')
    expect(document.getElementById('legacy-tab-recent')).toHaveFocus()
    fireEvent.keyDown(screen.getByRole('tab', { name: 'أحدث المذكرات' }), { key: 'ArrowLeft' })
    await waitFor(() => expect(screen.getByRole('tab', { name: 'ملخص' })).toHaveAttribute('aria-selected', 'true'))
  })

  it('shows the offline banner when the snapshot reports an unhealthy connection', async () => {
    apiMock.getSnapshot.mockResolvedValue({ ...makeHealthySnapshot(), isHealthy: false, errorMessage: 'db-down' })

    renderDashboard()

    expect(await screen.findByText(/db-down/)).toBeInTheDocument()
    await waitFor(() => expect(KpiSectionsMock).not.toHaveBeenCalled())
  })

  it('returns from print mode after the browser print event', async () => {
    apiMock.getSnapshot.mockResolvedValue(makeHealthySnapshot())

    renderDashboard()
    await screen.findByRole('tab', { name: 'ملخص' })

    fireEvent(window, new Event('beforeprint'))
    expect(screen.queryByRole('tab')).not.toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'ملخص' })).toBeInTheDocument()

    fireEvent(window, new Event('afterprint'))
    expect(await screen.findByRole('tab', { name: 'ملخص' })).toBeInTheDocument()
  })

  it('uses labeled regions rather than orphaned tab panels in display mode', async () => {
    apiMock.getSnapshot.mockResolvedValue(makeHealthySnapshot())
    window.history.replaceState(null, '', '/?display=1')

    renderDashboard()

    expect(await screen.findByRole('region', { name: 'ملخص' })).toBeInTheDocument()
    expect(screen.queryByRole('tab')).not.toBeInTheDocument()
  })})


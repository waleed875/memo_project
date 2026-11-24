import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { ModernDashboard } from '../../../src/MemoDashboard.Web/Frontend/src/dashboard/modern/ModernDashboard'
import { LanguageProvider } from '../../../src/MemoDashboard.Web/Frontend/src/i18n/LanguageProvider'
import { ThemeProvider } from '../../../src/MemoDashboard.Web/Frontend/src/theme/ThemeProvider'
import { EMPTY_FILTERS } from '../../../src/MemoDashboard.Web/Frontend/src/components/FilterBar'
import type { DashboardSnapshot } from '../../../src/MemoDashboard.Web/Frontend/src/types/dashboard'

const { apiMock, insightGridMock, recentNotesMock } = vi.hoisted(() => ({
  apiMock: { getSnapshot: vi.fn(), getLookups: vi.fn() },
  insightGridMock: vi.fn(),
  recentNotesMock: vi.fn(),
}))

vi.mock('../../../src/MemoDashboard.Web/Frontend/src/services/api', () => ({ api: apiMock }))
vi.mock('../../../src/MemoDashboard.Web/Frontend/src/dashboard/modern/ModernInsightGrid', () => ({
  ModernInsightGrid: (props: unknown) => { insightGridMock(props); return <div data-testid="modern-insights" /> },
}))
vi.mock('../../../src/MemoDashboard.Web/Frontend/src/components/RecentNotesTable', () => ({
  RecentNotesTable: (props: unknown) => { recentNotesMock(props); return <div data-testid="recent-notes" /> },
}))

const lookups = { companies: [], factories: [], productionLines: [], noteTypes: [] }

function snapshot(): DashboardSnapshot {
  return {
    summary: { totalNotes: 42, notesToday: 3, notesCurrentMonth: 20, activeCompaniesTotal: 7, companiesWithNotes: 1, approved: 10, pendingReview: 2, returnedForCorrection: 1, companyResend: 4, duplicateNumbers: 6, lateTotal: 2, lateSameMonth: 1, lateCrossMonth: 2, onTime: 10, avgLateDays: 1, maxLateDays: 2, latestArrivalDate: '2026-08-05' },
    companies: [{ name: 'Company A', noteCount: 20 }], monthlyActivity: [{ month: '2026-08', monthLabel: 'August 2026', count: 20 }], statusDistribution: [], duplicateBreakdown: [], recentNotes: [], isHealthy: true, serverTime: '', generatedAt: '', minSample: 0,
    sla: { eligibleNotes: 12, onTimeNotes: 10, lateNotes: 2, onTimeRate: 83.33, invalidDateOrderNotes: 0 }, companyMetrics: {} as any, fieldQuality: { totalFieldCount: 10, correctFieldCount: 7, errorFieldCount: 2, unknownFieldCount: 1, accuracyRate: 70 }, backlogByAge: [], incomingVsApproved: [], qualityByCompany: [], topErrorFields: [], indicatorStateDistribution: [], performanceByFactory: [], performanceByLine: [], duplicateTrend: [], dataCompleteness: [],
  }
}

function renderDashboard() {
  return render(<ThemeProvider><LanguageProvider><ModernDashboard config={{ host: 'h', port: 5432, database: 'd', username: 'u', organizationName: 'ORG', subOrganizationName: 'SUB', dashboardVersion: 2 }} onEditConnection={vi.fn()} /></LanguageProvider></ThemeProvider>)
}

beforeEach(() => {
  vi.clearAllMocks()
  window.history.replaceState(null, '', '/')
  apiMock.getLookups.mockResolvedValue(lookups)
})

describe('ModernDashboard', () => {
  it('renders the executive decision center and intentionally omits operational recent notes', async () => {
    const current = snapshot()
    apiMock.getSnapshot.mockResolvedValue(current)

    renderDashboard()

    await waitFor(() => expect(apiMock.getSnapshot).toHaveBeenCalledWith(EMPTY_FILTERS, expect.any(AbortSignal)))
    expect(await screen.findByRole('heading', { name: 'صورة الشركة في دقيقة' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'أهم مصادر الضغط' })).toBeInTheDocument()
    expect(screen.getByText('ORG')).toBeInTheDocument()
    await waitFor(() => expect(insightGridMock).toHaveBeenCalledWith({ snapshot: current }))
    expect(recentNotesMock).not.toHaveBeenCalled()
  })

  it('waits for lookups and clears lookup-dependent filters when lookups fail in display mode', async () => {
    let rejectLookups!: (reason?: unknown) => void
    apiMock.getLookups.mockReturnValue(new Promise<never>((_, reject) => { rejectLookups = reject }))
    apiMock.getSnapshot.mockResolvedValue(snapshot())
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
})
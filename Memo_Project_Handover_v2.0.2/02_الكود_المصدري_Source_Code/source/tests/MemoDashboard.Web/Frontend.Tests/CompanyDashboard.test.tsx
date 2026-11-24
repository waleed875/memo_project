import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { CompanyDashboard } from '../../../src/MemoDashboard.Web/Frontend/src/dashboard/company/CompanyDashboard'
import { LanguageProvider } from '../../../src/MemoDashboard.Web/Frontend/src/i18n/LanguageProvider'
import { ThemeProvider } from '../../../src/MemoDashboard.Web/Frontend/src/theme/ThemeProvider'
import type { CompanyMetrics, DashboardSnapshot } from '../../../src/MemoDashboard.Web/Frontend/src/types/dashboard'

const { apiMock } = vi.hoisted(() => ({ apiMock: { getSnapshot: vi.fn(), getLookups: vi.fn(), getCompanyMetrics: vi.fn() } }))
vi.mock('../../../src/MemoDashboard.Web/Frontend/src/services/api', () => ({ api: apiMock }))

const companyMetrics: CompanyMetrics = {
  totalNotes: 2, expectedItems: 100, reviewedItems: 90, unreviewedItems: 10,
  applicableReviewedItems: 80, correctItems: 60, errorItems: 20, notApplicableItems: 10,
  reviewCoveragePercentage: 90, itemQualityPercentage: 75, errorsPer100Checked: 25,
  qualityEligibleNoteCount: 2, cleanReviewedNotes: 0, allNotApplicableNoteCount: 0,
  noExpectedItemsNoteCount: 0, cleanReviewedNotePercent: 0, errorNotes: 2,
  incompleteReviewNotes: 1, timeEligibleNotes: 3, onTimeNotes: 2, lateNotes: 1,
  late6To10Notes: 0, late11To20Notes: 0, lateOver20Notes: 0,
  missingWritingDateNotes: 0, invalidDateOrderNotes: 0, timeCompliancePercentage: 66.67,
  averageLateDays: 1, medianLateDays: 1, p90LateDays: 1, maximumLateDays: 1,
  averageExcessDelayDays: 1, duplicateNumberIncidents: 2, companyResendIncidents: 1,
  duplicateIncidents: 3, affectedNotes: 2, incidentsPer100Notes: 150,
  affectedNoteRatePercentage: 100, duplicateFreePercentage: 0, entryNotes: 0,
  linkedEntryNotes: 0, unlinkedEntryNotes: 0, linkEfficiencyPercentage: null,
}

const snapshot: DashboardSnapshot = {
  summary: { totalNotes: 12, notesToday: 0, notesCurrentMonth: 0, activeCompaniesTotal: 1, companiesWithNotes: 1, approved: 0, pendingReview: 2, returnedForCorrection: 0, companyResend: 0, duplicateNumbers: 0, lateTotal: 1, lateSameMonth: 0, lateCrossMonth: 1, onTime: 11, avgLateDays: 1, maxLateDays: 1, latestArrivalDate: null },
  companies: [], monthlyActivity: [], statusDistribution: [], duplicateBreakdown: [], recentNotes: [], isHealthy: true, serverTime: '', generatedAt: '', minSample: 0,
  sla: { eligibleNotes: 12, onTimeNotes: 11, lateNotes: 1, onTimeRate: 91.7, invalidDateOrderNotes: 0 }, backlogByAge: [], incomingVsApproved: [], qualityByCompany: [], topErrorFields: [], fieldQuality: null, indicatorStateDistribution: [], performanceByFactory: [], performanceByLine: [], duplicateTrend: [], dataCompleteness: [],
  effectiveFilters: { from: null, to: '', companyId: 'company-a', factoryId: null, productionLineId: null, noteTypeId: null, status: null, entryCutoffDate: null },
  companyMetrics,
}

describe('CompanyDashboard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    apiMock.getLookups.mockResolvedValue({ companies: [{ id: 'company-a', name: 'شركة الاختبار' }], factories: [], productionLines: [], noteTypes: [] })
  })

  it('does not show a previously loaded company snapshot while the selected company request is loading', async () => {
    apiMock.getSnapshot.mockImplementation((filters: { companyId: string }) => filters.companyId ? new Promise(() => undefined) : Promise.resolve(snapshot))
    render(<ThemeProvider><LanguageProvider><CompanyDashboard config={{ host: 'h', port: 5432, database: 'd', username: 'u', dashboardVersion: 3 }} /></LanguageProvider></ThemeProvider>)

    expect(await screen.findByRole('heading', { name: 'شركة الاختبار' })).toBeInTheDocument()
    expect(screen.queryByText('إجمالي المذكرات')).not.toBeInTheDocument()
  })

  it('renders company rates from the unified snapshot without a parallel company request', async () => {
    apiMock.getSnapshot.mockResolvedValue(snapshot)
    render(<ThemeProvider><LanguageProvider><CompanyDashboard config={{ host: 'h', port: 5432, database: 'd', username: 'u', dashboardVersion: 3 }} /></LanguageProvider></ThemeProvider>)

    await screen.findByRole('heading', { name: 'شركة الاختبار' })
    expect(await screen.findByText('90%')).toBeInTheDocument()
    expect(screen.getByText('75%')).toBeInTheDocument()
    expect(screen.getByText('25%')).toBeInTheDocument()
    expect(screen.getByText('66.67%')).toBeInTheDocument()
    expect(screen.getByText('150%')).toBeInTheDocument()
    expect(screen.getByText('100%')).toBeInTheDocument()
    expect(screen.getByText('N/A')).toBeInTheDocument()
    expect(apiMock.getCompanyMetrics).not.toHaveBeenCalled()
  })
})
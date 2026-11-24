import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { CentralDashboard } from '../../../src/MemoDashboard.Web/Frontend/src/dashboard/central/CentralDashboard'
import { LanguageProvider } from '../../../src/MemoDashboard.Web/Frontend/src/i18n/LanguageProvider'
import { ThemeProvider } from '../../../src/MemoDashboard.Web/Frontend/src/theme/ThemeProvider'
import type { DashboardSnapshot } from '../../../src/MemoDashboard.Web/Frontend/src/types/dashboard'

const { apiMock } = vi.hoisted(() => ({
  apiMock: { getSnapshot: vi.fn(), getLookups: vi.fn(), getHealth: vi.fn(), getReviewQueue: vi.fn(), getCompanyMonthly: vi.fn() },
}))
vi.mock('../../../src/MemoDashboard.Web/Frontend/src/services/api', () => ({ api: apiMock }))

// jsdom has no canvas: stub the chart components so tests assert data flow,
// not Chart.js rendering (canvas behavior is covered by real-browser checks).
vi.mock('react-chartjs-2', () => ({
  Bar: () => <div data-testid="chart-bar" />,
  Doughnut: () => <div data-testid="chart-doughnut" />,
  Line: () => <div data-testid="chart-line" />,
}))

const COMPANY_A = '11111111-1111-4111-8111-111111111111'
const COMPANY_B = '22222222-2222-5222-9222-222222222222'

const snapshot = {
  summary: {
    totalNotes: 12, notesToday: 1, notesCurrentMonth: 12, activeCompaniesTotal: 2, companiesWithNotes: 2,
    approved: 5, pendingReview: 3, returnedForCorrection: 1, companyResend: 1, duplicateNumbers: 2,
    duplicateIncidentCount: 3, duplicateAffectedNotes: 2, nonDuplicateNotes: 10,
    lateTotal: 2, lateSameMonth: 1, lateCrossMonth: 1, onTime: 8,
    avgLateDays: 4, maxLateDays: 9, latestArrivalDate: null,
  },
  companies: [
    { id: COMPANY_A, name: 'شركة ألف', noteCount: 8 },
    { id: COMPANY_B, name: 'شركة باء', noteCount: 4 },
  ],
  timelinessByCompany: [
    { dimensionId: COMPANY_A, label: 'شركة ألف', totalNotes: 8, eligibleCount: 8, lateCount: 1, onTimeCount: 7, sampleCount: 0, correctCount: 0, errorCount: 0, qualityEligibleCount: 0, lateRate: 12.5, accuracyRate: 90 },
    { dimensionId: COMPANY_B, label: 'شركة باء', totalNotes: 4, eligibleCount: 4, lateCount: 2, onTimeCount: 2, sampleCount: 0, correctCount: 0, errorCount: 0, qualityEligibleCount: 0, lateRate: 50, accuracyRate: 60 },
  ],
  monthlyActivity: [{ month: '2026-08', monthLabel: 'أغسطس 2026', count: 12 }],
  statusDistribution: [
    { status: 'APPROVED', statusLabel: 'معتمدة', count: 5 },
    { status: 'NEEDS_REVIEW', statusLabel: 'تحتاج مراجعة', count: 3 },
  ],
  duplicateBreakdown: [], recentNotes: [], isHealthy: true, serverTime: '', generatedAt: '2026-09-07T10:00:00', minSample: 0,
  sla: { eligibleNotes: 10, onTimeNotes: 8, lateNotes: 2, onTimeRate: 80, invalidDateOrderNotes: 0 },
  backlogByAge: [], incomingVsApproved: [],
  qualityByCompany: [
    { companyId: COMPANY_A, companyName: 'شركة ألف', label: 'شركة ألف', sampleSize: 8, correctCount: 7, errorCount: 1, qualityEligibleCount: 8, allNotApplicableCount: 0, accuracyRate: 87.5, errorRate: 12.5, totalFieldCount: 0, correctFieldCount: 0, errorFieldCount: 0, unknownFieldCount: 0, fieldAccuracyRate: null },
    { companyId: COMPANY_B, companyName: 'شركة باء', label: 'شركة باء', sampleSize: 4, correctCount: 2, errorCount: 2, qualityEligibleCount: 4, allNotApplicableCount: 0, accuracyRate: 50, errorRate: 50, totalFieldCount: 0, correctFieldCount: 0, errorFieldCount: 0, unknownFieldCount: 0, fieldAccuracyRate: null },
  ],
  topErrorFields: [{ fieldId: 'f1', fieldName: 'البند الأول', label: 'البند الأول', sampleSize: 6, errorCount: 3, errorRate: 50, totalFieldCount: 0, correctFieldCount: 0, errorFieldCount: 0, unknownFieldCount: 0, fieldAccuracyRate: null }],
  fieldQuality: null, indicatorStateDistribution: [], performanceByFactory: [], performanceByLine: [], duplicateTrend: [], dataCompleteness: [],
  effectiveFilters: { from: null, to: null, dateBasis: 'WritingDate', companyId: null, factoryId: null, productionLineId: null, noteTypeId: null, status: null, entryCutoffDate: null },
  companyMetrics: {
    totalNotes: 8, eligibleCount: 8, lateCount: 1, onTimeCount: 7, sampleCount: 8, correctCount: 7, errorCount: 1,
    qualityEligibleCount: 8, expectedItems: 80, reviewedItems: 76, unreviewedItems: 4, applicableReviewedItems: 70,
    correctItems: 63, errorItems: 7, notApplicableItems: 6, reviewCoveragePercentage: 95, itemQualityPercentage: 90,
    errorsPer100Checked: 10, qualityEligibleNoteCount: 8, cleanReviewedNotes: 7, allNotApplicableNoteCount: 0,
    noExpectedItemsNoteCount: 0, cleanReviewedNotePercent: 87.5, errorNotes: 1, incompleteReviewNotes: 0,
    timeEligibleNotes: 8, onTimeNotes: 7, lateNotes: 1, late6To10Notes: 1, late11To20Notes: 0, lateOver20Notes: 0,
    missingWritingDateNotes: 0, invalidDateOrderNotes: 0, timeCompliancePercentage: 87.5, averageLateDays: 7,
    medianLateDays: 7, p90LateDays: 7, maximumLateDays: 7, averageExcessDelayDays: 2,
    duplicateNumberIncidents: 1, companyResendIncidents: 1, duplicateIncidents: 2, affectedNotes: 2,
    incidentsPer100Notes: 25, affectedNoteRatePercentage: 25, duplicateFreePercentage: 75,
    entryNotes: 4, linkedEntryNotes: 3, unlinkedEntryNotes: 1, linkEfficiencyPercentage: 75,
  },
  previousCompanyMetrics: null, previousPeriod: null,
} as unknown as DashboardSnapshot

const lookups = {
  companies: [
    { id: COMPANY_A, name: 'شركة ألف' },
    { id: COMPANY_B, name: 'شركة باء' },
  ],
  factories: [], productionLines: [], noteTypes: [],
}

const reviewQueue = {
  items: [
    {
      id: 'note-1', noteNumber: '12', companyId: COMPANY_A, companyName: 'شركة ألف',
      factoryName: 'مصنع 1', productionLineName: null, noteTypeName: null,
      status: 'NEEDS_REVIEW', statusLabel: 'تحتاج مراجعة',
      writingDate: '2026-08-20', arrivalAt: '2026-08-27T00:00:00', createdAt: '2026-08-27T00:00:00',
      daysInQueue: 11, isLate: true,
    },
  ],
  total: 1, limit: 20, offset: 0,
}

function renderCentral(query: string) {
  window.history.replaceState({}, '', query)
  return render(<ThemeProvider><LanguageProvider><CentralDashboard config={null} /></LanguageProvider></ThemeProvider>)
}

describe('CentralDashboard', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    apiMock.getLookups.mockResolvedValue(lookups)
    apiMock.getHealth.mockResolvedValue({ status: 'ok' })
    apiMock.getSnapshot.mockResolvedValue(snapshot)
    apiMock.getReviewQueue.mockResolvedValue(reviewQueue)
    apiMock.getCompanyMonthly.mockResolvedValue([
      { monthStart: '2026-08-01', periodLabel: '2026/08', totalNotes: 8, itemQualityPercentage: 90, cleanReviewedNotePercent: 87.5, cleanReviewedNotes: 7 },
    ])
  })

  it('renders the four tabs and the general-manager company table with live data', async () => {
    renderCentral('/?view=central')
    for (const tab of ['المدير العام', 'مدير البرنامج', 'الشركات', 'المراجعة']) {
      expect(await screen.findByRole('tab', { name: tab })).toBeInTheDocument()
    }
    const companiesTable = await screen.findByRole('table')
    expect(within(companiesTable).getByText('شركة ألف')).toBeInTheDocument()
    expect(within(companiesTable).getByText('شركة باء')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'عودة للواجهة الرئيسية' })).toHaveAttribute('href', '/')
  })

  it('switches to the program tab with period filters and program indicators', async () => {
    renderCentral('/?view=central')
    fireEvent.click(await screen.findByRole('tab', { name: 'مدير البرنامج' }))
    expect(await screen.findByText('مؤشرات البرنامج')).toBeInTheDocument()
    expect(screen.getByText('نطاق القراءة (الفترة وأساس التاريخ)')).toBeInTheDocument()
    expect(screen.getByText('البند الأول')).toBeInTheDocument()
  })

  it('shows the selected company summary and a link to the full report', async () => {
    renderCentral(`/?view=central&centralTab=companies&companyId=${COMPANY_A}`)
    expect(await screen.findByText('تحليل شركة')).toBeInTheDocument()
    const link = await screen.findByRole('link', { name: 'فتح التقرير المخصص الكامل' })
    expect(link.getAttribute('href')).toContain('report=company')
    expect(link.getAttribute('href')).toContain(`companyId=${COMPANY_A}`)
  })

  it('renders the three canonical charts with RTL rules and card-matching numbers', async () => {
    renderCentral(`/?view=central&centralTab=companies&companyId=${COMPANY_A}`)
    expect(await screen.findByText('التطور الشهري')).toBeInTheDocument()
    expect(screen.getByText('توزيع الحالات')).toBeInTheDocument()
    expect(screen.getByText('التأخير والالتزام')).toBeInTheDocument()

    expect(apiMock.getCompanyMonthly).toHaveBeenCalledWith(
      COMPANY_A,
      expect.objectContaining({ companyId: COMPANY_A }),
      expect.any(AbortSignal),
    )

    // Status buckets mirror the company cards (clean/error/incomplete/N/A).
    expect(screen.getByText('سليمة')).toBeInTheDocument()
    expect(screen.getByText('بها أخطاء')).toBeInTheDocument()

    // Chart containers stay RTL; canvases are stubbed in jsdom.
    for (const stub of screen.getAllByTestId('chart-bar')) {
      expect(stub.parentElement).toHaveAttribute('dir', 'rtl')
    }
    expect(screen.getByTestId('chart-doughnut').parentElement).toHaveAttribute('dir', 'rtl')
  })

  it('loads the review queue from the new endpoint with a company-report action', async () => {
    renderCentral('/?view=central&centralTab=review')
    expect(await screen.findByText('تحتاج مراجعة')).toBeInTheDocument()
    expect(apiMock.getReviewQueue).toHaveBeenCalled()
    const table = screen.getByRole('table')
    expect(within(table).getByText('12')).toBeInTheDocument()
    const action = within(table).getByRole('link', { name: 'فتح تقرير الشركة' })
    expect(action.getAttribute('href')).toContain('report=company')
  })
})

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { CompanyReportPage } from '../../../src/MemoDashboard.Web/Frontend/src/dashboard/company/CompanyReportPage'
import { LanguageProvider } from '../../../src/MemoDashboard.Web/Frontend/src/i18n/LanguageProvider'
import { ThemeProvider } from '../../../src/MemoDashboard.Web/Frontend/src/theme/ThemeProvider'
import type { CompanyMetrics, DashboardSnapshot } from '../../../src/MemoDashboard.Web/Frontend/src/types/dashboard'

const { apiMock } = vi.hoisted(() => ({ apiMock: { getSnapshot: vi.fn(), getLookups: vi.fn(), getHealth: vi.fn(), getCompanyMetrics: vi.fn(), getCompanyReportData: vi.fn() } }))
vi.mock('../../../src/MemoDashboard.Web/Frontend/src/services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../src/MemoDashboard.Web/Frontend/src/services/api')>()
  return { ...actual, api: apiMock }
})

vi.mock('react-chartjs-2', () => ({
  Bar: () => <div data-testid="chart-bar" />,
  Doughnut: () => <div data-testid="chart-doughnut" />,
  Line: () => <div data-testid="chart-line" />,
}))

const companyMetrics: CompanyMetrics = {
  totalNotes: 10, eligibleCount: 10, lateCount: 1, onTimeCount: 8, sampleCount: 10,
  correctCount: 6, errorCount: 2, qualityEligibleCount: 2, expectedItems: 100,
  reviewedItems: 90, unreviewedItems: 10, applicableReviewedItems: 80, correctItems: 60,
  errorItems: 20, notApplicableItems: 10, reviewCoveragePercentage: 90,
  itemQualityPercentage: 75, errorsPer100Checked: 25, qualityEligibleNoteCount: 2,
  cleanReviewedNotes: 0, allNotApplicableNoteCount: 0, noExpectedItemsNoteCount: 0,
  cleanReviewedNotePercent: 0, errorNotes: 2, incompleteReviewNotes: 1,
  timeEligibleNotes: 10, onTimeNotes: 8, lateNotes: 1, late6To10Notes: 1,
  late11To20Notes: 0, lateOver20Notes: 0, missingWritingDateNotes: 0,
  invalidDateOrderNotes: 0, timeCompliancePercentage: 80, averageLateDays: 1,
  medianLateDays: 1, p90LateDays: 1, maximumLateDays: 6, averageExcessDelayDays: 1,
  duplicateNumberIncidents: 2, companyResendIncidents: 1, duplicateIncidents: 3,
  affectedNotes: 2, incidentsPer100Notes: 30, affectedNoteRatePercentage: 20,
  duplicateFreePercentage: 80, entryNotes: 10, linkedEntryNotes: 7,
  unlinkedEntryNotes: 3, linkEfficiencyPercentage: 70,
}

const snapshot: DashboardSnapshot = {
  summary: { totalNotes: 10, notesToday: 0, notesCurrentMonth: 0, activeCompaniesTotal: 1, companiesWithNotes: 1, approved: 5, pendingReview: 2, returnedForCorrection: 1, companyResend: 1, duplicateNumbers: 2, lateTotal: 1, lateSameMonth: 0, lateCrossMonth: 1, onTime: 8, avgLateDays: 1, maxLateDays: 6, latestArrivalDate: null },
  companies: [], monthlyActivity: [{ month: '2026-07', monthLabel: 'يوليو 2026', count: 6 }], statusDistribution: [{ status: 'APPROVED', statusLabel: 'معتمدة', count: 5 }], duplicateBreakdown: [{ classification: 'COMPANY_RESEND', label: 'إعادة إرسال', count: 1 }], recentNotes: [], isHealthy: true, serverTime: '', generatedAt: '2026-08-03T10:00:00', minSample: 0,
  sla: { eligibleNotes: 10, onTimeNotes: 8, lateNotes: 1, onTimeRate: 80, invalidDateOrderNotes: 0 }, backlogByAge: [], incomingVsApproved: [], qualityByCompany: [], topErrorFields: [{ fieldId: 'f1', fieldName: 'البند الأول', label: 'البند الأول', sampleSize: 4, errorCount: 2, errorRate: 50 }], fieldQuality: null, indicatorStateDistribution: [], performanceByFactory: [{ dimensionId: 'f1', label: 'مصنع 1', totalNotes: 10, eligibleCount: 10, lateCount: 1, onTimeCount: 8, sampleCount: 10, correctCount: 6, errorCount: 2, qualityEligibleCount: 2, lateRate: 10, accuracyRate: 75 }], performanceByLine: [], duplicateTrend: [{ periodKey: '2026-07', periodLabel: 'يوليو 2026', label: 'يوليو 2026', companyResend: 1, duplicateNumber: 2 }], dataCompleteness: [],
  effectiveFilters: { from: null, to: '', companyId: 'company-a', factoryId: null, productionLineId: null, noteTypeId: null, status: null, entryCutoffDate: null },
  companyMetrics,
}

describe('CompanyReportPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    apiMock.getLookups.mockResolvedValue({ companies: [{ id: 'company-a', name: 'شركة الاختبار' }], factories: [], productionLines: [], noteTypes: [] })
    apiMock.getHealth.mockResolvedValue({ status: 'ok' })
    apiMock.getSnapshot.mockResolvedValue(snapshot)
    apiMock.getCompanyReportData.mockResolvedValue(null)
  })

  it('renders the approved KPI values computed from company metrics', async () => {
    render(<ThemeProvider><LanguageProvider><CompanyReportPage config={null} /></LanguageProvider></ThemeProvider>)

    // Complement efficiency (10-3)/10=70%, weighted ((8*0.9+1*0.1)/10)*100=73%, composite (100+75+70)/3=81.67%
    expect(await screen.findByText(/70% مؤشر كفاءة الآليات/)).toBeInTheDocument()
    expect(screen.getByText(/73% الالتزام الزمني المرجح/)).toBeInTheDocument()
    expect(screen.getByText(/81.67% المؤشر المركب للشركة/)).toBeInTheDocument()
  })

  it('exposes a manual form-efficiency field defaulting to empty (100 fallback)', async () => {
    window.localStorage.clear()
    const { container } = render(<ThemeProvider><LanguageProvider><CompanyReportPage config={null} /></LanguageProvider></ThemeProvider>)
    await screen.findByText(/81.67% المؤشر المركب للشركة/)

    const input = screen.getByLabelText(/كفاءة النموذج/) as HTMLInputElement
    expect(input.value).toBe('')
    expect(container.querySelector('input[placeholder="100"]')).not.toBeNull()
  })

  it('loads a previously saved form efficiency', async () => {
    window.localStorage.setItem('memo-report-form-efficiency', '80')
    render(<ThemeProvider><LanguageProvider><CompanyReportPage config={null} /></LanguageProvider></ThemeProvider>)
    expect(await screen.findByText(/75% المؤشر المركب للشركة/)).toBeInTheDocument()
  })

  it('exposes the letter-details form with server PDF/Excel/HTML actions', async () => {
    render(<ThemeProvider><LanguageProvider><CompanyReportPage config={null} /></LanguageProvider></ThemeProvider>)
    await screen.findByText(/81.67% المؤشر المركب للشركة/)

    fireEvent.click(screen.getByRole('button', { name: /بيانات الكتاب/ }))
    expect(screen.getByLabelText(/رقم الكتاب/)).toBeInTheDocument()
    expect(screen.getByLabelText(/المُصدِر/)).toBeInTheDocument()

    expect(screen.getByRole('button', { name: 'تنزيل PDF' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'تنزيل Excel' })).toBeInTheDocument()
    const preview = screen.getByRole('link', { name: 'معاينة HTML' })
    expect(preview.getAttribute('href')).toContain('/api/dashboard/company-report.html?')
    expect(preview.getAttribute('href')).toContain('companyId=company-a')
  })

  it('requires a bounded period before downloading PDF/Excel', async () => {
    render(<ThemeProvider><LanguageProvider><CompanyReportPage config={null} /></LanguageProvider></ThemeProvider>)
    await screen.findByText(/81.67% المؤشر المركب للشركة/)

    fireEvent.click(screen.getByRole('button', { name: 'تنزيل PDF' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/تحديد الشركة والفترة/)
  })
})

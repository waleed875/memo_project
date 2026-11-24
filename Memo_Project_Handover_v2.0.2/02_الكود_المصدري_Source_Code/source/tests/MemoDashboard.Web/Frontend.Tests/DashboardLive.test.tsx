import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { DashboardLive } from '../../../src/MemoDashboard.Web/Frontend/src/dashboard/DashboardLive'
import { LanguageProvider } from '../../../src/MemoDashboard.Web/Frontend/src/i18n/LanguageProvider'
import { ThemeProvider } from '../../../src/MemoDashboard.Web/Frontend/src/theme/ThemeProvider'
import type { DashboardSnapshot } from '../../../src/MemoDashboard.Web/Frontend/src/types/dashboard'

const { apiMock, hookMock } = vi.hoisted(() => ({
  apiMock: { getLookups: vi.fn(), getSnapshot: vi.fn() },
  hookMock: vi.fn(),
}))

vi.mock('../../../src/MemoDashboard.Web/Frontend/src/services/api', () => ({ api: apiMock }))
vi.mock('../../../src/MemoDashboard.Web/Frontend/src/hooks/useDashboardData', () => ({ useDashboardData: hookMock }))

const snapshot = {
  views: {
    context: { period: { from: '2026-07-01', to: '2026-08-03', entryCutoffDate: null }, scope: { companyId: null, factoryId: null, productionLineId: null, noteTypeId: null, status: null } },
    executiveSummary: {
      availability: { state: 'available', hasRows: true, explanation: null },
      summary: { totalNotes: 7, notesToday: 1, notesCurrentMonth: 6, activeCompaniesTotal: 8, companiesWithNotes: 2, approved: 2, pendingReview: 3, returnedForCorrection: 1, companyResend: 1, duplicateNumbers: 2, lateTotal: 2, lateSameMonth: 1, lateCrossMonth: 1, onTime: 4, avgLateDays: 8.5, maxLateDays: 9, latestArrivalDate: '2026-08-10' },
      sla: { eligibleNotes: 6, onTimeNotes: 4, lateNotes: 2, onTimeRate: 66.67, invalidDateOrderNotes: 0 },
      metrics: { reviewCoveragePercentage: 100, itemQualityPercentage: 75, correctItems: 6, applicableReviewedItems: 8, reviewedItems: 8, expectedItems: 8, errorNotes: 1, qualityEligibleNoteCount: 4, cleanReviewedNotes: 3, cleanReviewedNotePercent: 75, errorsPer100Checked: 25, affectedNoteRatePercentage: 25, linkEfficiencyPercentage: 0, incompleteReviewNotes: 1, duplicateIncidents: 3, companyResendIncidents: 1, duplicateNumberIncidents: 2 },
      fieldQuality: { totalFieldCount: 8, correctFieldCount: 6, errorFieldCount: 2, unknownFieldCount: 0, accuracyRate: 75 },
      companyComparison: [{ id: 'a', name: 'شركة الأمل', noteCount: 4 }, { id: 'b', name: 'شركة النور', noteCount: 3 }, { id: 'c', name: 'شركة بلا مذكرات', noteCount: 0 }],
      timelinessByCompany: [{ dimensionId: 'a', label: 'شركة الأمل', totalNotes: 4, eligibleCount: 4, lateCount: 1, onTimeCount: 3, sampleCount: 4, correctCount: 3, errorCount: 1, qualityEligibleCount: 4, lateRate: 25, accuracyRate: 75 }],
      monthlyTrend: [{ month: '2026-07', monthLabel: 'يوليو 2026', count: 1 }, { month: '2026-08', monthLabel: 'أغسطس 2026', count: 6 }],
      statusDistribution: [{ status: 'APPROVED', statusLabel: 'معتمدة', count: 2 }],
      qualityByCompany: [{ companyId: 'a', companyName: 'شركة الأمل', label: 'شركة الأمل', sampleSize: 4, correctCount: 3, errorCount: 1, accuracyRate: 75, errorRate: 25 }],
      highImpactProblems: [{ fieldId: 'item-1', fieldName: 'كمية الإنتاج', label: 'كمية الإنتاج', sampleSize: 4, errorCount: 1, errorRate: 25 }],
    },
    operationsSummary: {
      availability: { state: 'available', hasRows: true, explanation: null },
      summary: { totalNotes: 7, notesToday: 1, notesCurrentMonth: 6, activeCompaniesTotal: 8, companiesWithNotes: 2, approved: 2, pendingReview: 3, returnedForCorrection: 1, companyResend: 1, duplicateNumbers: 2, lateTotal: 2, lateSameMonth: 1, lateCrossMonth: 1, onTime: 4, avgLateDays: 8.5, maxLateDays: 9, latestArrivalDate: '2026-08-10' },
      sla: { eligibleNotes: 6, onTimeNotes: 4, lateNotes: 2, onTimeRate: 66.67, invalidDateOrderNotes: 0 },
      metrics: { reviewCoveragePercentage: 100, itemQualityPercentage: 75, correctItems: 6, applicableReviewedItems: 8, reviewedItems: 8, expectedItems: 8, errorNotes: 1, qualityEligibleNoteCount: 4, cleanReviewedNotes: 3, cleanReviewedNotePercent: 75, errorsPer100Checked: 25, affectedNoteRatePercentage: 25, linkEfficiencyPercentage: 0, incompleteReviewNotes: 1, duplicateIncidents: 3, companyResendIncidents: 1, duplicateNumberIncidents: 2 },
      incomingVsApproved: [{ periodKey: '2026-07', periodLabel: 'يوليو 2026', label: 'يوليو 2026', incoming: 1, approved: 0 }],
      backlogByAge: [{ code: 'lt-1-day', label: 'أقل من يوم', value: 4 }, { code: 'gt-7-days', label: 'أكثر من أسبوع', value: 0 }], workflowStatus: [{ status: 'APPROVED', statusLabel: 'معتمدة', count: 99 }, { status: 'DRAFT', statusLabel: 'مسودة', count: 1 }, { status: 'HAS_ERRORS', statusLabel: 'تحتوي أخطاء', count: 2 }],
      performanceByFactory: [], performanceByLine: [],
      repetitionClassification: [{ classification: 'COMPANY_RESEND', label: 'إعادة إرسال من الشركة', count: 0 }, { classification: 'COMPANY_DUPLICATE_NUMBER', label: 'تكرار رقم المذكرة', count: 2 }],
      repetitionTrend: [{ periodKey: '2026-07', periodLabel: 'يوليو 2026', label: 'يوليو 2026', companyResend: 1, duplicateNumber: 0 }],
      indicatorStates: [{ code: 1, stateLabel: 'صحيح', value: 1 }], dataCompleteness: [{ code: 'MISSING_WRITING_DATE', label: 'دون تاريخ كتابة', value: 1 }],
      topErrorFields: [], recentNotes: [],
    },
    companySummary: null,
  },
} as unknown as DashboardSnapshot

describe('DashboardLive', () => {
  beforeEach(() => {
    localStorage.clear()
    window.history.replaceState({}, '', '/?view=executive')
    apiMock.getLookups.mockResolvedValue({
      companies: [
        { id: 'a', name: 'شركة الأمل' }, { id: 'b', name: 'شركة النور' }, { id: 'c', name: 'شركة بلا مذكرات' },
        { id: 'd', name: 'شركة السهل' }, { id: 'e', name: 'شركة الرافدين' }, { id: 'f', name: 'شركة الشام' },
        { id: 'g', name: 'شركة البناء' }, { id: 'h', name: 'شركة المنارة' },
      ],
      factories: [], productionLines: [], noteTypes: [],
    })
    hookMock.mockReturnValue({ data: snapshot, isConnected: true, isLoading: false, lastUpdated: new Date('2026-08-17T10:00:00Z'), snapshotError: null, refreshNow: vi.fn() })
    apiMock.getSnapshot.mockClear()
  })

  it('renders live executive labels and reconciled values without the old translation key', async () => {
    render(<ThemeProvider><LanguageProvider><DashboardLive config={null} /></LanguageProvider></ThemeProvider>)
    expect(await screen.findByRole('heading', { name: 'المدير العام' })).toBeInTheDocument()
    expect(screen.getAllByText('شركة الأمل').length).toBeGreaterThan(0)
    expect(screen.getByText('يوليو 2026')).toBeInTheDocument()
    expect(screen.queryByText('charts.companies')).not.toBeInTheDocument()
    expect(document.querySelector('[data-chart-kind="donut"]')).toBeInTheDocument()
    expect(document.querySelector('[data-chart-kind="time-columns"]')).toBeInTheDocument()
    expect(document.querySelector('[data-chart-kind="stacked-bars"]')).toBeInTheDocument()
  })

  it('renders the independent directory with all eight companies', async () => {
    window.history.replaceState({}, '', '/?view=directory')
    render(<ThemeProvider><LanguageProvider><DashboardLive config={null} /></LanguageProvider></ThemeProvider>)
    expect(await screen.findByRole('heading', { name: 'اختيار شركة' })).toBeInTheDocument()
    expect(screen.getAllByRole('button').filter((button) => button.textContent?.includes('شركة')).length).toBeGreaterThanOrEqual(8)
    expect(screen.getByText('شركة بلا مذكرات')).toBeInTheDocument()
  })

  it('clears lookup-dependent filters before the first snapshot when lookups fail', async () => {
    const oldFilters = { from: '2026-08-01', to: '2026-08-03', companyId: '99999999-9999-4999-8999-999999999999', factoryId: '88888888-8888-4888-8888-888888888888', productionLineId: '77777777-7777-4777-8777-777777777777', noteTypeId: '66666666-6666-4666-8666-666666666666', status: 'APPROVED', entryCutoffDate: '' }
    localStorage.setItem('memo-dashboard-filters', JSON.stringify(oldFilters))
    window.history.replaceState({}, '', '/?view=executive&from=2026-08-01&to=2026-08-03&companyId=' + oldFilters.companyId + '&factoryId=' + oldFilters.factoryId + '&productionLineId=' + oldFilters.productionLineId + '&noteTypeId=' + oldFilters.noteTypeId + '&status=APPROVED')
    apiMock.getLookups.mockRejectedValue(new Error('lookups unavailable'))
    hookMock.mockImplementation((filters: typeof oldFilters, enabled: boolean) => {
      if (enabled) apiMock.getSnapshot(filters)
      return { data: snapshot, isConnected: true, isLoading: false, lastUpdated: null, snapshotError: null, refreshNow: vi.fn() }
    })

    render(<ThemeProvider><LanguageProvider><DashboardLive config={null} /></LanguageProvider></ThemeProvider>)

    await waitFor(() => expect(apiMock.getSnapshot).toHaveBeenCalledTimes(1))
    const firstFilters = apiMock.getSnapshot.mock.calls[0][0]
    expect(firstFilters).toMatchObject({ from: '2026-08-01', to: '2026-08-03', status: 'APPROVED' })
    expect(firstFilters.companyId).toBe('')
    expect(firstFilters.factoryId).toBe('')
    expect(firstFilters.productionLineId).toBe('')
    expect(firstFilters.noteTypeId).toBe('')
    expect(localStorage.getItem('memo-dashboard-filters')).toContain('"companyId":""')
  })

  it('filters the company directory while typing, clearing, and showing no-match state', async () => {
    window.history.replaceState({}, '', '/?view=directory')
    render(<ThemeProvider><LanguageProvider><DashboardLive config={null} /></LanguageProvider></ThemeProvider>)
    const input = await screen.findByRole('textbox', { name: 'بحث عن شركة' })

    fireEvent.change(input, { target: { value: 'الأمل' } })
    expect(screen.getByText('شركة الأمل')).toBeInTheDocument()
    expect(screen.queryByText('شركة النور')).not.toBeInTheDocument()

    fireEvent.change(input, { target: { value: 'لا-توجد' } })
    expect(screen.getByText('لا توجد شركة مطابقة للبحث.')).toBeInTheDocument()

    fireEvent.change(input, { target: { value: '' } })
    expect(screen.getByText('شركة النور')).toBeInTheDocument()
    expect(screen.getByText('شركة المنارة')).toBeInTheDocument()
  })

  it('keeps zero columns at zero, uses donut units/colors, and orders workflow semantically', async () => {
    window.history.replaceState({}, '', '/?view=operations')
    render(<ThemeProvider><LanguageProvider><DashboardLive config={null} /></LanguageProvider></ThemeProvider>)
    expect(await screen.findByRole('heading', { name: 'النظام والعمليات' })).toBeInTheDocument()
    expect(screen.getByText('الحالة الحالية: معتمدة')).toBeInTheDocument()
    expect(screen.getByText('اعتمادات تمت خلال الشهر')).toBeInTheDocument()
    expect(screen.queryByText('المعتمد/المراجع')).not.toBeInTheDocument()
    expect(screen.getAllByText('عدد الحالات').length).toBeGreaterThan(0)
    expect(screen.getByText('نسبة المذكرات المتأثرة بالتكرار')).toBeInTheDocument()
    expect(screen.getByText('حوادث لكل 100 مذكرة')).toBeInTheDocument()
    expect(screen.queryByText('نسبة التكرار')).not.toBeInTheDocument()
    expect(screen.queryByText('شاشة Phase C غير معبأة ببيانات ثابتة')).not.toBeInTheDocument()

    const zeroGroupedBars = Array.from(document.querySelectorAll('[data-chart-kind="grouped-columns"] .live-column-secondary'))
    expect(zeroGroupedBars.some((bar) => (bar as HTMLElement).style.height === '0%')).toBe(true)
    const zeroAgeBars = Array.from(document.querySelectorAll('[data-chart-kind="age-columns"] .live-column-age'))
    expect(zeroAgeBars.some((bar) => (bar as HTMLElement).style.height === '0%')).toBe(true)

    const duplicateLegend = Array.from(document.querySelectorAll('[data-chart-kind="donut"] .live-donut-row'))
    expect((duplicateLegend[0]?.querySelector('i') as HTMLElement)?.style.background).toBe('var(--primary)')
    expect((duplicateLegend[1]?.querySelector('i') as HTMLElement)?.style.background).toBe('var(--brand-gold)')

    const workflowLabels = Array.from(document.querySelectorAll('[data-chart-kind="workflow-stages"] .live-workflow-step span')).map((node) => node.textContent)
    expect(workflowLabels).toEqual(['مسودة', 'تحتوي أخطاء', 'معتمدة'])
  })
})



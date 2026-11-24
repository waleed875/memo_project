import { useEffect, useMemo, useState } from 'react'
import {
  ArcElement,
  BarElement,
  CategoryScale,
  Chart as ChartJS,
  Legend,
  LinearScale,
  LineElement,
  PointElement,
  Tooltip,
} from 'chart.js'
import { Bar, Doughnut, Line } from 'react-chartjs-2'
import { api, buildCompanyReportUrl, downloadCompanyReport, type CompanyReportIssueParams, type SetupConfig } from '../../services/api'
import { FilterBar, loadFiltersFromUrlOrSaved, persistFilters, sanitizeFiltersAgainstLookups } from '../../components/FilterBar'
import { Header } from '../../components/Header'
import { useLanguage } from '../../i18n/LanguageProvider'
import { OfflineBanner } from '../../components/OfflineBanner'
import { DashboardSnapshotError } from '../../components/DashboardSnapshotError'
import { SkeletonLoader } from '../../components/SkeletonLoader'
import { useDashboardData } from '../../hooks/useDashboardData'
import { useChartTheme } from '../../hooks/useChartTheme'
import { t } from '../../i18n'
import { formatDate, formatDateTime, formatNumber, formatPercent } from '../../services/format'
import { compositePercent, parseFormEfficiency, unlinkedMechanismPercent, weightedTimelinessPercent } from '../../services/kpi'
import type { CompanyMetrics, DashboardFilters, DashboardLookups } from '../../types/dashboard'
import type { CompanyReportDataModel } from '../../types/memo'
import { CompanyDetailedDocument } from '../../components/reports/CompanyDetailedDocument'

ChartJS.register(ArcElement, BarElement, CategoryScale, Legend, LinearScale, LineElement, PointElement, Tooltip)

const FORM_STORAGE_KEY = 'memo-report-form-efficiency'
const ISSUE_STORAGE_KEY = 'memo-report-issue'

function loadSavedIssue(): CompanyReportIssueParams {
  if (typeof window === 'undefined') return {}
  try {
    const raw = window.localStorage.getItem(ISSUE_STORAGE_KEY)
    if (!raw) return {}
    const parsed = JSON.parse(raw) as Partial<CompanyReportIssueParams>
    const clean: CompanyReportIssueParams = {}
    for (const key of ['title', 'letterNumber', 'city', 'hijriDate', 'gregorianDate', 'recipient', 'issuerName'] as const) {
      const value = parsed[key]
      if (typeof value === 'string' && value.trim()) clean[key] = value
    }
    return clean
  } catch {
    return {}
  }
}

function loadSavedForm(): string {
  if (typeof window === 'undefined') return ''
  try {
    return window.localStorage.getItem(FORM_STORAGE_KEY) ?? ''
  } catch {
    return ''
  }
}

function formatKpi(value: number | null): string {
  if (value === null || Number.isNaN(value)) return t('common.notAvailable')
  return formatPercent(value)
}

function formatPeriodRange(from: string, to: string): string {
  if (!from && !to) return t('report.allNotes')
  if (!from) return `${t('filters.to')} ${formatDate(to)}`
  if (!to) return `${t('filters.from')} ${formatDate(from)}`
  return `${formatDate(from)} — ${formatDate(to)}`
}

function backToCompanyHref(): string {
  const params = new URLSearchParams(window.location.search)
  params.delete('report')
  const query = params.toString()
  return `${window.location.pathname}${query ? `?${query}` : ''}`
}

function chartFont(colors: { text: string }) {
  return { family: "'Simplified Arabic','Segoe UI',Tahoma,Arial,sans-serif", size: 12, weight: 700 as const, color: colors.text }
}

interface CompanyReportPageProps {
  config?: SetupConfig | null
  onEditConnection?: () => void
}

export function CompanyReportPage({ config, onEditConnection }: CompanyReportPageProps) {
  const { language } = useLanguage()
  const [filters, setFilters] = useState<DashboardFilters>(() => loadFiltersFromUrlOrSaved())
  const [lookups, setLookups] = useState<DashboardLookups | null>(null)
  const [formInput, setFormInput] = useState<string>(() => loadSavedForm())
  const [issue, setIssue] = useState<CompanyReportIssueParams>(() => loadSavedIssue())
  const [filtersOpen, setFiltersOpen] = useState(false)
  const [issueOpen, setIssueOpen] = useState(false)
  const [downloadBusy, setDownloadBusy] = useState<'pdf' | 'xlsx' | 'docx' | null>(null)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [viewMode, setViewMode] = useState<'document' | 'analytics'>('document')
  const [reportDocData, setReportDocData] = useState<CompanyReportDataModel | null>(null)
  const [loadingDoc, setLoadingDoc] = useState(false)
  const { data, isConnected, isLoading, lastUpdated, snapshotError, refreshNow } = useDashboardData(filters, lookups !== null)
  const colors = useChartTheme()

  useEffect(() => {
    const controller = new AbortController()
    const emptyLookups = { companies: [], factories: [], productionLines: [], noteTypes: [] }
    api.getLookups(controller.signal).then((result) => {
      if (controller.signal.aborted) return
      setFilters((current) => {
        const normalized = sanitizeFiltersAgainstLookups(current, result)
        const next = !current.companyId && !normalized.companyId && result.companies.length > 0
          ? { ...normalized, companyId: result.companies[0].id }
          : normalized
        if (JSON.stringify(next) !== JSON.stringify(current)) persistFilters(next)
        return next
      })
      setLookups(result)
    }).catch(() => {
      if (controller.signal.aborted) return
      setFilters((current) => sanitizeFiltersAgainstLookups(current, emptyLookups))
      setLookups(emptyLookups)
    })
    return () => controller.abort()
  }, [])

  const selectedCompany = useMemo(
    () => lookups?.companies.find((company) => company.id === filters.companyId) ?? null,
    [filters.companyId, lookups],
  )

  const formValue = useMemo(() => parseFormEfficiency(formInput), [formInput])

  const onFormChange = (value: string) => {
    setFormInput(value)
    try {
      window.localStorage.setItem(FORM_STORAGE_KEY, value)
    } catch {
      // ignore storage failures
    }
  }

  const onIssueChange = (patch: Partial<CompanyReportIssueParams>) => {
    setIssue((current) => {
      const next = { ...current, ...patch }
      try {
        window.localStorage.setItem(ISSUE_STORAGE_KEY, JSON.stringify(next))
      } catch {
        // ignore storage failures
      }
      return next
    })
  }

  const issueParams: CompanyReportIssueParams = useMemo(
    () => ({ ...issue, formEfficiency: formInput.trim() || undefined }),
    [issue, formInput],
  )

  const basisLabel = filters.dateBasis === 'ArrivalDate' ? t('report.basisArrival') : t('report.basisWriting')

  const filterSummary = useMemo(() => {
    const parts = [formatPeriodRange(filters.from, filters.to), basisLabel]
    if (filters.entryCutoffDate) parts.push(`${t('report.cutoff')}: ${formatDate(filters.entryCutoffDate)}`)
    return parts.join(' | ')
  }, [filters.from, filters.to, filters.entryCutoffDate, basisLabel])

  const canDownload = Boolean(selectedCompany && filters.from && filters.to)

  const onDownload = async (kind: 'pdf' | 'xlsx' | 'docx') => {
    setDownloadError(null)
    if (!canDownload) {
      setDownloadError(t('report.periodRequired'))
      return
    }
    setDownloadBusy(kind)
    try {
      await downloadCompanyReport(kind, filters, issueParams)
    } catch {
      setDownloadError(t('report.downloadFailed'))
    } finally {
      setDownloadBusy(null)
    }
  }

  // Immediate local sync for presentation fields without triggering heavy SQL queries
  useEffect(() => {
    setReportDocData((prev) => {
      if (!prev) return prev
      return {
        ...prev,
        issue: {
          ...prev.issue,
          title: issue.title || '',
          letterNumber: issue.letterNumber || '',
          city: issue.city || prev.identity?.defaultCity || '',
          hijriDate: issue.hijriDate || '',
          gregorianDate: issue.gregorianDate || '',
          recipient: issue.recipient || '',
          issuerName: issue.issuerName || '',
          formEfficiencyIndex: Number(formInput) || 100,
        },
      }
    })
  }, [issue, formInput])

  useEffect(() => {
    if (!filters.companyId) {
      setReportDocData(null)
      return
    }

    const timer = setTimeout(() => {
      setLoadingDoc(true)
      const p: Record<string, string> = {
        companyId: filters.companyId,
      }
      if (filters.from) p.from = filters.from
      if (filters.to) p.to = filters.to
      if (filters.dateBasis) p.dateBasis = filters.dateBasis
      if (filters.entryCutoffDate) p.entryCutoffDate = filters.entryCutoffDate
      if (issue.title) p.title = issue.title
      if (issue.letterNumber) p.letterNumber = issue.letterNumber
      if (issue.city) p.city = issue.city
      if (issue.hijriDate) p.hijriDate = issue.hijriDate
      if (issue.gregorianDate) p.gregorianDate = issue.gregorianDate
      if (issue.recipient) p.recipient = issue.recipient
      if (issue.issuerName) p.issuerName = issue.issuerName
      if (formInput) p.formEfficiency = formInput

      if (typeof api.getCompanyReportData === 'function') {
        const maybePromise = api.getCompanyReportData(p)
        if (maybePromise && typeof maybePromise.then === 'function') {
          maybePromise
            .then((res) => {
              if (res) setReportDocData(res)
            })
            .catch((err) => {
              console.error('Failed to load company report document data', err)
            })
            .finally(() => setLoadingDoc(false))
        } else {
          setLoadingDoc(false)
        }
      } else {
        setLoadingDoc(false)
      }
    }, 200)

    return () => clearTimeout(timer)
  }, [filters])

  const handlePrint = () => {
    if (viewMode !== 'document') {
      setViewMode('document')
      setTimeout(() => window.print(), 100)
    } else {
      window.print()
    }
  }

  const isDisplayMode = typeof window !== 'undefined' && new URLSearchParams(window.location.search).get('display') === '1'

  const metrics: CompanyMetrics | null = data?.companyMetrics ?? null

  const approved = useMemo(() => {
    if (!metrics) return null
    const mechanism = unlinkedMechanismPercent(metrics.entryNotes, metrics.unlinkedEntryNotes)
    const weighted = weightedTimelinessPercent(metrics.totalNotes, metrics.onTimeNotes, metrics.lateNotes)
    return {
      items: metrics.itemQualityPercentage,
      form: formValue,
      mechanism,
      weighted,
      duplication: metrics.affectedNoteRatePercentage,
      composite: compositePercent(formValue, metrics.itemQualityPercentage, mechanism),
    }
  }, [metrics, formValue])

  const monthlyData = useMemo(() => ({
    labels: (data?.monthlyActivity ?? []).map((row) => row.monthLabel || row.month),
    datasets: [{
      label: t('report.monthlyTrend'),
      data: (data?.monthlyActivity ?? []).map((row) => row.count),
      backgroundColor: colors.primary,
      borderColor: colors.primary,
      borderWidth: 1,
    }],
  }), [data, colors])

  const qualityDistributionData = useMemo(() => {
    const clean = metrics?.cleanReviewedNotes ?? 0
    const errors = metrics?.errorNotes ?? 0
    const incomplete = metrics?.incompleteReviewNotes ?? 0
    const notApplicable = metrics?.allNotApplicableNoteCount ?? 0
    return {
      labels: ['مذكرات سليمة معتمدة', 'مذكرات بها أخطاء بنود', 'مراجعة غير مكتملة', 'غير منطبقة بالكامل'],
      datasets: [{
        data: [clean, errors, incomplete, notApplicable],
        backgroundColor: [colors.success, colors.danger, colors.warning, colors.textMuted],
        borderWidth: 1,
      }],
    }
  }, [metrics, colors])

  const delayData = useMemo(() => {
    const m = metrics
    return {
      labels: [t('charts.sla.onTime'), '6-10', '11-20', '>20'],
      datasets: [{
        label: t('report.delayDistribution'),
        data: [m?.onTimeNotes ?? 0, m?.late6To10Notes ?? 0, m?.late11To20Notes ?? 0, m?.lateOver20Notes ?? 0],
        backgroundColor: [colors.success, colors.warning, colors.warning, colors.danger],
        borderWidth: 1,
      }],
    }
  }, [metrics, colors])

  const duplicateTrendData = useMemo(() => ({
    labels: (data?.duplicateTrend ?? []).map((row) => row.periodLabel || row.label),
    datasets: [
      {
        label: t('charts.duplicateTrend.resend'),
        data: (data?.duplicateTrend ?? []).map((row) => row.companyResend),
        borderColor: colors.warning,
        backgroundColor: colors.warning,
        tension: 0.3,
      },
      {
        label: t('charts.duplicateTrend.duplicateNumber'),
        data: (data?.duplicateTrend ?? []).map((row) => row.duplicateNumber),
        borderColor: colors.danger,
        backgroundColor: colors.danger,
        tension: 0.3,
      },
    ],
  }), [data, colors])

  const topFieldsData = useMemo(() => {
    const rows = (data?.topErrorFields ?? []).slice(0, 10)
    return {
      labels: rows.map((row) => row.label),
      datasets: [{
        label: t('report.topErrorFields'),
        data: rows.map((row) => row.errorCount),
        backgroundColor: colors.danger,
        borderWidth: 1,
      }],
    }
  }, [data, colors])

  const chartOptions = useMemo(() => ({
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { rtl: true, labels: { color: colors.text, font: chartFont(colors) } },
      tooltip: { enabled: true, rtl: true },
    },
    scales: {
      x: { reverse: true, ticks: { color: colors.textMuted, font: chartFont(colors) }, grid: { color: colors.grid } },
      y: { ticks: { color: colors.textMuted, font: chartFont(colors) }, grid: { color: colors.grid }, beginAtZero: true },
    },
  }), [colors])

  const pieOptions = useMemo(() => ({
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { position: 'bottom' as const, rtl: true, labels: { color: colors.text, font: chartFont(colors) } }, tooltip: { enabled: true, rtl: true } },
  }), [colors])

  return (
    <div className="app-bg min-h-screen app-text company-report" dir={language === 'ar' ? 'rtl' : 'ltr'}>
      <Header
        isConnected={isConnected}
        isLoading={isLoading}
        lastUpdated={lastUpdated}
        onRefresh={refreshNow}
        onEditConnection={onEditConnection}
        isDisplayMode={isDisplayMode}
        organizationName={config?.organizationName}
        subOrganizationName={config?.subOrganizationName}
      />
      {!isConnected && <OfflineBanner message={t('common.connectionFailed')} />}
      <main className="company-dashboard-shell">
        <section className="company-toolbar app-surface report-toolbar print:hidden" aria-labelledby="company-report-title">
          <div className="company-toolbar-copy">
            <p className="company-eyebrow">{t('report.subtitle')}</p>
            <h1 id="company-report-title">{t('report.title')}: {selectedCompany?.name ?? '…'}</h1>
            <p>
              {t('report.period')}: {formatPeriodRange(filters.from, filters.to)} · {basisLabel}
              {filters.entryCutoffDate ? ` · ${t('report.cutoff')}: ${formatDate(filters.entryCutoffDate)}` : ''}
            </p>
            {data && <p className="company-toolbar-updated">{t('company.updated')}: {formatDateTime(data.generatedAt)}</p>}
          </div>
          <div className="print-controls flex flex-wrap items-center gap-2">
            <label className="app-text text-sm">
              <span className="mb-1 block text-xs app-text-muted">{t('report.formEfficiency')}</span>
              <input
                type="text"
                inputMode="decimal"
                value={formInput}
                onChange={(event) => onFormChange(event.target.value)}
                placeholder="100"
                className="w-32 rounded-lg border app-border app-surface px-3 py-2 text-sm app-text outline-none focus:border-emerald-500"
                aria-label={t('report.formEfficiency')}
              />
            </label>
            <button type="button" onClick={handlePrint} className="btn-primary rounded-lg px-4 py-2 text-sm font-semibold">
              {t('report.print')}
            </button>
            <button
              type="button"
              onClick={() => void onDownload('pdf')}
              disabled={downloadBusy !== null}
              className="rounded-lg border app-border px-4 py-2 text-sm font-semibold app-text app-hover disabled:cursor-not-allowed disabled:opacity-60"
            >
              {downloadBusy === 'pdf' ? t('report.downloading') : t('report.downloadPdf')}
            </button>
            <button
              type="button"
              onClick={() => void onDownload('xlsx')}
              disabled={downloadBusy !== null}
              className="rounded-lg border app-border px-4 py-2 text-sm font-semibold app-text app-hover disabled:cursor-not-allowed disabled:opacity-60"
            >
              {downloadBusy === 'xlsx' ? t('report.downloading') : t('report.downloadExcel')}
            </button>
            <button
              type="button"
              onClick={() => void onDownload('docx')}
              disabled={downloadBusy !== null}
              className="rounded-lg border app-border px-4 py-2 text-sm font-semibold app-text app-hover disabled:cursor-not-allowed disabled:opacity-60"
            >
              {downloadBusy === 'docx' ? t('report.downloading') : t('report.downloadWord')}
            </button>
            <a
              href={buildCompanyReportUrl('html', filters, issueParams)}
              target="_blank"
              rel="noreferrer"
              className="rounded-lg border app-border px-4 py-2 text-sm font-semibold app-text app-hover"
            >
              {t('report.previewHtml')}
            </a>
            <a href={backToCompanyHref()} className="text-sm font-semibold underline-offset-4 hover:underline" style={{ color: 'var(--primary)' }}>
              {t('report.backToCompany')}
            </a>
          </div>
        </section>
        <p className="px-1 text-xs app-text-muted print:hidden">{t('report.formEfficiencyHint')}</p>
        <p className="px-1 text-xs app-text-muted print:hidden">{t('report.filterSummaryLabel')}: {filterSummary}</p>
        {approved && (
          <p className="company-report-executive print:hidden" dir="rtl">
            {t('report.executiveLine')}: {selectedCompany?.name ?? ''} — {formatKpi(approved.items)} {t('report.itemsQuality')}، {formatKpi(approved.mechanism)} {t('report.mechanismUnlinked')}، {formatKpi(approved.weighted)} {t('report.weightedTimeliness')}، {formatKpi(approved.duplication)} {t('report.duplicationRate')}، {formatKpi(approved.composite)} {t('report.composite')}.
          </p>
        )}
        {downloadError && <p role="alert" className="rounded-xl border border-rose-300 bg-rose-50 px-4 py-2 text-sm text-rose-700 print:hidden">{downloadError}</p>}

        {!isDisplayMode && (
          <section className="app-surface rounded-2xl border app-border shadow-sm print:hidden">
            <button type="button" className="w-full px-4 py-3 text-start text-sm font-semibold app-text" onClick={() => setIssueOpen((open) => !open)} aria-expanded={issueOpen}>
              {t('report.presentation')} {issueOpen ? '−' : '+'}
            </button>
            {issueOpen && (
              <div className="grid grid-cols-2 gap-3 border-t app-border p-3 md:grid-cols-3 xl:grid-cols-4">
                <IssueField label={t('report.issueTitle')} value={issue.title ?? ''} onChange={(value) => onIssueChange({ title: value })} />
                <IssueField label={t('report.issueLetterNumber')} value={issue.letterNumber ?? ''} onChange={(value) => onIssueChange({ letterNumber: value })} />
                <IssueField label={t('report.issueCity')} value={issue.city ?? ''} onChange={(value) => onIssueChange({ city: value })} />
                <IssueField label={t('report.issueHijri')} value={issue.hijriDate ?? ''} onChange={(value) => onIssueChange({ hijriDate: value })} />
                <IssueField label={t('report.issueGregorian')} value={issue.gregorianDate ?? ''} onChange={(value) => onIssueChange({ gregorianDate: value })} />
                <IssueField label={t('report.issueRecipient')} value={issue.recipient ?? ''} onChange={(value) => onIssueChange({ recipient: value })} />
                <IssueField label={t('report.issueIssuer')} value={issue.issuerName ?? ''} onChange={(value) => onIssueChange({ issuerName: value })} />
              </div>
            )}
          </section>
        )}

        {!isDisplayMode && (
          <section className="app-surface rounded-2xl border app-border shadow-sm print:hidden">
            <button type="button" className="w-full px-4 py-3 text-start text-sm font-semibold app-text" onClick={() => setFiltersOpen((open) => !open)} aria-expanded={filtersOpen}>
              {t('v3.filterStrip')} {filtersOpen ? '−' : '+'}
            </button>
            {filtersOpen && <div className="border-t app-border p-3"><FilterBar lookups={lookups} filters={filters} onChange={setFilters} /></div>}
          </section>
        )}

        {/* View Mode Switcher */}
        {selectedCompany && (
          <div className="flex flex-wrap items-center justify-between gap-3 border-b app-border pb-3 print:hidden">
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => setViewMode('document')}
                className={`px-4 py-2 text-xs font-bold rounded-lg transition-all flex items-center gap-2 ${
                  viewMode === 'document'
                    ? 'bg-amber-600 text-white shadow-sm'
                    : 'app-surface border app-border app-text hover:bg-slate-100 dark:hover:bg-slate-800'
                }`}
              >
                <span>📜</span>
                <span>الوثيقة الرسمية للتقرير (نسخة المطابقة مع التطبيق)</span>
              </button>
              <button
                type="button"
                onClick={() => setViewMode('analytics')}
                className={`px-4 py-2 text-xs font-bold rounded-lg transition-all flex items-center gap-2 ${
                  viewMode === 'analytics'
                    ? 'bg-blue-600 text-white shadow-sm'
                    : 'app-surface border app-border app-text hover:bg-slate-100 dark:hover:bg-slate-800'
                }`}
              >
                <span>📊</span>
                <span>الرسوم البيانية والتحليلات التفاعلية</span>
              </button>
            </div>
            <span className="text-xs app-text-muted">
              {viewMode === 'document' ? 'عرض الوثيقة الحكومية الرسمية مع الترويسة والنسق المعتمد' : 'عرض المؤشرات التفاعلية ولوحات التوزيع'}
            </span>
          </div>
        )}

        {!selectedCompany ? (
          <div className="app-surface rounded-2xl border app-border p-10 text-center app-text-muted">{t('report.noCompany')}</div>
        ) : viewMode === 'document' ? (
          <div className="company-detailed-document-wrapper">
            {reportDocData ? (
              <CompanyDetailedDocument
                data={reportDocData}
                loading={loadingDoc}
                onPrint={() => window.print()}
                onDownloadPdf={() => void onDownload('pdf')}
              />
            ) : loadingDoc ? (
              <div className="max-w-6xl mx-auto p-12 text-center app-surface rounded-2xl border app-border">
                <div className="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-[#B9A77A]"></div>
                <p className="mt-3 text-sm font-semibold app-text-muted">جارٍ استخراج وتوليد التقرير التفصيلي...</p>
              </div>
            ) : (
              <div className="app-surface rounded-2xl border app-border p-10 text-center app-text-muted">
                تعذر تحميل بيانات التقرير الرسمي للشركة المحددة.
              </div>
            )}
          </div>
        ) : data && metrics && approved && !isLoading ? (
          <>
            <section className="company-report-hero app-surface" aria-label={t('report.kpiSection')}>
              <div className="company-report-hero-main">
                <p className="company-eyebrow">{t('report.composite')}</p>
                <p className="company-report-hero-value tnum">{formatKpi(approved.composite)}</p>
                <p className="company-report-hero-hint">{t('report.compositeHint')}</p>
              </div>
              <dl className="company-report-hero-grid">
                <div><dt>{t('report.itemsQuality')}</dt><dd>{formatKpi(approved.items)}</dd></div>
                <div><dt>{t('report.formEfficiency')}</dt><dd>{approved.form === null ? t('report.formNotEntered') : formatKpi(approved.form)}</dd></div>
                <div><dt>{t('report.mechanismUnlinked')}</dt><dd>{formatKpi(approved.mechanism)}</dd></div>
                <div><dt>{t('report.weightedTimeliness')}</dt><dd>{formatKpi(approved.weighted)}</dd></div>
                <div><dt>{t('report.duplicationRate')}</dt><dd>{formatKpi(approved.duplication)}</dd></div>
              </dl>
            </section>

            <section aria-label={t('report.summarySection')}>
              <h2 className="company-report-section-title">{t('report.summarySection')}</h2>
              <div className="company-kpi-grid">
                <KpiItem title={t('company.total')} value={formatNumber(metrics.totalNotes)} tone="var(--acc-cyan)" hint={`${t('company.cleanReviewed')}: ${formatNumber(metrics.cleanReviewedNotes)} · المذكرات بأخطاء: ${formatNumber(metrics.errorNotes)}`} />
                <KpiItem title={t('company.reviewCoverage')} value={formatKpi(metrics.reviewCoveragePercentage)} tone="var(--acc-violet)" hint={`${formatNumber(metrics.reviewedItems)} / ${formatNumber(metrics.expectedItems)}`} />
                <KpiItem title={t('company.cleanReviewed')} value={formatKpi(metrics.cleanReviewedNotePercent)} tone="var(--acc-emerald)" hint={`${formatNumber(metrics.cleanReviewedNotes)} / ${formatNumber(metrics.qualityEligibleNoteCount)}`} />
                <KpiItem title={t('company.errorsPer100')} value={formatKpi(metrics.errorsPer100Checked)} tone="var(--acc-rose)" hint={formatNumber(metrics.errorItems)} />
                <KpiItem title={t('company.timeCompliance')} value={formatKpi(metrics.timeCompliancePercentage)} tone="var(--acc-amber)" hint={`${formatNumber(metrics.onTimeNotes)} / ${formatNumber(metrics.timeEligibleNotes)}`} />
                <KpiItem title={t('company.affectedRate')} value={formatKpi(metrics.affectedNoteRatePercentage)} tone="var(--acc-amber)" hint={`${formatNumber(metrics.affectedNotes)} / ${formatNumber(metrics.totalNotes)}`} />
                <KpiItem title={t('company.incidentsPer100')} value={formatKpi(metrics.incidentsPer100Notes)} tone="var(--acc-rose)" hint={`${formatNumber(metrics.duplicateIncidents)} / ${formatNumber(metrics.totalNotes)}`} />
                <KpiItem title={t('company.linkEfficiency')} value={formatKpi(metrics.linkEfficiencyPercentage)} tone="var(--acc-cyan)" hint={`${formatNumber(metrics.linkedEntryNotes)} / ${formatNumber(metrics.entryNotes)}`} />
              </div>
              <p className="company-report-executive" dir="rtl">
                {t('report.executiveLine')}: {selectedCompany?.name ?? ''} — {formatKpi(approved.items)} {t('report.itemsQuality')}، {formatKpi(approved.mechanism)} {t('report.mechanismUnlinked')}، {formatKpi(approved.weighted)} {t('report.weightedTimeliness')}، {formatKpi(approved.duplication)} {t('report.duplicationRate')}، {formatKpi(approved.composite)} {t('report.composite')}.
              </p>
            </section>

            <section aria-label={t('report.chartsSection')}>
              <h2 className="company-report-section-title">{t('report.chartsSection')}</h2>
              <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
                <article className="company-table-panel app-surface">
                  <h2>{t('report.monthlyTrend')}</h2>
                  <div className="chart-box" dir="rtl"><Bar data={monthlyData} options={chartOptions} /></div>
                </article>
                <article className="company-table-panel app-surface">
                  <h2>توزيع جودة وسلامة المذكرات</h2>
                  <div className="chart-box" dir="rtl"><Doughnut data={qualityDistributionData} options={pieOptions} /></div>
                </article>
                <article className="company-table-panel app-surface">
                  <h2>{t('report.delayDistribution')}</h2>
                  <div className="chart-box" dir="rtl"><Bar data={delayData} options={chartOptions} /></div>
                </article>
                <article className="company-table-panel app-surface">
                  <h2>{t('report.duplicateTrendChart')}</h2>
                  <div className="chart-box" dir="rtl"><Line data={duplicateTrendData} options={chartOptions} /></div>
                </article>
                <article className="company-table-panel app-surface xl:col-span-2">
                  <h2>{t('report.topErrorFields')}</h2>
                  <div className="chart-box" dir="rtl">
                    <Bar
                      data={topFieldsData}
                      options={{
                        ...chartOptions,
                        indexAxis: 'y' as const,
                        scales: {
                          x: { ticks: { color: colors.textMuted, font: chartFont(colors) }, grid: { color: colors.grid }, beginAtZero: true },
                          y: { ticks: { color: colors.textMuted, font: chartFont(colors) }, grid: { color: colors.grid } },
                        },
                      }}
                    />
                  </div>
                </article>
              </div>
            </section>

            <section aria-label={t('report.tablesSection')}>
              <h2 className="company-report-section-title">{t('report.tablesSection')}</h2>
              <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
                <article className="company-table-panel app-surface">
                  <h2>{t('report.factoryTable')}</h2>
                  <div className="company-table-scroll">
                    <table className="company-table" dir="rtl">
                      <thead><tr><th>{t('company.factory')}</th><th>{t('v3.total')}</th><th>{t('company.qualityRatio')}</th><th>{t('v3.accuracy')}</th><th>{t('charts.sla.late')}</th></tr></thead>
                      <tbody>{data.performanceByFactory.map((row) => <tr key={row.label}><th scope="row">{row.label}</th><td>{formatNumber(row.totalNotes)}</td><td>{formatNumber(row.correctCount)} / {formatNumber(row.qualityEligibleCount)}</td><td>{formatPercent(row.accuracyRate)}</td><td>{formatNumber(row.lateCount)}</td></tr>)}</tbody>
                    </table>
                    {data.performanceByFactory.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
                  </div>
                </article>
                <article className="company-table-panel app-surface">
                  <h2>{t('report.recentTable')}</h2>
                  <div className="company-table-scroll">
                    <table className="company-table" dir="rtl">
                      <thead><tr><th>{t('recent.noteNumber')}</th><th>{t('recent.factory')}</th><th>{t('recent.arrivalDate')}</th><th>{t('recent.status')}</th></tr></thead>
                      <tbody>{data.recentNotes.map((note) => <tr key={note.id}><th scope="row">{note.noteNumber}</th><td>{note.factoryName ?? '—'}</td><td>{note.arrivalAt ? formatDate(note.arrivalAt) : '—'}</td><td>{note.statusLabel}</td></tr>)}</tbody>
                    </table>
                    {data.recentNotes.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
                  </div>
                </article>
              </div>
              <article className="company-table-panel app-surface">
                <h2>{t('charts.duplicateBreakdown')}</h2>
                <ul className="space-y-2 text-sm app-text">
                  {data.duplicateBreakdown.map((row) => <li key={row.classification} className="flex items-center justify-between gap-3"><span>{row.label}</span><strong>{formatNumber(row.count)}</strong></li>)}
                </ul>
                {data.duplicateBreakdown.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
              </article>
            </section>
          </>
        ) : isLoading ? <SkeletonLoader /> : <DashboardSnapshotError error={snapshotError} onRetry={refreshNow} isLoading={isLoading} />}
        {data && selectedCompany && (
          <p className="rounded-2xl border app-border app-surface px-4 py-3 text-start text-sm app-text">
            {t('report.issueLetterNumber')}: {issue.letterNumber?.trim() || '—'} · {t('report.issueIssuer')}: {issue.issuerName?.trim() || '—'} · {filterSummary}
          </p>
        )}
      </main>
      <footer className="app-surface border-t app-border py-4 text-center text-sm font-bold app-text-muted">{t('app.footer', { yearRange: `${new Date().getFullYear()}-${new Date().getFullYear() + 1}` })}</footer>
    </div>
  )
}

function IssueField({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs app-text-muted">{label}</span>
      <input
        type="text"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="w-full rounded-lg border app-border app-surface px-3 py-2 text-sm app-text outline-none focus:border-emerald-500"
        aria-label={label}
      />
    </label>
  )
}

function KpiItem({ title, value, hint, tone }: { title: string; value: string; hint: string; tone: string }) {
  return (
    <article className="modern-kpi-card" style={{ '--modern-kpi-tone': tone } as React.CSSProperties}>
      <p className="modern-kpi-title">{title}</p>
      <p className="modern-kpi-value tnum">{value}</p>
      <p className="modern-kpi-detail">{hint}</p>
    </article>
  )
}

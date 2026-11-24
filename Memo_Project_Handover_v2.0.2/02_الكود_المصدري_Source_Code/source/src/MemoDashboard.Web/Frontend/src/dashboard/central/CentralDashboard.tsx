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
  type ChartData,
} from 'chart.js'
import { Bar, Doughnut } from 'react-chartjs-2'
import { api, type SetupConfig } from '../../services/api'
import {
  EMPTY_FILTERS,
  FilterBar,
  loadFiltersFromUrlOrSaved,
  persistFilters,
  sanitizeFiltersAgainstLookups,
} from '../../components/FilterBar'
import { Header } from '../../components/Header'
import { OfflineBanner } from '../../components/OfflineBanner'
import { DashboardSnapshotError } from '../../components/DashboardSnapshotError'
import { SkeletonLoader } from '../../components/SkeletonLoader'
import { useDashboardData } from '../../hooks/useDashboardData'
import { useChartTheme } from '../../hooks/useChartTheme'
import { t, translateStatus } from '../../i18n'
import { useLanguage } from '../../i18n/LanguageProvider'
import { formatDate, formatDateTime, formatNumber, formatPercent } from '../../services/format'
import type {
  CompanyMetrics,
  CompanyMonthlyRow,
  DashboardFilters,
  DashboardLookups,
  DashboardSnapshot,
  ReviewQueueResult,
} from '../../types/dashboard'
import './CentralDashboard.css'

ChartJS.register(ArcElement, BarElement, CategoryScale, Legend, LinearScale, LineElement, PointElement, Tooltip)

export type CentralTabKey = 'general' | 'program' | 'companies' | 'review'

const CENTRAL_TABS: CentralTabKey[] = ['general', 'program', 'companies', 'review']
const REVIEW_PAGE_SIZE = 20

function readCentralTab(): CentralTabKey {
  if (typeof window === 'undefined') return 'general'
  const raw = new URLSearchParams(window.location.search).get('centralTab')
  return CENTRAL_TABS.includes(raw as CentralTabKey) ? (raw as CentralTabKey) : 'general'
}

function writeCentralTab(tab: CentralTabKey): void {
  if (typeof window === 'undefined') return
  const params = new URLSearchParams(window.location.search)
  params.set('centralTab', tab)
  const query = params.toString()
  window.history.replaceState({}, '', `${window.location.pathname}${query ? `?${query}` : ''}`)
}

export function backToMainHref(): string {
  const params = new URLSearchParams(window.location.search)
  params.delete('view')
  params.delete('centralTab')
  params.delete('report')
  const query = params.toString()
  return `${window.location.pathname}${query ? `?${query}` : ''}`
}

export function centralHref(): string {
  const params = new URLSearchParams(window.location.search)
  params.set('view', 'central')
  params.delete('report')
  return `${window.location.pathname}?${params.toString()}`
}

function fullReportHref(companyId: string, filters: DashboardFilters): string {
  const params = new URLSearchParams(window.location.search)
  // 1.2.1: رابط نظيف للتقرير المخصص — بلا view/centralTab حتى لا يلتبس التوجيه،
  // وهو نفس تقرير البرنامج (CompanyDetailed) بنفس الفلاتر.
  params.delete('view')
  params.delete('centralTab')
  params.set('report', 'company')
  params.set('companyId', companyId)
  if (filters.from) params.set('from', filters.from)
  if (filters.to) params.set('to', filters.to)
  params.set('dateBasis', filters.dateBasis)
  if (filters.entryCutoffDate) params.set('entryCutoffDate', filters.entryCutoffDate)
  return `${window.location.pathname}?${params.toString()}`
}

function formatKpi(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return t('common.notAvailable')
  return formatPercent(value)
}

function formatDays(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return t('common.notAvailable')
  return formatNumber(value)
}

function MiniKpi({ title, value, hint, tone }: { title: string; value: string; hint: string; tone: string }) {
  return (
    <article className="central-kpi-card modern-kpi-card" style={{ '--modern-kpi-tone': tone } as React.CSSProperties}>
      <p className="central-kpi-title modern-kpi-title">{title}</p>
      <p className="central-kpi-value modern-kpi-value tnum">{value}</p>
      <p className="central-kpi-hint modern-kpi-detail">{hint}</p>
    </article>
  )
}

function chartFont(colors: { text: string }) {
  return { family: "'Simplified Arabic','Segoe UI',Tahoma,Arial,sans-serif", size: 12, weight: 700 as const, color: colors.text }
}

interface CentralDashboardProps {
  config?: SetupConfig | null
  onEditConnection?: () => void
}

export function CentralDashboard({ config, onEditConnection }: CentralDashboardProps) {
  const { language } = useLanguage()
  const [activeTab, setActiveTab] = useState<CentralTabKey>(() => readCentralTab())
  const [filters, setFilters] = useState<DashboardFilters>(() => loadFiltersFromUrlOrSaved())
  const [lookups, setLookups] = useState<DashboardLookups | null>(null)

  useEffect(() => {
    const onPopState = () => setActiveTab(readCentralTab())
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  useEffect(() => {
    let cancelled = false
    api.getLookups().then((result) => {
      if (cancelled) return
      setFilters((current) => {
        const next = sanitizeFiltersAgainstLookups(current, result)
        if (JSON.stringify(next) !== JSON.stringify(current)) persistFilters(next)
        return next
      })
      setLookups(result)
    }).catch(() => {
      if (cancelled) return
      setLookups({ companies: [], factories: [], productionLines: [], noteTypes: [] })
    })
    return () => { cancelled = true }
  }, [])

  const changeTab = (tab: CentralTabKey) => {
    setActiveTab(tab)
    writeCentralTab(tab)
  }

  // المدير العام: قراءة فقط بلا فلاتر معقدة — لقطة غير مفلترة مخصصة لهذا التاب.
  const general = useDashboardData(EMPTY_FILTERS, activeTab === 'general')
  // بقية التابات: نفس عقد الفلاتر الموحد FilterQuery.
  const shared = useDashboardData(filters, lookups !== null && activeTab !== 'general')

  const data = activeTab === 'general' ? general.data : shared.data
  const isConnected = activeTab === 'general' ? general.isConnected : shared.isConnected
  const isLoading = activeTab === 'general' ? general.isLoading : shared.isLoading
  const lastUpdated = activeTab === 'general' ? general.lastUpdated : shared.lastUpdated
  const snapshotError = activeTab === 'general' ? general.snapshotError : shared.snapshotError
  const refreshNow = activeTab === 'general' ? general.refreshNow : shared.refreshNow

  const isDisplayMode = typeof window !== 'undefined' && new URLSearchParams(window.location.search).get('display') === '1'
  const dir = language === 'ar' ? 'rtl' : 'ltr'

  return (
    <div className="app-bg min-h-screen app-text central-board" dir={dir}>
      <Header
        isConnected={isConnected}
        isLoading={isLoading}
        lastUpdated={lastUpdated}
        onRefresh={refreshNow}
        onEditConnection={onEditConnection}
        onPrint={() => window.print()}
        isDisplayMode={isDisplayMode}
        organizationName={config?.organizationName}
        subOrganizationName={config?.subOrganizationName}
      />
      {!isConnected && <OfflineBanner message={t('common.connectionFailed')} />}
      <main className="mx-auto max-w-[1800px] space-y-4 px-4 py-4">
        <section className="central-hero" aria-labelledby="central-title">
          <div className="central-hero-copy">
            <p className="central-eyebrow">{t('central.subtitle')}</p>
            <h1 id="central-title">{t('central.title')}</h1>
            {data && <p className="central-hero-sub">{t('company.updated')}: {formatDateTime(data.generatedAt)}</p>}
          </div>
          <div className="print-controls central-no-print flex flex-wrap items-center gap-2">
            <a href={backToMainHref()} className="central-back-link">
              {t('central.backToMain')}
            </a>
          </div>
        </section>

        {!isDisplayMode && (
          <div className="central-tabs-bar central-no-print">
            <nav className="central-tabs" role="tablist" aria-label={t('central.title')}>
              {CENTRAL_TABS.map((tab) => (
                <button
                  key={tab}
                  type="button"
                  role="tab"
                  aria-selected={activeTab === tab}
                  onClick={() => changeTab(tab)}
                  className="central-tab"
                >
                  {t(`central.tabs.${tab}`)}
                </button>
              ))}
            </nav>
          </div>
        )}

        {activeTab === 'program' && !isDisplayMode && (
          <PeriodStrip filters={filters} onChange={setFilters} />
        )}
        {(activeTab === 'companies' || activeTab === 'review') && !isDisplayMode && (
          <FilterBar lookups={lookups} filters={filters} onChange={setFilters} />
        )}

        {data ? (
          <div key={activeTab}>
            {activeTab === 'general' && <GeneralTab data={data} />}
            {activeTab === 'program' && <ProgramTab data={data} />}
            {activeTab === 'companies' && (
              <CompaniesTab data={data} lookups={lookups} filters={filters} onChange={setFilters} />
            )}
            {activeTab === 'review' && <ReviewTab filters={filters} lookupsReady={lookups !== null} />}
          </div>
        ) : isLoading ? (
          <SkeletonLoader />
        ) : (
          <DashboardSnapshotError error={snapshotError} onRetry={refreshNow} isLoading={isLoading} />
        )}
      </main>
      <footer className="app-surface border-t app-border py-4 text-center text-sm font-bold app-text-muted">
        {t('app.footer', { yearRange: `${new Date().getFullYear()}-${new Date().getFullYear() + 1}` })}
      </footer>
    </div>
  )
}

function PeriodStrip({ filters, onChange }: { filters: DashboardFilters; onChange: (next: DashboardFilters) => void }) {
  const update = (patch: Partial<DashboardFilters>) => {
    const next = { ...filters, ...patch }
    persistFilters(next)
    onChange(next)
  }
  return (
    <section className="central-section central-no-print" aria-label={t('central.periodOnly')}>
      <div className="central-section-head">
        <div>
          <h2>{t('central.periodOnly')}</h2>
        </div>
      </div>
      <div className="central-period-strip">
        <label className="central-filter-label">
          {t('filters.from')}
          <input type="date" value={filters.from} onChange={(e) => update({ from: e.target.value })} className="central-input" />
        </label>
        <label className="central-filter-label">
          {t('filters.to')}
          <input type="date" value={filters.to} onChange={(e) => update({ to: e.target.value })} className="central-input" />
        </label>
        <label className="central-filter-label">
          {t('filters.dateBasis')}
          <select
            value={filters.dateBasis}
            onChange={(e) => update({ dateBasis: e.target.value as DashboardFilters['dateBasis'] })}
            className="central-input"
          >
            <option value="WritingDate">{t('filters.writingDate')}</option>
            <option value="ArrivalDate">{t('filters.arrivalDate')}</option>
          </select>
        </label>
      </div>
    </section>
  )
}

function GeneralTab({ data }: { data: DashboardSnapshot }) {
  const colors = useChartTheme()
  const quality = useMemo(() => {
    const rows = (data.qualityByCompany ?? []).filter((r) => r.accuracyRate !== null && r.accuracyRate !== undefined)
    const desc = [...rows].sort((a, b) => (b.accuracyRate ?? 0) - (a.accuracyRate ?? 0))
    return { top: desc.slice(0, 5), bottom: desc.slice(-5).reverse() }
  }, [data])
  const compliance = useMemo(() => {
    const rows = (data.timelinessByCompany ?? []).filter((r) => r.lateRate !== null && r.lateRate !== undefined)
    const asc = [...rows].sort((a, b) => (a.lateRate ?? 0) - (b.lateRate ?? 0))
    return { top: asc.slice(0, 5), bottom: asc.slice(-5).reverse() }
  }, [data])

  const monthlyData = useMemo(() => ({
    labels: (data.monthlyActivity ?? []).map((row) => row.monthLabel || row.month),
    datasets: [{
      label: t('central.monthlyTotal'),
      data: (data.monthlyActivity ?? []).map((row) => row.count),
      backgroundColor: colors.primary,
      borderColor: colors.primary,
      borderWidth: 1,
    }],
  }), [data, colors])

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

  const totalNotes = (data.monthlyActivity ?? []).reduce((sum, row) => sum + (row.count ?? 0), 0)

  return (
    <div className="space-y-4">
      <div className="central-kpi-grid">
        <MiniKpi title={t('kpi.totalNotes')} value={formatNumber(data.summary.totalNotes)} hint={t('central.monthlyTotal')} tone="var(--acc-cyan)" />
        <MiniKpi title={t('kpi.activeCompanies')} value={formatNumber(data.companies.length)} hint={t('central.companiesTable')} tone="var(--acc-violet)" />
        <MiniKpi title={t('central.timeCompliance')} value={formatKpi(data.sla.onTimeRate)} hint={`${formatNumber(data.sla.onTimeNotes)} / ${formatNumber(data.sla.eligibleNotes)}`} tone="var(--acc-emerald)" />
        <MiniKpi title={t('central.avgLate')} value={formatDays(data.summary.avgLateDays)} hint={t('kpi.avgLateDays')} tone="var(--acc-amber)" />
      </div>

      <section className="central-section" aria-label={t('central.companiesTable')}>
        <div className="central-section-head">
          <div>
            <p className="central-eyebrow">{t('central.generalEyebrow')}</p>
            <h2>{t('central.companiesTable')}</h2>
          </div>
          <span className="central-count tnum">{formatNumber(data.companies.length)}</span>
        </div>
        <div className="central-table-scroll">
          <table className="central-table company-table" dir="rtl">
            <thead><tr><th>#</th><th>{t('charts.companies')}</th><th>{t('charts.notesCount')}</th></tr></thead>
            <tbody>
              {data.companies.map((company, index) => (
                <tr key={company.id}>
                  <td className="tnum">{formatNumber(index + 1)}</td>
                  <th scope="row">{company.name}</th>
                  <td className="tnum">{formatNumber(company.noteCount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {data.companies.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
        </div>
      </section>

      <div className="central-rank-grid">
        <RankPanel title={t('central.topQuality')} rows={quality.top.map((r) => ({ label: r.companyName, value: formatKpi(r.accuracyRate) }))} />
        <RankPanel title={t('central.bottomQuality')} rows={quality.bottom.map((r) => ({ label: r.companyName, value: formatKpi(r.accuracyRate) }))} />
        <RankPanel
          title={t('central.topCompliance')}
          rows={compliance.top.map((r) => ({ label: r.label, value: formatKpi(r.lateRate === null || r.lateRate === undefined ? null : 100 - r.lateRate) }))}
        />
        <RankPanel
          title={t('central.bottomCompliance')}
          rows={compliance.bottom.map((r) => ({ label: r.label, value: formatKpi(r.lateRate === null || r.lateRate === undefined ? null : 100 - r.lateRate) }))}
        />
      </div>

      <section className="central-section" aria-label={t('central.monthlyTotal')}>
        <div className="central-section-head">
          <div>
            <h2>{t('central.monthlyTotal')}</h2>
            <p className="tnum">{formatNumber(totalNotes)}</p>
          </div>
        </div>
        <div className="central-chart" dir="rtl"><Bar data={monthlyData} options={chartOptions} /></div>
      </section>
    </div>
  )
}

function RankPanel({ title, rows }: { title: string; rows: Array<{ label: string; value: string }> }) {
  return (
    <article className="central-section" aria-label={title}>
      <div className="central-section-head">
        <h2>{title}</h2>
        <span className="central-count tnum">{formatNumber(rows.length)}</span>
      </div>
      {rows.length === 0 ? (
        <p className="company-table-empty">{t('company.empty')}</p>
      ) : (
        <ol className="central-rank-list">
          {rows.map((row, index) => (
            <li key={row.label}>
              <span className="central-rank-index tnum">{formatNumber(index + 1)}</span>
              <span className="central-rank-label" title={row.label}>{row.label}</span>
              <span className="central-rank-value tnum">{row.value}</span>
            </li>
          ))}
        </ol>
      )}
    </article>
  )
}

function ProgramTab({ data }: { data: DashboardSnapshot }) {
  const fields = (data.topErrorFields ?? []).slice(0, 10)
  return (
    <div className="space-y-4">
      <div className="central-kpi-grid central-kpi-grid-6">
        <MiniKpi title={t('central.approved')} value={formatNumber(data.summary.approved)} hint={t('kpi.approved')} tone="var(--acc-emerald)" />
        <MiniKpi title={t('central.pending')} value={formatNumber(data.summary.pendingReview)} hint={t('kpi.pendingReview')} tone="var(--acc-amber)" />
        <MiniKpi title={t('central.returned')} value={formatNumber(data.summary.returnedForCorrection)} hint={t('kpi.returnedForCorrection')} tone="var(--acc-rose)" />
        <MiniKpi title={t('central.timeCompliance')} value={formatKpi(data.sla.onTimeRate)} hint={`${formatNumber(data.sla.onTimeNotes)} / ${formatNumber(data.sla.eligibleNotes)}`} tone="var(--acc-cyan)" />
        <MiniKpi title={t('central.avgLate')} value={formatDays(data.summary.avgLateDays)} hint={t('kpi.avgLateDays')} tone="var(--acc-orange)" />
        <MiniKpi title={t('central.maxLate')} value={formatDays(data.summary.maxLateDays)} hint={t('kpi.maxLateDays')} tone="var(--acc-rose)" />
        <MiniKpi title={t('central.duplicates')} value={formatNumber(data.summary.duplicateIncidentCount)} hint={t('kpi.duplicateNumbers')} tone="var(--acc-violet)" />
        <MiniKpi title={t('central.affectedNotes')} value={formatNumber(data.summary.duplicateAffectedNotes)} hint={t('kpi.lateTotal')} tone="var(--acc-amber)" />
      </div>
      <section className="central-section" aria-label={t('central.topErrorFields')}>
        <div className="central-section-head">
          <div>
            <p className="central-eyebrow">{t('central.programEyebrow')}</p>
            <h2>{t('central.topErrorFields')}</h2>
          </div>
          <span className="central-count tnum">{formatNumber(fields.length)}</span>
        </div>
        <div className="central-table-scroll">
          <table className="central-table company-table" dir="rtl">
            <thead><tr><th>{t('charts.topErrorFields')}</th><th>{t('charts.topErrorFields.errorCount')}</th><th>{t('charts.qualityByCompany.error')}</th></tr></thead>
            <tbody>
              {fields.map((row) => (
                <tr key={row.fieldId}>
                  <th scope="row">{row.label}</th>
                  <td className="tnum">{formatNumber(row.errorCount)}</td>
                  <td className="tnum">{formatKpi(row.errorRate)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {fields.length === 0 && <p className="company-table-empty">{t('charts.topErrorFields.noErrors')}</p>}
        </div>
      </section>
    </div>
  )
}

function CompaniesTab({
  data,
  lookups,
  filters,
  onChange,
}: {
  data: DashboardSnapshot
  lookups: DashboardLookups | null
  filters: DashboardFilters
  onChange: (next: DashboardFilters) => void
}) {
  const colors = useChartTheme()
  const selectedCompany = useMemo(
    () => lookups?.companies.find((company) => company.id === filters.companyId) ?? null,
    [filters.companyId, lookups],
  )
  const metrics: CompanyMetrics | null = data.companyMetrics ?? null
  const [monthly, setMonthly] = useState<CompanyMonthlyRow[] | null>(null)

  useEffect(() => {
    if (!filters.companyId) {
      setMonthly(null)
      return
    }
    let cancelled = false
    const controller = new AbortController()
    api.getCompanyMonthly(filters.companyId, filters, controller.signal)
      .then((rows) => { if (!cancelled) setMonthly(rows) })
      .catch(() => { if (!cancelled) setMonthly(null) })
    return () => { cancelled = true; controller.abort() }
  }, [filters.companyId, filters.from, filters.to, filters.dateBasis, filters.entryCutoffDate])

  const updateCompany = (companyId: string) => {
    const next = { ...filters, companyId, factoryId: '', productionLineId: '' }
    persistFilters(next)
    onChange(next)
  }

  const monthlyMixed = useMemo(() => {
    const rows = monthly ?? []
    const volume = monthly
      ? rows.map((row) => row.totalNotes)
      : (data.monthlyActivity ?? []).map((row) => row.count)
    const labels = monthly
      ? rows.map((row) => row.periodLabel || row.monthStart)
      : (data.monthlyActivity ?? []).map((row) => row.monthLabel || row.month)
    const quality = monthly ? rows.map((row) => row.itemQualityPercentage ?? row.cleanReviewedNotePercent) : []
    return {
      labels,
      datasets: [
        {
          type: 'bar' as const,
          label: t('central.monthlyVolume'),
          data: volume,
          backgroundColor: colors.primary,
          borderColor: colors.primary,
          borderWidth: 1,
          yAxisID: 'y',
        },
        ...(monthly
          ? [{
              type: 'line' as const,
              label: t('central.monthlyQuality'),
              data: quality,
              borderColor: colors.success,
              backgroundColor: colors.success,
              tension: 0.3,
              yAxisID: 'y1',
            }]
          : []),
      ],
    }
  }, [monthly, data, colors])

  const statusBuckets = useMemo(() => (metrics ? [
    { label: t('central.healthyNotes'), value: metrics.cleanReviewedNotes, color: colors.success },
    { label: t('central.errorNotes'), value: metrics.errorNotes, color: colors.danger },
    { label: t('central.incompleteNotes'), value: metrics.incompleteReviewNotes, color: colors.warning },
    { label: t('central.notApplicableNotes'), value: metrics.allNotApplicableNoteCount, color: colors.textMuted },
  ] : []), [metrics, colors])

  const statusData = useMemo(() => ({
    labels: statusBuckets.map((row) => row.label),
    datasets: [{
      data: statusBuckets.map((row) => row.value),
      backgroundColor: statusBuckets.map((row) => row.color),
      borderWidth: 1,
    }],
  }), [statusBuckets])

  const delayBuckets = useMemo(() => (metrics ? [
    { label: t('charts.sla.onTime'), value: metrics.onTimeNotes, color: colors.success },
    { label: '6-10', value: metrics.late6To10Notes, color: colors.primary },
    { label: '11-20', value: metrics.late11To20Notes, color: colors.warning },
    { label: '>20', value: metrics.lateOver20Notes, color: colors.danger },
  ] : []), [metrics, colors])

  const delayData = useMemo(() => ({
    labels: delayBuckets.map((row) => row.label),
    datasets: [{
      label: t('central.delayCompliance'),
      data: delayBuckets.map((row) => row.value),
      backgroundColor: delayBuckets.map((row) => row.color),
      borderWidth: 1,
    }],
  }), [delayBuckets])

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
    plugins: {
      legend: { position: 'bottom' as const, rtl: true, labels: { color: colors.text, font: chartFont(colors) } },
      tooltip: { enabled: true, rtl: true },
    },
  }), [colors])

  const mixedOptions = useMemo(() => ({
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { rtl: true, labels: { color: colors.text, font: chartFont(colors) } },
      tooltip: { enabled: true, rtl: true },
    },
    scales: {
      x: { reverse: true, ticks: { color: colors.textMuted, font: chartFont(colors) }, grid: { color: colors.grid } },
      y: { position: 'right' as const, ticks: { color: colors.textMuted, font: chartFont(colors) }, grid: { color: colors.grid }, beginAtZero: true },
      y1: { position: 'left' as const, min: 0, max: 100, ticks: { color: colors.textMuted, font: chartFont(colors) }, grid: { display: false } },
    },
  }), [colors])

  const comparison = data.previousCompanyMetrics && data.previousPeriod
    ? [
        {
          label: t('company.itemQuality'),
          current: data.companyMetrics?.itemQualityPercentage ?? null,
          previous: data.previousCompanyMetrics.itemQualityPercentage,
        },
        {
          label: t('company.timeCompliance'),
          current: data.companyMetrics?.timeCompliancePercentage ?? null,
          previous: data.previousCompanyMetrics.timeCompliancePercentage,
        },
      ]
    : []

  return (
    <div className="space-y-4">
      <section className="central-section" aria-label={t('company.selector')}>
        <div className="central-section-head">
          <div>
            <p className="central-eyebrow">{t('central.companyEyebrow')}</p>
            <h2>{t('company.selector')}</h2>
          </div>
        </div>
        <label className="central-filter-label" style={{ maxWidth: '24rem' }}>
          {t('company.selector')}
          <select
            value={filters.companyId}
            onChange={(e) => updateCompany(e.target.value)}
            className="central-input"
          >
            <option value="">{t('central.chooseCompany')}</option>
            {(lookups?.companies ?? []).map((company) => (
              <option key={company.id} value={company.id}>{company.name}</option>
            ))}
          </select>
        </label>
      </section>

      {!selectedCompany || !metrics ? (
        <div className="central-section"><p className="company-table-empty">{t('report.noCompany')}</p></div>
      ) : (
        <>
          <div className="central-kpi-grid central-kpi-grid-6">
            <MiniKpi title={t('company.total')} value={formatNumber(metrics.totalNotes)} hint={selectedCompany.name} tone="var(--acc-cyan)" />
            <MiniKpi title={t('company.itemQuality')} value={formatKpi(metrics.itemQualityPercentage)} hint={t('company.itemQualityHint')} tone="var(--acc-emerald)" />
            <MiniKpi title={t('company.reviewCoverage')} value={formatKpi(metrics.reviewCoveragePercentage)} hint={t('company.reviewCoverageHint')} tone="var(--acc-violet)" />
            <MiniKpi title={t('company.timeCompliance')} value={formatKpi(metrics.timeCompliancePercentage)} hint={t('company.timeComplianceHint')} tone="var(--acc-amber)" />
            <MiniKpi title={t('company.affectedRate')} value={formatKpi(metrics.affectedNoteRatePercentage)} hint={`${formatNumber(metrics.affectedNotes)} / ${formatNumber(metrics.totalNotes)}`} tone="var(--acc-rose)" />
            <MiniKpi title={t('company.linkEfficiency')} value={formatKpi(metrics.linkEfficiencyPercentage)} hint={`${formatNumber(metrics.linkedEntryNotes)} / ${formatNumber(metrics.entryNotes)}`} tone="var(--acc-cyan)" />
          </div>

          {comparison.length > 0 && (
            <section className="central-section" aria-label={t('company.previousPeriodComparison')}>
              <div className="central-section-head">
                <h2>{t('company.previousPeriodComparison')}</h2>
              </div>
              <div className="central-table-scroll">
                <table className="central-table company-table" dir="rtl">
                  <thead><tr><th>{t('company.metric')}</th><th>{t('company.currentPeriod')}</th><th>{t('company.previousPeriod')}</th><th>{t('company.change')}</th></tr></thead>
                  <tbody>
                    {comparison.map((row) => {
                      const delta = row.current === null || row.current === undefined || row.previous === null || row.previous === undefined
                        ? null
                        : Math.round((row.current - row.previous) * 100) / 100
                      return (
                        <tr key={row.label}>
                          <th scope="row">{row.label}</th>
                          <td className="tnum">{formatKpi(row.current)}</td>
                          <td className="tnum">{formatKpi(row.previous)}</td>
                          <td className="tnum">{delta === null ? t('common.notAvailable') : `${formatNumber(delta)}${t('company.percentagePoints')}`}</td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
              <p className="central-hero-sub">{t('company.changeNote')}</p>
            </section>
          )}

          <div className="central-charts-grid">
            <article className="central-section central-span-2">
              <div className="central-section-head"><h2>{t('central.monthlyTrend')}</h2></div>
              {/* Mixed bar+line datasets: Chart.js resolves the type per dataset at runtime. */}
              <div className="central-chart" dir="rtl"><Bar data={monthlyMixed as unknown as ChartData<'bar'>} options={mixedOptions} /></div>
              {monthly === null && <p className="company-table-empty">{t('central.monthlyEmpty')}</p>}
            </article>
            <article className="central-section">
              <div className="central-section-head"><h2>{t('central.statusDistribution')}</h2></div>
              <div className="central-chart" dir="rtl"><Doughnut data={statusData} options={pieOptions} /></div>
              <ul className="central-legend-list">
                {statusBuckets.map((row) => (
                  <li key={row.label}>
                    <span className="flex items-center gap-2"><span className="central-dot" style={{ background: row.color }} />{row.label}</span>
                    <strong className="tnum">{formatNumber(row.value)}</strong>
                  </li>
                ))}
              </ul>
            </article>
            <article className="central-section">
              <div className="central-section-head"><h2>{t('central.delayCompliance')}</h2></div>
              <div className="central-chart" dir="rtl"><Bar data={delayData} options={chartOptions} /></div>
              <ul className="central-legend-list">
                {delayBuckets.map((row) => (
                  <li key={row.label}>
                    <span className="flex items-center gap-2"><span className="central-dot" style={{ background: row.color }} />{row.label}</span>
                    <strong className="tnum">{formatNumber(row.value)}</strong>
                  </li>
                ))}
              </ul>
            </article>
          </div>

          <a
            href={fullReportHref(selectedCompany.id, filters)}
            className="central-btn central-btn-primary central-no-print"
          >
            {t('central.openFullReport')}
          </a>
        </>
      )}
    </div>
  )
}

function ReviewTab({ filters, lookupsReady }: { filters: DashboardFilters; lookupsReady: boolean }) {
  const [page, setPage] = useState(0)
  const [result, setResult] = useState<ReviewQueueResult | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [failed, setFailed] = useState(false)

  useEffect(() => { setPage(0) }, [filters])

  useEffect(() => {
    if (!lookupsReady) return
    let cancelled = false
    setIsLoading(true)
    setFailed(false)
    const controller = new AbortController()
    api.getReviewQueue(filters, REVIEW_PAGE_SIZE, page * REVIEW_PAGE_SIZE, controller.signal)
      .then((payload) => {
        if (cancelled || controller.signal.aborted) return
        setResult(payload)
        setIsLoading(false)
      })
      .catch((error) => {
        if (cancelled || controller.signal.aborted) return
        if (error instanceof DOMException && error.name === 'AbortError') return
        setFailed(true)
        setIsLoading(false)
      })
    return () => { cancelled = true; controller.abort() }
  }, [filters, lookupsReady, page])

  const totalPages = result && result.total > 0 ? Math.max(1, Math.ceil(result.total / REVIEW_PAGE_SIZE)) : 1

  return (
    <div className="space-y-4">
      {isLoading && !result ? (
        <SkeletonLoader />
      ) : failed || !result ? (
        <div className="central-section"><p className="company-table-empty">{t('snapshot.failedBody')}</p></div>
      ) : (
        <>
          <section className="central-section" aria-label={t('central.reviewEyebrow')}>
            <div className="central-section-head">
              <div>
                <p className="central-eyebrow">{t('central.reviewEyebrow')}</p>
                <h2>{t('central.reviewEyebrow')} <span className="tnum">({formatNumber(result.total)})</span> {t('central.reviewTotal')}</h2>
              </div>
              <span className="central-count tnum">{t('central.pageOf', { page: page + 1, pages: totalPages })}</span>
            </div>
            <div className="central-table-scroll">
              <table className="central-table company-table" dir="rtl">
                <thead>
                  <tr>
                    <th>{t('recent.noteNumber')}</th>
                    <th>{t('recent.company')}</th>
                    <th>{t('recent.factory')}</th>
                    <th>{t('recent.status')}</th>
                    <th>{t('recent.arrivalDate')}</th>
                    <th>{t('central.daysInQueue')}</th>
                    <th>{t('recent.delay')}</th>
                    <th className="central-no-print">{t('central.openCompanyReport')}</th>
                  </tr>
                </thead>
                <tbody>
                  {result.items.map((note) => (
                    <tr key={note.id}>
                      <th scope="row" className="tnum">{note.noteNumber}</th>
                      <td>{note.companyName ?? '—'}</td>
                      <td>{note.factoryName ?? '—'}</td>
                      <td>{note.statusLabel || translateStatus(note.status)}</td>
                      <td className="tnum">{note.arrivalAt ? formatDate(note.arrivalAt) : '—'}</td>
                      <td className="tnum">{note.daysInQueue === null || note.daysInQueue === undefined ? '—' : formatNumber(note.daysInQueue)}</td>
                      <td><span className={`central-status-pill ${note.isLate ? 'is-late' : 'is-ontime'}`}>{note.isLate ? t('v3.late') : t('v3.onTime')}</span></td>
                      <td className="central-no-print">
                        {note.companyId ? (
                          <a
                            href={fullReportHref(note.companyId, filters)}
                            className="text-xs font-semibold underline-offset-4 hover:underline"
                            style={{ color: 'var(--success)' }}
                          >
                            {t('central.openCompanyReport')}
                          </a>
                        ) : '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {result.items.length === 0 && <p className="company-table-empty">{t('central.reviewEmpty')}</p>}
            </div>
          </section>
          <div className="central-pager central-no-print">
            <button
              type="button"
              disabled={page === 0 || isLoading}
              onClick={() => setPage((p) => Math.max(0, p - 1))}
              className="central-btn"
            >
              {t('central.prevPage')}
            </button>
            <span className="tnum">{t('central.pageOf', { page: page + 1, pages: totalPages })}</span>
            <button
              type="button"
              disabled={page + 1 >= totalPages || isLoading}
              onClick={() => setPage((p) => p + 1)}
              className="central-btn"
            >
              {t('central.nextPage')}
            </button>
          </div>
        </>
      )}
    </div>
  )
}

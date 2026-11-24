import { useEffect, useMemo, useState } from 'react'
import { api, type SetupConfig } from '../../services/api'
import { FilterBar, loadFiltersFromUrlOrSaved, persistFilters, sanitizeFiltersAgainstLookups } from '../../components/FilterBar'
import { Header } from '../../components/Header'
import { OfflineBanner } from '../../components/OfflineBanner'
import { DashboardSnapshotError } from '../../components/DashboardSnapshotError'
import { SkeletonLoader } from '../../components/SkeletonLoader'
import { useDashboardData } from '../../hooks/useDashboardData'
import { indicatorStateLabel, t } from '../../i18n'
import { formatDate, formatDateTime, formatNumber, formatPercent } from '../../services/format'
import type { DashboardFilters, DashboardLookups } from '../../types/dashboard'

interface CompanyDashboardProps {
  config?: SetupConfig | null
  onEditConnection?: () => void
  selectedCompanyId?: string
}

function MetricCard({ title, value, hint, tone }: { title: string; value: string | number; hint: string; tone: string }) {
  return (
    <article className="modern-kpi-card" style={{ '--modern-kpi-tone': tone } as React.CSSProperties}>
      <p className="modern-kpi-title">{title}</p>
      <p className="modern-kpi-value tnum">{typeof value === 'number' ? formatNumber(value) : value}</p>
      <p className="modern-kpi-detail">{hint}</p>
    </article>
  )
}

function formatCanonicalPercent(value: number | null): string {
  if (value === null || Number.isNaN(value)) return t('common.notAvailable')
  return `${new Intl.NumberFormat('en-US', { maximumFractionDigits: 2 }).format(value)}%`
}

function formatMetricDelta(current: number | null | undefined, previous: number | null | undefined, suffix = ''): string {
  if (current === null || current === undefined || previous === null || previous === undefined) return t('common.notAvailable')
  const delta = Math.round((current - previous) * 100) / 100
  const sign = delta > 0 ? '+' : ''
  return `${sign}${new Intl.NumberFormat('en-US', { maximumFractionDigits: 2 }).format(delta)}${suffix}`
}

function formatPeriodRange(from: string | null | undefined, to: string | null | undefined): string {
  if (!from && !to) return 'كل المذكرات'
  if (!to) return 'من ' + formatDate(from)
  if (!from) return 'حتى ' + formatDate(to)
  return formatDate(from) + ' — ' + formatDate(to)
}
function overviewHref(): string {
  const params = new URLSearchParams(window.location.search)
  params.delete("companyId")
  const query = params.toString()
  return `${window.location.pathname}${query ? `?${query}` : ""}`
}

function reportHref(): string {
  const params = new URLSearchParams(window.location.search)
  params.set("report", "company")
  return `${window.location.pathname}?${params.toString()}`
}

export function CompanyDashboard({ config, onEditConnection, selectedCompanyId }: CompanyDashboardProps) {
  const [filters, setFilters] = useState<DashboardFilters>(() => {
    const initial = loadFiltersFromUrlOrSaved()
    return { ...initial, companyId: selectedCompanyId ?? initial.companyId }
  })
  const [lookups, setLookups] = useState<DashboardLookups | null>(null)
  const [filtersOpen, setFiltersOpen] = useState(false)
  const { data, isConnected, isLoading, lastUpdated, snapshotError, refreshNow } = useDashboardData(filters, lookups !== null)

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

  const companyMetrics = data?.companyMetrics ?? null
  const activeFilterCount = Object.values(filters).filter((value) => value !== '').length
  const isDisplayMode = new URLSearchParams(window.location.search).get('display') === '1'

  return (
    <div className={'app-bg min-h-screen app-text company-dashboard' + (isDisplayMode ? ' display-mode' : '')} dir="rtl">
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
      {!isConnected && <OfflineBanner message={isConnected ? null : t('common.connectionFailed')} />}
      <main className="company-dashboard-shell">
        <section className="company-toolbar app-surface" aria-labelledby="company-dashboard-title">
          <div className="company-toolbar-copy">
            <p className="company-eyebrow">{t('company.eyebrow')}</p>
            <h1 id="company-dashboard-title">{selectedCompany?.name ?? t('company.title')}</h1>
            <p>{t('company.selectorHint')}</p>
            {data && <p className="company-toolbar-updated">{t('company.updated')}: {formatDateTime(data.generatedAt)}</p>}
          </div>
          <label className="company-selector app-text">
            <span>{t('company.selector')}</span>
            <select
              value={filters.companyId}
              onChange={(event) => { const next = { ...filters, companyId: event.target.value, factoryId: '', productionLineId: '' }; persistFilters(next); setFilters(next) }}
              className="app-surface app-text"
              aria-label={t('company.selector')}
            >
              <option value="">{t('company.choose')}</option>
              {lookups?.companies.map((company) => <option key={company.id} value={company.id}>{company.name}</option>)}
            </select>
          </label>
          <a href={overviewHref()} className="text-sm font-semibold text-cyan-700 underline-offset-4 hover:underline">{t('company.backToOverview')}</a>
          <a href={reportHref()} className="text-sm font-semibold text-emerald-700 underline-offset-4 hover:underline">{t('report.openReport')}</a>
        </section>
        {!isDisplayMode && (
          <section className="app-surface rounded-2xl border app-border shadow-sm">
            <button type="button" className="w-full px-4 py-3 text-start text-sm font-semibold app-text" onClick={() => setFiltersOpen((open) => !open)} aria-expanded={filtersOpen}>
              {t('v3.filterStrip')} <span className="mx-2 app-text-muted">{activeFilterCount > 0 ? t('filters.filteredBar', { count: activeFilterCount }) : ''}</span>{filtersOpen ? '−' : '+'}
            </button>
            {filtersOpen && <div className="border-t app-border p-3"><FilterBar lookups={lookups} filters={filters} onChange={setFilters} /></div>}
          </section>
        )}

        {!selectedCompany ? (
          <div className="app-surface rounded-2xl border app-border p-10 text-center app-text-muted">{t('company.choose')}</div>
        ) : data && companyMetrics && !isLoading ? (
          <>

            <section className="company-kpi-grid" aria-label={t('company.title')}>
              <MetricCard title={t('company.total')} value={companyMetrics.totalNotes} hint={t('company.updated')} tone="var(--acc-cyan)" />
              <MetricCard title={t('company.reviewCoverage')} value={formatCanonicalPercent(companyMetrics.reviewCoveragePercentage)} hint={`${formatNumber(companyMetrics.reviewedItems)} / ${formatNumber(companyMetrics.expectedItems)}`} tone="var(--acc-violet)" />
              <MetricCard title={t('company.itemQuality')} value={formatCanonicalPercent(companyMetrics.itemQualityPercentage)} hint={`${formatNumber(companyMetrics.correctItems)} / ${formatNumber(companyMetrics.applicableReviewedItems)}`} tone="var(--acc-emerald)" />
              <MetricCard title={t('company.cleanReviewed')} value={formatCanonicalPercent(companyMetrics.cleanReviewedNotePercent)} hint={`${formatNumber(companyMetrics.cleanReviewedNotes)} / ${formatNumber(companyMetrics.qualityEligibleNoteCount)} · ${t('company.allNotApplicable')}: ${formatNumber(companyMetrics.allNotApplicableNoteCount)}`} tone="var(--acc-emerald)" />
              <MetricCard title={t('company.errorsPer100')} value={formatCanonicalPercent(companyMetrics.errorsPer100Checked)} hint={formatNumber(companyMetrics.errorItems)} tone="var(--acc-rose)" />
              <MetricCard title={t('company.timeCompliance')} value={formatCanonicalPercent(companyMetrics.timeCompliancePercentage)} hint={`${formatNumber(companyMetrics.onTimeNotes)} / ${formatNumber(companyMetrics.timeEligibleNotes)}`} tone="var(--acc-amber)" />
              <MetricCard title={t('company.incidentsPer100')} value={formatCanonicalPercent(companyMetrics.incidentsPer100Notes)} hint={`${formatNumber(companyMetrics.duplicateIncidents)} / ${formatNumber(companyMetrics.totalNotes)}`} tone="var(--acc-rose)" />
              <MetricCard title={t('company.affectedRate')} value={formatCanonicalPercent(companyMetrics.affectedNoteRatePercentage)} hint={`${formatNumber(companyMetrics.affectedNotes)} / ${formatNumber(companyMetrics.totalNotes)}`} tone="var(--acc-amber)" />
              <MetricCard title={t('company.linkEfficiency')} value={formatCanonicalPercent(companyMetrics.linkEfficiencyPercentage)} hint={`${formatNumber(companyMetrics.linkedEntryNotes)} / ${formatNumber(companyMetrics.entryNotes)}`} tone="var(--acc-cyan)" />
            </section>

            {data.previousCompanyMetrics && data.previousPeriod && (
              <section className="company-table-panel app-surface">
                <h2>{t('company.previousPeriodComparison')}</h2>
                <p className="company-period-range">{t('company.currentPeriod')}: {formatPeriodRange(data.effectiveFilters.from, data.effectiveFilters.to)} · {t('company.previousPeriod')}: {formatPeriodRange(data.previousPeriod.from, data.previousPeriod.to)}</p>
                <p className="company-period-note">{t('company.changeNote')}</p>
                <div className="company-table-scroll">
                  <table className="company-table" dir="rtl">
                    <thead><tr><th>{t('company.metric')}</th><th>{t('company.currentPeriod')}</th><th>{t('company.previousPeriod')}</th><th>{t('company.change')}</th></tr></thead>
                    <tbody>
                      <tr><th scope="row">{t('company.total')}</th><td>{formatNumber(companyMetrics.totalNotes)}</td><td>{formatNumber(data.previousCompanyMetrics.totalNotes)}</td><td>{formatMetricDelta(companyMetrics.totalNotes, data.previousCompanyMetrics.totalNotes)}</td></tr>
                      <tr><th scope="row">{t('company.itemQuality')}</th><td>{formatCanonicalPercent(companyMetrics.itemQualityPercentage)}</td><td>{formatCanonicalPercent(data.previousCompanyMetrics.itemQualityPercentage)}</td><td>{formatMetricDelta(companyMetrics.itemQualityPercentage, data.previousCompanyMetrics.itemQualityPercentage, t('company.percentagePoints'))}</td></tr>
                      <tr><th scope="row">{t('company.timeCompliance')}</th><td>{formatCanonicalPercent(companyMetrics.timeCompliancePercentage)}</td><td>{formatCanonicalPercent(data.previousCompanyMetrics.timeCompliancePercentage)}</td><td>{formatMetricDelta(companyMetrics.timeCompliancePercentage, data.previousCompanyMetrics.timeCompliancePercentage, t('company.percentagePoints'))}</td></tr>
                      <tr><th scope="row">{t('company.errorsPer100')}</th><td>{formatCanonicalPercent(companyMetrics.errorsPer100Checked)}</td><td>{formatCanonicalPercent(data.previousCompanyMetrics.errorsPer100Checked)}</td><td>{formatMetricDelta(companyMetrics.errorsPer100Checked, data.previousCompanyMetrics.errorsPer100Checked, t('company.percentagePoints'))}</td></tr>
                    </tbody>
                  </table>
                </div>
              </section>
            )}
            <section className="grid grid-cols-1 gap-4 xl:grid-cols-2">
              <article className="company-table-panel app-surface">
                <h2>{t('charts.monthlyActivity')}</h2>
                <div className="company-table-scroll">
                  <table className="company-table" dir="rtl">
                    <thead><tr><th>{t('company.month')}</th><th>{t('charts.notesCount')}</th></tr></thead>
                    <tbody>{data.monthlyActivity.map((row) => <tr key={row.month}><th scope="row">{row.month}</th><td>{formatNumber(row.count)}</td></tr>)}</tbody>
                  </table>
                  {data.monthlyActivity.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
                </div>
              </article>
              <article className="company-table-panel app-surface">
                <h2>{t('charts.incomingVsApproved')}</h2>
                <div className="company-table-scroll">
                  <table className="company-table" dir="rtl">
                    <thead><tr><th>{t('company.month')}</th><th>{t('charts.incoming')}</th><th>{t('charts.approved')}</th></tr></thead>
                    <tbody>{data.incomingVsApproved.map((row) => <tr key={row.label}><th scope="row">{row.label}</th><td>{formatNumber(row.incoming)}</td><td>{formatNumber(row.approved)}</td></tr>)}</tbody>
                  </table>
                  {data.incomingVsApproved.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
                </div>
              </article>
            </section>

            <section className="grid grid-cols-1 gap-4 xl:grid-cols-2">
              <article className="company-table-panel app-surface">
                <h2>{t('charts.duplicateTrend')}</h2>
                <div className="company-table-scroll">
                  <table className="company-table" dir="rtl">
                    <thead><tr><th>{t('company.month')}</th><th>{t('charts.duplicateTrend.resend')}</th><th>{t('charts.duplicateTrend.duplicateNumber')}</th></tr></thead>
                    <tbody>{data.duplicateTrend.map((row) => <tr key={row.label}><th scope="row">{row.label}</th><td>{formatNumber(row.companyResend)}</td><td>{formatNumber(row.duplicateNumber)}</td></tr>)}</tbody>
                  </table>
                  {data.duplicateTrend.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
                </div>
              </article>
              <article className="company-table-panel app-surface">
                <h2>{t('charts.duplicateBreakdown')}</h2>
                <ul className="space-y-2 text-sm app-text">
                  {data.duplicateBreakdown.map((row) => <li key={row.classification} className="flex items-center justify-between gap-3"><span>{row.label}</span><strong>{formatNumber(row.count)}</strong></li>)}
                </ul>
                {data.duplicateBreakdown.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
              </article>
            </section>

            <section className="company-tables-grid">
              <article className="company-table-panel app-surface">
                <h2>{t('company.factoryTitle')}</h2>
                <div className="company-table-scroll">
                  <table className="company-table" dir="rtl">
                    <thead><tr><th>{t('company.factory')}</th><th>{t('v3.total')}</th><th>{t('company.qualityRatio')}</th><th>{t('v3.accuracy')}</th><th>{t('company.slaRatio')}</th><th>{t('charts.sla.late')}</th></tr></thead>
                    <tbody>{data.performanceByFactory.map((row) => <tr key={row.label}><th scope="row">{row.label}</th><td>{formatNumber(row.totalNotes)}</td><td>{formatNumber(row.correctCount)} / {formatNumber(row.qualityEligibleCount)}</td><td>{formatPercent(row.accuracyRate)}</td><td>{formatNumber(row.onTimeCount)} / {formatNumber(row.eligibleCount)}</td><td>{formatNumber(row.lateCount)}</td></tr>)}</tbody>
                  </table>
                  {data.performanceByFactory.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
                </div>
              </article>
              <article className="company-table-panel app-surface">
                <h2>{t('company.recentTitle')}</h2>
                <div className="company-table-scroll">
                  <table className="company-table" dir="rtl">
                    <thead><tr><th>{t('recent.noteNumber')}</th><th>{t('recent.factory')}</th><th>{t('recent.arrivalDate')}</th><th>{t('recent.status')}</th></tr></thead>
                    <tbody>{data.recentNotes.map((note) => <tr key={note.id}><th scope="row">{note.noteNumber}</th><td>{note.factoryName ?? '—'}</td><td>{note.arrivalAt ? formatDateTime(note.arrivalAt) : '—'}</td><td>{note.statusLabel}</td></tr>)}</tbody>
                  </table>
                  {data.recentNotes.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
                </div>
              </article>

            </section>

            <section className="grid grid-cols-1 gap-4 xl:grid-cols-2">
              <article className="company-table-panel app-surface">
                <h2>{t('charts.performance.line')}</h2>
                <div className="company-table-scroll">
                  <table className="company-table" dir="rtl">
                    <thead><tr><th>{t('recent.line')}</th><th>{t('v3.total')}</th><th>{t('charts.qualityByCompany.sample')}</th><th>{t('v3.accuracy')}</th><th>{t('charts.sla.eligible')}</th><th>{t('charts.sla.late')}</th><th>{t('charts.sla.onTime')}</th></tr></thead>
                    <tbody>{data.performanceByLine.map((row) => <tr key={row.label}><th scope="row">{row.label}</th><td>{formatNumber(row.totalNotes)}</td><td>{formatNumber(row.qualityEligibleCount)}</td><td>{formatPercent(row.accuracyRate)}</td><td>{formatNumber(row.eligibleCount)}</td><td>{formatNumber(row.lateCount)}</td><td>{formatNumber(row.onTimeCount)}</td></tr>)}</tbody>
                  </table>
                  {data.performanceByLine.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
                </div>
              </article>
              <article className="company-table-panel app-surface">
                <h2>{t('charts.qualityByCompany')}</h2>
                <div className="company-table-scroll">
                  <table className="company-table" dir="rtl">
                    <thead><tr><th>{t('company.company')}</th><th>{t('charts.qualityByCompany.sample')}</th><th>{t('charts.qualityByCompany.accuracy')}</th><th>{t('charts.qualityByCompany.error')}</th></tr></thead>
                    <tbody>{data.qualityByCompany.map((row) => <tr key={row.label}><th scope="row">{row.label}</th><td>{formatNumber(row.qualityEligibleCount ?? row.sampleSize)}</td><td>{formatPercent(row.accuracyRate)}</td><td>{formatPercent(row.errorRate)}</td></tr>)}</tbody>
                  </table>
                  {data.qualityByCompany.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
                </div>
                <ul className="mt-4 space-y-2 text-sm app-text">
                  {data.topErrorFields.slice(0, 10).map((row) => <li key={row.label} className="flex items-center justify-between gap-3"><span>{row.label}</span><span>{formatNumber(row.errorCount)} · {formatPercent(row.errorRate)}</span></li>)}
                </ul>
              </article>
            </section>
            <section className="grid grid-cols-1 gap-4 xl:grid-cols-3">
              <article className="company-table-panel app-surface">
                <h2>{t('charts.statusDistribution')}</h2>
                <ul className="space-y-2 text-sm app-text">
                  {data.statusDistribution.map((row) => <li key={row.status} className="flex items-center justify-between gap-3"><span>{row.statusLabel}</span><strong>{formatNumber(row.count)}</strong></li>)}
                </ul>
              </article>
              <article className="company-table-panel app-surface">
                <h2>{t('charts.indicatorStates')}</h2>
                <ul className="space-y-2 text-sm app-text">
                  {data.indicatorStateDistribution.map((row) => <li key={row.code} className="flex items-center justify-between gap-3"><span>{indicatorStateLabel(row.code)}</span><strong>{formatNumber(row.value)}</strong></li>)}
                </ul>
                {data.indicatorStateDistribution.length === 0 && <p className="company-table-empty">{t('company.empty')}</p>}
              </article>
              <article className="company-table-panel app-surface">
                <h2>{t('charts.backlog')}</h2>
                <ul className="space-y-2 text-sm app-text">
                  {data.backlogByAge.map((row) => <li key={row.code} className="flex items-center justify-between gap-3"><span>{row.label}</span><strong>{formatNumber(row.value)}</strong></li>)}
                </ul>
              </article>
            </section>

          </>
        ) : isLoading ? <SkeletonLoader /> : <DashboardSnapshotError error={snapshotError} onRetry={refreshNow} isLoading={isLoading} />}
      </main>
      <footer className="app-surface border-t app-border py-4 text-center text-sm font-bold app-text-muted">{t('app.footer', { yearRange: `${new Date().getFullYear()}-${new Date().getFullYear() + 1}` })}</footer>
    </div>
  )
}







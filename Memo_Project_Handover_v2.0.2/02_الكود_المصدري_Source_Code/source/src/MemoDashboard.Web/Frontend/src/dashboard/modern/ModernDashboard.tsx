import { useEffect, useState, type CSSProperties } from 'react'
import { api, type SetupConfig } from '../../services/api'
import { FilterBar, loadFiltersFromUrlOrSaved, persistFilters, sanitizeFiltersAgainstLookups } from '../../components/FilterBar'
import { Header } from '../../components/Header'
import { OfflineBanner } from '../../components/OfflineBanner'
import { DashboardSnapshotError } from '../../components/DashboardSnapshotError'
import { SkeletonLoader } from '../../components/SkeletonLoader'
import { useDashboardData } from '../../hooks/useDashboardData'
import { t } from '../../i18n'
import { formatDateTime, formatNumber, formatPercent } from '../../services/format'
import type { DashboardFilters, DashboardLookups } from '../../types/dashboard'
import { ModernInsightGrid } from './ModernInsightGrid'

interface ModernDashboardProps {
  config?: SetupConfig | null
  onEditConnection?: () => void
}

const clamp = (value: number, min: number, max: number): number => Math.min(max, Math.max(min, value))

function AnimatedMetric({ value }: { value: string | number }) {
  const text = typeof value === 'number' ? formatNumber(value) : value
  return <span key={text} className="modern-animated-number">{text}</span>
}

function ExecutiveKpi({ title, value, detail, tone, numericValue }: { title: string; value: string | number; detail: string; tone: string; numericValue?: number }) {
  return (
    <article className="modern-kpi-card modern-reveal" style={{ '--modern-kpi-tone': tone } as CSSProperties}>
      <div className="modern-kpi-glow" aria-hidden="true" />
      <p className="modern-kpi-title">{title}</p>
      <p className="modern-kpi-value"><AnimatedMetric value={value} /></p>
      <p className="modern-kpi-detail">{numericValue === undefined ? detail : `${detail} · ${formatNumber(numericValue)}`}</p>
    </article>
  )
}

function CountRow({ title, value }: { title: string; value: number }) {
  return (
    <div className="modern-pressure-item flex items-center justify-between gap-3">
      <span className="font-semibold app-text">{title}</span>
      <span className="font-bold app-text-muted">{formatNumber(value)}</span>
    </div>
  )
}
function CompanyRank({ name, value, maximum, index }: { name: string; value: number; maximum: number; index: number }) {
  const width = maximum > 0 ? clamp((value / maximum) * 100, 0, 100) : 0
  return (
    <div className="modern-company-row" style={{ '--modern-company-delay': `${index * 70}ms` } as CSSProperties}>
      <span className="modern-company-rank">{formatNumber(index + 1)}</span>
      <div className="min-w-0 flex-1">
        <div className="flex items-center justify-between gap-3 text-sm"><span className="truncate font-semibold app-text">{name}</span><span className="font-bold app-text-muted">{formatNumber(value)}</span></div>
        <div className="modern-progress-track mt-2"><span className="modern-progress-value" style={{ width: `${width}%` }} /></div>
      </div>
    </div>
  )
}

export function ModernDashboard({ config, onEditConnection }: ModernDashboardProps) {
  const [filters, setFilters] = useState<DashboardFilters>(loadFiltersFromUrlOrSaved)
  const [lookups, setLookups] = useState<DashboardLookups | null>(null)
  const { data, isConnected, isLoading, lastUpdated, snapshotError, refreshNow } = useDashboardData(filters, lookups !== null)
  const isDisplayMode = new URLSearchParams(window.location.search).get('display') === '1'
  const [filtersOpen, setFiltersOpen] = useState(false)

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
      setFilters((current) => {
        const next = sanitizeFiltersAgainstLookups(current, { companies: [], factories: [], productionLines: [], noteTypes: [] })
        if (JSON.stringify(next) !== JSON.stringify(current)) persistFilters(next)
        return next
      })
      setLookups({ companies: [], factories: [], productionLines: [], noteTypes: [] })
    })
    return () => { cancelled = true }
  }, [])

  const activeFilterCount = Object.values(filters).filter((value) => value !== '').length
  const canonical = data?.companyMetrics ?? null
  const printDashboard = () => {
    window.dispatchEvent(new Event('resize'))
    window.setTimeout(() => window.print(), 150)
  }

  return (
    <div className={'app-bg min-h-screen app-text modern-executive' + (isDisplayMode ? ' display-mode' : '')}>
      <Header
        isConnected={isConnected}
        isLoading={isLoading}
        lastUpdated={lastUpdated}
        onRefresh={refreshNow}
        onEditConnection={onEditConnection}
        isDisplayMode={isDisplayMode}
        organizationName={config?.organizationName}
        subOrganizationName={config?.subOrganizationName}
        onPrint={printDashboard}
      />
      {!isConnected && <OfflineBanner message={isConnected ? null : t('common.connectionFailed')} />}
      <main className="mx-auto max-w-[1800px] space-y-6 px-4 py-5">
        {!isDisplayMode && (
          <div className="modern-filter-drawer app-surface rounded-2xl border app-border shadow-sm">
            <button type="button" className="modern-filter-toggle" onClick={() => setFiltersOpen((open) => !open)} aria-expanded={filtersOpen}>
              <span><span className="modern-filter-pulse" aria-hidden="true" />{t('v2.executiveMode')}</span>
              <span className="app-text-muted">{activeFilterCount > 0 ? t('filters.filteredBar', { count: activeFilterCount }) : t('v2.statusSummary')} <span aria-hidden="true">{filtersOpen ? '−' : '+'}</span></span>
            </button>
            {filtersOpen && <div className="border-t app-border p-3"><FilterBar lookups={lookups} filters={filters} onChange={setFilters} /></div>}
          </div>
        )}
        {activeFilterCount > 0 && !filtersOpen && (
          <p className="rounded-xl border app-border app-surface-muted px-4 py-2 text-sm app-text-muted">{t('filters.filteredBar', { count: activeFilterCount })}</p>
        )}
        {data && canonical ? (
          <>
            <section className="modern-executive-hero modern-reveal" aria-labelledby="modern-overview-title">
              <div className="modern-hero-grid">
                <div className="modern-hero-copy">
                  <p className="modern-eyebrow">{t('v2.executiveEyebrow')}</p>
                  <h2 id="modern-overview-title" className="modern-hero-title">{t('v2.executiveTitle')}</h2>
                  <p className="modern-hero-description">{t('v2.executiveDescription')}</p>
                  <div className="modern-hero-meta">
                    <span className="modern-status-pill">{t('v2.statusSummary')}</span>
                    <span>{t('v2.animatedHint')}</span>
                  </div>
                </div>
              </div>
            </section>

            <section aria-labelledby="modern-kpi-title">
              <div className="mb-3 flex flex-wrap items-end justify-between gap-2">
                <div><p className="modern-eyebrow app-text-muted">{t('v2.statusSummary')}</p><h2 id="modern-kpi-title" className="text-xl font-bold app-text">{t('v2.healthLabel')}</h2></div>
                <span className="text-sm app-text-muted">{data.generatedAt ? `${t('v2.lastRefresh')}: ${formatDateTime(data.generatedAt)}` : t('v2.noCriticalRisks')}</span>
              </div>
              <div className="modern-kpi-grid">
                <ExecutiveKpi title={t('v2.kpiTotal')} value={data.summary.totalNotes} detail={t('v2.executiveMode')} tone="var(--acc-cyan)" />
                <ExecutiveKpi title={t('v2.kpiAccuracy')} value={formatPercent(canonical.itemQualityPercentage)} detail={t('v2.accuracyHint')} tone="var(--acc-emerald)" />
                 <ExecutiveKpi title={t('v2.kpiFieldAccuracy')} value={formatPercent(data.fieldQuality?.accuracyRate)} detail={t('v2.fieldAccuracyHint')} tone="var(--acc-violet)" />
                <ExecutiveKpi title={t('v2.kpiCorrectMemos')} value={canonical.cleanReviewedNotes} detail={t('v2.accuracyHint')} tone="var(--acc-cyan)" />
                <ExecutiveKpi title={t('v2.kpiErrorMemos')} value={canonical.errorNotes} detail={t('v2.errorHint')} tone="var(--acc-rose)" />
                <ExecutiveKpi title={t('v2.kpiLateTotal')} value={canonical.lateNotes} detail={t('v2.driversTitle')} tone="var(--acc-rose)" />
                <ExecutiveKpi title={t('v2.kpiReviewBacklog')} value={data.summary.pendingReview} detail={t('v2.healthDescription')} tone="var(--acc-amber)" />
              </div>
            </section>

            <section aria-labelledby="modern-insights-title">
              <div className="mb-3 flex flex-wrap items-end justify-between gap-2">
                <div><p className="modern-eyebrow app-text-muted">{t('v2.trendTitle')}</p><h2 id="modern-insights-title" className="text-xl font-bold app-text">{t('v2.trendTitle')}</h2></div>
                <span className="text-sm app-text-muted">{t('v2.trendDescription')}</span>
              </div>
              <ModernInsightGrid snapshot={data} />
            </section>

            <section className="grid grid-cols-1 gap-4 lg:grid-cols-[1.05fr_0.95fr]" aria-labelledby="modern-drivers-title">
              <article className="modern-surface-card modern-reveal">
                <div className="mb-5"><p className="modern-eyebrow app-text-muted">{t('v2.driversTitle')}</p><h2 id="modern-drivers-title" className="text-lg font-bold app-text">{t('v2.driversTitle')}</h2><p className="mt-1 text-sm app-text-muted">{t('v2.driversDescription')}</p></div>
                <div className="space-y-5">
                  <CountRow title={t('v2.kpiLateTotal')} value={canonical.lateNotes} />
                  <CountRow title={t('v2.kpiReviewBacklog')} value={data.summary.pendingReview} />
                  <CountRow title={t('v2.kpiRiskSignal')} value={data.summary.companyResend + data.summary.duplicateNumbers} />
                </div>
              </article>
              <article className="modern-surface-card modern-reveal">
                <div className="mb-5"><p className="modern-eyebrow app-text-muted">{t('v2.companiesTitle')}</p><h2 className="text-lg font-bold app-text">{t('v2.companiesTitle')}</h2><p className="mt-1 text-sm app-text-muted">{t('v2.companiesDescription')}</p></div>
                {data.companies.length > 0 ? <div className="space-y-4">{data.companies.map((company, index) => <CompanyRank key={company.name} name={company.name} value={company.noteCount} maximum={data.companies[0]?.noteCount ?? company.noteCount} index={index} />)}</div> : <p className="text-sm app-text-muted">{t('app.noData')}</p>}
              </article>
            </section>
          </>
        ) : isLoading ? <SkeletonLoader /> : <DashboardSnapshotError error={snapshotError} onRetry={refreshNow} isLoading={isLoading} />}
      </main>
      <footer className="app-surface border-t app-border py-4 text-center text-sm font-bold app-text-muted">{t('app.footer', { yearRange: String(new Date().getFullYear()) + '-' + String(new Date().getFullYear() + 1) })}</footer>
    </div>
  )
}

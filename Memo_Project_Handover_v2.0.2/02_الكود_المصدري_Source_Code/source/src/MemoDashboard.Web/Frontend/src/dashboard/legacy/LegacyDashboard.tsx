import { useEffect, useState } from 'react'
import { api, type SetupConfig } from '../../services/api'
import { loadFiltersFromUrlOrSaved, persistFilters, sanitizeFiltersAgainstLookups } from '../../components/FilterBar'
import { useDashboardData } from '../../hooks/useDashboardData'
import type { CompanyTopItem, DashboardFilters, DashboardLookups, DashboardSnapshot } from '../../types/dashboard'
import { Header } from '../../components/Header'
import { FilterBar } from '../../components/FilterBar'
import { centralHref } from '../central/CentralDashboard'
import { useDirection } from '../../i18n/LanguageProvider'
import { RecentNotesTable } from '../../components/RecentNotesTable'
import { SkeletonLoader } from '../../components/SkeletonLoader'
import { OfflineBanner } from '../../components/OfflineBanner'
import { DashboardSnapshotError } from '../../components/DashboardSnapshotError'
import {
  DEFAULT_SECTION,
  LEGACY_SECTION_KEYS,
  LegacySectionNav,
  LegacySectionPanel,
  type LegacySectionKey,
} from './LegacySectionNav'
import { LegacyKpiSections } from './LegacyKpiSections'
import { LegacyChartSections } from './LegacyChartSections'
import { t } from '../../i18n'
import { formatNumber } from '../../services/format'

interface LegacyDashboardProps {
  config?: SetupConfig | null
  onEditConnection?: () => void
}


function companyDetailsHref(id: string, filters: DashboardFilters): string {
  const params = new URLSearchParams(window.location.search)
  params.set("companyId", id)
  const filterKeys: Array<keyof DashboardFilters> = ["from", "to", "factoryId", "productionLineId", "noteTypeId", "status", "entryCutoffDate"]
  for (const key of filterKeys) {
    const value = filters[key]
    if (value) params.set(key, value)
    else params.delete(key)
  }
  const query = params.toString()
  return `${window.location.pathname}${query ? `?${query}` : ""}`
}

function companyReportHref(id: string): string {
  const params = new URLSearchParams(window.location.search)
  params.set("companyId", id)
  params.set("report", "company")
  return `${window.location.pathname}?${params.toString()}`
}

function CompanyDirectory({ companies, filters }: { companies: CompanyTopItem[]; filters: DashboardFilters }) {
  const [query, setQuery] = useState("")
  const normalizedQuery = query.trim().toLocaleLowerCase()
  const visibleCompanies = normalizedQuery
    ? companies.filter((company) => company.name.toLocaleLowerCase().includes(normalizedQuery))
    : companies
  return (
    <section className="app-surface rounded-2xl border app-border p-4 shadow-sm" aria-labelledby="company-directory-title">
      <div className="mb-3 flex flex-wrap items-start justify-between gap-2">
        <div>
          <p className="text-xs font-bold uppercase tracking-[0.16em] app-text-muted">{t("company.eyebrow")}</p>
          <h2 id="company-directory-title" className="text-lg font-bold app-text">{t("charts.companies")}</h2>
          <p className="mt-1 text-sm app-text-muted">{t("company.selectorHint")}</p>
          <label className="mt-3 block max-w-sm">
            <span className="sr-only">{t("company.search")}</span>
            <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder={t("company.search")} className="w-full rounded-lg border app-border app-surface px-3 py-2 text-sm app-text" />
          </label>
        </div>
      </div>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {visibleCompanies.map((company, index) => (
          <a key={company.id} href={companyDetailsHref(company.id, filters)} className="app-hover rounded-xl border app-border app-surface-muted p-3 transition-colors" aria-label={`${t("company.openDetails")}: ${company.name}`}>
            <div className="flex items-center justify-between gap-3">
              <span className="text-xs font-bold app-text-muted">#{index + 1}</span>
              <span className="text-sm font-bold app-text">{formatNumber(company.noteCount)}</span>
            </div>
            <p className="mt-2 truncate font-semibold app-text" title={company.name}>{company.name}</p>
            <span className="mt-2 inline-block text-xs font-semibold" style={{ color: 'var(--primary)' }}>{t("company.openDetails")}</span>
            <span className="mx-1 inline-block text-xs app-text-muted">·</span>
            <span
              role="link"
              tabIndex={0}
              onClick={(event) => { event.preventDefault(); event.stopPropagation(); window.location.assign(companyReportHref(company.id)) }}
              onKeyDown={(event) => { if (event.key === 'Enter') window.location.assign(companyReportHref(company.id)) }}
              className="mt-2 inline-block cursor-pointer text-xs font-semibold underline-offset-4 hover:underline"
              style={{ color: 'var(--success)' }}
            >{t("report.openReport")}</span>
          </a>
        ))}
      </div>
    </section>
  )
}

export function LegacyDashboardContent(key: LegacySectionKey, snapshot: DashboardSnapshot, compact: boolean) {
  switch (key) {
    case 'summary':
    case 'delays':
    case 'repetition':
      return (
        <>
          <LegacyKpiSections group={key} summary={snapshot.summary} companyMetrics={snapshot.companyMetrics} compact={compact} />
          <LegacyChartSections group={key} snapshot={snapshot} compact={compact} />
        </>
      )
    case 'workflow':
      return (
        <>
          <LegacyKpiSections group="workflow" summary={snapshot.summary} companyMetrics={snapshot.companyMetrics} compact={compact} />
          <LegacyChartSections group="workflow" snapshot={snapshot} compact={compact} />
        </>
      )
    case 'recent':
      return <RecentNotesTable notes={snapshot.recentNotes} />
  }
}

export function LegacyDashboard({ config, onEditConnection }: LegacyDashboardProps) {
  const dir = useDirection()
  const [filters, setFilters] = useState<DashboardFilters>(loadFiltersFromUrlOrSaved)
  const [lookups, setLookups] = useState<DashboardLookups | null>(null)
  const [activeSection, setActiveSection] = useState<LegacySectionKey>(DEFAULT_SECTION)
  const [isPrinting, setIsPrinting] = useState(false)
  const { data, isConnected, isLoading, lastUpdated, snapshotError, refreshNow } = useDashboardData(filters, lookups !== null)

  const isDisplayMode = new URLSearchParams(window.location.search).get('display') === '1'
  const showAllSections = isDisplayMode || isPrinting

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

  useEffect(() => {
    if (typeof window === 'undefined') return
    const onBeforePrint = () => setIsPrinting(true)
    const onAfterPrint = () => setIsPrinting(false)
    window.addEventListener('beforeprint', onBeforePrint)
    window.addEventListener('afterprint', onAfterPrint)

    if (!window.matchMedia) {
      return () => {
        window.removeEventListener('beforeprint', onBeforePrint)
        window.removeEventListener('afterprint', onAfterPrint)
      }
    }

    const media = window.matchMedia('print')
    setIsPrinting(media.matches)
    const onChange = (event: MediaQueryListEvent) => setIsPrinting(event.matches)
    if (typeof media.addEventListener === 'function') {
      media.addEventListener('change', onChange)
      return () => {
        window.removeEventListener('beforeprint', onBeforePrint)
        window.removeEventListener('afterprint', onAfterPrint)
        media.removeEventListener('change', onChange)
      }
    }
    if (typeof media.addListener === 'function') {
      media.addListener(onChange)
      return () => {
        window.removeEventListener('beforeprint', onBeforePrint)
        window.removeEventListener('afterprint', onAfterPrint)
        media.removeListener(onChange)
      }
    }
    return () => {
      window.removeEventListener('beforeprint', onBeforePrint)
      window.removeEventListener('afterprint', onAfterPrint)
    }
  }, [])

  const setupConfig = config
  const printDashboard = () => {
    window.dispatchEvent(new Event('resize'))
    setIsPrinting(true)
    window.setTimeout(() => window.print(), 300)
  }
  const activeFilterCount = Object.values(filters).filter((value) => value !== '').length

  return (
    <div className={'legacy-dashboard app-bg min-h-screen app-text' + (isDisplayMode ? ' display-mode' : '')} dir={dir}>
      <Header
        isConnected={isConnected}
        isLoading={isLoading}
        lastUpdated={lastUpdated}
        onRefresh={refreshNow}
        onEditConnection={onEditConnection}
        isDisplayMode={isDisplayMode}
        organizationName={setupConfig?.organizationName}
        subOrganizationName={setupConfig?.subOrganizationName}
        onPrint={printDashboard}
      />
      {!isConnected && <OfflineBanner message={isConnected ? null : t('common.connectionFailed')} />}
      <main className={'mx-auto max-w-[1800px] space-y-5 px-4 py-4' + (isDisplayMode ? ' dashboard-display' : '')}>
        {!isDisplayMode && (
          <section className="app-surface rounded-2xl border app-border p-4 shadow-sm" aria-label={t('central.title')}>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="text-xs font-bold uppercase tracking-[0.16em] app-text-muted">{t('central.subtitle')}</p>
                <h2 className="text-lg font-bold app-text">{t('central.title')}</h2>
                <p className="mt-1 text-sm app-text-muted">{t('central.openCentralHint')}</p>
              </div>
              <a href={centralHref()} className="btn-primary rounded-lg px-4 py-2 text-sm font-semibold">
                {t('central.openCentral')}
              </a>
            </div>
          </section>
        )}
        {!isDisplayMode && <FilterBar lookups={lookups} filters={filters} onChange={setFilters} />}
        <section className="print-report-meta" dir="auto">
          <h2>{t('app.title')}</h2>
          <p>{t('common.lastUpdated')}: {new Date().toLocaleString()}</p>
          <p>{activeFilterCount > 0 ? t('filters.filteredBar', { count: activeFilterCount }) : t('common.all')}</p>
        </section>
        {activeFilterCount > 0 && data && (
          <section
            className="chip-tint flex flex-wrap items-center gap-2 rounded-xl border px-4 py-2.5 text-sm"
            style={{ ['--c' as string]: 'var(--acc-amber)' }}
          >
            <span className="rounded-full border border-current px-2.5 py-0.5 text-xs font-bold">{t('filters.filteredBadge')}</span>
            <span>
              {t('filters.filteredBar', { count: activeFilterCount })} <b className="tnum">{data.summary.totalNotes}</b>
            </span>
          </section>
        )}
        {data && data.companies.length > 0 && <CompanyDirectory companies={data.companies} filters={filters} />}
        {data ? (
          <>
            {!showAllSections && <LegacySectionNav activeSection={activeSection} onChange={setActiveSection} />}
            <div key={showAllSections ? 'full-report' : activeSection} className={showAllSections ? 'space-y-6' : 'space-y-5'}>
              {showAllSections
                ? LEGACY_SECTION_KEYS.map((key) => (
                    <LegacySectionPanel key={key} section={key} stacked>
                      {LegacyDashboardContent(key, data, isDisplayMode)}
                    </LegacySectionPanel>
                  ))
                : (
                  <LegacySectionPanel section={activeSection}>
                    {LegacyDashboardContent(activeSection, data, isDisplayMode)}
                  </LegacySectionPanel>
                )}
            </div>
          </>
        ) : isLoading ? (
          <SkeletonLoader />
        ) : (
          <DashboardSnapshotError error={snapshotError} onRetry={refreshNow} isLoading={isLoading} />
        )}
      </main>
      <footer className="app-surface border-t app-border py-4 text-center text-sm font-bold app-text-muted">
        {t('app.footer', {
          yearRange: String(new Date().getFullYear()) + '-' + String(new Date().getFullYear() + 1),
        })}
      </footer>
    </div>
  )
}


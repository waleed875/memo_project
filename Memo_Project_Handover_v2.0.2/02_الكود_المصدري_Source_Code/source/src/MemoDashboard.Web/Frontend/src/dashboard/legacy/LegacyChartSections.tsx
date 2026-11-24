import { useMemo } from 'react'
import {
  ArcElement,
  BarElement,
  CategoryScale,
  Chart as ChartJS,
  Filler,
  Legend,
  LinearScale,
  LineElement,
  PointElement,
  Tooltip,
  type ChartData,
  type ChartOptions,
  type TooltipItem,
} from 'chart.js'
import { Bar, Doughnut, Line } from 'react-chartjs-2'
import type { DashboardSnapshot, PerformanceItem, StatusDistributionItem } from '../../types/dashboard'
import { t, translateStatus, indicatorStateLabel } from '../../i18n'
import { formatChartNumber, formatNumber, formatPercent, formatMonthYear, hasEnoughSample } from '../../services/format'
import { useChartTheme } from '../../hooks/useChartTheme'
import type { LegacySectionKey } from './LegacySectionNav'

ChartJS.register(
  ArcElement,
  BarElement,
  CategoryScale,
  Filler,
  Legend,
  LinearScale,
  LineElement,
  PointElement,
  Tooltip,
)

const CHART_PALETTE = ['#10b981', '#f59e0b', '#f43f5e', '#06b6d4', '#8b5cf6', '#f97316', '#94a3b8']

function withAlpha(hex: string, alpha: number): string {
  const value = hex.trim()
  if (!value.startsWith('#') || value.length < 7) return value
  const r = parseInt(value.slice(1, 3), 16)
  const g = parseInt(value.slice(3, 5), 16)
  const b = parseInt(value.slice(5, 7), 16)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

const BACKLOG_CODE_KEYS: Record<string, string> = {
  'lt-1-day': 'charts.backlog.lessThanDay',
  '1-3-days': 'charts.backlog.1to3',
  '4-7-days': 'charts.backlog.4to7',
  'gt-7-days': 'charts.backlog.moreThan7',
}

const COMPLETENESS_CODE_KEYS: Record<string, string> = {
  'missing-writing-date': 'charts.completeness.noWritingDate',
  'missing-factory': 'charts.completeness.noFactory',
  'missing-production-line': 'charts.completeness.noLine',
}

type ChartGroupKey = Exclude<LegacySectionKey, 'recent'>

interface LegacyChartSectionsProps {
  snapshot: DashboardSnapshot
  group: ChartGroupKey
  compact?: boolean
}

function ChartCard({ title, children, footer }: { title: string; children: React.ReactNode; footer?: React.ReactNode }) {
  return (
    <div className="chart-card app-surface rounded-xl border app-border p-4 shadow-sm">
      <h2 className="mb-3 text-lg font-bold app-text">{title}</h2>
      <div className="chart-canvas h-[300px]">{children}</div>
      {footer && <div className="mt-2 text-xs app-text-muted">{footer}</div>}
    </div>
  )
}

function EmptyChart() {
  return <div className="flex h-full items-center justify-center text-sm app-text-muted">{t('app.noData')}</div>
}

function noSampleChart() {
  return <div className="flex h-full items-center justify-center text-sm app-text-muted">{t('charts.sla.noSample')}</div>
}

function CompactChartCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="chart-card app-surface rounded-xl border app-border p-4 shadow-sm">
      <h2 className="mb-3 text-lg font-bold app-text">{title}</h2>
      <div className="chart-canvas compact-chart-canvas">{children}</div>
    </div>
  )
}

function CompactBars({ title, items }: { title: string; items: Array<{ id: string; label: string; value: number }> }) {
  const max = Math.max(...items.map((item) => item.value), 0)
  return (
    <CompactChartCard title={title}>
      <div className="live-bar-list" data-chart-kind="horizontal-bars">
        {items.length === 0 ? <p className="live-chart-empty">{t('app.noData')}</p> : items.map((item) => (
          <div className="live-bar-row" key={item.id}>
            <div className="live-bar-caption"><span title={item.label}>{item.label}</span><strong dir="ltr">{formatNumber(item.value)}</strong></div>
            <div className="live-bar-track"><span className="live-bar-fill" style={{ width: `${max > 0 ? (item.value / max) * 100 : 0}%` }} /></div>
          </div>
        ))}
      </div>
    </CompactChartCard>
  )
}

function CompactColumns({
  title,
  items,
  primaryLabel,
  secondaryLabel,
  kind = 'grouped-columns',
}: {
  title: string
  items: Array<{ id: string; label: string; primary: number; secondary: number }>
  primaryLabel: string
  secondaryLabel: string
  kind?: string
}) {
  const max = Math.max(...items.flatMap((item) => [item.primary, item.secondary]), 0)
  return (
    <CompactChartCard title={title}>
      <div className="live-columns" data-chart-kind={kind}>
        {items.length === 0 ? <p className="live-chart-empty">{t('app.noData')}</p> : items.map((item) => (
          <div className="live-column-group" key={item.id} title={item.label}>
            <div className="live-column-pair">
              <span className="live-column live-column-primary" style={{ height: `${max > 0 && item.primary > 0 ? Math.max(3, item.primary / max * 100) : 0}%` }}><b dir="ltr">{formatNumber(item.primary)}</b></span>
              <span className="live-column live-column-secondary" style={{ height: `${max > 0 && item.secondary > 0 ? Math.max(3, item.secondary / max * 100) : 0}%` }}><b dir="ltr">{formatNumber(item.secondary)}</b></span>
            </div>
            <small>{item.label}</small>
          </div>
        ))}
      </div>
      <div className="live-chart-legend"><span><i className="live-legend-dot live-column-primary" />{primaryLabel}</span><span><i className="live-legend-dot live-column-secondary" />{secondaryLabel}</span></div>
    </CompactChartCard>
  )
}

function CompactAgeColumns({ title, items }: { title: string; items: Array<{ code: string; label: string; value: number }> }) {
  const max = Math.max(...items.map((item) => item.value), 0)
  return (
    <CompactChartCard title={title}>
      <div className="live-columns" data-chart-kind="age-columns">
        {items.length === 0 ? <p className="live-chart-empty">{t('app.noData')}</p> : items.map((item) => (
          <div className="live-column-group" key={item.code} title={item.label}>
            <div className="live-column-pair"><span className="live-column live-column-age" style={{ height: `${max > 0 && item.value > 0 ? Math.max(3, item.value / max * 100) : 0}%` }}><b dir="ltr">{formatNumber(item.value)}</b></span></div>
            <small>{item.label}</small>
          </div>
        ))}
      </div>
    </CompactChartCard>
  )
}

function CompactDonut({ title, items, unit }: { title: string; items: StatusDistributionItem[]; unit: string }) {
  const colors = ['var(--primary)', 'var(--brand-gold)', 'var(--danger)', 'var(--success)', 'var(--acc-violet)', 'var(--acc-slate)']
  const total = items.reduce((sum, item) => sum + item.count, 0)
  let cursor = 0
  const stops = items.filter((item) => item.count > 0).map((item, index) => {
    const start = total > 0 ? cursor / total * 360 : 0
    cursor += item.count
    const end = total > 0 ? cursor / total * 360 : 0
    return `${colors[index % colors.length]} ${start}deg ${end}deg`
  })
  return (
    <CompactChartCard title={title}>
      <div className="live-donut-chart" data-chart-kind="donut">
        {total <= 0 ? <p className="live-chart-empty">{t('app.noData')}</p> : <div className="live-donut-layout">
          <div className="live-donut" style={{ background: `conic-gradient(${stops.join(', ')})` }}><span>{formatNumber(total)}<small>{unit}</small></span></div>
          <div className="live-donut-legend">
            {items.map((item, index) => <div className="live-donut-row" key={item.status}><span><i style={{ background: colors[index % colors.length] }} />{item.statusLabel}</span><strong dir="ltr">{formatNumber(item.count)} <small>{formatPercent(total > 0 ? item.count / total * 100 : null)}</small></strong></div>)}
          </div>
        </div>}
      </div>
    </CompactChartCard>
  )
}

function CompactWorkflow({ title, items }: { title: string; items: StatusDistributionItem[] }) {
  const order: Record<string, number> = { DRAFT: 10, NEEDS_REVIEW: 20, UNDER_REVIEW: 30, HAS_ERRORS: 40, RETURNED_FOR_CORRECTION: 50, COMPLETE: 60, APPROVED: 70 }
  return (
    <CompactChartCard title={title}>
      <div className="live-workflow" data-chart-kind="workflow-stages">
        {[...items].sort((a, b) => (order[a.status] ?? 999) - (order[b.status] ?? 999)).map((item) => <div className="live-workflow-step" key={item.status}><span>{item.statusLabel}</span><strong dir="ltr">{formatNumber(item.count)}</strong></div>)}
      </div>
    </CompactChartCard>
  )
}

function CompactStackedBars({ title, items }: { title: string; items: PerformanceItem[] }) {
  return (
    <CompactChartCard title={title}>
      <div className="live-stacked-chart" data-chart-kind="stacked-bars">
        {items.length === 0 ? <p className="live-chart-empty">{t('app.noData')}</p> : <div className="live-stack-list">{items.map((item) => {
          const total = item.onTimeCount + item.lateCount
          return <div className="live-stack-row" key={item.dimensionId}><div className="live-stack-caption"><span>{item.label}</span><strong dir="ltr">{formatNumber(total)}</strong></div><div className="live-stack-track"><span className="live-stack-on-time" style={{ width: `${total > 0 ? item.onTimeCount / total * 100 : 0}%` }} /><span className="live-stack-late" style={{ width: `${total > 0 ? item.lateCount / total * 100 : 0}%` }} /></div></div>
        })}</div>}
      </div>
    </CompactChartCard>
  )
}

function CompactOperationalSummary({ snapshot }: { snapshot: DashboardSnapshot }) {
  const operations = snapshot.views.operationsSummary
  const approved = operations.workflowStatus.find((item) => item.status === 'APPROVED')
  const metrics = operations.metrics
  return (
    <div className="live-legacy-summary-strip" aria-label={t('operations.summaryLabel')}>
      <div><span>{t('operations.currentStatus')}: {approved?.statusLabel ?? '—'}</span><strong dir="ltr">{formatNumber(approved?.count)}</strong></div>
      <div><span>{t('operations.approvedThisMonth')}</span><strong dir="ltr">{formatNumber(operations.summary.approved)}</strong></div>
      <div><span>{t('operations.affectedRate')}</span><strong dir="ltr">{formatPercent(metrics?.affectedNoteRatePercentage)}</strong></div>
      <div><span>{t('operations.incidentsPer100')}</span><strong dir="ltr">{formatNumber(metrics?.incidentsPer100Notes)}</strong></div>
    </div>
  )
}
function CompactLegacyChartSections({ group, snapshot }: { group: Exclude<LegacySectionKey, 'recent'>; snapshot: DashboardSnapshot }) {
  const compactMonthItems = snapshot.monthlyActivity.map((item) => ({ id: item.month, label: item.monthLabel, primary: item.count, secondary: 0 }))
  const incomingItems = snapshot.incomingVsApproved.map((item) => ({ id: item.periodKey, label: item.periodLabel, primary: item.incoming, secondary: item.approved }))
  const duplicateItems = snapshot.duplicateBreakdown.map((item) => ({ status: item.classification, statusLabel: item.label, count: item.count }))
  const duplicateTrendItems = snapshot.duplicateTrend.map((item) => ({ id: item.periodKey, label: item.periodLabel, primary: item.companyResend, secondary: item.duplicateNumber }))
  const indicatorItems = snapshot.indicatorStateDistribution.map((item) => ({ status: String(item.code), statusLabel: item.stateLabel, count: item.value }))
  const qualityItems = snapshot.qualityByCompany.slice(0, 6).map((item) => ({ id: item.companyId, label: item.companyName, value: item.errorCount ?? 0 }))
  const errorItems = snapshot.topErrorFields.slice(0, 6).map((item) => ({ id: item.fieldId, label: item.fieldName, value: item.errorCount }))
  const timelinessItems = snapshot.views.executiveSummary.timelinessByCompany.slice(0, 5)
  const factoryItems = snapshot.performanceByFactory.slice(0, 6)
  const lineItems = snapshot.performanceByLine.slice(0, 6)

  const charts: Record<Exclude<LegacySectionKey, 'recent'>, React.ReactNode> = {
    summary: <div className="charts-grid grid grid-cols-1 gap-3"><CompactColumns title={t('charts.monthlyActivity')} items={compactMonthItems} primaryLabel={t('charts.notesCount')} secondaryLabel="" kind="time-columns" /><CompactDonut title={t('charts.statusDistribution')} items={snapshot.statusDistribution} unit={t('charts.notesCount')} /><CompactStackedBars title={t('charts.timelinessByCompany')} items={timelinessItems} /></div>,
    delays: <div className="charts-grid grid grid-cols-1 gap-3"><CompactDonut title={t('charts.sla')} items={[{ status: 'ON_TIME', statusLabel: t('charts.sla.onTime'), count: snapshot.sla.onTimeNotes }, { status: 'LATE', statusLabel: t('charts.sla.late'), count: snapshot.sla.lateNotes }, { status: 'INVALID', statusLabel: t('charts.sla.invalidOrder'), count: snapshot.sla.invalidDateOrderNotes }]} unit={t('charts.notesCount')} /><CompactAgeColumns title={t('charts.backlog')} items={snapshot.backlogByAge} /></div>,
    repetition: <div className="charts-grid grid grid-cols-1 gap-3"><CompactDonut title={t('charts.duplicateBreakdown')} items={duplicateItems} unit={t('charts.casesCount')} /><CompactColumns title={t('charts.duplicateTrend')} items={duplicateTrendItems} primaryLabel={t('charts.duplicateTrend.resend')} secondaryLabel={t('charts.duplicateTrend.duplicateNumber')} /><CompactBars title={t('charts.qualityByCompany')} items={qualityItems} /><CompactBars title={t('charts.topErrorFields')} items={errorItems} /><CompactDonut title={t('charts.indicatorStates')} items={indicatorItems} unit={t('charts.casesCount')} /></div>,
    workflow: <><CompactOperationalSummary snapshot={snapshot} /><div className="charts-grid grid grid-cols-1 gap-3"><CompactWorkflow title={t('charts.statusDistribution')} items={snapshot.views.operationsSummary.workflowStatus} /><CompactColumns title={t('charts.incomingVsApproved')} items={incomingItems} primaryLabel={t('charts.incoming')} secondaryLabel={t('charts.approved')} /><CompactAgeColumns title={t('charts.backlog')} items={snapshot.backlogByAge} /><CompactDonut title={t('charts.duplicateBreakdown')} items={duplicateItems} unit={t('charts.casesCount')} /><CompactDonut title={t('charts.indicatorStates')} items={indicatorItems} unit={t('charts.casesCount')} /><CompactBars title={t('charts.completeness')} items={snapshot.dataCompleteness.map((item) => ({ id: item.code, label: item.label, value: item.value }))} /><CompactStackedBars title={t('charts.performance.factory')} items={factoryItems} /><CompactStackedBars title={t('charts.performance.line')} items={lineItems} /><CompactBars title={t('charts.topErrorFields')} items={errorItems} /></div></>,
  }

  return <section className="dashboard-charts space-y-3"><div className="charts-grid grid grid-cols-1 gap-3">{charts[group]}</div></section>
}
export function LegacyChartSections({ group, snapshot, compact = false }: LegacyChartSectionsProps) {
  const colors = useChartTheme()
  const palette = colors.palette.length > 0 ? colors.palette : CHART_PALETTE
  const listColor = (index: number) => palette[index % palette.length]
  const minSample = snapshot.minSample
  // 1.2.2: توحيد الخط — Simplified Arabic في كل شارتات الواجهة الرئيسية أيضًا.
  const fontFamily = "'Simplified Arabic','Segoe UI',Tahoma,Arial,sans-serif"

  const baseScales = useMemo(
    () => ({
      x: {
        ticks: { color: colors.textMuted, maxRotation: 30, autoSkip: true },
        grid: { color: colors.grid },
      },
      y: {
        beginAtZero: true,
        ticks: { color: colors.textMuted, precision: 0, callback: (value: number | string) => formatChartNumber(value) },
        grid: { color: colors.grid },
      },
    }),
    [colors],
  )

  const barOptions = useMemo<ChartOptions<'bar'>>(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { display: false } },
      scales: baseScales,
    }),
    [baseScales],
  )

  const barLegendOptions = useMemo<ChartOptions<'bar'>>(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { position: 'bottom', labels: { color: colors.text, font: { family: fontFamily } } } },
      scales: baseScales,
    }),
    [baseScales, colors.text],
  )

  const lineOptions = useMemo<ChartOptions<'line'>>(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { labels: { color: colors.text, font: { family: fontFamily } } } },
      scales: baseScales,
    }),
    [baseScales, colors.text],
  )

  const doughnutOptions = useMemo<ChartOptions<'doughnut'>>(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'bottom', labels: { color: colors.text, font: { family: fontFamily } } },
      },
    }),
    [colors.text],
  )

  const performanceOptions = useMemo(
    () => (items: PerformanceItem[]): ChartOptions<'bar'> => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'bottom', labels: { color: colors.text, font: { family: fontFamily } } },
        tooltip: {
          callbacks: {
            label: (ctx: TooltipItem<'bar'>): string => {
              const item = items[ctx.dataIndex]
              if (!item) return ''
              const parts = [`${ctx.dataset.label ?? ''}: ${formatNumber(ctx.parsed.y)}`]
              if (item.lateRate !== null) parts.push(`${t('badges.lateRate')}: ${formatPercent(item.lateRate)}`)
              if (item.accuracyRate !== null) parts.push(`${t('charts.performance.accuracyRate')}: ${formatPercent(item.accuracyRate)}`)
              else parts.push(t('charts.performance.accuracyHidden'))
              return parts.join('\n')
            },
          },
        },
      },
      scales: {
        x: {
          stacked: true,
          ticks: { color: colors.textMuted, maxRotation: 30, autoSkip: true },
          grid: { color: colors.grid },
        },
        y: {
          beginAtZero: true,
          stacked: true,
          ticks: { color: colors.textMuted, precision: 0, callback: (value: number | string) => formatChartNumber(value) },
          grid: { color: colors.grid },
        },
      },
    }),
    [colors, fontFamily],
  )

  const hasMonthlyData = snapshot.monthlyActivity.some((item) => item.count > 0)
  const hasStatusData = snapshot.statusDistribution.some((item) => item.count > 0)
  const hasDuplicateData = snapshot.duplicateBreakdown.some((item) => item.count > 0)
  const hasIncomingData = snapshot.incomingVsApproved.some((item) => item.incoming > 0 || item.approved > 0)
  const hasFactoryData = snapshot.performanceByFactory.some((item) => item.totalNotes > 0)
  const hasLineData = snapshot.performanceByLine.some((item) => item.totalNotes > 0)
  const hasQualityData = snapshot.qualityByCompany.some((item) => item.sampleSize >= minSample)
  const hasFieldData = snapshot.topErrorFields.some((item) => item.errorCount > 0)
  const hasStatesData = snapshot.indicatorStateDistribution.some((item) => item.value > 0)
  const hasBacklogData = snapshot.backlogByAge.some((item) => item.value > 0)
  const hasCompletenessData = snapshot.dataCompleteness.some((item) => item.value > 0)
  const hasTrendData = snapshot.duplicateTrend.some((item) => item.companyResend > 0 || item.duplicateNumber > 0)
  const hasSlaData = hasEnoughSample(snapshot.sla.eligibleNotes, minSample)

  if (compact) return <CompactLegacyChartSections group={group} snapshot={snapshot} />

  const monthlyData: ChartData<'line'> = {
    labels: snapshot.monthlyActivity.map((item) => formatMonthYear(item.month)),
    datasets: [
      {
        label: t('charts.notesCount'),
        data: snapshot.monthlyActivity.map((item) => item.count),
        borderColor: colors.primary,
        backgroundColor: colors.grid,
        fill: true,
        tension: 0.35,
        pointRadius: 4,
        pointBackgroundColor: colors.primary,
      },
    ],
  }

  const statusData: ChartData<'doughnut'> = {
    labels: snapshot.statusDistribution.map((item) => translateStatus(item.status)),
    datasets: [
      {
        data: snapshot.statusDistribution.map((item) => item.count),
        backgroundColor: snapshot.statusDistribution.map((_, index) => listColor(index)),
        borderColor: colors.surface,
        borderWidth: 2,
      },
    ],
  }

  const duplicateData: ChartData<'bar'> = {
    labels: snapshot.duplicateBreakdown.map((item) => item.label),
    datasets: [
      {
        label: t('charts.casesCount'),
        data: snapshot.duplicateBreakdown.map((item) => item.count),
        backgroundColor: [colors.danger, colors.warning],
        borderRadius: 6,
      },
    ],
  }

  const incomingData: ChartData<'line'> = {
    labels: snapshot.incomingVsApproved.map((item) => formatMonthYear(item.label)),
    datasets: [
      {
        label: t('charts.incoming'),
        data: snapshot.incomingVsApproved.map((item) => item.incoming),
        borderColor: colors.primary,
        backgroundColor: withAlpha(colors.primary, 0.15),
        fill: false,
        tension: 0.3,
        pointRadius: 2,
      },
      {
        label: t('charts.approved'),
        data: snapshot.incomingVsApproved.map((item) => item.approved),
        borderColor: colors.success,
        backgroundColor: withAlpha(colors.success, 0.15),
        fill: false,
        tension: 0.3,
        pointRadius: 2,
      },
    ],
  }

  const buildPerformanceData = (items: PerformanceItem[]): ChartData<'bar'> => ({
    labels: items.map((item) => item.label),
    datasets: [
      {
        label: t('charts.sla.onTime'),
        data: items.map((item) =>
          item.onTimeCount,
        ),
        backgroundColor: colors.success,
        borderRadius: 4,
      },
      {
        label: t('charts.sla.late'),
        data: items.map((item) => item.lateCount),
        backgroundColor: colors.danger,
        borderRadius: 4,
      },
    ],
  })

  const qualityData: ChartData<'bar'> = {
    labels: snapshot.qualityByCompany.map((item) => item.label),
    datasets: [
      {
        label: t('charts.qualityByCompany.accuracy'),
        data: snapshot.qualityByCompany.map((item) => (item.sampleSize >= minSample ? item.accuracyRate : null)),
        backgroundColor: colors.success,
        borderRadius: 4,
      },
      {
        label: t('charts.qualityByCompany.error'),
        data: snapshot.qualityByCompany.map((item) => (item.sampleSize >= minSample ? item.errorRate : null)),
        backgroundColor: colors.danger,
        borderRadius: 4,
      },
    ],
  }

  const topFieldsData: ChartData<'bar'> = {
    labels: snapshot.topErrorFields.map((item) => item.label),
    datasets: [
      {
        label: t('charts.topErrorFields.errorCount'),
        data: snapshot.topErrorFields.map((item) => item.errorCount),
        backgroundColor: snapshot.topErrorFields.map((item) =>
          item.errorRate !== null && item.errorRate >= 50 ? colors.danger : colors.warning,
        ),
        borderRadius: 4,
      },
    ],
  }

  const statesData: ChartData<'doughnut'> = {
    labels: snapshot.indicatorStateDistribution.map((item) => indicatorStateLabel(item.code)),
    datasets: [
      {
        data: snapshot.indicatorStateDistribution.map((item) => item.value),
        backgroundColor: snapshot.indicatorStateDistribution.map((_, index) => listColor(index)),
        borderColor: colors.surface,
        borderWidth: 2,
      },
    ],
  }

  const backlogData: ChartData<'bar'> = {
    labels: snapshot.backlogByAge.map((item) => t(BACKLOG_CODE_KEYS[item.code] ?? item.label)),
    datasets: [
      {
        label: t('charts.notesCount'),
        data: snapshot.backlogByAge.map((item) => item.value),
        backgroundColor: colors.warning,
        borderRadius: 6,
      },
    ],
  }

  const completenessData: ChartData<'bar'> = {
    labels: snapshot.dataCompleteness.map((item) => t(COMPLETENESS_CODE_KEYS[item.code] ?? item.label)),
    datasets: [
      {
        label: t('charts.notesCount'),
        data: snapshot.dataCompleteness.map((item) => item.value),
        backgroundColor: colors.warning,
        borderRadius: 6,
      },
    ],
  }

  const slaData: ChartData<'doughnut'> = {
    labels: [t('charts.sla.onTime'), t('charts.sla.late'), t('charts.sla.invalidOrder')],
    datasets: [
      {
        data: [snapshot.sla.onTimeNotes, snapshot.sla.lateNotes, snapshot.sla.invalidDateOrderNotes],
        backgroundColor: [colors.success, colors.danger, colors.warning],
        borderColor: colors.surface,
        borderWidth: 2,
      },
    ],
  }

  const trendData: ChartData<'line'> = {
    labels: snapshot.duplicateTrend.map((item) => formatMonthYear(item.label)),
    datasets: [
      {
        label: t('charts.duplicateTrend.resend'),
        data: snapshot.duplicateTrend.map((item) => item.companyResend),
        borderColor: colors.danger,
        backgroundColor: withAlpha(colors.danger, 0.15),
        fill: false,
        tension: 0.3,
        pointRadius: 3,
      },
      {
        label: t('charts.duplicateTrend.duplicateNumber'),
        data: snapshot.duplicateTrend.map((item) => item.duplicateNumber),
        borderColor: colors.warning,
        backgroundColor: withAlpha(colors.warning, 0.15),
        fill: false,
        tension: 0.3,
        pointRadius: 3,
      },
    ],
  }

  const sectionCharts: Record<ChartGroupKey, React.ReactNode> = {
    summary: (
      <>
        <ChartCard title={t('charts.monthlyActivity')}>
          {hasMonthlyData ? <Line data={monthlyData} options={lineOptions} /> : <EmptyChart />}
        </ChartCard>
      </>
    ),
    delays: (
      <>
        <ChartCard
          title={t('charts.sla')}
          footer={hasSlaData ? `${t('charts.sla.eligible')}: ${formatNumber(snapshot.sla.eligibleNotes)}` : undefined}
        >
          {hasSlaData ? <Doughnut data={slaData} options={doughnutOptions} /> : noSampleChart()}
        </ChartCard>
        <ChartCard title={t('charts.backlog')}>
          {hasBacklogData ? <Bar data={backlogData} options={barOptions} /> : <EmptyChart />}
        </ChartCard>
      </>
    ),
    repetition: (
      <>
        <ChartCard title={t('charts.duplicateBreakdown')}>
          {hasDuplicateData ? <Bar data={duplicateData} options={barOptions} /> : <EmptyChart />}
        </ChartCard>
        <ChartCard title={t('charts.duplicateTrend')}>
          {hasTrendData ? <Line data={trendData} options={lineOptions} /> : <EmptyChart />}
        </ChartCard>
        <ChartCard
          title={t('charts.qualityByCompany')}
          footer={t('charts.qualityByCompany.noSample', { count: minSample })}
        >
          {hasQualityData ? <Bar data={qualityData} options={barLegendOptions} /> : <EmptyChart />}
        </ChartCard>
        <ChartCard
          title={t('charts.topErrorFields')}
          footer={`${t('charts.topErrorFields.sample')} ≥ ${minSample} — ${t('charts.topErrorFields.filtersApplied')}`}
        >
          {hasFieldData ? <Bar data={topFieldsData} options={barOptions} /> : <EmptyChart />}
        </ChartCard>
        <ChartCard title={t('charts.indicatorStates')}>
          {hasStatesData ? <Doughnut data={statesData} options={doughnutOptions} /> : <EmptyChart />}
        </ChartCard>
      </>
    ),
    workflow: (
      <>
        <ChartCard title={t('charts.statusDistribution')}>
          {hasStatusData ? <Doughnut data={statusData} options={doughnutOptions} /> : <EmptyChart />}
        </ChartCard>
        <ChartCard title={t('charts.incomingVsApproved')}>
          {hasIncomingData ? <Line data={incomingData} options={lineOptions} /> : <EmptyChart />}
        </ChartCard>
        <ChartCard title={t('charts.performance.factory')}>
          {hasFactoryData ? (
            <Bar data={buildPerformanceData(snapshot.performanceByFactory)} options={performanceOptions(snapshot.performanceByFactory)} />
          ) : (
            <EmptyChart />
          )}
        </ChartCard>
        <ChartCard title={t('charts.performance.line')}>
          {hasLineData ? (
            <Bar data={buildPerformanceData(snapshot.performanceByLine)} options={performanceOptions(snapshot.performanceByLine)} />
          ) : (
            <EmptyChart />
          )}
        </ChartCard>
        <ChartCard title={t('charts.completeness')} footer={t('charts.completeness.hint')}>
          {hasCompletenessData ? <Bar data={completenessData} options={barOptions} /> : <EmptyChart />}
        </ChartCard>
      </>
    ),
  }

  return (
    <section className={compact ? 'dashboard-charts space-y-3' : 'space-y-4'}>
      <div className={compact ? 'charts-grid grid' : 'charts-grid grid grid-cols-1 gap-4 xl:grid-cols-2'}>
        {sectionCharts[group]}
      </div>
    </section>
  )
}

import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, type SetupConfig } from '../services/api'
import { FilterBar, loadFiltersFromUrlOrSaved, persistFilters, sanitizeFiltersAgainstLookups } from '../components/FilterBar'
import { Header } from '../components/Header'
import { DashboardSnapshotError } from '../components/DashboardSnapshotError'
import { OfflineBanner } from '../components/OfflineBanner'
import { SkeletonLoader } from '../components/SkeletonLoader'
import { useDashboardData } from '../hooks/useDashboardData'
import { formatDate, formatNumber, formatPercent } from '../services/format'
import { LegacyDashboardContent } from './legacy/LegacyDashboard'
import { DEFAULT_SECTION, LegacySectionNav, LegacySectionPanel, type LegacySectionKey } from './legacy/LegacySectionNav'
import type {
  CompanyMetrics,
  DashboardFilters,
  DashboardLookups,
  DashboardSnapshot,
  PerformanceItem,
  BacklogAgeItem,
  StatusDistributionItem,
} from '../types/dashboard'

export type LiveDashboardView = 'executive' | 'operations' | 'company' | 'directory'

const viewLabels: Record<LiveDashboardView, string> = {
  executive: 'المدير العام',
  operations: 'النظام والعمليات',
  company: 'تقرير الشركة',
  directory: 'اختيار شركة',
}

function readView(): LiveDashboardView {
  const params = new URLSearchParams(window.location.search)
  const value = params.get('view')
  if (value === 'operations' || value === 'company' || value === 'directory') return value
  if (params.get('companyId')) return 'company'
  return 'executive'
}

function updateRoute(view: LiveDashboardView, companyId?: string): void {
  const params = new URLSearchParams(window.location.search)
  params.delete('dashboardPreview')
  params.set('view', view)
  if (view === 'company' && companyId) params.set('companyId', companyId)
  else if (view !== 'company') params.delete('companyId')
  const query = params.toString()
  window.history.pushState({}, '', window.location.pathname + (query ? '?' + query : '') + window.location.hash)
  window.dispatchEvent(new PopStateEvent('popstate'))
}

function isAvailable(snapshot: DashboardSnapshot | null): boolean {
  return Boolean(snapshot?.views?.executiveSummary?.availability?.hasRows)
}

function displayCount(snapshot: DashboardSnapshot | null, value: number | null | undefined): string {
  return isAvailable(snapshot) ? formatNumber(value) : '—'
}

function displayPercent(snapshot: DashboardSnapshot | null, value: number | null | undefined): string {
  return isAvailable(snapshot) ? formatPercent(value) : '—'
}

function displayDeltaPoints(delta: number | null | undefined): string {
  if (delta === null || delta === undefined) return 'غير متاح'
  if (delta === 0) return '0 نقطة مئوية'
  return (delta > 0 ? '+' : '') + formatNumber(delta) + ' نقطة مئوية'
}

function comparisonText(hasPreviousPeriod: boolean, delta: number | null | undefined): string {
  return hasPreviousPeriod ? displayDeltaPoints(delta) : 'حدد من وإلى لتفعيل مقارنة الفترة السابقة'
}

function scopeLabel(snapshot: DashboardSnapshot): string {
  const filters = snapshot.effectiveFilters
  const basis = filters?.dateBasis === 'ArrivalDate' ? 'حسب تاريخ الوصول' : 'حسب تاريخ الكتابة'
  if (!filters?.to) return filters?.from ? 'النطاق: من ' + formatDate(filters.from) + ' · ' + basis : 'النطاق: كل المذكرات · ' + basis
  return filters.from
    ? `النطاق: ${formatDate(filters.from)} — ${formatDate(filters.to)} · ${basis}`
    : `النطاق: حتى ${formatDate(filters.to)} · ${basis}`
}

function Section({ title, description, children, className = '' }: { title: string; description?: string; children: ReactNode; className?: string }) {
  return (
    <section className={'live-section ' + className} aria-labelledby={title.replace(/\s+/g, '-')}>
      <div className="live-section-heading">
        <div>
          <p className="live-eyebrow">مؤشرات موثقة</p>
          <h2 id={title.replace(/\s+/g, '-')}>{title}</h2>
          {description && <p className="live-section-description">{description}</p>}
        </div>
        <span className="live-section-mark" aria-hidden="true">✦</span>
      </div>
      {children}
    </section>
  )
}

function MetricCard({ label, value, detail, tone }: { label: string; value: string; detail: string; tone: 'blue' | 'teal' | 'gold' | 'red' | 'violet' | 'slate' }) {
  return (
    <article className={'live-metric live-metric-' + tone}>
      <p className="live-metric-label">{label}</p>
      <strong className="live-metric-value" dir="ltr">{value}</strong>
      <p className="live-metric-detail">{detail}</p>
    </article>
  )
}

function AvailabilityNotice({ hasRows, explanation }: { hasRows: boolean; explanation?: string | null }) {
  if (hasRows) return null
  return (
    <div className="live-no-data" role="status">
      <strong>لا توجد بيانات ضمن النطاق المحدد</strong>
      <span>{explanation ?? 'غيّر الفترة أو الشركة لإعادة القراءة. لا تُعرض الحالة كصفر افتراضي.'}</span>
    </div>
  )
}

type BarItem = { id: string; label: string; value: number | null; detail?: string; tone?: string }

function HorizontalBars({ title, items, valueLabel = 'قيمة', empty = 'لا توجد قيم قابلة للعرض.' }: { title: string; items: BarItem[]; valueLabel?: string; empty?: string }) {
  const numericValues = items.map((item) => item.value).filter((value): value is number => typeof value === 'number' && Number.isFinite(value))
  const max = Math.max(...numericValues, 0)
  return (
    <div className="live-chart" role="img" aria-label={title}>
      {items.length === 0 ? <p className="live-chart-empty">{empty}</p> : (
        <div className="live-bar-list">
          {items.map((item) => {
            const value = item.value
            const width = max > 0 && value !== null && value !== undefined ? Math.min(100, Math.max(0, (value / max) * 100)) : 0
            return (
              <div className="live-bar-row" key={item.id}>
                <div className="live-bar-caption">
                  <span title={item.label}>{item.label}</span>
                  <strong dir="ltr">{value === null || value === undefined ? '—' : formatNumber(value)}</strong>
                </div>
                <div className="live-bar-track" aria-label={valueLabel + ': ' + (value === null || value === undefined ? 'لا تتوفر بيانات' : formatNumber(value))}>
                  <span className={'live-bar-fill ' + (item.tone ?? '')} style={{ width: width + '%' }} />
                </div>
                {item.detail && <small>{item.detail}</small>}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

function DonutChart({ title, items, unit, empty = 'لا توجد بيانات قابلة للتصنيف.' }: { title: string; items: StatusDistributionItem[]; unit: string; empty?: string }) {
  const total = items.reduce((sum, item) => sum + item.count, 0)
  const colors = ['var(--primary)', 'var(--brand-gold)', 'var(--danger)', 'var(--success)', 'var(--acc-violet)', 'var(--acc-slate)']
  let cursor = 0
  const stops = items
    .map((item, originalIndex) => ({ item, originalIndex }))
    .filter(({ item }) => item.count > 0)
    .map(({ item, originalIndex }) => {
      const start = total > 0 ? (cursor / total) * 360 : 0
      cursor += item.count
      const end = total > 0 ? (cursor / total) * 360 : 0
      return colors[originalIndex % colors.length] + ' ' + start + 'deg ' + end + 'deg'
    })
  return (
    <div className="live-donut-chart" role="img" aria-label={title} data-chart-kind="donut">
      {total <= 0 ? <p className="live-chart-empty">{empty}</p> : (
        <div className="live-donut-layout">
          <div className="live-donut" style={{ background: 'conic-gradient(' + stops.join(', ') + ')' }} aria-hidden="true"><span>{formatNumber(total)}<small>{unit}</small></span></div>
          <div className="live-donut-legend">
            {items.map((item, itemIndex) => (
              <div className="live-donut-row" key={item.status}>
                <span><i style={{ background: colors[itemIndex % colors.length] }} />{item.statusLabel}</span>
                <strong dir="ltr">{formatNumber(item.count)} <small>{formatPercent(total > 0 ? (item.count / total) * 100 : null)}</small></strong>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

function TimeTrend({ title, items, valueLabel }: { title: string; items: Array<{ id: string; label: string; value: number }>; valueLabel: string }) {
  const max = Math.max(...items.map((item) => item.value), 0)
  if (items.length <= 2) {
    return (
      <div className="live-columns-chart" role="img" aria-label={title} data-chart-kind={items.length === 1 ? 'time-single' : 'time-columns'}>
        {items.length === 0 ? <p className="live-chart-empty">لا توجد فترات قابلة للعرض.</p> : <>
          <div className="live-columns live-time-columns">
            {items.map((item) => <div className="live-column-group" key={item.id} title={item.label}>
              <div className="live-column-pair"><span className="live-column live-column-primary" style={{ height: max > 0 && item.value > 0 ? Math.max(3, item.value / max * 100) + '%' : '0%' }}><b dir="ltr">{formatNumber(item.value)}</b></span></div>
              <small>{item.label}</small>
            </div>)}
          </div>
          <div className="live-chart-legend"><span><i className="live-legend-dot live-column-primary" />{valueLabel}</span></div>
        </>}
      </div>
    )
  }

  const points = items.map((item, itemIndex) => {
    const x = (itemIndex / (items.length - 1)) * 100
    const y = max > 0 ? 92 - (item.value / max) * 76 : 92
    return { ...item, x, y }
  })
  const pointString = points.map((point) => point.x + ',' + point.y).join(' ')
  return (
    <div className="live-time-trend" role="img" aria-label={title} data-chart-kind="time-line">
      <svg viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
        <polyline className="live-time-trend-area" points={'0,92 ' + pointString + ' 100,92'} />
        <polyline className="live-time-trend-line" points={pointString} />
        {points.map((point) => <circle key={point.id} cx={point.x} cy={point.y} r="1.8"><title>{point.label}: {formatNumber(point.value)} {valueLabel}</title></circle>)}
      </svg>
      <div className="live-time-trend-labels">{items.map((item) => <span key={item.id}><b>{item.label}</b><strong dir="ltr">{formatNumber(item.value)}</strong></span>)}</div>
    </div>
  )
}

function GroupedColumns({ title, items, primaryLabel, secondaryLabel }: { title: string; items: Array<{ id: string; label: string; primary: number; secondary: number }>; primaryLabel: string; secondaryLabel: string }) {
  const max = Math.max(...items.flatMap((item) => [item.primary, item.secondary]), 0)
  return (
    <div className="live-columns-chart" role="img" aria-label={title} data-chart-kind="grouped-columns">
      {items.length === 0 ? <p className="live-chart-empty">لا توجد فترات قابلة للعرض.</p> : <>
        <div className="live-columns">
          {items.map((item) => <div className="live-column-group" key={item.id} title={item.label}>
            <div className="live-column-pair">
              <span className="live-column live-column-primary" style={{ height: max > 0 && item.primary > 0 ? Math.max(3, item.primary / max * 100) + '%' : '0%' }}><b dir="ltr">{formatNumber(item.primary)}</b></span>
              <span className="live-column live-column-secondary" style={{ height: max > 0 && item.secondary > 0 ? Math.max(3, item.secondary / max * 100) + '%' : '0%' }}><b dir="ltr">{formatNumber(item.secondary)}</b></span>
            </div>
            <small>{item.label}</small>
          </div>)}
        </div>
        <div className="live-chart-legend"><span><i className="live-legend-dot live-column-primary" />{primaryLabel}</span><span><i className="live-legend-dot live-column-secondary" />{secondaryLabel}</span></div>
      </>}
    </div>
  )
}

function StackedBars({ title, rows, primaryLabel, secondaryLabel }: { title: string; rows: PerformanceItem[]; primaryLabel: string; secondaryLabel: string }) {
  return (
    <div className="live-stacked-chart" role="img" aria-label={title} data-chart-kind="stacked-bars">
      {rows.length === 0 ? <p className="live-chart-empty">لا توجد وحدات قابلة للعرض.</p> : <div className="live-stack-list">
        {rows.map((row) => {
          const denominator = row.onTimeCount + row.lateCount
          const onTimeWidth = denominator > 0 ? row.onTimeCount / denominator * 100 : 0
          const lateWidth = denominator > 0 ? row.lateCount / denominator * 100 : 0
          return <div className="live-stack-row" key={row.dimensionId}>
            <div className="live-stack-caption"><span>{row.label}</span><strong dir="ltr">{denominator > 0 ? formatNumber(denominator) : '—'}</strong></div>
            {denominator > 0 ? <div className="live-stack-track" title={primaryLabel + ': ' + formatNumber(row.onTimeCount) + ' · ' + secondaryLabel + ': ' + formatNumber(row.lateCount)}><span className="live-stack-on-time" style={{ width: onTimeWidth + '%' }} /><span className="live-stack-late" style={{ width: lateWidth + '%' }} /></div> : <small className="live-stack-empty">لا توجد بيانات ضمن الفترة</small>}
          </div>
        })}
      </div>}
      <div className="live-chart-legend"><span><i className="live-legend-dot live-stack-on-time" />{primaryLabel}</span><span><i className="live-legend-dot live-stack-late" />{secondaryLabel}</span></div>
    </div>
  )
}

const workflowStatusOrder: Record<string, number> = {
  DRAFT: 10,
  NEEDS_REVIEW: 20,
  UNDER_REVIEW: 30,
  HAS_ERRORS: 40,
  RETURNED_FOR_CORRECTION: 50,
  COMPLETE: 60,
  APPROVED: 70,
}

function WorkflowSteps({ title, items }: { title: string; items: StatusDistributionItem[] }) {
  const orderedItems = [...items].sort((left, right) => (workflowStatusOrder[left.status] ?? 999) - (workflowStatusOrder[right.status] ?? 999) || left.statusLabel.localeCompare(right.statusLabel, 'ar'))
  return <div className="live-workflow" role="img" aria-label={title} data-chart-kind="workflow-stages">
    {orderedItems.length === 0 ? <p className="live-chart-empty">لا توجد حالات مسجلة.</p> : orderedItems.map((item) => <div className="live-workflow-step" key={item.status}><span>{item.statusLabel}</span><strong dir="ltr">{formatNumber(item.count)}</strong></div>)}
  </div>
}
function SecondaryMetricStrip({ metrics }: { metrics: CompanyMetrics | null }) {
  const items = metrics ? [
    ['مذكرات سليمة', formatNumber(metrics.cleanReviewedNotes), 'من ' + formatNumber(metrics.qualityEligibleNoteCount) + ' مذكرة مؤهلة'],
    ['نسبة السليم', formatPercent(metrics.cleanReviewedNotePercent), 'المذكرات السليمة من العينة'],
    ['أخطاء لكل 100 بند', formatNumber(metrics.errorsPer100Checked), 'من البنود المنطبقة والمراجعة'],
    ['نسبة المذكرات المتأثرة بالتكرار', formatPercent(metrics.affectedNoteRatePercentage), formatNumber(metrics.affectedNotes) + ' من ' + formatNumber(metrics.totalNotes) + ' مذكرة'],
    ['حوادث لكل 100 مذكرة', formatNumber(metrics.incidentsPer100Notes), formatNumber(metrics.duplicateIncidents) + ' حادثة من ' + formatNumber(metrics.totalNotes) + ' مذكرة'],
    ['كفاءة الربط', formatPercent(metrics.linkEfficiencyPercentage), 'المذكرات المرتبطة من مذكرات الإدخال'],
  ] : []
  return <div className="live-secondary-strip" aria-label="مؤشرات تفصيلية">{items.map(([label, value, detail]) => <div key={label}><span>{label}</span><strong dir="ltr">{value}</strong><small>{detail}</small></div>)}</div>
}

function AgeColumns({ title, items }: { title: string; items: BacklogAgeItem[] }) {
  const max = Math.max(...items.map((item) => item.value), 0)
  return <div className="live-columns-chart" role="img" aria-label={title} data-chart-kind="age-columns">
    {items.length === 0 ? <p className="live-chart-empty">لا توجد أعمار رصيد قابلة للعرض.</p> : <div className="live-columns live-age-columns">
      {items.map((item) => <div className="live-column-group" key={item.code} title={item.label}>
        <div className="live-column-pair"><span className="live-column live-column-age" style={{ height: max > 0 && item.value > 0 ? Math.max(3, item.value / max * 100) + '%' : '0%' }}><b dir="ltr">{formatNumber(item.value)}</b></span></div>
        <small>{item.label}</small>
      </div>)}
    </div>}
  </div>
}
function RecentNotes({ snapshot }: { snapshot: DashboardSnapshot }) {
  const rows = snapshot.views.operationsSummary.recentNotes
  return (
    <div className="live-table-wrap">
      <table className="live-table live-recent-table">
        <caption>أحدث المذكرات</caption>
        <thead><tr><th>رقم المذكرة</th><th>الشركة</th><th>المصنع</th><th>الحالة</th><th>الوصول</th></tr></thead>
        <tbody>
          {rows.length === 0 ? <tr><td colSpan={5} className="live-empty-cell">لا توجد مذكرات حديثة.</td></tr> : rows.map((row) => (
            <tr key={row.id}>
              <th scope="row" dir="ltr">{row.noteNumber}</th>
              <td>{row.companyName ?? '—'}</td>
              <td>{row.factoryName ?? '—'}</td>
              <td><span className={row.isLate ? 'live-status-badge live-status-late' : 'live-status-badge'}>{row.statusLabel}</span></td>
              <td>{formatDate(row.arrivalAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function ExecutiveView({ snapshot }: { snapshot: DashboardSnapshot }) {
  const section = snapshot.views.executiveSummary
  const summary = section.summary
  const metrics = section.metrics
  const hasRows = section.availability.hasRows
  const qualityDetail = metrics ? formatNumber(metrics.correctItems) + ' صحيح من ' + formatNumber(metrics.applicableReviewedItems) + ' بندًا منطبقًا' : 'لا تتوفر عينة'
  const companyBars = section.companyComparison.map((company) => ({
    id: company.id,
    label: company.name,
    value: company.noteCount,
    detail: company.noteCount === 0 ? 'لا توجد مذكرات ضمن الفترة' : 'مذكرات ضمن الفترة',
    tone: company.noteCount === 0 ? 'gold' : 'blue',
  }))
  const qualityBars = section.qualityByCompany.map((company) => ({
    id: company.companyId,
    label: company.companyName,
    value: company.errorCount ?? null,
    detail: company.errorCount === null || company.errorCount === undefined
      ? 'لا تتوفر بيانات جودة ضمن الفترة'
      : company.errorCount === 0 ? 'لا أخطاء مسجلة' : formatNumber(company.errorCount) + ' خطأ من ' + formatNumber(company.sampleSize) + ' عينة',
    tone: company.errorCount ? 'red' : 'teal',
  }))
  return (
    <>
      <AvailabilityNotice hasRows={hasRows} explanation={section.availability.explanation} />
      <div className="live-kpi-grid">
        <MetricCard label="إجمالي المذكرات" value={displayCount(snapshot, summary.totalNotes)} detail="ضمن الفترة المحددة" tone="blue" />
        <MetricCard label="الالتزام الزمني" value={displayPercent(snapshot, section.sla.onTimeRate)} detail={hasRows ? formatNumber(section.sla.onTimeNotes) + ' في الموعد من ' + formatNumber(section.sla.eligibleNotes) : 'لا توجد عينة مؤهلة'} tone="teal" />
        <MetricCard label="تغطية المراجعة" value={displayPercent(snapshot, metrics?.reviewCoveragePercentage)} detail={metrics ? formatNumber(metrics.reviewedItems) + ' من ' + formatNumber(metrics.expectedItems) + ' بندًا متوقعًا' : 'لا تتوفر عينة'} tone="gold" />
        <MetricCard label="جودة البنود" value={displayPercent(snapshot, metrics?.itemQualityPercentage)} detail={qualityDetail} tone="gold" />
        <MetricCard label="مذكرات تحتوي أخطاء" value={displayCount(snapshot, metrics?.errorNotes)} detail={metrics ? 'من ' + formatNumber(metrics.qualityEligibleNoteCount) + ' مذكرة مؤهلة للجودة' : 'لا تتوفر عينة'} tone="red" />
        <MetricCard label="المذكرات المتأخرة" value={displayCount(snapshot, summary.lateTotal)} detail={hasRows ? 'قيد المراجعة (مؤشر منفصل): ' + formatNumber(summary.pendingReview) : 'لا توجد عينة'} tone="violet" />
      </div>
      <SecondaryMetricStrip metrics={metrics} />
      <div className="live-grid live-grid-two">
        <Section title="اتجاه المذكرات" description="الأشهر والتجميع ضمن الفترة الحالية.">
          <TimeTrend title="اتجاه المذكرات" valueLabel="مذكرة" items={section.monthlyTrend.map((item) => ({ id: item.month, label: item.monthLabel, value: item.count }))} />
        </Section>
        <Section title="مقارنة الشركات" description="مقارنة مختصرة تشمل الشركات الثماني، دون تحويلها إلى بطاقات تنقل.">
          <HorizontalBars title="مقارنة الشركات" items={companyBars} valueLabel="عدد المذكرات" />
        </Section>
      </div>
      <div className="live-grid live-grid-three">
        <Section title="توزيع الحالات" description="توزيع الحالات المسجل في العقد الموحد."><DonutChart title="توزيع الحالات" unit="مذكرة" items={section.statusDistribution} /></Section>
        <Section title="جودة ومشكلات الشركات" description="عدد الأخطاء من العينة لكل شركة."><HorizontalBars title="مشكلات الشركات" items={qualityBars} valueLabel="عدد الأخطاء" /></Section>
        <Section title="إشارة الإدارة" description="قراءة مختصرة تساعد على تحديد الأولوية.">
          <div className="live-callouts">
            <div className="live-callout live-callout-warning"><strong>{summary.pendingReview > 0 ? 'مراجعة مطلوبة' : 'لا يوجد رصيد مراجعة'}</strong><span>{formatNumber(summary.pendingReview)} مذكرات قيد المراجعة.</span></div>
            <div className="live-callout live-callout-danger"><strong>{summary.lateTotal > 0 ? 'تأخير يحتاج متابعة' : 'لا يوجد تأخير مسجل'}</strong><span>{formatNumber(summary.lateTotal)} مذكرات متأخرة.</span></div>
          </div>
        </Section>
      </div>
      <div className="live-grid live-grid-two">
        <Section title="الالتزام الزمني حسب الشركة" description="عدد في الموعد مقابل المتأخر، مع إبقاء الشركات بلا بيانات صريحة.">
          <StackedBars title="الالتزام الزمني حسب الشركة" rows={section.timelinessByCompany} primaryLabel="في الموعد" secondaryLabel="متأخر" />
        </Section>
        <Section title="أعلى مصادر الأخطاء" description="البند والعدد كما ظهرا في البيانات، مع مقام العينة عند توفره.">
        <HorizontalBars title="أعلى مصادر الأخطاء" valueLabel="عدد الأخطاء" items={section.highImpactProblems.map((item) => ({ id: item.fieldId, label: item.fieldName, value: item.errorCount, detail: formatNumber(item.errorCount) + ' أخطاء من ' + formatNumber(item.sampleSize) + ' مؤشرات مقيمة' }))} />
      </Section>
      </div>
    </>
  )
}

function OperationsView({ snapshot }: { snapshot: DashboardSnapshot }) {
  const section = snapshot.views.operationsSummary
  const summary = section.summary
  const hasRows = section.availability.hasRows
  const metrics = section.metrics
  const duplicateItems = section.repetitionClassification.map((item) => ({ status: item.classification, statusLabel: item.label, count: item.count }))
  const trendItems = section.repetitionTrend.map((item) => ({ id: item.periodKey, label: item.periodLabel, primary: item.companyResend, secondary: item.duplicateNumber }))
  const completeness = section.dataCompleteness.map((item) => ({ id: item.code, label: item.label, value: item.value, tone: item.value > 0 ? 'red' : 'teal' }))
  const indicatorItems = section.indicatorStates.map((item) => ({ status: String(item.code), statusLabel: item.stateLabel, count: item.value }))
  return (
    <>
      <AvailabilityNotice hasRows={hasRows} explanation={section.availability.explanation} />
      <div className="live-kpi-grid">
        <MetricCard label="الوارد/المدخل" value={displayCount(snapshot, summary.totalNotes)} detail="عدد المذكرات الواصلة ضمن الفترة" tone="blue" />
        <MetricCard label="قيد المراجعة" value={displayCount(snapshot, summary.pendingReview)} detail="رصيد يحتاج معالجة" tone="violet" />
        <MetricCard label="مراجعة ناقصة" value={displayCount(snapshot, metrics?.incompleteReviewNotes)} detail={metrics ? 'مذكرات غير مكتملة المراجعة' : 'لا تتوفر عينة'} tone="gold" />
        <MetricCard label="الحالة الحالية: معتمدة" value={displayCount(snapshot, summary.approved)} detail="مذكرات حالتها الحالية معتمدة ضمن نطاق الوصول" tone="teal" />
        <MetricCard label="التأخير" value={displayCount(snapshot, summary.lateTotal)} detail={formatNumber(summary.lateSameMonth) + ' داخل الشهر و' + formatNumber(summary.lateCrossMonth) + ' عابرة للشهر'} tone="red" />
        <MetricCard label="حوادث التكرار" value={displayCount(snapshot, metrics?.duplicateIncidents)} detail={metrics ? formatNumber(metrics.companyResendIncidents) + ' إعادة إرسال و' + formatNumber(metrics.duplicateNumberIncidents) + ' تكرار رقم' : 'لا تتوفر عينة'} tone="gold" />
        <MetricCard label="المذكرات المتأثرة بالتكرار" value={displayCount(snapshot, summary.duplicateAffectedNotes)} detail="عدد مميز؛ لا تُخصم الحادثة أكثر من مرة" tone="violet" />
        <MetricCard label="الإجمالي بدون مكرر" value={displayCount(snapshot, summary.nonDuplicateNotes)} detail="الإجمالي ناقص المذكرات المتأثرة المميزة" tone="teal" />
      </div>
      <SecondaryMetricStrip metrics={metrics} />
      <div className="live-grid live-grid-two">
        <Section title="الوارد وأحداث الاعتماد" description="اعتمادات تمت خلال الشهر حسب approved_at؛ ليست عدد الحالات الحالية المعتمدة.">
          <GroupedColumns title="الوارد وأحداث الاعتماد" primaryLabel="مذكرات وصلت خلال الشهر" secondaryLabel="اعتمادات تمت خلال الشهر" items={section.incomingVsApproved.map((item) => ({ id: item.periodKey, label: item.periodLabel, primary: item.incoming, secondary: item.approved }))} />
        </Section>
        <Section title="توزيع حالات العمل" description="ترتيب دلالي للحالات؛ لا يمثل مسارًا خطيًا بسبب فروع الإعادة والأخطاء."><WorkflowSteps title="توزيع حالات العمل" items={section.workflowStatus} /></Section>
      </div>
      <div className="live-grid live-grid-three">
        <Section title="تصنيف التكرار" description="البطاقة والتصنيف والاتجاه تستخدم الحقول نفسها."><DonutChart title="تصنيف التكرار" unit="حادثة" items={duplicateItems} empty="لا توجد حوادث تكرار ضمن الفترة." /></Section>
        <Section title="اتجاه التكرار" description="إعادة إرسال وتكرار رقم المذكرة حسب الفترة."><GroupedColumns title="اتجاه التكرار" primaryLabel="إعادة إرسال" secondaryLabel="تكرار الرقم" items={trendItems.map((item) => ({ id: item.id, label: item.label, primary: item.primary, secondary: item.secondary ?? 0 }))} /></Section>
        <Section title="اكتمال البيانات" description="المؤشر يوضح النقص ولا يحوله إلى خطأ أو صفر."><HorizontalBars title="اكتمال البيانات" items={completeness} valueLabel="عدد الحالات" /></Section>
      </div>
      <Section title="أعمار رصيد الانتظار" description="التجميع الزمني ضمن الفترة، ولا يحول عدم وجود البيانات إلى صفر."><AgeColumns title="أعمار رصيد الانتظار" items={section.backlogByAge} /></Section>
      <Section title="حالات المؤشرات" description="التسمية العربية من قاموس الحالات، دون عرض رموز رقمية كعناوين."><DonutChart title="حالات المؤشرات" unit="حالة" items={indicatorItems} empty="لا توجد حالات مؤشرات ضمن الفترة." /></Section>
      <div className="live-grid live-grid-two">
        <Section title="أداء المصانع" description="أسماء المصانع من المعرفات الدلالية."><StackedBars title="أداء المصانع" rows={section.performanceByFactory} primaryLabel="في الموعد" secondaryLabel="متأخر" /></Section>
        <Section title="أداء خطوط الإنتاج" description="أسماء الخطوط الفعلية، لا فهارس المصفوفة."><StackedBars title="أداء خطوط الإنتاج" rows={section.performanceByLine} primaryLabel="في الموعد" secondaryLabel="متأخر" /></Section>
      </div>
      <div className="live-grid live-grid-two">
        <Section title="أكثر البنود تسببًا بالمشكلات"><HorizontalBars title="أكثر البنود تسببًا بالمشكلات" valueLabel="عدد الأخطاء" items={section.topErrorFields.map((item) => ({ id: item.fieldId, label: item.fieldName, value: item.errorCount, detail: formatNumber(item.errorCount) + ' من ' + formatNumber(item.sampleSize) + ' عينة' }))} /></Section>
        <Section title="المذكرات الحديثة" description="صف تشغيلي مختصر مرتبط بالمعرف الحقيقي."><RecentNotes snapshot={snapshot} /></Section>
      </div>
    </>
  )
}

function CompanyKpi({ label, value, detail, tone }: { label: string; value: string; detail: string; tone: 'blue' | 'teal' | 'gold' | 'red' | 'violet' }) {
  return <MetricCard label={label} value={value} detail={detail} tone={tone} />
}

function CompanyView({ snapshot, companyId, lookups }: { snapshot: DashboardSnapshot; companyId: string; lookups: DashboardLookups }) {
  const section = snapshot.views.companySummary
  const company = section?.company ?? lookups.companies.find((item) => item.id === companyId) ?? null
  const metrics: CompanyMetrics | null = section?.metrics ?? null
  const previous = section?.previousMetrics ?? null
  const comparison = section?.comparison ?? null
  if (!section || !company) {
    return <div className="live-no-data"><strong>اختر شركة لعرض التقرير</strong><span>افتح دليل الشركات ثم اختر شركة بالمعرف الثابت.</span></div>
  }
  const period = snapshot.views.context.period
  return (
    <>
      <div className="live-company-hero">
        <div>
          <span className="live-company-tag">تقرير شركة مستقل</span>
          <h2>{company.name}</h2>
          <p>الفترة: {formatDate(period.from)} — {formatDate(period.to)}</p>
        </div>
        <div className="live-company-score"><span>التغير في الجودة</span><strong dir="ltr">{comparisonText(Boolean(section.previousPeriod), comparison?.itemQualityDeltaPoints)}</strong><small>نقاط مئوية مقابل الفترة السابقة</small></div>
      </div>
      <AvailabilityNotice hasRows={section.availability.hasRows} explanation={section.availability.explanation} />
      <div className="live-kpi-grid">
        <CompanyKpi label="إجمالي المذكرات" value={formatNumber(metrics?.totalNotes)} detail="ضمن الفترة المختارة" tone="blue" />
        <CompanyKpi label="تغطية المراجعة" value={formatPercent(metrics?.reviewCoveragePercentage)} detail={formatNumber(metrics?.reviewedItems) + ' من ' + formatNumber(metrics?.expectedItems) + ' بندًا متوقعًا'} tone="teal" />
        <CompanyKpi label="جودة البنود" value={formatPercent(metrics?.itemQualityPercentage)} detail={formatNumber(metrics?.correctItems) + ' صحيح من ' + formatNumber(metrics?.applicableReviewedItems) + ' منطبق'} tone="gold" />
        <CompanyKpi label="الالتزام الزمني" value={formatPercent(metrics?.timeCompliancePercentage)} detail={formatNumber(metrics?.onTimeNotes) + ' في الموعد من ' + formatNumber(metrics?.timeEligibleNotes)} tone="teal" />
        <CompanyKpi label="المتأخرة" value={formatNumber(metrics?.lateNotes)} detail="وفق حقل التاريخ المعتمد" tone="red" />
        <CompanyKpi label="حوادث التكرار" value={formatNumber(metrics?.duplicateIncidents)} detail={formatNumber(metrics?.companyResendIncidents) + ' إعادة إرسال و' + formatNumber(metrics?.duplicateNumberIncidents) + ' تكرار رقم'} tone="violet" />
      </div>
      <div className="live-grid live-grid-two">
        <Section title="اتجاه الشركة" description="الأشهر المعادة ضمن الفترة الحالية.">
          <TimeTrend title="اتجاه الشركة" valueLabel="مذكرة" items={section.monthlyTrend.map((item) => ({ id: item.month, label: item.monthLabel, value: item.count }))} />
        </Section>
        <Section title="حالات الشركة"><DonutChart title="توزيع الحالات" unit="مذكرة" items={section.statusDistribution} /></Section>
      </div>
      <Section title="جودة بنود الشركة" description="الصحيح والخطأ من العداد نفسه، ولا تُستنتج النسبة داخل الواجهة.">
        <GroupedColumns title="جودة بنود الشركة" primaryLabel="صحيح" secondaryLabel="خطأ" items={metrics && metrics.qualityEligibleNoteCount > 0 ? [{ id: company.id, label: company.name, primary: metrics.correctItems, secondary: metrics.errorItems }] : []} />
      </Section>
      <div className="live-grid live-grid-two">
        <Section title="مصانع الشركة"><StackedBars title="مصانع الشركة" rows={section.performanceByFactory} primaryLabel="في الموعد" secondaryLabel="متأخر" /></Section>
        <Section title="خطوط الإنتاج"><StackedBars title="خطوط الإنتاج" rows={section.performanceByLine} primaryLabel="في الموعد" secondaryLabel="متأخر" /></Section>
      </div>
      <div className="live-grid live-grid-two">
        <Section title="أعلى مشكلات الشركة"><HorizontalBars title="أكثر البنود تسببًا بالمشكلات" valueLabel="عدد الأخطاء" items={section.topErrorFields.map((item) => ({ id: item.fieldId, label: item.fieldName, value: item.errorCount, detail: formatNumber(item.errorCount) + ' من ' + formatNumber(item.sampleSize) + ' عينة' }))} /></Section>
        <Section title="الفترة السابقة" description={section.previousPeriod ? formatDate(section.previousPeriod.from) + ' — ' + formatDate(section.previousPeriod.to) : 'المقارنة تتطلب نطاقًا صريحًا.'}>
          {section.previousPeriod ? <div className="live-previous-grid">
            <div><span>جودة البنود</span><strong dir="ltr">{formatPercent(previous?.itemQualityPercentage)}</strong><small>{displayDeltaPoints(comparison?.itemQualityDeltaPoints)}</small></div>
            <div><span>الالتزام الزمني</span><strong dir="ltr">{formatPercent(previous?.timeCompliancePercentage)}</strong><small>{displayDeltaPoints(comparison?.timeComplianceDeltaPoints)}</small></div>
            <div><span>المذكرات</span><strong dir="ltr">{formatNumber(previous?.totalNotes)}</strong><small>مقارنة مباشرة بنطاق سابق</small></div>
          </div> : <p className="live-previous-guidance">حدد من وإلى لتفعيل مقارنة الفترة السابقة.</p>}
        </Section>
      </div>
      <Section title="آخر مذكرات الشركة"><RecentNotes snapshot={snapshot} /></Section>
    </>
  )
}

function readDirectorySearch(): string {
  return new URLSearchParams(window.location.search).get('companySearch') ?? ''
}

function DirectoryView({ snapshot, lookups, onChoose }: { snapshot: DashboardSnapshot; lookups: DashboardLookups; onChoose: (companyId: string) => void }) {
  const counts = useMemo(() => new Map(snapshot.views.executiveSummary.companyComparison.map((item) => [item.id, item.noteCount])), [snapshot])
  const [search, setSearch] = useState(readDirectorySearch)
  useEffect(() => {
    const onPopState = () => setSearch(readDirectorySearch())
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])
  const query = search.trim().toLowerCase()
  const companies = lookups.companies.filter((company) => !query || company.name.toLowerCase().includes(query))
  const updateSearch = (value: string) => {
    setSearch(value)
    const params = new URLSearchParams(window.location.search)
    if (value) params.set('companySearch', value)
    else params.delete('companySearch')
    const queryString = params.toString()
    window.history.replaceState(window.history.state, '', window.location.pathname + (queryString ? '?' + queryString : '') + window.location.hash)
  }
  return (
    <Section title="اختيار شركة للتقرير" description="دليل مستقل للشركات الثماني، بما فيها الشركة بلا مذكرات ضمن الفترة.">
      <div className="live-directory-toolbar">
        <label>بحث عن شركة<input aria-label="بحث عن شركة" value={search} onChange={(event) => updateSearch(event.target.value)} /></label>
        <span>{formatNumber(lookups.companies.length)} شركات نشطة</span>
      </div>
      <div className="live-directory">
        {companies.map((company) => {
          const count = counts.get(company.id)
          return (
            <button type="button" className="live-directory-row" key={company.id} onClick={() => onChoose(company.id)}>
              <span className="live-directory-dot" aria-hidden="true" />
              <span className="live-directory-name">{company.name}</span>
              <strong dir="ltr">{count === undefined ? '—' : formatNumber(count)}</strong>
              <small>{count === undefined ? 'لا تتوفر بيانات' : count === 0 ? 'لا توجد مذكرات ضمن الفترة' : 'مذكرة ضمن الفترة'}</small>
              <span className="live-directory-action">فتح التقرير ←</span>
            </button>
          )
        })}
        {companies.length === 0 && <p className="live-chart-empty">لا توجد شركة مطابقة للبحث.</p>}
      </div>
    </Section>
  )
}

void ExecutiveView
void OperationsView

function buildLegacySnapshot(snapshot: DashboardSnapshot): DashboardSnapshot {
  const executive = snapshot.views.executiveSummary
  const operations = snapshot.views.operationsSummary
  const current = snapshot as DashboardSnapshot & { summary?: DashboardSnapshot['summary']; companyMetrics?: DashboardSnapshot['companyMetrics'] }
  const context = snapshot.views.context

  return {
    ...snapshot,
    summary: current.summary ?? executive.summary ?? operations.summary,
    companyMetrics: current.companyMetrics ?? executive.metrics ?? operations.metrics,
    monthlyActivity: snapshot.monthlyActivity ?? executive.monthlyTrend ?? [],
    statusDistribution: snapshot.statusDistribution ?? executive.statusDistribution ?? operations.workflowStatus ?? [],
    duplicateBreakdown: snapshot.duplicateBreakdown ?? operations.repetitionClassification ?? [],
    recentNotes: snapshot.recentNotes ?? operations.recentNotes ?? [],
    sla: snapshot.sla ?? executive.sla ?? operations.sla,
    backlogByAge: snapshot.backlogByAge ?? operations.backlogByAge ?? [],
    incomingVsApproved: snapshot.incomingVsApproved ?? operations.incomingVsApproved ?? [],
    qualityByCompany: snapshot.qualityByCompany ?? executive.qualityByCompany ?? [],
    topErrorFields: snapshot.topErrorFields ?? operations.topErrorFields ?? executive.highImpactProblems ?? [],
    indicatorStateDistribution: snapshot.indicatorStateDistribution ?? operations.indicatorStates ?? [],
    performanceByFactory: snapshot.performanceByFactory ?? operations.performanceByFactory ?? [],
    performanceByLine: snapshot.performanceByLine ?? operations.performanceByLine ?? [],
    duplicateTrend: snapshot.duplicateTrend ?? operations.repetitionTrend ?? [],
    dataCompleteness: snapshot.dataCompleteness ?? operations.dataCompleteness ?? [],
    minSample: snapshot.minSample ?? 10,
    effectiveFilters: snapshot.effectiveFilters ?? {
      from: context.period.from ?? '',
      to: context.period.to ?? '',
      companyId: context.scope.companyId ?? '',
      factoryId: context.scope.factoryId ?? '',
      productionLineId: context.scope.productionLineId ?? '',
      noteTypeId: context.scope.noteTypeId ?? '',
      status: context.scope.status ?? '',
      dateBasis: context.period.dateBasis ?? 'WritingDate',
      entryCutoffDate: context.period.entryCutoffDate ?? '',
    },
  }
}
function LegacyTabbedReport({
  snapshot,
  initialSection,
  availability,
  showSectionNav = true,
}: {
  snapshot: DashboardSnapshot
  initialSection: LegacySectionKey
  availability: { hasRows: boolean; explanation: string | null }
  showSectionNav?: boolean
}) {
  const [activeSection, setActiveSection] = useState<LegacySectionKey>(initialSection)
  const legacySnapshot = buildLegacySnapshot(snapshot)
  const visibleSection = showSectionNav ? activeSection : initialSection

  return (
    <div className="live-legacy-report">
      <AvailabilityNotice hasRows={availability.hasRows} explanation={availability.explanation} />
      {showSectionNav && <LegacySectionNav activeSection={activeSection} onChange={setActiveSection} />}
      <LegacySectionPanel section={visibleSection}>
        {LegacyDashboardContent(visibleSection, legacySnapshot, true)}
      </LegacySectionPanel>
    </div>
  )
}
function LiveNavigation({ view, onChange }: { view: LiveDashboardView; onChange: (view: LiveDashboardView) => void }) {
  return (
    <nav className="live-navigation" aria-label="تنقل لوحة المؤشرات">
      {(Object.keys(viewLabels) as LiveDashboardView[]).map((item) => (
        <button type="button" key={item} className={view === item ? 'is-active' : ''} onClick={() => onChange(item)} aria-current={view === item ? 'page' : undefined}>
          {viewLabels[item]}
        </button>
      ))}
    </nav>
  )
}

interface DashboardLiveProps {
  config: SetupConfig | null
  onEditConnection?: () => void
}

export function DashboardLive({ config, onEditConnection }: DashboardLiveProps) {
  const [view, setView] = useState<LiveDashboardView>(readView)
  const [filters, setFilters] = useState<DashboardFilters>(loadFiltersFromUrlOrSaved)
  const [lookups, setLookups] = useState<DashboardLookups | null>(null)
  const { data, isConnected, isLoading, lastUpdated, snapshotError, refreshNow } = useDashboardData(filters, lookups !== null)

  useEffect(() => {
    const onPopState = () => setView(readView())
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  useEffect(() => {
    let cancelled = false
    api.getLookups().then((result) => {
      if (cancelled) return
      const next = sanitizeFiltersAgainstLookups(filters, result)
      if (JSON.stringify(next) !== JSON.stringify(filters)) {
        persistFilters(next)
        setFilters(next)
      }
      setLookups(result)
    }).catch(() => {
      if (cancelled) return
      const emptyLookups: DashboardLookups = { companies: [], factories: [], productionLines: [], noteTypes: [] }
      const safeFilters = sanitizeFiltersAgainstLookups(filters, emptyLookups)
      persistFilters(safeFilters)
      setFilters(safeFilters)
      setLookups(emptyLookups)
    })
    return () => { cancelled = true }
  }, [])

  const changeView = (nextView: LiveDashboardView) => {
    const nextFilters = nextView === 'company' ? filters : { ...filters, companyId: '', factoryId: '', productionLineId: '' }
    if (JSON.stringify(nextFilters) !== JSON.stringify(filters)) {
      persistFilters(nextFilters)
      setFilters(nextFilters)
    }
    updateRoute(nextView, nextView === 'company' ? filters.companyId || undefined : undefined)
    setView(nextView)
  }

  const chooseCompany = (companyId: string) => {
    const nextFilters = { ...filters, companyId, factoryId: '', productionLineId: '' }
    persistFilters(nextFilters)
    setFilters(nextFilters)
    updateRoute('company', companyId)
    setView('company')
  }

  const printReport = () => {
    window.dispatchEvent(new Event('resize'))
    window.setTimeout(() => window.print(), 120)
  }

  const content = !lookups || (isLoading && !data)
    ? <SkeletonLoader />
    : snapshotError && !data
      ? <DashboardSnapshotError error={snapshotError} onRetry={refreshNow} isLoading={isLoading} />
      : data
        ? view === 'executive'
          ? <LegacyTabbedReport snapshot={data} initialSection={DEFAULT_SECTION} availability={data.views.executiveSummary.availability} />
          : view === 'operations'
            ? <LegacyTabbedReport snapshot={data} initialSection="workflow" showSectionNav={false} availability={data.views.operationsSummary.availability} />
            : view === 'company'
              ? <CompanyView snapshot={data} companyId={filters.companyId} lookups={lookups} />
              : <DirectoryView snapshot={data} lookups={lookups} onChoose={chooseCompany} />
        : <DashboardSnapshotError error={snapshotError} onRetry={refreshNow} isLoading={isLoading} />

  return (
    <div className="live-dashboard app-bg app-text">
      <Header
        isConnected={isConnected}
        isLoading={isLoading}
        lastUpdated={lastUpdated}
        onRefresh={refreshNow}
        onEditConnection={onEditConnection}
        onPrint={view === 'company' ? printReport : undefined}
        organizationName={config?.organizationName}
        subOrganizationName={config?.subOrganizationName}
      />
      <main className="live-main">
        <LiveNavigation view={view} onChange={changeView} />
        {view !== 'directory' && <FilterBar lookups={lookups} filters={filters} onChange={setFilters} />}
        {view === 'directory' && <div className="live-directory-intro"><span>المسار المستقل</span><strong>اختر الشركة ثم انتقل إلى تقريرها الزمني.</strong></div>}
        {!isConnected && <OfflineBanner message="تعذر الاتصال بالخادم أو قاعدة البيانات. ستظهر البيانات القديمة إن كانت متاحة." />}
        <section className="live-page-heading">
          <div><p className="live-eyebrow">نظام متابعة المذكرات · بيانات موحدة</p><h1>{viewLabels[view]}</h1><p>{view === 'executive' ? 'ملخص قراري خلال دقيقة.' : view === 'operations' ? 'التدقيق والإدخال والتأخير والتكرار وجودة البيانات.' : view === 'company' ? 'تقرير مستقل لشركة واحدة من تاريخ إلى تاريخ.' : 'دليل الشركات الثماني دون إسقاط الشركات بلا نشاط.'}</p></div>
          <div className="live-page-meta"><span>{data ? scopeLabel(data) : 'النطاق: —'}</span><strong>{lastUpdated ? lastUpdated.toLocaleTimeString('ar-SY-u-nu-latn', { hour: '2-digit', minute: '2-digit' }) : '—'}</strong></div>
        </section>
        <div className="live-content">{content}</div>
      </main>
      <footer className="live-footer">تنفيذ وإعداد دائرة نظم وقواعد البيانات</footer>
    </div>
  )
}












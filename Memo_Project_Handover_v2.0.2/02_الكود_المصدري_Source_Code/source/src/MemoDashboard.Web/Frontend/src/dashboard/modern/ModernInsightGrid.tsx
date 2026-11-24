import { useMemo } from 'react'
import {
  ArcElement,
  BarElement,
  CategoryScale,
  Chart as ChartJS,
  DoughnutController,
  Legend,
  LinearScale,
  LineController,
  LineElement,
  PointElement,
  Tooltip,
  type ChartData,
  type ChartOptions,
} from 'chart.js'
import { Bar, Doughnut, Line } from 'react-chartjs-2'
import type { DashboardSnapshot } from '../../types/dashboard'
import { t } from '../../i18n'
import { formatChartNumber, formatMonthYear } from '../../services/format'
import { useChartTheme } from '../../hooks/useChartTheme'

ChartJS.register(ArcElement, BarElement, CategoryScale, DoughnutController, Legend, LinearScale, LineController, LineElement, PointElement, Tooltip)

interface ModernInsightGridProps {
  snapshot: DashboardSnapshot
}

function ModernChartCard({ title, description, children, className = '' }: { title: string; description: string; children: React.ReactNode; className?: string }) {
  return (
    <section className={`modern-chart-card modern-reveal ${className}`}>
      <div className="mb-3">
        <h3 className="text-base font-bold app-text">{title}</h3>
        <p className="mt-1 text-xs app-text-muted">{description}</p>
      </div>
      <div className="modern-chart-canvas h-72">{children}</div>
    </section>
  )
}

function EmptyChart() {
  return <div className="flex h-full items-center justify-center text-sm app-text-muted">{t('app.noData')}</div>
}

export function ModernInsightGrid({ snapshot }: ModernInsightGridProps) {
  const colors = useChartTheme()
  const reduceMotion = typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
  const animation = reduceMotion ? false : { duration: 850, easing: 'easeOutQuart' as const }
  const baseScales = useMemo(
    () => ({
      x: { ticks: { color: colors.textMuted, maxRotation: 25, autoSkip: true }, grid: { color: colors.grid } },
      y: { beginAtZero: true, ticks: { color: colors.textMuted, precision: 0, callback: (value: number | string) => formatChartNumber(value) }, grid: { color: colors.grid } },
    }),
    [colors],
  )
  const lineOptions = useMemo<ChartOptions<'line'>>(
    () => ({ responsive: true, maintainAspectRatio: false, animation, plugins: { legend: { labels: { color: colors.text } } }, scales: baseScales }),
    [animation, baseScales, colors.text],
  )
  const barOptions = useMemo<ChartOptions<'bar'>>(
    () => ({ responsive: true, maintainAspectRatio: false, animation, indexAxis: 'y', plugins: { legend: { display: false } }, scales: { x: baseScales.y, y: baseScales.x } }),
    [animation, baseScales],
  )
  const doughnutOptions = useMemo<ChartOptions<'doughnut'>>(
    () => ({ responsive: true, maintainAspectRatio: false, animation, cutout: '68%', plugins: { legend: { position: 'bottom', labels: { color: colors.text, padding: 16 } } } }),
    [animation, colors.text],
  )

  const monthlyData: ChartData<'line'> = {
    labels: snapshot.monthlyActivity.map((item) => formatMonthYear(item.month)),
    datasets: [{ label: t('charts.notesCount'), data: snapshot.monthlyActivity.map((item) => item.count), borderColor: colors.primary, backgroundColor: `${colors.primary}22`, fill: true, tension: 0.38, pointRadius: 3, pointHoverRadius: 6 }],
  }
  const eligible = snapshot.sla.eligibleNotes > 0 ? snapshot.sla.eligibleNotes : snapshot.summary.onTime + snapshot.summary.lateTotal
  const onTime = snapshot.sla.eligibleNotes > 0 ? snapshot.sla.onTimeNotes : snapshot.summary.onTime
  const late = snapshot.sla.eligibleNotes > 0 ? snapshot.sla.lateNotes : snapshot.summary.lateTotal
  const complianceData: ChartData<'doughnut'> = {
    labels: [t('charts.sla.onTime'), t('charts.sla.late'), t('charts.sla.invalidOrder')],
    datasets: [{ data: [onTime, late, snapshot.sla.invalidDateOrderNotes], backgroundColor: [colors.success, colors.danger, colors.warning], borderColor: colors.surface, borderWidth: 3 }],
  }
  const companies = snapshot.companies
  const companyData: ChartData<'bar'> = {
    labels: companies.map((item) => item.name),
    datasets: [{ label: t('charts.notesCount'), data: companies.map((item) => item.noteCount), backgroundColor: colors.palette[0] ?? colors.primary, borderRadius: 7, barThickness: 18 }],
  }

  return (
    <section className="modern-insight-grid" aria-label={t('v2.insightsLabel')}>
      <ModernChartCard title={t('v2.trendTitle')} description={t('v2.trendDescription')} className="modern-chart-wide">
        {snapshot.monthlyActivity.some((item) => item.count > 0) ? <Line data={monthlyData} options={lineOptions} /> : <EmptyChart />}
      </ModernChartCard>
      <ModernChartCard title={t('v2.complianceTitle')} description={t('v2.complianceDescription')}>
        {eligible > 0 ? <Doughnut data={complianceData} options={doughnutOptions} /> : <EmptyChart />}
      </ModernChartCard>
      <ModernChartCard title={t('v2.companiesTitle')} description={t('v2.companiesDescription')}>
        {companies.some((item) => item.noteCount > 0) ? <Bar data={companyData} options={barOptions} /> : <EmptyChart />}
      </ModernChartCard>
    </section>
  )
}
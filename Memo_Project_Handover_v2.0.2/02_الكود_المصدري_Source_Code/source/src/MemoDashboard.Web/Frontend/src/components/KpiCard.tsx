import { t } from '../i18n'

interface KpiCardProps {
  title: string
  value: string
  tint: string
  compact?: boolean
  valueClassName?: string
}

export function KpiCard({ title, value, tint, compact = false, valueClassName }: KpiCardProps) {
  const displayValue = value === '—' ? t('common.notAvailable') : value
  return (
    <div
      className={compact ? 'kpi-card dashboard-kpi-card rounded-xl border shadow-sm' : 'kpi-card rounded-xl border p-4 shadow-sm'}
      style={{ ['--c' as string]: tint }}
    >
      <p className={compact ? 'dashboard-kpi-title font-bold app-text-muted' : 'text-sm font-bold app-text-muted'} title={title}>
        {title}
      </p>
      <p className={(compact ? 'dashboard-kpi-value tnum font-bold leading-none' : 'mt-2 text-3xl font-bold leading-none tnum') + (valueClassName ? ' ' + valueClassName : '')}>
        {displayValue}
      </p>
    </div>
  )
}
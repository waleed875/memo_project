import { t } from '../i18n'
import type { DashboardSnapshotError } from '../hooks/useDashboardData'

interface DashboardSnapshotErrorProps {
  error: DashboardSnapshotError | null
  onRetry: () => void | Promise<void>
  isLoading: boolean
}

export function DashboardSnapshotError({ error, onRetry, isLoading }: DashboardSnapshotErrorProps) {
  return (
    <section className="app-surface rounded-2xl border app-border p-8 text-center shadow-sm" role="alert">
      <h2 className="text-lg font-bold app-text">{t('snapshot.failedTitle')}</h2>
      <p className="mt-2 text-sm app-text-muted">{error?.message ?? t('snapshot.failedBody')}</p>
      <button type="button" onClick={() => void onRetry()} disabled={isLoading} className="btn-primary mt-4 rounded-lg px-4 py-2 text-sm font-semibold disabled:opacity-60">
        {isLoading ? t('common.refreshing') : t('snapshot.retry')}
      </button>
    </section>
  )
}
import { t } from '../i18n'

interface OfflineBannerProps {
  message: string | null
}

export function OfflineBanner({ message }: OfflineBannerProps) {
  return (
    <div className="border-b border-rose-300 bg-rose-100 px-4 py-2 text-center text-sm font-medium text-rose-700">
      {t('offline.title')}
      {message ? ` (${message})` : ''}
    </div>
  )
}

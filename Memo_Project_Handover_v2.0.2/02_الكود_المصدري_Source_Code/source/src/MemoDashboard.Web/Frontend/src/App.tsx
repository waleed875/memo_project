import { useEffect, useState } from 'react'
import { api, ApiRequestError, type SetupConfig } from './services/api'
import { DashboardRouter } from './DashboardRouter'
import { AdminLoginPage } from './pages/AdminLoginPage'
import { SetupPage } from './pages/SetupPage'
import { DashboardPreview, type PreviewView } from './preview/DashboardPreview'
import { useAuth } from './context/AuthContext'
import { t } from './i18n'

type AppMode = 'loading' | 'setup' | 'login' | 'ready' | 'preview' | 'error'

export default function App() {
  const [mode, setMode] = useState<AppMode>('loading')
  const [isEditing, setIsEditing] = useState(false)
  const [config, setConfig] = useState<SetupConfig | null>(null)
  const [startupError, setStartupError] = useState<{ message: string; status: number | null } | null>(null)

  const { user, refreshSession } = useAuth()

  const loadConfig = () => {
    api
      .getSetupConfig()
      .then((result) => setConfig(result))
      .catch(() => setConfig(null))
  }

  useEffect(() => {
    const preview = new URLSearchParams(window.location.search).get('dashboardResetPreview')
    if (preview) {
      setMode('preview')
      return
    }

    let cancelled = false
    const timeout = window.setTimeout(() => {
      cancelled = true
      setStartupError({ message: t('common.connectionFailed'), status: null })
      setMode('error')
    }, 15000)

    api
      .getSetupStatus()
      .then(async (status) => {
        if (cancelled) return
        if (!status.isConfigured) {
          setMode('setup')
          return
        }

        const session = await api.getAdminSession()
        if (cancelled) return
        if (!session.authenticated) {
          setMode('login')
          return
        }

        setMode('ready')
        loadConfig()
      })
      .catch((error) => {
        if (!cancelled) {
          if (error instanceof ApiRequestError) {
            setStartupError({ message: error.message, status: error.status })
          } else {
            setStartupError({ message: error instanceof Error ? error.message : t('common.connectionFailed'), status: null })
          }
          setMode('error')
        }
      })
      .finally(() => window.clearTimeout(timeout))

    return () => {
      cancelled = true
      window.clearTimeout(timeout)
    }
  }, [])

  // If we were ready and user logged out, switch to login
  useEffect(() => {
    if (mode === 'ready' && !user) {
      setMode('login')
    }
  }, [mode, user])

  const openSetup = () => {
    setIsEditing(true)
    setMode('setup')
  }

  const setupSaved = () => {
    if (isEditing) {
      setIsEditing(false)
      if (user) {
        setMode('ready')
      } else {
        setMode('login')
      }
      loadConfig()
      return
    }
    setMode('login')
  }

  const cancelSetup = () => {
    setIsEditing(false)
    if (user) {
      setMode('ready')
    } else {
      setMode('login')
    }
    loadConfig()
  }

  const authenticated = async () => {
    await refreshSession()
    setMode('ready')
    loadConfig()
  }

  if (mode === 'preview') {
    const requested = new URLSearchParams(window.location.search).get('dashboardResetPreview')
    const view: PreviewView = requested === 'operations' || requested === 'company' || requested === 'directory' ? requested : 'executive'
    return <DashboardPreview view={view} />
  }

  if (mode === 'error') {
    if (startupError?.status === 429) {
      return <RateLimitedGate />
    }
    return (
      <div className="app-bg flex min-h-screen items-center justify-center px-4" dir="rtl">
        <section className="app-surface max-w-lg rounded-2xl border app-border p-8 text-center shadow-lg">
          <h1 className="text-xl font-bold app-text">{t('common.connectionFailed')}</h1>
          <p className="mt-3 text-sm app-text-muted">{startupError?.message ?? t('common.connectionFailed')}</p>
          <div className="mt-5 flex items-center justify-center gap-3">
            <button type="button" onClick={() => window.location.reload()} className="btn-primary rounded-lg px-4 py-2 text-sm font-semibold">
              {t('common.refresh')}
            </button>
            <button type="button" onClick={openSetup} className="rounded-lg border app-border app-surface-muted app-hover px-4 py-2 text-sm font-semibold app-text">
              ضبط الاتصال بقاعدة البيانات
            </button>
          </div>
        </section>
      </div>
    )
  }

  if (mode === 'loading') {
    return (
      <div className="app-bg flex min-h-screen items-center justify-center text-lg app-text-muted" dir="rtl">
        {t('app.loading')}
      </div>
    )
  }

  if (mode === 'login') return <AdminLoginPage onAuthenticated={authenticated} onOpenSetup={openSetup} />

  return mode === 'setup' ? (
    <SetupPage isEdit={isEditing} onSaved={setupSaved} onCancel={isEditing ? cancelSetup : undefined} />
  ) : (
    <DashboardRouter config={config} onEditConnection={openSetup} />
  )
}

const RATE_LIMIT_RETRY_SECONDS = 30

function RateLimitedGate() {
  const [secondsLeft, setSecondsLeft] = useState(RATE_LIMIT_RETRY_SECONDS)

  useEffect(() => {
    if (secondsLeft <= 0) {
      window.location.reload()
      return
    }
    const timer = window.setTimeout(() => setSecondsLeft((s) => s - 1), 1000)
    return () => window.clearTimeout(timer)
  }, [secondsLeft])

  return (
    <div className="app-bg flex min-h-screen items-center justify-center px-4" dir="rtl">
      <section className="app-surface max-w-lg rounded-2xl border app-border p-8 text-center shadow-lg" role="alert">
        <h1 className="text-xl font-bold app-text">{t('app.rateLimitedTitle')}</h1>
        <p className="mt-3 text-sm app-text-muted">{t('app.rateLimitedBody', { seconds: secondsLeft })}</p>
        <p className="tnum mt-2 text-sm font-bold app-text">{t('app.retryIn', { seconds: secondsLeft })}</p>
        <button type="button" onClick={() => window.location.reload()} className="btn-primary mt-5 rounded-lg px-4 py-2 text-sm font-semibold">
          {t('common.refresh')}
        </button>
      </section>
    </div>
  )
}
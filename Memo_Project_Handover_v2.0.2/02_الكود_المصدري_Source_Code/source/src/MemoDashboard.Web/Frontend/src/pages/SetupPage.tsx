import { useEffect, useState } from 'react'
import { api, type SetupPayload } from '../services/api'
import { t } from '../i18n'
import { useDirection } from '../i18n/LanguageProvider'

interface SetupPageProps {
  isEdit?: boolean
  onSaved: () => void
  onCancel?: () => void
}

const inputClass =
  'w-full rounded-lg border app-border app-surface px-3 py-2 text-sm app-text outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-100'

const labelClass = 'mb-1 block text-xs font-medium app-text-muted'

type MessageState = { type: 'success' | 'error'; text: string } | null

export function SetupPage({ isEdit = false, onSaved, onCancel }: SetupPageProps) {
  const dir = useDirection()
  const [host, setHost] = useState('localhost')
  const [port, setPort] = useState('5432')
  const [database, setDatabase] = useState('')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [organizationName, setOrganizationName] = useState('')
  const [subOrganizationName, setSubOrganizationName] = useState('')
  const [dashboardVersion, setDashboardVersion] = useState(1)
  const [sslMode, setSslMode] = useState('Disable')
  const [setupToken, setSetupToken] = useState('')
  const [loadingConfig, setLoadingConfig] = useState(isEdit)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<MessageState>(null)
  const [migrationPrompt, setMigrationPrompt] = useState<{ dbVersion: string; appVersion: string; message: string } | null>(null)
  const [newerDbBlocked, setNewerDbBlocked] = useState<{ dbVersion: string; appVersion: string; message: string } | null>(null)
  const [applyMigrationsConfirmed, setApplyMigrationsConfirmed] = useState(false)

  useEffect(() => {
    if (!isEdit) return
    let cancelled = false
    api
      .getSetupConfig()
      .then((config) => {
        if (cancelled) return
        setHost(config.host || 'localhost')
        setPort(String(config.port || 5432))
        setDatabase(config.database ?? '')
        setUsername(config.username ?? '')
        setOrganizationName(config.organizationName ?? '')
        setSubOrganizationName(config.subOrganizationName ?? '')
        setDashboardVersion(config.dashboardVersion ?? 1)
        setSslMode(config.sslMode === 'Require' ? 'Require' : 'Disable')
      })
      .catch(() => {
        if (!cancelled) setMessage({ type: 'error', text: t('setup.loadFailed') })
      })
      .finally(() => {
        if (!cancelled) setLoadingConfig(false)
      })
    return () => {
      cancelled = true
    }
  }, [isEdit])

  const buildPayload = (): SetupPayload => ({
    host,
    port: Number(port) || 5432,
    database,
    username,
    organizationName,
    subOrganizationName,
    password,
    dashboardVersion: dashboardVersion === 3 ? 3 : dashboardVersion === 2 ? 2 : 1,
    sslMode: sslMode === 'Require' ? 'Require' : 'Disable',
    applyMigrations: applyMigrationsConfirmed,
  })

  const handleTest = async () => {
    setBusy(true)
    setMessage(null)
    try {
      const result = await api.testConnection(buildPayload(), setupToken || undefined)
      if (result.databaseIsNewer) {
        setNewerDbBlocked({
          dbVersion: result.databaseVersion || '',
          appVersion: result.applicationVersion || '',
          message: result.message || 'إصدار قاعدة البيانات أحدث من إصدار البرنامج الحالي. يرجى مراجعة دائرة نظم وقواعد البيانات للتأكد من التحديث.',
        })
        setMigrationPrompt(null)
        setMessage({ type: 'error', text: result.message || '' })
      } else if (result.requiresUpgrade) {
        setNewerDbBlocked(null)
        setMigrationPrompt({
          dbVersion: result.databaseVersion || '',
          appVersion: result.applicationVersion || '',
          message: result.message || `قاعدة البيانات الحالية بالإصدار (${result.databaseVersion}) وهي أقدم من إصدار النظام (${result.applicationVersion}). هل ترغب في تطبيق الترحيلات لتحديثها؟`,
        })
        setMessage({ type: 'success', text: result.message || '' })
      } else {
        setNewerDbBlocked(null)
        setMigrationPrompt(null)
        setMessage({ type: result.success ? 'success' : 'error', text: result.message ?? '' })
      }
    } catch (err) {
      setMessage({ type: 'error', text: err instanceof Error ? err.message : t('setup.testFailed') })
    } finally {
      setBusy(false)
    }
  }

  const handleSave = async () => {
    if (newerDbBlocked) {
      setMessage({ type: 'error', text: newerDbBlocked.message })
      return
    }

    setBusy(true)
    setMessage(null)
    try {
      const payload = buildPayload()
      const result = await api.saveSetup(payload, setupToken || undefined)

      if (result.databaseIsNewer) {
        setNewerDbBlocked({
          dbVersion: result.databaseVersion || '',
          appVersion: result.applicationVersion || '',
          message: result.message || 'إصدار قاعدة البيانات أحدث من إصدار البرنامج الحالي. يرجى مراجعة دائرة نظم وقواعد البيانات للتأكد من التحديث.',
        })
        setMessage({ type: 'error', text: result.message || '' })
        return
      }

      if (result.requiresUpgrade && !applyMigrationsConfirmed) {
        setMigrationPrompt({
          dbVersion: result.databaseVersion || '',
          appVersion: result.applicationVersion || '',
          message: result.message || `قاعدة البيانات الحالية بالإصدار (${result.databaseVersion}) وهي أقدم من إصدار النظام (${result.applicationVersion}). يلزم موافقة مدير النظام لتطبيق الترحيلات لتحديثها.`,
        })
        setMessage({ type: 'success', text: result.message || '' })
        return
      }

      setMessage({ type: result.success ? 'success' : 'error', text: result.message ?? '' })
      if (result.success) {
        setSetupToken('')
        window.setTimeout(onSaved, 800)
      }
    } catch (err) {
      setMessage({ type: 'error', text: err instanceof Error ? err.message : t('setup.saveFailed') })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="app-bg flex min-h-screen items-center justify-center px-4 py-8" dir={dir}>
      <div className="app-surface w-full max-w-2xl rounded-2xl border app-border p-8 shadow-lg">
        <div className="flex items-center gap-3">
          <div className="app-setup-emblem">
            <img src="/brand/syrian-logo-gold.svg" alt={t('app.officialLogoAlt')} />
          </div>
          <div>
            <h1 className="text-2xl font-bold app-text">{isEdit ? t('setup.editTitle') : t('setup.title')}</h1>
            <p className="text-xs app-text-muted">{isEdit ? t('setup.editSubtitle') : t('setup.subtitle')}</p>
          </div>
        </div>
        <p className="mt-4 text-sm leading-relaxed app-text-muted">
          {t('setup.intro')}
          <span className="mx-1 font-mono" style={{ color: 'var(--primary)' }}>audit_memo_dashboard_reader</span>
          {t('setup.scriptBy')}
          <span className="mx-1 font-mono" style={{ color: 'var(--primary)' }}>scripts/create_db_reader_user.sql</span>.
        </p>
        <div className="mt-4 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-900">
          <p className="font-semibold">{t('setup.internalNetworkTitle')}</p>
          <p className="mt-1">{t('setup.internalNetworkHint')}</p>
        </div>
        {loadingConfig && <p className="mt-4 text-sm app-text-muted">{t('setup.loadingConfig')}</p>}
        <form
          className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2"
          onSubmit={(e) => {
            e.preventDefault()
            void handleSave()
          }}
        >
          <div className="sm:col-span-2">
            <h2 className="app-form-section-title">{t('setup.organizationSection')}</h2>
            <p className="mt-1 text-sm app-text-muted">{t('setup.organizationHint')}</p>
          </div>
          <label className="block sm:col-span-2">
            <span className={labelClass}>{t('setup.organizationName')}</span>
            <input
              value={organizationName}
              onChange={(e) => setOrganizationName(e.target.value)}
              className={inputClass}
              dir="auto"
            />
          </label>
          <label className="block sm:col-span-2">
            <span className={labelClass}>{t('setup.subOrganizationName')}</span>
            <input
              value={subOrganizationName}
              onChange={(e) => setSubOrganizationName(e.target.value)}
              className={inputClass}
              dir="auto"
            />
          </label>
          <label className="block">
            <span className={labelClass}>{t('setup.host')}</span>
            <input value={host} onChange={(e) => setHost(e.target.value)} className={inputClass} dir="ltr" />
          </label>
          <label className="block">
            <span className={labelClass}>{t('setup.port')}</span>
            <input
              type="number"
              value={port}
              onChange={(e) => setPort(e.target.value)}
              className={inputClass}
              dir="ltr"
            />
          </label>
          <label className="block sm:col-span-2">
            <span className={labelClass}>{t('setup.database')}</span>
            <input
              value={database}
              onChange={(e) => setDatabase(e.target.value)}
              className={inputClass}
              dir="ltr"
            />
          </label>
          <label className="block">
            <span className={labelClass}>{t('setup.username')}</span>
            <input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className={inputClass}
              dir="ltr"
            />
          </label>
          <label className="block">
            <span className={labelClass}>{t('setup.password')}</span>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className={inputClass}
              dir="ltr"
              placeholder={isEdit ? t('setup.passwordHint') : ''}
            />
          </label>
          <label className="block">
            <span className={labelClass}>{t('setup.sslMode')}</span>
            <select
              value={sslMode}
              onChange={(e) => setSslMode(e.target.value)}
              className={inputClass}
              dir="auto"
            >
              <option value="Disable">{t('setup.sslDisabled')}</option>
              <option value="Require">{t('setup.sslRequired')}</option>
            </select>
          </label>
          {!isEdit && (
            <label className="block sm:col-span-2">
              <span className={labelClass}>{t('setup.token')}</span>
              <input
                type="password"
                value={setupToken}
                onChange={(e) => setSetupToken(e.target.value)}
                className={inputClass}
                dir="ltr"
                autoComplete="off"
                placeholder={t('setup.tokenHint')}
              />
            </label>
          )}
          {newerDbBlocked && (
            <div className="sm:col-span-2 rounded-xl border border-rose-300 bg-rose-50 p-4 text-sm text-rose-900 shadow-sm">
              <div className="flex items-center gap-2 font-bold text-rose-800">
                <svg className="h-5 w-5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                </svg>
                <span>تعذر الربط: إصدار قاعدة البيانات أحدث من إصدار البرنامج الحالي</span>
              </div>
              <p className="mt-2 font-semibold text-rose-800 leading-relaxed">
                {newerDbBlocked.message}
              </p>
              <div className="mt-2 text-xs text-rose-600">
                <span>إصدار قاعدة البيانات: {newerDbBlocked.dbVersion || 'غير محدد'}</span>
                <span className="mx-2">|</span>
                <span>إصدار البرنامج المطلوب: {newerDbBlocked.appVersion || 'غير محدد'}</span>
              </div>
            </div>
          )}

          {migrationPrompt && !newerDbBlocked && (
            <div className="sm:col-span-2 rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900 shadow-sm">
              <div className="flex items-center gap-2 font-bold text-amber-800">
                <svg className="h-5 w-5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                <span>موافقة مدير النظام على ترحيل وتحديث قاعدة البيانات</span>
              </div>
              <p className="mt-2 font-medium text-amber-900 leading-relaxed">
                قاعدة البيانات الحالية بالإصدار ({migrationPrompt.dbVersion}) وهي أقدم من إصدار النظام ({migrationPrompt.appVersion}).
                يلزم موافقة مدير النظام لتطبيق الترحيلات المناسبة لتحديثها.
              </p>
              <div className="mt-3 flex flex-wrap items-center justify-between gap-3 pt-2 border-t border-amber-200">
                <label className="flex items-center gap-2 text-xs font-bold text-amber-900 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={applyMigrationsConfirmed}
                    onChange={(e) => setApplyMigrationsConfirmed(e.target.checked)}
                    className="h-4 w-4 rounded border-amber-400 text-emerald-600 focus:ring-emerald-500"
                  />
                  <span>أوافق بصفتي مدير النظام على تطبيق الترحيلات وتحديث القاعدة</span>
                </label>
                <button
                  type="button"
                  disabled={busy}
                  onClick={async () => {
                    setBusy(true)
                    try {
                      const res = await api.applyMigrations(setupToken || undefined)
                      setMessage({ type: res.success ? 'success' : 'error', text: res.message || '' })
                      if (res.success) {
                        setMigrationPrompt(null)
                        setApplyMigrationsConfirmed(false)
                      }
                    } catch (err) {
                      setMessage({ type: 'error', text: err instanceof Error ? err.message : 'فشل تطبيق الترحيلات.' })
                    } finally {
                      setBusy(false)
                    }
                  }}
                  className="rounded-lg bg-amber-600 px-3.5 py-1.5 text-xs font-bold text-white shadow hover:bg-amber-700 disabled:opacity-50"
                >
                  تطبيق الترحيلات الآن
                </button>
              </div>
            </div>
          )}

          {message && (
            <p
              className={
                message.type === 'success'
                  ? 'rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-700 sm:col-span-2'
                  : 'rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700 sm:col-span-2'
              }
            >
              {message.text}
            </p>
          )}
          <div className="flex gap-3 sm:col-span-2">
            <button
              type="button"
              onClick={() => void handleTest()}
              disabled={busy || loadingConfig}
              className="app-surface app-border rounded-lg border px-4 py-2 text-sm font-semibold app-text transition-colors app-hover disabled:cursor-not-allowed disabled:opacity-60"
            >
              {t('setup.test')}
            </button>
            <button
              type="submit"
              disabled={busy || loadingConfig}
              className="btn-primary flex-1 rounded-lg px-4 py-2 text-sm font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-60"
            >
              {busy ? t('setup.saving') : isEdit ? t('setup.saveEdit') : t('setup.save')}
            </button>
            {isEdit && onCancel && (
              <button
                type="button"
                onClick={onCancel}
                disabled={busy}
                className="app-surface app-border rounded-lg border px-4 py-2 text-sm font-semibold app-text transition-colors app-hover disabled:cursor-not-allowed disabled:opacity-60"
              >
                {t('common.back')}
              </button>
            )}
          </div>
        </form>
      </div>
    </div>
  )
}
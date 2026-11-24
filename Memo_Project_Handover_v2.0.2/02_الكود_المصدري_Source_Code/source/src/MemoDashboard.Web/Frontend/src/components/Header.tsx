import { useEffect, useState } from 'react'
import { formatTime } from '../services/format'
import { t, type Language } from '../i18n'
import { useLanguage } from '../i18n/LanguageProvider'
import { useTheme } from '../theme/ThemeProvider'
import { useAuth } from '../context/AuthContext'
import { ChangePasswordModal } from './common/ChangePasswordModal'
import { MemoEditorModal } from '../pages/memos/MemoEditorModal'
import SyrianLogo from '../assets/Syrian_logo_icon_gold.svg'

interface HeaderProps {
  isConnected?: boolean
  isLoading?: boolean
  lastUpdated?: Date | null
  onRefresh?: () => void
  onEditConnection?: () => void
  onPrint?: () => void
  isDisplayMode?: boolean
  organizationName?: string | null
  subOrganizationName?: string | null
  activeView?: string
  onNavigate?: (view: string) => void
}

const buttonBase =
  'rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-60'

export function Header({
  isConnected = true,
  isLoading = false,
  lastUpdated = null,
  onRefresh,
  onEditConnection,
  onPrint,
  isDisplayMode = false,
  organizationName,
  subOrganizationName,
  activeView,
  onNavigate,
}: HeaderProps) {
  const [isFullscreen, setIsFullscreen] = useState(false)
  const { language, setLanguage } = useLanguage()
  const { mode, cycleMode } = useTheme()
  const { user, role, isAdmin, isReviewer, isDataEntry, logout } = useAuth()

  const [isPasswordModalOpen, setIsPasswordModalOpen] = useState(false)
  const [isNewMemoOpen, setIsNewMemoOpen] = useState(false)

  const mainOrganization = organizationName?.trim() || t('app.defaultOrganization')
  const subOrganization = subOrganizationName?.trim()

  useEffect(() => {
    const handleChange = () => setIsFullscreen(Boolean(document.fullscreenElement))
    document.addEventListener('fullscreenchange', handleChange)
    return () => document.removeEventListener('fullscreenchange', handleChange)
  }, [])

  const toggleFullscreen = () => {
    if (document.fullscreenElement) {
      void document.exitFullscreen().catch(() => undefined)
    } else {
      void document.documentElement.requestFullscreen().catch(() => undefined)
    }
  }

  const toggleLanguage = () => {
    const next: Language = language === 'ar' ? 'en' : 'ar'
    setLanguage(next)
  }

  const currentViewKey = activeView || (typeof window !== 'undefined' ? new URLSearchParams(window.location.search).get('view') || 'dashboard' : 'dashboard')

  const handleNavClick = (viewId: string) => {
    if (onNavigate) {
      onNavigate(viewId)
      return
    }
    const params = new URLSearchParams(window.location.search)
    params.delete('report')
    if (viewId === 'dashboard') {
      params.delete('view')
    } else {
      params.set('view', viewId)
    }
    const query = params.toString()
    window.location.href = `${window.location.pathname}${query ? `?${query}` : ''}`
  }

  const navItems = [
    { id: 'dashboard', label: 'الرئيسية', icon: '🏛️', visible: true },
    { id: 'memos', label: 'سجل المذكرات', icon: '📝', visible: true },
    { id: 'review', label: 'طابور التدقيق', icon: '🛡️', visible: isReviewer || isAdmin },
    { id: 'incomplete', label: 'مؤشرات غير مكتملة', icon: '⚠️', visible: isDataEntry || isReviewer || isAdmin },
    { id: 'reports', label: 'مركز التقارير', icon: '📑', visible: true },
    { id: 'master', label: 'البيانات الأساسية', icon: '🏢', visible: isAdmin || isReviewer },
    { id: 'analytics', label: 'التحليلات البيانية', icon: '📊', visible: true },
    { id: 'admin', label: 'لوحة الإدارة', icon: '⚙️', visible: isAdmin },
  ]

  const getRoleArabicName = (r?: string | null) => {
    switch (r) {
      case 'ADMIN':
        return 'مدير النظام'
      case 'REVIEWER':
        return 'مدقق معتمد'
      case 'DATA_ENTRY':
        return 'مدخل بيانات'
      case 'VIEWER':
        return 'مشاهد ومطلع'
      default:
        return r || 'مستخدم'
    }
  }

  return (
    <header className="app-surface border-b app-border shadow-sm sticky top-0 z-30 print:hidden">
      {/* Top Banner */}
      <div className="mx-auto flex max-w-[1800px] flex-wrap items-center justify-between gap-4 px-4 py-3">
        <div className="flex min-w-0 items-center gap-3">
          <div className="shrink-0 h-11 w-11 flex items-center justify-center p-1 rounded-xl border border-amber-400/40 bg-gradient-to-br from-amber-500/15 via-amber-500/5 to-transparent shadow-sm">
            <img
              src={SyrianLogo}
              alt={t('app.officialLogoAlt')}
              className="h-full w-full object-contain"
              onError={(e) => { e.currentTarget.src = '/brand/Syrian_logo_icon_gold.svg'; }}
            />
          </div>
          <div className="min-w-0" dir={language === 'ar' ? 'rtl' : 'ltr'}>
            <div className="flex items-center gap-2">
              <h1 className="text-sm font-bold app-text">{mainOrganization}</h1>
              <span className="text-[10px] font-mono font-bold px-1.5 py-0.5 rounded bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-300">
                Release 2.0.2
              </span>
            </div>
            {subOrganization && <p className="text-xs app-text-muted">{subOrganization}</p>}
            <p className="text-xs font-semibold text-blue-600 dark:text-blue-400">{t('app.title')}</p>
          </div>
        </div>

        {/* User profile & Action controls */}
        <div className="print-controls flex flex-wrap items-center gap-2 text-xs">
          {(isDataEntry || isAdmin) && (
            <button
              type="button"
              onClick={() => setIsNewMemoOpen(true)}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-blue-600 text-white hover:bg-blue-700 font-bold shadow-sm transition-colors"
            >
              <span>+</span>
              <span>مذكرة جديدة</span>
            </button>
          )}

          {user && (
            <div className="flex items-center gap-2 px-2.5 py-1 rounded-lg border app-border app-surface-muted">
              <div className="text-right">
                <span className="font-bold app-text block">{user.fullName || user.username}</span>
                <span className="text-[10px] text-blue-600 dark:text-blue-400 font-semibold">
                  {getRoleArabicName(role)}
                </span>
              </div>
              <button
                type="button"
                onClick={() => setIsPasswordModalOpen(true)}
                title="تغيير كلمة المرور"
                className="p-1 rounded hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-500"
              >
                🔑
              </button>
              <button
                type="button"
                onClick={() => {
                  if (window.confirm('هل تريد تسجيل الخروج؟')) {
                    void logout()
                  }
                }}
                title="تسجيل الخروج"
                className="p-1 rounded hover:bg-rose-100 dark:hover:bg-rose-950 text-rose-600"
              >
                🚪
              </button>
            </div>
          )}

          <span className="flex items-center gap-1.5 rounded-lg border app-border app-surface-muted px-2.5 py-1.5 app-text">
            <span
              className={'h-2 w-2 rounded-full ' + (isConnected ? 'animate-pulse bg-emerald-500' : 'bg-rose-500')}
            />
            {isConnected ? t('common.connected') : t('common.disconnected')}
          </span>

          {lastUpdated && (
            <span className="rounded-lg border app-border app-surface-muted px-2.5 py-1.5 app-text">
              {t('common.lastUpdated')}: {formatTime(lastUpdated)}
            </span>
          )}

          {!isDisplayMode && onPrint && (
            <button type="button" onClick={onPrint} className={buttonBase + ' app-hover border app-border app-text'}>
              {t('common.printDashboard')}
            </button>
          )}

          {!isDisplayMode && onEditConnection && (
            <button type="button" onClick={onEditConnection} className={buttonBase + ' app-hover border app-border app-text'}>
              {t('common.connectionSettings')}
            </button>
          )}

          {onRefresh && (
            <button type="button" onClick={onRefresh} disabled={isLoading} className={buttonBase + ' btn-primary'}>
              {isLoading ? t('common.refreshing') : t('common.refresh')}
            </button>
          )}

          <button
            type="button"
            onClick={toggleLanguage}
            title={language === 'ar' ? t('lang.en') : t('lang.ar')}
            className={buttonBase + ' app-hover border app-border app-text'}
          >
            {language === 'ar' ? t('lang.en') : t('lang.ar')}
          </button>

          <button type="button" onClick={cycleMode} title={t('theme.cycle')} className={buttonBase + ' app-hover border app-border app-text'}>
            {t('theme.' + mode)}
          </button>

          {!isDisplayMode && (
            <button type="button" onClick={toggleFullscreen} className={buttonBase + ' app-hover border app-border app-text'}>
              {isFullscreen ? t('common.exitFullscreen') : t('common.fullscreen')}
            </button>
          )}
        </div>
      </div>

      {/* Navigation Tabs Bar */}
      <nav className="border-t app-border bg-slate-50/60 dark:bg-slate-900/40">
        <div className="mx-auto flex max-w-[1800px] items-center gap-1 px-4 overflow-x-auto">
          {navItems
            .filter((item) => item.visible)
            .map((item) => {
              const isActive = currentViewKey === item.id || (item.id === 'dashboard' && (currentViewKey === '' || currentViewKey === 'central'))
              return (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => handleNavClick(item.id)}
                  className={`flex items-center gap-1.5 px-4 py-2.5 text-xs font-bold transition-colors whitespace-nowrap border-b-2 ${
                    isActive
                      ? 'border-blue-600 text-blue-600 dark:text-blue-400 bg-white dark:bg-slate-800 shadow-sm'
                      : 'border-transparent app-text-muted hover:text-slate-900 dark:hover:text-slate-100 hover:bg-slate-100/60 dark:hover:bg-slate-800/40'
                  }`}
                >
                  <span>{item.icon}</span>
                  <span>{item.label}</span>
                </button>
              )
            })}
        </div>
      </nav>

      {/* Modals */}
      <ChangePasswordModal
        isOpen={isPasswordModalOpen}
        onClose={() => setIsPasswordModalOpen(false)}
      />

      {isNewMemoOpen && (
        <MemoEditorModal
          isOpen={isNewMemoOpen}
          noteId={null}
          onClose={() => setIsNewMemoOpen(false)}
          onSaved={() => {
            setIsNewMemoOpen(false)
            if (onRefresh) onRefresh()
          }}
        />
      )}
    </header>
  )
}
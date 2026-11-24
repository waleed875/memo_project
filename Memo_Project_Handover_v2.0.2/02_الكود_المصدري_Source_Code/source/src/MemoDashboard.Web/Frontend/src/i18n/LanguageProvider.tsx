import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { setCurrentLocale } from '../services/format'
import {
  LANGUAGE_STORAGE_KEY,
  getDirection,
  getCurrentLocale,
  setCurrentLanguage,
  loadSavedLanguage,
  type Language,
} from './index'

interface LanguageContextValue {
  language: Language
  direction: 'rtl' | 'ltr'
  setLanguage: (language: Language) => void
}

const LanguageContext = createContext<LanguageContextValue | null>(null)

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(loadSavedLanguage)

  useEffect(() => {
    setCurrentLanguage(language)
    setCurrentLocale(getCurrentLocale())
    document.documentElement.lang = language
    document.documentElement.dir = getDirection()
    try {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, language)
    } catch {
      // ignore storage failures
    }
  }, [language])

  const setLanguage = useCallback((next: Language) => {
    setLanguageState(next)
  }, [])

  const value = useMemo(
    () => ({ language, direction: getDirection(), setLanguage }),
    [language, setLanguage],
  )

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>
}

export function useLanguage(): LanguageContextValue {
  const context = useContext(LanguageContext)
  if (!context) throw new Error('useLanguage must be used within LanguageProvider')
  return context
}

// 1.2.2: اتجاه آمن يعمل حتى خارج المزود (اختبارات/تضمينات) — يرجع dir الحاوية
// أو الوثيقة، ويفترض rtl (لغة الواجهة الافتراضية) عند غيابهما.
export function useDirection(): 'rtl' | 'ltr' {
  const context = useContext(LanguageContext)
  if (context) return context.direction
  if (typeof document !== 'undefined') {
    return document.documentElement.dir === 'ltr' ? 'ltr' : 'rtl'
  }
  return 'rtl'
}

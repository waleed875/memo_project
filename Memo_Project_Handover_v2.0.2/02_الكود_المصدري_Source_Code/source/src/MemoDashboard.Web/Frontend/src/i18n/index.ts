import ar from './ar.json'
import en from './en.json'

export type Language = 'ar' | 'en'

export const DEFAULT_LANGUAGE: Language = 'ar'
export const LANGUAGE_STORAGE_KEY = 'memo-dashboard-language'

type Dictionary = Record<string, string>

const dictionaries: Record<Language, Dictionary> = { ar: ar as Dictionary, en: en as Dictionary }

let currentLanguage: Language = DEFAULT_LANGUAGE

export function setCurrentLanguage(language: Language): void {
  currentLanguage = language
}

export function getCurrentLanguage(): Language {
  return currentLanguage
}

export function getCurrentLocale(): string {
  return currentLanguage === 'ar' ? 'ar-SY-u-nu-latn' : 'en-US'
}

export function getDirection(): 'rtl' | 'ltr' {
  return currentLanguage === 'ar' ? 'rtl' : 'ltr'
}

export function t(key: string, params?: Record<string, string | number>): string {
  const dict = dictionaries[currentLanguage]
  let value = dict[key] ?? dictionaries.ar[key] ?? key
  if (params) {
    for (const [name, param] of Object.entries(params)) {
      value = value.split(`{${name}}`).join(String(param))
    }
  }
  return value
}

export function translateStatus(status: string | null | undefined): string {
  if (!status) return t('app.unknownStatus')
  const key = `status.${status}`
  return key in dictionaries.ar ? t(key) : t('app.unknownStatus')
}

export function translateClassification(classification: string | null | undefined): string {
  if (!classification) return t('app.unknownClassification')
  const key = `classification.${classification}`
  return key in dictionaries.ar ? t(key) : t('app.unknownClassification')
}

export function indicatorStateLabel(code: number): string {
  const key = `indicator.${code}`
  return key in dictionaries.ar ? t(key) : String(code)
}

export function statusOptions(): { value: string; label: string }[] {
  return [
    'DRAFT',
    'COMPLETE',
    'HAS_ERRORS',
    'NEEDS_REVIEW',
    'UNDER_REVIEW',
    'RETURNED_FOR_CORRECTION',
    'APPROVED',
  ].map((value) => ({ value, label: translateStatus(value) }))
}

export function loadSavedLanguage(): Language {
  try {
    const raw = localStorage.getItem(LANGUAGE_STORAGE_KEY)
    return raw === 'en' ? 'en' : 'ar'
  } catch {
    return DEFAULT_LANGUAGE
  }
}

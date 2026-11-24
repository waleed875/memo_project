export type ThemeId = 'system' | 'light' | 'dark' | 'ocean' | 'violet' | 'amber'

export interface ThemeOption {
  id: ThemeId
  labelKey: string
}

export const THEME_STORAGE_KEY = 'memo-dashboard-theme'
export const DEFAULT_THEME: ThemeId = 'light'

export const THEME_OPTIONS: ThemeOption[] = [
  { id: 'system', labelKey: 'theme.system' },
  { id: 'light', labelKey: 'theme.light' },
  { id: 'dark', labelKey: 'theme.dark' },
  { id: 'ocean', labelKey: 'theme.ocean' },
  { id: 'violet', labelKey: 'theme.violet' },
  { id: 'amber', labelKey: 'theme.amber' },
]

export function isDarkTheme(id: ThemeId): boolean {
  return id === 'dark' || id === 'violet' || id === 'amber'
}

export function loadSavedTheme(): ThemeId {
  try {
    const raw = localStorage.getItem(THEME_STORAGE_KEY)
    if (raw && THEME_OPTIONS.some((option) => option.id === raw)) return raw as ThemeId
  } catch {
    // ignore storage failures
  }
  return DEFAULT_THEME
}

export function saveTheme(id: ThemeId): void {
  try {
    localStorage.setItem(THEME_STORAGE_KEY, id)
  } catch {
    // ignore storage failures
  }
}

export function nextTheme(current: ThemeId): ThemeId {
  const index = THEME_OPTIONS.findIndex((option) => option.id === current)
  return THEME_OPTIONS[(index + 1) % THEME_OPTIONS.length].id
}
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  loadSavedTheme,
  saveTheme,
  nextTheme,
  type ThemeId,
} from './theme'

interface ThemeContextValue {
  mode: ThemeId
  theme: ThemeId
  setMode: (mode: ThemeId) => void
  cycleMode: () => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [mode, setModeState] = useState<ThemeId>(loadSavedTheme)
  const [systemDark, setSystemDark] = useState<boolean>(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return false
    return window.matchMedia('(prefers-color-scheme: dark)').matches
  })

  const resolved = mode === 'system' ? (systemDark ? 'dark' : 'light') : mode

  useEffect(() => {
    saveTheme(mode)
    document.documentElement.dataset.theme = resolved
  }, [mode, resolved])

  useEffect(() => {
    if (mode !== 'system' || typeof window === 'undefined' || !window.matchMedia) return

    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const onChange = (event: MediaQueryListEvent) => setSystemDark(event.matches)
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [mode])

  const setMode = useCallback((next: ThemeId) => {
    setModeState(next)
  }, [])

  const cycleMode = useCallback(() => {
    setModeState((current) => nextTheme(current))
  }, [])

  const value = useMemo(
    () => ({ mode, theme: resolved, setMode, cycleMode }),
    [mode, resolved, setMode, cycleMode],
  )

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext)
  if (!context) throw new Error('useTheme must be used within ThemeProvider')
  return context
}
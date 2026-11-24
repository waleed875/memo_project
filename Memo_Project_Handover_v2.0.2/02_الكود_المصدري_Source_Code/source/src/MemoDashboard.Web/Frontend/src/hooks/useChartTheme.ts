import { useEffect, useState } from 'react'
import { useTheme } from '../theme/ThemeProvider'

export interface ChartThemeColors {
  text: string
  textMuted: string
  border: string
  grid: string
  surface: string
  primary: string
  success: string
  warning: string
  danger: string
  palette: string[]
}

const FALLBACK: ChartThemeColors = {
  text: '#0f172a',
  textMuted: '#64748b',
  border: '#e2e8f0',
  grid: 'rgba(100, 116, 139, 0.15)',
  surface: '#ffffff',
  primary: '#059669',
  success: '#10b981',
  warning: '#f59e0b',
  danger: '#f43f5e',
  palette: ['#10b981', '#f59e0b', '#f43f5e', '#06b6d4', '#8b5cf6', '#f97316', '#94a3b8'],
}

const PALETTE_VARS = [
  '--acc-emerald',
  '--acc-amber',
  '--acc-rose',
  '--acc-cyan',
  '--acc-violet',
  '--acc-orange',
  '--acc-pink',
  '--acc-blue',
  '--acc-slate',
]

function readColors(): ChartThemeColors {
  if (typeof document === 'undefined') return FALLBACK
  const styles = getComputedStyle(document.documentElement)
  const get = (name: string, fallback: string): string => {
    const value = styles.getPropertyValue(name).trim()
    return value || fallback
  }
  const palette = PALETTE_VARS.map((name) => get(name, '')).filter(Boolean)
  if (palette.length < 4) palette.push(...FALLBACK.palette)
  return {
    text: get('--text', FALLBACK.text),
    textMuted: get('--text-muted', FALLBACK.textMuted),
    border: get('--border', FALLBACK.border),
    grid: get('--chart-grid', FALLBACK.grid),
    surface: get('--surface', FALLBACK.surface),
    primary: get('--primary', FALLBACK.primary),
    success: get('--success', FALLBACK.success),
    warning: get('--warning', FALLBACK.warning),
    danger: get('--danger', FALLBACK.danger),
    palette,
  }
}

export function useChartTheme(): ChartThemeColors {
  const { theme } = useTheme()
  const [colors, setColors] = useState<ChartThemeColors>(readColors)

  useEffect(() => {
    setColors(readColors())
  }, [theme])

  return colors
}

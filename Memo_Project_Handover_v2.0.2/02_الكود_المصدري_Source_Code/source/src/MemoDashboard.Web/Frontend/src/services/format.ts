let currentLocale = 'ar-SY-u-nu-latn'

export function setCurrentLocale(locale: string): void {
  currentLocale = locale
}

export function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—'
  return new Intl.NumberFormat(currentLocale, { maximumFractionDigits: 1 }).format(value)
}

export function formatChartNumber(value: number | string | null | undefined): string {
  const numeric = typeof value === 'number' ? value : Number(value)
  return formatNumber(Number.isFinite(numeric) ? numeric : null)
}

export function formatPercent(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—'
  return new Intl.NumberFormat(currentLocale, { maximumFractionDigits: 2 }).format(value) + '%'
}

function parseDate(value: string): Date | null {
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    const [year, month, day] = value.split('-').map(Number)
    const date = new Date(year, month - 1, day)
    if (date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day) return date
    return null
  }
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

export function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const date = parseDate(value)
  if (!date) return '—'
  return date.toLocaleDateString(currentLocale, { year: 'numeric', month: 'long', day: 'numeric' })
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = parseDate(value)
  if (!date) return '—'
  const timeMatch = value.match(/(?:T|\s)(\d{2}):(\d{2})(?::(\d{2}))?/)
  const hasTime = Boolean(
    timeMatch && (Number(timeMatch[1]) !== 0 || Number(timeMatch[2]) !== 0 || Number(timeMatch[3] ?? 0) !== 0),
  )
  return date.toLocaleString(
    currentLocale,
    hasTime
      ? { year: 'numeric', month: 'long', day: 'numeric', hour: '2-digit', minute: '2-digit' }
      : { year: 'numeric', month: 'long', day: 'numeric' },
  )
}

export function formatShortDate(value: string | null | undefined): string {
  if (!value) return '—'
  const date = parseDate(value)
  if (!date) return '—'
  return date.toLocaleDateString(currentLocale, { year: 'numeric', month: '2-digit', day: '2-digit' })
}

export function formatMonthYear(value: string | null | undefined): string {
  if (!value || !/^\d{4}-\d{2}$/.test(value)) return value ?? '—'
  const [year, month] = value.split('-').map(Number)
  const date = new Date(year, month - 1, 1)
  return date.toLocaleDateString(currentLocale, { year: 'numeric', month: 'long' })
}

export function formatTime(value: Date | null): string {
  if (!value) return '—'
  return value.toLocaleTimeString(currentLocale, { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

export function calcDelayDays(
  writingDate: string | null | undefined,
  arrivalAt: string | null | undefined,
): number | null {
  if (!writingDate || !arrivalAt) return null
  const startDate = parseDate(writingDate)
  const endDate = parseDate(arrivalAt)
  if (!startDate || !endDate) return null
  return Math.max(0, Math.round((endDate.getTime() - startDate.getTime()) / 86400000))
}

export function complianceRate(totalNotes: number, onTime: number): number | null {
  if (!totalNotes || totalNotes <= 0) return null
  return Math.round((onTime / totalNotes) * 10000) / 100
}

export function percentage(total: number, part: number): number | null {
  if (!total || total <= 0) return null
  return Math.round((part / total) * 10000) / 100
}

export function rateAsPercent(numerator: number, denominator: number): number | null {
  if (!denominator || denominator <= 0) return null
  return Math.round((numerator / denominator) * 10000) / 100
}

export function hasEnoughSample(sampleSize: number, minimum: number): boolean {
  return sampleSize >= minimum
}
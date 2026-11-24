import { useEffect, useState } from 'react'
import type { DashboardFilters, DashboardLookups } from '../types/dashboard'
import { statusOptions } from '../i18n'
import { t } from '../i18n'
import { formatDate } from '../services/format'

export const EMPTY_FILTERS: DashboardFilters = {
  from: '',
  to: '',
  dateBasis: 'WritingDate',
  companyId: '',
  factoryId: '',
  productionLineId: '',
  noteTypeId: '',
  status: '',
  entryCutoffDate: '',
}

const FILTERS_STORAGE_KEY = 'memo-dashboard-filters'
export const FILTER_QUERY_KEYS: Array<keyof DashboardFilters> = [
  'from', 'to', 'dateBasis', 'companyId', 'factoryId', 'productionLineId', 'noteTypeId', 'status', 'entryCutoffDate',
]
const DATE_FILTER_KEYS: Array<keyof DashboardFilters> = ['from', 'to', 'entryCutoffDate']
const IDENTIFIER_FILTER_KEYS: Array<keyof DashboardFilters> = ['companyId', 'factoryId', 'productionLineId', 'noteTypeId']

function isValidIsoDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const [year, month, day] = value.split('-').map(Number)
  const date = new Date(year, month - 1, day)
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day
}

function isValidUuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value)
}

function isValidFilterValue(key: keyof DashboardFilters, value: string): boolean {
  if (!value) return true
  if (DATE_FILTER_KEYS.includes(key)) return isValidIsoDate(value)
  if (IDENTIFIER_FILTER_KEYS.includes(key)) return isValidUuid(value)
  if (key === 'status') return statusOptions().some((option) => option.value === value)
  if (key === 'dateBasis') return value === 'WritingDate' || value === 'ArrivalDate'
  return true
}

export function sanitizeFilterValues(input: Partial<DashboardFilters>): DashboardFilters {
  const next: DashboardFilters = { ...EMPTY_FILTERS, ...input }
  for (const key of FILTER_QUERY_KEYS) {
    const value = next[key] ?? ''
    if (!isValidFilterValue(key, value)) {
      if (key === 'dateBasis') next.dateBasis = 'WritingDate'
      else next[key] = '' as never
    }
  }
  if (next.from && next.to && next.from > next.to) {
    next.from = ''
    next.to = ''
  }
  return next
}

function saveFiltersToStorage(filters: DashboardFilters): void {
  if (typeof window === 'undefined') return
  try {
    localStorage.setItem(FILTERS_STORAGE_KEY, JSON.stringify(filters))
  } catch {
    // ignore storage failures
  }
}

export function syncFiltersToUrl(filters: DashboardFilters): void {
  if (typeof window === 'undefined') return
  const params = new URLSearchParams(window.location.search)
  for (const key of FILTER_QUERY_KEYS) {
    const value = filters[key] ?? ''
    if (value) params.set(key, value)
    else params.delete(key)
  }
  const query = params.toString()
  const nextUrl = window.location.pathname + (query ? '?' + query : '') + window.location.hash
  window.history.replaceState(window.history.state, '', nextUrl)
}

export function persistFilters(filters: DashboardFilters): void {
  const sanitized = sanitizeFilterValues(filters)
  saveFiltersToStorage(sanitized)
  syncFiltersToUrl(sanitized)
}

export function loadFiltersFromUrlOrSaved(): DashboardFilters {
  const saved = loadSavedFilters()
  if (typeof window === 'undefined') return saved
  const params = new URLSearchParams(window.location.search)
  const next = { ...saved }
  let cleanedUrl = false
  for (const key of FILTER_QUERY_KEYS) {
    if (!params.has(key)) continue
    const value = params.get(key) ?? ''
    if (isValidFilterValue(key, value)) {
      if (key === 'dateBasis') next.dateBasis = value as DashboardFilters['dateBasis']
      else next[key] = value as never
    }
    else cleanedUrl = true
  }
  if (next.from && next.to && next.from > next.to) {
    next.from = saved.from
    next.to = saved.to
    cleanedUrl = true
  }
  const sanitized = sanitizeFilterValues(next)
  if (JSON.stringify(sanitized) !== JSON.stringify(next)) cleanedUrl = true
  if (cleanedUrl) {
    saveFiltersToStorage(sanitized)
    syncFiltersToUrl(sanitized)
  }
  return sanitized
}

export function loadSavedFilters(): DashboardFilters {
  if (typeof window === 'undefined') return EMPTY_FILTERS
  try {
    const raw = localStorage.getItem(FILTERS_STORAGE_KEY)
    if (!raw) return EMPTY_FILTERS
    const parsed = JSON.parse(raw) as Partial<DashboardFilters>
    const sanitized = sanitizeFilterValues(parsed)
    saveFiltersToStorage(sanitized)
    return sanitized
  } catch {
    return EMPTY_FILTERS
  }
}
export function sanitizeFiltersAgainstLookups(filters: DashboardFilters, lookups: DashboardLookups): DashboardFilters {
  const next = sanitizeFilterValues(filters)
  if (next.companyId && !lookups.companies.some((company) => company.id === next.companyId)) {
    next.companyId = ''
    next.factoryId = ''
    next.productionLineId = ''
  }
  const factory = next.factoryId ? lookups.factories.find((item) => item.id === next.factoryId) : null
  if (next.factoryId && (!factory || (next.companyId && factory.companyId !== next.companyId))) {
    next.factoryId = ''
    next.productionLineId = ''
  }
  const line = next.productionLineId ? lookups.productionLines.find((item) => item.id === next.productionLineId) : null
  const lineFactory = line ? lookups.factories.find((item) => item.id === line.factoryId) : null
  if (next.productionLineId && (!line || (next.factoryId && line.factoryId !== next.factoryId) || (next.companyId && (!lineFactory || lineFactory.companyId !== next.companyId)))) {
    next.productionLineId = ''
  }
  if (next.noteTypeId && !lookups.noteTypes.some((item) => item.id === next.noteTypeId)) {
    next.noteTypeId = ''
  }
  return next
}
interface ActiveFilterChip {
  key: string
  label: string
}

function buildActiveChips(filters: DashboardFilters, lookups: DashboardLookups | null): ActiveFilterChip[] {
  const chips: ActiveFilterChip[] = []
  if (filters.dateBasis === 'ArrivalDate') chips.push({ key: 'dateBasis', label: t('filters.chipDateBasisArrival') })
  if (filters.from) chips.push({ key: 'from', label: t('filters.chipFrom', { date: formatDate(filters.from) }) })
  if (filters.to) chips.push({ key: 'to', label: t('filters.chipTo', { date: formatDate(filters.to) }) })
  if (filters.entryCutoffDate) chips.push({ key: 'entryCutoffDate', label: t('filters.chipEntryCutoff', { date: formatDate(filters.entryCutoffDate) }) })
  if (filters.companyId) {
    const name = lookups?.companies.find((c) => c.id === filters.companyId)?.name
    chips.push({ key: 'companyId', label: t('filters.chipCompany', { name: name ?? filters.companyId }) })
  }
  if (filters.factoryId) {
    const name = lookups?.factories.find((f) => f.id === filters.factoryId)?.name
    chips.push({ key: 'factoryId', label: t('filters.chipFactory', { name: name ?? filters.factoryId }) })
  }
  if (filters.productionLineId) {
    const name = lookups?.productionLines.find((p) => p.id === filters.productionLineId)?.name
    chips.push({ key: 'productionLineId', label: t('filters.chipLine', { name: name ?? filters.productionLineId }) })
  }
  if (filters.noteTypeId) {
    const name = lookups?.noteTypes.find((n) => n.id === filters.noteTypeId)?.nameAr
    chips.push({ key: 'noteTypeId', label: t('filters.chipType', { name: name ?? filters.noteTypeId }) })
  }
  if (filters.status) {
    const label = statusOptions().find((o) => o.value === filters.status)?.label
    chips.push({ key: 'status', label: t('filters.chipStatus', { name: label ?? filters.status }) })
  }
  return chips
}

interface FilterBarProps {
  lookups: DashboardLookups | null
  filters: DashboardFilters
  onChange: (filters: DashboardFilters) => void
}

const inputClass =
  'w-full rounded-lg border app-border app-surface px-3 py-2 text-sm app-text outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-100'

export function FilterBar({ lookups, filters, onChange }: FilterBarProps) {
  const [isOpen, setIsOpen] = useState(true)

  const update = (patch: Partial<DashboardFilters>) => {
    const next = { ...filters, ...patch }
    persistFilters(next)
    onChange(next)
  }

  const reset = () => update(EMPTY_FILTERS)

  useEffect(() => {
    if (!lookups) return
    const next = sanitizeFiltersAgainstLookups(filters, lookups)
    if (JSON.stringify(next) !== JSON.stringify(filters)) {
      persistFilters(next)
      onChange(next)
    }
  }, [filters, lookups, onChange])

  const removeChip = (key: string) => {
    const patch: Partial<DashboardFilters> = {}
    if (key === 'from') patch.from = ''
    else if (key === 'to') patch.to = ''
    else if (key === 'dateBasis') patch.dateBasis = 'WritingDate'
    else if (key === 'entryCutoffDate') patch.entryCutoffDate = ''
    else if (key === 'companyId') {
      patch.companyId = ''
      patch.factoryId = ''
      patch.productionLineId = ''
    } else if (key === 'factoryId') {
      patch.factoryId = ''
      patch.productionLineId = ''
    }
    else if (key === 'productionLineId') patch.productionLineId = ''
    else if (key === 'noteTypeId') patch.noteTypeId = ''
    else if (key === 'status') patch.status = ''
    update(patch)
  }

  const chips = buildActiveChips(filters, lookups)
  const factories =
    filters.companyId && lookups
      ? lookups.factories.filter((factory) => factory.companyId === filters.companyId)
      : lookups?.factories ?? []
  const lines =
    filters.factoryId && lookups
      ? lookups.productionLines.filter((line) => line.factoryId === filters.factoryId)
      : lookups?.productionLines ?? []

  return (
    <section className="dashboard-filter-bar app-surface rounded-xl border app-border shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
        <button
          onClick={() => setIsOpen((v) => !v)}
          className="flex items-center gap-2 text-sm font-semibold app-text transition-colors hover:opacity-80"
        >
          {isOpen ? t('filters.hide') : t('filters.show')}
          <span className="text-xs font-normal app-text-muted">{t('filters.hint')}</span>
          {chips.length > 0 && (
            <span className="chip-danger rounded-full border px-2.5 py-0.5 text-xs font-bold">
              {t('filters.active')}: {chips.length}
            </span>
          )}
        </button>
        {chips.length > 0 && (
          <button onClick={reset} className="text-sm transition-colors hover:opacity-80" style={{ color: 'var(--danger)' }}>
            {t('filters.reset')}
          </button>
        )}
      </div>
      {chips.length > 0 && (
        <div className="app-surface-muted flex flex-wrap items-center gap-2 border-t app-border px-4 py-2.5">
          <span className="text-xs font-semibold app-text-muted">{t('filters.activeChips')}</span>
          {chips.map((chip) => (
            <span
              key={chip.key}
              className="chip-tint inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-medium"
              style={{ ['--c' as string]: 'var(--success)' }}
            >
              {chip.label}
              <button
                onClick={() => removeChip(chip.key)}
                className="rounded-full transition-colors hover:opacity-70"
                style={{ color: 'var(--danger)' }}
                aria-label={t('filters.remove', { label: chip.label })}
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}
      {isOpen && (
        <div className="grid grid-cols-2 gap-3 border-t app-border px-4 py-3 md:grid-cols-3 xl:grid-cols-8">
          <label className="block">
            <span className="mb-1 block text-xs app-text-muted">{t('filters.dateBasis')}</span>
            <select value={filters.dateBasis} onChange={(e) => update({ dateBasis: e.target.value as DashboardFilters['dateBasis'] })} className={inputClass}>
              <option value="WritingDate">{t('filters.writingDate')}</option>
              <option value="ArrivalDate">{t('filters.arrivalDate')}</option>
            </select>
          </label>
          <label className="block">
            <span className="mb-1 block text-xs app-text-muted">{t('filters.from')}</span>
            <input type="date" value={filters.from} onChange={(e) => update({ from: e.target.value })} className={inputClass} />
          </label>
          <label className="block">
            <span className="mb-1 block text-xs app-text-muted">{t('filters.to')}</span>
            <input type="date" value={filters.to} onChange={(e) => update({ to: e.target.value })} className={inputClass} />
          </label>
          <label className="block">
            <span className="mb-1 block text-xs app-text-muted">{t('filters.entryCutoff')}</span>
            <input type="date" value={filters.entryCutoffDate ?? ''} onChange={(e) => update({ entryCutoffDate: e.target.value })} className={inputClass} />
          </label>
          <label className="block">
            <span className="mb-1 block text-xs app-text-muted">{t('filters.company')}</span>
            <select
              value={filters.companyId}
              onChange={(e) =>
                update({ companyId: e.target.value, factoryId: '', productionLineId: '' })
              }
              className={inputClass}
            >
              <option value="">{t('common.all')}</option>
              {(lookups?.companies ?? []).map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </select>
          </label>
          <label className="block">
            <span className="mb-1 block text-xs app-text-muted">{t('filters.factory')}</span>
            <select
              value={filters.factoryId}
              onChange={(e) => update({ factoryId: e.target.value, productionLineId: '' })}
              className={inputClass}
            >
              <option value="">{t('common.all')}</option>
              {factories.map((factory) => (
                <option key={factory.id} value={factory.id}>
                  {factory.name}
                </option>
              ))}
            </select>
          </label>
          <label className="block">
            <span className="mb-1 block text-xs app-text-muted">{t('filters.productionLine')}</span>
            <select
              value={filters.productionLineId}
              onChange={(e) => update({ productionLineId: e.target.value })}
              className={inputClass}
            >
              <option value="">{t('common.all')}</option>
              {lines.map((line) => (
                <option key={line.id} value={line.id}>
                  {line.name}
                </option>
              ))}
            </select>
          </label>
          <label className="block">
            <span className="mb-1 block text-xs app-text-muted">{t('filters.noteType')}</span>
            <select
              value={filters.noteTypeId}
              onChange={(e) => update({ noteTypeId: e.target.value })}
              className={inputClass}
            >
              <option value="">{t('common.all')}</option>
              {(lookups?.noteTypes ?? []).map((noteType) => (
                <option key={noteType.id} value={noteType.id}>
                  {noteType.nameAr}
                </option>
              ))}
            </select>
          </label>
          <label className="block">
            <span className="mb-1 block text-xs app-text-muted">{t('filters.status')}</span>
            <select value={filters.status} onChange={(e) => update({ status: e.target.value })} className={inputClass}>
              <option value="">{t('common.all')}</option>
              {statusOptions().map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
        </div>
      )}
    </section>
  )
}

export function buildActiveFilterChips(filters: DashboardFilters, lookups: DashboardLookups | null): ActiveFilterChip[] {
  return buildActiveChips(filters, lookups)
}

export type { ActiveFilterChip }



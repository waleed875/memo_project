import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { FilterBar, loadFiltersFromUrlOrSaved, persistFilters } from '../../../src/MemoDashboard.Web/Frontend/src/components/FilterBar'
import { useDashboardData } from '../../../src/MemoDashboard.Web/Frontend/src/hooks/useDashboardData'
import { t } from '../../../src/MemoDashboard.Web/Frontend/src/i18n'
import { api, ApiRequestError } from '../../../src/MemoDashboard.Web/Frontend/src/services/api'
import type {
  DashboardFilters,
  DashboardLookups,
  DashboardSnapshot,
} from '../../../src/MemoDashboard.Web/Frontend/src/types/dashboard'

const filters: DashboardFilters = {
  from: '',
  to: '',
  companyId: '11111111-1111-4111-8111-111111111111',
  factoryId: '33333333-3333-4333-8333-333333333333',
  productionLineId: '55555555-5555-4555-8555-555555555555',
  noteTypeId: '',
  status: '',
}

const lookups: DashboardLookups = {
  companies: [
    { id: '11111111-1111-4111-8111-111111111111', name: 'الشركة الأولى' },
    { id: '22222222-2222-4222-8222-222222222222', name: 'الشركة الثانية' },
  ],
  factories: [
    { id: '33333333-3333-4333-8333-333333333333', companyId: '11111111-1111-4111-8111-111111111111', name: 'المصنع الأول' },
    { id: '44444444-4444-4444-8444-444444444444', companyId: '22222222-2222-4222-8222-222222222222', name: 'المصنع الثاني' },
  ],
  productionLines: [
    { id: '55555555-5555-4555-8555-555555555555', factoryId: '33333333-3333-4333-8333-333333333333', name: 'الخط الأول' },
    { id: '66666666-6666-4666-8666-666666666666', factoryId: '44444444-4444-4444-8444-444444444444', name: 'الخط الثاني' },
  ],
  noteTypes: [],
}

afterEach(() => {
  vi.restoreAllMocks()
  localStorage.clear()
  window.history.replaceState({}, '', '/')
})

describe('filter URL restoration', () => {
  it('rejects invalid dates/status and writes changed filters back to URL and storage', () => {
    window.history.replaceState({}, '', '/?from=2026-02-31&to=2026-01-01&status=INVALID&companyId=not-a-uuid')
    const restored = loadFiltersFromUrlOrSaved()

    expect(restored.from).toBe('')
    expect(restored.to).toBe('2026-01-01')
    expect(restored.status).toBe('')
    expect(restored.companyId).toBe('')
    expect(window.location.search).toContain('to=2026-01-01')

    persistFilters({ ...filters, from: '2026-03-01', entryCutoffDate: '2026-03-05' })
    expect(window.location.search).toContain('from=2026-03-01')
    expect(window.location.search).toContain('entryCutoffDate=2026-03-05')
    expect(JSON.parse(localStorage.getItem('memo-dashboard-filters') ?? '{}')).toMatchObject({ from: '2026-03-01', entryCutoffDate: '2026-03-05' })
  })
  it('gives valid URL filters precedence over saved filters, including entry cutoff', () => {
    localStorage.setItem('memo-dashboard-filters', JSON.stringify({
      from: '2026-01-01',
      companyId: '77777777-7777-4777-8777-777777777777',
      entryCutoffDate: '2026-01-15',
    }))
    window.history.replaceState({}, '', '/?from=2026-02-01&companyId=88888888-8888-4888-8888-888888888888&entryCutoffDate=2026-02-20')
    const restored = loadFiltersFromUrlOrSaved()

    expect(restored.from).toBe('2026-02-01')
    expect(restored.companyId).toBe('88888888-8888-4888-8888-888888888888')
    expect(restored.entryCutoffDate).toBe('2026-02-20')
  })
})

describe('dependent dashboard filters', () => {
  it('clears factory and production line when the company changes', () => {
    const onChange = vi.fn()
    render(<FilterBar lookups={lookups} filters={filters} onChange={onChange} />)

    fireEvent.change(screen.getByLabelText(t('filters.company')), {
      target: { value: '22222222-2222-4222-8222-222222222222' },
    })

    expect(onChange).toHaveBeenCalledWith({
      ...filters,
      companyId: '22222222-2222-4222-8222-222222222222',
      factoryId: '',
      productionLineId: '',
    })
  })

  it('clears the production line when the factory changes', () => {
    const onChange = vi.fn()
    const allFactories = { ...filters, companyId: '' }
    render(<FilterBar lookups={lookups} filters={allFactories} onChange={onChange} />)

    fireEvent.change(screen.getByLabelText(t('filters.factory')), {
      target: { value: '44444444-4444-4444-8444-444444444444' },
    })

    expect(onChange).toHaveBeenCalledWith({
      ...allFactories,
      factoryId: '44444444-4444-4444-8444-444444444444',
      productionLineId: '',
    })
  })
})

describe('dashboard refresh behavior', () => {
  it('loads immediately when filters change', async () => {
    const healthySnapshot = { isHealthy: true } as DashboardSnapshot
    const getSnapshot = vi.spyOn(api, 'getSnapshot').mockResolvedValue(healthySnapshot)
    const initialFilters: DashboardFilters = {
      from: '',
      to: '',
      companyId: '',
      factoryId: '',
      productionLineId: '',
      noteTypeId: '',
      status: '',
    }

    const { rerender } = renderHook(
      ({ currentFilters }) => useDashboardData(currentFilters),
      { initialProps: { currentFilters: initialFilters } },
    )

    await waitFor(() => expect(getSnapshot).toHaveBeenCalledTimes(1))

    const changedFilters = { ...initialFilters, companyId: '22222222-2222-4222-8222-222222222222' }
    rerender({ currentFilters: changedFilters })

    await waitFor(() => {
      expect(getSnapshot).toHaveBeenCalledTimes(2)
      expect(getSnapshot).toHaveBeenLastCalledWith(changedFilters, expect.any(AbortSignal))
    })
  })

  it('does not request a snapshot until lookup-dependent filters are ready', async () => {
    const getSnapshot = vi.spyOn(api, 'getSnapshot').mockResolvedValue({ isHealthy: true } as DashboardSnapshot)
    const { rerender } = renderHook(
      ({ enabled }) => useDashboardData(filters, enabled),
      { initialProps: { enabled: false } },
    )

    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(getSnapshot).not.toHaveBeenCalled()

    rerender({ enabled: true })
    await waitFor(() => expect(getSnapshot).toHaveBeenCalledTimes(1))
  })
  it('retains the last healthy snapshot and marks it stale after a refresh error', async () => {
    const healthySnapshot = { isHealthy: true } as DashboardSnapshot
    const getSnapshot = vi.spyOn(api, 'getSnapshot')
      .mockResolvedValueOnce(healthySnapshot)
      .mockRejectedValueOnce(new Error('network unavailable'))
    const staleFilters = { ...filters, companyId: '' }
    const { result } = renderHook(({ currentFilters }) => useDashboardData(currentFilters), {
      initialProps: { currentFilters: staleFilters },
    })

    await waitFor(() => expect(result.current.data).toBe(healthySnapshot))
    getSnapshot.mockRejectedValueOnce(new Error('network unavailable'))

    await act(async () => {
      await result.current.refreshNow()
    })

    await waitFor(() => expect(result.current.isStale).toBe(true))
    expect(result.current.data).toBe(healthySnapshot)
    expect(getSnapshot).toHaveBeenCalledTimes(2)
  })
})



describe('snapshot failure semantics', () => {
  const healthySnapshot = { isHealthy: true } as DashboardSnapshot

  it.each([400, 401, 403, 500])('keeps server health separate from snapshot HTTP %s', async (status) => {
    vi.spyOn(api, 'getHealth').mockResolvedValue({ status: 'ok' })
    const getSnapshot = vi.spyOn(api, 'getSnapshot').mockRejectedValue(
      new ApiRequestError('server detail', status, 'snapshot_query_failed', 'corr-1'),
    )
    const { result } = renderHook(() => useDashboardData(filters))

    await waitFor(() => expect(result.current.snapshotError?.status).toBe(status))
    expect(result.current.isConnected).toBe(true)
    expect(result.current.snapshotStatus).toBe('error')
    expect(getSnapshot).toHaveBeenCalledTimes(1)
  })

  it('classifies invalid JSON without presenting it as a connection failure', async () => {
    vi.spyOn(api, 'getHealth').mockResolvedValue({ status: 'ok' })
    vi.spyOn(api, 'getSnapshot').mockRejectedValue(
      new ApiRequestError('invalid', 200, 'invalid_json', 'corr-2', 'invalid-json'),
    )
    const { result } = renderHook(() => useDashboardData(filters))

    await waitFor(() => expect(result.current.snapshotError?.code).toBe('invalid_json'))
    expect(result.current.isConnected).toBe(true)
    expect(result.current.snapshotError?.status).toBe(200)
  })

  it('does not schedule automatic retries and recovers through manual retry', async () => {
    const intervalSpy = vi.spyOn(window, 'setInterval')
    vi.spyOn(api, 'getHealth').mockResolvedValue({ status: 'ok' })
    const getSnapshot = vi.spyOn(api, 'getSnapshot')
      .mockRejectedValueOnce(new ApiRequestError('failed', 500, 'snapshot_query_failed', 'corr-3'))
      .mockResolvedValueOnce(healthySnapshot)
    const { result } = renderHook(() => useDashboardData(filters))

    await waitFor(() => expect(result.current.snapshotStatus).toBe('error'))
    expect(intervalSpy.mock.calls.some((call) => call[1] === 10_000)).toBe(false)
    await act(async () => { await result.current.refreshNow() })
    await waitFor(() => expect(result.current.data).toBe(healthySnapshot))
    expect(getSnapshot).toHaveBeenCalledTimes(2)
  })
})
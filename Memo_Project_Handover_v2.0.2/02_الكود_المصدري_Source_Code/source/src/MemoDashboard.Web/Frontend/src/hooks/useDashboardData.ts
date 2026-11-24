import { useCallback, useEffect, useRef, useState } from 'react'
import { api, ApiRequestError } from '../services/api'
import { t } from '../i18n'
import type { DashboardFilters, DashboardSnapshot } from '../types/dashboard'

export type SnapshotStatus = 'idle' | 'loading' | 'success' | 'error'

export interface DashboardSnapshotError {
  message: string
  status: number | null
  code: string | null
  correlationId: string | null
}

function classifySnapshotError(error: unknown): DashboardSnapshotError {
  if (error instanceof ApiRequestError) {
    if (error.status === 400) return { message: t('snapshot.invalidRequest'), status: error.status, code: error.code, correlationId: error.correlationId }
    if (error.status === 401) return { message: t('snapshot.unauthorized'), status: error.status, code: error.code, correlationId: error.correlationId }
    if (error.status === 403) return { message: t('snapshot.forbidden'), status: error.status, code: error.code, correlationId: error.correlationId }
    // 1.2.1: حد الطلبات (429) رسالة مفهومة بدل نص الخادم الخام.
    if (error.status === 429) return { message: t('snapshot.rateLimited'), status: error.status, code: error.code, correlationId: error.correlationId }
    if (error.status === 408 || error.status === 504) return { message: t('snapshot.timeout'), status: error.status, code: error.code, correlationId: error.correlationId }
    if (error.kind === 'network') return { message: t('snapshot.networkError'), status: error.status, code: error.code, correlationId: error.correlationId }
    if (error.kind === 'invalid-json' || error.kind === 'invalid-response') return { message: t('snapshot.invalidResponse'), status: error.status, code: error.code, correlationId: error.correlationId }
    return { message: t('snapshot.failedBody'), status: error.status, code: error.code, correlationId: error.correlationId }
  }
  return { message: t('snapshot.failedBody'), status: null, code: null, correlationId: null }
}

export function useDashboardData(filters: DashboardFilters, enabled = true) {
  const [data, setData] = useState<DashboardSnapshot | null>(null)
  const [isConnected, setIsConnected] = useState(true)
  const [isLoading, setIsLoading] = useState(true)
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null)
  const [snapshotError, setSnapshotError] = useState<DashboardSnapshotError | null>(null)
  const [snapshotStatus, setSnapshotStatus] = useState<SnapshotStatus>('idle')
  const [isStale, setIsStale] = useState(false)

  const requestIdRef = useRef(0)
  const abortRef = useRef<AbortController | null>(null)
  const filtersRef = useRef(filters)

  useEffect(() => {
    filtersRef.current = filters
  }, [filters])

  const load = useCallback(async (showSpinner: boolean) => {
    if (!enabled) return
    if (showSpinner) setIsLoading(true)
    setSnapshotStatus('loading')
    setSnapshotError(null)
    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller
    const requestId = ++requestIdRef.current

    const healthRequest = typeof api.getHealth === 'function'
      ? api.getHealth(controller.signal)
      : Promise.resolve({ status: 'ok' })
    const snapshotRequest = api.getSnapshot(filtersRef.current, controller.signal)
    const [healthResult, snapshotResult] = await Promise.allSettled([healthRequest, snapshotRequest])

    if (requestId !== requestIdRef.current || controller.signal.aborted) return

    setIsConnected(healthResult.status === 'fulfilled' && healthResult.value.status !== 'error')

    if (snapshotResult.status === 'rejected') {
      const failure = classifySnapshotError(snapshotResult.reason)
      setSnapshotError(failure)
      setSnapshotStatus('error')
      setIsStale(true)
      setIsLoading(false)
      return
    }

    const snapshot = snapshotResult.value
    if (!snapshot.isHealthy) {
      setSnapshotError({
        message: snapshot.errorMessage ?? t('snapshot.failedBody'),
        status: 200,
        code: 'snapshot_reported_failure',
        correlationId: null,
      })
      setSnapshotStatus('error')
      setIsStale(true)
      setIsLoading(false)
      return
    }

    setData(snapshot)
    setSnapshotStatus('success')
    setIsStale(false)
    setLastUpdated(new Date())
    setSnapshotError(null)
    setIsLoading(false)
  }, [enabled])

  useEffect(() => {
    if (!enabled) {
      abortRef.current?.abort()
      requestIdRef.current += 1
      setData(null)
      setIsLoading(true)
      setIsConnected(true)
      setSnapshotError(null)
      setSnapshotStatus('idle')
      setIsStale(false)
      return
    }
    setData(null)
    void load(true)
    return () => {
      abortRef.current?.abort()
      requestIdRef.current += 1
    }
  }, [enabled, filters, load])

  const refreshNow = useCallback(() => load(true), [load])

  return { data, isConnected, isLoading, lastUpdated, snapshotError, snapshotStatus, isStale, refreshNow }
}
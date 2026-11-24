import type { CompanyMetrics, CompanyMonthlyRow, DashboardFilters, DashboardLookups, DashboardSnapshot, ReviewQueueResult } from '../types/dashboard'
import type {
  CompanyModel,
  FactoryModel,
  ProductionLineModel,
  NoteTypeModel,
  NoteTypeFieldModel,
  NoteTypeFieldDefinitionModel,
  NoteModel,
  NoteDetailsModel,
  PagedResult,
  UserModel,
  RoleModel,
  AuditLogModel,
  IncompleteIndicatorQueueItemModel,
  ReviewQueueItemModel,
  ReportMeta,
  IndicatorValue,
  NoteLinkCandidateModel,
  NoteAttachmentModel,
  ReportIdentitySettingsModel,
  CompanyReportDataModel,
  NoteSourceModel,
  SaveNoteSourceRequest,
  NoteDuplicateModel
} from '../types/memo'
import { t } from '../i18n'

export interface SetupPayload {
  host: string
  port: number
  database: string
  username: string
  organizationName: string
  subOrganizationName: string
  password: string
  dashboardVersion: number
  sslMode: string
  applyMigrations?: boolean
}

export interface SetupConfig {
  host: string
  port: number
  database: string
  username: string
  organizationName?: string | null
  subOrganizationName?: string | null
  dashboardVersion?: number
  sslMode?: string
}

export interface SetupResult {
  success: boolean
  message?: string
  databaseVersion?: string
  applicationVersion?: string
  requiresUpgrade?: boolean
  databaseIsNewer?: boolean
  isCompatible?: boolean
}

let csrfToken: string | null = null
export type ApiRequestFailureKind = 'http' | 'network' | 'invalid-json' | 'invalid-response'

export class ApiRequestError extends Error {
  readonly status: number | null
  readonly code: string | null
  readonly correlationId: string | null
  readonly kind: ApiRequestFailureKind

  constructor(message: string, status: number | null, code: string | null, correlationId: string | null, kind: ApiRequestFailureKind = 'http') {
    super(message)
    this.name = 'ApiRequestError'
    this.status = status
    this.code = code
    this.correlationId = correlationId
    this.kind = kind
  }
}

async function ensureCsrfToken(forceRefresh = false): Promise<string> {
  if (csrfToken && !forceRefresh) return csrfToken
  const response = await fetch('/api/admin/csrf', { credentials: 'same-origin' })
  const body = (await response.json().catch(() => null)) as { token?: string } | null
  if (!response.ok || !body?.token) {
    throw new Error(t('common.requestFailed', { status: response.status }))
  }
  csrfToken = body.token
  return csrfToken
}

async function requestJson<T>(path: string, init?: RequestInit, isRetry = false): Promise<T> {
  const method = (init?.method ?? 'GET').toUpperCase()
  let requestInit = { ...init, credentials: 'same-origin' as RequestCredentials }
  if (!['GET', 'HEAD', 'OPTIONS'].includes(method)) {
    const token = await ensureCsrfToken()
    const headers = new Headers(init?.headers)
    headers.set('X-CSRF-TOKEN', token)
    requestInit = { ...requestInit, headers }
  }

  let response: Response
  try {
    response = await fetch(path, requestInit)
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw new ApiRequestError(t('snapshot.networkError'), null, 'network_error', null, 'network')
  }

  const correlationId = response.headers.get('X-Correlation-ID')
  const raw = await response.text()
  let body: unknown = null
  if (raw.trim()) {
    try {
      body = JSON.parse(raw) as unknown
    } catch {
      throw new ApiRequestError(t('snapshot.invalidResponse'), response.status, 'invalid_json', correlationId, 'invalid-json')
    }
  }

  // If 400 Bad Request due to stale/invalid CSRF token, auto-refresh and retry once
  if (!response.ok && response.status === 400 && !isRetry && !['GET', 'HEAD', 'OPTIONS'].includes(method)) {
    const record = body && typeof body === 'object' ? body as Record<string, unknown> : null
    const msg = typeof record?.message === 'string' ? record.message : ''
    if (msg.includes('حماية') || msg.toLowerCase().includes('csrf') || msg.toLowerCase().includes('antiforgery')) {
      await ensureCsrfToken(true)
      return requestJson<T>(path, init, true)
    }
  }

  if (!response.ok) {
    const record = body && typeof body === 'object' ? body as Record<string, unknown> : null
    const message = typeof record?.message === 'string'
      ? record.message
      : typeof record?.title === 'string'
        ? record.title
        : typeof record?.detail === 'string'
          ? record.detail
          : t('common.requestFailed', { status: response.status })
    const code = typeof record?.code === 'string' ? record.code : null
    throw new ApiRequestError(message, response.status, code, correlationId, 'http')
  }

  if (response.status === 204) return undefined as T
  if (body === null) {
    throw new ApiRequestError(t('snapshot.invalidResponse'), response.status, 'empty_response', correlationId, 'invalid-response')
  }
  return body as T
}
export function buildFilterQuery(filters: DashboardFilters): string {
  const params = new URLSearchParams()
  if (filters.from) params.set('from', filters.from)
  if (filters.to) params.set('to', filters.to)
  if (filters.dateBasis) params.set('dateBasis', filters.dateBasis)
  if (filters.companyId) params.set('companyId', filters.companyId)
  if (filters.factoryId) params.set('factoryId', filters.factoryId)
  if (filters.productionLineId) params.set('productionLineId', filters.productionLineId)
  if (filters.noteTypeId) params.set('noteTypeId', filters.noteTypeId)
  if (filters.status) params.set('status', filters.status)
  if (filters.entryCutoffDate) params.set('entryCutoffDate', filters.entryCutoffDate)
  const query = params.toString()
  return query ? `?${query}` : ''
}

export interface CompanyReportIssueParams {
  title?: string
  letterNumber?: string
  city?: string
  hijriDate?: string
  gregorianDate?: string
  recipient?: string
  issuerName?: string
  formEfficiency?: string
}

export function buildCompanyReportUrl(kind: 'pdf' | 'xlsx' | 'docx' | 'html', filters: DashboardFilters, issue: CompanyReportIssueParams = {}): string {
  const params = new URLSearchParams(buildFilterQuery(filters).replace(/^\?/, ''))
  params.delete('status')
  params.delete('factoryId')
  params.delete('productionLineId')
  params.delete('noteTypeId')
  if (issue.title) params.set('title', issue.title)
  if (issue.letterNumber) params.set('letterNumber', issue.letterNumber)
  if (issue.city) params.set('city', issue.city)
  if (issue.hijriDate) params.set('hijriDate', issue.hijriDate)
  if (issue.gregorianDate) params.set('gregorianDate', issue.gregorianDate)
  if (issue.recipient) params.set('recipient', issue.recipient)
  if (issue.issuerName) params.set('issuerName', issue.issuerName)
  if (issue.formEfficiency) params.set('formEfficiency', issue.formEfficiency)
  return `/api/dashboard/company-report.${kind}?${params}`
}

export async function downloadCompanyReport(kind: 'pdf' | 'xlsx' | 'docx', filters: DashboardFilters, issue: CompanyReportIssueParams = {}): Promise<void> {
  const response = await fetch(buildCompanyReportUrl(kind, filters, issue), { credentials: 'same-origin' })
  if (!response.ok) {
    throw new Error(t('report.downloadFailed'))
  }
  const blob = await response.blob()
  const objectUrl = URL.createObjectURL(blob)
  try {
    const anchor = document.createElement('a')
    anchor.href = objectUrl
    anchor.download = `company-report.${kind}`
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
  } finally {
    URL.revokeObjectURL(objectUrl)
  }
}

export interface AdminSession {
  authenticated: boolean
  mustChangePassword?: boolean
  message?: string
  id?: string
  username?: string
  fullName?: string | null
  role?: string
}


export interface ServerHealth {
  status?: string
  database?: string
  message?: string
}
export interface UpdateHealth {
  service?: string
  status?: string
  configured?: boolean
  publishingConfigured?: boolean
  checkInConfigured?: boolean
  publicBaseUrlValid?: boolean
}
export const api = {
  getSnapshot(filters: DashboardFilters, signal?: AbortSignal): Promise<DashboardSnapshot> {
    return requestJson<DashboardSnapshot>(`/api/dashboard/snapshot${buildFilterQuery(filters)}`, { signal })
  },
  getLookups(signal?: AbortSignal): Promise<DashboardLookups> {
    return requestJson<DashboardLookups>('/api/dashboard/lookups', { signal })
  },
  getHealth(signal?: AbortSignal): Promise<ServerHealth> {
    return requestJson<ServerHealth>('/health', { signal })
  },
  getCompanyMetrics(companyId: string, from: string, to: string, signal?: AbortSignal, entryCutoffDate?: string): Promise<CompanyMetrics> {
    const params = new URLSearchParams({ companyId })
    if (from) params.set('from', from)
    if (to) params.set('to', to)
    if (entryCutoffDate) params.set('entryCutoffDate', entryCutoffDate)
    return requestJson<CompanyMetrics>(`/api/dashboard/company-metrics?${params}`, { signal })
  },
  getReviewQueue(filters: DashboardFilters, limit = 50, offset = 0, signal?: AbortSignal): Promise<ReviewQueueResult> {    const params = new URLSearchParams(buildFilterQuery(filters).replace(/^\?/, ''))
    params.delete('status')
    params.set('limit', String(Math.min(Math.max(limit, 1), 200)))
    params.set('offset', String(Math.max(0, offset)))
    return requestJson<ReviewQueueResult>(`/api/dashboard/review-queue?${params}`, { signal })
  },
  getCompanyMonthly(
    companyId: string,
    scope: Pick<DashboardFilters, 'from' | 'to' | 'dateBasis' | 'entryCutoffDate'>,
    signal?: AbortSignal,
  ): Promise<CompanyMonthlyRow[]> {
    const params = new URLSearchParams({ companyId })
    if (scope.from) params.set('from', scope.from)
    if (scope.to) params.set('to', scope.to)
    if (scope.dateBasis) params.set('dateBasis', scope.dateBasis)
    if (scope.entryCutoffDate) params.set('entryCutoffDate', scope.entryCutoffDate)
    return requestJson<CompanyMonthlyRow[]>(`/api/dashboard/company-monthly?${params}`, { signal })
  },
getSetupStatus(): Promise<{ isConfigured: boolean; internalNetworkSetup?: boolean }> {
    return requestJson<{ isConfigured: boolean; internalNetworkSetup?: boolean }>('/api/setup/status')
  },
  getSetupConfig(): Promise<SetupConfig> {
    return requestJson<SetupConfig>('/api/setup/config')
  },
  testConnection(payload: SetupPayload, setupToken?: string): Promise<SetupResult> {
    return requestJson<SetupResult>('/api/setup/test-connection', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...(setupToken ? { 'X-Setup-Token': setupToken } : {}) },
      body: JSON.stringify(payload),
    })
  },
  saveSetup(payload: SetupPayload, setupToken?: string): Promise<SetupResult> {
    return requestJson<SetupResult>('/api/setup/save', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...(setupToken ? { 'X-Setup-Token': setupToken } : {}) },
      body: JSON.stringify(payload),
    })
  },
  applyMigrations(setupToken?: string): Promise<SetupResult> {
    return requestJson<SetupResult>('/api/setup/apply-migrations', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...(setupToken ? { 'X-Setup-Token': setupToken } : {}) },
    })
  },
  getDatabaseStatus(): Promise<{ databaseVersion: string; applicationVersion: string; requiresUpgrade: boolean; databaseIsNewer: boolean; isCompatible: boolean }> {
    return requestJson('/api/admin/database-status')
  },
  applyAdminMigrations(): Promise<{ success: boolean; databaseVersion?: string; applicationVersion?: string; message?: string }> {
    return requestJson('/api/admin/apply-migrations', { method: 'POST' })
  },
  getAdminSession(): Promise<AdminSession> {
    return requestJson<AdminSession>('/api/admin/session')
  },
  async loginAdmin(username: string, password: string): Promise<AdminSession> {
    csrfToken = null
    const res = await requestJson<AdminSession>('/api/admin/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    })
    csrfToken = null
    return res
  },
  async logoutAdmin(): Promise<void> {
    csrfToken = null
    await requestJson<void>('/api/admin/logout', { method: 'POST' })
    csrfToken = null
  },
  getUpdateHealth(): Promise<UpdateHealth> {
    return requestJson<UpdateHealth>('/api/admin/updates/health')
  },
  publishUpdate(form: FormData): Promise<Record<string, unknown>> {
    return requestJson<Record<string, unknown>>('/api/admin/updates/releases', { method: 'POST', body: form })
  },
  changePassword(currentPassword: string, newPassword: string, username?: string): Promise<{ success: boolean; message: string }> {
    return requestJson<{ success: boolean; message: string }>('/api/auth/change-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ currentPassword, newPassword, username }),
    })
  },

  // ---------------- Master Data ----------------
  getCompanies(): Promise<CompanyModel[]> {
    return requestJson<CompanyModel[]>('/api/master/companies')
  },
  getCompany(id: string): Promise<CompanyModel> {
    return requestJson<CompanyModel>(`/api/master/companies/${id}`)
  },
  saveCompany(model: Partial<CompanyModel>): Promise<CompanyModel> {
    return requestJson<CompanyModel>('/api/master/companies', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(model),
    })
  },
  createCompany(model: Partial<CompanyModel>): Promise<CompanyModel> {
    return this.saveCompany(model)
  },
  updateCompany(id: string, model: Partial<CompanyModel>): Promise<CompanyModel> {
    return requestJson<CompanyModel>(`/api/master/companies/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(model),
    })
  },
  deleteCompany(id: string): Promise<void> {
    return requestJson<void>(`/api/master/companies/${id}`, { method: 'DELETE' })
  },

  getFactories(companyId?: string): Promise<FactoryModel[]> {
    const q = companyId ? `?companyId=${companyId}` : ''
    return requestJson<FactoryModel[]>(`/api/master/factories${q}`)
  },
  saveFactory(model: Partial<FactoryModel>): Promise<FactoryModel> {
    return requestJson<FactoryModel>('/api/master/factories', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(model),
    })
  },
  createFactory(model: Partial<FactoryModel>): Promise<FactoryModel> {
    return this.saveFactory(model)
  },
  updateFactory(id: string, model: Partial<FactoryModel>): Promise<FactoryModel> {
    return requestJson<FactoryModel>(`/api/master/factories/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(model),
    })
  },
  deleteFactory(id: string): Promise<void> {
    return requestJson<void>(`/api/master/factories/${id}`, { method: 'DELETE' })
  },

  getProductionLines(factoryId?: string): Promise<ProductionLineModel[]> {
    const q = factoryId ? `?factoryId=${factoryId}` : ''
    return requestJson<ProductionLineModel[]>(`/api/master/production-lines${q}`)
  },
  saveProductionLine(model: Partial<ProductionLineModel>): Promise<ProductionLineModel> {
    return requestJson<ProductionLineModel>('/api/master/production-lines', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(model),
    })
  },
  createProductionLine(model: Partial<ProductionLineModel>): Promise<ProductionLineModel> {
    return this.saveProductionLine(model)
  },
  updateProductionLine(id: string, model: Partial<ProductionLineModel>): Promise<ProductionLineModel> {
    return requestJson<ProductionLineModel>(`/api/master/production-lines/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(model),
    })
  },
  deleteProductionLine(id: string): Promise<void> {
    return requestJson<void>(`/api/master/production-lines/${id}`, { method: 'DELETE' })
  },
  toggleProductionLine(id: string, isActive: boolean): Promise<void> {
    return requestJson<void>(`/api/master/production-lines/${id}/toggle`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ isActive }),
    })
  },

  getNoteTypes(): Promise<NoteTypeModel[]> {
    return requestJson<NoteTypeModel[]>('/api/master/note-types')
  },
  getNoteTypeFields(noteTypeId: string): Promise<NoteTypeFieldModel[]> {
    return requestJson<NoteTypeFieldModel[]>(`/api/master/note-types/${noteTypeId}/fields`)
  },
  getFieldDefinitions(noteTypeId: string): Promise<NoteTypeFieldModel[]> {
    return this.getNoteTypeFields(noteTypeId)
  },
  previewFieldDefinition(definition: NoteTypeFieldDefinitionModel): Promise<Record<string, unknown>> {
    return requestJson<Record<string, unknown>>('/api/master/note-types/fields/preview', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(definition),
    })
  },
  saveFieldDefinition(definition: NoteTypeFieldDefinitionModel): Promise<NoteTypeFieldDefinitionModel> {
    return requestJson<NoteTypeFieldDefinitionModel>('/api/master/note-types/fields', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(definition),
    })
  },
  deleteFieldDefinition(fieldId: string): Promise<void> {
    return requestJson<void>(`/api/master/note-types/fields/${fieldId}`, { method: 'DELETE' })
  },

  // ---------------- Notes / Memos ----------------
  searchNotes(filters: Record<string, unknown>): Promise<PagedResult<NoteDetailsModel>> {
    const params = new URLSearchParams()
    for (const [key, value] of Object.entries(filters)) {
      if (value !== undefined && value !== null && value !== '') {
        params.set(key, String(value))
      }
    }
    return requestJson<PagedResult<NoteDetailsModel>>(`/api/notes?${params}`)
  },
  getNote(id: string): Promise<NoteModel> {
    return requestJson<NoteModel>(`/api/notes/${id}`)
  },
  getNoteById(id: string): Promise<NoteModel> {
    return this.getNote(id)
  },
  saveNote(request: { note: NoteModel; saveAsDraft?: boolean; relatedNoteId?: string; markAllIndicatorsCorrect?: boolean; relatedNoteIds?: string[] }): Promise<{ id: string; message: string }> {
    return requestJson<{ id: string; message: string }>('/api/notes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    })
  },
  updateNote(id: string, request: { note: NoteModel; saveAsDraft?: boolean; relatedNoteId?: string; markAllIndicatorsCorrect?: boolean; relatedNoteIds?: string[] }): Promise<{ id: string; message: string }> {
    return requestJson<{ id: string; message: string }>(`/api/notes/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    })
  },
  deleteNote(id: string, expectedUpdatedAt: string): Promise<void> {
    return requestJson<void>(`/api/notes/${id}?expectedUpdatedAt=${encodeURIComponent(expectedUpdatedAt)}`, { method: 'DELETE' })
  },
  startReview(id: string, expectedUpdatedAt?: string): Promise<void> {
    return requestJson<void>(`/api/notes/${id}/start-review`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ expectedUpdatedAt }),
    })
  },
  approveNote(id: string, expectedUpdatedAt?: string): Promise<void> {
    return requestJson<void>(`/api/notes/${id}/approve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ expectedUpdatedAt }),
    })
  },
  returnNote(id: string, reason: string, expectedUpdatedAt?: string): Promise<void> {
    return requestJson<void>(`/api/notes/${id}/return`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reason, expectedUpdatedAt }),
    })
  },
  releaseReview(id: string, reason: string): Promise<void> {
    return requestJson<void>(`/api/notes/${id}/release-review`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reason }),
    })
  },
  getReviewQueueItems(): Promise<ReviewQueueItemModel[]> {
    return requestJson<ReviewQueueItemModel[]>('/api/notes/review-queue')
  },
  getIncompleteIndicators(filters: Record<string, unknown>): Promise<PagedResult<IncompleteIndicatorQueueItemModel>> {
    const params = new URLSearchParams()
    for (const [key, value] of Object.entries(filters)) {
      if (value !== undefined && value !== null && value !== '') {
        params.set(key, String(value))
      }
    }
    return requestJson<PagedResult<IncompleteIndicatorQueueItemModel>>(`/api/notes/incomplete-indicators?${params}`)
  },
  completeIndicator(request: { noteId: string; fieldId: string; value: IndicatorValue; expectedUpdatedAt: string; reason?: string }): Promise<void> {
    return requestJson<void>('/api/notes/incomplete-indicators/complete', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    })
  },
  checkDuplicates(params: { noteNumber: string; noteTypeId: string; companyId: string; writingDate?: string; currentNoteId?: string; isInvoice?: boolean }): Promise<NoteDuplicateModel[]> {
    const q = new URLSearchParams({
      noteNumber: params.noteNumber,
      noteTypeId: params.noteTypeId,
      companyId: params.companyId,
    })
    if (params.writingDate) q.set('writingDate', params.writingDate)
    if (params.currentNoteId) q.set('currentNoteId', params.currentNoteId)
    if (params.isInvoice !== undefined) q.set('isInvoice', String(params.isInvoice))
    return requestJson<NoteDuplicateModel[]>(`/api/notes/duplicates/check?${q}`)
  },
  resolveDuplicate(request: Record<string, unknown>): Promise<Record<string, unknown>> {
    return requestJson<Record<string, unknown>>('/api/notes/duplicates/resolve', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    })
  },
  getLinkCandidates(id: string, includeUnlinked = false): Promise<NoteLinkCandidateModel[]> {
    return requestJson<NoteLinkCandidateModel[]>(`/api/notes/${id}/link-candidates?includeUnlinked=${includeUnlinked}`)
  },
  getAvailableLinkCandidates(companyId: string, noteTypeId: string, currentNoteId?: string): Promise<NoteLinkCandidateModel[]> {
    const q = new URLSearchParams({ companyId, noteTypeId })
    if (currentNoteId) q.set('currentNoteId', currentNoteId)
    return requestJson<NoteLinkCandidateModel[]>(`/api/notes/link-candidates?${q}`)
  },
  linkNotes(id: string, relatedNoteId: string | string[]): Promise<void> {
    const payload = Array.isArray(relatedNoteId)
      ? { relatedNoteIds: relatedNoteId }
      : { relatedNoteId };
    return requestJson<void>(`/api/notes/${id}/link`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
  },
  unlinkNotes(id: string, relatedNoteId: string): Promise<void> {
    return requestJson<void>(`/api/notes/${id}/unlink/${relatedNoteId}`, { method: 'DELETE' })
  },
  getNoteSource(id: string): Promise<NoteSourceModel | null> {
    return requestJson<NoteSourceModel | null>(`/api/notes/${id}/source`)
  },
  saveNoteSource(id: string, request: SaveNoteSourceRequest): Promise<{ message: string }> {
    return requestJson<{ message: string }>(`/api/notes/${id}/source`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    })
  },
  removeNoteSource(id: string): Promise<{ message: string }> {
    return requestJson<{ message: string }>(`/api/notes/${id}/source`, { method: 'DELETE' })
  },
  getNoteSourceInvoiceAttachmentUrl(id: string, invoiceId: string, download = false): string {
    return `/api/notes/${id}/source/invoices/${invoiceId}/attachment${download ? '?download=true' : ''}`
  },
  getAttachmentUrl(id: string, attachmentId?: string, download = false): string {
    const base = attachmentId
      ? `/api/notes/${id}/attachments/${attachmentId}`
      : `/api/notes/${id}/attachment`
    return download ? `${base}?download=true` : base
  },
  getNoteAttachments(id: string): Promise<NoteAttachmentModel[]> {
    return requestJson<NoteAttachmentModel[]>(`/api/notes/${id}/attachments`)
  },
  async uploadAttachment(id: string, file: File): Promise<{ fileName: string; contentType: string; size: number; message: string }> {
    const form = new FormData()
    form.append('file', file)
    const token = await ensureCsrfToken()
    const response = await fetch(`/api/notes/${id}/attachment`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-CSRF-TOKEN': token },
      body: form,
    })
    if (!response.ok) {
      const err = await response.json().catch(() => ({})) as { message?: string }
      throw new Error(err.message || 'فشل رفع المرفق.')
    }
    return response.json()
  },
  async uploadNoteAttachments(id: string, files: File[]): Promise<{ uploaded: Array<{ id: string; fileName: string; contentType: string; fileSize: number }>; message: string }> {
    const form = new FormData()
    for (const f of files) {
      form.append('files', f)
    }
    const token = await ensureCsrfToken()
    const response = await fetch(`/api/notes/${id}/attachments`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-CSRF-TOKEN': token },
      body: form,
    })
    if (!response.ok) {
      const err = await response.json().catch(() => ({})) as { message?: string }
      throw new Error(err.message || 'فشل رفع المرفقات.')
    }
    return response.json()
  },
  deleteNoteAttachment(id: string, attachmentId: string): Promise<void> {
    return requestJson<void>(`/api/notes/${id}/attachments/${attachmentId}`, { method: 'DELETE' })
  },

  // ---------------- Reports ----------------
  getReportsCatalog(): Promise<ReportMeta[]> {
    return requestJson<ReportMeta[]>('/api/reports')
  },
  getReportData(key: string, filter: Record<string, unknown>): Promise<{ reportKey: string; title: string; columns: { key: string; name: string; caption: string; type: string }[]; rows: Record<string, unknown>[]; rowCount: number }> {
    return requestJson(`/api/reports/${key}/data`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(filter),
    })
  },
  async exportReportExcel(key: string, filter: Record<string, unknown>): Promise<void> {
    const token = await ensureCsrfToken()
    const response = await fetch(`/api/reports/${key}/export/excel`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
      body: JSON.stringify(filter),
    })
    if (!response.ok) throw new Error('فشل تصدير ملف Excel.')
    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${key}_${new Date().toISOString().slice(0, 10)}.xlsx`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  },
  async exportReportPdf(key: string, filter: Record<string, unknown>): Promise<void> {
    const token = await ensureCsrfToken()
    const response = await fetch(`/api/reports/${key}/export/pdf`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
      body: JSON.stringify(filter),
    })
    if (!response.ok) throw new Error('فشل تصدير ملف PDF.')
    const blob = await response.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${key}_${new Date().toISOString().slice(0, 10)}.pdf`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  },
  getReportIdentitySettings(): Promise<ReportIdentitySettingsModel> {
    return requestJson<ReportIdentitySettingsModel>('/api/reports/identity-settings')
  },
  async saveReportIdentitySettings(settings: ReportIdentitySettingsModel): Promise<void> {
    const token = await ensureCsrfToken()
    await requestJson('/api/reports/identity-settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
      body: JSON.stringify(settings),
    })
  },
  getCompanyReportData(params: Record<string, string>): Promise<CompanyReportDataModel> {
    const q = new URLSearchParams(params).toString()
    return requestJson<CompanyReportDataModel>(`/api/dashboard/company-report.data?${q}`)
  },

  // ---------------- Admin ----------------
  getUsers(): Promise<UserModel[]> {
    return requestJson<UserModel[]>('/api/admin/users')
  },
  getRoles(): Promise<RoleModel[]> {
    return requestJson<RoleModel[]>('/api/admin/roles')
  },
  saveUser(user: Partial<UserModel>, newPassword?: string): Promise<void> {
    return requestJson<void>('/api/admin/users', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ user, newPassword }),
    })
  },
  updateUser(id: string, user: Partial<UserModel>, newPassword?: string): Promise<void> {
    return requestJson<void>(`/api/admin/users/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ user, newPassword }),
    })
  },
  resetPassword(userId: string, newPassword: string): Promise<void> {
    return requestJson<void>(`/api/admin/users/${userId}/reset-password`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ newPassword }),
    })
  },
  getAuditLogs(filters: Record<string, unknown>): Promise<PagedResult<AuditLogModel>> {
    const params = new URLSearchParams()
    for (const [key, value] of Object.entries(filters)) {
      if (value !== undefined && value !== null && value !== '') {
        params.set(key, String(value))
      }
    }
    return requestJson<PagedResult<AuditLogModel>>(`/api/admin/audit-logs?${params}`)
  },
  getAuditActions(): Promise<string[]> {
    return requestJson<string[]>('/api/admin/audit-actions')
  },
  getSystemMode(): Promise<{ mode: string }> {
    return requestJson<{ mode: string }>('/api/admin/system-mode')
  },
  setSystemMode(mode: 'SERVER' | 'LOCAL_READONLY'): Promise<void> {
    return requestJson<void>('/api/admin/system-mode', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ mode: mode === 'SERVER' ? 0 : 1 }),
    })
  },
}

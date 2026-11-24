export interface DashboardSummary {
  totalNotes: number
  notesToday: number
  notesCurrentMonth: number
  activeCompaniesTotal: number
  companiesWithNotes: number
  approved: number
  pendingReview: number
  returnedForCorrection: number
  companyResend: number
  duplicateNumbers: number
  duplicateIncidentCount: number
  duplicateAffectedNotes: number
  nonDuplicateNotes: number
  lateTotal: number
  lateSameMonth: number
  lateCrossMonth: number
  onTime: number
  avgLateDays: number | null
  maxLateDays: number | null
  latestArrivalDate: string | null
}

export interface CompanyTopItem {
  id: string
  name: string
  noteCount: number
}

export interface MonthlyActivityItem {
  month: string
  monthLabel: string
  count: number
}

export interface StatusDistributionItem {
  status: string
  statusLabel: string
  count: number
}

export interface DuplicateBreakdownItem {
  classification: string
  label: string
  count: number
}

export interface RecentNoteItem {
  id: string
  noteNumber: string
  companyName: string | null
  factoryName: string | null
  productionLineName: string | null
  noteTypeName: string | null
  status: string
  statusLabel: string
  writingDate: string | null
  arrivalAt: string | null
  isLate: boolean
}

export interface SlaStats {
  eligibleNotes: number
  onTimeNotes: number
  lateNotes: number
  onTimeRate: number | null
  invalidDateOrderNotes: number
}

export interface BacklogAgeItem {
  code: string
  label: string
  value: number
}

export interface IncomingVsApprovedItem {
    periodKey: string
  periodLabel: string
label: string
  incoming: number
  approved: number
}

export interface QualityByCompanyItem {
    companyId: string
  companyName: string
label: string
  sampleSize: number
  correctCount?: number
  errorCount?: number
  qualityEligibleCount?: number
  allNotApplicableCount?: number
  accuracyRate: number | null
  errorRate: number | null
  totalFieldCount?: number
  correctFieldCount?: number
  errorFieldCount?: number
  unknownFieldCount?: number
  fieldAccuracyRate?: number | null
}

export interface FieldQualitySummary {
  totalFieldCount: number
  correctFieldCount: number
  errorFieldCount: number
  unknownFieldCount: number
  accuracyRate: number | null
}

export interface TopErrorFieldItem {
    fieldId: string
  fieldName: string
label: string
  sampleSize: number
  errorCount: number
  errorRate: number | null
  totalFieldCount?: number
  correctFieldCount?: number
  errorFieldCount?: number
  unknownFieldCount?: number
  fieldAccuracyRate?: number | null
}

export interface IndicatorStateItem {
    stateLabel: string
code: number
  value: number
}

export interface PerformanceItem {
    dimensionId: string
label: string
  totalNotes: number
  eligibleCount: number
  lateCount: number
  onTimeCount: number
  sampleCount: number
  correctCount: number
  errorCount: number
  qualityEligibleCount: number
  lateRate: number | null
  accuracyRate: number | null
}

export interface DuplicateTrendItem {
    periodKey: string
  periodLabel: string
label: string
  companyResend: number
  duplicateNumber: number
}

export interface DataCompletenessItem {
  code: string
  label: string
  value: number
}

export interface DashboardEffectiveFilters {
  from: string | null
  to: string | null
  dateBasis: 'WritingDate' | 'ArrivalDate'
  companyId: string | null
  factoryId: string | null
  productionLineId: string | null
  noteTypeId: string | null
  status: string | null
  entryCutoffDate: string | null
}
export interface DashboardPeriod {
  from: string
  to: string | null
  dateBasis: 'WritingDate' | 'ArrivalDate'
  entryCutoffDate?: string | null
}
export interface DashboardContractPeriod {
  from: string | null
  to: string | null
  dateBasis: 'WritingDate' | 'ArrivalDate'
  entryCutoffDate: string | null
}

export interface DashboardContractScope {
  companyId: string | null
  factoryId: string | null
  productionLineId: string | null
  noteTypeId: string | null
  status: string | null
}

export interface DashboardDataAvailabilityContract {
  state: 'available' | 'no-data'
  hasRows: boolean
  explanation: string | null
}

export interface ExecutiveDashboardContract {
  availability: DashboardDataAvailabilityContract
  summary: DashboardSummary
  sla: SlaStats
  metrics: CompanyMetrics | null
  fieldQuality: FieldQualitySummary | null
  companyComparison: CompanyTopItem[]
  timelinessByCompany: PerformanceItem[]
  monthlyTrend: MonthlyActivityItem[]
  statusDistribution: StatusDistributionItem[]
  qualityByCompany: QualityByCompanyItem[]
  highImpactProblems: TopErrorFieldItem[]
}

export interface OperationsDashboardContract {
  availability: DashboardDataAvailabilityContract
  summary: DashboardSummary
  sla: SlaStats
  metrics: CompanyMetrics | null
  incomingVsApproved: IncomingVsApprovedItem[]
  backlogByAge: BacklogAgeItem[]
  workflowStatus: StatusDistributionItem[]
  performanceByFactory: PerformanceItem[]
  performanceByLine: PerformanceItem[]
  repetitionClassification: DuplicateBreakdownItem[]
  repetitionTrend: DuplicateTrendItem[]
  indicatorStates: IndicatorStateItem[]
  dataCompleteness: DataCompletenessItem[]
  topErrorFields: TopErrorFieldItem[]
  recentNotes: RecentNoteItem[]
}

export interface CompanyComparisonContract {
  itemQualityDeltaPoints: number | null
  timeComplianceDeltaPoints: number | null
  totalNotesDelta: number | null
}

export interface CompanyDashboardContract {
  availability: DashboardDataAvailabilityContract
  company: CompanyTopItem | null
  metrics: CompanyMetrics | null
  previousMetrics: CompanyMetrics | null
  previousPeriod: DashboardPeriod | null
  comparison: CompanyComparisonContract | null
  monthlyTrend: MonthlyActivityItem[]
  statusDistribution: StatusDistributionItem[]
  incomingVsApproved: IncomingVsApprovedItem[]
  repetitionClassification: DuplicateBreakdownItem[]
  repetitionTrend: DuplicateTrendItem[]
  performanceByFactory: PerformanceItem[]
  performanceByLine: PerformanceItem[]
  qualityByCompany: QualityByCompanyItem[]
  topErrorFields: TopErrorFieldItem[]
  recentNotes: RecentNoteItem[]
}

export interface DashboardViewsContract {
  context: {
    period: DashboardContractPeriod
    scope: DashboardContractScope
  }
  executiveSummary: ExecutiveDashboardContract
  operationsSummary: OperationsDashboardContract
  companySummary: CompanyDashboardContract | null
}
export interface DashboardSnapshot {
  summary: DashboardSummary
  companies: CompanyTopItem[]
  timelinessByCompany: PerformanceItem[]
  monthlyActivity: MonthlyActivityItem[]
  statusDistribution: StatusDistributionItem[]
  duplicateBreakdown: DuplicateBreakdownItem[]
  recentNotes: RecentNoteItem[]
  isHealthy: boolean
  serverTime: string
  generatedAt: string
  minSample: number
  sla: SlaStats
  backlogByAge: BacklogAgeItem[]
  incomingVsApproved: IncomingVsApprovedItem[]
  qualityByCompany: QualityByCompanyItem[]
  topErrorFields: TopErrorFieldItem[]
  fieldQuality: FieldQualitySummary | null
  indicatorStateDistribution: IndicatorStateItem[]
  performanceByFactory: PerformanceItem[]
  performanceByLine: PerformanceItem[]
  duplicateTrend: DuplicateTrendItem[]
  dataCompleteness: DataCompletenessItem[]
  effectiveFilters: DashboardEffectiveFilters
  companyMetrics?: CompanyMetrics | null
  previousCompanyMetrics?: CompanyMetrics | null
  previousPeriod?: DashboardPeriod | null
  views: DashboardViewsContract
  errorMessage?: string
}

export interface CompanyLookup {
  id: string
  name: string
}

export interface FactoryLookup {
  id: string
  companyId: string
  name: string
}

export interface ProductionLineLookup {
  id: string
  factoryId: string
  name: string
}

export interface NoteTypeLookup {
  id: string
  code: string
  nameAr: string
}

export interface DashboardLookups {
  companies: CompanyLookup[]
  factories: FactoryLookup[]
  productionLines: ProductionLineLookup[]
  noteTypes: NoteTypeLookup[]
}

export interface DashboardFilters {
  from: string
  to: string
  dateBasis: 'WritingDate' | 'ArrivalDate'
  companyId: string
  factoryId: string
  productionLineId: string
  noteTypeId: string
  status: string
  entryCutoffDate?: string
}

export interface ReviewQueueItem {
  id: string
  noteNumber: string
  companyId: string | null
  companyName: string | null
  factoryName: string | null
  productionLineName: string | null
  noteTypeName: string | null
  status: string
  statusLabel: string
  writingDate: string | null
  arrivalAt: string | null
  createdAt: string | null
  daysInQueue: number | null
  isLate: boolean
}

export interface ReviewQueueResult {
  items: ReviewQueueItem[]
  total: number
  limit: number
  offset: number
}

export interface CompanyMonthlyRow {
  monthStart: string
  periodLabel: string
  totalNotes: number
  itemQualityPercentage: number | null
  cleanReviewedNotePercent: number | null
  cleanReviewedNotes: number
}
export interface CompanyMetrics {
  totalNotes: number
  eligibleCount: number
  lateCount: number
  onTimeCount: number
  sampleCount: number
  correctCount: number
  errorCount: number
  qualityEligibleCount: number
  expectedItems: number
  reviewedItems: number
  unreviewedItems: number
  applicableReviewedItems: number
  correctItems: number
  errorItems: number
  notApplicableItems: number
  reviewCoveragePercentage: number | null
  itemQualityPercentage: number | null
  errorsPer100Checked: number | null
  qualityEligibleNoteCount: number
  cleanReviewedNotes: number
  allNotApplicableNoteCount: number
  noExpectedItemsNoteCount: number
  cleanReviewedNotePercent: number | null
  errorNotes: number
  incompleteReviewNotes: number
  timeEligibleNotes: number
  onTimeNotes: number
  lateNotes: number
  late6To10Notes: number
  late11To20Notes: number
  lateOver20Notes: number
  missingWritingDateNotes: number
  invalidDateOrderNotes: number
  timeCompliancePercentage: number | null
  averageLateDays: number | null
  medianLateDays: number | null
  p90LateDays: number | null
  maximumLateDays: number | null
  averageExcessDelayDays: number | null
  duplicateNumberIncidents: number
  companyResendIncidents: number
  duplicateIncidents: number
  affectedNotes: number
  incidentsPer100Notes: number | null
  affectedNoteRatePercentage: number | null
  duplicateFreePercentage: number | null
  entryNotes: number
  linkedEntryNotes: number
  unlinkedEntryNotes: number
  linkEfficiencyPercentage: number | null
}


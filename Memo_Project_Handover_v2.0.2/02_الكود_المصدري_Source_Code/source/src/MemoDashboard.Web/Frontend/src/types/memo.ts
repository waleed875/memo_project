export type UserRole = 'ADMIN' | 'DATA_ENTRY' | 'REVIEWER' | 'VIEWER';

export type NoteStatus =
  | 'DRAFT'
  | 'COMPLETE'
  | 'HAS_ERRORS'
  | 'NEEDS_REVIEW'
  | 'UNDER_REVIEW'
  | 'RETURNED_FOR_CORRECTION'
  | 'APPROVED';

export const NOTE_STATUS_LABELS: Record<NoteStatus, string> = {
  DRAFT: 'مسودة',
  COMPLETE: 'مكتملة',
  HAS_ERRORS: 'تحتوي أخطاء',
  NEEDS_REVIEW: 'تحتاج مراجعة',
  UNDER_REVIEW: 'قيد التدقيق',
  RETURNED_FOR_CORRECTION: 'معادة للتصحيح',
  APPROVED: 'معتمدة',
};

export function normalizeNoteStatus(val: unknown): NoteStatus {
  if (val === null || val === undefined) return 'DRAFT';
  if (typeof val === 'number' || (typeof val === 'string' && /^\d+$/.test(val.trim()))) {
    const num = Number(val);
    switch (num) {
      case 0: return 'DRAFT';
      case 1: return 'COMPLETE';
      case 2: return 'HAS_ERRORS';
      case 3: return 'NEEDS_REVIEW';
      case 4: return 'UNDER_REVIEW';
      case 5: return 'RETURNED_FOR_CORRECTION';
      case 6: return 'APPROVED';
      default: return 'DRAFT';
    }
  }
  const str = String(val).toUpperCase().trim();
  const validStatuses: NoteStatus[] = [
    'DRAFT', 'COMPLETE', 'HAS_ERRORS', 'NEEDS_REVIEW', 'UNDER_REVIEW', 'RETURNED_FOR_CORRECTION', 'APPROVED'
  ];
  if (validStatuses.includes(str as NoteStatus)) {
    return str as NoteStatus;
  }
  return 'DRAFT';
}

export interface NoteAttachmentModel {
  id: string;
  noteId?: string;
  fileName: string;
  contentType: string;
  fileSize: number;
  createdAt: string;
  createdBy?: string | null;
}

export enum IndicatorValue {
  Unreviewed = 0,
  Correct = 1,
  Incorrect = 2,
  Missing = 3,
  Unclear = 4,
  Incomplete = 5,
  NotApplicable = 6
}

export interface NoteFieldValueModel {
  id: string;
  noteId: string;
  fieldId: string;
  fieldName: string;
  fieldValue: IndicatorValue;
  inactiveReason?: string | null;
}

export interface NoteReviewHistoryModel {
  action: string;
  reason?: string | null;
  userName?: string | null;
  userRoleName?: string | null;
  createdAt: string;
}

export interface NoteModel {
  id: string;
  noteNumber: string;
  noteTypeId: string;
  noteTypeName: string;
  companyId: string;
  companyName: string;
  factoryId?: string | null;
  factoryName?: string | null;
  productionLineId?: string | null;
  productionLineName?: string | null;
  writingDate?: string | null;
  arrivalAt: string;
  imagePath?: string | null;
  filePath?: string | null;
  attachmentFileName?: string | null;
  attachmentContentType?: string | null;
  notes?: string | null;
  status: NoteStatus;
  createdBy?: string | null;
  createdByName?: string | null;
  createdAt: string;
  updatedBy?: string | null;
  updatedAt: string;
  approvedBy?: string | null;
  approvedAt?: string | null;
  approvedByName?: string | null;
  reviewerId?: string | null;
  reviewerName?: string | null;
  reviewStartedAt?: string | null;
  returnReason?: string | null;
  duplicateReason?: string | null;
  isLoot?: boolean;
  isInvoice?: boolean;
  relatedNoteIds?: string[];
  fieldValues: NoteFieldValueModel[];
  reviewHistory: NoteReviewHistoryModel[];
}

export interface NoteDetailsModel {
  noteId: string;
  noteNumber: string;
  noteTypeName: string;
  companyName: string;
  factoryId?: string | null;
  factoryName?: string | null;
  productionLineId?: string | null;
  productionLineName?: string | null;
  writingDate?: string | null;
  arrivalAt: string;
  status: NoteStatus;
  imagePath?: string | null;
  createdBy?: string | null;
  createdByName?: string | null;
  createdAt: string;
  reviewStartedAt?: string | null;
  approvedAt?: string | null;
  approvedByName?: string | null;
  updatedAt: string;
  totalFields: number;
  activeCount: number;
  inactiveCount: number;
  unknownCount: number;
  accuracyPercentage: number;
  errorPercentage: number;
  isLoot?: boolean;
  isInvoice?: boolean;
  linkCount?: number;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

export interface CompanyModel {
  id: string;
  name: string;
  isActive: boolean;
}

export interface FactoryModel {
  id: string;
  name: string;
  companyId?: string | null;
  companyName?: string | null;
  isActive: boolean;
}

export interface ProductionLineModel {
  id: string;
  factoryId: string;
  factoryName: string;
  companyId?: string | null;
  companyName?: string | null;
  name: string;
  isActive: boolean;
}

export interface NoteTypeModel {
  id: string;
  code: string;
  nameAr: string;
  description?: string | null;
  isActive: boolean;
}

export interface NoteTypeFieldModel {
  id: string;
  noteTypeId: string;
  fieldId: string;
  fieldCode: string;
  fieldNameAr: string;
  sortOrder: number;
  isRequired: boolean;
  isActive: boolean;
}

export interface NoteTypeFieldDefinitionModel {
  id: string;
  noteTypeId: string;
  fieldId?: string | null;
  fieldCode: string;
  fieldNameAr: string;
  description?: string | null;
  sortOrder: number;
  isRequired: boolean;
  isActive: boolean;
}

export interface UserModel {
  id: string;
  username: string;
  fullName: string;
  roleId: string;
  roleCode: string;
  roleNameAr: string;
  isActive: boolean;
  mustChangePassword: boolean;
}

export interface RoleModel {
  id: string;
  code: string;
  nameAr: string;
  isActive: boolean;
}

export interface AuditLogModel {
  id: string;
  userId?: string | null;
  userName?: string | null;
  actionType: string;
  schemaName?: string | null;
  tableName?: string | null;
  recordId?: string | null;
  description?: string | null;
  createdAt: string;
}

export interface IncompleteIndicatorQueueItemModel {
  noteId: string;
  fieldId: string;
  fieldName: string;
  noteNumber: string;
  companyName: string;
  noteTypeName: string;
  arrivalAt: string;
  writingDate?: string | null;
  status: NoteStatus;
  updatedAt: string;
}

export interface ReviewQueueItemModel {
  noteId: string;
  updatedAt: string;
  noteNumber: string;
  companyName: string;
  noteTypeName: string;
  factoryName?: string | null;
  productionLineName?: string | null;
  arrivalAt: string;
  status: NoteStatus;
  createdBy?: string | null;
  createdByName?: string | null;
  createdAt: string;
  reviewerId?: string | null;
  reviewerName?: string | null;
  reviewStartedAt?: string | null;
}

export interface ReportMeta {
  key: string;
  title: string;
  category: string;
  description: string;
  isCompanyDetailed: boolean;
}

export interface NoteLinkCandidateModel {
  noteId: string;
  noteNumber: string;
  companyName: string;
  noteTypeName: string;
  factoryName?: string | null;
  productionLineName?: string | null;
  writingDate?: string | null;
  arrivalAt: string;
  status: NoteStatus;
  isLinked: boolean;
  isInvoice?: boolean;
}

export interface NoteDelayAssessment {
  isDelayed: boolean;
  daysDelayed: number;
  displayText: string;
  hasWritingDate: boolean;
}

export interface ReportIdentitySettingsModel {
  republicNameArabic: string;
  republicNameEnglish: string;
  ministryName: string;
  ministryNameEnglish: string;
  administrativeLevelOne: string;
  administrativeLevelOneEnglish: string;
  administrativeLevelTwo: string;
  administrativeLevelTwoEnglish: string;
  administrativeLevelThree: string;
  administrativeLevelThreeEnglish: string;
  companyName: string;
  companyNameEnglish: string;
  issuingEntityName: string;
  defaultCity: string;
  updatedAt?: string | null;
  updatedBy?: string | null;
}

export interface CompanyReportDataModel {
  companyName: string;
  applicationName: string;
  filterSummary: string;
  lastArrivalAt?: string | null;
  factoryCount: number;
  from?: string | null;
  to?: string | null;
  metrics: import('./dashboard').CompanyMetrics;
  issue: {
    title: string;
    letterNumber: string;
    city: string;
    hijriDate: string;
    gregorianDate: string;
    recipient: string;
    issuerName: string;
    issuedAt: string;
    formEfficiencyIndex?: number | null;
  };
  identity: ReportIdentitySettingsModel;
  monthly: Array<{
    monthStart: string;
    periodLabel: string;
    totalNotes: number;
    itemQualityPercentage: number | null;
    cleanReviewedNotePercent: number | null;
    cleanReviewedNotes: number;
  }>;
  fieldErrors: Array<{
    fieldLabel: string;
    correctItems: number;
    errorItems: number;
    unreviewedItems: number;
    notApplicableItems: number;
    checkedItems: number;
    errorRate: number | null;
  }>;
  factories: Array<{
    factoryId?: string | null;
    factoryName: string;
    totalNotes: number;
    cleanReviewedNotes: number;
    cleanReviewedNotePercent: number | null;
  }>;
}

export type NoteSourceType = 'Exit' | 'Invoice' | 'Loot' | 'Other';

export interface NoteSourceExitReferenceModel {
  exitNoteId: string;
  referenceOrder: number;
  noteNumber?: string | null;
}

export interface NoteSourceInvoiceModel {
  id: string;
  invoiceNumber: string;
  invoiceDate?: string | null;
  destination?: string | null;
  attachmentFileName?: string | null;
  attachmentContentType?: string | null;
  hasAttachment?: boolean;
}

export interface NoteSourceModel {
  id: string;
  noteId: string;
  sourceType: NoteSourceType;
  otherDescription?: string | null;
  lootDescription?: string | null;
  updatedAt: string;
  exitReferences: NoteSourceExitReferenceModel[];
  invoices: NoteSourceInvoiceModel[];
}

export interface SaveNoteSourceRequest {
  sourceType: NoteSourceType;
  otherDescription?: string;
  lootDescription?: string;
  exitReferences?: Array<{ exitNoteId: string; referenceOrder: number; noteNumber?: string }>;
  invoices?: Array<{
    id?: string;
    invoiceNumber: string;
    invoiceDate?: string;
    destination?: string;
    attachmentFileName?: string;
    attachmentContentType?: string;
    attachmentBase64?: string;
  }>;
}

export type DuplicateClassification =
  | 'COMPANY_DUPLICATE_NUMBER'
  | 'COMPANY_RESEND'
  | 'DATA_ENTRY_DUPLICATE_ATTEMPT';

export interface NoteDuplicateModel {
  noteId: string;
  noteNumber: string;
  companyId: string;
  companyName: string;
  factoryId?: string | null;
  factoryName?: string | null;
  productionLineId?: string | null;
  productionLineName?: string | null;
  noteTypeId: string;
  noteTypeName: string;
  writingDate?: string | null;
  arrivalAt: string;
  createdAt: string;
  attachmentFileName?: string | null;
  attachmentContentType?: string | null;
}
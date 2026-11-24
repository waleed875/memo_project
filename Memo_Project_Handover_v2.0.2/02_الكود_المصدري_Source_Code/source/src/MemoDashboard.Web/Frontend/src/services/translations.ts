import { normalizeNoteStatus } from '../types/memo';

export const STATUS_TRANSLATIONS: Record<string, string> = {
  DRAFT: 'مسودة',
  COMPLETE: 'مكتملة',
  HAS_ERRORS: 'تحتوي أخطاء',
  NEEDS_REVIEW: 'تحتاج مراجعة',
  UNDER_REVIEW: 'قيد التدقيق',
  RETURNED_FOR_CORRECTION: 'معادة للتصحيح',
  APPROVED: 'معتمدة',
}

export const STATUS_OPTIONS: { value: string; label: string }[] = Object.entries(
  STATUS_TRANSLATIONS,
).map(([value, label]) => ({ value, label }))

export const CLASSIFICATION_TRANSLATIONS: Record<string, string> = {
  COMPANY_RESEND: 'إعادة إرسال من الشركة',
  COMPANY_DUPLICATE_NUMBER: 'أرقام مكررة',
}

export const MONTH_NAMES_AR = [
  'يناير',
  'فبراير',
  'مارس',
  'أبريل',
  'مايو',
  'يونيو',
  'يوليو',
  'أغسطس',
  'سبتمبر',
  'أكتوبر',
  'نوفمبر',
  'ديسمبر',
]

const NUMERIC_STATUS_MAP: Record<number, string> = {
  0: 'DRAFT',
  1: 'COMPLETE',
  2: 'HAS_ERRORS',
  3: 'NEEDS_REVIEW',
  4: 'UNDER_REVIEW',
  5: 'RETURNED_FOR_CORRECTION',
  6: 'APPROVED',
}

export function translateStatus(status: unknown): string {
  if (status === null || status === undefined || status === '') return 'غير معروف'
  if (typeof status === 'number' || (typeof status === 'string' && /^\d+$/.test(status.trim()))) {
    const key = NUMERIC_STATUS_MAP[Number(status)]
    return key ? (STATUS_TRANSLATIONS[key] ?? 'غير معروف') : 'غير معروف'
  }
  const str = String(status).trim()
  if (STATUS_TRANSLATIONS[str]) {
    return STATUS_TRANSLATIONS[str]
  }
  const upper = str.toUpperCase()
  if (STATUS_TRANSLATIONS[upper]) {
    return STATUS_TRANSLATIONS[upper]
  }
  return 'غير معروف'
}

export function translateClassification(classification: string | null | undefined): string {
  if (!classification) return 'غير مصنف'
  return CLASSIFICATION_TRANSLATIONS[classification] ?? 'غير مصنف'
}

export const STATUS_BADGE_CLASSES: Record<string, string> = {
  DRAFT: 'bg-slate-100 text-slate-600 border-slate-300',
  COMPLETE: 'bg-emerald-100 text-emerald-700 border-emerald-200',
  HAS_ERRORS: 'bg-rose-100 text-rose-700 border-rose-200',
  NEEDS_REVIEW: 'bg-amber-100 text-amber-700 border-amber-200',
  UNDER_REVIEW: 'bg-cyan-100 text-cyan-700 border-cyan-200',
  RETURNED_FOR_CORRECTION: 'bg-orange-100 text-orange-700 border-orange-200',
  APPROVED: 'bg-emerald-50 text-emerald-800 border-emerald-300',
}

export function statusBadgeClass(status: unknown): string {
  if (status === null || status === undefined) return STATUS_BADGE_CLASSES.DRAFT
  const normalized = normalizeNoteStatus(status)
  return STATUS_BADGE_CLASSES[normalized] ?? STATUS_BADGE_CLASSES.DRAFT
}

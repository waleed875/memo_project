import type { NoteDelayAssessment } from '../types/memo';

export const ALLOWED_DELAY_DAYS = 5;

/**
 * Calculates note arrival delay relative to writing date based on standard business rules:
 * - Allowed delay is 5 days.
 * - If writing date is missing: cannot calculate delay.
 * - If arrival is before writing: invalid order.
 * - If delay > 5 days: marked as delayed with excess days count.
 */
export function calculateNoteDelay(
  writingDate?: string | null,
  arrivalAt?: string | null
): NoteDelayAssessment {
  if (!writingDate || !writingDate.trim()) {
    return {
      isDelayed: false,
      daysDelayed: 0,
      displayText: 'تاريخ التحرير غير مدخل',
      hasWritingDate: false,
    };
  }

  if (!arrivalAt || !arrivalAt.trim()) {
    return {
      isDelayed: false,
      daysDelayed: 0,
      displayText: 'تاريخ الورود غير مسجل',
      hasWritingDate: true,
    };
  }

  // Parse YYYY-MM-DD dates at midnight local time to avoid timezone offset shifts
  const [wYear, wMonth, wDay] = writingDate.slice(0, 10).split('-').map(Number);
  const [aYear, aMonth, aDay] = arrivalAt.slice(0, 10).split('-').map(Number);

  if (isNaN(wYear) || isNaN(wMonth) || isNaN(wDay) || isNaN(aYear) || isNaN(aMonth) || isNaN(aDay)) {
    return {
      isDelayed: false,
      daysDelayed: 0,
      displayText: 'تاريخ غير صالح',
      hasWritingDate: false,
    };
  }

  const wDate = new Date(wYear, wMonth - 1, wDay);
  const aDate = new Date(aYear, aMonth - 1, aDay);

  const diffMs = aDate.getTime() - wDate.getTime();
  const diffDays = Math.round(diffMs / (1000 * 60 * 60 * 24));

  if (diffDays < 0) {
    return {
      isDelayed: false,
      daysDelayed: 0,
      displayText: 'تاريخ الورود يسبق تاريخ التحرير',
      hasWritingDate: true,
    };
  }

  if (diffDays <= ALLOWED_DELAY_DAYS) {
    return {
      isDelayed: false,
      daysDelayed: diffDays,
      displayText: diffDays === 0 ? 'ورود في نفس اليوم' : `طبيعي (${diffDays} يوماً)`,
      hasWritingDate: true,
    };
  }

  const excess = diffDays - ALLOWED_DELAY_DAYS;
  return {
    isDelayed: true,
    daysDelayed: diffDays,
    displayText: `متأخرة ${diffDays} يوماً (تجاوز بمقدار ${excess} ${excess === 1 ? 'يوم' : 'أيام'})`,
    hasWritingDate: true,
  };
}

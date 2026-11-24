/**
 * Approved company KPI computations (mirrors CompanyDetailedReport formulas).
 * All percentages use 2-decimal rounding; null means "not applicable".
 */

export function round2(value: number): number {
  return Math.round(value * 100) / 100
}

/**
 * Machinery efficiency indicator (complement of unlinked entries):
 * (100 - (unlinkedEntryNotes / entryNotes * 100)) = linked entry notes / all entry notes * 100.
 * Null when there are no entry notes.
 */
export function mechanismEfficiencyPercent(entryNotes: number | null | undefined, unlinkedEntryNotes: number | null | undefined): number | null {
  if (entryNotes === null || entryNotes === undefined || unlinkedEntryNotes === null || unlinkedEntryNotes === undefined) return null
  if (!Number.isFinite(entryNotes) || !Number.isFinite(unlinkedEntryNotes) || entryNotes <= 0) return null
  return round2(100 - (unlinkedEntryNotes * 100) / entryNotes)
}

/** Compatibility alias returning the machinery efficiency complement */
export function unlinkedMechanismPercent(entryNotes: number | null | undefined, unlinkedEntryNotes: number | null | undefined): number | null {
  return mechanismEfficiencyPercent(entryNotes, unlinkedEntryNotes)
}

/**
 * Weighted timeliness: [(onTime/total * 0.9) + (late/total * 0.1)] * 100.
 * Notes with invalid/missing dates stay in the denominator only.
 */
export function weightedTimelinessPercent(totalNotes: number | null | undefined, onTimeNotes: number | null | undefined, lateNotes: number | null | undefined): number | null {
  if (totalNotes === null || totalNotes === undefined || onTimeNotes === null || onTimeNotes === undefined || lateNotes === null || lateNotes === undefined) return null
  if (!Number.isFinite(totalNotes) || !Number.isFinite(onTimeNotes) || !Number.isFinite(lateNotes) || totalNotes <= 0) return null
  return round2(((onTimeNotes * 0.9 + lateNotes * 0.1) / totalNotes) * 100)
}

/**
 * Composite indicator: (manual form [missing = 100] + items + mechanisms) / 3.
 */
export function compositePercent(formEfficiency: number | null | undefined, itemQuality: number | null | undefined, mechanism: number | null | undefined): number | null {
  const form = formEfficiency === null || formEfficiency === undefined || Number.isNaN(formEfficiency) ? 100 : formEfficiency
  if (itemQuality === null || itemQuality === undefined || Number.isNaN(itemQuality)) return null
  if (mechanism === null || mechanism === undefined || Number.isNaN(mechanism)) return null
  return round2((form + itemQuality + mechanism) / 3)
}

/** Parses the manual form-efficiency field; empty/invalid means "not entered" (null). */
export function parseFormEfficiency(input: string): number | null {
  const trimmed = input.trim().replace(',', '.')
  if (!trimmed) return null
  const value = Number(trimmed)
  if (!Number.isFinite(value) || value < 0 || value > 100) return null
  return value
}

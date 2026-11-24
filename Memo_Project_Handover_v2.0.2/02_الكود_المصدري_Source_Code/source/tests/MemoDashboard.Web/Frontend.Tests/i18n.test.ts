import { describe, expect, it } from 'vitest'
import ar from '../../../src/MemoDashboard.Web/Frontend/src/i18n/ar.json'
import en from '../../../src/MemoDashboard.Web/Frontend/src/i18n/en.json'

const LEGACY_KEYS = [
  'legacy.navLabel',
  'legacy.sections.summary',
  'legacy.sections.delays',
  'legacy.sections.repetition',
  'legacy.sections.workflow',
  'legacy.sections.recent',
]

describe('i18n dictionaries', () => {
  it('keeps Arabic and English dictionaries in key parity', () => {
    expect(Object.keys(ar).sort()).toEqual(Object.keys(en).sort())
  })

  it('defines the legacy section navigation labels in both languages', () => {
    for (const key of LEGACY_KEYS) {
      expect(ar, `ar missing ${key}`).toHaveProperty(key)
      expect(en, `en missing ${key}`).toHaveProperty(key)
    }
  })

  it('defines the quality KPI labels used by all dashboard variants', () => {
    for (const key of ['kpi.memoAccuracy', 'kpi.correctMemos', 'kpi.errorMemos']) {
      expect(ar, `ar missing ${key}`).toHaveProperty(key)
      expect(en, `en missing ${key}`).toHaveProperty(key)
    }
  })
})
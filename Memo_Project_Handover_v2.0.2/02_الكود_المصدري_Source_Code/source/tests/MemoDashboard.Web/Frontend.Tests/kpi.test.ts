import { describe, expect, it } from 'vitest'
import { compositePercent, mechanismEfficiencyPercent, parseFormEfficiency, round2, unlinkedMechanismPercent, weightedTimelinessPercent } from '../../../src/MemoDashboard.Web/Frontend/src/services/kpi'

describe('approved company KPI formulas', () => {
  it('computes the machinery efficiency indicator as complement of unlinked entries', () => {
    // 3 unlinked out of 10 total -> 100 - 30 = 70%
    expect(mechanismEfficiencyPercent(10, 3)).toBe(70)
    expect(unlinkedMechanismPercent(10, 3)).toBe(70)
    expect(mechanismEfficiencyPercent(0, 0)).toBeNull()
    expect(mechanismEfficiencyPercent(null, 3)).toBeNull()
  })

  it('computes the weighted timeliness formula', () => {
    // ((8 * 0.9 + 1 * 0.1) / 10) * 100 = 73
    expect(weightedTimelinessPercent(10, 8, 1)).toBe(73)
    expect(weightedTimelinessPercent(0, 0, 0)).toBeNull()
  })

  it('computes the composite with a 100 default for a missing form value', () => {
    // (80 + 75 + 70) / 3 = 75 ; missing form falls back to 100 -> (100 + 75 + 70) / 3 = 81.67
    expect(compositePercent(80, 75, 70)).toBe(75)
    expect(compositePercent(null, 75, 70)).toBe(81.67)
    expect(compositePercent(null, null, 70)).toBeNull()
    expect(compositePercent(null, 75, null)).toBeNull()
  })

  it('rounds to two decimals', () => {
    expect(round2(10 / 3)).toBe(3.33)
  })

  it('parses the manual form field strictly between 0 and 100', () => {
    expect(parseFormEfficiency('')).toBeNull()
    expect(parseFormEfficiency('80')).toBe(80)
    expect(parseFormEfficiency('101')).toBeNull()
    expect(parseFormEfficiency('-1')).toBeNull()
    expect(parseFormEfficiency('abc')).toBeNull()
  })
})

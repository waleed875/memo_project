import { describe, expect, it } from 'vitest'
import { calcDelayDays, complianceRate, formatChartNumber, formatDate, formatDateTime, formatNumber, formatPercent, percentage, setCurrentLocale } from '../../../src/MemoDashboard.Web/Frontend/src/services/format'

describe('حساب أيام التأخير بين الكتابة والوصول', () => {
  it('يحسب عدد الأيام عند توفر التاريخين', () => {
    expect(calcDelayDays('2026-08-01', '2026-08-10')).toBe(9)
    expect(calcDelayDays('2026-08-01', '2026-08-06')).toBe(5)
  })

  it('يعيد null عند غياب أحد التاريخين أو عدم صحته', () => {
    expect(calcDelayDays(null, '2026-08-10')).toBeNull()
    expect(calcDelayDays('2026-08-01', undefined)).toBeNull()
    expect(calcDelayDays('invalid', '2026-08-10')).toBeNull()
  })

  it('لا يعيد قيماً سالبة عند الوصول قبل الكتابة', () => {
    expect(calcDelayDays('2026-08-10', '2026-08-01')).toBe(0)
  })
})

describe('النسب المئوية', () => {
  it('نسبة الالتزام تحسب من إجمالي المذكرات وفي الوقت المحدد', () => {
    expect(complianceRate(7, 4)).toBe(57.14)
    expect(complianceRate(10, 10)).toBe(100)
  })

  it('نسبة المتأخرة تحسب بنفس الطريقة', () => {
    expect(percentage(7, 2)).toBe(28.57)
  })

  it('تعيد null عندما يكون الإجمالي صفراً', () => {
    expect(complianceRate(0, 0)).toBeNull()
    expect(percentage(0, 5)).toBeNull()
  })
})

describe('تنسيق تواريخ قاعدة البيانات', () => {
  it('لا يعرض منتصف الليل كتوقيت وهمي لقيمة تاريخ PostgreSQL', () => {
    expect(formatDateTime('2026-08-05T00:00:00')).toBe(formatDate('2026-08-05'))
  })
})
describe('سياسة الأرقام العربية', () => {
  it('تستخدم الأرقام اللاتينية في الأرقام والنسب والمحاور', () => {
    setCurrentLocale('ar-SY-u-nu-latn')
    expect(formatNumber(1234)).toMatch(/[0-9]/)
    expect(formatNumber(1234)).not.toMatch(/[٠-٩]/)
    expect(formatPercent(57.14)).toContain('57.14')
    expect(formatChartNumber('57.14')).toContain('57.1')
  })
})
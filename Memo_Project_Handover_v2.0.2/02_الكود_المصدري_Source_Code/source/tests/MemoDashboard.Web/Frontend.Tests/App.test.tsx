import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import App from '../../../src/MemoDashboard.Web/Frontend/src/App'
import { STATUS_TRANSLATIONS, translateStatus } from '../../../src/MemoDashboard.Web/Frontend/src/services/translations'

describe('ترجمة الحالات إلى العربية', () => {
  it('يترجم جميع الحالات المدعومة', () => {
    expect(translateStatus('DRAFT')).toBe('مسودة')
    expect(translateStatus('COMPLETE')).toBe('مكتملة')
    expect(translateStatus('HAS_ERRORS')).toBe('تحتوي أخطاء')
    expect(translateStatus('NEEDS_REVIEW')).toBe('تحتاج مراجعة')
    expect(translateStatus('UNDER_REVIEW')).toBe('قيد التدقيق')
    expect(translateStatus('RETURNED_FOR_CORRECTION')).toBe('معادة للتصحيح')
    expect(translateStatus('APPROVED')).toBe('معتمدة')
  })

  it('القيم غير المعروفة أو الفارغة تعود بقيمة افتراضية', () => {
    expect(translateStatus(null)).toBe('غير معروف')
    expect(translateStatus(undefined)).toBe('غير معروف')
    expect(translateStatus('UNKNOWN')).toBe('غير معروف')
  })

  it('قاموس الترجمة يطابق جدول الحالات بالكامل', () => {
    expect(STATUS_TRANSLATIONS).toEqual({
      DRAFT: 'مسودة',
      COMPLETE: 'مكتملة',
      HAS_ERRORS: 'تحتوي أخطاء',
      NEEDS_REVIEW: 'تحتاج مراجعة',
      UNDER_REVIEW: 'قيد التدقيق',
      RETURNED_FOR_CORRECTION: 'معادة للتصحيح',
      APPROVED: 'معتمدة',
    })
  })
})

describe('توجيه التطبيق', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ isConfigured: false }), { status: 200 })),
    )
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('يعرض صفحة الإعداد عند عدم تهيئة الاتصال بقاعدة البيانات', async () => {
    render(<App />)
    expect(await screen.findByText('إعداد الاتصال بقاعدة البيانات')).toBeInTheDocument()
  })

  it('يعرض حقلي هوية الجهة ضمن صفحة الإعداد', async () => {
    render(<App />)
    expect(await screen.findByLabelText('اسم الجهة الرئيسية')).toBeInTheDocument()
    expect(screen.getByLabelText('اسم الجهة الفرعية')).toBeInTheDocument()
  })
})

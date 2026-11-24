import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DashboardPreview, type PreviewView } from '../../../src/MemoDashboard.Web/Frontend/src/preview/DashboardPreview'

describe('معاينات تصميم الداشبورد غير المنشورة', () => {
  it.each([
    ['executive', 'ملخص المدير العام'],
    ['operations', 'لوحة النظام والعمليات'],
    ['company', 'الشهباء'],
    ['directory', 'اختيار شركة للتقرير'],
  ] as Array<[PreviewView, string]>)('تعرض واجهة %s بيانات القبول دون طلب شبكة', (view, title) => {
    render(<DashboardPreview view={view} />)
    expect(screen.getByRole('heading', { level: 1, name: title })).toBeInTheDocument()
    expect(screen.getByTestId('preview-view-' + view)).toBeInTheDocument()
    expect(screen.getByText('8 شركات مسجلة · 943 مذكرة · 5 لديها نشاط')).toBeInTheDocument()
  })

  it('تعرض الشركات الثماني فقط في شاشة الاختيار المستقلة', () => {
    const { unmount } = render(<DashboardPreview view="directory" />)
    expect(screen.getByText('8 من 8 شركات')).toBeInTheDocument()
    expect(screen.getByText('رعد')).toBeInTheDocument()
    expect(screen.getAllByText('لا توجد بيانات ضمن النطاق').length).toBeGreaterThanOrEqual(3)
    unmount()

    render(<DashboardPreview view="executive" />)
    expect(screen.queryByText('اختيار شركة للتقرير')).not.toBeInTheDocument()
    expect(screen.queryByText('رعد')).not.toBeInTheDocument()
  })

  it('لا يحوّل غياب البيانات إلى صفر ويعرض حالات الواجهة صراحة', () => {
    render(<DashboardPreview view="company" />)
    expect(screen.getByText('لا توجد بيانات ضمن النطاق')).toBeInTheDocument()
    expect(screen.getByText('تعذر تحميل البيانات')).toBeInTheDocument()
    expect(screen.queryByText(/Health Score/i)).not.toBeInTheDocument()
  })
})

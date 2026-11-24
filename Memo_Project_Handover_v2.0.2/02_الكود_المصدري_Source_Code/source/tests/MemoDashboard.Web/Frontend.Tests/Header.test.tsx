import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { Header } from '../../../src/MemoDashboard.Web/Frontend/src/components/Header'
import { LanguageProvider } from '../../../src/MemoDashboard.Web/Frontend/src/i18n/LanguageProvider'
import { ThemeProvider } from '../../../src/MemoDashboard.Web/Frontend/src/theme/ThemeProvider'

describe('Header unified controls', () => {
  it('does not expose the removed dashboard-version switch', () => {
    render(
      <ThemeProvider>
        <LanguageProvider>
          <Header isConnected isLoading={false} lastUpdated={null} onRefresh={vi.fn()} />
        </LanguageProvider>
      </ThemeProvider>,
    )

    expect(screen.queryByRole('button', { name: 'الانتقال إلى الواجهة الثانية' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'تحديث الآن' })).toBeInTheDocument()
  })
})

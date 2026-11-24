import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import {
  DEFAULT_SECTION,
  LEGACY_SECTION_KEYS,
  LegacySectionNav,
  legacySectionLabel,
} from '../../../src/MemoDashboard.Web/Frontend/src/dashboard/legacy/LegacySectionNav'

describe('LegacySectionNav', () => {
  it('renders all five section tabs in order with the active one selected', () => {
    render(<LegacySectionNav activeSection={DEFAULT_SECTION} onChange={vi.fn()} />)

    const tabs = screen.getAllByRole('tab')
    expect(tabs).toHaveLength(5)
    expect(tabs.map((tab) => tab.textContent)).toEqual(LEGACY_SECTION_KEYS.map(legacySectionLabel))
    expect(screen.getByRole('tab', { name: 'ملخص' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'ملخص' })).toHaveAttribute('aria-controls', 'legacy-panel-summary')
    expect(screen.getByRole('tab', { name: 'التأخيرات' })).toHaveAttribute('aria-selected', 'false')
  })

  it('calls onChange with the clicked section key', () => {
    const onChange = vi.fn()
    render(<LegacySectionNav activeSection={DEFAULT_SECTION} onChange={onChange} />)

    fireEvent.click(screen.getByRole('tab', { name: 'التكرار والجودة' }))
    expect(onChange).toHaveBeenCalledWith('repetition')
  })

  it('keeps every tab keyboard focusable through the roving tabindex', () => {
    render(<LegacySectionNav activeSection="summary" onChange={vi.fn()} />)

    const summary = screen.getByRole('tab', { name: 'ملخص' })
    const delays = screen.getByRole('tab', { name: 'التأخيرات' })
    expect(summary).toHaveAttribute('tabindex', '0')
    expect(delays).toHaveAttribute('tabindex', '-1')
  })

  it('moves focus and selection with arrow keys, Home and End', () => {
    const onChange = vi.fn()
    render(<LegacySectionNav activeSection="summary" onChange={onChange} />)

    fireEvent.keyDown(screen.getByRole('tab', { name: 'ملخص' }), { key: 'ArrowRight' })
    expect(onChange).toHaveBeenLastCalledWith('delays')
    fireEvent.keyDown(screen.getByRole('tab', { name: 'ملخص' }), { key: 'ArrowLeft' })
    expect(onChange).toHaveBeenLastCalledWith('recent')
    fireEvent.keyDown(screen.getByRole('tab', { name: 'ملخص' }), { key: 'Home' })
    expect(onChange).toHaveBeenLastCalledWith('summary')
    fireEvent.keyDown(screen.getByRole('tab', { name: 'ملخص' }), { key: 'End' })
    expect(onChange).toHaveBeenLastCalledWith('recent')
  })
})
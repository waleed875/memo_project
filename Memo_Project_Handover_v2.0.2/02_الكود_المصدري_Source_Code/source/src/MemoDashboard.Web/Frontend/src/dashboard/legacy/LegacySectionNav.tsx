import type { KeyboardEvent, ReactNode } from 'react'
import { t } from '../../i18n'

export type LegacySectionKey = 'summary' | 'delays' | 'repetition' | 'workflow' | 'recent'

export const LEGACY_SECTION_KEYS: LegacySectionKey[] = ['summary', 'delays', 'repetition', 'workflow', 'recent']

export const DEFAULT_SECTION: LegacySectionKey = 'summary'

const labelKey = (key: LegacySectionKey): string => `legacy.sections.${key}`

export function legacySectionLabel(key: LegacySectionKey): string {
  return t(labelKey(key))
}

export function legacySectionTabId(key: LegacySectionKey): string {
  return `legacy-tab-${key}`
}

export function legacySectionPanelId(key: LegacySectionKey): string {
  return `legacy-panel-${key}`
}

interface LegacySectionNavProps {
  activeSection: LegacySectionKey
  onChange: (section: LegacySectionKey) => void
}

const tabBase =
  'rounded-lg px-4 py-2 text-sm font-semibold transition-colors whitespace-nowrap'

export function LegacySectionNav({ activeSection, onChange }: LegacySectionNavProps) {
  const moveTo = (section: LegacySectionKey, focus: boolean) => {
    onChange(section)
    if (focus) {
      const next = document.getElementById(legacySectionTabId(section))
      next?.focus()
    }
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, section: LegacySectionKey) => {
    const index = LEGACY_SECTION_KEYS.indexOf(section)
    // 1.2.2: في RTL الاتجاه البصري معكوس — السهم الأيمن يذهب للتاب السابق بصريًا.
    // (نقرأ dir من أقرب حاوية لأن <html> ثابتة rtl واللغة تتبدل لكل صفحة.)
    const containerDir = event.currentTarget.closest('[dir]')?.getAttribute('dir')
    const isRtl = (containerDir ?? (typeof document !== 'undefined' ? document.dir : 'rtl')) === 'rtl'
    const forward = LEGACY_SECTION_KEYS[(index + 1) % LEGACY_SECTION_KEYS.length]
    const backward = LEGACY_SECTION_KEYS[(index - 1 + LEGACY_SECTION_KEYS.length) % LEGACY_SECTION_KEYS.length]
    switch (event.key) {
      case 'ArrowRight':
        event.preventDefault()
        moveTo(isRtl ? backward : forward, true)
        break
      case 'ArrowLeft':
        event.preventDefault()
        moveTo(isRtl ? forward : backward, true)
        break
      case 'Home':
        event.preventDefault()
        moveTo(LEGACY_SECTION_KEYS[0], true)
        break
      case 'End':
        event.preventDefault()
        moveTo(LEGACY_SECTION_KEYS[LEGACY_SECTION_KEYS.length - 1], true)
        break
    }
  }

  return (
    <nav className="legacy-section-nav" aria-label={t('legacy.navLabel')}>
      <div role="tablist" className="flex items-center gap-2 overflow-x-auto">
        {LEGACY_SECTION_KEYS.map((key) => {
          const selected = key === activeSection
          return (
            <button
              key={key}
              type="button"
              role="tab"
              id={legacySectionTabId(key)}
              aria-controls={legacySectionPanelId(key)}
              aria-selected={selected}
              tabIndex={selected ? 0 : -1}
              onClick={() => onChange(key)}
              onKeyDown={(event) => handleKeyDown(event, key)}
              className={tabBase + (selected ? ' btn-primary' : ' app-hover app-surface border app-border app-text')}
            >
              {legacySectionLabel(key)}
            </button>
          )
        })}
      </div>
    </nav>
  )
}

interface LegacySectionPanelProps {
  section: LegacySectionKey
  stacked?: boolean
  children: ReactNode
}

export function LegacySectionPanel({ section, stacked = false, children }: LegacySectionPanelProps) {
  const panelClass = 'legacy-section app-surface rounded-2xl border app-border p-4 shadow-sm'
  const heading = (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-2 border-b app-border pb-3">
      <div>
        <p className="text-xs font-semibold uppercase tracking-[0.14em] app-text-muted">{t('legacy.sectionEyebrow')}</p>
        <h2 className="mt-1 text-xl font-bold app-text">{legacySectionLabel(section)}</h2>
      </div>

    </div>
  )

  if (stacked) {
    return (
      <section className={panelClass} aria-label={legacySectionLabel(section)}>
        {heading}
        <div className="space-y-5">
          {children}
        </div>
      </section>
    )
  }

  return (
    <section id={legacySectionPanelId(section)} role="tabpanel" aria-labelledby={legacySectionTabId(section)} className={panelClass}>
      {heading}
      <div className="space-y-5">
        {children}
      </div>
    </section>
  )
}
import type { CompanyMetrics } from '../../types/dashboard'
import type { DashboardSummary } from '../../types/dashboard'
import { formatNumber, formatPercent, formatShortDate } from '../../services/format'
import { KpiCard } from '../../components/KpiCard'
import { t } from '../../i18n'
import type { LegacySectionKey } from './LegacySectionNav'

const T = 'var(--acc-'

interface KpiGroupProps {
  summary: DashboardSummary
  companyMetrics?: CompanyMetrics | null
  compact?: boolean
}

interface LegacyKpiSectionsProps extends KpiGroupProps {
  group: Exclude<LegacySectionKey, 'recent'>
}

interface CardDefinition {
  title: string
  value: string
  tint: string
  valueClassName?: string
}

function buildCard(title: string, value: string, tint: string, valueClassName?: string): CardDefinition {
  return { title, value, tint, valueClassName }
}

function kpiCards(summary: DashboardSummary, metrics: CompanyMetrics | null | undefined): Record<Exclude<LegacySectionKey, 'recent'>, CardDefinition[]> {
  const canonical = metrics ?? null
  return {
    summary: [
      buildCard(t('kpi.totalNotes'), formatNumber(summary.totalNotes), T + 'cyan)'),
      buildCard(t('company.reviewCoverage'), formatPercent(canonical?.reviewCoveragePercentage), T + 'violet)'),
      buildCard(t('company.itemQuality'), formatPercent(canonical?.itemQualityPercentage), T + 'emerald)'),
      buildCard(t('company.accuracy'), formatPercent(canonical?.cleanReviewedNotePercent), T + 'cyan)'),
      buildCard(t('company.cleanReviewed'), formatNumber(canonical?.cleanReviewedNotes), T + 'emerald)'),
      buildCard(t('company.errors'), formatNumber(canonical?.errorNotes), T + 'rose)'),
      buildCard(t('company.incompleteReview'), formatNumber(canonical?.incompleteReviewNotes), T + 'amber)'),
      buildCard(t('company.allNotApplicable'), formatNumber(canonical?.allNotApplicableNoteCount), T + 'slate)'),
      buildCard(t('company.noExpected'), formatNumber(canonical?.noExpectedItemsNoteCount), T + 'orange)'),
      buildCard(t('company.timeCompliance'), formatPercent(canonical?.timeCompliancePercentage), T + 'blue)'),
    ],
    delays: [
      buildCard(t('kpi.lateTotal'), formatNumber(summary.lateTotal), `${T}rose)`),
      buildCard(t('kpi.lateSameMonth'), formatNumber(summary.lateSameMonth), `${T}amber)`),
      buildCard(t('kpi.lateCrossMonth'), formatNumber(summary.lateCrossMonth), `${T}violet)`),
      buildCard(t('kpi.onTime'), formatNumber(summary.onTime), `${T}emerald)`),
      buildCard(t('kpi.avgLateDays'), formatNumber(summary.avgLateDays), `${T}yellow)`),
      buildCard(t('kpi.maxLateDays'), formatNumber(summary.maxLateDays), `${T}red)`),
      buildCard(t('kpi.latestArrivalDate'), formatShortDate(summary.latestArrivalDate), `${T}slate)`, 'dashboard-kpi-value-date'),
    ],
    repetition: [
      buildCard(t('kpi.companyResend'), formatNumber(summary.companyResend), `${T}rose)`),
      buildCard(t('kpi.duplicateNumbers'), formatNumber(summary.duplicateNumbers), `${T}pink)`),
    ],
    workflow: [
      buildCard(t("kpi.notesToday"), formatNumber(summary.notesToday), `${T}cyan)`),
      buildCard(t("kpi.notesCurrentMonth"), formatNumber(summary.notesCurrentMonth), `${T}violet)`),
      buildCard(t("kpi.activeCompanies"), formatNumber(summary.activeCompaniesTotal), `${T}emerald)`),
      buildCard(t("kpi.approved"), formatNumber(summary.approved), `${T}blue)`),
      buildCard(t("kpi.pendingReview"), formatNumber(summary.pendingReview), `${T}amber)`),
      buildCard(t("kpi.returnedForCorrection"), formatNumber(summary.returnedForCorrection), `${T}rose)`),
    ],
  }
}

function KpiCards({ cards, compact }: { cards: CardDefinition[]; compact: boolean }) {
  return (
    <div className="dashboard-kpi-grid grid">
      {cards.map((card) => (
        <KpiCard key={card.title} title={card.title} value={card.value} tint={card.tint} compact={compact} valueClassName={card.valueClassName} />
      ))}
    </div>
  )
}

export function LegacyKpiSections({ group, summary, companyMetrics, compact = false }: LegacyKpiSectionsProps) {
  const cards = kpiCards(summary, companyMetrics)[group]

  if (group === 'summary') {
    return (
      <div className={compact ? 'dashboard-kpi-section' : 'space-y-3'}>
        <div className={compact ? 'dashboard-kpi-chips flex flex-wrap items-center' : 'flex flex-wrap items-center gap-2'}>
          <span className="chip-primary rounded-full border px-3 py-1 text-xs font-semibold">
            {t('company.timeCompliance')}: {formatPercent(companyMetrics?.timeCompliancePercentage)}
          </span>
          <span className="chip-danger rounded-full border px-3 py-1 text-xs font-semibold">
            {t('kpi.lateTotal')}: {formatNumber(summary.lateTotal)}
          </span>
          <span className="chip-tint rounded-full border px-3 py-1 text-xs font-semibold" style={{ ['--c' as string]: `${T}slate)` }}>
            {t('badges.pendingReview')}: {formatNumber(summary.pendingReview)}
          </span>
        </div>
        <KpiCards cards={cards} compact={compact} />
      </div>
    )
  }

  return <KpiCards cards={cards} compact={compact} />
}
import type { RecentNoteItem } from '../types/dashboard'
import { statusBadgeClass } from '../services/translations'
import { formatDate, calcDelayDays } from '../services/format'
import { t, translateStatus } from '../i18n'

const thClass = 'px-3 py-2.5 text-start text-xs font-semibold app-text-muted'
const tdClass = 'px-3 py-2.5 text-sm app-text'

interface RecentNotesTableProps {
  notes: RecentNoteItem[]
}

function delayBadge(note: RecentNoteItem) {
  const days = calcDelayDays(note.writingDate, note.arrivalAt)
  if (!note.isLate) {
    return <span className="text-xs app-text-muted">{t('recent.onTime')}</span>
  }
  const text =
    days !== null && days > 0
      ? days === 1
        ? t('recent.lateDays', { count: days })
        : t('recent.lateDaysPlural', { count: days })
      : t('recent.onTime')
  return (
    <span
      className="inline-block rounded-full border border-rose-300 bg-rose-100 px-2.5 py-0.5 text-xs font-medium text-rose-700"
      title={days !== null ? `... ${days} يوم` : undefined}
    >
      {text}
    </span>
  )
}

function statusPill(note: RecentNoteItem) {
  return (
    <span className={`inline-block rounded-full border px-2.5 py-0.5 text-xs font-medium ${statusBadgeClass(note.status)}`}>
      {translateStatus(note.status)}
    </span>
  )
}

function NoteCard({ note }: { note: RecentNoteItem }) {
  return (
    <article className="app-surface-muted rounded-xl border app-border p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-sm font-bold app-text">{note.noteNumber}</span>
        {statusPill(note)}
      </div>
      <dl className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1.5 text-sm">
        <div className="min-w-0">
          <dt className="text-xs app-text-muted">{t('recent.company')}</dt>
          <dd className="truncate app-text">{note.companyName ?? '—'}</dd>
        </div>
        <div className="min-w-0">
          <dt className="text-xs app-text-muted">{t('recent.factory')}</dt>
          <dd className="truncate app-text">{note.factoryName ?? '—'}</dd>
        </div>
        <div className="min-w-0">
          <dt className="text-xs app-text-muted">{t('recent.line')}</dt>
          <dd className="truncate app-text">{note.productionLineName ?? '—'}</dd>
        </div>
        <div className="min-w-0">
          <dt className="text-xs app-text-muted">{t('recent.type')}</dt>
          <dd className="truncate app-text">{note.noteTypeName ?? '—'}</dd>
        </div>
        <div className="min-w-0">
          <dt className="text-xs app-text-muted">{t('recent.writingDate')}</dt>
          <dd className="truncate app-text">{formatDate(note.writingDate)}</dd>
        </div>
        <div className="min-w-0">
          <dt className="text-xs app-text-muted">{t('recent.arrivalDate')}</dt>
          <dd className="truncate app-text">{formatDate(note.arrivalAt)}</dd>
        </div>
      </dl>
      <div className="mt-2 flex items-center justify-between gap-2">
        <span className="text-xs app-text-muted">{t('recent.delay')}</span>
        {delayBadge(note)}
      </div>
    </article>
  )
}

export function RecentNotesTable({ notes }: RecentNotesTableProps) {
  if (notes.length === 0) {
    return (
      <section className="app-surface rounded-xl border app-border p-8 text-center app-text-muted shadow-sm">
        {t('recent.empty')}
      </section>
    )
  }

  return (
    <section className="app-surface overflow-hidden rounded-xl border app-border shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b app-border px-4 py-3">
        <h2 className="text-lg font-bold app-text">
          {t('recent.title')}
          {notes.length < 10 ? ` (${notes.length})` : ''}
        </h2>
        <span className="app-surface-muted rounded-full px-2.5 py-0.5 text-xs font-medium app-text-muted">
          {notes.length} {t('recent.results')}
        </span>
      </div>
      <div className="md:hidden space-y-3 p-4">
        {notes.map((note) => (
          <NoteCard key={note.id} note={note} />
        ))}
      </div>
      <div className="hidden overflow-x-auto md:block">
        <table className="w-full min-w-[950px] border-collapse text-start">
          <thead className="app-surface-muted">
            <tr>
              <th className={thClass}>{t('recent.noteNumber')}</th>
              <th className={thClass}>{t('recent.company')}</th>
              <th className={thClass}>{t('recent.factory')}</th>
              <th className={thClass}>{t('recent.line')}</th>
              <th className={thClass}>{t('recent.type')}</th>
              <th className={thClass}>{t('recent.writingDate')}</th>
              <th className={thClass}>{t('recent.arrivalDate')}</th>
              <th className={thClass}>{t('recent.status')}</th>
              <th className={thClass}>{t('recent.delay')}</th>
            </tr>
          </thead>
          <tbody className="divide-y app-border">
            {notes.map((note) => (
              <tr key={note.id} className="app-hover-row transition-colors">
                <td className={`${tdClass} font-semibold`}>{note.noteNumber}</td>
                <td className={tdClass}>{note.companyName ?? '—'}</td>
                <td className={tdClass}>{note.factoryName ?? '—'}</td>
                <td className={tdClass}>{note.productionLineName ?? '—'}</td>
                <td className={tdClass}>{note.noteTypeName ?? '—'}</td>
                <td className={tdClass}>{formatDate(note.writingDate)}</td>
                <td className={tdClass}>{formatDate(note.arrivalAt)}</td>
                <td className={tdClass}>{statusPill(note)}</td>
                <td className={tdClass}>{delayBadge(note)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
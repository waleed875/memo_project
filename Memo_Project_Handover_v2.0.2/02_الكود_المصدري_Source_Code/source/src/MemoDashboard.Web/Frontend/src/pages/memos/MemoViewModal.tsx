import React, { useEffect, useState, useCallback } from 'react';
import { Modal } from '../../components/common/Modal';
import { api } from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import { calculateNoteDelay } from '../../utils/noteDelay';
import { MemoLinksModal } from './MemoLinksModal';
import { NoteSourceModal } from './NoteSourceModal';
import { IndicatorValue, normalizeNoteStatus, type NoteModel, type NoteStatus, type NoteAttachmentModel } from '../../types/memo';

interface MemoViewModalProps {
  isOpen: boolean;
  onClose: () => void;
  noteId: string | null;
  onEditNote?: (noteId: string) => void;
  onStatusChanged?: () => void;
}

export const MemoViewModal: React.FC<MemoViewModalProps> = ({
  isOpen,
  onClose,
  noteId,
  onEditNote,
  onStatusChanged,
}) => {
  const { user, hasRole } = useAuth();
  const canReview = hasRole('ADMIN', 'REVIEWER');
  const canEdit = hasRole('ADMIN', 'DATA_ENTRY');

  const [loading, setLoading] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [note, setNote] = useState<NoteModel | null>(null);

  // Multi-attachment viewer state
  const [attachments, setAttachments] = useState<NoteAttachmentModel[]>([]);
  const [selectedAttachmentIndex, setSelectedAttachmentIndex] = useState(0);
  const [showAttachmentViewer, setShowAttachmentViewer] = useState(false);

  // Viewer image transform state (Zoom & Rotation)
  const [imageZoom, setImageZoom] = useState(1);
  const [imageRotation, setImageRotation] = useState(0);

  const resetImageTransform = () => {
    setImageZoom(1);
    setImageRotation(0);
  };

  // Return reason dialog state
  const [showReturnDialog, setShowReturnDialog] = useState(false);
  const [returnReason, setReturnReason] = useState('');

  // Release review dialog state
  const [showReleaseDialog, setShowReleaseDialog] = useState(false);
  const [releaseReason, setReleaseReason] = useState('');

  // Memo Links modal
  const [showLinksModal, setShowLinksModal] = useState(false);

  // Note Source modal
  const [showNoteSourceModal, setShowNoteSourceModal] = useState(false);

  const fetchNote = useCallback(async () => {
    if (!noteId) return;
    setLoading(true);
    setError(null);
    try {
      const [data, atts] = await Promise.all([
        api.getNote(noteId),
        api.getNoteAttachments(noteId).catch(() => []),
      ]);
      setNote(data);
      setAttachments(atts);
      if (atts.length > 0) {
        setSelectedAttachmentIndex(0);
      }
    } catch (err: unknown) {
      console.error('Failed to load note details', err);
      setError(err instanceof Error ? err.message : 'تعذر تحميل تفاصيل المذكرة.');
    } finally {
      setLoading(false);
    }
  }, [noteId]);

  useEffect(() => {
    if (isOpen && noteId) {
      fetchNote();
      setShowReturnDialog(false);
      setShowReleaseDialog(false);
      setShowLinksModal(false);
      setShowAttachmentViewer(false);
    } else {
      setNote(null);
    }
  }, [isOpen, noteId, fetchNote]);

  const handleStartReview = async () => {
    if (!note) return;
    setActionLoading(true);
    setError(null);
    try {
      await api.startReview(note.id, note.updatedAt);
      await fetchNote();
      onStatusChanged?.();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'فشل بدء التدقيق.');
    } finally {
      setActionLoading(false);
    }
  };

  const handleApprove = async () => {
    if (!note) return;
    if (!window.confirm(`هل تريد بالتأكيد اعتماد المذكرة رقم ${note.noteNumber} نهائياً؟`)) return;
    setActionLoading(true);
    setError(null);
    try {
      await api.approveNote(note.id, note.updatedAt);
      await fetchNote();
      onStatusChanged?.();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'فشل اعتماد المذكرة.');
    } finally {
      setActionLoading(false);
    }
  };

  const handleReturnForCorrection = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!note) return;
    if (!returnReason.trim()) {
      alert('سبب الإعادة للتصحيح مطلوب إلزامياً.');
      return;
    }
    setActionLoading(true);
    setError(null);
    try {
      await api.returnNote(note.id, returnReason.trim(), note.updatedAt);
      setShowReturnDialog(false);
      setReturnReason('');
      await fetchNote();
      onStatusChanged?.();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'فشل إعادة المذكرة للتصحيح.');
    } finally {
      setActionLoading(false);
    }
  };

  const handleReleaseReview = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!note) return;
    if (!releaseReason.trim()) {
      alert('سبب فك حجز المراجعة مطلوب إلزامياً.');
      return;
    }
    setActionLoading(true);
    setError(null);
    try {
      await api.releaseReview(note.id, releaseReason.trim());
      setShowReleaseDialog(false);
      setReleaseReason('');
      await fetchNote();
      onStatusChanged?.();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'فشل فك حجز المراجعة.');
    } finally {
      setActionLoading(false);
    }
  };

  const getStatusBadge = (s: unknown) => {
    const normalized = normalizeNoteStatus(s);
    const map: Record<NoteStatus, { label: string; cls: string }> = {
      APPROVED: { label: 'معتمدة', cls: 'bg-emerald-500/15 border-emerald-500/30 text-emerald-400' },
      RETURNED_FOR_CORRECTION: { label: 'معادة للتصحيح', cls: 'bg-rose-500/15 border-rose-500/30 text-rose-400' },
      UNDER_REVIEW: { label: 'قيد المراجعة', cls: 'bg-blue-500/15 border-blue-500/30 text-blue-400' },
      NEEDS_REVIEW: { label: 'بانتظار المراجعة', cls: 'bg-indigo-500/15 border-indigo-500/30 text-indigo-400' },
      DRAFT: { label: 'مسودة', cls: 'bg-amber-500/15 border-amber-500/30 text-amber-400' },
      COMPLETE: { label: 'مكتملة', cls: 'bg-teal-500/15 border-teal-500/30 text-teal-400' },
      HAS_ERRORS: { label: 'تحوي أخطاء', cls: 'bg-orange-500/15 border-orange-500/30 text-orange-400' },
    };
    const item = map[normalized] || { label: String(s), cls: 'bg-slate-500/15 border-slate-500/30 text-slate-400' };
    return (
      <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold border ${item.cls}`}>
        {item.label}
      </span>
    );
  };

  const getIndicatorBadge = (val: IndicatorValue) => {
    switch (val) {
      case IndicatorValue.Correct:
        return <span className="px-2 py-0.5 rounded text-[11px] font-semibold bg-emerald-500/15 text-emerald-400 border border-emerald-500/30">صحيح</span>;
      case IndicatorValue.Incorrect:
        return <span className="px-2 py-0.5 rounded text-[11px] font-semibold bg-rose-500/15 text-rose-400 border border-rose-500/30">غير صحيح</span>;
      case IndicatorValue.Missing:
        return <span className="px-2 py-0.5 rounded text-[11px] font-semibold bg-amber-500/15 text-amber-400 border border-amber-500/30">مفقود</span>;
      case IndicatorValue.Unclear:
        return <span className="px-2 py-0.5 rounded text-[11px] font-semibold bg-purple-500/15 text-purple-400 border border-purple-500/30">غير واضح</span>;
      case IndicatorValue.Incomplete:
        return <span className="px-2 py-0.5 rounded text-[11px] font-semibold bg-orange-500/15 text-orange-400 border border-orange-500/30">غير مكتمل</span>;
      case IndicatorValue.NotApplicable:
        return <span className="px-2 py-0.5 rounded text-[11px] font-semibold bg-slate-500/15 text-slate-400 border border-slate-500/30">غير منطبق</span>;
      default:
        return <span className="px-2 py-0.5 rounded text-[11px] font-semibold bg-yellow-500/15 text-yellow-400 border border-yellow-500/30">غير مدقق</span>;
    }
  };

  const getIndicatorRowClass = (val: IndicatorValue) => {
    if (val === IndicatorValue.Correct) return 'bg-emerald-950/10 hover:bg-emerald-950/20';
    if (val === IndicatorValue.Unreviewed) return 'bg-yellow-950/10 hover:bg-yellow-950/20';
    if (val === IndicatorValue.NotApplicable) return 'bg-slate-900/40 hover:bg-slate-900/60';
    return 'bg-rose-950/15 hover:bg-rose-950/25';
  };

  const formatDate = (isoStr?: string | null, emptyText = 'غير محدد') => {
    if (!isoStr) return emptyText;
    try {
      const d = new Date(isoStr);
      return `${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, '0')}/${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
    } catch {
      return isoStr;
    }
  };

  const delay = note ? calculateNoteDelay(note.writingDate, note.arrivalAt) : null;
  const isReviewerOwner = note?.reviewerId && user?.id && note.reviewerId.toLowerCase() === user.id.toLowerCase();
  const canActOnReview = canReview && note?.status === 'UNDER_REVIEW' && (isReviewerOwner || hasRole('ADMIN'));
  const canStartReviewAction = canReview && (note?.status === 'NEEDS_REVIEW' || note?.status === 'COMPLETE');

  return (
    <>
      <Modal
        isOpen={isOpen}
        onClose={onClose}
        title={note ? `تفاصيل المذكرة رقم ${note.noteNumber}` : 'عرض المذكرة'}
        maxWidth="6xl"
      >
        <div className="space-y-5" dir="rtl">
          {loading ? (
            <div className="py-16 text-center text-slate-400">جارٍ تحميل بيانات المذكرة...</div>
          ) : !note ? (
            <div className="py-12 text-center text-slate-500">
              {error || 'لم يتم العثور على بيانات المذكرة.'}
            </div>
          ) : (
            <>
              {error && (
                <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-3 text-xs text-rose-400">
                  {error}
                </div>
              )}

              {/* Status & Return Reason Banner */}
              {note.status === 'RETURNED_FOR_CORRECTION' && (
                <div className="rounded-2xl border border-rose-500/40 bg-rose-950/30 p-4 space-y-1 text-rose-300 shadow-sm">
                  <div className="flex items-center gap-2 font-bold text-sm text-rose-200">
                    <span>⚠️</span>
                    <span>المذكرة معادة للتصحيح من قبل المراجع:</span>
                  </div>
                  <p className="text-xs text-rose-300 leading-relaxed font-semibold pr-6">
                    {note.returnReason || 'لا يوجد سبب مفصل مسجل.'}
                  </p>
                </div>
              )}

              {/* Duplicate Reason Banner */}
              {note.duplicateReason && (
                <div className="rounded-2xl border border-amber-500/40 bg-amber-950/25 p-4 space-y-1 text-amber-300 shadow-sm">
                  <div className="flex items-center gap-2 font-bold text-sm text-amber-200">
                    <span>⚠️</span>
                    <span>سبب الاستمرار رغم تكرار رقم المذكرة:</span>
                  </div>
                  <p className="text-xs text-amber-300 leading-relaxed pr-6">
                    {note.duplicateReason}
                  </p>
                </div>
              )}

              {/* Loot Banner */}
              {note.isLoot && (
                <div className="rounded-2xl border border-emerald-500/40 bg-emerald-950/25 p-4 space-y-1 text-emerald-300 shadow-sm">
                  <div className="flex items-center gap-2 font-bold text-sm text-emerald-200">
                    <span>🛡️</span>
                    <span>مذكرة غنائم ومستوردات:</span>
                  </div>
                  <p className="text-xs text-emerald-200/90 leading-relaxed pr-6">
                    هذه المذكرة مسجلة كـ غنائم ومستوردات وهي معفية نظامياً من الربط بمذكرة إخراج مقابلة، وتُحتسب تلقائياً كإجراء سليم ومطابق في مؤشر كفاءة الآليات ولا تعد خللاً غير مربوط.
                  </p>
                </div>
              )}

              {/* Invoice Banner */}
              {note.isInvoice && (
                <div className="rounded-2xl border border-sky-500/40 bg-sky-950/25 p-4 space-y-1 text-sky-300 shadow-sm">
                  <div className="flex items-center gap-2 font-bold text-sm text-sky-200">
                    <span>🧾</span>
                    <span>خروج بموجب فاتورة:</span>
                  </div>
                  <p className="text-xs text-sky-200/90 leading-relaxed pr-6">
                    تم إخراج هذه المذكرة بموجب فاتورة رسمية، وتُعامل معاملة مذكرة الإخراج للربط ومؤشرات التدقيق.
                  </p>
                </div>
              )}

              {/* Author Note Banner */}
              {note.notes && (
                <div className="rounded-2xl border border-slate-700/80 bg-slate-800/40 p-3.5 text-xs text-slate-300 space-y-1">
                  <span className="font-bold text-slate-200">
                    ملاحظة مدخل البيانات ({note.createdByName || 'غير محدد'} — {formatDate(note.createdAt)}):
                  </span>
                  <p className="pr-4 text-slate-300">{note.notes}</p>
                </div>
              )}

              {/* Header Info Grid */}
              <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-4 shadow-sm grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-4 text-xs">
                <div>
                  <span className="text-slate-400 block mb-1">الشركة:</span>
                  <span className="font-bold text-slate-100">{note.companyName}</span>
                </div>
                <div>
                  <span className="text-slate-400 block mb-1">نوع المذكرة:</span>
                  <div className="flex items-center gap-1.5 flex-wrap">
                    <span className="font-bold text-slate-100">{note.noteTypeName}</span>
                    {note.isLoot && (
                      <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                        غنائم ومستوردات
                      </span>
                    )}
                    {note.isInvoice && (
                      <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold bg-sky-500/20 text-sky-300 border border-sky-500/30">
                        خروج بموجب فاتورة
                      </span>
                    )}
                  </div>
                </div>
                <div>
                  <span className="text-slate-400 block mb-1">المعمل / الخط:</span>
                  <span className="font-semibold text-slate-200">
                    {note.factoryName || 'غير محدد'}
                    {note.productionLineName && ` / ${note.productionLineName}`}
                  </span>
                </div>
                <div>
                  <span className="text-slate-400 block mb-1">الحالة الحالية:</span>
                  <div>{getStatusBadge(note.status)}</div>
                </div>
                <div>
                  <span className="text-slate-400 block mb-1">تاريخ التحرير:</span>
                  <span className={note.writingDate ? 'font-semibold text-slate-200' : 'text-amber-400 font-semibold'}>
                    {note.writingDate ? note.writingDate.slice(0, 10) : 'غير مسجل (مفقود)'}
                  </span>
                </div>
                <div>
                  <span className="text-slate-400 block mb-1">تاريخ الورود:</span>
                  <span className="font-semibold text-slate-200">{note.arrivalAt?.slice(0, 10)}</span>
                </div>
                <div className="col-span-2">
                  <span className="text-slate-400 block mb-1">تقييم مدة الورود:</span>
                  <span
                    className={`inline-flex items-center px-2.5 py-0.5 rounded text-xs font-semibold ${
                      !delay?.hasWritingDate
                        ? 'bg-amber-500/15 text-amber-400 border border-amber-500/30'
                        : delay.isDelayed
                        ? 'bg-rose-500/15 text-rose-400 border border-rose-500/30'
                        : 'bg-emerald-500/15 text-emerald-400 border border-emerald-500/30'
                    }`}
                  >
                    {delay?.displayText}
                  </span>
                </div>
              </div>

              {/* Lifecycle Summary (5 Metrics matching FrmNoteView.cs) */}
              <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-4 shadow-sm space-y-3">
                <h3 className="text-xs font-bold text-slate-300 uppercase tracking-wider">بيانات دورة حياة المذكرة والتدقيق</h3>
                <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3 text-center text-xs">
                  <div className="rounded-xl border border-slate-800 bg-slate-800/40 p-2.5">
                    <span className="text-slate-400 block mb-1 text-[11px]">تاريخ الإدخال</span>
                    <span className="font-semibold text-slate-200">{formatDate(note.createdAt)}</span>
                  </div>
                  <div className="rounded-xl border border-slate-800 bg-slate-800/40 p-2.5">
                    <span className="text-slate-400 block mb-1 text-[11px]">مدخل البيانات</span>
                    <span className="font-semibold text-slate-200">{note.createdByName || 'غير محدد'}</span>
                  </div>
                  <div className="rounded-xl border border-slate-800 bg-slate-800/40 p-2.5">
                    <span className="text-slate-400 block mb-1 text-[11px]">بدء التدقيق</span>
                    <span className="font-semibold text-slate-200">{formatDate(note.reviewStartedAt, 'لم يبدأ')}</span>
                  </div>
                  <div className="rounded-xl border border-slate-800 bg-slate-800/40 p-2.5">
                    <span className="text-slate-400 block mb-1 text-[11px]">تاريخ الاعتماد</span>
                    <span className="font-semibold text-slate-200">{formatDate(note.approvedAt, 'لم يعتمد بعد')}</span>
                  </div>
                  <div className="rounded-xl border border-slate-800 bg-slate-800/40 p-2.5">
                    <span className="text-slate-400 block mb-1 text-[11px]">المدقق النهائي</span>
                    <span className="font-semibold text-slate-200">{note.approvedByName || (note.reviewerName ?? 'غير متاح')}</span>
                  </div>
                </div>
              </div>

              {/* Indicators / Field Values Table */}
              <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-4 shadow-sm space-y-3">
                <div className="flex items-center justify-between">
                  <h3 className="text-sm font-bold text-slate-100 flex items-center gap-2">
                    <span>📋</span>
                    <span>مؤشرات المذكرة وحالاتها</span>
                    <span className="text-xs font-normal text-slate-400">({note.fieldValues.length} مؤشر)</span>
                  </h3>
                </div>

                <div className="overflow-x-auto rounded-xl border border-slate-800">
                  <table className="w-full text-right text-xs">
                    <thead className="bg-slate-800/70 border-b border-slate-700/60 text-slate-300">
                      <tr>
                        <th className="px-3 py-2.5 w-12 text-center">#</th>
                        <th className="px-3 py-2.5">المؤشر</th>
                        <th className="px-3 py-2.5 text-center">حالة المؤشر</th>
                        <th className="px-3 py-2.5">سبب عدم الصلاحية أو الملاحظة</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-800/60">
                      {note.fieldValues.length === 0 ? (
                        <tr>
                          <td colSpan={4} className="px-4 py-8 text-center text-slate-500">
                            لا توجد قيم مؤشرات مسجلة لهذه المذكرة.
                          </td>
                        </tr>
                      ) : (
                        note.fieldValues.map((fv, idx) => (
                          <tr key={fv.id || fv.fieldId} className={`${getIndicatorRowClass(fv.fieldValue)} transition-colors`}>
                            <td className="px-3 py-2.5 text-center text-slate-400">{idx + 1}</td>
                            <td className="px-3 py-2.5 font-medium text-slate-200">{fv.fieldName}</td>
                            <td className="px-3 py-2.5 text-center">{getIndicatorBadge(fv.fieldValue)}</td>
                            <td className="px-3 py-2.5 text-slate-300">
                              {fv.inactiveReason || <span className="text-slate-600">-</span>}
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              </div>

              {/* Review History Table matching FrmNoteView.cs */}
              {note.reviewHistory && note.reviewHistory.length > 0 && (
                <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-4 shadow-sm space-y-3">
                  <h3 className="text-sm font-bold text-slate-100 flex items-center gap-2">
                    <span>📜</span>
                    <span>سجل التدقيق وتغيير الحالات</span>
                    <span className="text-xs font-normal text-slate-400">({note.reviewHistory.length} حركة)</span>
                  </h3>

                  <div className="overflow-x-auto rounded-xl border border-slate-800 max-h-48 overflow-y-auto">
                    <table className="w-full text-right text-xs">
                      <thead className="sticky top-0 bg-slate-800/90 backdrop-blur border-b border-slate-700/60 text-slate-300">
                        <tr>
                          <th className="px-3 py-2">الإجراء</th>
                          <th className="px-3 py-2">السبب / الملاحظة</th>
                          <th className="px-3 py-2">المستخدم</th>
                          <th className="px-3 py-2">الدور</th>
                          <th className="px-3 py-2">التاريخ والوقت</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-slate-800/60 text-slate-300">
                        {note.reviewHistory.map((h, i) => (
                          <tr key={i} className="hover:bg-slate-800/30 transition-colors">
                            <td className="px-3 py-2 font-semibold text-teal-300">{h.action}</td>
                            <td className="px-3 py-2 text-slate-300">{h.reason || '-'}</td>
                            <td className="px-3 py-2 font-medium">{h.userName || '-'}</td>
                            <td className="px-3 py-2 text-slate-400">{h.userRoleName || '-'}</td>
                            <td className="px-3 py-2 text-slate-400">{formatDate(h.createdAt)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {/* Attachment Section */}
              {(attachments.length > 0 || note.attachmentFileName) && (
                <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-4 shadow-sm space-y-3">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2 font-bold text-xs text-slate-200">
                      <span>📎</span>
                      <span>مرفقات المذكرة</span>
                      <span className="text-slate-400 font-normal">({attachments.length || 1} مرفق)</span>
                    </div>
                    <button
                      type="button"
                      onClick={() => {
                        setSelectedAttachmentIndex(0);
                        resetImageTransform();
                        setShowAttachmentViewer(true);
                      }}
                      className="px-3 py-1.5 text-xs font-semibold rounded-lg bg-teal-600/20 border border-teal-500/30 text-teal-300 hover:bg-teal-600/30 flex items-center gap-1.5"
                    >
                      <span>👁️</span>
                      <span>معاينة مباشرة في الموقع</span>
                    </button>
                  </div>

                  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2.5">
                    {(attachments.length > 0 ? attachments : [{
                      id: '',
                      fileName: note.attachmentFileName || 'مرفق المذكرة',
                      contentType: note.attachmentContentType || 'application/pdf',
                      fileSize: 0,
                      createdAt: note.createdAt
                    }]).map((att, idx) => {
                      const isPdf = att.contentType.includes('pdf') || att.fileName.toLowerCase().endsWith('.pdf');
                      return (
                        <div
                          key={att.id || idx}
                          className="flex items-center justify-between p-2.5 rounded-xl border border-slate-700/70 bg-slate-800/60 hover:bg-slate-800 transition-colors"
                        >
                          <div className="flex items-center gap-2.5 truncate min-w-0">
                            <span className="text-base">{isPdf ? '📄' : '🖼️'}</span>
                            <div className="truncate">
                              <span className="text-xs font-medium text-slate-200 block truncate" title={att.fileName}>
                                {att.fileName}
                              </span>
                              {att.fileSize > 0 && (
                                <span className="text-[10px] text-slate-400">{(att.fileSize / 1024).toFixed(0)} KB</span>
                              )}
                            </div>
                          </div>

                          <button
                            type="button"
                            onClick={() => {
                              setSelectedAttachmentIndex(idx);
                              resetImageTransform();
                              setShowAttachmentViewer(true);
                            }}
                            className="px-2.5 py-1 text-xs rounded-lg bg-teal-600/20 border border-teal-500/30 text-teal-300 hover:bg-teal-600/30 flex-shrink-0 font-medium"
                          >
                            معاينة
                          </button>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* Action Buttons Bar */}
              <div className="flex flex-wrap items-center justify-between gap-3 pt-4 border-t border-slate-800">
                <div className="flex flex-wrap items-center gap-2">
                  <button
                    type="button"
                    onClick={() => setShowLinksModal(true)}
                    className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold rounded-xl bg-slate-800 border border-slate-700 text-slate-200 hover:bg-slate-700 shadow-sm"
                  >
                    <span>🔗</span>
                    <span>المذكرات المرتبطة</span>
                  </button>

                  {(note.noteTypeName?.includes('إدخال') || note.noteTypeName?.toLowerCase().includes('entry')) && (
                    <button
                      type="button"
                      onClick={() => setShowNoteSourceModal(true)}
                      className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold rounded-xl bg-sky-600/20 border border-sky-500/30 text-sky-300 hover:bg-sky-600/30 shadow-sm cursor-pointer"
                    >
                      <span>📦</span>
                      <span>مصدر المذكرة</span>
                    </button>
                  )}

                  {canEdit && note.status !== 'APPROVED' && onEditNote && (
                    <button
                      type="button"
                      onClick={() => {
                        onClose();
                        onEditNote(note.id);
                      }}
                      className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold rounded-xl bg-blue-600/20 border border-blue-500/30 text-blue-300 hover:bg-blue-600/30 shadow-sm"
                    >
                      <span>✏️</span>
                      <span>تعديل المذكرة</span>
                    </button>
                  )}
                </div>

                <div className="flex flex-wrap items-center gap-2">
                  {canStartReviewAction && (
                    <button
                      type="button"
                      disabled={actionLoading}
                      onClick={handleStartReview}
                      className="px-4 py-2 text-xs font-bold rounded-xl bg-blue-600 text-white hover:bg-blue-500 shadow-md disabled:opacity-50"
                    >
                      {actionLoading ? 'جارٍ البدء...' : 'بدء التدقيق'}
                    </button>
                  )}

                  {canActOnReview && (
                    <>
                      <button
                        type="button"
                        disabled={actionLoading}
                        onClick={() => setShowReleaseDialog(true)}
                        className="px-3.5 py-2 text-xs font-semibold rounded-xl bg-slate-700 border border-slate-600 text-slate-200 hover:bg-slate-600 shadow-sm disabled:opacity-50"
                      >
                        فك حجز المراجعة
                      </button>
                      <button
                        type="button"
                        disabled={actionLoading}
                        onClick={() => setShowReturnDialog(true)}
                        className="px-3.5 py-2 text-xs font-semibold rounded-xl bg-rose-600/20 border border-rose-500/30 text-rose-300 hover:bg-rose-600/30 shadow-sm disabled:opacity-50"
                      >
                        إعادة للتصحيح
                      </button>
                      <button
                        type="button"
                        disabled={actionLoading}
                        onClick={handleApprove}
                        className="px-4 py-2 text-xs font-bold rounded-xl bg-emerald-600 text-white hover:bg-emerald-500 shadow-md disabled:opacity-50"
                      >
                        {actionLoading ? 'جارٍ الاعتماد...' : 'اعتماد المذكرة'}
                      </button>
                    </>
                  )}

                  <button
                    type="button"
                    onClick={onClose}
                    className="px-4 py-2 text-xs font-medium rounded-xl border border-slate-700 text-slate-300 hover:bg-slate-800"
                  >
                    إغلاق
                  </button>
                </div>
              </div>
            </>
          )}
        </div>
      </Modal>

      {/* Return for Correction Reason Dialog */}
      {showReturnDialog && (
        <Modal
          isOpen={showReturnDialog}
          onClose={() => setShowReturnDialog(false)}
          title="إعادة المذكرة للتصحيح"
          maxWidth="md"
        >
          <form onSubmit={handleReturnForCorrection} className="space-y-4" dir="rtl">
            <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-3 text-xs text-rose-400">
              يرجى كتابة سبب إعادة المذكرة لمدخل البيانات مع توضيح النقاط الواجب تعديلها بدقة (حقل إلزامي).
            </div>
            <div>
              <label className="block text-xs font-semibold text-slate-300 mb-1">سبب الإعادة *</label>
              <textarea
                required
                rows={4}
                value={returnReason}
                onChange={(e) => setReturnReason(e.target.value)}
                placeholder="اكتب أسباب الإعادة والملاحظات هنا..."
                className="w-full rounded-lg border border-slate-700 bg-slate-800 p-2.5 text-xs text-slate-100 placeholder-slate-500 focus:border-teal-500 focus:outline-none"
              />
            </div>
            <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-800">
              <button
                type="button"
                onClick={() => setShowReturnDialog(false)}
                className="px-3 py-1.5 text-xs rounded-lg border border-slate-700 text-slate-300 hover:bg-slate-800"
              >
                إلغاء
              </button>
              <button
                type="submit"
                disabled={actionLoading || !returnReason.trim()}
                className="px-4 py-1.5 text-xs font-bold rounded-lg bg-rose-600 text-white hover:bg-rose-500 disabled:opacity-50"
              >
                {actionLoading ? 'جارٍ الحفظ...' : 'تأكيد الإعادة'}
              </button>
            </div>
          </form>
        </Modal>
      )}

      {/* Release Review Lock Reason Dialog */}
      {showReleaseDialog && (
        <Modal
          isOpen={showReleaseDialog}
          onClose={() => setShowReleaseDialog(false)}
          title="فك حجز المراجعة"
          maxWidth="md"
        >
          <form onSubmit={handleReleaseReview} className="space-y-4" dir="rtl">
            <div className="rounded-xl border border-amber-500/30 bg-amber-500/10 p-3 text-xs text-amber-300">
              سيتم إلغاء قفل المراجعة وتصبح المذكرة متاحة لمراجعين آخرين. يرجى توضيح سبب فك الحجز (حقل إلزامي).
            </div>
            <div>
              <label className="block text-xs font-semibold text-slate-300 mb-1">سبب فك الحجز *</label>
              <input
                type="text"
                required
                value={releaseReason}
                onChange={(e) => setReleaseReason(e.target.value)}
                placeholder="مثال: تعذر استكمال التدقيق حالياً / تحويل لزميل آخر..."
                className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-xs text-slate-100 placeholder-slate-500 focus:border-teal-500 focus:outline-none"
              />
            </div>
            <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-800">
              <button
                type="button"
                onClick={() => setShowReleaseDialog(false)}
                className="px-3 py-1.5 text-xs rounded-lg border border-slate-700 text-slate-300 hover:bg-slate-800"
              >
                إلغاء
              </button>
              <button
                type="submit"
                disabled={actionLoading || !releaseReason.trim()}
                className="px-4 py-1.5 text-xs font-bold rounded-lg bg-amber-600 text-white hover:bg-amber-500 disabled:opacity-50"
              >
                {actionLoading ? 'جارٍ الحفظ...' : 'تأكيد فك الحجز'}
              </button>
            </div>
          </form>
        </Modal>
      )}

      {/* Inline Viewer Modal (Embedded PDF & Image Viewer) */}
      {showAttachmentViewer && note && (() => {
        const currentAtt = attachments[selectedAttachmentIndex] || (note.attachmentFileName ? {
          id: '',
          fileName: note.attachmentFileName,
          contentType: note.attachmentContentType || 'application/pdf',
          fileSize: 0,
          createdAt: note.createdAt
        } : null);
        const targetUrl = currentAtt && currentAtt.id ? api.getAttachmentUrl(note.id, currentAtt.id) : api.getAttachmentUrl(note.id);
        const isPdf = currentAtt ? (currentAtt.contentType.includes('pdf') || currentAtt.fileName.toLowerCase().endsWith('.pdf')) : false;
        const isImage = currentAtt ? (currentAtt.contentType.startsWith('image/') || /\.(png|jpe?g|webp|gif|bmp)$/i.test(currentAtt.fileName)) : true;

        return (
          <Modal
            isOpen={showAttachmentViewer}
            onClose={() => setShowAttachmentViewer(false)}
            title={`معاينة المرفق: ${currentAtt?.fileName || note.attachmentFileName || note.noteNumber}`}
            maxWidth="6xl"
          >
            <div className="space-y-3" dir="rtl">
              {/* Attachment Switcher Tabs if multiple */}
              {attachments.length > 1 && (
                <div className="flex items-center gap-1.5 overflow-x-auto pb-2 border-b border-slate-800">
                  <span className="text-xs text-slate-400 flex-shrink-0 ml-2">المرفقات ({attachments.length}):</span>
                  {attachments.map((att, idx) => {
                    const isCurrent = idx === selectedAttachmentIndex;
                    const isAttPdf = att.contentType.includes('pdf') || att.fileName.toLowerCase().endsWith('.pdf');
                    return (
                      <button
                        key={att.id}
                        type="button"
                        onClick={() => {
                          setSelectedAttachmentIndex(idx);
                          resetImageTransform();
                        }}
                        className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all whitespace-nowrap ${
                          isCurrent
                            ? 'bg-teal-600 text-white shadow-md'
                            : 'bg-slate-800 text-slate-300 hover:bg-slate-700 border border-slate-700'
                        }`}
                      >
                        <span>{isAttPdf ? '📄' : '🖼️'}</span>
                        <span className="max-w-[160px] truncate">{att.fileName}</span>
                      </button>
                    );
                  })}
                </div>
              )}

              {/* Image Transform Toolbar if viewing an image */}
              {isImage && (
                <div className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 bg-slate-900/90 rounded-xl border border-slate-800 text-xs">
                  <div className="flex items-center gap-2">
                    <span className="text-[11px] text-slate-400 font-semibold">تدوير الصورة:</span>
                    <button
                      type="button"
                      onClick={() => setImageRotation((r) => (r - 90 + 360) % 360)}
                      className="px-2.5 py-1 rounded-lg bg-slate-800 hover:bg-slate-750 text-slate-200 border border-slate-700 text-xs font-semibold flex items-center gap-1 cursor-pointer transition-colors"
                      title="تدوير 90 درجة إلى اليسار"
                    >
                      <span>⟲</span>
                      <span>يسار (90°)</span>
                    </button>
                    <button
                      type="button"
                      onClick={() => setImageRotation((r) => (r + 90) % 360)}
                      className="px-2.5 py-1 rounded-lg bg-slate-800 hover:bg-slate-750 text-slate-200 border border-slate-700 text-xs font-semibold flex items-center gap-1 cursor-pointer transition-colors"
                      title="تدوير 90 درجة إلى اليمين"
                    >
                      <span>⟳</span>
                      <span>يمين (90°)</span>
                    </button>
                    {imageRotation !== 0 && (
                      <span className="text-[11px] font-mono text-teal-400">({imageRotation}°)</span>
                    )}
                  </div>

                  <div className="flex items-center gap-2">
                    <span className="text-[11px] text-slate-400 font-semibold">التقريب:</span>
                    <button
                      type="button"
                      disabled={imageZoom <= 0.5}
                      onClick={() => setImageZoom((z) => Math.max(0.5, Number((z - 0.25).toFixed(2))))}
                      className="w-7 h-7 rounded-lg bg-slate-800 hover:bg-slate-750 text-slate-200 border border-slate-700 text-sm font-bold disabled:opacity-40 flex items-center justify-center cursor-pointer transition-colors"
                      title="تصغير (-25%)"
                    >
                      -
                    </button>
                    <span className="text-xs font-mono text-teal-300 w-12 text-center">
                      {Math.round(imageZoom * 100)}%
                    </span>
                    <button
                      type="button"
                      disabled={imageZoom >= 3.0}
                      onClick={() => setImageZoom((z) => Math.min(3.0, Number((z + 0.25).toFixed(2))))}
                      className="w-7 h-7 rounded-lg bg-slate-800 hover:bg-slate-750 text-slate-200 border border-slate-700 text-sm font-bold disabled:opacity-40 flex items-center justify-center cursor-pointer transition-colors"
                      title="تكبير (+25%)"
                    >
                      +
                    </button>
                    {(imageZoom !== 1 || imageRotation !== 0) && (
                      <button
                        type="button"
                        onClick={resetImageTransform}
                        className="px-2.5 py-1 rounded-lg bg-teal-600/20 text-teal-300 border border-teal-500/30 text-xs font-semibold hover:bg-teal-600/30 cursor-pointer transition-colors"
                        title="إعادة تعيين الوضع الافتراضي"
                      >
                        إعادة تعيين
                      </button>
                    )}
                  </div>
                </div>
              )}

              {/* Embedded Inline Viewer */}
              <div className="w-full flex items-center justify-center rounded-xl bg-slate-950/80 border border-slate-800 p-2 min-h-[450px] overflow-auto max-h-[78vh]">
                {isPdf ? (
                  <iframe
                    src={targetUrl}
                    title={currentAtt?.fileName || 'عرض مستند PDF'}
                    className="w-full h-[78vh] rounded-lg border border-slate-700 bg-white"
                  />
                ) : isImage ? (
                  <img
                    src={targetUrl}
                    alt={currentAtt?.fileName || 'مرفق صورة'}
                    style={{
                      transform: `rotate(${imageRotation}deg) scale(${imageZoom})`,
                      transformOrigin: 'center center',
                      transition: 'transform 0.2s ease',
                    }}
                    className="max-h-[72vh] max-w-full rounded-lg object-contain mx-auto shadow-2xl"
                  />
                ) : (
                  <iframe
                    src={targetUrl}
                    title={currentAtt?.fileName || 'عرض المرفق'}
                    className="w-full h-[78vh] rounded-lg border border-slate-700 bg-white"
                  />
                )}
              </div>

              {/* Modal Footer with Actions */}
              <div className="flex items-center justify-between pt-2 border-t border-slate-800">
                <span className="text-xs text-slate-400">
                  {currentAtt?.fileName} {currentAtt?.fileSize ? `(${(currentAtt.fileSize / 1024).toFixed(0)} KB)` : ''}
                </span>

                <div className="flex items-center gap-2">
                  <a
                    href={`${targetUrl}${targetUrl.includes('?') ? '&' : '?'}download=true`}
                    download={currentAtt?.fileName || 'مرفق'}
                    className="px-3.5 py-1.5 text-xs font-semibold rounded-lg bg-slate-800 border border-slate-700 text-slate-300 hover:bg-slate-700 transition-colors"
                  >
                    تحميل نسخة
                  </a>
                  <button
                    type="button"
                    onClick={() => setShowAttachmentViewer(false)}
                    className="px-4 py-1.5 text-xs font-semibold rounded-lg bg-teal-600 text-white hover:bg-teal-500 transition-colors"
                  >
                    إغلاق المعاينة
                  </button>
                </div>
              </div>
            </div>
          </Modal>
        );
      })()}

      {/* Note Links Modal */}
      {showLinksModal && note && (
        <MemoLinksModal
          isOpen={showLinksModal}
          onClose={() => setShowLinksModal(false)}
          noteId={note.id}
          noteNumber={note.noteNumber}
          allowChanges={canEdit}
          onViewNote={(targetId) => {
            setShowLinksModal(false);
            // Switch current view to target note
            api.getNote(targetId).then((n) => setNote(n)).catch(console.error);
          }}
          onLinksChanged={fetchNote}
        />
      )}

      {/* Note Source Modal */}
      {showNoteSourceModal && note && (
        <NoteSourceModal
          isOpen={showNoteSourceModal}
          onClose={() => setShowNoteSourceModal(false)}
          noteId={note.id}
          noteNumber={note.noteNumber}
          readOnly={!canEdit || note.status === 'APPROVED'}
          onOpenLinksModal={() => {
            setShowNoteSourceModal(false);
            setShowLinksModal(true);
          }}
          onSourceChanged={fetchNote}
        />
      )}
    </>
  );
};

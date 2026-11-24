import React, { useEffect, useState, useCallback } from 'react';
import { api } from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import { MemoEditorModal } from '../memos/MemoEditorModal';
import { MemoViewModal } from '../memos/MemoViewModal';
import { MemoLinksModal } from '../memos/MemoLinksModal';
import { Modal } from '../../components/common/Modal';
import { type ReviewQueueItemModel, type NoteModel, normalizeNoteStatus, IndicatorValue } from '../../types/memo';

export const ReviewQueuePage: React.FC = () => {
  const { hasRole, user } = useAuth();
  const canReview = hasRole('ADMIN', 'REVIEWER');

  const [items, setItems] = useState<ReviewQueueItemModel[]>([]);
  const [loading, setLoading] = useState(false);
  const [filterText, setFilterText] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('ALL');

  // Selected note detail state
  const [selectedItem, setSelectedItem] = useState<ReviewQueueItemModel | null>(null);
  const [selectedNote, setSelectedNote] = useState<NoteModel | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);

  // Return dialog state
  const [isReturnModalOpen, setIsReturnModalOpen] = useState(false);
  const [returnReason, setReturnReason] = useState('');
  const [submittingAction, setSubmittingAction] = useState(false);

  // Editor Modal
  const [isEditorOpen, setIsEditorOpen] = useState(false);
  const [editNoteId, setEditNoteId] = useState<string | null>(null);

  // Viewer Modal & Links Modal
  const [viewModalNoteId, setViewModalNoteId] = useState<string | null>(null);
  const [linksModalNote, setLinksModalNote] = useState<{ id: string; number: string } | null>(null);

  const fetchQueue = useCallback(async () => {
    setLoading(true);
    try {
      const data = await api.getReviewQueueItems();
      setItems(data);
    } catch (err) {
      console.error('Failed to load review queue', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchQueue();
  }, [fetchQueue]);

  const loadNoteDetail = async (item: ReviewQueueItemModel) => {
    setSelectedItem(item);
    setLoadingDetail(true);
    try {
      const note = await api.getNoteById(item.noteId);
      setSelectedNote(note);
    } catch (err) {
      console.error('Failed to load note details', err);
      alert('فشل تحميل تفاصيل المذكرة.');
    } finally {
      setLoadingDetail(false);
    }
  };

  const handleStartReview = async (item: ReviewQueueItemModel) => {
    setSubmittingAction(true);
    try {
      await api.startReview(item.noteId, item.updatedAt);
      await fetchQueue();
      if (selectedItem?.noteId === item.noteId) {
        await loadNoteDetail(item);
      }
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل بدء التدقيق.');
    } finally {
      setSubmittingAction(false);
    }
  };

  const handleApprove = async (item: ReviewQueueItemModel) => {
    if (!window.confirm(`هل أنت متأكد من اعتماد المذكرة رقم ${item.noteNumber} بشكل نهائي؟`)) return;
    setSubmittingAction(true);
    try {
      await api.approveNote(item.noteId, item.updatedAt);
      alert(`تم اعتماد المذكرة رقم ${item.noteNumber} بنجاح.`);
      setSelectedNote(null);
      setSelectedItem(null);
      await fetchQueue();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل اعتماد المذكرة.');
    } finally {
      setSubmittingAction(false);
    }
  };

  const handleOpenReturn = (item: ReviewQueueItemModel) => {
    setSelectedItem(item);
    setReturnReason('');
    setIsReturnModalOpen(true);
  };

  const submitReturn = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedItem || !returnReason.trim()) return;
    setSubmittingAction(true);
    try {
      await api.returnNote(selectedItem.noteId, returnReason.trim(), selectedItem.updatedAt);
      setIsReturnModalOpen(false);
      setReturnReason('');
      setSelectedNote(null);
      setSelectedItem(null);
      await fetchQueue();
      alert('تمت إعادة المذكرة لمدخل البيانات مع سبب الإرجاع.');
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل إعادة المذكرة.');
    } finally {
      setSubmittingAction(false);
    }
  };

  const handleReleaseReview = async (item: ReviewQueueItemModel) => {
    const reason = window.prompt('سبب فك حجز المراجعة (إلزامي):', '');
    if (reason === null) return;
    if (!reason.trim()) {
      alert('سبب فك حجز المراجعة مطلوب إلزامياً.');
      return;
    }
    setSubmittingAction(true);
    try {
      await api.releaseReview(item.noteId, reason.trim());
      await fetchQueue();
      if (selectedItem?.noteId === item.noteId) {
        await loadNoteDetail(item);
      }
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل إلغاء الحجز.');
    } finally {
      setSubmittingAction(false);
    }
  };

  const filteredItems = items.filter((item) => {
    if (statusFilter !== 'ALL' && item.status !== statusFilter) return false;
    if (!filterText.trim()) return true;
    const term = filterText.toLowerCase();
    return (
      item.noteNumber.toLowerCase().includes(term) ||
      item.companyName.toLowerCase().includes(term) ||
      item.noteTypeName.toLowerCase().includes(term) ||
      (item.createdByName && item.createdByName.toLowerCase().includes(term)) ||
      (item.reviewerName && item.reviewerName.toLowerCase().includes(term))
    );
  });

  const getStatusBadge = (status: unknown) => {
    const normalized = normalizeNoteStatus(status);
    switch (normalized) {
      case 'APPROVED':
        return <span className="px-2.5 py-1 text-xs rounded-full bg-emerald-100 dark:bg-emerald-950/60 text-emerald-800 dark:text-emerald-300 font-semibold">معتمدة</span>;
      case 'UNDER_REVIEW':
        return <span className="px-2.5 py-1 text-xs rounded-full bg-amber-100 dark:bg-amber-950/60 text-amber-800 dark:text-amber-300 font-semibold">قيد التدقيق</span>;
      case 'RETURNED_FOR_CORRECTION':
        return <span className="px-2.5 py-1 text-xs rounded-full bg-rose-100 dark:bg-rose-950/60 text-rose-800 dark:text-rose-300 font-semibold">معادة للتصحيح</span>;
      case 'NEEDS_REVIEW':
        return <span className="px-2.5 py-1 text-xs rounded-full bg-blue-100 dark:bg-blue-950/60 text-blue-800 dark:text-blue-300 font-semibold">بانتظار التدقيق</span>;
      case 'HAS_ERRORS':
        return <span className="px-2.5 py-1 text-xs rounded-full bg-orange-100 dark:bg-orange-950/60 text-orange-800 dark:text-orange-300 font-semibold">تحوي أخطاء</span>;
      case 'COMPLETE':
        return <span className="px-2.5 py-1 text-xs rounded-full bg-teal-100 dark:bg-teal-950/60 text-teal-800 dark:text-teal-300 font-semibold">مكتملة</span>;
      case 'DRAFT':
        return <span className="px-2.5 py-1 text-xs rounded-full bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 font-semibold">مسودة</span>;
      default:
        return <span className="px-2.5 py-1 text-xs rounded-full bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 font-semibold">غير معروف</span>;
    }
  };

  const getIndicatorLabel = (val: IndicatorValue) => {
    switch (val) {
      case IndicatorValue.Correct:
        return <span className="text-xs px-2 py-0.5 rounded bg-emerald-100 text-emerald-800 dark:bg-emerald-900/50 dark:text-emerald-300">سليم</span>;
      case IndicatorValue.Incorrect:
        return <span className="text-xs px-2 py-0.5 rounded bg-rose-100 text-rose-800 dark:bg-rose-900/50 dark:text-rose-300">غير سليم</span>;
      case IndicatorValue.Missing:
        return <span className="text-xs px-2 py-0.5 rounded bg-amber-100 text-amber-800 dark:bg-amber-900/50 dark:text-amber-300">مفقود</span>;
      case IndicatorValue.Unclear:
        return <span className="text-xs px-2 py-0.5 rounded bg-purple-100 text-purple-800 dark:bg-purple-900/50 dark:text-purple-300">غير واضح</span>;
      case IndicatorValue.Incomplete:
        return <span className="text-xs px-2 py-0.5 rounded bg-orange-100 text-orange-800 dark:bg-orange-900/50 dark:text-orange-300">غير مكتمل</span>;
      case IndicatorValue.NotApplicable:
        return <span className="text-xs px-2 py-0.5 rounded bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300">لا ينطبق</span>;
      default:
        return <span className="text-xs px-2 py-0.5 rounded bg-slate-100 text-slate-500">لم يدقق</span>;
    }
  };

  return (
    <div className="flex flex-col gap-6 p-6 min-h-screen app-bg" dir="rtl">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-4 border-b app-border pb-4">
        <div>
          <h1 className="text-2xl font-bold app-text flex items-center gap-3">
            <span>🛡️</span>
            <span>طابور المراجعة والتدقيق</span>
            <span className="text-sm font-normal px-2.5 py-0.5 rounded-full bg-blue-100 dark:bg-blue-950 text-blue-800 dark:text-blue-300">
              {filteredItems.length} مذكرة
            </span>
          </h1>
          <p className="text-sm app-text-muted mt-1">
            مراجعة وتدقيق مذكرات التفتيش والتحقق من المؤشرات واعتمادها أو إعادتها للتصحيح.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={fetchQueue}
            disabled={loading}
            className="px-4 py-2 text-sm font-semibold rounded-lg border app-border app-surface-muted app-hover app-text transition-colors"
          >
            {loading ? 'جارٍ التحديث...' : 'تحديث القائمة'}
          </button>
        </div>
      </div>

      {/* Filter and Search */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 app-surface p-4 rounded-xl border app-border shadow-sm">
        <div className="md:col-span-2">
          <label className="block text-xs font-semibold app-text-muted mb-1">بحث سريع</label>
          <input
            type="text"
            value={filterText}
            onChange={(e) => setFilterText(e.target.value)}
            placeholder="ابحث برقم المذكرة، الشركة، نوع المذكرة، أو اسم المدخل..."
            className="w-full px-3 py-2 text-sm rounded-lg border app-border app-surface app-text focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div>
          <label className="block text-xs font-semibold app-text-muted mb-1">تصفية حسب الحالة</label>
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="w-full px-3 py-2 text-sm rounded-lg border app-border app-surface app-text focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            <option value="ALL">جميع الحالات المعروضة</option>
            <option value="NEEDS_REVIEW">بانتظار التدقيق</option>
            <option value="UNDER_REVIEW">قيد التدقيق حالياً</option>
            <option value="RETURNED_FOR_CORRECTION">معادة للتصحيح</option>
          </select>
        </div>
      </div>

      {/* Main Content: Split Grid */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* Table List */}
        <div className={`transition-all duration-200 ${selectedNote ? 'lg:col-span-6' : 'lg:col-span-12'}`}>
          <div className="app-surface rounded-xl border app-border overflow-hidden shadow-sm">
            <div className="overflow-x-auto">
              <table className="w-full text-right text-sm border-collapse">
                <thead>
                  <tr className="border-b app-border bg-slate-50/50 dark:bg-slate-900/50 text-xs font-semibold app-text-muted">
                    <th className="p-3">رقم المذكرة</th>
                    <th className="p-3">الشركة / المعمل</th>
                    <th className="p-3">النوع</th>
                    <th className="p-3">تاريخ الوصول</th>
                    <th className="p-3">الحالة</th>
                    <th className="p-3">المدقق</th>
                    <th className="p-3 text-center">الإجراءات</th>
                  </tr>
                </thead>
                <tbody className="divide-y app-border">
                  {loading ? (
                    <tr>
                      <td colSpan={7} className="p-8 text-center app-text-muted">
                        جارٍ تحميل طابور التدقيق...
                      </td>
                    </tr>
                  ) : filteredItems.length === 0 ? (
                    <tr>
                      <td colSpan={7} className="p-8 text-center app-text-muted">
                        لا توجد مذكرات بانتظار التدقيق حالياً.
                      </td>
                    </tr>
                  ) : (
                    filteredItems.map((item) => {
                      const isSelected = selectedItem?.noteId === item.noteId;
                      const isMyReview = item.reviewerId === user?.id;
                      return (
                        <tr
                          key={item.noteId}
                          onClick={() => loadNoteDetail(item)}
                          className={`cursor-pointer transition-colors ${
                            isSelected
                              ? 'bg-blue-50/70 dark:bg-blue-950/30'
                              : 'hover:bg-slate-50 dark:hover:bg-slate-800/50'
                          }`}
                        >
                          <td className="p-3 font-bold app-text">
                            <span className="font-mono text-blue-600 dark:text-blue-400">
                              {item.noteNumber}
                            </span>
                          </td>
                          <td className="p-3 app-text">
                            <div className="font-medium">{item.companyName}</div>
                            {item.factoryName && (
                              <div className="text-xs app-text-muted">{item.factoryName}</div>
                            )}
                          </td>
                          <td className="p-3 app-text-muted text-xs">{item.noteTypeName}</td>
                          <td className="p-3 font-mono text-xs app-text-muted">
                            {item.arrivalAt ? item.arrivalAt.slice(0, 10) : '—'}
                          </td>
                          <td className="p-3">{getStatusBadge(item.status)}</td>
                          <td className="p-3 text-xs">
                            {item.reviewerName ? (
                              <span className={`font-semibold ${isMyReview ? 'text-blue-600 dark:text-blue-400' : 'app-text-muted'}`}>
                                {item.reviewerName} {isMyReview && '(أنت)'}
                              </span>
                            ) : (
                              <span className="text-slate-400">—</span>
                            )}
                          </td>
                          <td className="p-3 text-center" onClick={(e) => e.stopPropagation()}>
                            <div className="flex items-center justify-center gap-1">
                              <button
                                type="button"
                                onClick={() => setViewModalNoteId(item.noteId)}
                                title="عرض شامل للمذكرة"
                                className="p-1.5 rounded-lg border app-border app-hover app-text text-xs cursor-pointer"
                              >
                                👁️
                              </button>
                              {canReview && item.status !== 'UNDER_REVIEW' && (
                                <button
                                  type="button"
                                  onClick={() => handleStartReview(item)}
                                  disabled={submittingAction}
                                  title="بدء التدقيق"
                                  className="px-2 py-1 rounded-lg bg-blue-600 text-white hover:bg-blue-700 text-xs font-semibold"
                                >
                                  تدقيق
                                </button>
                              )}
                              {canReview && isMyReview && (
                                <>
                                  <button
                                    type="button"
                                    onClick={() => handleApprove(item)}
                                    disabled={submittingAction}
                                    title="اعتماد نهائي"
                                    className="px-2 py-1 rounded-lg bg-emerald-600 text-white hover:bg-emerald-700 text-xs font-semibold"
                                  >
                                    اعتماد
                                  </button>
                                  <button
                                    type="button"
                                    onClick={() => handleOpenReturn(item)}
                                    disabled={submittingAction}
                                    title="إعادة للتصحيح"
                                    className="px-2 py-1 rounded-lg bg-rose-600 text-white hover:bg-rose-700 text-xs font-semibold"
                                  >
                                    إرجاع
                                  </button>
                                </>
                              )}
                            </div>
                          </td>
                        </tr>
                      );
                    })
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>

        {/* Note Details Side Drawer / Card */}
        {selectedItem && (
          <div className="lg:col-span-6 flex flex-col gap-4">
            <div className="app-surface rounded-xl border app-border p-5 shadow-sm">
              <div className="flex items-center justify-between border-b app-border pb-3 mb-4">
                <div className="flex items-center gap-3">
                  <h2 className="text-lg font-bold app-text">
                    مذكرة رقم: <span className="font-mono text-blue-600 dark:text-blue-400">{selectedItem.noteNumber}</span>
                  </h2>
                  {getStatusBadge(selectedItem.status)}
                </div>
                <button
                  type="button"
                  onClick={() => {
                    setSelectedNote(null);
                    setSelectedItem(null);
                  }}
                  className="text-slate-400 hover:text-slate-600 text-lg font-bold"
                >
                  ✕
                </button>
              </div>

              {loadingDetail ? (
                <div className="p-8 text-center app-text-muted">جارٍ جلب تفاصيل المذكرة...</div>
              ) : selectedNote ? (
                <div className="space-y-4">
                  {/* Meta Information Cards */}
                  <div className="grid grid-cols-2 gap-3 text-xs bg-slate-50 dark:bg-slate-900/50 p-3 rounded-lg border app-border">
                    <div>
                      <span className="app-text-muted block">الشركة:</span>
                      <span className="font-bold app-text">{selectedNote.companyName}</span>
                    </div>
                    <div>
                      <span className="app-text-muted block">المعمل / الخط:</span>
                      <span className="font-semibold app-text">
                        {selectedNote.factoryName || '—'} {selectedNote.productionLineName ? `(${selectedNote.productionLineName})` : ''}
                      </span>
                    </div>
                    <div>
                      <span className="app-text-muted block">نوع المذكرة:</span>
                      <span className="font-semibold app-text">{selectedNote.noteTypeName}</span>
                    </div>
                    <div>
                      <span className="app-text-muted block">تاريخ الوصول:</span>
                      <span className="font-mono app-text">{selectedNote.arrivalAt ? selectedNote.arrivalAt.slice(0, 10) : '—'}</span>
                    </div>
                    <div>
                      <span className="app-text-muted block">المدخل:</span>
                      <span className="app-text">{selectedNote.createdByName || '—'}</span>
                    </div>
                    <div>
                      <span className="app-text-muted block">المدقق الحالي:</span>
                      <span className="app-text font-semibold">{selectedNote.reviewerName || 'غير محجوز'}</span>
                    </div>
                  </div>

                  {/* Attachment Box */}
                  <div className="flex items-center justify-between p-3 rounded-lg border app-border bg-slate-50 dark:bg-slate-900/30">
                    <div className="flex items-center gap-2">
                      <span className="text-xl">📎</span>
                      <div>
                        <div className="text-xs font-semibold app-text">
                          {selectedNote.attachmentFileName || 'مستند المرفق (PDF/صورة)'}
                        </div>
                        <div className="text-[10px] app-text-muted">
                          {selectedNote.attachmentContentType || 'مخزن في قاعدة البيانات'}
                        </div>
                      </div>
                    </div>
                    <a
                      href={api.getAttachmentUrl(selectedNote.id)}
                      target="_blank"
                      rel="noreferrer"
                      className="px-3 py-1.5 text-xs font-semibold rounded-lg bg-blue-600 text-white hover:bg-blue-700 transition-colors"
                    >
                      فتح / تحميل المرفق
                    </a>
                  </div>

                  {/* Notes / Remarks */}
                  {selectedNote.notes && (
                    <div className="text-xs bg-amber-50 dark:bg-amber-950/20 border border-amber-200 dark:border-amber-900/50 p-3 rounded-lg">
                      <span className="font-bold text-amber-900 dark:text-amber-300 block mb-1">ملاحظات:</span>
                      <p className="app-text whitespace-pre-wrap">{selectedNote.notes}</p>
                    </div>
                  )}

                  {/* Return reason if returned */}
                  {selectedNote.returnReason && (
                    <div className="text-xs bg-rose-50 dark:bg-rose-950/20 border border-rose-200 dark:border-rose-900/50 p-3 rounded-lg">
                      <span className="font-bold text-rose-900 dark:text-rose-300 block mb-1">سبب الإرجاع:</span>
                      <p className="text-rose-800 dark:text-rose-200">{selectedNote.returnReason}</p>
                    </div>
                  )}

                  {/* Indicator Fields Table */}
                  <div>
                    <h3 className="text-xs font-bold app-text-muted uppercase tracking-wider mb-2">
                      المؤشرات وحقول التحقق ({selectedNote.fieldValues.length})
                    </h3>
                    <div className="max-h-64 overflow-y-auto border app-border rounded-lg">
                      <table className="w-full text-right text-xs border-collapse">
                        <thead className="bg-slate-50 dark:bg-slate-900/60 border-b app-border">
                          <tr>
                            <th className="p-2">المؤشر</th>
                            <th className="p-2">الحالة</th>
                            <th className="p-2">ملاحظة / سبب</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y app-border">
                          {selectedNote.fieldValues.map((fv) => (
                            <tr key={fv.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/30">
                              <td className="p-2 font-medium app-text">{fv.fieldName}</td>
                              <td className="p-2">{getIndicatorLabel(fv.fieldValue)}</td>
                              <td className="p-2 text-slate-400 text-[11px]">{fv.inactiveReason || '—'}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>

                  {/* Actions Toolbar */}
                  <div className="pt-4 border-t app-border flex flex-wrap items-center justify-between gap-2">
                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={() => setViewModalNoteId(selectedNote.id)}
                        className="px-3 py-1.5 text-xs font-semibold rounded-lg bg-teal-600/20 border border-teal-500/30 text-teal-300 hover:bg-teal-600/30 cursor-pointer"
                      >
                        👁️ عرض شامل
                      </button>
                      <button
                        type="button"
                        onClick={() => setLinksModalNote({ id: selectedNote.id, number: selectedNote.noteNumber })}
                        className="px-3 py-1.5 text-xs font-semibold rounded-lg border app-border app-surface-muted app-hover app-text cursor-pointer"
                      >
                        🔗 المذكرات المرتبطة
                      </button>
                      <button
                        type="button"
                        onClick={() => {
                          setEditNoteId(selectedNote.id);
                          setIsEditorOpen(true);
                        }}
                        className="px-3 py-1.5 text-xs font-semibold rounded-lg border app-border app-surface-muted app-hover app-text cursor-pointer"
                      >
                        ✏️ فتح للتعديل
                      </button>
                    </div>
                    {canReview && (
                      <div className="flex items-center gap-2">
                        {selectedNote.status === 'UNDER_REVIEW' && selectedNote.reviewerId === user?.id ? (
                          <>
                            <button
                              type="button"
                              onClick={() => handleReleaseReview(selectedItem)}
                              disabled={submittingAction}
                              className="px-3 py-1.5 text-xs font-semibold rounded-lg border border-amber-300 text-amber-700 dark:border-amber-800 dark:text-amber-300 app-hover"
                            >
                              إلغاء حجز التدقيق
                            </button>
                            <button
                              type="button"
                              onClick={() => handleOpenReturn(selectedItem)}
                              disabled={submittingAction}
                              className="px-3 py-1.5 text-xs font-semibold rounded-lg bg-rose-600 text-white hover:bg-rose-700"
                            >
                              إرجاع للتصحيح
                            </button>
                            <button
                              type="button"
                              onClick={() => handleApprove(selectedItem)}
                              disabled={submittingAction}
                              className="px-4 py-1.5 text-xs font-semibold rounded-lg bg-emerald-600 text-white hover:bg-emerald-700"
                            >
                              ✓ اعتماد المذكرة
                            </button>
                          </>
                        ) : (
                          <button
                            type="button"
                            onClick={() => handleStartReview(selectedItem)}
                            disabled={submittingAction}
                            className="px-4 py-1.5 text-xs font-semibold rounded-lg bg-blue-600 text-white hover:bg-blue-700"
                          >
                            بدء التدقيق الآن
                          </button>
                        )}
                      </div>
                    )}
                  </div>
                </div>
              ) : null}
            </div>
          </div>
        )}
      </div>

      {/* Return With Reason Dialog */}
      <Modal
        isOpen={isReturnModalOpen}
        onClose={() => setIsReturnModalOpen(false)}
        title="إعادة المذكرة للتصحيح"
      >
        <form onSubmit={submitReturn} className="space-y-4">
          <p className="text-sm app-text">
            يرجى تحديد سبب الإرجاع لمدخل البيانات بوضوح ليتمكن من معالجة الخلل:
          </p>
          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">سبب الإرجاع *</label>
            <textarea
              value={returnReason}
              onChange={(e) => setReturnReason(e.target.value)}
              rows={4}
              required
              placeholder="مثال: مؤشرات وزن العبوة غير مكتملة، يرجى إعادة إدخال بيانات التفتيش وإرفاق الملاحظة التوضيحية..."
              className="w-full px-3 py-2 text-sm rounded-lg border app-border app-surface app-text focus:outline-none focus:ring-2 focus:ring-rose-500"
            />
          </div>
          <div className="flex justify-end gap-3 pt-3 border-t app-border">
            <button
              type="button"
              onClick={() => setIsReturnModalOpen(false)}
              className="px-4 py-2 text-sm font-semibold rounded-lg border app-border app-surface-muted app-hover app-text"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={submittingAction || !returnReason.trim()}
              className="px-5 py-2 text-sm font-semibold rounded-lg bg-rose-600 text-white hover:bg-rose-700 disabled:opacity-60"
            >
              {submittingAction ? 'جارٍ الإرسال...' : 'تأكيد الإرجاع للتصحيح'}
            </button>
          </div>
        </form>
      </Modal>

      {/* Editor Modal for editing note */}
      {isEditorOpen && (
        <MemoEditorModal
          isOpen={isEditorOpen}
          noteId={editNoteId}
          onClose={() => {
            setIsEditorOpen(false);
            setEditNoteId(null);
          }}
          onSaved={() => {
            setIsEditorOpen(false);
            setEditNoteId(null);
            fetchQueue();
            if (selectedItem) loadNoteDetail(selectedItem);
          }}
        />
      )}

      {/* Comprehensive Memo Viewer Modal */}
      <MemoViewModal
        isOpen={Boolean(viewModalNoteId)}
        noteId={viewModalNoteId}
        onClose={() => setViewModalNoteId(null)}
        onEditNote={(id) => {
          setViewModalNoteId(null);
          setEditNoteId(id);
          setIsEditorOpen(true);
        }}
        onStatusChanged={() => {
          fetchQueue();
          if (selectedItem) loadNoteDetail(selectedItem);
        }}
      />

      {/* Memo Links Modal */}
      {linksModalNote && (
        <MemoLinksModal
          isOpen={Boolean(linksModalNote)}
          onClose={() => setLinksModalNote(null)}
          noteId={linksModalNote.id}
          noteNumber={linksModalNote.number}
          allowChanges={hasRole('ADMIN', 'DATA_ENTRY')}
          onViewNote={(id) => {
            setLinksModalNote(null);
            setViewModalNoteId(id);
          }}
          onLinksChanged={() => {
            fetchQueue();
            if (selectedItem) loadNoteDetail(selectedItem);
          }}
        />
      )}
    </div>
  );
};
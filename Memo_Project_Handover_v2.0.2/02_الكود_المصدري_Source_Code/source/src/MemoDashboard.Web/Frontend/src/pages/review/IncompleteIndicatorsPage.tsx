import React, { useEffect, useState, useCallback } from 'react';
import { api } from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import { MemoEditorModal } from '../memos/MemoEditorModal';
import { MemoViewModal } from '../memos/MemoViewModal';
import { Modal } from '../../components/common/Modal';
import { type IncompleteIndicatorQueueItemModel, IndicatorValue } from '../../types/memo';
import { translateStatus, statusBadgeClass } from '../../services/translations';

export const IncompleteIndicatorsPage: React.FC = () => {
  const { hasRole } = useAuth();
  const canEdit = hasRole('ADMIN', 'REVIEWER', 'DATA_ENTRY');

  const [items, setItems] = useState<IncompleteIndicatorQueueItemModel[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(20);
  const [loading, setLoading] = useState(false);

  // Filters
  const [search, setSearch] = useState('');

  // Complete Single Indicator Modal
  const [selectedItem, setSelectedItem] = useState<IncompleteIndicatorQueueItemModel | null>(null);
  const [targetValue, setTargetValue] = useState<IndicatorValue>(IndicatorValue.Correct);
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);

  // Full Editor Modal
  const [editorNoteId, setEditorNoteId] = useState<string | null>(null);

  // Memo Viewer Modal
  const [viewNoteId, setViewNoteId] = useState<string | null>(null);

  const fetchItems = useCallback(async () => {
    setLoading(true);
    try {
      const res = await api.getIncompleteIndicators({
        pageNumber,
        pageSize,
      });
      setItems(res.items);
      setTotalCount(res.totalCount);
      setTotalPages(res.totalPages);
    } catch (err) {
      console.error('Failed to load incomplete indicators', err);
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize]);

  useEffect(() => {
    fetchItems();
  }, [fetchItems]);

  const handleOpenCompleteModal = (item: IncompleteIndicatorQueueItemModel) => {
    setSelectedItem(item);
    setTargetValue(IndicatorValue.Correct);
    setReason('');
  };

  const isDefectiveValue = (val: IndicatorValue) => {
    return [
      IndicatorValue.Incorrect,
      IndicatorValue.Missing,
      IndicatorValue.Unclear,
      IndicatorValue.Incomplete,
    ].includes(val);
  };

  const submitCompletion = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedItem) return;

    // Reason/notes are always optional as requested

    setSubmitting(true);
    try {
      await api.completeIndicator({
        noteId: selectedItem.noteId,
        fieldId: selectedItem.fieldId,
        value: targetValue,
        expectedUpdatedAt: selectedItem.updatedAt,
        reason: reason.trim() || undefined,
      });
      setSelectedItem(null);
      fetchItems();
      alert('تم تحديث المؤشر بنجاح.');
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل تحديث المؤشر.');
    } finally {
      setSubmitting(false);
    }
  };

  const filteredItems = items.filter((item) => {
    if (!search.trim()) return true;
    const s = search.toLowerCase();
    return (
      item.noteNumber.toLowerCase().includes(s) ||
      item.companyName.toLowerCase().includes(s) ||
      item.fieldName.toLowerCase().includes(s) ||
      item.noteTypeName.toLowerCase().includes(s)
    );
  });

  return (
    <div className="flex flex-col gap-6 p-6 min-h-screen app-bg" dir="rtl">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-4 border-b app-border pb-4">
        <div>
          <h1 className="text-2xl font-bold app-text flex items-center gap-3">
            <span>⚠️</span>
            <span>طابور المؤشرات غير المكتملة</span>
            <span className="text-sm font-normal px-2.5 py-0.5 rounded-full bg-amber-100 dark:bg-amber-950 text-amber-800 dark:text-amber-300">
              {totalCount} مؤشر بانتظار الاستكمال
            </span>
          </h1>
          <p className="text-sm app-text-muted mt-1">
            قائمة حقول ومؤشرات التفتيش المعلقة أو غير المستكملة عبر مختلف المذكرات لتسريع استكمالها وتدقيقها وفق النظام المعتمد.
          </p>
        </div>
        <button
          type="button"
          onClick={fetchItems}
          disabled={loading}
          className="px-4 py-2 text-sm font-semibold rounded-lg border app-border app-surface-muted app-hover app-text transition-colors cursor-pointer"
        >
          {loading ? 'جارٍ التحديث...' : 'تحديث القائمة'}
        </button>
      </div>

      {/* Filter */}
      <div className="app-surface p-4 rounded-xl border app-border shadow-sm">
        <label className="block text-xs font-semibold app-text-muted mb-1">بحث وتصفية</label>
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="ابحث برقم المذكرة، الشركة، اسم المؤشر..."
          className="w-full max-w-lg px-3 py-2 text-sm rounded-lg border app-border app-surface app-text focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
      </div>

      {/* Table */}
      <div className="app-surface rounded-xl border app-border overflow-hidden shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full text-right text-sm border-collapse">
            <thead>
              <tr className="border-b app-border bg-slate-50/50 dark:bg-slate-900/50 text-xs font-semibold app-text-muted">
                <th className="p-3">رقم المذكرة</th>
                <th className="p-3">الشركة</th>
                <th className="p-3">نوع المذكرة</th>
                <th className="p-3">اسم المؤشر غير المكتمل</th>
                <th className="p-3">تاريخ الوصول</th>
                <th className="p-3">حالة المذكرة</th>
                <th className="p-3 text-center">الإجراءات</th>
              </tr>
            </thead>
            <tbody className="divide-y app-border">
              {loading ? (
                <tr>
                  <td colSpan={7} className="p-8 text-center app-text-muted">
                    جارٍ جلب المؤشرات غير المكتملة...
                  </td>
                </tr>
              ) : filteredItems.length === 0 ? (
                <tr>
                  <td colSpan={7} className="p-8 text-center app-text-muted">
                    {search ? 'لا توجد مؤشرات مطابقة لنص البحث.' : 'لا توجد مؤشرات غير مكتملة حالياً (جميع المذكرات مستوفية).'}
                  </td>
                </tr>
              ) : (
                filteredItems.map((item) => (
                  <tr key={`${item.noteId}_${item.fieldId}`} className="app-hover transition-colors">
                    <td className="p-3 font-mono font-bold">
                      <button
                        type="button"
                        onClick={() => setViewNoteId(item.noteId)}
                        className="text-blue-600 dark:text-blue-400 hover:underline"
                        title="عرض كامل للمذكرة"
                      >
                        {item.noteNumber}
                      </button>
                    </td>
                    <td className="p-3 font-medium app-text">{item.companyName}</td>
                    <td className="p-3 app-text-muted">{item.noteTypeName}</td>
                    <td className="p-3">
                      <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-semibold bg-amber-50 dark:bg-amber-950/30 text-amber-700 dark:text-amber-400 border border-amber-200 dark:border-amber-900/40">
                        <span>⚠️</span>
                        <span>{item.fieldName}</span>
                      </span>
                    </td>
                    <td className="p-3 font-mono text-xs app-text-muted">{item.arrivalAt?.slice(0, 10)}</td>
                    <td className="p-3">
                      <span className={`text-xs px-2.5 py-1 rounded-full border font-medium ${statusBadgeClass(item.status)}`}>
                        {translateStatus(item.status)}
                      </span>
                    </td>
                    <td className="p-3 text-center">
                      <div className="inline-flex items-center gap-2">
                        <button
                          type="button"
                          onClick={() => setViewNoteId(item.noteId)}
                          className="px-2.5 py-1 text-xs rounded-lg border app-border app-surface-muted app-hover app-text"
                          title="عرض تفاصيل المذكرة"
                        >
                          👁️ تفاصيل
                        </button>
                        {canEdit && (
                          <>
                            <button
                              type="button"
                              onClick={() => handleOpenCompleteModal(item)}
                              className="px-3 py-1 text-xs font-semibold rounded-lg bg-emerald-600 text-white hover:bg-emerald-700 shadow-sm"
                            >
                              استكمال التقييم
                            </button>
                            <button
                              type="button"
                              onClick={() => setEditorNoteId(item.noteId)}
                              className="px-2.5 py-1 text-xs rounded-lg border app-border app-surface-muted app-hover app-text"
                              title="فتح المذكرة بالكامل للتعديل"
                            >
                              ✏️ تعديل
                            </button>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="p-4 border-t app-border flex items-center justify-between text-xs app-text-muted">
            <div>
              الصفحة {pageNumber} من {totalPages} (إجمالي النتائج: {totalCount})
            </div>
            <div className="flex gap-1">
              <button
                type="button"
                disabled={pageNumber <= 1}
                onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
                className="px-3 py-1 rounded border app-border app-hover disabled:opacity-40 cursor-pointer"
              >
                السابق
              </button>
              <button
                type="button"
                disabled={pageNumber >= totalPages}
                onClick={() => setPageNumber((p) => Math.min(totalPages, p + 1))}
                className="px-3 py-1 rounded border app-border app-hover disabled:opacity-40 cursor-pointer"
              >
                التالي
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Complete Single Indicator Modal */}
      <Modal
        isOpen={Boolean(selectedItem)}
        onClose={() => setSelectedItem(null)}
        title="استكمال تقييم المؤشر"
      >
        {selectedItem && (
          <form onSubmit={submitCompletion} className="space-y-4" dir="rtl">
            <div className="bg-slate-50 dark:bg-slate-900/50 p-3 rounded-lg border app-border text-xs space-y-1">
              <div>المذكرة: <strong className="font-mono text-blue-600">{selectedItem.noteNumber}</strong> - {selectedItem.companyName}</div>
              <div>المؤشر المستهدف: <strong className="text-amber-600 dark:text-amber-400">{selectedItem.fieldName}</strong></div>
            </div>

            <div>
              <label className="block text-xs font-semibold app-text-muted mb-1">التقييم الجديد *</label>
              <select
                value={targetValue}
                onChange={(e) => setTargetValue(Number(e.target.value) as IndicatorValue)}
                className="w-full px-3 py-2 text-sm rounded-lg border app-border app-surface app-text focus:outline-none focus:ring-2 focus:ring-blue-500"
              >
                <option value={IndicatorValue.Correct}>سليم / صحيح (Correct)</option>
                <option value={IndicatorValue.Incorrect}>غير صحيح (Incorrect)</option>
                <option value={IndicatorValue.Missing}>مفقود (Missing)</option>
                <option value={IndicatorValue.Incomplete}>غير مكتمل (Incomplete)</option>
                <option value={IndicatorValue.Unclear}>غير واضح (Unclear)</option>
                <option value={IndicatorValue.NotApplicable}>لا ينطبق (Not Applicable)</option>
              </select>
            </div>

            <div>
              <label className="block text-xs font-semibold app-text-muted mb-1">
                {isDefectiveValue(targetValue) ? 'سبب عدم الصلاحية أو الملاحظة (اختياري)' : 'ملاحظة إضافية (اختياري)'}
              </label>
              <textarea
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                rows={3}
                placeholder={
                  isDefectiveValue(targetValue)
                    ? 'أدخل سبب عدم الصلاحية أو النقص إن وجد (اختياري)...'
                    : 'أدخل أي ملاحظة إضافية حول التقييم (اختياري)...'
                }
                className="w-full px-3 py-2 text-sm rounded-lg border app-border app-surface app-text focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>

            <div className="flex justify-end gap-3 pt-3 border-t app-border">
              <button
                type="button"
                onClick={() => setSelectedItem(null)}
                className="px-4 py-2 text-sm font-semibold rounded-lg border app-border app-surface-muted app-hover app-text"
              >
                إلغاء
              </button>
              <button
                type="submit"
                disabled={submitting}
                className="px-5 py-2 text-sm font-semibold rounded-lg bg-emerald-600 text-white hover:bg-emerald-700 disabled:opacity-60 cursor-pointer"
              >
                {submitting ? 'جارٍ الحفظ...' : 'حفظ التقييم'}
              </button>
            </div>
          </form>
        )}
      </Modal>

      {/* Full Editor Modal */}
      {editorNoteId && (
        <MemoEditorModal
          isOpen={Boolean(editorNoteId)}
          noteId={editorNoteId}
          onClose={() => setEditorNoteId(null)}
          onSaved={() => {
            setEditorNoteId(null);
            fetchItems();
          }}
        />
      )}

      {/* Full View Modal */}
      <MemoViewModal
        isOpen={Boolean(viewNoteId)}
        noteId={viewNoteId}
        onClose={() => setViewNoteId(null)}
        onEditNote={(id) => {
          setViewNoteId(null);
          setEditorNoteId(id);
        }}
        onStatusChanged={fetchItems}
      />
    </div>
  );
};

import React, { useEffect, useState, useCallback } from 'react';
import { Modal } from '../../components/common/Modal';
import { api } from '../../services/api';
import { normalizeNoteStatus, type NoteLinkCandidateModel, type NoteStatus } from '../../types/memo';

interface MemoLinksModalProps {
  isOpen: boolean;
  onClose: () => void;
  noteId: string;
  noteNumber?: string;
  allowChanges?: boolean;
  onViewNote?: (targetNoteId: string) => void;
  onLinksChanged?: () => void;
}

export const MemoLinksModal: React.FC<MemoLinksModalProps> = ({
  isOpen,
  onClose,
  noteId,
  noteNumber,
  allowChanges = false,
  onViewNote,
  onLinksChanged,
}) => {
  const [loading, setLoading] = useState(false);
  const [linking, setLinking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const [linkedNotes, setLinkedNotes] = useState<NoteLinkCandidateModel[]>([]);
  const [candidates, setCandidates] = useState<NoteLinkCandidateModel[]>([]);
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedCandidateIds, setSelectedCandidateIds] = useState<string[]>([]);

  const isMultiSelect =
    candidates.some((c) => c.noteTypeName.includes('إخراج') || c.noteTypeName.toUpperCase().includes('EXIT')) ||
    linkedNotes.some((l) => l.noteTypeName.includes('إخراج') || l.noteTypeName.toUpperCase().includes('EXIT'));

  const fetchLinksData = useCallback(async () => {
    if (!noteId) return;
    setLoading(true);
    setError(null);
    try {
      // 1. Fetch currently linked note(s)
      const linked = await api.getLinkCandidates(noteId, false);
      setLinkedNotes(linked);

      // 2. If changes are allowed, fetch candidate notes for linking
      if (allowChanges) {
        const allCandidates = await api.getLinkCandidates(noteId, true);
        // Exclude notes that are already linked
        setCandidates(allCandidates.filter((c) => !c.isLinked));
      }
    } catch (err: unknown) {
      console.error('Failed to load note links', err);
      setError(err instanceof Error ? err.message : 'تعذر تحميل بيانات ربط المذكرات.');
    } finally {
      setLoading(false);
    }
  }, [noteId, allowChanges]);

  useEffect(() => {
    if (isOpen && noteId) {
      setSelectedCandidateIds([]);
      setSuccessMessage(null);
      fetchLinksData();
    }
  }, [isOpen, noteId, fetchLinksData]);

  const toggleCandidate = (candId: string) => {
    if (isMultiSelect) {
      setSelectedCandidateIds((prev) =>
        prev.includes(candId) ? prev.filter((id) => id !== candId) : [...prev, candId]
      );
    } else {
      setSelectedCandidateIds([candId]);
    }
  };

  const toggleSelectAll = () => {
    if (selectedCandidateIds.length === filteredCandidates.length && filteredCandidates.length > 0) {
      setSelectedCandidateIds([]);
    } else {
      setSelectedCandidateIds(filteredCandidates.map((c) => c.noteId));
    }
  };

  const handleLink = async () => {
    if (selectedCandidateIds.length === 0) return;
    setLinking(true);
    setError(null);
    setSuccessMessage(null);
    try {
      await api.linkNotes(noteId, selectedCandidateIds);
      setSuccessMessage(
        selectedCandidateIds.length > 1
          ? `تم ربط ${selectedCandidateIds.length} مذكرات بنجاح.`
          : 'تم ربط المذكرتين بنجاح.'
      );
      setSelectedCandidateIds([]);
      await fetchLinksData();
      onLinksChanged?.();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'فشل ربط المذكرة.');
    } finally {
      setLinking(false);
    }
  };

  const handleUnlink = async (relatedNoteId: string, targetNumber: string) => {
    if (!window.confirm(`هل أنت متأكد من إلغاء الربط بالمذكرة رقم ${targetNumber}؟`)) return;
    setLinking(true);
    setError(null);
    setSuccessMessage(null);
    try {
      await api.unlinkNotes(noteId, relatedNoteId);
      setSuccessMessage('تم إلغاء ربط المذكرة بنجاح.');
      await fetchLinksData();
      onLinksChanged?.();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'فشل إلغاء الربط.');
    } finally {
      setLinking(false);
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
      <span className={`inline-flex items-center px-2 py-0.5 rounded text-[11px] font-medium border ${item.cls}`}>
        {item.label}
      </span>
    );
  };

  const filteredCandidates = candidates.filter((c) => {
    if (!searchQuery.trim()) return true;
    const q = searchQuery.toLowerCase();
    return (
      c.noteNumber.toLowerCase().includes(q) ||
      c.noteTypeName.toLowerCase().includes(q) ||
      (c.factoryName && c.factoryName.toLowerCase().includes(q)) ||
      (c.productionLineName && c.productionLineName.toLowerCase().includes(q))
    );
  });

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={allowChanges ? `ربط المذكرة ${noteNumber ? `(${noteNumber})` : ''} بمذكرة أخرى` : `المذكرات المرتبطة بالمذكرة ${noteNumber ? `(${noteNumber})` : ''}`}
      maxWidth="4xl"
    >
      <div className="space-y-5" dir="rtl">
        {/* Instructions banner matching desktop FrmNoteLinks */}
        <div className="rounded-xl border border-amber-500/30 bg-amber-500/10 p-3 text-xs text-amber-300 leading-relaxed">
          {allowChanges
            ? '💡 اختر مذكرة أو أكثر من قائمة المذكرات المقابلة (إدخال/إخراج) لنفس الشركة ثم اضغط «ربط المذكرات المحددة». يمكن ربط مذكرة الإدخال بأكثر من مذكرة إخراج في آن واحد مهما كانت حالتهما، بما في ذلك المذكرات المعتمدة.'
            : '💡 تعرض هذه النافذة المذكرات المقابلة المرتبطة بهذه المذكرة في النظام.'}
        </div>

        {error && (
          <div className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-3 text-xs text-rose-400">
            {error}
          </div>
        )}

        {successMessage && (
          <div className="rounded-xl border border-emerald-500/30 bg-emerald-500/10 p-3 text-xs text-emerald-400">
            {successMessage}
          </div>
        )}

        {/* Section 1: Currently Linked Notes */}
        <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-4 space-y-3">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-bold text-slate-100 flex items-center gap-2">
              <span>🔗</span>
              <span>المذكرات المرتبطة حالياً ({linkedNotes.length})</span>
            </h3>
            <button
              type="button"
              onClick={fetchLinksData}
              disabled={loading}
              className="text-xs text-slate-400 hover:text-slate-200 underline"
            >
              تحديث
            </button>
          </div>

          {loading ? (
            <div className="py-6 text-center text-xs text-slate-400">جارٍ فحص الروابط...</div>
          ) : linkedNotes.length === 0 ? (
            <div className="py-6 text-center text-xs text-slate-500 bg-slate-800/20 rounded-xl border border-dashed border-slate-800">
              لا توجد مذكرة مرتبطة بهذه المذكرة حالياً.
            </div>
          ) : (
            <div className="overflow-x-auto rounded-xl border border-slate-800">
              <table className="w-full text-right text-xs">
                <thead className="bg-slate-800/70 border-b border-slate-700/60 text-slate-300">
                  <tr>
                    <th className="px-3 py-2.5">رقم المذكرة</th>
                    <th className="px-3 py-2.5">النوع</th>
                    <th className="px-3 py-2.5">الشركة / المعمل</th>
                    <th className="px-3 py-2.5">تاريخ الورود</th>
                    <th className="px-3 py-2.5">الحالة</th>
                    <th className="px-3 py-2.5 text-center">الإجراءات</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800 text-slate-200">
                  {linkedNotes.map((item) => (
                    <tr key={item.noteId} className="hover:bg-slate-800/40 transition-colors">
                      <td className="px-3 py-2.5 font-bold text-teal-300">
                        {item.noteNumber}
                        {item.isInvoice && (
                          <span className="mr-1.5 px-1.5 py-0.5 rounded text-[10px] bg-sky-500/20 text-sky-300 border border-sky-500/30 font-normal">
                            فاتورة
                          </span>
                        )}
                      </td>
                      <td className="px-3 py-2.5">{item.noteTypeName}</td>
                      <td className="px-3 py-2.5">
                        <div>{item.companyName}</div>
                        {item.factoryName && <div className="text-[10px] text-slate-400">{item.factoryName}</div>}
                      </td>
                      <td className="px-3 py-2.5">{item.arrivalAt?.slice(0, 10)}</td>
                      <td className="px-3 py-2.5">{getStatusBadge(item.status)}</td>
                      <td className="px-3 py-2.5 text-center">
                        <div className="inline-flex items-center gap-2">
                          {onViewNote && (
                            <button
                              type="button"
                              onClick={() => onViewNote(item.noteId)}
                              className="px-2.5 py-1 text-[11px] rounded-lg bg-teal-600/20 border border-teal-500/30 text-teal-300 hover:bg-teal-600/30"
                            >
                              عرض المذكرة
                            </button>
                          )}
                          {allowChanges && (
                            <button
                              type="button"
                              disabled={linking}
                              onClick={() => handleUnlink(item.noteId, item.noteNumber)}
                              className="px-2.5 py-1 text-[11px] rounded-lg bg-rose-600/20 border border-rose-500/30 text-rose-300 hover:bg-rose-600/30 disabled:opacity-50"
                            >
                              إلغاء الربط
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {/* Section 2: Candidate Notes for Linking (Matching desktop FrmNoteLinks) */}
        {allowChanges && (
          <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-4 space-y-3">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
              <h3 className="text-sm font-bold text-slate-100 flex items-center gap-2">
                <span>➕</span>
                <span>اختر مذكرة من هنا للربط</span>
                <span className="text-xs font-normal text-slate-400">({filteredCandidates.length} متاحة)</span>
              </h3>

              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="تصفية المذكرات بالرقم أو المصنع..."
                className="w-full sm:w-64 rounded-lg border border-slate-700 bg-slate-800 px-3 py-1.5 text-xs text-slate-200 placeholder-slate-500 focus:border-teal-500 focus:outline-none"
              />
            </div>

            {loading ? (
              <div className="py-8 text-center text-xs text-slate-400">جارٍ جلب المذكرات المقابلة...</div>
            ) : filteredCandidates.length === 0 ? (
              <div className="py-8 text-center text-xs text-slate-500 bg-slate-800/20 rounded-xl border border-dashed border-slate-800">
                لا توجد مذكرات مقابلة متاحة من النوع الآخر (إدخال/إخراج) تابعة لنفس الشركة.
              </div>
            ) : (
              <div className="max-h-64 overflow-y-auto overflow-x-auto rounded-xl border border-slate-800">
                <table className="w-full text-right text-xs">
                  <thead className="sticky top-0 bg-slate-800/90 backdrop-blur border-b border-slate-700/60 text-slate-300">
                    <tr>
                      <th className="px-3 py-2 text-center w-10">
                        {isMultiSelect ? (
                          <input
                            type="checkbox"
                            checked={filteredCandidates.length > 0 && selectedCandidateIds.length === filteredCandidates.length}
                            onChange={toggleSelectAll}
                            className="accent-teal-500 cursor-pointer"
                            title="تحديد الكل"
                          />
                        ) : (
                          'اختيار'
                        )}
                      </th>
                      <th className="px-3 py-2">رقم المذكرة</th>
                      <th className="px-3 py-2">النوع</th>
                      <th className="px-3 py-2">المعمل / الخط</th>
                      <th className="px-3 py-2">تاريخ التحرير</th>
                      <th className="px-3 py-2">تاريخ الورود</th>
                      <th className="px-3 py-2">الحالة</th>
                      <th className="px-3 py-2 text-center">معاينة</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800 text-slate-200">
                    {filteredCandidates.map((cand) => {
                      const isSelected = selectedCandidateIds.includes(cand.noteId);
                      return (
                        <tr
                          key={cand.noteId}
                          onClick={() => toggleCandidate(cand.noteId)}
                          className={`cursor-pointer transition-colors ${
                            isSelected ? 'bg-teal-950/40 border-l-2 border-teal-500' : 'hover:bg-slate-800/40'
                          }`}
                        >
                          <td className="px-3 py-2 text-center" onClick={(e) => e.stopPropagation()}>
                            <input
                              type={isMultiSelect ? 'checkbox' : 'radio'}
                              name="selectedCandidate"
                              checked={isSelected}
                              onChange={() => toggleCandidate(cand.noteId)}
                              className="accent-teal-500 cursor-pointer"
                            />
                          </td>
                          <td className="px-3 py-2 font-bold text-slate-100">
                            {cand.noteNumber}
                            {cand.isInvoice && (
                              <span className="mr-1.5 px-1.5 py-0.5 rounded text-[10px] bg-sky-500/20 text-sky-300 border border-sky-500/30 font-normal">
                                فاتورة
                              </span>
                            )}
                          </td>
                          <td className="px-3 py-2">{cand.noteTypeName}</td>
                          <td className="px-3 py-2 text-slate-400">
                            {cand.factoryName || 'غير محدد'}
                            {cand.productionLineName && ` - ${cand.productionLineName}`}
                          </td>
                          <td className="px-3 py-2 text-slate-400">{cand.writingDate?.slice(0, 10) || '-'}</td>
                          <td className="px-3 py-2">{cand.arrivalAt?.slice(0, 10)}</td>
                          <td className="px-3 py-2">{getStatusBadge(cand.status)}</td>
                          <td className="px-3 py-2 text-center">
                            {onViewNote && (
                              <button
                                type="button"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  onViewNote(cand.noteId);
                                }}
                                className="px-2 py-0.5 text-[10px] rounded bg-slate-700 hover:bg-slate-600 text-slate-300"
                              >
                                عرض
                              </button>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}

            <div className="flex items-center justify-between pt-2">
              <span className="text-xs text-slate-400">
                {selectedCandidateIds.length > 0
                  ? isMultiSelect
                    ? `تم تحديد ${selectedCandidateIds.length} مذكرة من أصل ${filteredCandidates.length}`
                    : `تم تحديد المذكرة: ${filteredCandidates.find((c) => c.noteId === selectedCandidateIds[0])?.noteNumber || ''}`
                  : 'يرجى تحديد مذكرة من الجدول للربط'}
              </span>

              <button
                type="button"
                disabled={selectedCandidateIds.length === 0 || linking}
                onClick={handleLink}
                className="inline-flex items-center gap-2 rounded-xl bg-teal-600 px-4 py-2 text-xs font-semibold text-white shadow-md hover:bg-teal-500 disabled:opacity-40 transition-all cursor-pointer"
              >
                {linking
                  ? 'جارٍ الربط...'
                  : selectedCandidateIds.length > 1
                  ? `ربط المذكرات المحددة (${selectedCandidateIds.length})`
                  : 'ربط المذكرة المحددة'}
              </button>
            </div>
          </div>
        )}

        {/* Footer */}
        <div className="flex items-center justify-end pt-3 border-t border-slate-800">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-slate-700 px-4 py-2 text-xs font-medium text-slate-300 hover:bg-slate-800"
          >
            إغلاق
          </button>
        </div>
      </div>
    </Modal>
  );
};

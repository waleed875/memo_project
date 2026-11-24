import React, { useEffect, useState, useCallback } from 'react';
import { Modal } from '../../components/common/Modal';
import { api } from '../../services/api';
import type {
  NoteSourceType,
  NoteSourceExitReferenceModel,
  NoteSourceModel,
  SaveNoteSourceRequest,
} from '../../types/memo';

interface NoteSourceModalProps {
  isOpen: boolean;
  onClose: () => void;
  noteId: string;
  noteNumber?: string;
  readOnly?: boolean;
  onOpenLinksModal?: () => void;
  onSourceChanged?: () => void;
}

interface InvoiceDraftItem {
  id?: string;
  invoiceNumber: string;
  invoiceDate?: string;
  destination?: string;
  attachmentFileName?: string;
  attachmentContentType?: string;
  attachmentBase64?: string;
  hasExistingAttachment?: boolean;
}

export const NoteSourceModal: React.FC<NoteSourceModalProps> = ({
  isOpen,
  onClose,
  noteId,
  noteNumber,
  readOnly = false,
  onOpenLinksModal,
  onSourceChanged,
}) => {
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const [loadedSource, setLoadedSource] = useState<NoteSourceModel | null>(null);
  const [sourceType, setSourceType] = useState<NoteSourceType>('Exit');
  const [lootDescription, setLootDescription] = useState('');
  const [otherDescription, setOtherDescription] = useState('');
  const [exitReferences, setExitReferences] = useState<NoteSourceExitReferenceModel[]>([]);
  const [invoices, setInvoices] = useState<InvoiceDraftItem[]>([]);

  // Draft invoice input fields
  const [draftInvNumber, setDraftInvNumber] = useState('');
  const [draftInvDate, setDraftInvDate] = useState('');
  const [draftInvDestination, setDraftInvDestination] = useState('');
  const [draftInvFileName, setDraftInvFileName] = useState('');
  const [draftInvContentType, setDraftInvContentType] = useState('');
  const [draftInvBase64, setDraftInvBase64] = useState<string | null>(null);

  const fetchSource = useCallback(async () => {
    if (!noteId) return;
    setLoading(true);
    setError(null);
    try {
      const src = await api.getNoteSource(noteId);
      setLoadedSource(src);
      if (src) {
        setSourceType(src.sourceType);
        setLootDescription(src.lootDescription || '');
        setOtherDescription(src.otherDescription || '');
        setExitReferences(src.exitReferences || []);
        setInvoices(
          (src.invoices || []).map((inv) => ({
            id: inv.id,
            invoiceNumber: inv.invoiceNumber,
            invoiceDate: inv.invoiceDate ? inv.invoiceDate.slice(0, 10) : '',
            destination: inv.destination || '',
            attachmentFileName: inv.attachmentFileName || '',
            attachmentContentType: inv.attachmentContentType || '',
            hasExistingAttachment: !!inv.attachmentFileName,
          }))
        );
      } else {
        setSourceType('Exit');
        setLootDescription('');
        setOtherDescription('');
        setExitReferences([]);
        setInvoices([]);
      }
    } catch (err: unknown) {
      console.error('Failed to load note source', err);
      setError(err instanceof Error ? err.message : 'تعذر تحميل مصدر المذكرة.');
    } finally {
      setLoading(false);
    }
  }, [noteId]);

  useEffect(() => {
    if (isOpen && noteId) {
      setSuccessMessage(null);
      setError(null);
      fetchSource();
    }
  }, [isOpen, noteId, fetchSource]);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    if (file.size > 20 * 1024 * 1024) {
      setError('حجم الملف المرفق يجب ألا يتجاوز 20 ميجابايت.');
      return;
    }

    setDraftInvFileName(file.name);
    setDraftInvContentType(file.type || 'application/octet-stream');

    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result as string;
      const base64 = result.includes(',') ? result.split(',')[1] : result;
      setDraftInvBase64(base64);
    };
    reader.readAsDataURL(file);
  };

  const handleAddInvoice = () => {
    if (!draftInvNumber.trim()) {
      setError('يرجى إدخال رقم الفاتورة أولاً.');
      return;
    }
    setError(null);

    const newItem: InvoiceDraftItem = {
      invoiceNumber: draftInvNumber.trim(),
      invoiceDate: draftInvDate || undefined,
      destination: draftInvDestination.trim() || undefined,
      attachmentFileName: draftInvFileName || undefined,
      attachmentContentType: draftInvContentType || undefined,
      attachmentBase64: draftInvBase64 || undefined,
      hasExistingAttachment: false,
    };

    setInvoices((prev) => [...prev, newItem]);
    setDraftInvNumber('');
    setDraftInvDate('');
    setDraftInvDestination('');
    setDraftInvFileName('');
    setDraftInvContentType('');
    setDraftInvBase64(null);
  };

  const handleRemoveInvoice = (index: number) => {
    setInvoices((prev) => prev.filter((_, i) => i !== index));
  };

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    setSuccessMessage(null);

    try {
      const request: SaveNoteSourceRequest = {
        sourceType,
        otherDescription: sourceType === 'Other' ? otherDescription.trim() : undefined,
        lootDescription: sourceType === 'Loot' ? lootDescription.trim() : undefined,
        exitReferences:
          sourceType === 'Exit'
            ? exitReferences.map((r) => ({
                exitNoteId: r.exitNoteId,
                referenceOrder: r.referenceOrder,
                noteNumber: r.noteNumber || undefined,
              }))
            : [],
        invoices:
          sourceType === 'Invoice'
            ? invoices.map((inv) => ({
                id: inv.id,
                invoiceNumber: inv.invoiceNumber,
                invoiceDate: inv.invoiceDate || undefined,
                destination: inv.destination || undefined,
                attachmentFileName: inv.attachmentFileName || undefined,
                attachmentContentType: inv.attachmentContentType || undefined,
                attachmentBase64: inv.attachmentBase64 || undefined,
              }))
            : [],
      };

      const res = await api.saveNoteSource(noteId, request);
      setSuccessMessage(res.message || 'تم حفظ مصدر المذكرة بنجاح.');
      await fetchSource();
      onSourceChanged?.();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'فشل حفظ مصدر المذكرة.');
    } finally {
      setSaving(false);
    }
  };

  const handleRemoveSource = async () => {
    if (!window.confirm('سيتم إزالة مصدر المذكرة وبياناته التابعة بالكامل. هل تريد المتابعة؟')) return;
    setSaving(true);
    setError(null);
    setSuccessMessage(null);

    try {
      const res = await api.removeNoteSource(noteId);
      setSuccessMessage(res.message || 'تمت إزالة مصدر المذكرة بنجاح.');
      await fetchSource();
      onSourceChanged?.();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'فشل إزالة مصدر المذكرة.');
    } finally {
      setSaving(false);
    }
  };

  const modalTitle = `مصدر مذكرة الإدخال ${noteNumber ? `(${noteNumber})` : ''}`;

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={modalTitle}
      maxWidth="4xl"
    >
      <div className="space-y-4" dir="rtl">
        <div className="rounded-xl border border-amber-500/30 bg-amber-500/10 p-3 text-xs text-amber-300 leading-relaxed">
          💡 اختر نوعاً واحداً لمصدر مذكرة الإدخال. يمكن ربط عدة مذكرات إخراج، أو إدراج عدة فواتير مع مرفقاتها، أو توثيق الغنائم والمضبوطات أو مصدر آخر. لا يجوز الجمع بين أكثر من نوع لنفس المذكرة.
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

        <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-4 space-y-4">
          <div className="flex items-center gap-3">
            <label className="text-xs font-bold text-slate-300 whitespace-nowrap">نوع المصدر:</label>
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 flex-1">
              {(
                [
                  { type: 'Exit', label: 'مذكرات إخراج' },
                  { type: 'Invoice', label: 'فواتير' },
                  { type: 'Loot', label: 'غنائم / مضبوطات' },
                  { type: 'Other', label: 'مصدر آخر' },
                ] as const
              ).map((tab) => (
                <button
                  key={tab.type}
                  type="button"
                  disabled={readOnly || loading || saving}
                  onClick={() => setSourceType(tab.type)}
                  className={`px-3 py-2 text-xs font-bold rounded-xl border transition-all cursor-pointer ${
                    sourceType === tab.type
                      ? 'bg-teal-600 border-teal-500 text-white shadow-md'
                      : 'bg-slate-800 border-slate-700 text-slate-400 hover:text-slate-200 hover:bg-slate-750'
                  }`}
                >
                  {tab.label}
                </button>
              ))}
            </div>
          </div>

          {sourceType === 'Exit' && (
            <div className="space-y-3 pt-2 border-t border-slate-800">
              <div className="flex items-center justify-between">
                <div>
                  <h4 className="text-xs font-bold text-teal-300">مذكرات الإخراج المرتبطة</h4>
                  <p className="text-[11px] text-slate-400">
                    يمكن ربط أكثر من مذكرة إخراج بمذكرة الإدخال نفسها. استخدم شاشة الربط لإضافة أو إلغاء كل مرجع على حدة.
                  </p>
                </div>
                {onOpenLinksModal && (
                  <button
                    type="button"
                    onClick={onOpenLinksModal}
                    className="px-3 py-1.5 rounded-lg bg-teal-600/20 border border-teal-500/40 text-xs font-semibold text-teal-300 hover:bg-teal-600/30 transition-all cursor-pointer"
                  >
                    🔗 إدارة مذكرات الإخراج والربط
                  </button>
                )}
              </div>

              {exitReferences.length === 0 ? (
                <div className="py-6 text-center text-xs text-slate-500 bg-slate-800/30 rounded-xl border border-dashed border-slate-800">
                  لم يتم ربط أي مذكرة إخراج بعد كمرجع لمذكرة الإدخال هذه.
                </div>
              ) : (
                <div className="overflow-x-auto rounded-xl border border-slate-800">
                  <table className="w-full text-right text-xs">
                    <thead className="bg-slate-800/80 text-slate-300 border-b border-slate-700/60">
                      <tr>
                        <th className="px-3 py-2 w-16">الترتيب</th>
                        <th className="px-3 py-2">رقم مذكرة الإخراج</th>
                        <th className="px-3 py-2">المعرف الداخلي</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-800 text-slate-200">
                      {exitReferences.map((ref, idx) => (
                        <tr key={ref.exitNoteId || idx} className="hover:bg-slate-800/40">
                          <td className="px-3 py-2 text-slate-400">{ref.referenceOrder}</td>
                          <td className="px-3 py-2 font-bold text-teal-300">{ref.noteNumber || 'غير محدد'}</td>
                          <td className="px-3 py-2 text-[11px] text-slate-400 font-mono">{ref.exitNoteId}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          )}

          {sourceType === 'Invoice' && (
            <div className="space-y-4 pt-2 border-t border-slate-800">
              {!readOnly && (
                <div className="rounded-xl border border-slate-700/70 bg-slate-800/40 p-3 space-y-3">
                  <div className="text-xs font-bold text-slate-200">إضافة فاتورة جديدة:</div>
                  <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-2.5">
                    <div>
                      <label className="block text-[11px] font-semibold text-slate-400 mb-1">رقم الفاتورة *</label>
                      <input
                        type="text"
                        value={draftInvNumber}
                        onChange={(e) => setDraftInvNumber(e.target.value)}
                        placeholder="مثال: INV-1049"
                        className="w-full rounded-lg border border-slate-700 bg-slate-800 px-2.5 py-1.5 text-xs text-slate-100 focus:border-teal-500 focus:outline-none"
                      />
                    </div>
                    <div>
                      <label className="block text-[11px] font-semibold text-slate-400 mb-1">تاريخ الفاتورة</label>
                      <input
                        type="date"
                        value={draftInvDate}
                        onChange={(e) => setDraftInvDate(e.target.value)}
                        className="w-full rounded-lg border border-slate-700 bg-slate-800 px-2.5 py-1.5 text-xs text-slate-100 focus:border-teal-500 focus:outline-none"
                      />
                    </div>
                    <div>
                      <label className="block text-[11px] font-semibold text-slate-400 mb-1">الوجهة</label>
                      <input
                        type="text"
                        value={draftInvDestination}
                        onChange={(e) => setDraftInvDestination(e.target.value)}
                        placeholder="اسم الجهة أو المستودع"
                        className="w-full rounded-lg border border-slate-700 bg-slate-800 px-2.5 py-1.5 text-xs text-slate-100 focus:border-teal-500 focus:outline-none"
                      />
                    </div>
                    <div>
                      <label className="block text-[11px] font-semibold text-slate-400 mb-1">مرفق الفاتورة (اختياري)</label>
                      <input
                        type="file"
                        accept=".pdf,.png,.jpg,.jpeg"
                        onChange={handleFileChange}
                        className="w-full text-[11px] text-slate-300 file:mr-2 file:py-1 file:px-2 file:rounded file:border-0 file:text-[11px] file:bg-slate-700 file:text-slate-200 hover:file:bg-slate-600"
                      />
                    </div>
                  </div>
                  <div className="flex justify-end">
                    <button
                      type="button"
                      onClick={handleAddInvoice}
                      className="px-3.5 py-1.5 rounded-lg bg-teal-600 text-xs font-semibold text-white hover:bg-teal-500 transition-all cursor-pointer"
                    >
                      + إضافة الفاتورة للقائمة
                    </button>
                  </div>
                </div>
              )}

              <div className="overflow-x-auto rounded-xl border border-slate-800">
                <table className="w-full text-right text-xs">
                  <thead className="bg-slate-800/80 text-slate-300 border-b border-slate-700/60">
                    <tr>
                      <th className="px-3 py-2">رقم الفاتورة</th>
                      <th className="px-3 py-2">التاريخ</th>
                      <th className="px-3 py-2">الوجهة</th>
                      <th className="px-3 py-2">المرفق</th>
                      {!readOnly && <th className="px-3 py-2 text-center w-20">إجراءات</th>}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800 text-slate-200">
                    {invoices.length === 0 ? (
                      <tr>
                        <td colSpan={readOnly ? 4 : 5} className="py-6 text-center text-slate-500">
                          لا توجد فواتير مضافة حالياً.
                        </td>
                      </tr>
                    ) : (
                      invoices.map((inv, idx) => (
                        <tr key={inv.id || idx} className="hover:bg-slate-800/40">
                          <td className="px-3 py-2 font-bold text-teal-300">{inv.invoiceNumber}</td>
                          <td className="px-3 py-2 text-slate-400">{inv.invoiceDate || '-'}</td>
                          <td className="px-3 py-2 text-slate-300">{inv.destination || '-'}</td>
                          <td className="px-3 py-2">
                            {inv.id && inv.hasExistingAttachment ? (
                              <a
                                href={api.getNoteSourceInvoiceAttachmentUrl(noteId, inv.id)}
                                target="_blank"
                                rel="noreferrer"
                                className="inline-flex items-center gap-1 text-[11px] text-teal-400 hover:text-teal-300 underline"
                              >
                                <span>📎</span>
                                <span>{inv.attachmentFileName || 'عرض المرفق'}</span>
                              </a>
                            ) : inv.attachmentFileName ? (
                              <span className="text-[11px] text-amber-300">
                                📎 {inv.attachmentFileName} (جديد)
                              </span>
                            ) : (
                              <span className="text-slate-500 text-[11px]">بدون مرفق</span>
                            )}
                          </td>
                          {!readOnly && (
                            <td className="px-3 py-2 text-center">
                              <button
                                type="button"
                                onClick={() => handleRemoveInvoice(idx)}
                                className="px-2 py-0.5 text-[11px] rounded bg-rose-600/20 text-rose-300 border border-rose-500/30 hover:bg-rose-600/30"
                              >
                                حذف
                              </button>
                            </td>
                          )}
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {sourceType === 'Loot' && (
            <div className="space-y-2 pt-2 border-t border-slate-800">
              <label className="block text-xs font-semibold text-slate-300">بيانات ووصف الغنائم / المضبوطات:</label>
              <textarea
                rows={5}
                disabled={readOnly}
                value={lootDescription}
                onChange={(e) => setLootDescription(e.target.value)}
                placeholder="أدخل تفاصيل الغنائم أو المضبوطات وأي جهات مسؤولة أو أرقام محاضر ضبط..."
                className="w-full rounded-xl border border-slate-700 bg-slate-800/80 p-3 text-xs text-slate-100 focus:border-teal-500 focus:outline-none leading-relaxed disabled:opacity-60"
              />
            </div>
          )}

          {sourceType === 'Other' && (
            <div className="space-y-2 pt-2 border-t border-slate-800">
              <label className="block text-xs font-semibold text-slate-300">وصف المصدر الآخر:</label>
              <textarea
                rows={5}
                disabled={readOnly}
                value={otherDescription}
                onChange={(e) => setOtherDescription(e.target.value)}
                placeholder="أدخل وصف وبيانات المصدر الاستثنائي الآخر..."
                className="w-full rounded-xl border border-slate-700 bg-slate-800/80 p-3 text-xs text-slate-100 focus:border-teal-500 focus:outline-none leading-relaxed disabled:opacity-60"
              />
            </div>
          )}
        </div>

        <div className="flex items-center justify-between pt-3 border-t border-slate-800">
          <div>
            {!readOnly && loadedSource && (
              <button
                type="button"
                disabled={saving || loading}
                onClick={handleRemoveSource}
                className="rounded-lg bg-rose-600/20 border border-rose-500/30 px-3 py-2 text-xs font-medium text-rose-300 hover:bg-rose-600/30 disabled:opacity-40 transition-all cursor-pointer"
              >
                إزالة المصدر
              </button>
            )}
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg border border-slate-700 px-4 py-2 text-xs font-medium text-slate-300 hover:bg-slate-800 cursor-pointer"
            >
              إغلاق
            </button>
            {!readOnly && (
              <button
                type="button"
                disabled={saving || loading}
                onClick={handleSave}
                className="rounded-lg bg-teal-600 px-5 py-2 text-xs font-bold text-white shadow-md hover:bg-teal-500 disabled:opacity-40 transition-all cursor-pointer"
              >
                {saving ? 'جارٍ الحفظ...' : 'حفظ المصدر'}
              </button>
            )}
          </div>
        </div>
      </div>
    </Modal>
  );
};

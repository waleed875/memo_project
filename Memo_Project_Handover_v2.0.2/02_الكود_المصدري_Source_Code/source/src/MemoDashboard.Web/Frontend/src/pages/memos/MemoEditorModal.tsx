import React, { useEffect, useState } from 'react';
import { Modal } from '../../components/common/Modal';
import { api } from '../../services/api';
import { calculateNoteDelay } from '../../utils/noteDelay';
import {
  IndicatorValue,
  type CompanyModel,
  type FactoryModel,
  type NoteModel,
  type NoteTypeFieldModel,
  type NoteTypeModel,
  type ProductionLineModel,
  type NoteStatus,
  type NoteAttachmentModel,
  type NoteLinkCandidateModel,
  type NoteDuplicateModel,
  type DuplicateClassification,
} from '../../types/memo';
import { MemoLinksModal } from './MemoLinksModal';
import { NoteSourceModal } from './NoteSourceModal';
import { DuplicateResolutionModal } from './DuplicateResolutionModal';

interface MemoEditorModalProps {
  isOpen: boolean;
  onClose: () => void;
  noteId?: string | null;
  onSaved: () => void;
}

function getDefaultDates() {
  const now = new Date();
  const arrival = new Date(now);
  arrival.setDate(now.getDate() - 1);
  const writing = new Date(arrival);
  writing.setDate(arrival.getDate() - 3);

  const pad = (n: number) => String(n).padStart(2, '0');
  const format = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

  return {
    arrivalAt: format(arrival),
    writingDate: format(writing),
  };
}

export const MemoEditorModal: React.FC<MemoEditorModalProps> = ({
  isOpen,
  onClose,
  noteId,
  onSaved,
}) => {
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Lookups
  const [companies, setCompanies] = useState<CompanyModel[]>([]);
  const [factories, setFactories] = useState<FactoryModel[]>([]);
  const [lines, setLines] = useState<ProductionLineModel[]>([]);
  const [noteTypes, setNoteTypes] = useState<NoteTypeModel[]>([]);
  const [fields, setFields] = useState<NoteTypeFieldModel[]>([]);

  // Form State
  const defaultDates = getDefaultDates();
  const [noteNumber, setNoteNumber] = useState('');
  const [companyId, setCompanyId] = useState('');
  const [factoryId, setFactoryId] = useState('');
  const [productionLineId, setProductionLineId] = useState('');
  const [noteTypeId, setNoteTypeId] = useState('');
  const [writingDate, setWritingDate] = useState(defaultDates.writingDate);
  const [arrivalAt, setArrivalAt] = useState(defaultDates.arrivalAt);
  const [notesText, setNotesText] = useState('');
  const [duplicateReason, setDuplicateReason] = useState('');
  const [retainForNext, setRetainForNext] = useState(false);

  // Entry/Exit Note Linking & Flags
  const [isLoot, setIsLoot] = useState(false);
  const [isInvoice, setIsInvoice] = useState(false);
  const [relatedNoteIds, setRelatedNoteIds] = useState<string[]>([]);
  const [linkCandidates, setLinkCandidates] = useState<NoteLinkCandidateModel[]>([]);

  // Concurrency & status tracking from loaded note
  const [expectedUpdatedAt, setExpectedUpdatedAt] = useState<string | null>(null);
  const [existingStatus, setExistingStatus] = useState<NoteStatus | null>(null);
  const [returnReason, setReturnReason] = useState<string | null>(null);

  // Field indicators state (Defaults to Correct)
  const [fieldValues, setFieldValues] = useState<Record<string, { value: IndicatorValue; reason: string }>>({});

  // Attachments state
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const [existingAttachments, setExistingAttachments] = useState<NoteAttachmentModel[]>([]);

  // Duplicates Warning & Detailed Matches
  const [duplicateWarning, setDuplicateWarning] = useState<string | null>(null);
  const [duplicatesList, setDuplicatesList] = useState<NoteDuplicateModel[]>([]);
  const [showDuplicateResolution, setShowDuplicateResolution] = useState(false);

  // Modals for Links and Note Source
  const [showLinksModal, setShowLinksModal] = useState(false);
  const [showNoteSourceModal, setShowNoteSourceModal] = useState(false);
  const [linkFilterQuery, setLinkFilterQuery] = useState('');

  // Load initial lookups
  useEffect(() => {
    if (!isOpen) return;
    api.getCompanies().then(setCompanies).catch(console.error);
    api.getNoteTypes().then(setNoteTypes).catch(console.error);
  }, [isOpen]);

  // Load factories when company changes
  useEffect(() => {
    if (!companyId) {
      setFactories([]);
      setFactoryId('');
      return;
    }
    api.getFactories(companyId).then(setFactories).catch(console.error);
  }, [companyId]);

  // Load lines when factory changes
  useEffect(() => {
    if (!factoryId) {
      setLines([]);
      setProductionLineId('');
      return;
    }
    api.getProductionLines(factoryId).then(setLines).catch(console.error);
  }, [factoryId]);

  // Load fields when note type changes
  useEffect(() => {
    if (!noteTypeId) {
      setFields([]);
      return;
    }
    api.getNoteTypeFields(noteTypeId).then((data) => {
      setFields(data);
      // initialize missing field values with Correct = 1
      setFieldValues((prev) => {
        const next = { ...prev };
        for (const f of data) {
          if (!next[f.fieldId]) {
            next[f.fieldId] = { value: IndicatorValue.Correct, reason: '' };
          }
        }
        return next;
      });
    }).catch(console.error);
  }, [noteTypeId]);

  // Fetch link candidates for Entry/Exit notes when company or noteTypeId changes
  useEffect(() => {
    if (!companyId || !noteTypeId) {
      setLinkCandidates([]);
      setRelatedNoteIds([]);
      return;
    }
    const currentType = noteTypes.find((t) => t.id === noteTypeId);
    if (currentType && (currentType.code === 'ENTRY' || currentType.code === 'EXIT')) {
      api.getAvailableLinkCandidates(companyId, noteTypeId, noteId || undefined)
        .then((data) => {
          setLinkCandidates(data);
          if (!noteId) {
            const alreadyLinked = data.filter((c) => c.isLinked).map((c) => c.noteId);
            if (alreadyLinked.length > 0) {
              setRelatedNoteIds(alreadyLinked);
            }
          }
        })
        .catch(console.error);
    } else {
      setLinkCandidates([]);
      setRelatedNoteIds([]);
    }
  }, [companyId, noteTypeId, noteTypes, noteId]);

  // Check duplicates
  useEffect(() => {
    if (!noteNumber.trim() || !companyId || !noteTypeId) {
      setDuplicateWarning(null);
      setDuplicatesList([]);
      return;
    }
    const timer = setTimeout(async () => {
      try {
        const currentType = noteTypes.find((t) => t.id === noteTypeId);
        const isExitInvoice = currentType?.code === 'EXIT' && isInvoice;
        const dups = await api.checkDuplicates({
          noteNumber: noteNumber.trim(),
          companyId,
          noteTypeId,
          writingDate: writingDate || undefined,
          currentNoteId: noteId || undefined,
          isInvoice: isExitInvoice,
        });
        setDuplicatesList(dups || []);
        if (dups && dups.length > 0) {
          setDuplicateWarning(`تنبيه: يوجد ${dups.length} مذكرة بنفس الرقم والشركة في النظام. يرجى تدوين سبب الاستمرار أدناه.`);
        } else {
          setDuplicateWarning(null);
        }
      } catch {
        // ignore check errors
      }
    }, 400);
    return () => clearTimeout(timer);
  }, [noteNumber, companyId, noteTypeId, writingDate, noteId, isInvoice, noteTypes]);

  const handleResolveDuplicateDecision = async (
    classification: DuplicateClassification,
    matchedNoteId: string,
    customReason?: string
  ) => {
    if (customReason) {
      setDuplicateReason(customReason);
    }
    if (classification === 'COMPANY_DUPLICATE_NUMBER') {
      setShowDuplicateResolution(false);
    } else {
      try {
        setSaving(true);
        await api.resolveDuplicate({
          note: {
            noteNumber: noteNumber.trim(),
            companyId,
            factoryId: factoryId || null,
            productionLineId: productionLineId || null,
            noteTypeId,
            writingDate: writingDate || null,
            arrivalAt,
            notes: notesText.trim() || null,
            duplicateReason: customReason || duplicateReason || null,
          },
          resolution: {
            classification,
            matchedNoteId,
          },
          saveAsDraft: false,
          relatedNoteId: relatedNoteIds[0] || null,
        });
        setShowDuplicateResolution(false);
        onClose();
        onSaved();
      } catch (err: unknown) {
        setError(err instanceof Error ? err.message : 'فشلت معالجة التكرار.');
      } finally {
        setSaving(false);
      }
    }
  };

  // Load existing note if editing
  useEffect(() => {
    if (!isOpen || !noteId) {
      if (!retainForNext) {
        const defs = getDefaultDates();
        setNoteNumber('');
        setCompanyId('');
        setFactoryId('');
        setProductionLineId('');
        setNoteTypeId('');
        setWritingDate(defs.writingDate);
        setArrivalAt(defs.arrivalAt);
      } else {
        setNoteNumber('');
      }
      setNotesText('');
      setDuplicateReason('');
      setFieldValues({});
      setSelectedFiles([]);
      setExistingAttachments([]);
      setIsLoot(false);
      setIsInvoice(false);
      setRelatedNoteIds([]);
      setExpectedUpdatedAt(null);
      setExistingStatus(null);
      setReturnReason(null);
      setError(null);
      return;
    }

    setLoading(true);
    Promise.all([
      api.getNote(noteId),
      api.getNoteAttachments(noteId).catch(() => []),
      api.getLinkCandidates(noteId, false).catch(() => []),
    ]).then(([n, atts, linked]) => {
      setNoteNumber(n.noteNumber);
      setCompanyId(n.companyId);
      setFactoryId(n.factoryId || '');
      setProductionLineId(n.productionLineId || '');
      setNoteTypeId(n.noteTypeId);
      setWritingDate(n.writingDate || '');
      setArrivalAt(n.arrivalAt?.slice(0, 10) || '');
      setNotesText(n.notes || '');
      setDuplicateReason(n.duplicateReason || '');
      setIsLoot(Boolean(n.isLoot));
      setIsInvoice(Boolean(n.isInvoice));
      setExistingAttachments(atts);
      if (n.relatedNoteIds && n.relatedNoteIds.length > 0) {
        setRelatedNoteIds(n.relatedNoteIds);
      } else if (linked && linked.length > 0) {
        setRelatedNoteIds(linked.map((l) => l.noteId));
      } else {
        setRelatedNoteIds([]);
      }
      setExpectedUpdatedAt(n.updatedAt);
      setExistingStatus(n.status);
      setReturnReason(n.returnReason || null);

      const map: Record<string, { value: IndicatorValue; reason: string }> = {};
      for (const val of n.fieldValues) {
        map[val.fieldId] = { value: val.fieldValue, reason: val.inactiveReason || '' };
      }
      setFieldValues(map);
    }).catch((err) => {
      setError(err instanceof Error ? err.message : 'تعذر تحميل بيانات المذكرة.');
    }).finally(() => setLoading(false));
  }, [isOpen, noteId]);

  const handleMarkAllCorrect = () => {
    setFieldValues((prev) => {
      const next = { ...prev };
      for (const f of fields) {
        next[f.fieldId] = { value: IndicatorValue.Correct, reason: prev[f.fieldId]?.reason || '' };
      }
      return next;
    });
  };

  const handleFieldChange = (fieldId: string, value: IndicatorValue, reason = '') => {
    setFieldValues((prev) => ({
      ...prev,
      [fieldId]: { value, reason: reason ?? prev[fieldId]?.reason ?? '' },
    }));
  };

  const isDefectiveValue = (val: IndicatorValue) => {
    return [
      IndicatorValue.Incorrect,
      IndicatorValue.Missing,
      IndicatorValue.Unclear,
      IndicatorValue.Incomplete,
    ].includes(val);
  };

  const handleDeleteExistingAttachment = async (attachmentId: string, fileName: string) => {
    if (!noteId) return;
    if (!window.confirm(`هل أنت متأكد من حذف المرفق «${fileName}»؟`)) return;
    try {
      await api.deleteNoteAttachment(noteId, attachmentId);
      setExistingAttachments((prev) => prev.filter((a) => a.id !== attachmentId));
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل حذف المرفق.');
    }
  };

  const handleSave = async (saveAsDraft: boolean) => {
    setError(null);
    const trimmedNumber = noteNumber.trim();
    if (!trimmedNumber) {
      setError('رقم المذكرة مطلوب.');
      return;
    }

    // Convert Arabic and Persian indic digits to Latin digits for numeric validation
    const normalizedDigits = trimmedNumber
      .replace(/[٠-٩]/g, (d) => '٠١٢٣٤٥٦٧٨٩'.indexOf(d).toString())
      .replace(/[۰-۹]/g, (d) => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d).toString());
    if (!/^\d+$/.test(normalizedDigits) || parseInt(normalizedDigits, 10) <= 0) {
      setError('رقم المذكرة يجب أن يكون عدداً صحيحاً موجباً (أرقام فقط مثل: 1234 أو ٥٦٧٨).');
      return;
    }

    if (!companyId) {
      setError('يرجى اختيار الشركة.');
      return;
    }
    if (!noteTypeId) {
      setError('يرجى اختيار نوع المذكرة.');
      return;
    }
    if (!arrivalAt) {
      setError('تاريخ الورود مطلوب.');
      return;
    }

    const todayIso = new Date().toISOString().slice(0, 10);
    if (arrivalAt > todayIso) {
      setError('تاريخ الورود لا يمكن أن يكون في المستقبل.');
      return;
    }

    if (writingDate && arrivalAt < writingDate) {
      setError('تاريخ كتابة المذكرة لا يمكن أن يكون أحدث من تاريخ الورود.');
      return;
    }

    // If duplicate warning exists or duplicateReason already set, enforce duplicateReason
    if (duplicateWarning && !duplicateReason.trim()) {
      setError('يوجد تكرار لرقم المذكرة مع نفس الشركة. يرجى تدوين سبب الاستمرار رغم التكرار.');
      return;
    }

    // Indicator notes/reasons are always optional as requested

    setSaving(true);
    try {
      const fieldList = fields.map((f) => ({
        id: '00000000-0000-0000-0000-000000000000',
        noteId: noteId || '00000000-0000-0000-0000-000000000000',
        fieldId: f.fieldId,
        fieldName: f.fieldNameAr,
        fieldValue: fieldValues[f.fieldId]?.value ?? IndicatorValue.Correct,
        inactiveReason: fieldValues[f.fieldId]?.reason?.trim() || null,
      }));

      const currentType = noteTypes.find((t) => t.id === noteTypeId);
      const isEntry = currentType?.code === 'ENTRY';
      const isExit = currentType?.code === 'EXIT';

      const notePayload: NoteModel = {
        id: noteId || '00000000-0000-0000-0000-000000000000',
        noteNumber: noteNumber.trim(),
        noteTypeId,
        noteTypeName: currentType?.nameAr || '',
        companyId,
        companyName: companies.find((c) => c.id === companyId)?.name || '',
        factoryId: factoryId || null,
        productionLineId: productionLineId || null,
        writingDate: writingDate || null,
        arrivalAt,
        notes: notesText.trim() || null,
        duplicateReason: duplicateReason.trim() || null,
        status: saveAsDraft ? 'DRAFT' : 'NEEDS_REVIEW',
        createdAt: new Date().toISOString(),
        updatedAt: expectedUpdatedAt || new Date().toISOString(),
        fieldValues: fieldList,
        reviewHistory: [],
        isLoot: isEntry ? isLoot : false,
        isInvoice: isExit ? isInvoice : false,
        relatedNoteIds: (isEntry && isLoot) ? [] : relatedNoteIds,
      };

      const primaryRelatedId = (!isLoot && relatedNoteIds.length > 0) ? relatedNoteIds[0] : undefined;
      const targetRelatedIds = (isEntry && isLoot) ? [] : relatedNoteIds;

      let savedNoteId: string;
      if (noteId) {
        const res = await api.updateNote(noteId, {
          note: notePayload,
          saveAsDraft,
          relatedNoteId: primaryRelatedId,
          relatedNoteIds: targetRelatedIds,
        });
        savedNoteId = res.id;
      } else {
        const res = await api.saveNote({
          note: notePayload,
          saveAsDraft,
          relatedNoteId: primaryRelatedId,
          relatedNoteIds: targetRelatedIds,
        });
        savedNoteId = res.id;
      }

      // Upload multiple attachments if selected
      if (selectedFiles.length > 0) {
        try {
          await api.uploadNoteAttachments(savedNoteId, selectedFiles);
        } catch (uploadErr: unknown) {
          console.error('Failed to upload attachments:', uploadErr);
          alert('تم حفظ المذكرة بنجاح، لكن تعذر رفع بعض المرفقات: ' + (uploadErr instanceof Error ? uploadErr.message : ''));
        }
      }

      onSaved();

      if (!noteId && retainForNext) {
        // Retain shared fields for next entry
        setNoteNumber('');
        setNotesText('');
        setDuplicateReason('');
        setSelectedFiles([]);
        setExistingAttachments([]);
        setIsLoot(false);
        setIsInvoice(false);
        setRelatedNoteIds([]);
        setDuplicateWarning(null);
        // Reset indicators to Correct
        setFieldValues((prev) => {
          const next = { ...prev };
          for (const f of fields) {
            next[f.fieldId] = { value: IndicatorValue.Correct, reason: '' };
          }
          return next;
        });
        alert('تم حفظ المذكرة بنجاح! تم الإبقاء على بيانات المنشأة والتاريخ لتسريع إدخال المذكرة التالية.');
      } else {
        onClose();
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'فشل حفظ المذكرة.');
    } finally {
      setSaving(false);
    }
  };

  const delayAssessment = calculateNoteDelay(writingDate, arrivalAt);
  const currentNoteType = noteTypes.find((t) => t.id === noteTypeId);
  const isEntryNote = currentNoteType?.code === 'ENTRY';
  const filteredLinkCandidates = linkCandidates.filter((c) => {
    if (!linkFilterQuery.trim()) return true;
    const q = linkFilterQuery.toLowerCase();
    return (
      c.noteNumber.toLowerCase().includes(q) ||
      c.noteTypeName.toLowerCase().includes(q) ||
      (c.factoryName && c.factoryName.toLowerCase().includes(q))
    );
  });

  return (
    <>
      <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={noteId ? `تعديل مذكرة التدقيق (${noteNumber || ''})` : 'إدخال مذكرة تدقيق جديدة'}
      maxWidth="4xl"
    >
      {loading ? (
        <div className="py-12 text-center text-slate-400">جارٍ تحميل بيانات المذكرة...</div>
      ) : (
        <div className="space-y-6" dir="rtl">
          {error && (
            <div className="rounded-xl bg-rose-500/10 border border-rose-500/30 p-3 text-xs text-rose-400 font-semibold">
              {error}
            </div>
          )}

          {/* Return reason banner if returned for correction */}
          {existingStatus === 'RETURNED_FOR_CORRECTION' && returnReason && (
            <div className="rounded-xl bg-rose-950/30 border border-rose-500/40 p-3.5 text-xs text-rose-300 space-y-1">
              <span className="font-bold text-rose-200 flex items-center gap-1.5">
                <span>⚠️</span>
                <span>المذكرة معادة للتصحيح بواسطة المراجع:</span>
              </span>
              <p className="pr-5 font-semibold text-rose-300 leading-relaxed">{returnReason}</p>
            </div>
          )}

          {duplicateWarning && (
            <div className="rounded-xl bg-amber-500/10 border border-amber-500/30 p-3 text-xs text-amber-300 flex flex-col sm:flex-row sm:items-center justify-between gap-2">
              <div className="flex items-center gap-2">
                <svg className="w-5 h-5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                </svg>
                <span>{duplicateWarning}</span>
              </div>
              {duplicatesList.length > 0 && (
                <button
                  type="button"
                  onClick={() => setShowDuplicateResolution(true)}
                  className="px-3 py-1 rounded-lg bg-amber-600/30 border border-amber-500/40 text-xs font-semibold text-amber-200 hover:bg-amber-600/40 transition-colors whitespace-nowrap cursor-pointer self-start sm:self-auto"
                >
                  🔍 مقارنة المذكرات المتطابقة ({duplicatesList.length})
                </button>
              )}
            </div>
          )}

          {/* Core Info Grid */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div>
              <label className="block text-xs font-semibold text-slate-300 mb-1">رقم المذكرة *</label>
              <input
                type="text"
                required
                value={noteNumber}
                onChange={(e) => setNoteNumber(e.target.value)}
                placeholder="أرقام فقط مثال: 1234 أو ٥٦٧٨"
                className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-slate-100 placeholder-slate-500 focus:border-teal-500 focus:outline-none"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-300 mb-1">نوع المذكرة *</label>
              <select
                value={noteTypeId}
                onChange={(e) => setNoteTypeId(e.target.value)}
                className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-slate-100 focus:border-teal-500 focus:outline-none"
              >
                <option value="">-- اختر نوع المذكرة --</option>
                {noteTypes.map((nt) => (
                  <option key={nt.id} value={nt.id}>{nt.nameAr}</option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-300 mb-1">الشركة *</label>
              <select
                value={companyId}
                onChange={(e) => setCompanyId(e.target.value)}
                className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-slate-100 focus:border-teal-500 focus:outline-none"
              >
                <option value="">-- اختر الشركة --</option>
                {companies.map((c) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-300 mb-1">المعمل (اختياري)</label>
              <select
                value={factoryId}
                disabled={!companyId}
                onChange={(e) => setFactoryId(e.target.value)}
                className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-slate-100 focus:border-teal-500 focus:outline-none disabled:opacity-40"
              >
                <option value="">-- كافة المعامل / غير محدد --</option>
                {factories.map((f) => (
                  <option key={f.id} value={f.id}>{f.name}</option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-300 mb-1">خط الإنتاج (اختياري)</label>
              <select
                value={productionLineId}
                disabled={!factoryId}
                onChange={(e) => setProductionLineId(e.target.value)}
                className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-slate-100 focus:border-teal-500 focus:outline-none disabled:opacity-40"
              >
                <option value="">-- كافة الخطوط / غير محدد --</option>
                {lines.map((l) => (
                  <option key={l.id} value={l.id}>{l.name}</option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-300 mb-1">
                تاريخ التحرير / الكتابة (افتراضياً قبل الورود بـ 3 أيام)
              </label>
              <input
                type="date"
                value={writingDate}
                onChange={(e) => setWritingDate(e.target.value)}
                className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-slate-100 focus:border-teal-500 focus:outline-none"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-300 mb-1">
                تاريخ الورود / الوصول * (افتراضياً بالأمس)
              </label>
              <input
                type="date"
                required
                value={arrivalAt}
                onChange={(e) => setArrivalAt(e.target.value)}
                className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-slate-100 focus:border-teal-500 focus:outline-none"
              />
            </div>

            <div className="md:col-span-2">
              <label className="block text-xs font-semibold text-slate-300 mb-1">حالة التأخير التقديرية</label>
              <div className="py-2 px-3 rounded-lg border border-slate-700/80 bg-slate-800/60 text-xs flex items-center gap-2">
                <span className="text-slate-400">تقييم المدة:</span>
                <span
                  className={`font-semibold px-2 py-0.5 rounded text-[11px] ${
                    !delayAssessment.hasWritingDate
                      ? 'bg-amber-500/15 text-amber-400 border border-amber-500/30'
                      : delayAssessment.isDelayed
                      ? 'bg-rose-500/15 text-rose-400 border border-rose-500/30'
                      : 'bg-emerald-500/15 text-emerald-400 border border-emerald-500/30'
                  }`}
                >
                  {delayAssessment.displayText}
                </span>
              </div>
            </div>

            {/* Entry Note Flag: Loot & Imports (غنائم ومستوردات) */}
            {isEntryNote && (
              <div className="md:col-span-3 rounded-xl border border-emerald-500/40 bg-emerald-950/20 p-4 transition-all">
                <label className="flex items-start gap-3 cursor-pointer select-none">
                  <input
                    type="checkbox"
                    checked={isLoot}
                    onChange={(e) => {
                      const checked = e.target.checked;
                      setIsLoot(checked);
                      if (checked) {
                        setRelatedNoteIds([]);
                      }
                    }}
                    className="mt-0.5 h-5 w-5 rounded border-emerald-500/60 bg-slate-900 text-emerald-500 focus:ring-emerald-400 cursor-pointer"
                  />
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <span className="font-bold text-sm text-emerald-300">مذكرة غنائم ومستوردات</span>
                      <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                        معفية من مذكرة الإخراج المقابلة
                      </span>
                    </div>
                    <p className="text-xs text-emerald-200/80 leading-relaxed">
                      عند تفعيل هذا الخيار، تعتبر المذكرة نظامية ولا تتطلب وجود مذكرة إخراج مقابلة، ويتم احتسابها تلقائياً كإجراء سليم ومطابق في مؤشر كفاءة الآليات ولا تعتبر خللاً غير مربوط.
                    </p>
                  </div>
                </label>
              </div>
            )}

            {/* Exit Note Flag: Invoice Exit (خروج بموجب فاتورة) */}
            {currentNoteType?.code === 'EXIT' && (
              <div className="md:col-span-3 rounded-xl border border-sky-500/40 bg-sky-950/20 p-4 transition-all">
                <label className="flex items-start gap-3 cursor-pointer select-none">
                  <input
                    type="checkbox"
                    checked={isInvoice}
                    onChange={(e) => setIsInvoice(e.target.checked)}
                    className="mt-0.5 h-5 w-5 rounded border-sky-500/60 bg-slate-900 text-sky-500 focus:ring-sky-400 cursor-pointer"
                  />
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <span className="font-bold text-sm text-sky-300">خروج بموجب فاتورة</span>
                      <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-sky-500/20 text-sky-300 border border-sky-500/30">
                        فاتورة رسمية بدلاً من مذكرة إخراج
                      </span>
                    </div>
                    <p className="text-xs text-sky-200/80 leading-relaxed">
                      تُعامل معاملة مذكرة الإخراج للربط مع مذكرات الإدخال، مع السماح بتكرار رقم الفاتورة للشركة وتجاوز تنبيه التكرار إلا في حال تطابق تاريخ التحرير لنفس الفاتورة تماماً.
                    </p>
                  </div>
                </label>
              </div>
            )}

            {/* Note Source Management for ENTRY notes */}
            {isEntryNote && !isLoot && (
              <div className="md:col-span-3 rounded-xl border border-sky-500/40 bg-sky-950/20 p-3.5 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                <div>
                  <div className="text-xs font-bold text-sky-300 flex items-center gap-1.5">
                    <span>📦</span>
                    <span>مصدر مذكرة الإدخال (مذكرات إخراج، فواتير، غنائم ومضبوطات، مصدر آخر)</span>
                  </div>
                  <p className="text-[11px] text-sky-200/80 mt-0.5">
                    {noteId
                      ? 'يمكنك توثيق مراجع مذكرات الإخراج أو إدخال الفواتير والمرفقات ومصادر المذكرة بدقة.'
                      : 'احفظ المذكرة أولاً لتتمكن من إضافة تفاصيل ومرفقات المصدر.'}
                  </p>
                </div>
                <button
                  type="button"
                  disabled={!noteId}
                  onClick={() => setShowNoteSourceModal(true)}
                  className="px-3.5 py-1.5 rounded-lg bg-sky-600 hover:bg-sky-500 disabled:opacity-50 text-xs font-semibold text-white whitespace-nowrap transition-all shadow-sm cursor-pointer"
                >
                  إدارة مصدر المذكرة
                </button>
              </div>
            )}

            {/* Entry/Exit Note Linking during Creation or Editing */}
            {isLoot ? (
              <div className="md:col-span-3 rounded-xl border border-emerald-500/40 bg-emerald-950/25 p-3.5 flex items-center gap-3">
                <span className="text-2xl">🛡️</span>
                <div className="text-xs text-emerald-200 leading-relaxed">
                  <span className="font-bold text-emerald-300">ملاحظة الربط:</span> المذكرة مصنفة كـ «غنائم ومستوردات» وهي معفية نظامياً من الربط بمذكرات إخراج، وتُحتسب مطابقة تلقائياً في مؤشر كفاءة الآليات.
                </div>
              </div>
            ) : (
              (linkCandidates.length > 0 || (noteId && (currentNoteType?.code === 'ENTRY' || currentNoteType?.code === 'EXIT'))) && (
                <div className="md:col-span-3 rounded-xl border border-teal-500/40 bg-teal-950/20 p-3.5 space-y-3">
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                    <label className="text-xs font-bold text-teal-300 flex items-center gap-1.5">
                      <span>🔗</span>
                      <span>
                        {isEntryNote
                          ? 'ربط مذكرة الإدخال بمذكرات الإخراج المقابلة (مذكرة، مذكرتان، أو أكثر لنفس الشركة)'
                          : 'ربط مذكرة الإخراج بمذكرة الإدخال المقابلة لنفس الشركة'}
                      </span>
                    </label>
                    <div className="flex items-center gap-2">
                      <span className="text-[11px] text-teal-400 font-semibold">({linkCandidates.length} متاحة)</span>
                      {noteId && (
                        <button
                          type="button"
                          onClick={() => setShowLinksModal(true)}
                          className="px-2.5 py-1 text-[11px] font-semibold rounded-lg bg-teal-600/30 border border-teal-500/40 text-teal-200 hover:bg-teal-600/40 transition-colors cursor-pointer"
                        >
                          فتح نافذة الربط المتقدمة
                        </button>
                      )}
                    </div>
                  </div>

                  {/* Selected Linked Notes Chips */}
                  {relatedNoteIds.length > 0 && (
                    <div className="space-y-1.5">
                      <span className="text-[11px] font-semibold text-slate-300">
                        {isEntryNote ? `المذكرات المقابلة المرتبطة (${relatedNoteIds.length}):` : 'المذكرة المقابلة المرتبطة:'}
                      </span>
                      <div className="flex flex-wrap gap-2">
                        {relatedNoteIds.map((rId) => {
                          const cand = linkCandidates.find((c) => c.noteId === rId);
                          return (
                            <span
                              key={rId}
                              className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-teal-900/50 border border-teal-500/50 text-teal-200 text-xs font-semibold"
                            >
                              <span>🔗 مذكرة رقم {cand ? cand.noteNumber : rId.slice(0, 8)}</span>
                              {cand?.isInvoice && (
                                <span className="px-1 py-0.2 rounded text-[9px] bg-sky-500/20 text-sky-300 border border-sky-500/30 font-normal">
                                  فاتورة
                                </span>
                              )}
                              {cand && <span className="text-[10px] text-teal-300/70">({cand.arrivalAt})</span>}
                              <button
                                type="button"
                                onClick={() => setRelatedNoteIds((prev) => prev.filter((id) => id !== rId))}
                                className="mr-1 text-teal-300 hover:text-rose-400 font-bold text-sm cursor-pointer"
                                title="إلغاء الربط بهذه المذكرة"
                              >
                                ×
                              </button>
                            </span>
                          );
                        })}
                      </div>
                    </div>
                  )}

                  {/* Dropdown to select/add counterpart memo */}
                  <div className="space-y-1.5">
                    {linkCandidates.length > 3 && (
                      <input
                        type="text"
                        value={linkFilterQuery}
                        onChange={(e) => setLinkFilterQuery(e.target.value)}
                        placeholder="تصفية المذكرات المقابلة بالرقم أو المصنع..."
                        className="w-full rounded-lg border border-teal-500/30 bg-slate-900/80 px-2.5 py-1 text-xs text-slate-200 placeholder-slate-500 focus:border-teal-400 focus:outline-none"
                      />
                    )}
                    <select
                      value={!isEntryNote ? (relatedNoteIds[0] || '') : ''}
                      onChange={(e) => {
                        const val = e.target.value;
                        if (!val) {
                          if (!isEntryNote) setRelatedNoteIds([]);
                          return;
                        }
                        if (isEntryNote) {
                          if (!relatedNoteIds.includes(val)) {
                            setRelatedNoteIds((prev) => [...prev, val]);
                          }
                        } else {
                          setRelatedNoteIds([val]);
                        }
                      }}
                      className="w-full rounded-lg border border-teal-500/50 bg-slate-900 px-3 py-2 text-xs text-slate-100 focus:border-teal-400 focus:outline-none"
                    >
                      <option value="">
                        {isEntryNote
                          ? '+ اختر مذكرة إخراج لإضافتها إلى قائمة المذكرات المرتبطة...'
                          : '-- اختر مذكرة إدخال للربط معها (أو اختر هذا الخيار لإلغاء الربط) --'}
                      </option>
                      {filteredLinkCandidates
                        .filter((c) => isEntryNote ? !relatedNoteIds.includes(c.noteId) : true)
                        .map((c) => (
                          <option key={c.noteId} value={c.noteId}>
                            مذكرة رقم {c.noteNumber} ({c.noteTypeName}) {c.isInvoice ? '[فاتورة] ' : ''}— تاريخ الورود: {c.arrivalAt} {c.factoryName ? `— ${c.factoryName}` : ''} {c.isLinked ? '✓ [مرتبطة حالياً]' : ''}
                          </option>
                        ))}
                    </select>
                  </div>

                  <p className="text-[11px] text-teal-300/80 leading-relaxed">
                    {isEntryNote
                      ? 'يمكن ربط مذكرة الإدخال بأكثر من مذكرة إخراج. يتم توثيق الربط وحساب كفاءة الآليات تلقائياً فور الحفظ.'
                      : 'سيتم إنشاء الربط الثنائي بين المذكرتين تلقائياً وتوثيق ذلك في سجل التدقيق فور الحفظ.'}
                  </p>
                </div>
              )
            )}

            <div className="md:col-span-3">
              <label className="block text-xs font-semibold text-slate-300 mb-1">ملاحظات مدخل البيانات</label>
              <input
                type="text"
                value={notesText}
                onChange={(e) => setNotesText(e.target.value)}
                placeholder="أي ملاحظات عامة حول المذكرة أو استثناءات..."
                className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-slate-100 focus:border-teal-500 focus:outline-none"
              />
            </div>

            {/* Duplicate Reason input if duplicate is detected or already has value */}
            {(duplicateWarning || duplicateReason) && (
              <div className="md:col-span-3 rounded-xl border border-amber-500/40 bg-amber-950/20 p-3.5 space-y-2">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                  <label className="block text-xs font-bold text-amber-300">
                    سبب الاستمرار رغم تكرار رقم المذكرة مع نفس الشركة *
                  </label>
                  {duplicatesList.length > 0 && (
                    <button
                      type="button"
                      onClick={() => setShowDuplicateResolution(true)}
                      className="px-3 py-1 rounded-lg bg-amber-600/30 border border-amber-500/40 text-xs font-semibold text-amber-200 hover:bg-amber-600/40 transition-colors whitespace-nowrap cursor-pointer self-start sm:self-auto"
                    >
                      🔍 مقارنة المذكرات المتطابقة ({duplicatesList.length})
                    </button>
                  )}
                </div>
                <input
                  type="text"
                  required
                  value={duplicateReason}
                  onChange={(e) => setDuplicateReason(e.target.value)}
                  placeholder="مثال: مذكرة تعديلية رسمية / محضر إلحاقي برقم مختلف..."
                  className="w-full rounded-lg border border-amber-500/50 bg-slate-900 px-3 py-2 text-sm text-amber-100 placeholder-amber-500/50 focus:border-amber-400 focus:outline-none"
                />
              </div>
            )}
          </div>

          {/* Multiple Attachments Upload & Management */}
          <div className="rounded-xl border border-slate-800 bg-slate-800/40 p-4 space-y-3">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
              <div>
                <label className="text-xs font-bold text-slate-200 block">مرفقات المذكرة (صور أو ملفات PDF متعددة)</label>
                <p className="text-[11px] text-slate-400">يمكنك رفع أكثر من ملف (PDF أو صور JPG/PNG/WEBP بحجم يصل إلى 20 ميغابايت لكل ملف)</p>
              </div>
              <label className="cursor-pointer inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-teal-600 hover:bg-teal-500 text-white text-xs font-semibold shadow-sm transition-all self-start sm:self-auto">
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
                </svg>
                <span>إضافة ملفات</span>
                <input
                  type="file"
                  multiple
                  accept=".pdf,image/png,image/jpeg,image/jpg,image/webp"
                  className="hidden"
                  onChange={(e) => {
                    if (e.target.files && e.target.files.length > 0) {
                      const newFiles = Array.from(e.target.files);
                      setSelectedFiles((prev) => [...prev, ...newFiles]);
                    }
                    e.target.value = '';
                  }}
                />
              </label>
            </div>

            {/* Existing Attachments (When editing) */}
            {existingAttachments.length > 0 && (
              <div className="space-y-1.5">
                <span className="text-[11px] font-semibold text-slate-300">المرفقات المسجلة حالياً ({existingAttachments.length}):</span>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                  {existingAttachments.map((att) => (
                    <div key={att.id} className="flex items-center justify-between p-2 rounded-lg border border-slate-700 bg-slate-900/80 text-xs">
                      <div className="flex items-center gap-2 truncate">
                        <span className="text-teal-400">{att.contentType.includes('pdf') ? '📄' : '🖼️'}</span>
                        <span className="text-slate-200 truncate font-medium" title={att.fileName}>{att.fileName}</span>
                        <span className="text-[10px] text-slate-400">({(att.fileSize / 1024).toFixed(0)} KB)</span>
                      </div>
                      <div className="flex items-center gap-1.5 flex-shrink-0">
                        <a
                          href={api.getAttachmentUrl(noteId!, att.id)}
                          target="_blank"
                          rel="noreferrer"
                          className="px-2 py-1 rounded text-[11px] bg-slate-800 border border-slate-700 text-teal-300 hover:bg-slate-750"
                        >
                          معاينة
                        </a>
                        <button
                          type="button"
                          onClick={() => handleDeleteExistingAttachment(att.id, att.fileName)}
                          className="px-2 py-1 rounded text-[11px] bg-rose-500/15 border border-rose-500/30 text-rose-400 hover:bg-rose-500/25"
                        >
                          حذف
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Newly Staged Files */}
            {selectedFiles.length > 0 && (
              <div className="space-y-1.5 pt-2 border-t border-slate-700/50">
                <span className="text-[11px] font-semibold text-amber-300">ملفات جديدة محددة للرفع عند الحفظ ({selectedFiles.length}):</span>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                  {selectedFiles.map((file, idx) => (
                    <div key={idx} className="flex items-center justify-between p-2 rounded-lg border border-amber-500/30 bg-amber-950/20 text-xs">
                      <div className="flex items-center gap-2 truncate">
                        <span className="text-amber-400">{file.type.includes('pdf') ? '📄' : '🖼️'}</span>
                        <span className="text-amber-100 truncate font-medium" title={file.name}>{file.name}</span>
                        <span className="text-[10px] text-amber-400/80">({(file.size / 1024).toFixed(0)} KB)</span>
                      </div>
                      <button
                        type="button"
                        onClick={() => setSelectedFiles((prev) => prev.filter((_, i) => i !== idx))}
                        className="text-rose-400 hover:text-rose-300 px-2 py-0.5 text-xs font-bold"
                        title="إلغاء هذا الملف"
                      >
                        ✕
                      </button>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>

          {/* Dynamic Indicators Section with Default Correct and Non-Mandatory Reason for Correct */}
          <div className="rounded-xl border border-slate-800 bg-slate-800/30 p-4">
            <div className="flex items-center justify-between mb-4">
              <div>
                <h3 className="text-sm font-bold text-slate-200">حقول المؤشرات المطلوبة للمراجعة</h3>
                <p className="text-xs text-slate-400">جميع المؤشرات مهيأة كـ «صحيح» افتراضياً. الملاحظة إلزامية فقط في حالات الخلل والنقص</p>
              </div>
              {fields.length > 0 && (
                <button
                  type="button"
                  onClick={handleMarkAllCorrect}
                  className="rounded-lg bg-emerald-600/20 border border-emerald-500/30 px-3 py-1 text-xs font-semibold text-emerald-400 hover:bg-emerald-600/30"
                >
                  ✓ تعيين الكل صحيح
                </button>
              )}
            </div>

            {fields.length === 0 ? (
              <div className="py-6 text-center text-xs text-slate-500">
                {noteTypeId ? 'لا توجد حقول مؤشرات معرفة لهذا النوع.' : 'يرجى اختيار نوع المذكرة أولاً لعرض المؤشرات.'}
              </div>
            ) : (
              <div className="space-y-3">
                {fields.map((f) => {
                  const current = fieldValues[f.fieldId]?.value ?? IndicatorValue.Correct;
                  const isDefective = isDefectiveValue(current);
                  return (
                    <div
                      key={f.fieldId}
                      className="p-3 rounded-lg border border-slate-700/60 bg-slate-800/60 space-y-2"
                    >
                      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                        <div className="flex items-center gap-2">
                          <span className="text-xs font-medium text-slate-200">{f.fieldNameAr}</span>
                          {f.isRequired && <span className="text-rose-400 text-xs">*</span>}
                        </div>

                        <div className="flex flex-wrap items-center gap-1.5">
                          <button
                            type="button"
                            onClick={() => handleFieldChange(f.fieldId, IndicatorValue.Correct)}
                            className={`px-2.5 py-1 text-xs rounded-md font-semibold transition-all ${
                              current === IndicatorValue.Correct
                                ? 'bg-emerald-600 text-white shadow-sm'
                                : 'bg-slate-700/60 text-slate-300 hover:bg-slate-700'
                            }`}
                          >
                            صحيح
                          </button>
                          <button
                            type="button"
                            onClick={() => handleFieldChange(f.fieldId, IndicatorValue.Incorrect)}
                            className={`px-2.5 py-1 text-xs rounded-md font-semibold transition-all ${
                              current === IndicatorValue.Incorrect
                                ? 'bg-rose-600 text-white shadow-sm'
                                : 'bg-slate-700/60 text-slate-300 hover:bg-slate-700'
                            }`}
                          >
                            غير صحيح
                          </button>
                          <button
                            type="button"
                            onClick={() => handleFieldChange(f.fieldId, IndicatorValue.Missing)}
                            className={`px-2.5 py-1 text-xs rounded-md font-semibold transition-all ${
                              current === IndicatorValue.Missing
                                ? 'bg-amber-600 text-white shadow-sm'
                                : 'bg-slate-700/60 text-slate-300 hover:bg-slate-700'
                            }`}
                          >
                            مفقود
                          </button>
                          <button
                            type="button"
                            onClick={() => handleFieldChange(f.fieldId, IndicatorValue.Incomplete)}
                            className={`px-2.5 py-1 text-xs rounded-md font-semibold transition-all ${
                              current === IndicatorValue.Incomplete
                                ? 'bg-orange-600 text-white shadow-sm'
                                : 'bg-slate-700/60 text-slate-300 hover:bg-slate-700'
                            }`}
                          >
                            غير مكتمل
                          </button>
                          <button
                            type="button"
                            onClick={() => handleFieldChange(f.fieldId, IndicatorValue.Unclear)}
                            className={`px-2.5 py-1 text-xs rounded-md font-semibold transition-all ${
                              current === IndicatorValue.Unclear
                                ? 'bg-purple-600 text-white shadow-sm'
                                : 'bg-slate-700/60 text-slate-300 hover:bg-slate-700'
                            }`}
                          >
                            غير واضح
                          </button>
                          <button
                            type="button"
                            onClick={() => handleFieldChange(f.fieldId, IndicatorValue.NotApplicable)}
                            className={`px-2.5 py-1 text-xs rounded-md font-semibold transition-all ${
                              current === IndicatorValue.NotApplicable
                                ? 'bg-slate-600 text-white shadow-sm'
                                : 'bg-slate-700/60 text-slate-300 hover:bg-slate-700'
                            }`}
                          >
                            غير منطبق
                          </button>
                          <button
                            type="button"
                            onClick={() => handleFieldChange(f.fieldId, IndicatorValue.Unreviewed)}
                            className={`px-2.5 py-1 text-xs rounded-md font-semibold transition-all ${
                              current === IndicatorValue.Unreviewed
                                ? 'bg-slate-500 text-white shadow-sm'
                                : 'bg-slate-700/30 text-slate-400 hover:bg-slate-700'
                            }`}
                          >
                            غير مدقق
                          </button>
                        </div>
                      </div>

                      {/* Reason Input: Always optional as requested */}
                      <div className="pt-2 border-t border-slate-700/30 flex flex-col sm:flex-row sm:items-center gap-2">
                        <label className={`text-xs sm:w-44 flex-shrink-0 ${isDefective ? 'font-medium text-rose-300' : 'text-slate-400'}`}>
                          {isDefective ? 'سبب عدم الصلاحية (اختياري)' : 'ملاحظة إضافية (اختيارية)'}
                        </label>
                        <input
                          type="text"
                          value={fieldValues[f.fieldId]?.reason || ''}
                          onChange={(e) => handleFieldChange(f.fieldId, current, e.target.value)}
                          placeholder={isDefective ? 'أدخل سبب عدم الصلاحية أو الملاحظة (اختياري)...' : 'ملاحظة للبند إن وجدت (اختياري، لا يعيق الحفظ)...'}
                          className={`w-full rounded-lg border px-3 py-1.5 text-xs text-slate-100 placeholder-slate-500 focus:outline-none ${
                            isDefective
                              ? 'border-rose-500/40 bg-slate-900 focus:border-rose-400'
                              : 'border-slate-700/60 bg-slate-900/60 focus:border-teal-500'
                          }`}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>

          {/* Retain for Next Entry Checkbox (Only when creating new note) */}
          {!noteId && (
            <div className="flex items-center gap-2 p-3 rounded-xl border border-slate-800 bg-slate-800/30">
              <input
                type="checkbox"
                id="retainForNext"
                checked={retainForNext}
                onChange={(e) => setRetainForNext(e.target.checked)}
                className="w-4 h-4 rounded border-slate-700 text-teal-600 focus:ring-teal-500 bg-slate-800 cursor-pointer"
              />
              <label htmlFor="retainForNext" className="text-xs text-slate-300 cursor-pointer select-none">
                الاحتفاظ بالبيانات المشتركة (الشركة، المعمل، الخط، التاريخ) لتسريع إدخال المذكرة التالية
              </label>
            </div>
          )}

          {/* Action Buttons */}
          <div className="flex flex-col sm:flex-row items-center justify-between gap-3 pt-4 border-t border-slate-800">
            <button
              type="button"
              onClick={onClose}
              className="w-full sm:w-auto rounded-lg border border-slate-700 px-4 py-2 text-sm font-medium text-slate-300 hover:bg-slate-800"
            >
              إلغاء
            </button>

            <div className="flex items-center gap-3 w-full sm:w-auto">
              <button
                type="button"
                disabled={saving}
                onClick={() => handleSave(true)}
                className="flex-1 sm:flex-none rounded-lg border border-slate-600 bg-slate-700/50 px-4 py-2 text-sm font-medium text-slate-200 hover:bg-slate-700 disabled:opacity-50"
              >
                حفظ كمسودة
              </button>
              <button
                type="button"
                disabled={saving}
                onClick={() => handleSave(false)}
                className="flex-1 sm:flex-none rounded-lg bg-teal-600 px-5 py-2 text-sm font-semibold text-white shadow-sm hover:bg-teal-500 disabled:opacity-50"
              >
                {saving ? 'جارٍ الحفظ...' : 'إرسال للمراجعة'}
              </button>
            </div>
          </div>
        </div>
      )}
    </Modal>

    {showDuplicateResolution && (
      <DuplicateResolutionModal
        isOpen={showDuplicateResolution}
        onClose={() => setShowDuplicateResolution(false)}
        currentNote={{
          noteNumber: noteNumber.trim(),
          companyName: companies.find((c) => c.id === companyId)?.name,
          factoryName: factories.find((f) => f.id === factoryId)?.name,
          productionLineName: lines.find((l) => l.id === productionLineId)?.name,
          noteTypeName: currentNoteType?.nameAr,
          writingDate,
          arrivalAt,
          duplicateReason,
          attachmentPreviewUrl:
            selectedFiles.length > 0 &&
            (selectedFiles[0].type.startsWith('image/') || /\.(png|jpe?g|webp)$/i.test(selectedFiles[0].name))
              ? URL.createObjectURL(selectedFiles[0])
              : null,
          attachmentFileName: selectedFiles.length > 0 ? selectedFiles[0].name : existingAttachments[0]?.fileName,
        }}
        duplicates={duplicatesList}
        onApplyDecision={handleResolveDuplicateDecision}
      />
    )}

    {showNoteSourceModal && noteId && (
      <NoteSourceModal
        isOpen={showNoteSourceModal}
        onClose={() => setShowNoteSourceModal(false)}
        noteId={noteId}
        noteNumber={noteNumber}
        onOpenLinksModal={() => {
          setShowNoteSourceModal(false);
          setShowLinksModal(true);
        }}
      />
    )}

    {showLinksModal && noteId && (
      <MemoLinksModal
        isOpen={showLinksModal}
        onClose={() => setShowLinksModal(false)}
        noteId={noteId}
        noteNumber={noteNumber}
        allowChanges={true}
        onLinksChanged={() => {
          if (companyId && noteTypeId) {
            api.getAvailableLinkCandidates(companyId, noteTypeId, noteId).then((data) => {
              setLinkCandidates(data);
              const alreadyLinked = data.filter((c) => c.isLinked).map((c) => c.noteId);
              setRelatedNoteIds(alreadyLinked);
            }).catch(console.error);
          }
        }}
      />
    )}
  </>
  );
};

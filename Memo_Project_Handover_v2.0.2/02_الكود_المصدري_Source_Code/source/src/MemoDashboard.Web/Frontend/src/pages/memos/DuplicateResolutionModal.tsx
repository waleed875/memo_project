import React, { useState } from 'react';
import { Modal } from '../../components/common/Modal';
import { api } from '../../services/api';
import type { DuplicateClassification, NoteDuplicateModel } from '../../types/memo';

interface DuplicateResolutionModalProps {
  isOpen: boolean;
  onClose: () => void;
  currentNote: {
    noteNumber: string;
    companyName?: string;
    factoryName?: string;
    productionLineName?: string;
    noteTypeName?: string;
    writingDate?: string;
    arrivalAt?: string;
    duplicateReason?: string;
    attachmentPreviewUrl?: string | null;
    attachmentFileName?: string | null;
  };
  duplicates: NoteDuplicateModel[];
  onApplyDecision: (
    classification: DuplicateClassification,
    matchedNoteId: string,
    customReason?: string
  ) => void;
}

export const DuplicateResolutionModal: React.FC<DuplicateResolutionModalProps> = ({
  isOpen,
  onClose,
  currentNote,
  duplicates,
  onApplyDecision,
}) => {
  const [selectedDuplicateId, setSelectedDuplicateId] = useState<string>(
    duplicates[0]?.noteId || ''
  );
  const [classification, setClassification] = useState<DuplicateClassification>(
    'COMPANY_DUPLICATE_NUMBER'
  );
  const [reason, setReason] = useState<string>(currentNote.duplicateReason || '');

  const selectedDuplicate = duplicates.find((d) => d.noteId === selectedDuplicateId) || duplicates[0];

  const handleApply = () => {
    if (!selectedDuplicate) return;
    onApplyDecision(classification, selectedDuplicate.noteId, reason);
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="معالجة المذكرة المكررة (مطابقة السجلات)"
      maxWidth="6xl"
    >
      <div className="space-y-4" dir="rtl">
        {/* Banner */}
        <div className="rounded-xl border border-amber-500/40 bg-amber-500/10 p-3.5 text-xs text-amber-300 leading-relaxed">
          <p className="font-bold mb-1">⚠️ تم اكتشاف مذكرة سابقة تحمل نفس الرقم لنفس الشركة:</p>
          <p>
            اختر المذكرة المطابقة أولاً، ثم قارن بيانات وتفاصيل المذكرتين، وحدد طبيعة التكرار لاعتماد الإجراء المناسب في النظام وسجل التدقيق.
          </p>
        </div>

        {/* Duplicates Table */}
        <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-3.5 space-y-2">
          <div className="text-xs font-bold text-slate-300">
            المذكرات المطابقة المسجلة في النظام ({duplicates.length}):
          </div>
          <div className="max-h-36 overflow-y-auto rounded-xl border border-slate-800">
            <table className="w-full text-right text-xs">
              <thead className="bg-slate-800/80 text-slate-300 border-b border-slate-700/60 sticky top-0">
                <tr>
                  <th className="px-3 py-2 text-center w-12">تحديد</th>
                  <th className="px-3 py-2">رقم المذكرة</th>
                  <th className="px-3 py-2">المعمل / الخط</th>
                  <th className="px-3 py-2">تاريخ التحرير</th>
                  <th className="px-3 py-2">تاريخ الورود</th>
                  <th className="px-3 py-2">تاريخ الإدخال</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800 text-slate-200">
                {duplicates.map((dup) => {
                  const isSelected = dup.noteId === selectedDuplicateId;
                  return (
                    <tr
                      key={dup.noteId}
                      onClick={() => setSelectedDuplicateId(dup.noteId)}
                      className={`cursor-pointer transition-colors ${
                        isSelected ? 'bg-amber-950/40 border-l-2 border-amber-500 font-medium' : 'hover:bg-slate-800/40'
                      }`}
                    >
                      <td className="px-3 py-2 text-center">
                        <input
                          type="radio"
                          name="duplicateMatch"
                          checked={isSelected}
                          onChange={() => setSelectedDuplicateId(dup.noteId)}
                          className="accent-amber-500 cursor-pointer"
                        />
                      </td>
                      <td className="px-3 py-2 font-bold text-amber-300">{dup.noteNumber}</td>
                      <td className="px-3 py-2 text-slate-300">
                        {dup.factoryName || 'غير محدد'} {dup.productionLineName ? `(${dup.productionLineName})` : ''}
                      </td>
                      <td className="px-3 py-2 text-slate-400">{dup.writingDate || '-'}</td>
                      <td className="px-3 py-2">{dup.arrivalAt?.slice(0, 10)}</td>
                      <td className="px-3 py-2 text-[11px] text-slate-400">{dup.createdAt?.slice(0, 10)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>

        {/* Side-by-Side Comparison */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {/* Card 1: Current Note Being Entered */}
          <div className="rounded-2xl border border-teal-500/40 bg-teal-950/10 p-3.5 space-y-3">
            <div className="flex items-center justify-between border-b border-teal-500/20 pb-2">
              <span className="text-xs font-bold text-teal-300 flex items-center gap-1.5">
                <span>📝</span>
                <span>المذكرة الجاري إدخالها حالياً</span>
              </span>
              <span className="text-[11px] px-2 py-0.5 rounded bg-teal-500/20 text-teal-300 border border-teal-500/30">
                إدخال جديد
              </span>
            </div>

            <div className="grid grid-cols-2 gap-2 text-xs">
              <div className="text-slate-400">رقم المذكرة:</div>
              <div className="font-bold text-slate-100">{currentNote.noteNumber}</div>
              <div className="text-slate-400">النوع:</div>
              <div className="text-slate-200">{currentNote.noteTypeName || '-'}</div>
              <div className="text-slate-400">الشركة:</div>
              <div className="text-slate-200">{currentNote.companyName || '-'}</div>
              <div className="text-slate-400">المعمل / الخط:</div>
              <div className="text-slate-200">
                {currentNote.factoryName || '-'} {currentNote.productionLineName ? `(${currentNote.productionLineName})` : ''}
              </div>
              <div className="text-slate-400">تاريخ التحرير:</div>
              <div className="text-slate-200">{currentNote.writingDate || '-'}</div>
              <div className="text-slate-400">تاريخ الورود:</div>
              <div className="text-slate-200">{currentNote.arrivalAt || '-'}</div>
            </div>

            {/* Current Attachment Preview if exists */}
            <div className="border-t border-teal-500/20 pt-2">
              <span className="text-[11px] font-semibold text-slate-400 block mb-1">مرفق المذكرة الجاري إدخالها:</span>
              {currentNote.attachmentPreviewUrl ? (
                <div className="rounded-xl border border-slate-800 bg-slate-950/60 p-2 flex items-center justify-center max-h-52 overflow-hidden">
                  <img
                    src={currentNote.attachmentPreviewUrl}
                    alt="معاينة المرفق الجديد"
                    className="max-h-48 object-contain rounded"
                  />
                </div>
              ) : currentNote.attachmentFileName ? (
                <div className="p-3 text-center text-xs text-slate-400 bg-slate-900/50 rounded-lg border border-slate-800">
                  📎 {currentNote.attachmentFileName}
                </div>
              ) : (
                <div className="p-3 text-center text-xs text-slate-500 bg-slate-900/50 rounded-lg border border-slate-800">
                  لا يوجد ملف مرفق محدد حالياً
                </div>
              )}
            </div>
          </div>

          {/* Card 2: Existing Note In Database */}
          <div className="rounded-2xl border border-amber-500/40 bg-amber-950/10 p-3.5 space-y-3">
            <div className="flex items-center justify-between border-b border-amber-500/20 pb-2">
              <span className="text-xs font-bold text-amber-300 flex items-center gap-1.5">
                <span>📁</span>
                <span>المذكرة الموجودة مسبقاً في النظام</span>
              </span>
              <span className="text-[11px] px-2 py-0.5 rounded bg-amber-500/20 text-amber-300 border border-amber-500/30">
                سجل سابق
              </span>
            </div>

            {selectedDuplicate ? (
              <>
                <div className="grid grid-cols-2 gap-2 text-xs">
                  <div className="text-slate-400">رقم المذكرة:</div>
                  <div className="font-bold text-slate-100">{selectedDuplicate.noteNumber}</div>
                  <div className="text-slate-400">النوع:</div>
                  <div className="text-slate-200">{selectedDuplicate.noteTypeName}</div>
                  <div className="text-slate-400">الشركة:</div>
                  <div className="text-slate-200">{selectedDuplicate.companyName}</div>
                  <div className="text-slate-400">المعمل / الخط:</div>
                  <div className="text-slate-200">
                    {selectedDuplicate.factoryName || '-'} {selectedDuplicate.productionLineName ? `(${selectedDuplicate.productionLineName})` : ''}
                  </div>
                  <div className="text-slate-400">تاريخ التحرير:</div>
                  <div className="text-slate-200">{selectedDuplicate.writingDate || '-'}</div>
                  <div className="text-slate-400">تاريخ الورود:</div>
                  <div className="text-slate-200">{selectedDuplicate.arrivalAt?.slice(0, 10)}</div>
                </div>

                <div className="border-t border-amber-500/20 pt-2">
                  <span className="text-[11px] font-semibold text-slate-400 block mb-1">مرفق المذكرة المسجلة:</span>
                  <div className="rounded-xl border border-slate-800 bg-slate-950/60 p-2 flex items-center justify-center max-h-52 overflow-hidden">
                    <img
                      src={api.getAttachmentUrl(selectedDuplicate.noteId)}
                      alt="مرفق المذكرة الموجودة"
                      onError={(e) => {
                        (e.target as HTMLElement).style.display = 'none';
                      }}
                      className="max-h-48 object-contain rounded"
                    />
                    <a
                      href={api.getAttachmentUrl(selectedDuplicate.noteId)}
                      target="_blank"
                      rel="noreferrer"
                      className="text-xs text-teal-400 hover:underline block text-center"
                    >
                      فتح المرفق الكامل في نافذة مستقلة ↗
                    </a>
                  </div>
                </div>
              </>
            ) : (
              <div className="py-12 text-center text-xs text-slate-500">
                يرجى اختيار مذكرة من جدول التطابقات أعلاه
              </div>
            )}
          </div>
        </div>

        {/* Decisions Radio List matching Desktop FrmDuplicateResolution */}
        <div className="rounded-2xl border border-slate-800 bg-slate-900/80 p-4 space-y-3">
          <div className="text-xs font-bold text-slate-200">حدد الإجراء والسبب المعتمد لهذا التكرار:</div>

          <div className="space-y-2.5">
            <label className="flex items-start gap-2.5 cursor-pointer p-2.5 rounded-xl border border-slate-800 hover:bg-slate-800/40">
              <input
                type="radio"
                name="decisionClassification"
                checked={classification === 'COMPANY_DUPLICATE_NUMBER'}
                onChange={() => setClassification('COMPANY_DUPLICATE_NUMBER')}
                className="mt-0.5 accent-teal-500"
              />
              <div className="text-xs leading-relaxed">
                <span className="font-bold text-slate-100 block">
                  مذكرتان مختلفتان برقم واحد (خطأ تكرار من الشركة)
                </span>
                <span className="text-slate-400 text-[11px]">
                  احفظ المذكرة الجديدة كإدخال مستقل وسجّل مخالفة تكرار الرقم على الشركة في تقارير الجودة.
                </span>
              </div>
            </label>

            <label className="flex items-start gap-2.5 cursor-pointer p-2.5 rounded-xl border border-slate-800 hover:bg-slate-800/40">
              <input
                type="radio"
                name="decisionClassification"
                checked={classification === 'COMPANY_RESEND'}
                onChange={() => setClassification('COMPANY_RESEND')}
                className="mt-0.5 accent-teal-500"
              />
              <div className="text-xs leading-relaxed">
                <span className="font-bold text-slate-100 block">
                  أعادت الشركة إرسال المذكرة نفسها
                </span>
                <span className="text-slate-400 text-[11px]">
                  لا تقم بحفظ المذكرة أو إدراج سجل مكرر في قاعدة البيانات.
                </span>
              </div>
            </label>

            <label className="flex items-start gap-2.5 cursor-pointer p-2.5 rounded-xl border border-slate-800 hover:bg-slate-800/40">
              <input
                type="radio"
                name="decisionClassification"
                checked={classification === 'DATA_ENTRY_DUPLICATE_ATTEMPT'}
                onChange={() => setClassification('DATA_ENTRY_DUPLICATE_ATTEMPT')}
                className="mt-0.5 accent-teal-500"
              />
              <div className="text-xs leading-relaxed">
                <span className="font-bold text-slate-100 block">
                  محاولة إدخال مكرر من قِبل مدخل البيانات
                </span>
                <span className="text-slate-400 text-[11px]">
                  المذكرة مسجلة سابقاً بالفعل في النظام — سجّل محاولة الإدخال فقط في سجل التدقيق دون إضافة سجل جديد.
                </span>
              </div>
            </label>
          </div>

          {/* Reason Input */}
          <div className="pt-2 border-t border-slate-800">
            <label className="block text-xs font-semibold text-slate-300 mb-1">
              ملاحظة أو تبرير مدخل البيانات للقرار:
            </label>
            <input
              type="text"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="اكتب توضيحاً موجزاً..."
              className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-1.5 text-xs text-slate-100 focus:border-teal-500 focus:outline-none"
            />
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between pt-3 border-t border-slate-800">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-slate-700 px-4 py-2 text-xs font-medium text-slate-300 hover:bg-slate-800 cursor-pointer"
          >
            إلغاء الحفظ
          </button>
          <button
            type="button"
            disabled={!selectedDuplicate}
            onClick={handleApply}
            className="rounded-lg bg-teal-600 px-5 py-2 text-xs font-bold text-white shadow-md hover:bg-teal-500 disabled:opacity-40 transition-all cursor-pointer"
          >
            اعتماد القرار المختار
          </button>
        </div>
      </div>
    </Modal>
  );
};

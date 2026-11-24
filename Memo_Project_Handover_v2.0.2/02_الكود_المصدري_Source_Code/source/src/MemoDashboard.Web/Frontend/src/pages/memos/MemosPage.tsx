import React, { useEffect, useState, useCallback } from 'react';
import { api } from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import { MemoEditorModal } from './MemoEditorModal';
import { MemoViewModal } from './MemoViewModal';
import { MemoLinksModal } from './MemoLinksModal';
import { NoteSourceModal } from './NoteSourceModal';
import {
  normalizeNoteStatus,
  type CompanyModel,
  type NoteDetailsModel,
  type NoteStatus,
  type NoteTypeModel,
  type UserModel,
} from '../../types/memo';

export const MemosPage: React.FC = () => {
  const { hasRole } = useAuth();
  const canEdit = hasRole('ADMIN', 'DATA_ENTRY');
  const canDelete = hasRole('ADMIN');

  const [notes, setNotes] = useState<NoteDetailsModel[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize] = useState(25);
  const [loading, setLoading] = useState(false);

  // Lookups
  const [companies, setCompanies] = useState<CompanyModel[]>([]);
  const [noteTypes, setNoteTypes] = useState<NoteTypeModel[]>([]);
  const [users, setUsers] = useState<UserModel[]>([]);

  // Filter state
  const [noteNumber, setNoteNumber] = useState('');
  const [companyId, setCompanyId] = useState('');
  const [noteTypeId, setNoteTypeId] = useState('');
  const [status, setStatus] = useState<NoteStatus | ''>('');
  const [createdBy, setCreatedBy] = useState('');
  const [unlinkedOnly, setUnlinkedOnly] = useState(false);
  const [missingWritingDateOnly, setMissingWritingDateOnly] = useState(false);
  const [lootOnly, setLootOnly] = useState(false);
  const [invoiceOnly, setInvoiceOnly] = useState(false);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [dateBasis, setDateBasis] = useState<'ArrivalDate' | 'WritingDate'>('ArrivalDate');

  // Editor Modal State
  const [isEditorOpen, setIsEditorOpen] = useState(false);
  const [selectedNoteId, setSelectedNoteId] = useState<string | null>(null);

  // Viewer Modal State
  const [isViewOpen, setIsViewOpen] = useState(false);
  const [viewNoteId, setViewNoteId] = useState<string | null>(null);

  // Links Modal State
  const [isLinksOpen, setIsLinksOpen] = useState(false);
  const [linksNote, setLinksNote] = useState<{ id: string; number: string } | null>(null);

  // Note Source Modal State
  const [isSourceOpen, setIsSourceOpen] = useState(false);
  const [sourceNote, setSourceNote] = useState<{ id: string; number: string } | null>(null);

  useEffect(() => {
    api.getCompanies().then(setCompanies).catch(console.error);
    api.getNoteTypes().then(setNoteTypes).catch(console.error);
    api.getUsers().then(setUsers).catch(() => setUsers([]));
  }, []);

  const fetchNotes = useCallback(async () => {
    setLoading(true);
    try {
      const res = await api.searchNotes({
        noteNumber: noteNumber.trim() || undefined,
        companyId: companyId || undefined,
        noteTypeId: noteTypeId || undefined,
        status: status || undefined,
        createdBy: createdBy || undefined,
        unlinkedOnly: unlinkedOnly ? true : undefined,
        hasWritingDate: missingWritingDateOnly ? false : undefined,
        isLoot: lootOnly ? true : undefined,
        isInvoice: invoiceOnly ? true : undefined,
        from: from || undefined,
        to: to || undefined,
        dateBasis,
        pageNumber,
        pageSize,
      });
      setNotes(res.items);
      setTotalCount(res.totalCount);
      setTotalPages(res.totalPages);
    } catch (err) {
      console.error('Failed to search notes', err);
    } finally {
      setLoading(false);
    }
  }, [
    noteNumber,
    companyId,
    noteTypeId,
    status,
    createdBy,
    unlinkedOnly,
    missingWritingDateOnly,
    lootOnly,
    invoiceOnly,
    from,
    to,
    dateBasis,
    pageNumber,
    pageSize,
  ]);

  useEffect(() => {
    fetchNotes();
  }, [fetchNotes]);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setPageNumber(1);
    fetchNotes();
  };

  const handleReset = () => {
    setNoteNumber('');
    setCompanyId('');
    setNoteTypeId('');
    setStatus('');
    setCreatedBy('');
    setUnlinkedOnly(false);
    setMissingWritingDateOnly(false);
    setLootOnly(false);
    setInvoiceOnly(false);
    setFrom('');
    setTo('');
    setDateBasis('ArrivalDate');
    setPageNumber(1);
  };

  const handleQuickMonth = (monthIndex: number, year = new Date().getFullYear()) => {
    const pad = (n: number) => String(n).padStart(2, '0');
    const start = `${year}-${pad(monthIndex)}-01`;
    const lastDay = new Date(year, monthIndex, 0).getDate();
    const end = `${year}-${pad(monthIndex)}-${pad(lastDay)}`;
    setFrom(start);
    setTo(end);
    setPageNumber(1);
  };

  const handleOpenNew = () => {
    setSelectedNoteId(null);
    setIsEditorOpen(true);
  };

  const handleOpenEdit = (id: string) => {
    setSelectedNoteId(id);
    setIsEditorOpen(true);
  };

  const handleOpenView = (id: string) => {
    setViewNoteId(id);
    setIsViewOpen(true);
  };

  const handleOpenLinks = (id: string, number: string) => {
    setLinksNote({ id, number });
    setIsLinksOpen(true);
  };

  const handleOpenSource = (id: string, number: string) => {
    setSourceNote({ id, number });
    setIsSourceOpen(true);
  };

  const handleDelete = async (note: NoteDetailsModel) => {
    if (!window.confirm(`هل أنت متأكد من حذف المذكرة رقم ${note.noteNumber}؟\nتنبيه: الحذف يقتصر على مدراء النظام فقط.`)) return;
    try {
      await api.deleteNote(note.noteId, note.updatedAt);
      fetchNotes();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل حذف المذكرة.');
    }
  };

  const getStatusBadge = (s: unknown) => {
    const normalized = normalizeNoteStatus(s);
    const config: Record<NoteStatus, { label: string; bg: string; text: string }> = {
      APPROVED: { label: 'معتمدة', bg: 'bg-emerald-500/15 border-emerald-500/30', text: 'text-emerald-400' },
      RETURNED_FOR_CORRECTION: { label: 'معادة للتصحيح', bg: 'bg-rose-500/15 border-rose-500/30', text: 'text-rose-400' },
      UNDER_REVIEW: { label: 'قيد المراجعة', bg: 'bg-blue-500/15 border-blue-500/30', text: 'text-blue-400' },
      NEEDS_REVIEW: { label: 'بانتظار المراجعة', bg: 'bg-indigo-500/15 border-indigo-500/30', text: 'text-indigo-400' },
      DRAFT: { label: 'مسودة', bg: 'bg-amber-500/15 border-amber-500/30', text: 'text-amber-400' },
      COMPLETE: { label: 'مكتملة', bg: 'bg-teal-500/15 border-teal-500/30', text: 'text-teal-400' },
      HAS_ERRORS: { label: 'تحوي أخطاء', bg: 'bg-orange-500/15 border-orange-500/30', text: 'text-orange-400' },
    };
    const c = config[normalized] || { label: String(s), bg: 'bg-slate-500/15 border-slate-500/30', text: 'text-slate-400' };
    return (
      <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold border ${c.bg} ${c.text}`}>
        {c.label}
      </span>
    );
  };

  return (
    <div className="space-y-6" dir="rtl">
      {/* Top action header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-slate-100">سجل مذكرات التدقيق</h1>
          <p className="text-xs text-slate-400 mt-1">البحث والفلترة وإدارة مذكرات تدقيق المنشآت الصناعية وربطها ومراجعتها</p>
        </div>
        {canEdit && (
          <button
            type="button"
            onClick={handleOpenNew}
            className="inline-flex items-center gap-2 rounded-xl bg-teal-600 px-4 py-2.5 text-sm font-semibold text-white shadow-md hover:bg-teal-500 transition-all cursor-pointer"
          >
            <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
            </svg>
            إدخال مذكرة جديدة
          </button>
        )}
      </div>

      {/* Filter Bar */}
      <form onSubmit={handleSearch} className="rounded-2xl border border-slate-800 bg-slate-900/60 p-4 shadow-sm space-y-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 lg:grid-cols-8 gap-3">
          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">رقم المذكرة</label>
            <input
              type="text"
              value={noteNumber}
              onChange={(e) => setNoteNumber(e.target.value)}
              placeholder="بحث بالرقم..."
              className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-1.5 text-xs text-slate-200 placeholder-slate-500 focus:border-teal-500 focus:outline-none"
            />
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">الشركة</label>
            <select
              value={companyId}
              onChange={(e) => setCompanyId(e.target.value)}
              className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-1.5 text-xs text-slate-200 focus:border-teal-500 focus:outline-none"
            >
              <option value="">كافة الشركات</option>
              {companies.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">نوع المذكرة</label>
            <select
              value={noteTypeId}
              onChange={(e) => setNoteTypeId(e.target.value)}
              className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-1.5 text-xs text-slate-200 focus:border-teal-500 focus:outline-none"
            >
              <option value="">كافة الأنواع</option>
              {noteTypes.map((t) => (
                <option key={t.id} value={t.id}>{t.nameAr}</option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">الحالة</label>
            <select
              value={status}
              onChange={(e) => setStatus(e.target.value as NoteStatus | '')}
              className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-1.5 text-xs text-slate-200 focus:border-teal-500 focus:outline-none"
            >
              <option value="">كافة الحالات</option>
              <option value="DRAFT">مسودة</option>
              <option value="NEEDS_REVIEW">بانتظار المراجعة</option>
              <option value="UNDER_REVIEW">قيد المراجعة</option>
              <option value="APPROVED">معتمدة</option>
              <option value="RETURNED_FOR_CORRECTION">معادة للتصحيح</option>
              <option value="COMPLETE">مكتملة</option>
              <option value="HAS_ERRORS">تحوي أخطاء</option>
            </select>
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">مدخل البيانات</label>
            <select
              value={createdBy}
              onChange={(e) => setCreatedBy(e.target.value)}
              className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-1.5 text-xs text-slate-200 focus:border-teal-500 focus:outline-none"
            >
              <option value="">كافة المدخلين</option>
              {users.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.fullName || u.username}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">أساس التاريخ</label>
            <select
              value={dateBasis}
              onChange={(e) => setDateBasis(e.target.value as 'ArrivalDate' | 'WritingDate')}
              className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-1.5 text-xs text-slate-200 focus:border-teal-500 focus:outline-none"
            >
              <option value="ArrivalDate">تاريخ الورود (افتراضي)</option>
              <option value="WritingDate">تاريخ التحرير (الداشبورد)</option>
            </select>
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">من تاريخ</label>
            <input
              type="date"
              value={from}
              onChange={(e) => setFrom(e.target.value)}
              className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-1.5 text-xs text-slate-200 focus:border-teal-500 focus:outline-none"
            />
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">إلى تاريخ</label>
            <input
              type="date"
              value={to}
              onChange={(e) => setTo(e.target.value)}
              className="w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-1.5 text-xs text-slate-200 focus:border-teal-500 focus:outline-none"
            />
          </div>
        </div>

        {/* Advanced Filter Toggles */}
        <div className="flex flex-wrap items-center gap-5 pt-2 border-t border-slate-800/80 text-xs">
          <label className="inline-flex items-center gap-2 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={unlinkedOnly}
              onChange={(e) => setUnlinkedOnly(e.target.checked)}
              className="rounded border-slate-700 bg-slate-800 text-teal-600 focus:ring-teal-500 cursor-pointer"
            />
            <span className="font-semibold text-teal-300">🔗 مذكرات الإدخال غير المربوطة فقط</span>
          </label>

          <label className="inline-flex items-center gap-2 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={missingWritingDateOnly}
              onChange={(e) => setMissingWritingDateOnly(e.target.checked)}
              className="rounded border-slate-700 bg-slate-800 text-teal-600 focus:ring-teal-500 cursor-pointer"
            />
            <span className="font-semibold text-amber-300">⚠️ بدون تاريخ تحرير فقط</span>
          </label>

          <label className="inline-flex items-center gap-2 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={lootOnly}
              onChange={(e) => setLootOnly(e.target.checked)}
              className="rounded border-slate-700 bg-slate-800 text-emerald-500 focus:ring-emerald-400 cursor-pointer"
            />
            <span className="font-semibold text-emerald-300">🛡️ غنائم ومستوردات فقط</span>
          </label>

          <label className="inline-flex items-center gap-2 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={invoiceOnly}
              onChange={(e) => setInvoiceOnly(e.target.checked)}
              className="rounded border-slate-700 bg-slate-800 text-sky-500 focus:ring-sky-400 cursor-pointer"
            />
            <span className="font-semibold text-sky-300">🧾 فواتير خروج فقط</span>
          </label>
        </div>

        {/* Quick Month Filter Bar */}
        <div className="flex flex-wrap items-center gap-1.5 pt-1 text-xs">
          <span className="text-slate-400 ml-1 text-[11px]">اختصارات سريعة للأشهر:</span>
          {[1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12].map((m) => (
            <button
              key={m}
              type="button"
              onClick={() => handleQuickMonth(m)}
              className="px-2 py-0.5 rounded text-[11px] bg-slate-800 hover:bg-teal-600 hover:text-white text-slate-300 border border-slate-700 transition-colors"
            >
              شهر {m}
            </button>
          ))}
        </div>

        <div className="flex items-center justify-between pt-2">
          <span className="text-xs text-slate-400">إجمالي النتائج: <strong className="text-slate-200 font-bold">{totalCount}</strong> مذكرة</span>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={handleReset}
              className="rounded-lg border border-slate-700 px-3 py-1.5 text-xs text-slate-300 hover:bg-slate-800 cursor-pointer"
            >
              إعادة ضبط
            </button>
            <button
              type="submit"
              className="rounded-lg bg-slate-700 hover:bg-slate-600 px-4 py-1.5 text-xs font-semibold text-slate-100 shadow-sm cursor-pointer"
            >
              بحث وتصفية
            </button>
          </div>
        </div>
      </form>

      {/* Table Section */}
      <div className="rounded-2xl border border-slate-800 bg-slate-900/60 overflow-hidden shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full text-right text-xs">
            <thead className="border-b border-slate-800 bg-slate-800/40 text-slate-400 font-semibold">
              <tr>
                <th className="px-4 py-3">رقم المذكرة</th>
                <th className="px-4 py-3">النوع</th>
                <th className="px-4 py-3">الشركة / المعمل</th>
                <th className="px-4 py-3">تحرير / ورود</th>
                <th className="px-4 py-3">المؤشرات (دقة %)</th>
                <th className="px-4 py-3">الحالة</th>
                <th className="px-4 py-3">المرفق</th>
                <th className="px-4 py-3 text-center">الإجراءات</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/60 text-slate-300">
              {loading ? (
                <tr>
                  <td colSpan={8} className="px-4 py-12 text-center text-slate-400">
                    جارٍ تحميل البيانات...
                  </td>
                </tr>
              ) : notes.length === 0 ? (
                <tr>
                  <td colSpan={8} className="px-4 py-12 text-center text-slate-500">
                    لا توجد مذكرات مطابقة لشروط البحث الحالية.
                  </td>
                </tr>
              ) : (
                notes.map((n) => (
                  <tr key={n.noteId} className="hover:bg-slate-800/40 transition-colors">
                    <td className="px-4 py-3 font-semibold text-slate-100">
                      <div className="flex items-center gap-1.5 flex-wrap">
                        <button
                          type="button"
                          onClick={() => handleOpenView(n.noteId)}
                          className="text-teal-400 hover:text-teal-300 hover:underline font-bold"
                          title="عرض تفاصيل المذكرة"
                        >
                          {n.noteNumber}
                        </button>
                        {n.isLoot && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/20 text-emerald-300 border border-emerald-500/30">
                            غنائم
                          </span>
                        )}
                        {n.isInvoice && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold bg-sky-500/20 text-sky-300 border border-sky-500/30">
                            فاتورة
                          </span>
                        )}
                        {(n.linkCount ?? 0) > 0 && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold bg-teal-500/15 text-teal-300 border border-teal-500/30" title={`مرتبطة مع ${n.linkCount} مذكرات`}>
                            🔗 {n.linkCount}
                          </span>
                        )}
                      </div>
                    </td>
                    <td className="px-4 py-3 text-slate-300">{n.noteTypeName}</td>
                    <td className="px-4 py-3">
                      <div>{n.companyName}</div>
                      {n.factoryName && <div className="text-[11px] text-slate-400">{n.factoryName}</div>}
                    </td>
                    <td className="px-4 py-3">
                      <div>ورود: {n.arrivalAt}</div>
                      {n.writingDate ? (
                        <div className="text-[11px] text-slate-400">تحرير: {n.writingDate}</div>
                      ) : (
                        <span className="inline-block mt-0.5 px-1.5 py-0.5 rounded text-[10px] font-semibold bg-amber-500/15 text-amber-300 border border-amber-500/30">
                          بدون تاريخ تحرير
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1.5">
                        <span className={`font-semibold ${n.accuracyPercentage >= 90 ? 'text-emerald-400' : n.accuracyPercentage >= 70 ? 'text-amber-400' : 'text-rose-400'}`}>
                          %{n.accuracyPercentage}
                        </span>
                        <span className="text-[10px] text-slate-500">({n.activeCount}/{n.totalFields})</span>
                      </div>
                    </td>
                    <td className="px-4 py-3">{getStatusBadge(n.status)}</td>
                    <td className="px-4 py-3">
                      {n.imagePath ? (
                        <button
                          type="button"
                          onClick={() => handleOpenView(n.noteId)}
                          className="inline-flex items-center gap-1 text-teal-400 hover:text-teal-300 text-[11px] font-medium cursor-pointer"
                          title="معاينة المرفق مباشرة في الموقع"
                        >
                          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.172 7l-6.586 6.586a2 2 0 102.828 2.828l6.414-6.586a4 4 0 00-5.656-5.656l-6.415 6.585a6 6 0 108.486 8.486L20.5 13" />
                          </svg>
                          معاينة
                        </button>
                      ) : (
                        <span className="text-slate-600">-</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-center">
                      <div className="inline-flex items-center gap-1">
                        {/* View button */}
                        <button
                          type="button"
                          onClick={() => handleOpenView(n.noteId)}
                          className="rounded-lg p-1.5 text-teal-400 hover:bg-teal-500/10 hover:text-teal-300"
                          title="عرض تفاصيل المذكرة"
                        >
                          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                          </svg>
                        </button>

                        {/* Links button */}
                        <button
                          type="button"
                          onClick={() => handleOpenLinks(n.noteId, n.noteNumber)}
                          className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-700 hover:text-slate-200"
                          title="المذكرات المرتبطة"
                        >
                          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
                          </svg>
                        </button>

                        {/* Note Source button for ENTRY notes */}
                        {(n.noteTypeName?.includes('إدخال') || n.noteTypeName?.toLowerCase().includes('entry')) && (
                          <button
                            type="button"
                            onClick={() => handleOpenSource(n.noteId, n.noteNumber)}
                            className="rounded-lg p-1.5 text-sky-400 hover:bg-slate-700 hover:text-sky-300"
                            title="مصدر مذكرة الإدخال (فواتير/إخراج/غنائم)"
                          >
                            <span className="text-xs">📦</span>
                          </button>
                        )}

                        {/* Edit button */}
                        {canEdit && (
                          <button
                            type="button"
                            onClick={() => handleOpenEdit(n.noteId)}
                            className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-700 hover:text-slate-200"
                            title="تعديل المذكرة"
                          >
                            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                            </svg>
                          </button>
                        )}

                        {/* Delete button (Strictly ADMIN only) */}
                        {canDelete && (
                          <button
                            type="button"
                            onClick={() => handleDelete(n)}
                            className="rounded-lg p-1.5 text-rose-400 hover:bg-rose-500/10 hover:text-rose-300"
                            title="حذف المذكرة (مدير النظام)"
                          >
                            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                            </svg>
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination bar */}
        {totalPages > 1 && (
          <div className="flex items-center justify-between border-t border-slate-800 px-4 py-3 text-xs text-slate-400">
            <div>
              الصفحة <strong className="text-slate-200">{pageNumber}</strong> من <strong className="text-slate-200">{totalPages}</strong>
            </div>
            <div className="flex items-center gap-2">
              <button
                type="button"
                disabled={pageNumber <= 1}
                onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
                className="rounded-lg border border-slate-700 px-3 py-1.5 hover:bg-slate-800 disabled:opacity-30 cursor-pointer"
              >
                السابقة
              </button>
              <button
                type="button"
                disabled={pageNumber >= totalPages}
                onClick={() => setPageNumber((p) => Math.min(totalPages, p + 1))}
                className="rounded-lg border border-slate-700 px-3 py-1.5 hover:bg-slate-800 disabled:opacity-30 cursor-pointer"
              >
                التالية
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Memo Editor Modal */}
      <MemoEditorModal
        isOpen={isEditorOpen}
        onClose={() => setIsEditorOpen(false)}
        noteId={selectedNoteId}
        onSaved={fetchNotes}
      />

      {/* Memo View Modal */}
      <MemoViewModal
        isOpen={isViewOpen}
        onClose={() => {
          setIsViewOpen(false);
          setViewNoteId(null);
        }}
        noteId={viewNoteId}
        onEditNote={(id) => handleOpenEdit(id)}
        onStatusChanged={fetchNotes}
      />

      {/* Memo Links Modal */}
      {linksNote && (
        <MemoLinksModal
          isOpen={isLinksOpen}
          onClose={() => {
            setIsLinksOpen(false);
            setLinksNote(null);
          }}
          noteId={linksNote.id}
          noteNumber={linksNote.number}
          allowChanges={canEdit}
          onViewNote={(id) => {
            setIsLinksOpen(false);
            handleOpenView(id);
          }}
          onLinksChanged={fetchNotes}
        />
      )}

      {/* Note Source Modal */}
      {sourceNote && (
        <NoteSourceModal
          isOpen={isSourceOpen}
          onClose={() => {
            setIsSourceOpen(false);
            setSourceNote(null);
          }}
          noteId={sourceNote.id}
          noteNumber={sourceNote.number}
          readOnly={!canEdit}
          onOpenLinksModal={() => {
            setIsSourceOpen(false);
            handleOpenLinks(sourceNote.id, sourceNote.number);
          }}
          onSourceChanged={fetchNotes}
        />
      )}
    </div>
  );
};

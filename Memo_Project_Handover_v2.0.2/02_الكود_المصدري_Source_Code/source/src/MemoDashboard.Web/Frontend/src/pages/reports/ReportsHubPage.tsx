import React, { useEffect, useState, useCallback } from 'react';
import { api, buildCompanyReportUrl, downloadCompanyReport, type CompanyReportIssueParams } from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import type { ReportMeta, CompanyModel, NoteTypeModel, CompanyReportDataModel } from '../../types/memo';
import { CompanyDetailedDocument } from '../../components/reports/CompanyDetailedDocument';
import { ReportIdentitySettingsModal } from '../../components/reports/ReportIdentitySettingsModal';

interface ReportColumn {
  key: string;
  name: string;
  caption: string;
  type: string;
}

export const ReportsHubPage: React.FC = () => {
  const { isAdmin } = useAuth();

  const [catalog, setCatalog] = useState<ReportMeta[]>([]);
  const [selectedReportKey, setSelectedReportKey] = useState<string>('CompanyDetailed');
  const [companies, setCompanies] = useState<CompanyModel[]>([]);
  const [noteTypes, setNoteTypes] = useState<NoteTypeModel[]>([]);

  // Filter state
  const [companyId, setCompanyId] = useState('');
  const [noteTypeId, setNoteTypeId] = useState('');
  const [from, setFrom] = useState(() => {
    const d = new Date();
    d.setMonth(d.getMonth() - 6);
    return d.toISOString().slice(0, 10);
  });
  const [to, setTo] = useState(() => new Date().toISOString().slice(0, 10));
  const [dateBasis, setDateBasis] = useState<'WritingDate' | 'ArrivalDate'>('WritingDate');
  const [entryCutoffDate, setEntryCutoffDate] = useState('');

  // Presentation / Issue params (for CompanyDetailed)
  const [showIssueControls, setShowIssueControls] = useState(false);
  const [issueTitle, setIssueTitle] = useState('التقرير التفصيلي لأداء الشركة');
  const [letterNumber, setLetterNumber] = useState('');
  const [city, setCity] = useState('');
  const [hijriDate, setHijriDate] = useState('');
  const [gregorianDate, setGregorianDate] = useState('');
  const [recipient, setRecipient] = useState('');
  const [issuerName, setIssuerName] = useState('');
  const [formEfficiency, setFormEfficiency] = useState('100');

  // Report Data State
  const [companyReportData, setCompanyReportData] = useState<CompanyReportDataModel | null>(null);
  const [tabularTitle, setTabularTitle] = useState('');
  const [columns, setColumns] = useState<ReportColumn[]>([]);
  const [rows, setRows] = useState<Record<string, unknown>[]>([]);
  const [loadingData, setLoadingData] = useState(false);
  const [exporting, setExporting] = useState<'excel' | 'pdf' | 'docx' | null>(null);

  // Settings Modal
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);

  useEffect(() => {
    api.getReportsCatalog().then((list) => {
      setCatalog(list);
      if (list.length > 0 && !selectedReportKey) {
        setSelectedReportKey(list[0].key);
      }
    }).catch(console.error);

    api.getCompanies().then((list) => {
      setCompanies(list);
      if (list.length > 0 && !companyId) {
        setCompanyId(list[0].id);
      }
    }).catch(console.error);

    api.getNoteTypes().then(setNoteTypes).catch(console.error);
  }, []);

  const getFilterPayload = useCallback(() => {
    const p: Record<string, unknown> = {};
    if (from) p.from = from;
    if (to) p.to = to;
    if (companyId) p.companyId = companyId;
    if (noteTypeId) p.noteTypeId = noteTypeId;
    if (dateBasis) p.dateBasis = dateBasis;
    if (entryCutoffDate) p.entryCutoffDate = entryCutoffDate;
    return p;
  }, [from, to, companyId, noteTypeId, dateBasis, entryCutoffDate]);

  // Immediate local sync for presentation fields without triggering heavy SQL backend queries
  useEffect(() => {
    setCompanyReportData((prev) => {
      if (!prev) return prev;
      return {
        ...prev,
        issue: {
          ...prev.issue,
          title: issueTitle,
          letterNumber,
          city: city || prev.identity?.defaultCity || '',
          hijriDate,
          gregorianDate,
          recipient,
          issuerName,
          formEfficiencyIndex: Number(formEfficiency) || 100,
        },
      };
    });
  }, [issueTitle, letterNumber, city, hijriDate, gregorianDate, recipient, issuerName, formEfficiency]);

  const loadReportData = useCallback(async () => {
    if (!selectedReportKey) return;
    setLoadingData(true);

    if (selectedReportKey === 'CompanyDetailed') {
      if (!companyId) {
        setCompanyReportData(null);
        setLoadingData(false);
        return;
      }

      const params: Record<string, string> = { companyId };
      if (from) params.from = from;
      if (to) params.to = to;
      if (dateBasis) params.dateBasis = dateBasis;
      if (entryCutoffDate) params.entryCutoffDate = entryCutoffDate;
      if (issueTitle) params.title = issueTitle;
      if (letterNumber) params.letterNumber = letterNumber;
      if (city) params.city = city;
      if (hijriDate) params.hijriDate = hijriDate;
      if (gregorianDate) params.gregorianDate = gregorianDate;
      if (recipient) params.recipient = recipient;
      if (issuerName) params.issuerName = issuerName;
      if (formEfficiency) params.formEfficiency = formEfficiency;

      try {
        const data = await api.getCompanyReportData(params);
        setCompanyReportData(data);
      } catch (err: unknown) {
        console.error('Failed to load company report data', err);
        setCompanyReportData(null);
      } finally {
        setLoadingData(false);
      }
    } else {
      // Tabular report (MemoLogExport, Duplicates)
      try {
        const data = await api.getReportData(selectedReportKey, getFilterPayload());
        setTabularTitle(data.title);
        setColumns(data.columns);
        setRows(data.rows);
      } catch (err: unknown) {
        console.error('Failed to load tabular report data', err);
        alert(err instanceof Error ? err.message : 'فشل تحميل بيانات التقرير.');
      } finally {
        setLoadingData(false);
      }
    }
  }, [selectedReportKey, companyId, from, to, dateBasis, entryCutoffDate, getFilterPayload]);

  useEffect(() => {
    if (!selectedReportKey) return;
    const timer = setTimeout(() => {
      loadReportData();
    }, 200);
    return () => clearTimeout(timer);
  }, [selectedReportKey, companyId, from, to, dateBasis, entryCutoffDate, loadReportData]);

  const getDashboardFilters = (): import('../../types/dashboard').DashboardFilters => ({
    companyId,
    from,
    to,
    dateBasis,
    entryCutoffDate,
    factoryId: '',
    productionLineId: '',
    noteTypeId: '',
    status: '',
  });

  const handleExportExcel = async () => {
    if (!selectedReportKey) return;
    setExporting('excel');
    try {
      if (selectedReportKey === 'CompanyDetailed') {
        const dashboardFilters = getDashboardFilters();
        const issueParams: CompanyReportIssueParams = {
          title: issueTitle,
          letterNumber,
          city,
          hijriDate,
          gregorianDate,
          recipient,
          issuerName,
          formEfficiency,
        };
        await downloadCompanyReport('xlsx', dashboardFilters, issueParams);
      } else {
        await api.exportReportExcel(selectedReportKey, getFilterPayload());
      }
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل تصدير ملف Excel.');
    } finally {
      setExporting(null);
    }
  };

  const handleExportPdf = async () => {
    if (!selectedReportKey) return;
    setExporting('pdf');
    try {
      if (selectedReportKey === 'CompanyDetailed') {
        const dashboardFilters = getDashboardFilters();
        const issueParams: CompanyReportIssueParams = {
          title: issueTitle,
          letterNumber,
          city,
          hijriDate,
          gregorianDate,
          recipient,
          issuerName,
          formEfficiency,
        };
        await downloadCompanyReport('pdf', dashboardFilters, issueParams);
      } else {
        await api.exportReportPdf(selectedReportKey, getFilterPayload());
      }
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل تصدير ملف PDF.');
    } finally {
      setExporting(null);
    }
  };

  const handleExportDocx = async () => {
    if (!selectedReportKey) return;
    setExporting('docx');
    try {
      if (selectedReportKey === 'CompanyDetailed') {
        const dashboardFilters = getDashboardFilters();
        const issueParams: CompanyReportIssueParams = {
          title: issueTitle,
          letterNumber,
          city,
          hijriDate,
          gregorianDate,
          recipient,
          issuerName,
          formEfficiency,
        };
        await downloadCompanyReport('docx', dashboardFilters, issueParams);
      }
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل تصدير ملف Word.');
    } finally {
      setExporting(null);
    }
  };

  const handleOpenPrintPreview = () => {
    if (!selectedReportKey) return;
    if (selectedReportKey === 'CompanyDetailed') {
      const dashboardFilters = getDashboardFilters();
      const issueParams: CompanyReportIssueParams = {
        title: issueTitle,
        letterNumber,
        city,
        hijriDate,
        gregorianDate,
        recipient,
        issuerName,
        formEfficiency,
      };
      window.open(buildCompanyReportUrl('html', dashboardFilters, issueParams), '_blank');
    } else {
      const p = new URLSearchParams();
      if (from) p.set('from', from);
      if (to) p.set('to', to);
      if (companyId) p.set('companyId', companyId);
      if (noteTypeId) p.set('noteTypeId', noteTypeId);
      if (dateBasis) p.set('dateBasis', dateBasis);
      if (entryCutoffDate) p.set('entryCutoffDate', entryCutoffDate);
      window.open(`/api/reports/${selectedReportKey}/export/html?${p.toString()}`, '_blank');
    }
  };

  const currentReport = catalog.find((r) => r.key === selectedReportKey);

  return (
    <div className="flex flex-col gap-6 p-6 print:p-0 print:m-0 print:gap-0 min-h-screen print:min-h-0 app-bg print:bg-white" dir="rtl">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-4 border-b app-border pb-4 print:hidden">
        <div>
          <h1 className="text-2xl font-bold app-text flex items-center gap-3">
            <span>📑</span>
            <span>مركز التقارير الرسمي الموحد</span>
          </h1>
          <p className="text-sm app-text-muted mt-1">
            التقارير المعتمدة والمطابقة للتطبيق المكتبي: التقرير التفصيلي للشركة، تصدير سجل المذكرات الشامل (Excel)، وتقرير المذكرات المكررة.
          </p>
        </div>
        <div className="flex items-center gap-3">
          {isAdmin && (
            <button
              type="button"
              onClick={() => setIsSettingsOpen(true)}
              className="flex items-center gap-2 px-3.5 py-2 text-xs font-semibold rounded-lg border border-amber-500/40 bg-amber-50 dark:bg-amber-950/30 text-amber-800 dark:text-amber-300 hover:bg-amber-100 transition-colors shadow-xs"
            >
              <span>⚙️</span>
              <span>إعدادات قالب التقرير والهوية</span>
            </button>
          )}
          <a
            href="?report=company"
            className="flex items-center gap-2 px-3.5 py-2 text-xs font-semibold rounded-lg bg-blue-50 dark:bg-blue-950/40 text-blue-700 dark:text-blue-300 border border-blue-200 dark:border-blue-800 hover:bg-blue-100 transition-colors"
          >
            <span>🏢</span>
            <span>عرض في لوحة الشركة</span>
          </a>
        </div>
      </div>

      {/* 3 Core Reports Cards / Selector */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 print:hidden">
        {catalog.map((rep) => {
          const isSelected = selectedReportKey === rep.key;
          const icon =
            rep.key === 'CompanyDetailed'
              ? '🏢'
              : rep.key === 'MemoLogExport'
              ? '📑'
              : '⚠️';

          return (
            <button
              key={rep.key}
              type="button"
              onClick={() => setSelectedReportKey(rep.key)}
              className={`p-4 rounded-xl border text-right transition-all flex flex-col justify-between gap-2 relative overflow-hidden ${
                isSelected
                  ? 'border-blue-500 bg-blue-50/70 dark:bg-blue-950/40 shadow-sm ring-2 ring-blue-500/30'
                  : 'app-surface border-slate-200 dark:border-slate-800 hover:border-slate-300 app-text'
              }`}
            >
              <div className="flex items-start justify-between gap-3 w-full">
                <span className="text-2xl">{icon}</span>
                <span
                  className={`text-[11px] font-bold px-2 py-0.5 rounded-full ${
                    isSelected
                      ? 'bg-blue-600 text-white'
                      : 'bg-slate-100 dark:bg-slate-800 app-text-muted'
                  }`}
                >
                  {rep.category}
                </span>
              </div>
              <div className="mt-1">
                <h3 className={`text-sm font-bold ${isSelected ? 'text-blue-700 dark:text-blue-300' : 'app-text'}`}>
                  {rep.title}
                </h3>
                <p className="text-xs app-text-muted mt-1 line-clamp-2">
                  {rep.description}
                </p>
              </div>
            </button>
          );
        })}
      </div>

      {/* Main Report Area */}
      <div className="flex flex-col gap-4 print:gap-0 print:p-0 print:m-0">
        {/* Filters & Actions Bar */}
        <div className="app-surface rounded-xl border app-border p-4 shadow-sm space-y-4 print:hidden">
          <div className="flex flex-wrap items-center justify-between gap-4 border-b app-border pb-3">
            <div>
              <h2 className="text-lg font-bold app-text flex items-center gap-2">
                <span>{currentReport?.title}</span>
              </h2>
              <p className="text-xs app-text-muted mt-0.5">{currentReport?.description}</p>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={() => window.print()}
                disabled={loadingData}
                className="px-3 py-1.5 text-xs font-semibold rounded-lg bg-amber-600 text-white hover:bg-amber-700 transition-colors flex items-center gap-1.5 disabled:opacity-50"
              >
                <span>🖨️</span>
                <span>طباعة رسمية</span>
              </button>
              <button
                type="button"
                onClick={handleExportExcel}
                disabled={exporting !== null || loadingData}
                className="px-3.5 py-1.5 text-xs font-semibold rounded-lg bg-emerald-600 text-white hover:bg-emerald-700 transition-colors flex items-center gap-1.5 disabled:opacity-50"
              >
                <span>📊</span>
                <span>{exporting === 'excel' ? 'جارٍ التصدير...' : 'تصدير Excel'}</span>
              </button>
              <button
                type="button"
                onClick={handleExportPdf}
                disabled={exporting !== null || loadingData}
                className="px-3.5 py-1.5 text-xs font-semibold rounded-lg bg-rose-600 text-white hover:bg-rose-700 transition-colors flex items-center gap-1.5 disabled:opacity-50"
              >
                <span>📄</span>
                <span>{exporting === 'pdf' ? 'جارٍ التصدير...' : 'تصدير PDF'}</span>
              </button>
              {selectedReportKey === 'CompanyDetailed' && (
                <button
                  type="button"
                  onClick={handleExportDocx}
                  disabled={exporting !== null || loadingData}
                  className="px-3.5 py-1.5 text-xs font-semibold rounded-lg bg-blue-600 text-white hover:bg-blue-700 transition-colors flex items-center gap-1.5 disabled:opacity-50"
                >
                  <span>📝</span>
                  <span>{exporting === 'docx' ? 'جارٍ التصدير...' : 'تصدير Word'}</span>
                </button>
              )}
              <button
                type="button"
                onClick={handleOpenPrintPreview}
                disabled={loadingData}
                className="px-3 py-1.5 text-xs font-semibold rounded-lg border app-border app-surface-muted app-hover app-text transition-colors flex items-center gap-1.5"
              >
                <span>🌐</span>
                <span>معاينة مستقلة</span>
              </button>
            </div>
          </div>

          {/* Filters grid */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 text-xs">
            {/* Company Filter */}
            <div>
              <label className="block font-semibold app-text-muted mb-1">
                الشركة {selectedReportKey === 'CompanyDetailed' ? '(مطلوبة)' : ''}
              </label>
              <select
                value={companyId}
                onChange={(e) => setCompanyId(e.target.value)}
                className="w-full px-2.5 py-1.5 rounded-lg border app-border app-surface app-text focus:ring-1 focus:ring-blue-500"
              >
                {selectedReportKey !== 'CompanyDetailed' && <option value="">جميع الشركات</option>}
                {companies.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </div>

            {/* Note Type Filter (for MemoLogExport) */}
            {selectedReportKey === 'MemoLogExport' && (
              <div>
                <label className="block font-semibold app-text-muted mb-1">نوع المذكرة</label>
                <select
                  value={noteTypeId}
                  onChange={(e) => setNoteTypeId(e.target.value)}
                  className="w-full px-2.5 py-1.5 rounded-lg border app-border app-surface app-text focus:ring-1 focus:ring-blue-500"
                >
                  <option value="">جميع الأنواع</option>
                  {noteTypes.map((nt) => (
                    <option key={nt.id} value={nt.id}>
                      {nt.nameAr}
                    </option>
                  ))}
                </select>
              </div>
            )}

            {/* Date Basis */}
            <div>
              <label className="block font-semibold app-text-muted mb-1">أساس التاريخ</label>
              <select
                value={dateBasis}
                onChange={(e) => setDateBasis(e.target.value as 'WritingDate' | 'ArrivalDate')}
                className="w-full px-2.5 py-1.5 rounded-lg border app-border app-surface app-text focus:ring-1 focus:ring-blue-500"
              >
                <option value="WritingDate">تاريخ الكتابة</option>
                <option value="ArrivalDate">تاريخ الوصول</option>
              </select>
            </div>

            {/* From Date */}
            <div>
              <label className="block font-semibold app-text-muted mb-1">من تاريخ</label>
              <input
                type="date"
                value={from}
                onChange={(e) => setFrom(e.target.value)}
                className="w-full px-2.5 py-1.5 rounded-lg border app-border app-surface app-text focus:ring-1 focus:ring-blue-500"
              />
            </div>

            {/* To Date */}
            <div>
              <label className="block font-semibold app-text-muted mb-1">إلى تاريخ</label>
              <input
                type="date"
                value={to}
                onChange={(e) => setTo(e.target.value)}
                className="w-full px-2.5 py-1.5 rounded-lg border app-border app-surface app-text focus:ring-1 focus:ring-blue-500"
              />
            </div>

            {/* Entry Cutoff Date */}
            <div>
              <label className="block font-semibold app-text-muted mb-1">تاريخ قطع الإدخال (اختياري)</label>
              <input
                type="date"
                value={entryCutoffDate}
                onChange={(e) => setEntryCutoffDate(e.target.value)}
                className="w-full px-2.5 py-1.5 rounded-lg border app-border app-surface app-text focus:ring-1 focus:ring-blue-500"
              />
            </div>
          </div>

          {/* Presentation & Issue Metadata Controls for CompanyDetailed */}
          {selectedReportKey === 'CompanyDetailed' && (
            <div className="border-t app-border pt-3">
              <button
                type="button"
                onClick={() => setShowIssueControls(!showIssueControls)}
                className="text-xs font-semibold text-blue-600 dark:text-blue-400 hover:underline flex items-center gap-1.5"
              >
                <span>{showIssueControls ? '▼' : '◀'}</span>
                <span>بيانات إصدار وترويسة التقرير (رقم الكتاب، التاريخ الهجري والميلادي، المستلم، كفاءة النموذج...)</span>
              </button>

              {showIssueControls && (
                <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-3 mt-3 p-3 bg-slate-50 dark:bg-slate-900/60 rounded-xl border app-border text-xs">
                  <div>
                    <label className="block font-medium app-text-muted mb-1">عنوان التقرير</label>
                    <input
                      type="text"
                      value={issueTitle}
                      onChange={(e) => setIssueTitle(e.target.value)}
                      className="w-full px-2.5 py-1 rounded border app-border app-surface app-text"
                    />
                  </div>
                  <div>
                    <label className="block font-medium app-text-muted mb-1">رقم الكتاب</label>
                    <input
                      type="text"
                      value={letterNumber}
                      onChange={(e) => setLetterNumber(e.target.value)}
                      className="w-full px-2.5 py-1 rounded border app-border app-surface app-text"
                      placeholder="مثال: 142/ص"
                    />
                  </div>
                  <div>
                    <label className="block font-medium app-text-muted mb-1">المدينة</label>
                    <input
                      type="text"
                      value={city}
                      onChange={(e) => setCity(e.target.value)}
                      className="w-full px-2.5 py-1 rounded border app-border app-surface app-text"
                      placeholder="دمشق"
                    />
                  </div>
                  <div>
                    <label className="block font-medium app-text-muted mb-1">مؤشر كفاءة النموذج (%)</label>
                    <input
                      type="text"
                      value={formEfficiency}
                      onChange={(e) => setFormEfficiency(e.target.value)}
                      className="w-full px-2.5 py-1 rounded border app-border app-surface app-text"
                      placeholder="100"
                    />
                  </div>
                  <div>
                    <label className="block font-medium app-text-muted mb-1">التاريخ الهجري</label>
                    <input
                      type="text"
                      value={hijriDate}
                      onChange={(e) => setHijriDate(e.target.value)}
                      className="w-full px-2.5 py-1 rounded border app-border app-surface app-text"
                      placeholder="مثال: 15 رمضان 1447"
                    />
                  </div>
                  <div>
                    <label className="block font-medium app-text-muted mb-1">التاريخ الميلادي</label>
                    <input
                      type="text"
                      value={gregorianDate}
                      onChange={(e) => setGregorianDate(e.target.value)}
                      className="w-full px-2.5 py-1 rounded border app-border app-surface app-text"
                      placeholder="مثال: 10/09/2026"
                    />
                  </div>
                  <div>
                    <label className="block font-medium app-text-muted mb-1">الجهة المستلمة</label>
                    <input
                      type="text"
                      value={recipient}
                      onChange={(e) => setRecipient(e.target.value)}
                      className="w-full px-2.5 py-1 rounded border app-border app-surface app-text"
                    />
                  </div>
                  <div>
                    <label className="block font-medium app-text-muted mb-1">اسم المسؤول المصدر</label>
                    <input
                      type="text"
                      value={issuerName}
                      onChange={(e) => setIssuerName(e.target.value)}
                      className="w-full px-2.5 py-1 rounded border app-border app-surface app-text"
                    />
                  </div>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Report Content View */}
        {selectedReportKey === 'CompanyDetailed' ? (
          <div>
            {!companyId ? (
              <div className="app-surface rounded-2xl border app-border p-12 text-center app-text-muted">
                <span className="text-4xl block mb-2">🏢</span>
                <p className="font-bold">يرجى اختيار شركة من القائمة المنسدلة أعلاه لعرض التقرير التفصيلي الرسمي.</p>
              </div>
            ) : companyReportData ? (
              <CompanyDetailedDocument data={companyReportData} loading={loadingData} />
            ) : loadingData ? (
              <div className="max-w-6xl mx-auto p-12 text-center app-surface rounded-2xl border app-border">
                <div className="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-[#B9A77A]"></div>
                <p className="mt-3 text-sm font-semibold app-text-muted">جارٍ استخراج وتوليد التقرير التفصيلي للشركة...</p>
              </div>
            ) : (
              <div className="app-surface rounded-2xl border app-border p-12 text-center app-text-muted">
                تعذر تحميل بيانات التقرير للشركة المحددة.
              </div>
            )}
          </div>
        ) : (
          /* Tabular Preview View for MemoLogExport and Duplicates */
          <div className="app-surface rounded-xl border app-border overflow-hidden shadow-sm flex flex-col min-h-[450px]">
            <div className="flex items-center justify-between p-3 border-b app-border bg-slate-50/60 dark:bg-slate-900/60 text-xs">
              <span className="font-semibold app-text">
                معاينة بيانات {tabularTitle || currentReport?.title} ({rows.length} سجل)
              </span>
              <span className="text-slate-400">
                {loadingData ? 'جارٍ معالجة التقرير...' : 'مُحدّث'}
              </span>
            </div>

            <div className="overflow-x-auto flex-1">
              <table className="w-full text-right text-xs border-collapse">
                <thead>
                  <tr className="border-b app-border bg-slate-50 dark:bg-slate-900/50 font-semibold app-text-muted">
                    <th className="p-2.5 w-10 text-center">#</th>
                    {columns.map((col) => (
                      <th key={col.key} className="p-2.5 whitespace-nowrap">
                        {col.caption || col.name}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y app-border">
                  {loadingData ? (
                    <tr>
                      <td colSpan={Math.max(columns.length + 1, 1)} className="p-12 text-center app-text-muted">
                        جارٍ استخراج وتوليد بيانات التقرير...
                      </td>
                    </tr>
                  ) : rows.length === 0 ? (
                    <tr>
                      <td colSpan={Math.max(columns.length + 1, 1)} className="p-12 text-center app-text-muted">
                        لا توجد بيانات مطابقة لخيارات التصفية المحددة.
                      </td>
                    </tr>
                  ) : (
                    rows.map((row, rIdx) => (
                      <tr key={rIdx} className="hover:bg-slate-50 dark:hover:bg-slate-800/40">
                        <td className="p-2.5 text-center font-mono text-slate-400">{rIdx + 1}</td>
                        {columns.map((col) => {
                          const val = row[col.key];
                          const strVal = val === null || val === undefined ? '—' : String(val);
                          return (
                            <td key={col.key} className="p-2.5 whitespace-nowrap font-medium app-text">
                              {strVal}
                            </td>
                          );
                        })}
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

      {/* Admin Settings Modal */}
      <ReportIdentitySettingsModal
        isOpen={isSettingsOpen}
        onClose={() => setIsSettingsOpen(false)}
        onSaved={() => {
          if (selectedReportKey === 'CompanyDetailed') {
            loadReportData();
          }
        }}
      />
    </div>
  );
};
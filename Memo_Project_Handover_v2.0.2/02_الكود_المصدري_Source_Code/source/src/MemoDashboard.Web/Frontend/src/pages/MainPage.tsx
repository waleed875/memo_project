import React, { useEffect, useState, useCallback } from 'react';
import { api } from '../services/api';
import { useAuth } from '../context/AuthContext';
import SyrianLogo from '../assets/Syrian_logo_icon_gold.svg';
import { normalizeNoteStatus, type NoteDetailsModel } from '../types/memo';
import type { ActiveNavView } from '../components/layout/AppLayout';
import { MemoEditorModal } from './memos/MemoEditorModal';

interface MainPageProps {
  onNavigate: (view: ActiveNavView | 'analytics') => void;
}

export const MainPage: React.FC<MainPageProps> = ({ onNavigate }) => {
  const { user, role, isAdmin, isReviewer, isDataEntry } = useAuth();
  const [loading, setLoading] = useState(true);
  const [isNewMemoOpen, setIsNewMemoOpen] = useState(false);

  // High-level pulse statistics
  const [stats, setStats] = useState({
    totalMemos: 0,
    approvedMemos: 0,
    pendingReview: 0,
    activeCompanies: 0,
    activeFactories: 0,
  });

  const [recentNotes, setRecentNotes] = useState<NoteDetailsModel[]>([]);

  const loadDashboardPulse = useCallback(async () => {
    setLoading(true);
    try {
      const [memosRes, approvedRes, reviewRes, companiesRes, factoriesRes] = await Promise.allSettled([
        api.searchNotes({ page: 1, pageSize: 5 }),
        api.searchNotes({ status: 'APPROVED', page: 1, pageSize: 1 }),
        api.getReviewQueueItems(),
        api.getCompanies(),
        api.getFactories(),
      ]);

      const recentItems = memosRes.status === 'fulfilled' ? memosRes.value.items || [] : [];
      const totalCount = memosRes.status === 'fulfilled' ? memosRes.value.totalCount || 0 : 0;
      const approvedCount = approvedRes.status === 'fulfilled' ? approvedRes.value.totalCount || 0 : 0;
      const pendingCount = reviewRes.status === 'fulfilled' ? reviewRes.value.length || 0 : 0;
      const compCount = companiesRes.status === 'fulfilled' ? companiesRes.value.filter((c) => c.isActive).length : 0;
      const factCount = factoriesRes.status === 'fulfilled' ? factoriesRes.value.filter((f) => f.isActive).length : 0;

      setStats({
        totalMemos: totalCount,
        approvedMemos: approvedCount,
        pendingReview: pendingCount,
        activeCompanies: compCount,
        activeFactories: factCount,
      });
      setRecentNotes(recentItems);
    } catch (err) {
      console.error('Failed to load main page pulse', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadDashboardPulse();
  }, [loadDashboardPulse]);

  const getRoleArabicName = (r?: string | null) => {
    switch (r) {
      case 'ADMIN':
        return 'مدير النظام';
      case 'REVIEWER':
        return 'مدقق معتمد';
      case 'DATA_ENTRY':
        return 'مدخل بيانات';
      case 'VIEWER':
        return 'مشاهد ومطلع';
      default:
        return r || 'مستخدم النظام';
    }
  };

  const getStatusBadge = (status: unknown) => {
    const normalized = normalizeNoteStatus(status);
    switch (normalized) {
      case 'APPROVED':
        return <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-100 text-emerald-800 dark:bg-emerald-950/80 dark:text-emerald-300">معتمدة</span>;
      case 'UNDER_REVIEW':
        return <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-blue-100 text-blue-800 dark:bg-blue-950/80 dark:text-blue-300">قيد التدقيق</span>;
      case 'NEEDS_REVIEW':
        return <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-amber-100 text-amber-800 dark:bg-amber-950/80 dark:text-amber-300">بانتظار التدقيق</span>;
      case 'RETURNED_FOR_CORRECTION':
        return <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-rose-100 text-rose-800 dark:bg-rose-950/80 dark:text-rose-300">معادة للتصحيح</span>;
      case 'HAS_ERRORS':
        return <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-orange-100 text-orange-800 dark:bg-orange-950/80 dark:text-orange-300">تحوي أخطاء</span>;
      case 'COMPLETE':
        return <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-teal-100 text-teal-800 dark:bg-teal-950/80 dark:text-teal-300">مكتملة</span>;
      case 'DRAFT':
        return <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300">مسودة</span>;
      default:
        return <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-slate-100 text-slate-600">غير معروف</span>;
    }
  };

  const todayStr = new Intl.DateTimeFormat('ar-SY', {
    weekday: 'long',
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  }).format(new Date());

  return (
    <div className="flex flex-col gap-6 p-6 max-w-[1600px] mx-auto min-h-screen" dir="rtl">
      {/* Welcome Hero Banner */}
      <section className="app-surface rounded-2xl border app-border p-6 shadow-sm relative overflow-hidden">
        <div className="absolute top-0 left-0 w-48 h-full bg-gradient-to-r from-amber-500/5 to-transparent pointer-events-none" />
        <div className="flex flex-wrap items-center justify-between gap-6 relative z-10">
          <div className="flex items-center gap-4 min-w-0">
            <div className="shrink-0 h-16 w-16 flex items-center justify-center p-2 rounded-2xl border border-amber-400/40 bg-gradient-to-br from-amber-500/15 via-amber-500/5 to-transparent shadow-sm">
              <img
                src={SyrianLogo}
                alt="الشعار الرسمي"
                className="h-full w-full object-contain"
                onError={(e) => { e.currentTarget.src = '/brand/Syrian_logo_icon_gold.svg'; }}
              />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold text-amber-700 dark:text-amber-400 tracking-wide">الجمهورية العربية السورية</span>
                <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-300 font-semibold">
                  الإصدار 2.0.2
                </span>
              </div>
              <h1 className="text-2xl font-bold app-text mt-1">نظام تدقيق المذكرات والرقابة والتفتيش الصناعي</h1>
              <p className="text-xs app-text-muted mt-1">
                مرحباً بك، <strong className="app-text font-bold">{user?.fullName || user?.username}</strong> ({getRoleArabicName(role)}) — اليوم هو {todayStr}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-3">
            {(isDataEntry || isAdmin) && (
              <button
                type="button"
                onClick={() => setIsNewMemoOpen(true)}
                className="btn-primary flex items-center gap-2 px-4 py-2.5 rounded-xl font-bold text-sm shadow-sm transition-all"
              >
                <span className="text-lg leading-none">+</span>
                <span>تسجيل مذكرة جديدة</span>
              </button>
            )}
            <button
              type="button"
              onClick={loadDashboardPulse}
              disabled={loading}
              title="تحديث البيانات السريعة"
              className="p-2.5 rounded-xl border app-border app-surface-muted app-hover app-text text-sm transition-colors"
            >
              🔄
            </button>
          </div>
        </div>
      </section>

      {/* Summary KPI Cards */}
      <section className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* Card 1: Total Memos */}
        <div
          onClick={() => onNavigate('memos')}
          className="app-surface rounded-2xl border app-border p-5 shadow-sm hover:shadow-md transition-shadow cursor-pointer group"
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold app-text-muted">إجمالي المذكرات المسجلة</span>
            <span className="text-xl p-2 rounded-xl bg-blue-50 dark:bg-blue-950/50 text-blue-600">📝</span>
          </div>
          <div className="mt-3 flex items-baseline gap-2">
            <span className="text-3xl font-bold app-text font-mono">
              {loading ? '—' : stats.totalMemos.toLocaleString('ar-SY')}
            </span>
            <span className="text-xs app-text-muted">مذكرة تفتيشية</span>
          </div>
          <p className="mt-2 text-[11px] text-blue-600 dark:text-blue-400 group-hover:underline">استعراض السجل الكامل ←</p>
        </div>

        {/* Card 2: Approved Memos */}
        <div
          onClick={() => onNavigate('memos')}
          className="app-surface rounded-2xl border app-border p-5 shadow-sm hover:shadow-md transition-shadow cursor-pointer group"
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold app-text-muted">المذكرات المعتمدة</span>
            <span className="text-xl p-2 rounded-xl bg-emerald-50 dark:bg-emerald-950/50 text-emerald-600">✅</span>
          </div>
          <div className="mt-3 flex items-baseline gap-2">
            <span className="text-3xl font-bold text-emerald-600 dark:text-emerald-400 font-mono">
              {loading ? '—' : stats.approvedMemos.toLocaleString('ar-SY')}
            </span>
            <span className="text-xs app-text-muted">مكتملة ومدققة</span>
          </div>
          <p className="mt-2 text-[11px] text-emerald-600 dark:text-emerald-400 group-hover:underline">عرض المذكرات المعتمدة ←</p>
        </div>

        {/* Card 3: Pending Review */}
        <div
          onClick={() => onNavigate('review')}
          className="app-surface rounded-2xl border app-border p-5 shadow-sm hover:shadow-md transition-shadow cursor-pointer group"
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold app-text-muted">قيد التدقيق والمراجعة</span>
            <span className="text-xl p-2 rounded-xl bg-amber-50 dark:bg-amber-950/50 text-amber-600">🛡️</span>
          </div>
          <div className="mt-3 flex items-baseline gap-2">
            <span className="text-3xl font-bold text-amber-600 dark:text-amber-400 font-mono">
              {loading ? '—' : stats.pendingReview.toLocaleString('ar-SY')}
            </span>
            <span className="text-xs app-text-muted">تحتاج إجراء</span>
          </div>
          <p className="mt-2 text-[11px] text-amber-600 dark:text-amber-400 group-hover:underline">فتح طابور التدقيق ←</p>
        </div>

        {/* Card 4: Active Facilities */}
        <div
          onClick={() => onNavigate('master')}
          className="app-surface rounded-2xl border app-border p-5 shadow-sm hover:shadow-md transition-shadow cursor-pointer group"
        >
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold app-text-muted">المنشآت الصناعية الفعالة</span>
            <span className="text-xl p-2 rounded-xl bg-purple-50 dark:bg-purple-950/50 text-purple-600">🏢</span>
          </div>
          <div className="mt-3 flex items-baseline gap-2">
            <span className="text-3xl font-bold text-purple-600 dark:text-purple-400 font-mono">
              {loading ? '—' : `${stats.activeCompanies} / ${stats.activeFactories}`}
            </span>
            <span className="text-xs app-text-muted">شركة / معمل</span>
          </div>
          <p className="mt-2 text-[11px] text-purple-600 dark:text-purple-400 group-hover:underline">إدارة البيانات الأساسية ←</p>
        </div>
      </section>

      {/* Core Action Navigation Grid */}
      <section className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-base font-bold app-text flex items-center gap-2">
            <span>⚡</span>
            <span>بوابة الوصول السريع للوحدات</span>
          </h2>
          <span className="text-xs app-text-muted">اختر القسم المطلوب للمتابعة المباشرة</span>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {/* Action 1: Memos Register */}
          <div
            onClick={() => onNavigate('memos')}
            className="app-surface rounded-2xl border app-border p-5 shadow-sm hover:border-blue-500/60 hover:shadow-md transition-all cursor-pointer flex flex-col justify-between"
          >
            <div>
              <div className="flex items-center gap-3 mb-2">
                <span className="text-2xl p-2 rounded-xl bg-blue-50 dark:bg-blue-950 text-blue-600">📝</span>
                <div>
                  <h3 className="text-sm font-bold app-text">سجل المذكرات</h3>
                  <span className="text-[11px] text-blue-600 dark:text-blue-400 font-semibold">إدخال، استعراض وبحث</span>
                </div>
              </div>
              <p className="text-xs app-text-muted leading-relaxed">
                استعراض سجل المذكرات التفتيشية الشامل، البحث المتقدم برقم المذكرة والشركة، وإرفاق الصور والتقارير الميدانية.
              </p>
            </div>
            <div className="mt-4 pt-3 border-t app-border flex justify-between items-center text-xs font-bold text-blue-600 dark:text-blue-400">
              <span>فتح السجل</span>
              <span>←</span>
            </div>
          </div>

          {/* Action 2: Review Queue */}
          {(isReviewer || isAdmin) && (
            <div
              onClick={() => onNavigate('review')}
              className="app-surface rounded-2xl border app-border p-5 shadow-sm hover:border-amber-500/60 hover:shadow-md transition-all cursor-pointer flex flex-col justify-between"
            >
              <div>
                <div className="flex items-center gap-3 mb-2">
                  <span className="text-2xl p-2 rounded-xl bg-amber-50 dark:bg-amber-950 text-amber-600">🛡️</span>
                  <div>
                    <h3 className="text-sm font-bold app-text">طابور التدقيق والاعتماد</h3>
                    <span className="text-[11px] text-amber-600 dark:text-amber-400 font-semibold">
                      {stats.pendingReview > 0 ? `${stats.pendingReview} مذكرة معلقة` : 'مكتمل حالياً'}
                    </span>
                  </div>
                </div>
                <p className="text-xs app-text-muted leading-relaxed">
                  تدقيق المذكرات المرفوعة من المفتشين، التحقق من دقة المؤشرات وبنود التفتيش، وإصدار قرارات الاعتماد أو الإرجاع.
                </p>
              </div>
              <div className="mt-4 pt-3 border-t app-border flex justify-between items-center text-xs font-bold text-amber-600 dark:text-amber-400">
                <span>فتح طابور التدقيق</span>
                <span>←</span>
              </div>
            </div>
          )}

          {/* Action 3: Master Data */}
          {(isAdmin || isReviewer) && (
            <div
              onClick={() => onNavigate('master')}
              className="app-surface rounded-2xl border app-border p-5 shadow-sm hover:border-purple-500/60 hover:shadow-md transition-all cursor-pointer flex flex-col justify-between"
            >
              <div>
                <div className="flex items-center gap-3 mb-2">
                  <span className="text-2xl p-2 rounded-xl bg-purple-50 dark:bg-purple-950 text-purple-600">🏢</span>
                  <div>
                    <h3 className="text-sm font-bold app-text">البيانات الأساسية</h3>
                    <span className="text-[11px] text-purple-600 dark:text-purple-400 font-semibold">هيكلة المنشآت والبنود</span>
                  </div>
                </div>
                <p className="text-xs app-text-muted leading-relaxed">
                  إدارة الشركات الصناعية، المعامل التابعة، خطوط الإنتاج، وتحديد أنواع مذكرات التفتيش وقوائم بنود الفحص الخاصة بها.
                </p>
              </div>
              <div className="mt-4 pt-3 border-t app-border flex justify-between items-center text-xs font-bold text-purple-600 dark:text-purple-400">
                <span>إدارة المنشآت والبنود</span>
                <span>←</span>
              </div>
            </div>
          )}

          {/* Action 4: Reports Hub */}
          <div
            onClick={() => onNavigate('reports')}
            className="app-surface rounded-2xl border app-border p-5 shadow-sm hover:border-emerald-500/60 hover:shadow-md transition-all cursor-pointer flex flex-col justify-between"
          >
            <div>
              <div className="flex items-center gap-3 mb-2">
                <span className="text-2xl p-2 rounded-xl bg-emerald-50 dark:bg-emerald-950 text-emerald-600">📑</span>
                <div>
                  <h3 className="text-sm font-bold app-text">مركز التقارير والكشوفات</h3>
                  <span className="text-[11px] text-emerald-600 dark:text-emerald-400 font-semibold">تصدير PDF و Excel</span>
                </div>
              </div>
              <p className="text-xs app-text-muted leading-relaxed">
                استخراج الكشوفات الرسمية الدورية للمذكرات، سجلات الرقابة، وتقارير أداء المعامل للتصدير والطباعة الورقية المعتمدة.
              </p>
            </div>
            <div className="mt-4 pt-3 border-t app-border flex justify-between items-center text-xs font-bold text-emerald-600 dark:text-emerald-400">
              <span>فتح مركز التقارير</span>
              <span>←</span>
            </div>
          </div>

          {/* Action 5: Incomplete Indicators */}
          {(isDataEntry || isReviewer || isAdmin) && (
            <div
              onClick={() => onNavigate('incomplete')}
              className="app-surface rounded-2xl border app-border p-5 shadow-sm hover:border-orange-500/60 hover:shadow-md transition-all cursor-pointer flex flex-col justify-between"
            >
              <div>
                <div className="flex items-center gap-3 mb-2">
                  <span className="text-2xl p-2 rounded-xl bg-orange-50 dark:bg-orange-950 text-orange-600">⚠️</span>
                  <div>
                    <h3 className="text-sm font-bold app-text">استكمال المؤشرات</h3>
                    <span className="text-[11px] text-orange-600 dark:text-orange-400 font-semibold">متابعة البنود الناقصة</span>
                  </div>
                </div>
                <p className="text-xs app-text-muted leading-relaxed">
                  حصر وتدقيق المذكرات التي تحتوي على مؤشرات فنية غير مكتملة أو لم تتم مراجعتها بشكل نهائي لضمان شمولية التفتيش.
                </p>
              </div>
              <div className="mt-4 pt-3 border-t app-border flex justify-between items-center text-xs font-bold text-orange-600 dark:text-orange-400">
                <span>استكمال البنود</span>
                <span>←</span>
              </div>
            </div>
          )}

          {/* Action 6: Advanced Analytics */}
          <div
            onClick={() => onNavigate('analytics')}
            className="app-surface rounded-2xl border app-border p-5 shadow-sm hover:border-cyan-500/60 hover:shadow-md transition-all cursor-pointer flex flex-col justify-between"
          >
            <div>
              <div className="flex items-center gap-3 mb-2">
                <span className="text-2xl p-2 rounded-xl bg-cyan-50 dark:bg-cyan-950 text-cyan-600">📊</span>
                <div>
                  <h3 className="text-sm font-bold app-text">الرسوم البيانية والتحليلات</h3>
                  <span className="text-[11px] text-cyan-600 dark:text-cyan-400 font-semibold">إحصائيات تخصصية ومؤشرات تراكمية</span>
                </div>
              </div>
              <p className="text-xs app-text-muted leading-relaxed">
                لمن يرغب في استعراض الرسوم البيانية التخصصية، تحليل نسب العيوب، وتوزيع المذكرات ومقارنات الأداء عبر الزمن.
              </p>
            </div>
            <div className="mt-4 pt-3 border-t app-border flex justify-between items-center text-xs font-bold text-cyan-600 dark:text-cyan-400">
              <span>الانتقال للرسوم البيانية</span>
              <span>←</span>
            </div>
          </div>
        </div>
      </section>

      {/* Recent Activity Mini-Feed */}
      <section className="app-surface rounded-2xl border app-border p-5 shadow-sm space-y-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="text-lg">🕒</span>
            <h3 className="text-sm font-bold app-text">أحدث مذكرات التفتيش المسجلة بالنظام</h3>
          </div>
          <button
            type="button"
            onClick={() => onNavigate('memos')}
            className="text-xs font-bold text-blue-600 dark:text-blue-400 hover:underline"
          >
            عرض الكل ({stats.totalMemos}) ←
          </button>
        </div>

        {loading ? (
          <div className="p-8 text-center app-text-muted text-xs">جارٍ تحميل المذكرات الأخيرة...</div>
        ) : recentNotes.length === 0 ? (
          <div className="p-8 text-center app-text-muted text-xs">لا توجد مذكرات مسجلة بعد.</div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-right text-xs border-collapse">
              <thead>
                <tr className="border-b app-border bg-slate-50/50 dark:bg-slate-900/50 text-slate-500 font-semibold">
                  <th className="p-3">رقم المذكرة</th>
                  <th className="p-3">نوع المذكرة</th>
                  <th className="p-3">الشركة / المعمل</th>
                  <th className="p-3">تاريخ الوصول</th>
                  <th className="p-3">الحالة</th>
                  <th className="p-3 text-center">الإجراء</th>
                </tr>
              </thead>
              <tbody className="divide-y app-border">
                {recentNotes.map((note) => (
                  <tr key={note.noteId} className="hover:bg-slate-50 dark:hover:bg-slate-800/40">
                    <td className="p-3 font-mono font-bold text-blue-600 dark:text-blue-400">{note.noteNumber}</td>
                    <td className="p-3 font-medium app-text">{note.noteTypeName}</td>
                    <td className="p-3 app-text-muted">
                      <span>{note.companyName}</span>
                      {note.factoryName && <span className="text-[11px] block text-slate-400">{note.factoryName}</span>}
                    </td>
                    <td className="p-3 font-mono text-[11px] app-text-muted">
                      {new Date(note.arrivalAt).toLocaleDateString('ar-SY')}
                    </td>
                    <td className="p-3">{getStatusBadge(note.status)}</td>
                    <td className="p-3 text-center">
                      <button
                        type="button"
                        onClick={() => onNavigate('memos')}
                        className="px-3 py-1 rounded-lg border app-border app-surface-muted app-hover app-text text-[11px] font-semibold"
                      >
                        معاينة
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* New Memo Modal */}
      {isNewMemoOpen && (
        <MemoEditorModal
          isOpen={isNewMemoOpen}
          noteId={null}
          onClose={() => setIsNewMemoOpen(false)}
          onSaved={() => {
            setIsNewMemoOpen(false);
            loadDashboardPulse();
          }}
        />
      )}
    </div>
  );
};

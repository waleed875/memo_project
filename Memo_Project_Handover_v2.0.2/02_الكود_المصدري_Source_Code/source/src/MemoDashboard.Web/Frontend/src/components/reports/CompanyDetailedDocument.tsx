import React from 'react';
import SyrianLogo from '../../assets/Syrian_logo_icon_gold.svg';
import type { CompanyReportDataModel } from '../../types/memo';
import { formatNumber, formatPercent } from '../../services/format';
import {
  compositePercent,
  mechanismEfficiencyPercent,
  parseFormEfficiency,
  weightedTimelinessPercent,
} from '../../services/kpi';

interface CompanyDetailedDocumentProps {
  data: CompanyReportDataModel;
  loading?: boolean;
  onPrint?: () => void;
  onDownloadPdf?: () => void;
}

export const CompanyDetailedDocument: React.FC<CompanyDetailedDocumentProps> = ({
  data,
  loading = false,
  onPrint,
  onDownloadPdf,
}) => {
  const { identity, issue, metrics, monthly, fieldErrors, factories } = data;

  const formEfficiency = parseFormEfficiency(issue?.formEfficiencyIndex?.toString() ?? '100');
  const mechanismEfficiency = mechanismEfficiencyPercent(
    metrics.entryNotes ?? 0,
    metrics.unlinkedEntryNotes ?? 0
  );
  const weightedTimeliness = weightedTimelinessPercent(
    metrics.totalNotes ?? 0,
    metrics.onTimeNotes ?? 0,
    metrics.lateNotes ?? 0
  );
  const composite = compositePercent(
    formEfficiency,
    metrics.itemQualityPercentage,
    mechanismEfficiency
  );

  const cleanReviewedRate = metrics.totalNotes > 0
    ? ((metrics.cleanReviewedNotes ?? 0) / metrics.totalNotes) * 100
    : null;

  // Build Arabic institution lines
  const arLines = [
    identity.republicNameArabic || 'الجمهورية العربية السورية',
    identity.ministryName || 'وزارة الدفاع',
    identity.companyName || 'الشركة القابضة للصناعات الدفاعية',
    identity.administrativeLevelOne,
    identity.administrativeLevelTwo,
    identity.administrativeLevelThree,
  ].filter(Boolean);

  // Build English institution lines
  const enLines = [
    identity.republicNameEnglish || 'Syrian Arab Republic',
    identity.ministryNameEnglish || 'Ministry of Defense',
    identity.companyNameEnglish || 'Defense Industries Holding Company',
    identity.administrativeLevelOneEnglish,
    identity.administrativeLevelTwoEnglish,
    identity.administrativeLevelThreeEnglish,
  ].filter(Boolean);

  if (loading) {
    return (
      <div className="max-w-6xl mx-auto p-12 text-center bg-white rounded-2xl border border-[#988561] shadow-md">
        <div className="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-[#B9A779]"></div>
        <p className="mt-3 text-sm font-semibold text-[#3D3A3B]">جارٍ تحميل وتوليد التقرير التفصيلي...</p>
      </div>
    );
  }

  const kpiCards = [
    { label: 'إجمالي المذكرات', val: formatNumber(metrics.totalNotes), tone: 'text-[#161616]' },
    { label: 'نسبة صحة البنود', val: formatPercent(metrics.itemQualityPercentage), tone: 'text-[#054239]' },
    { label: 'نسبة صحة المذكرات', val: formatPercent(cleanReviewedRate), tone: 'text-[#054239]' },
    { label: 'مذكرات سليمة بعد المراجعة', val: formatNumber(metrics.cleanReviewedNotes), tone: 'text-[#054239]' },
    { label: 'مراجعة غير مكتملة', val: formatNumber(metrics.incompleteReviewNotes), tone: 'text-[#988561]' },
    { label: 'مذكرات كلها غير منطبقة', val: formatNumber(metrics.allNotApplicableNoteCount), tone: 'text-[#988561]' },
    { label: 'مؤهلة للجودة', val: formatNumber(metrics.qualityEligibleNoteCount), tone: 'text-[#054239]' },
    { label: 'مذكرات تحتوي أخطاء', val: formatNumber(metrics.errorNotes), tone: 'text-[#6B1F2A]' },
    { label: 'نسبة الالتزام بالمدة', val: formatPercent(metrics.timeCompliancePercentage), tone: 'text-[#054239]' },
    { label: 'مذكرات متأثرة بالتكرار', val: formatNumber(metrics.affectedNotes), tone: 'text-[#988561]' },
    { label: 'نسبة عدم تكرار المذكرات', val: formatPercent(metrics.duplicateFreePercentage), tone: 'text-[#054239]' },
    { label: 'حوادث التكرار', val: formatNumber(metrics.duplicateIncidents), tone: 'text-[#988561]' },
    { label: 'إعادات إرسال الشركة', val: formatNumber(metrics.companyResendIncidents), tone: 'text-[#6B1F2A]' },
    { label: 'مذكرات الإدخال', val: formatNumber(metrics.entryNotes), tone: 'text-[#161616]' },
    { label: 'إدخالات غير مربوطة', val: formatNumber(metrics.unlinkedEntryNotes), tone: 'text-[#6B1F2A]' },
    { label: 'نسبة تغطية المراجعة', val: formatPercent(metrics.reviewCoveragePercentage), tone: 'text-[#054239]' },
    { label: 'بنود غير مراجعة', val: formatNumber(metrics.unreviewedItems), tone: 'text-[#6B1F2A]' },
    { label: 'بنود غير منطبقة', val: formatNumber(metrics.notApplicableItems), tone: 'text-[#988561]' },
    { label: 'مذكرات بلا بنود متوقعة', val: formatNumber(metrics.noExpectedItemsNoteCount), tone: 'text-[#3D3A3B]' },
    { label: 'متوسط التأخير', val: metrics.averageLateDays !== null ? `${metrics.averageLateDays} يوم` : '—', tone: 'text-[#988561]' },
    { label: 'أقصى تأخير', val: metrics.maximumLateDays !== null ? `${metrics.maximumLateDays} يوم` : '—', tone: 'text-[#6B1F2A]' },
    { label: 'مؤشر كفاءة النموذج', val: formatPercent(formEfficiency), tone: 'text-[#054239]' },
    { label: 'نسبة التكرار الشهري', val: formatPercent(metrics.affectedNoteRatePercentage), tone: 'text-[#6B1F2A]' },
    { label: 'مؤشر كفاءة الآليات', val: formatPercent(mechanismEfficiency), tone: (mechanismEfficiency ?? 0) >= 60 ? 'text-[#054239]' : 'text-[#988561]' },
    { label: 'الالتزام الزمني المرجح', val: formatPercent(weightedTimeliness), tone: 'text-[#054239]' },
    { label: 'المؤشر المركب للشركة', val: formatPercent(composite), tone: 'text-[#054239] font-black' },
  ];

  return (
    <div
      className="company-detailed-document max-w-6xl mx-auto bg-white border border-[#988561] rounded-2xl p-6 md:p-8 shadow-md print:shadow-none print:border-none print:p-0 print:m-0 print:max-w-none text-[#161616] transition-colors"
      dir="rtl"
    >
      {/* Document Action Bar (Print / Export) */}
      <div className="flex flex-wrap items-center justify-between gap-3 mb-6 pb-4 border-b border-[#988561]/30 print:hidden">
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onPrint || (() => window.print())}
            className="inline-flex items-center gap-2 bg-[#988561] hover:bg-[#857350] text-white font-bold px-4 py-2 rounded-lg text-xs shadow-sm transition-all cursor-pointer"
            title="طباعة هذه الوثيقة مباشرة كملف PDF بالتنسيق السيادي الفاخر"
          >
            <span className="text-sm">🖨️</span>
            <span>طباعة المستند / حفظ كـ PDF</span>
          </button>
          {onDownloadPdf && (
            <button
              type="button"
              onClick={onDownloadPdf}
              className="inline-flex items-center gap-2 border border-[#988561] hover:bg-[#EDEBE0] text-[#988561] font-bold px-4 py-2 rounded-lg text-xs shadow-sm transition-all cursor-pointer"
              title="تصدير PDF عالي الدقة من السيرفر بمحرك Chromium"
            >
              <span className="text-sm">📥</span>
              <span>تصدير PDF عالي الدقة (سيرفر)</span>
            </button>
          )}
          <span className="text-[11px] text-[#3D3A3B]">
            النسق المعتمد: وثيقة سيادية رسمية تطابق النسخة المعتمدة
          </span>
        </div>
        <div className="text-xs font-semibold text-[#054239] flex items-center gap-1.5">
          <span className="inline-block w-2 h-2 rounded-full bg-[#054239] animate-pulse"></span>
          <span>جاهز للطباعة والتصدير بالدقة الكاملة</span>
        </div>
      </div>

      {/* 1. Official Syrian Institution Header */}
      <div className="official-document-header relative print:block">
        <div className="grid grid-cols-1 md:grid-cols-3 print:!grid-cols-3 gap-4 print:gap-2 items-center">
          {/* Right: Arabic Hierarchy */}
          <div className="text-right text-xs md:text-sm print:text-[10.5px] font-bold text-[#161616] leading-relaxed print:leading-tight">
            {arLines.map((line, idx) => (
              <div key={idx}>{line}</div>
            ))}
          </div>

          {/* Center: Golden Emblem */}
          <div className="flex flex-col items-center justify-center">
            <img
              src={SyrianLogo}
              alt="شعار الجمهورية العربية السورية"
              className="h-20 w-auto print:h-14 object-contain drop-shadow-sm print:!block"
            />
          </div>

          {/* Left: English Hierarchy */}
          <div className="text-left text-xs print:text-[10px] font-semibold text-[#3D3A3B] leading-normal print:leading-tight font-sans" dir="ltr">
            {enLines.map((line, idx) => (
              <div key={idx}>{line}</div>
            ))}
          </div>
        </div>

        {/* Gold Separator Line */}
        <div className="w-full border-t-2 border-[#B9A779] my-3 print:my-1.5"></div>

        {/* Sub-Header Metadata */}
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs print:text-[10px] font-semibold text-[#3D3A3B] px-1">
          <div>
            <span>رقم الكتاب: </span>
            <span className="font-mono text-[#161616]">{issue?.letterNumber || '—'}</span>
          </div>
          <div>
            <span>الفترة: </span>
            <span className="font-mono text-[#161616]">{data.filterSummary || 'غير محددة'}</span>
          </div>
        </div>

        {/* Main Title & Company Name */}
        <div className="text-center my-3 print:my-1.5">
          <h1 className="text-xl md:text-2xl print:text-lg font-black text-[#161616] tracking-tight">
            {issue?.title || 'التقرير التفصيلي لأداء الشركة'}
          </h1>
          <p className="text-sm md:text-base print:text-xs font-bold text-[#988561] mt-1 print:mt-0.5">
            الشركة: {data.companyName}
          </p>
        </div>
      </div>

      {/* 2. The 26 KPI Metric Cards Grid */}
      <section className="my-5 print:my-2" aria-label="شبكة مؤشرات التقرير التفصيلي">
        <div className="doc-kpi-grid grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 print:!grid-cols-6 gap-2 print:!gap-1">
          {kpiCards.map((card, idx) => (
            <div
              key={idx}
              className="border border-[#988561] rounded-lg overflow-hidden flex flex-col justify-between bg-white shadow-xs print:shadow-none hover:border-[#B9A779] transition-all"
            >
              <div className="bg-[#EDEBE0] px-1.5 py-1 text-[11px] font-bold text-[#161616] text-center min-h-[36px] print:min-h-[22px] flex items-center justify-center border-b border-[#988561] leading-snug print:text-[8.5px] print:py-0.5">
                {card.label}
              </div>
              <div className={`py-1.5 px-2 text-center text-xs md:text-sm font-bold tnum print:text-[10.5px] print:py-0.5 ${card.tone}`}>
                {card.val}
              </div>
            </div>
          ))}
        </div>
      </section>

      {/* 3. Visual Charts & Trend Breakdown Section */}
      <section className="my-5 print:my-2" aria-label="الرسوم البيانية والتطور الشهري">
        <div className="doc-charts-grid grid grid-cols-1 md:grid-cols-3 print:!grid-cols-3 gap-4 print:!gap-2">
          {/* Chart 1: Monthly Quality Trend */}
          <div className="border border-[#988561] rounded-xl p-3 print:p-2 bg-white shadow-xs print:shadow-none flex flex-col justify-between">
            <div>
              <h3 className="text-xs print:text-[10px] font-bold text-[#161616] mb-2 print:mb-1 border-b border-[#EDEBE0] pb-1.5 print:pb-0.5">
                📈 تطور نسبة صحة البنود شهرياً
              </h3>
              <div className="space-y-1.5 print:space-y-0.5 max-h-44 overflow-y-auto pr-1 print:max-h-none print:overflow-visible">
                {monthly.length === 0 ? (
                  <p className="text-xs text-[#3D3A3B] text-center py-4 print:py-1">لا توجد بيانات شهرية متاحة للفترة</p>
                ) : (
                  monthly.map((m, idx) => {
                    const pct = m.itemQualityPercentage ?? 0;
                    return (
                      <div key={idx} className="flex items-center gap-2 text-xs print:text-[9.5px]">
                        <span className="w-16 print:w-12 font-mono text-[#3D3A3B] flex-shrink-0">
                          {m.periodLabel}
                        </span>
                        <div className="flex-1 bg-[#EDEBE0] h-2.5 print:h-2 rounded-full overflow-hidden">
                          <div
                            className="bg-[#428177] h-full rounded-full transition-all"
                            style={{ width: `${Math.min(100, Math.max(0, pct))}%` }}
                          ></div>
                        </div>
                        <span className="w-12 print:w-10 font-bold text-left text-[#054239] tnum">
                          {pct.toFixed(1)}%
                        </span>
                      </div>
                    );
                  })
                )}
              </div>
            </div>
            <div className="text-[11px] print:text-[9px] text-[#3D3A3B] pt-2 print:pt-1 border-t border-[#EDEBE0]">
              متوسط جودة البنود: <strong className="text-[#054239]">{formatPercent(metrics.itemQualityPercentage)}</strong>
            </div>
          </div>

          {/* Chart 2: Delay & Timeliness Distribution */}
          <div className="border border-[#988561] rounded-xl p-3 print:p-2 bg-white shadow-xs print:shadow-none flex flex-col justify-between">
            <div>
              <h3 className="text-xs print:text-[10px] font-bold text-[#161616] mb-2 print:mb-1 border-b border-[#EDEBE0] pb-1.5 print:pb-0.5">
                ⏱️ توزيع التأخير والالتزام الزمني
              </h3>
              <div className="space-y-1.5 print:space-y-0.5 py-1 print:py-0">
                {[
                  { label: 'ضمن المدة النظامية', count: metrics.onTimeNotes ?? 0, color: 'bg-[#428177]', text: 'text-[#054239]' },
                  { label: 'تأخير 6 - 10 أيام', count: metrics.late6To10Notes ?? 0, color: 'bg-[#988561]', text: 'text-[#988561]' },
                  { label: 'تأخير 11 - 20 يوماً', count: metrics.late11To20Notes ?? 0, color: 'bg-[#B9A779]', text: 'text-[#988561]' },
                  { label: 'تأخير أكثر من 20 يوماً', count: metrics.lateOver20Notes ?? 0, color: 'bg-[#6B1F2A]', text: 'text-[#6B1F2A]' },
                ].map((row, idx) => {
                  const total = metrics.totalNotes || 1;
                  const ratio = Math.round((row.count / total) * 100);
                  return (
                    <div key={idx} className="flex items-center gap-2 text-xs print:text-[9.5px]">
                      <span className="w-28 print:w-22 text-[#3D3A3B] flex-shrink-0">{row.label}</span>
                      <div className="flex-1 bg-[#EDEBE0] h-2.5 print:h-2 rounded-full overflow-hidden">
                        <div className={`${row.color} h-full rounded-full`} style={{ width: `${ratio}%` }}></div>
                      </div>
                      <span className={`w-14 print:w-12 font-bold text-left ${row.text} tnum`}>
                        {formatNumber(row.count)} ({ratio}%)
                      </span>
                    </div>
                  );
                })}
              </div>
            </div>
            <div className="text-[11px] print:text-[9px] text-[#3D3A3B] pt-2 print:pt-1 border-t border-[#EDEBE0]">
              نسبة الالتزام: <strong className="text-[#054239]">{formatPercent(metrics.timeCompliancePercentage)}</strong>
            </div>
          </div>

          {/* Chart 3: Machinery Linking & Efficiency */}
          <div className="border border-[#988561] rounded-xl p-3 print:p-2 bg-white shadow-xs print:shadow-none flex flex-col justify-between">
            <div>
              <h3 className="text-xs print:text-[10px] font-bold text-[#161616] mb-2 print:mb-1 border-b border-[#EDEBE0] pb-1.5 print:pb-0.5">
                ⚙️ كفاءة ربط الآليات ومطابقة الإدخالات
              </h3>
              <div className="space-y-2 print:space-y-1 py-1 print:py-0">
                <div>
                  <div className="flex justify-between text-xs print:text-[9.5px] font-semibold mb-1 print:mb-0.5">
                    <span className="text-[#3D3A3B]">مؤشر كفاءة الآليات (المتمم):</span>
                    <span className="font-bold text-[#054239] tnum">
                      {formatPercent(mechanismEfficiency)}
                    </span>
                  </div>
                  <div className="w-full bg-[#EDEBE0] h-3 print:h-2 rounded-full overflow-hidden flex">
                    <div
                      className="bg-[#428177] h-full rounded-r-full transition-all"
                      style={{ width: `${Math.min(100, Math.max(0, mechanismEfficiency ?? 0))}%` }}
                      title={`مربوطة: ${metrics.linkedEntryNotes}`}
                    ></div>
                    <div
                      className="bg-[#6B1F2A] h-full rounded-l-full transition-all"
                      style={{ width: `${Math.min(100, Math.max(0, 100 - (mechanismEfficiency ?? 100)))}%` }}
                      title={`غير مربوطة: ${metrics.unlinkedEntryNotes}`}
                    ></div>
                  </div>
                </div>

                <div className="space-y-1 print:space-y-0.5 text-xs print:text-[9px] pt-1 print:pt-0.5">
                  <div className="flex justify-between text-[#3D3A3B]">
                    <span>إجمالي مذكرات الإدخال:</span>
                    <strong className="font-mono tnum text-[#161616]">{formatNumber(metrics.entryNotes)}</strong>
                  </div>
                  <div className="flex justify-between text-[#054239] font-semibold">
                    <span>مذكرات مربوطة بآليات:</span>
                    <strong className="font-mono tnum">{formatNumber(metrics.linkedEntryNotes)}</strong>
                  </div>
                  <div className="flex justify-between text-[#6B1F2A] font-semibold">
                    <span>إدخالات بلا مقابل (غير مربوطة):</span>
                    <strong className="font-mono tnum">{formatNumber(metrics.unlinkedEntryNotes)}</strong>
                  </div>
                </div>
              </div>
            </div>
            <div className="text-[11px] print:text-[9px] text-[#3D3A3B] pt-2 print:pt-1 border-t border-[#EDEBE0]">
              المؤشر المركب العام: <strong className="text-[#054239]">{formatPercent(composite)}</strong>
            </div>
          </div>
        </div>
      </section>

      {/* 4. Executive Summary Paragraph */}
      <section className="my-4 print:my-2 bg-[#EDEBE0] border border-[#988561] rounded-xl p-3.5 print:p-2 text-xs md:text-sm print:text-[10px] font-semibold text-[#161616] leading-relaxed print:leading-normal shadow-xs print:shadow-none">
        <span className="font-bold text-[#054239]">الملخص التنفيذي لتقييم الأداء: </span>
        بلغ إجمالي مذكرات الشركة ({formatNumber(metrics.totalNotes)}) مذكرة، محققة نسبة دقة بنود بلغت ({formatPercent(metrics.itemQualityPercentage)})، ومذكرات سليمة تماماً ({formatNumber(metrics.cleanReviewedNotes)})، مع نسبة الالتزام بالمهلة الزمنية بلغت ({formatPercent(metrics.timeCompliancePercentage)})، ومؤشر كفاءة ربط الآليات ({formatPercent(mechanismEfficiency)})، ليكون المؤشر المركب العام لتقييم أداء الشركة ({formatPercent(composite)}).
      </section>

      {/* 5. Detail Tables (Field Errors & Factories) */}
      <section className="my-5 print:my-2" aria-label="جداول التفاصيل التحليلية">
        <div className="doc-tables-grid grid grid-cols-1 md:grid-cols-2 print:!grid-cols-2 gap-4 print:!gap-2">
          {/* Table 1: Top Error Fields */}
          <div className="border border-[#988561] rounded-xl overflow-hidden shadow-xs print:shadow-none bg-white">
            <div className="bg-[#EDEBE0] px-3 py-2 print:px-2 print:py-1 border-b border-[#988561]">
              <h3 className="text-xs print:text-[10px] font-bold text-[#161616]">
                ⚠️ البنود الأكثر تكراراً للأخطاء
              </h3>
            </div>
            <div className="overflow-x-auto max-h-56 print:max-h-none print:overflow-visible">
              <table className="w-full text-right text-xs print:text-[9.5px] text-[#161616]">
                <thead className="bg-[#EDEBE0] text-[#161616] font-bold border-b border-[#988561]">
                  <tr>
                    <th className="p-2 print:p-1">البند / المؤشر</th>
                    <th className="p-2 print:p-1 text-center">أخطاء</th>
                    <th className="p-2 print:p-1 text-center">سليم</th>
                    <th className="p-2 print:p-1 text-center">نسبة الخطأ</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[#EDEBE0]">
                  {fieldErrors.length === 0 ? (
                    <tr>
                      <td colSpan={4} className="p-4 print:p-1.5 text-center text-[#3D3A3B]">لا توجد أخطاء مسجلة في البنود</td>
                    </tr>
                  ) : (
                    fieldErrors.map((row, idx) => (
                      <tr key={idx} className="hover:bg-[#FAF9F6]">
                        <td className="p-2 print:p-1 font-medium">{row.fieldLabel}</td>
                        <td className="p-2 print:p-1 text-center font-bold text-[#6B1F2A] tnum">{row.errorItems}</td>
                        <td className="p-2 print:p-1 text-center text-[#054239] tnum">{row.correctItems}</td>
                        <td className="p-2 print:p-1 text-center font-bold text-[#6B1F2A] tnum">
                          {formatPercent(row.errorRate)}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>

          {/* Table 2: Factory Performance */}
          <div className="border border-[#988561] rounded-xl overflow-hidden shadow-xs print:shadow-none bg-white">
            <div className="bg-[#EDEBE0] px-3 py-2 print:px-2 print:py-1 border-b border-[#988561]">
              <h3 className="text-xs print:text-[10px] font-bold text-[#161616]">
                🏭 أداء المصانع التابعة للشركة
              </h3>
            </div>
            <div className="overflow-x-auto max-h-56 print:max-h-none print:overflow-visible">
              <table className="w-full text-right text-xs print:text-[9.5px] text-[#161616]">
                <thead className="bg-[#EDEBE0] text-[#161616] font-bold border-b border-[#988561]">
                  <tr>
                    <th className="p-2 print:p-1">المصنع</th>
                    <th className="p-2 print:p-1 text-center">إجمالي المذكرات</th>
                    <th className="p-2 print:p-1 text-center">سليمة</th>
                    <th className="p-2 print:p-1 text-center">نسبة الدقة</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[#EDEBE0]">
                  {factories.length === 0 ? (
                    <tr>
                      <td colSpan={4} className="p-4 print:p-1.5 text-center text-[#3D3A3B]">لا توجد مصانع مسجلة ضمن الفترة</td>
                    </tr>
                  ) : (
                    factories.map((row, idx) => {
                      const name = (row as any).factoryName || (row as any).factoryLabel || 'غير محدد';
                      const total = (row as any).totalNotes ?? (row as any).total ?? 0;
                      const clean = (row as any).cleanReviewedNotes ?? (row as any).cleanNotes ?? 0;
                      const percent = (row as any).cleanReviewedNotePercent ?? (row as any).qualityPercentage ?? (total > 0 ? (clean * 100) / total : 0);
                      return (
                        <tr key={idx} className="hover:bg-[#FAF9F6]">
                          <td className="p-2 print:p-1 font-medium">{name}</td>
                          <td className="p-2 print:p-1 text-center font-mono tnum text-[#161616]">{formatNumber(total)}</td>
                          <td className="p-2 print:p-1 text-center text-[#054239] font-mono tnum">{formatNumber(clean)}</td>
                          <td className="p-2 print:p-1 text-center font-bold text-[#054239] tnum">
                            {formatPercent(percent)}
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
      </section>

      {/* 6. Official Footer */}
      <div className="official-document-footer mt-4 print:mt-2 pt-2 border-t border-[#B9A779] text-xs print:text-[9px] text-[#3D3A3B] flex flex-wrap items-center justify-between gap-2">
        <div>
          آخر وصول مسجل: <span className="font-mono text-[#161616]">{data.lastArrivalAt?.slice(0, 10) || '—'}</span> | {data.factoryCount} مصنع مرتبط | الدقة تعني نسبة المذكرات بلا أخطاء.
        </div>
        <div>
          صدر بتاريخ: {issue?.issuedAt?.slice(0, 16).replace('T', ' ') || new Date().toISOString().slice(0, 16).replace('T', ' ')} | أعده: {issue?.issuerName || 'مدير النظام'} | بواسطة برنامج إدارة المذكرات
        </div>
      </div>
    </div>
  );
};

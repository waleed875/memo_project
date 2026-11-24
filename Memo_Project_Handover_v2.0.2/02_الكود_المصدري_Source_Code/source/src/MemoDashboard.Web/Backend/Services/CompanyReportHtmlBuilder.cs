using System.Globalization;
using System.Net;
using MemoDashboard.Backend.Models;

namespace MemoDashboard.Backend.Services;

public sealed class ReportAssets
{
    public string? FontRegularBase64 { get; set; }

    public string? FontBoldBase64 { get; set; }

    public string? LogoSvg { get; set; }
}

/// <summary>
/// Loads ITF Qomra Arabic / Simplified Arabic fonts + gold logo for the report.
/// Degrades cleanly to system fonts if specific files are missing.
/// </summary>
public static class ReportAssetLoader
{
    private static readonly Lazy<ReportAssets> _cachedAssets = new(LoadInternal);

    public static ReportAssets Load() => _cachedAssets.Value;

    private static ReportAssets LoadInternal()
    {
        var baseDir = AppContext.BaseDirectory;
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windir))
            windir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        return new ReportAssets
        {
            FontRegularBase64 = TryReadBase64(Path.Combine(baseDir, "Fonts", "itfQomraArabic-Regular.ttf"))
                ?? TryReadBase64(Path.Combine(baseDir, "..", "..", "..", "..", "Fonts", "itfQomraArabic-Regular.ttf"))
                ?? TryReadBase64(@"D:\Memo\Memo 2.0.0\source\Fonts\itfQomraArabic-Regular.ttf")
                ?? TryReadBase64(Path.Combine(baseDir, "Fonts", "itfQomraArabic-Regular.otf"))
                ?? TryReadBase64(Path.Combine(baseDir, "Fonts", "simpo.ttf"))
                ?? TryReadBase64(Path.Combine(windir, "Fonts", "simpo.ttf"))
                ?? TryReadBase64("/usr/share/fonts/truetype/simplified-arabic/simpo.ttf"),
            FontBoldBase64 = TryReadBase64(Path.Combine(baseDir, "Fonts", "itfQomraArabic-Bold.ttf"))
                ?? TryReadBase64(Path.Combine(baseDir, "..", "..", "..", "..", "Fonts", "itfQomraArabic-Bold.ttf"))
                ?? TryReadBase64(@"D:\Memo\Memo 2.0.0\source\Fonts\itfQomraArabic-Bold.ttf")
                ?? TryReadBase64(Path.Combine(baseDir, "Fonts", "itfQomraArabic-Bold.otf"))
                ?? TryReadBase64(Path.Combine(baseDir, "Fonts", "simpbdo.ttf"))
                ?? TryReadBase64(Path.Combine(windir, "Fonts", "simpbdo.ttf"))
                ?? TryReadBase64("/usr/share/fonts/truetype/simplified-arabic/simpbdo.ttf"),
            LogoSvg = TryReadText(Path.Combine(baseDir, "ReportAssets", "Syrian_logo_icon_gold.svg"))
                ?? TryReadText(Path.Combine(baseDir, "img", "Syrian_logo_icon_gold.svg"))
                ?? TryReadText(Path.Combine(baseDir, "..", "..", "..", "Frontend", "src", "assets", "Syrian_logo_icon_gold.svg"))
                ?? TryReadText(@"D:\Memo\Memo 2.0.0\source\src\MemoDashboard.Web\Frontend\src\assets\Syrian_logo_icon_gold.svg")
        };
    }

    private static string? TryReadBase64(string path)
    {
        try
        {
            return File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Self-contained RTL Arabic HTML for CompanyDetailed (A4 landscape).
/// Matches screen report styling: deep sovereign navy background (#0B132B), crisp typography,
/// official golden Syrian emblem header, 26 KPI metric cards, 3 analytical visual panels,
/// executive summary card, and detailed tables.
/// </summary>
public static class CompanyReportHtmlBuilder
{
    private const string PageBg = "#FFFFFF";
    private const string CardBg = "#FFFFFF";
    private const string CardHeaderBg = "#EDEBE0";
    private const string TableHeaderBg = "#EDEBE0";
    private const string RowEven = "#FFFFFF";
    private const string RowOdd = "#FAF9F6";
    private const string BorderWheat = "#988561";
    private const string BorderLight = "#EDEBE0";
    private const string Gold = "#B9A779";

    private const string TextPrimary = "#161616";
    private const string TextSecondary = "#3D3A3B";

    private const string ForestGreen = "#054239";
    private const string GreenBar = "#428177";
    private const string DeepUmber = "#6B1F2A";
    private const string RedBar = "#6B1F2A";
    private const string WheatAmber = "#988561";
    private const string AmberBar = "#B9A779";

    public static string BuildHtml(CompanyReportData data, ReportAssets assets)
    {
        var m = data.Metrics;
        var issue = data.Issue;
        var identity = data.Identity;

        var form = CompanyReportKpis.FormEfficiencyPercent(issue.FormEfficiencyIndex);
        var mechanism = CompanyReportKpis.MechanismUnlinkedPercent(m.EntryNotes, m.UnlinkedEntryNotes);
        var weighted = CompanyReportKpis.WeightedTimelinessPercent(m.TotalNotes, m.OnTimeNotes, m.LateNotes);
        var composite = CompanyReportKpis.OverallCompositePercent(form, m.ItemQualityPercentage, mechanism);

        var title = string.IsNullOrWhiteSpace(issue.Title) ? "التقرير التفصيلي لأداء الشركة" : issue.Title.Trim();
        var arabicInstitution = BuildInstitutionBlock(
            identity.RepublicNameArabic, identity.MinistryName,
            identity.AdministrativeLevelOne, identity.AdministrativeLevelTwo, identity.AdministrativeLevelThree,
            identity.CompanyName, identity.IssuingEntityName);
        var englishInstitution = BuildInstitutionBlock(
            identity.RepublicNameEnglish, identity.MinistryNameEnglish,
            identity.AdministrativeLevelOneEnglish, identity.AdministrativeLevelTwoEnglish, identity.AdministrativeLevelThreeEnglish,
            identity.CompanyNameEnglish, null);

        var cards = BuildCards(m, form, mechanism, weighted, composite);
        var monthlyBars = BuildMonthlyBars(data.Monthly, m.ItemQualityPercentage);
        var delayBars = BuildDelayBars(m);
        var machineryBars = BuildMachineryBars(m, mechanism, composite);
        var fieldTable = BuildFieldTable(data.FieldErrors);
        var factoryTable = BuildFactoryTable(data.Factories);
        var executive = BuildExecutiveSummary(data, form, mechanism, weighted, composite);

        var fontFace = BuildFontFace(assets);
        var logo = string.IsNullOrWhiteSpace(assets.LogoSvg)
            ? """<div class="logo-fallback">الجمهورية العربية السورية</div>"""
            : assets.LogoSvg;

        var bodyContent = m.TotalNotes == 0
            ? """<div class="empty">لا توجد بيانات للشركة المحددة ضمن الفترة المحددة.</div>"""
            : "<div class=\"cards\">" + cards + "</div>"
                + "<div class=\"charts-grid\">"
                    + "<div class=\"chart-panel\">" + monthlyBars + "</div>"
                    + "<div class=\"chart-panel\">" + delayBars + "</div>"
                    + "<div class=\"chart-panel\">" + machineryBars + "</div>"
                + "</div>"
                + "<div class=\"exec-card\">" + executive + "</div>"
                + "<div class=\"two-col\">"
                    + "<div class=\"table-panel\"><div class=\"table-panel-header\">⚠️ البنود الأكثر تكراراً للأخطاء</div>" + fieldTable + "</div>"
                    + "<div class=\"table-panel\"><div class=\"table-panel-header\">🏭 أداء المصانع التابعة للشركة</div>" + factoryTable + "</div>"
                + "</div>";

        var formattedIssueDate = issue.IssuedAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        var lastArrivalFormatted = CompanyReportKpis.FormatDate(data.LastArrivalAt);

        var filterDisplay = string.IsNullOrWhiteSpace(data.FilterSummary)
            ? "غير محددة"
            : data.FilterSummary.Trim();

        return $$"""
            <!DOCTYPE html>
            <html lang="ar" dir="rtl">
            <head>
            <meta charset="utf-8">
            <title>{{E(title)}} — {{E(data.CompanyName)}}</title>
            <style>
            {{fontFace}}
            @page {
              size: A4 landscape;
              margin: 4mm 6mm 4mm 6mm;
              @bottom-center {
                content: "الصفحة " counter(page) " من " counter(pages);
                font-size: 7pt;
                color: {{TextSecondary}};
                font-family: 'ITF Qomra Arabic','Qomra','Simplified Arabic','Segoe UI',Tahoma,Arial,sans-serif;
              }
              @bottom-right {
                content: "بواسطة {{E(data.ApplicationName)}}";
                font-size: 7pt;
                color: {{TextSecondary}};
                font-family: 'ITF Qomra Arabic','Qomra','Simplified Arabic','Segoe UI',Tahoma,Arial,sans-serif;
              }
            }
            *, *:before, *:after {
              box-sizing: border-box;
              -webkit-print-color-adjust: exact !important;
              print-color-adjust: exact !important;
              color-adjust: exact !important;
            }
            :lang(ar), :lang(ar) * { letter-spacing: 0; }
            html {
              background-color: {{PageBg}} !important;
            }
            body {
              font-family: 'ITF Qomra Arabic','Qomra','Simplified Arabic','Segoe UI',Tahoma,Arial,sans-serif;
              color: {{TextPrimary}} !important;
              margin: 0;
              padding: 0;
              font-size: 8.4pt;
              line-height: 1.38;
              text-align: right;
              direction: rtl;
              background-color: {{PageBg}} !important;
            }
            .num { font-variant-numeric: tabular-nums; direction: ltr; unicode-bidi: embed; }

            /* 1. Official Syrian Header */
            .inst { display: flex; justify-content: space-between; align-items: center; gap: 14px; margin-bottom: 2px; }
            .inst-ar { text-align: right; font-weight: 700; font-size: 8.8pt; line-height: 1.45; flex: 1; color: {{TextPrimary}}; }
            .inst-en { text-align: left; direction: ltr; font-size: 8.4pt; line-height: 1.38; flex: 1; color: {{TextSecondary}}; font-family: 'Segoe UI',Tahoma,Arial,sans-serif; }
            .logo { width: 78px; text-align: center; flex: none; display: flex; align-items: center; justify-content: center; }
            .logo svg { width: 78px; height: auto; max-height: 62px; }
            .logo-fallback { width: 78px; height: 52px; border: 1px solid {{BorderWheat}}; border-radius: 4px; display: flex; align-items: center; justify-content: center; font-size: 7.5pt; color: {{BorderWheat}}; text-align: center; }

            .gold-rule { border: none; border-top: 2px solid {{Gold}}; margin: 3px 0 5px; }

            .meta-row { display: flex; justify-content: space-between; align-items: center; font-size: 7.8pt; color: {{TextSecondary}}; margin-bottom: 2px; }
            .meta-row strong { color: {{TextPrimary}}; }

            h1.title { text-align: center; font-size: 13.5pt; font-weight: 800; line-height: 1.2; margin: 1px 0; color: {{TextPrimary}}; letter-spacing: -0.01em; }
            .company-line { text-align: center; font-size: 10pt; font-weight: 700; color: {{BorderWheat}}; margin-bottom: 5px; }

            /* 2. 26 KPI Metric Cards Grid */
            .cards { display: grid; grid-template-columns: repeat(6, 1fr); gap: 4px; margin: 4px 0 5px; }
            .card { border: 1px solid {{BorderWheat}}; border-radius: 5px; overflow: hidden; page-break-inside: avoid; background: {{CardBg}}; display: flex; flex-direction: column; justify-content: space-between; }
            .card .t { background: {{CardHeaderBg}}; color: {{TextPrimary}}; font-size: 6.8pt; font-weight: 700; padding: 2px 2px; text-align: center; min-height: 24px; line-height: 1.25; display: flex; align-items: center; justify-content: center; border-bottom: 1px solid {{BorderWheat}}; }
            .card .v { background: {{CardBg}}; font-size: 9.5pt; font-weight: 800; padding: 2px 3px; text-align: center; }

            /* 3. 3-Panel Charts Grid */
            .charts-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 6px; margin: 5px 0; }
            .chart-panel { border: 1px solid {{BorderWheat}}; border-radius: 6px; padding: 5px 7px; background: {{CardBg}}; page-break-inside: avoid; display: flex; flex-direction: column; justify-content: space-between; }
            .chart-title { font-size: 7.6pt; font-weight: 700; color: {{TextPrimary}}; border-bottom: 1px solid {{BorderLight}}; padding-bottom: 3px; margin-bottom: 4px; }
            .chart-body { flex: 1; min-height: 68px; }
            .chart-footer { font-size: 6.8pt; color: {{TextSecondary}}; border-top: 1px solid {{BorderLight}}; padding-top: 3px; margin-top: 4px; }

            .bar-row { display: flex; align-items: center; gap: 5px; margin: 2px 0; font-size: 7.2pt; }
            .bar-label { width: 95px; flex: none; text-align: right; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 600; color: {{TextSecondary}}; }
            .bar-track { flex: 1; background: {{BorderLight}}; height: 8px; border-radius: 4px; overflow: hidden; display: flex; }
            .bar-fill { height: 8px; border-radius: 4px; }
            .bar-val { width: 50px; flex: none; text-align: left; font-size: 7pt; }

            /* 4. Executive Callout */
            .exec-card { background: {{CardHeaderBg}}; border: 1px solid {{BorderWheat}}; border-radius: 6px; padding: 5px 9px; font-size: 7.8pt; font-weight: 600; line-height: 1.55; margin: 5px 0; page-break-inside: avoid; text-align: right; color: {{TextPrimary}}; }
            .exec-title { font-weight: 800; color: {{ForestGreen}}; }

            /* 5. Analytical Tables */
            .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; margin: 5px 0; }
            .table-panel { border: 1px solid {{BorderWheat}}; border-radius: 6px; overflow: hidden; page-break-inside: avoid; background: {{CardBg}}; }
            .table-panel-header { background: {{TableHeaderBg}}; padding: 4px 6px; font-size: 7.6pt; font-weight: 700; color: {{TextPrimary}}; border-bottom: 1px solid {{BorderWheat}}; }

            table.grid { width: 100%; border-collapse: collapse; font-size: 7.1pt; color: {{TextPrimary}}; }
            table.grid th, table.grid td { border: 1px solid {{BorderLight}}; padding: 2.5px 5px; text-align: right; vertical-align: middle; line-height: 1.3; }
            table.grid thead th { background: {{TableHeaderBg}}; color: {{TextPrimary}}; font-size: 7.2pt; font-weight: 700; text-align: right; border-bottom: 1px solid {{BorderWheat}}; }
            table.grid tbody tr:nth-child(even) { background: {{RowEven}}; }
            table.grid tbody tr:nth-child(odd) { background: {{RowOdd}}; }
            table.grid td.num { text-align: center; }

            /* 6. Official Footer */
            .issue-foot { font-size: 7pt; margin-top: 5px; text-align: right; border-top: 1px solid {{Gold}}; padding-top: 3px; display: flex; justify-content: space-between; align-items: center; color: {{TextSecondary}}; }
            .empty { text-align: center; color: {{TextSecondary}}; padding: 15px; font-size: 9pt; }

            /* 7. Action Toolbar for Independent Browser Preview (hidden on print) */
            @media print {
              .report-actions-toolbar { display: none !important; }
            }
            .report-actions-toolbar {
              background: {{CardHeaderBg}};
              border: 1px solid {{BorderWheat}};
              border-radius: 8px;
              padding: 8px 14px;
              margin: 0 0 12px 0;
              display: flex;
              flex-wrap: wrap;
              align-items: center;
              justify-content: space-between;
              gap: 10px;
              box-shadow: 0 1px 3px rgba(0,0,0,0.06);
              font-family: 'ITF Qomra Arabic','Simplified Arabic','Segoe UI',Tahoma,Arial,sans-serif;
            }
            .toolbar-buttons { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
            .toolbar-btn {
              background: {{ForestGreen}};
              color: #FFFFFF;
              border: 1px solid {{ForestGreen}};
              border-radius: 6px;
              padding: 5px 12px;
              font-size: 8.5pt;
              font-weight: 700;
              cursor: pointer;
              text-decoration: none;
              display: inline-flex;
              align-items: center;
              gap: 5px;
              transition: opacity 0.15s;
            }
            .toolbar-btn:hover { opacity: 0.9; }
            .toolbar-btn.btn-sec {
              background: #FFFFFF;
              color: {{TextPrimary}};
              border: 1px solid {{BorderWheat}};
            }
            .toolbar-btn.btn-sec:hover {
              background: {{CardHeaderBg}};
            }
            .toolbar-hint {
              font-size: 7.6pt;
              color: {{DeepUmber}};
              font-weight: 700;
            }
            </style>
            </head>
            <body>
            <div class="report-actions-toolbar">
              <div class="toolbar-buttons">
                <button type="button" class="toolbar-btn" onclick="window.print()">
                  <span>🖨️</span> طباعة المستند (A4 أفقي)
                </button>
                <button type="button" class="toolbar-btn btn-sec" onclick="const u=new URL(window.location.href);u.pathname=u.pathname.replace('.html','.pdf');window.location.href=u.href;">
                  <span>📥</span> تحميل PDF عالي الدقة
                </button>
                <button type="button" class="toolbar-btn btn-sec" onclick="const u=new URL(window.location.href);u.pathname=u.pathname.replace('.html','.xlsx');window.location.href=u.href;">
                  <span>📊</span> تصدير Excel
                </button>
                <button type="button" class="toolbar-btn btn-sec" onclick="const u=new URL(window.location.href);u.pathname=u.pathname.replace('.html','.docx');window.location.href=u.href;">
                  <span>📝</span> تصدير Word
                </button>
              </div>
              <div class="toolbar-hint">
                💡 تنبيه: للطباعة على ورقة واحدة متكاملة بدون تشويه، يرجى اختيار الاتجاه (أفقي / Landscape) وتفعيل (الرسومات في الخلفية / Background graphics).
              </div>
            </div>
            <div class="inst">
              <div class="inst-ar">{{arabicInstitution}}</div>
              <div class="logo">{{logo}}</div>
              <div class="inst-en">{{englishInstitution}}</div>
            </div>
            <hr class="gold-rule">
            <div class="meta-row">
              <div><span>رقم الكتاب: </span><strong>{{E(issue.LetterNumber)}}</strong></div>
              <div><span>الفترة: </span><strong>{{E(filterDisplay)}}</strong></div>
            </div>
            <h1 class="title">{{E(title)}}</h1>
            <div class="company-line">الشركة: {{E(data.CompanyName)}}</div>
            {{bodyContent}}
            <div class="issue-foot">
              <div>آخر وصول: {{lastArrivalFormatted}} | {{CompanyReportKpis.FormatInt(data.FactoryCount)}} مصنع مرتبط | الدقة تعني نسبة المذكرات بلا أخطاء.</div>
              <div>صدر بتاريخ: {{formattedIssueDate}} | أعده: {{E(issue.IssuerName)}} | بواسطة {{E(data.ApplicationName)}}</div>
            </div>
            </body>
            </html>
            """;
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string BuildFontFace(ReportAssets assets)
    {
        var css = string.Empty;
        if (!string.IsNullOrWhiteSpace(assets.FontRegularBase64))
        {
            css += "@font-face{font-family:'ITF Qomra Arabic';font-style:normal;font-weight:400;"
                + "src:url(data:font/ttf;base64," + assets.FontRegularBase64 + ") format('truetype');font-display:swap;}";
            css += "@font-face{font-family:'Qomra';font-style:normal;font-weight:400;"
                + "src:url(data:font/ttf;base64," + assets.FontRegularBase64 + ") format('truetype');font-display:swap;}";
            css += "@font-face{font-family:'Simplified Arabic';font-style:normal;font-weight:400;"
                + "src:url(data:font/ttf;base64," + assets.FontRegularBase64 + ") format('truetype');font-display:swap;}";
        }
        if (!string.IsNullOrWhiteSpace(assets.FontBoldBase64))
        {
            css += "@font-face{font-family:'ITF Qomra Arabic';font-style:normal;font-weight:700;"
                + "src:url(data:font/ttf;base64," + assets.FontBoldBase64 + ") format('truetype');font-display:swap;}";
            css += "@font-face{font-family:'Qomra';font-style:normal;font-weight:700;"
                + "src:url(data:font/ttf;base64," + assets.FontBoldBase64 + ") format('truetype');font-display:swap;}";
            css += "@font-face{font-family:'Simplified Arabic';font-style:normal;font-weight:700;"
                + "src:url(data:font/ttf;base64," + assets.FontBoldBase64 + ") format('truetype');font-display:swap;}";
        }
        return css;
    }

    private static string BuildInstitutionBlock(params string?[] lines)
    {
        var kept = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => E(l!.Trim()));
        return string.Join("<br>", kept);
    }

    private sealed record Card(string Title, string Value, string Color);

    private static string BuildCards(CompanyMetrics m, decimal? form, decimal? mechanism, decimal? weighted, decimal? composite)
    {
        var cleanReviewedRate = m.TotalNotes > 0
            ? (m.CleanReviewedNotes * 100m / m.TotalNotes)
            : (decimal?)null;

        var cards = new List<Card>
        {
            new("إجمالي المذكرات", CompanyReportKpis.FormatInt(m.TotalNotes), TextPrimary),
            new("نسبة صحة البنود", CompanyReportKpis.FormatPercent(m.ItemQualityPercentage), ForestGreen),
            new("نسبة صحة المذكرات", CompanyReportKpis.FormatPercent(cleanReviewedRate ?? m.CleanReviewedNotePercent), ForestGreen),
            new("مذكرات سليمة بعد المراجعة", CompanyReportKpis.FormatInt(m.CleanReviewedNotes), ForestGreen),
            new("مراجعة غير مكتملة", CompanyReportKpis.FormatInt(m.IncompleteReviewNotes), WheatAmber),
            new("مذكرات كلها غير منطبقة", CompanyReportKpis.FormatInt(m.AllNotApplicableNoteCount), WheatAmber),
            new("مؤهلة للجودة", CompanyReportKpis.FormatInt(m.QualityEligibleNoteCount), ForestGreen),
            new("مذكرات تحتوي أخطاء", CompanyReportKpis.FormatInt(m.ErrorNotes), DeepUmber),
            new("نسبة الالتزام بالمدة", CompanyReportKpis.FormatPercent(m.TimeCompliancePercentage), ForestGreen),
            new("مذكرات متأثرة بالتكرار", CompanyReportKpis.FormatInt(m.AffectedNotes), WheatAmber),
            new("نسبة عدم تكرار المذكرات", CompanyReportKpis.FormatPercent(m.DuplicateFreePercentage), ForestGreen),
            new("حوادث التكرار", CompanyReportKpis.FormatInt(m.DuplicateIncidents), WheatAmber),
            new("إعادات إرسال الشركة", CompanyReportKpis.FormatInt(m.CompanyResendIncidents), DeepUmber),
            new("مذكرات الإدخال", CompanyReportKpis.FormatInt(m.EntryNotes), TextPrimary),
            new("إدخالات غير مربوطة", CompanyReportKpis.FormatInt(m.UnlinkedEntryNotes), DeepUmber),
            new("نسبة تغطية المراجعة", CompanyReportKpis.FormatPercent(m.ReviewCoveragePercentage), ForestGreen),
            new("بنود غير مراجعة", CompanyReportKpis.FormatInt(m.UnreviewedItems), DeepUmber),
            new("بنود غير منطبقة", CompanyReportKpis.FormatInt(m.NotApplicableItems), WheatAmber),
            new("مذكرات بلا بنود متوقعة", CompanyReportKpis.FormatInt(m.NoExpectedItemsNoteCount), TextSecondary),
            new("متوسط التأخير", CompanyReportKpis.FormatDays(m.AverageLateDays), WheatAmber),
            new("أقصى تأخير", CompanyReportKpis.FormatDays(m.MaximumLateDays), DeepUmber),
            new("مؤشر كفاءة النموذج", form is null ? "غير مسجل" : CompanyReportKpis.FormatPercent(form), ForestGreen),
            new("نسبة التكرار الشهري", CompanyReportKpis.FormatPercent(m.AffectedNoteRatePercentage), DeepUmber),
            new("مؤشر كفاءة الآليات", CompanyReportKpis.FormatPercent(mechanism), (mechanism ?? 0) >= 60 ? ForestGreen : WheatAmber),
            new("الالتزام الزمني المرجح", CompanyReportKpis.FormatPercent(weighted), ForestGreen),
            new("المؤشر المركب للشركة", CompanyReportKpis.FormatPercent(composite), ForestGreen),
        };
        return string.Join(string.Empty, cards.Select(c =>
            $"<div class=\"card\"><div class=\"t\">{E(c.Title)}</div><div class=\"v num\" style=\"color:{c.Color}\">{E(c.Value)}</div></div>"));
    }

    private static string BuildMonthlyBars(List<CompanyReportMonthRow> rows, decimal? avgQuality)
    {
        if (rows.Count == 0) return "<div class=\"chart-title\">📈 تطور صحة البنود شهرياً</div><div class=\"empty\">لا توجد حركة شهرية.</div>";
        var max = Math.Max(1, rows.Max(r => r.TotalNotes));
        var items = rows.Take(6).Select(r =>
        {
            var width = Math.Round(r.TotalNotes * 100.0 / max, 1).ToString("0.#", CultureInfo.InvariantCulture);
            var quality = r.ItemQualityPercentage ?? r.CleanReviewedNotePercent;
            return "<div class=\"bar-row\">"
                + "<span class=\"bar-label\" title=\"" + E(r.PeriodLabel) + "\">" + E(r.PeriodLabel) + "</span>"
                + "<span class=\"bar-track\"><span class=\"bar-fill\" style=\"display:block;width:" + width + "%;background:" + GreenBar + "\"></span></span>"
                + "<span class=\"bar-val num\" style=\"color:" + ForestGreen + ";font-weight:700;\">" + CompanyReportKpis.FormatPercent(quality) + "</span>"
                + "</div>";
        });
        return "<div class=\"chart-title\">📈 تطور نسبة صحة البنود شهرياً</div>"
            + "<div class=\"chart-body\">" + string.Join(string.Empty, items) + "</div>"
            + "<div class=\"chart-footer\">متوسط جودة البنود: <strong style=\"color:" + ForestGreen + "\">" + CompanyReportKpis.FormatPercent(avgQuality) + "</strong></div>";
    }

    private static string BuildDelayBars(CompanyMetrics m)
    {
        var max = Math.Max(1, Math.Max(Math.Max(m.OnTimeNotes, m.Late6To10Notes), Math.Max(m.Late11To20Notes, m.LateOver20Notes)));
        string Row(string label, long value, string barColor, string textColor)
        {
            var width = Math.Round(value * 100.0 / max, 1).ToString("0.#", CultureInfo.InvariantCulture);
            var total = m.TotalNotes > 0 ? Math.Round(value * 100.0 / m.TotalNotes, 0).ToString("0", CultureInfo.InvariantCulture) : "0";
            return "<div class=\"bar-row\">"
                + "<span class=\"bar-label\">" + E(label) + "</span>"
                + "<span class=\"bar-track\"><span class=\"bar-fill\" style=\"display:block;width:" + width + "%;background:" + barColor + "\"></span></span>"
                + "<span class=\"bar-val num\" style=\"color:" + textColor + ";font-weight:700;\">" + CompanyReportKpis.FormatInt(value) + " (" + total + "%)</span>"
                + "</div>";
        }
        return "<div class=\"chart-title\">⏱️ توزيع التأخير والالتزام الزمني</div>"
            + "<div class=\"chart-body\">"
            + Row("ضمن المدة النظامية", m.OnTimeNotes, GreenBar, ForestGreen)
            + Row("تأخير 6 - 10 أيام", m.Late6To10Notes, WheatAmber, WheatAmber)
            + Row("تأخير 11 - 20 يوماً", m.Late11To20Notes, AmberBar, WheatAmber)
            + Row("تأخير أكثر من 20 يوماً", m.LateOver20Notes, RedBar, DeepUmber)
            + "</div>"
            + "<div class=\"chart-footer\">نسبة الالتزام بالمدة: <strong style=\"color:" + ForestGreen + "\">" + CompanyReportKpis.FormatPercent(m.TimeCompliancePercentage) + "</strong></div>";
    }

    private static string BuildMachineryBars(CompanyMetrics m, decimal? mechanism, decimal? composite)
    {
        var mechVal = mechanism ?? 100m;
        var mechWidth = Math.Min(100, Math.Max(0, (double)mechVal)).ToString("0.#", CultureInfo.InvariantCulture);
        var unlinkedWidth = Math.Min(100, Math.Max(0, 100.0 - (double)mechVal)).ToString("0.#", CultureInfo.InvariantCulture);

        return "<div class=\"chart-title\">⚙️ كفاءة ربط الآليات ومطابقة الإدخالات</div>"
            + "<div class=\"chart-body\">"
            + "<div style=\"display:flex;justify-content:space-between;font-size:7.5pt;margin-bottom:3px;\">"
            + "<span style=\"color:#3D3A3B;\">مؤشر كفاءة الآليات (المتمم):</span>"
            + "<strong class=\"num\" style=\"color:" + ((mechanism ?? 0) >= 60 ? ForestGreen : WheatAmber) + "\">" + CompanyReportKpis.FormatPercent(mechanism) + "</strong>"
            + "</div>"
            + "<div class=\"bar-track\" style=\"height:10px;margin-bottom:6px;background:#EDEBE0;\">"
            + "<span style=\"display:block;height:100%;width:" + mechWidth + "%;background:" + GreenBar + ";border-radius:0 4px 4px 0;\"></span>"
            + "<span style=\"display:block;height:100%;width:" + unlinkedWidth + "%;background:" + RedBar + ";border-radius:4px 0 0 4px;\"></span>"
            + "</div>"
            + "<div style=\"font-size:7.4pt;line-height:1.6;\">"
            + "<div style=\"display:flex;justify-content:space-between;\"><span style=\"color:#3D3A3B;\">إجمالي مذكرات الإدخال:</span><strong class=\"num\" style=\"color:" + TextPrimary + ";\">" + CompanyReportKpis.FormatInt(m.EntryNotes) + "</strong></div>"
            + "<div style=\"display:flex;justify-content:space-between;color:" + ForestGreen + ";\"><span style=\"color:#3D3A3B;\">مذكرات مربوطة بآليات:</span><strong class=\"num\">" + CompanyReportKpis.FormatInt(m.LinkedEntryNotes) + "</strong></div>"
            + "<div style=\"display:flex;justify-content:space-between;color:" + DeepUmber + ";\"><span style=\"color:#3D3A3B;\">إدخالات بلا مقابل (غير مربوطة):</span><strong class=\"num\">" + CompanyReportKpis.FormatInt(m.UnlinkedEntryNotes) + "</strong></div>"
            + "</div>"
            + "</div>"
            + "<div class=\"chart-footer\">المؤشر المركب العام: <strong style=\"color:" + ForestGreen + "\">" + CompanyReportKpis.FormatPercent(composite) + "</strong></div>";
    }

    private static string BuildFieldTable(List<CompanyReportFieldRow> rows)
    {
        if (rows.Count == 0) return "<p class=\"empty\">لا توجد أخطاء مسجلة في البنود.</p>";
        var body = string.Join(string.Empty, rows.Take(6).Select(r =>
            $"<tr><td>{E(r.FieldLabel)}</td><td class=\"num\" style=\"color:{DeepUmber};font-weight:700;\">{CompanyReportKpis.FormatInt(r.ErrorItems)}</td>"
            + $"<td class=\"num\" style=\"color:{ForestGreen};\">{CompanyReportKpis.FormatInt(r.CorrectItems)}</td><td class=\"num\" style=\"color:{DeepUmber};font-weight:700;\">{CompanyReportKpis.FormatPercent(r.ErrorRate)}</td></tr>"));
        return "<table class=\"grid\"><thead><tr><th>البند / المؤشر</th><th>أخطاء</th><th>سليم</th><th>نسبة الخطأ</th></tr></thead>"
            + $"<tbody>{body}</tbody></table>";
    }

    private static string BuildFactoryTable(List<CompanyReportFactoryRow> rows)
    {
        if (rows.Count == 0) return "<p class=\"empty\">لا توجد مصانع مسجلة ضمن الفترة.</p>";
        var body = string.Join(string.Empty, rows.Select(r =>
        {
            var name = !string.IsNullOrWhiteSpace(r.FactoryName) ? r.FactoryName : (!string.IsNullOrWhiteSpace(r.FactoryLabel) ? r.FactoryLabel : "غير محدد");
            var total = r.TotalNotes > 0 ? r.TotalNotes : r.Total;
            var clean = r.CleanReviewedNotes > 0 ? r.CleanReviewedNotes : r.CleanNotes;
            var pct = r.CleanReviewedNotePercent ?? r.QualityPercentage ?? (total > 0 ? (clean * 100m / total) : 0m);
            return $"<tr><td>{E(name)}</td><td class=\"num\" style=\"color:{TextPrimary};\">{CompanyReportKpis.FormatInt(total)}</td>"
                + $"<td class=\"num\" style=\"color:{ForestGreen};\">{CompanyReportKpis.FormatInt(clean)}</td>"
                + $"<td class=\"num\" style=\"color:{ForestGreen};font-weight:700;\">{CompanyReportKpis.FormatPercent(pct)}</td></tr>";
        }));
        return "<table class=\"grid\"><thead><tr><th>المصنع</th><th>إجمالي المذكرات</th><th>سليمة</th><th>نسبة الدقة</th></tr></thead>"
            + $"<tbody>{body}</tbody></table>";
    }

    private static string BuildExecutiveSummary(CompanyReportData data, decimal? form, decimal? mechanism, decimal? weighted, decimal? composite)
    {
        var m = data.Metrics;
        return "<span class=\"exec-title\">الملخص التنفيذي لتقييم الأداء: </span>"
            + $"بلغ إجمالي مذكرات الشركة ({CompanyReportKpis.FormatInt(m.TotalNotes)}) مذكرة، "
            + $"محققة نسبة دقة بنود بلغت ({CompanyReportKpis.FormatPercent(m.ItemQualityPercentage)})، "
            + $"ومذكرات سليمة تماماً ({CompanyReportKpis.FormatInt(m.CleanReviewedNotes)})، "
            + $"مع نسبة الالتزام بالمهلة الزمنية بلغت ({CompanyReportKpis.FormatPercent(m.TimeCompliancePercentage)})، "
            + $"ومؤشر كفاءة ربط الآليات ({CompanyReportKpis.FormatPercent(mechanism)})، "
            + $"ليكون المؤشر المركب العام لتقييم أداء الشركة ({CompanyReportKpis.FormatPercent(composite)}).";
    }
}

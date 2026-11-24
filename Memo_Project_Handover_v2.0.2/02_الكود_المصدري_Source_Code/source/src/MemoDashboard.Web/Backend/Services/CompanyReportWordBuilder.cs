using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MemoDashboard.Backend.Models;

namespace MemoDashboard.Backend.Services;

/// <summary>
/// Dependency-free OOXML Word processing document (.docx) builder for CompanyDetailed report.
/// Generates an A4 landscape, native RTL document with Syrian emblem header,
/// 26 KPI metrics, executive summary callout, factory performance, and top error fields.
/// </summary>
public static class CompanyReportWordBuilder
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string GoldHex = "B9A779";
    private const string PaleGoldHex = "EDEBE0";
    private const string BorderColorHex = "988561";
    private const string InkHex = "161616";
    private const string MutedHex = "3D3A3B";
    private const string GreenHex = "054239";
    private const string BlueHex = "428177";
    private const string RedHex = "6B1F2A";
    private const string AmberHex = "988561";
    private const string BrownHex = "260F14";

    public static byte[] Build(CompanyReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            WriteXml(archive, "[Content_Types].xml", ContentTypes());
            WriteXml(archive, "_rels/.rels", PackageRels());
            WriteXml(archive, "word/_rels/document.xml.rels", DocumentRels());
            WriteXml(archive, "word/settings.xml", Settings());
            WriteXml(archive, "word/fontTable.xml", FontTable());
            WriteXml(archive, "word/styles.xml", Styles());
            WriteXml(archive, "word/document.xml", Document(data));
        }
        return stream.ToArray();
    }

    private static void WriteXml(ZipArchive archive, string name, XElement root)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        root.Save(writer, SaveOptions.DisableFormatting);
    }

    private static XElement ContentTypes() => new(C + "Types",
        new XElement(C + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
        new XElement(C + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
        new XElement(C + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
        new XElement(C + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
        new XElement(C + "Override", new XAttribute("PartName", "/word/settings.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml")),
        new XElement(C + "Override", new XAttribute("PartName", "/word/fontTable.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml")));

    private static XElement PackageRels() => new(R + "Relationships",
        new XElement(R + "Relationship", new XAttribute("Id", "rId1"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
            new XAttribute("Target", "word/document.xml")));

    private static XElement DocumentRels() => new(R + "Relationships",
        new XElement(R + "Relationship", new XAttribute("Id", "rId1"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
            new XAttribute("Target", "styles.xml")),
        new XElement(R + "Relationship", new XAttribute("Id", "rId2"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings"),
            new XAttribute("Target", "settings.xml")),
        new XElement(R + "Relationship", new XAttribute("Id", "rId3"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable"),
            new XAttribute("Target", "fontTable.xml")));

    private static XElement Settings() => new(W + "settings",
        new XElement(W + "bidi"));

    private static XElement FontTable() => new(W + "fonts",
        new XElement(W + "font", new XAttribute(W + "name", "ITF Qomra Arabic"),
            new XElement(W + "altName", new XAttribute(W + "val", "Qomra")),
            new XElement(W + "pitch", new XAttribute(W + "val", "variable"))),
        new XElement(W + "font", new XAttribute(W + "name", "Simplified Arabic"),
            new XElement(W + "pitch", new XAttribute(W + "val", "variable"))),
        new XElement(W + "font", new XAttribute(W + "name", "Segoe UI"),
            new XElement(W + "pitch", new XAttribute(W + "val", "variable"))));

    private static XElement Styles() => new(W + "styles",
        new XElement(W + "docDefaults",
            new XElement(W + "rPrDefault",
                new XElement(W + "rPr",
                    new XElement(W + "rFonts",
                        new XAttribute(W + "ascii", "ITF Qomra Arabic"),
                        new XAttribute(W + "hAnsi", "ITF Qomra Arabic"),
                        new XAttribute(W + "cs", "ITF Qomra Arabic")),
                    new XElement(W + "sz", new XAttribute(W + "val", "20")), // 10pt
                    new XElement(W + "szCs", new XAttribute(W + "val", "20")),
                    new XElement(W + "color", new XAttribute(W + "val", InkHex)))),
            new XElement(W + "pPrDefault",
                new XElement(W + "pPr",
                    new XElement(W + "bidi"),
                    new XElement(W + "jc", new XAttribute(W + "val", "right")),
                    new XElement(W + "spacing", new XAttribute(W + "after", "80"), new XAttribute(W + "line", "260"), new XAttribute(W + "lineRule", "auto"))))),
        new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "default", "1"), new XAttribute(W + "styleId", "Normal"),
            new XElement(W + "name", new XAttribute(W + "val", "Normal")),
            new XElement(W + "qFormat")));

    private static XElement Document(CompanyReportData data)
    {
        var m = data.Metrics;
        var issue = data.Issue;
        var identity = data.Identity;

        var form = CompanyReportKpis.FormEfficiencyPercent(issue.FormEfficiencyIndex);
        var mechanism = CompanyReportKpis.MechanismUnlinkedPercent(m.EntryNotes, m.UnlinkedEntryNotes);
        var weighted = CompanyReportKpis.WeightedTimelinessPercent(m.TotalNotes, m.OnTimeNotes, m.LateNotes);
        var composite = CompanyReportKpis.OverallCompositePercent(form, m.ItemQualityPercentage, mechanism);

        var elements = new List<XElement>();

        // 1. Institution Header (3 columns table: Arabic right, Title center, English left)
        elements.Add(BuildInstitutionHeaderTable(identity));

        // Separator gold line
        elements.Add(new XElement(W + "p",
            new XElement(W + "pPr",
                new XElement(W + "pBdr",
                    new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "18"), new XAttribute(W + "space", "4"), new XAttribute(W + "color", GoldHex))),
                new XElement(W + "spacing", new XAttribute(W + "before", "40"), new XAttribute(W + "after", "80")))));

        // Metadata row (Letter number and period)
        elements.Add(BuildMetaRow(issue.LetterNumber, data.FilterSummary));

        // Report Title & Company Name
        var title = string.IsNullOrWhiteSpace(issue.Title) ? "التقرير التفصيلي لأداء الشركة" : issue.Title.Trim();
        elements.Add(new XElement(W + "p",
            new XElement(W + "pPr",
                new XElement(W + "bidi"),
                new XElement(W + "jc", new XAttribute(W + "val", "center")),
                new XElement(W + "spacing", new XAttribute(W + "before", "60"), new XAttribute(W + "after", "40"))),
            new XElement(W + "r",
                new XElement(W + "rPr",
                    new XElement(W + "b"),
                    new XElement(W + "sz", new XAttribute(W + "val", "32")), // 16pt
                    new XElement(W + "szCs", new XAttribute(W + "val", "32")),
                    new XElement(W + "color", new XAttribute(W + "val", InkHex))),
                new XElement(W + "t", title))));

        elements.Add(new XElement(W + "p",
            new XElement(W + "pPr",
                new XElement(W + "bidi"),
                new XElement(W + "jc", new XAttribute(W + "val", "center")),
                new XElement(W + "spacing", new XAttribute(W + "before", "0"), new XAttribute(W + "after", "140"))),
            new XElement(W + "r",
                new XElement(W + "rPr",
                    new XElement(W + "b"),
                    new XElement(W + "sz", new XAttribute(W + "val", "24")), // 12pt
                    new XElement(W + "szCs", new XAttribute(W + "val", "24")),
                    new XElement(W + "color", new XAttribute(W + "val", BrownHex))),
                new XElement(W + "t", "الشركة: " + data.CompanyName))));

        if (m.TotalNotes == 0)
        {
            elements.Add(new XElement(W + "p",
                new XElement(W + "pPr",
                    new XElement(W + "bidi"),
                    new XElement(W + "jc", new XAttribute(W + "val", "center")),
                    new XElement(W + "spacing", new XAttribute(W + "before", "200"), new XAttribute(W + "after", "200"))),
                new XElement(W + "r",
                    new XElement(W + "rPr",
                        new XElement(W + "color", new XAttribute(W + "val", MutedHex))),
                    new XElement(W + "t", "لا توجد بيانات للشركة المحددة ضمن الفترة المحددة."))));
        }
        else
        {
            // 2. The 26 KPI Metric Cards in a structured 6-column table
            elements.Add(BuildKpiCardsTable(m, form, mechanism, weighted, composite));

            // Spacing
            elements.Add(new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", "100")))));

            // 3. Executive Summary Box
            elements.Add(BuildExecutiveSummaryBox(data, form, mechanism, weighted, composite));

            // Spacing
            elements.Add(new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", "100")))));

            // 4. Analytical Breakdown Tables (Factory Performance & Field Errors)
            elements.Add(BuildSectionTitle("أداء المصانع التابعة للشركة"));
            elements.Add(BuildFactoryTable(data.Factories));

            elements.Add(new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", "100")))));

            elements.Add(BuildSectionTitle("البنود الأكثر تكراراً للأخطاء"));
            elements.Add(BuildFieldTable(data.FieldErrors));

            elements.Add(new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", "100")))));

            // 5. Monthly & Delay Progression
            elements.Add(BuildSectionTitle("التطور الشهري وحجم المذكرات"));
            elements.Add(BuildMonthlyTable(data.Monthly));
        }

        // 6. Official Footer
        elements.Add(new XElement(W + "p",
            new XElement(W + "pPr",
                new XElement(W + "pBdr",
                    new XElement(W + "top", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "18"), new XAttribute(W + "space", "4"), new XAttribute(W + "color", GoldHex))),
                new XElement(W + "spacing", new XAttribute(W + "before", "140"), new XAttribute(W + "after", "40")))));

        elements.Add(BuildFooterRow(data));

        // Section Properties: A4 Landscape, 15mm margins, RTL
        var sectPr = new XElement(W + "sectPr",
            new XElement(W + "pgSz",
                new XAttribute(W + "w", "16838"), // 297 mm
                new XAttribute(W + "h", "11906"), // 210 mm
                new XAttribute(W + "orient", "landscape")),
            new XElement(W + "pgMar",
                new XAttribute(W + "top", "850"),
                new XAttribute(W + "right", "850"),
                new XAttribute(W + "bottom", "850"),
                new XAttribute(W + "left", "850"),
                new XAttribute(W + "header", "450"),
                new XAttribute(W + "footer", "450"),
                new XAttribute(W + "gutter", "0")),
            new XElement(W + "bidi"));

        elements.Add(sectPr);

        return new XElement(W + "document",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
            new XElement(W + "body", elements));
    }

    private static XElement BuildSectionTitle(string text) =>
        new(W + "p",
            new XElement(W + "pPr",
                new XElement(W + "bidi"),
                new XElement(W + "jc", new XAttribute(W + "val", "right")),
                new XElement(W + "spacing", new XAttribute(W + "before", "100"), new XAttribute(W + "after", "60"))),
            new XElement(W + "r",
                new XElement(W + "rPr",
                    new XElement(W + "b"),
                    new XElement(W + "sz", new XAttribute(W + "val", "22")),
                    new XElement(W + "szCs", new XAttribute(W + "val", "22")),
                    new XElement(W + "color", new XAttribute(W + "val", BrownHex))),
                new XElement(W + "t", text)));

    private static XElement BuildInstitutionHeaderTable(CompanyReportIdentity identity)
    {
        var arLines = new[]
        {
            identity.RepublicNameArabic ?? "الجمهورية العربية السورية",
            identity.MinistryName ?? "وزارة الصناعة",
            identity.CompanyName ?? "المؤسسة العامة للصناعات الكيميائية",
            identity.AdministrativeLevelOne,
            identity.AdministrativeLevelTwo,
            identity.AdministrativeLevelThree
        }.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        var enLines = new[]
        {
            identity.RepublicNameEnglish ?? "Syrian Arab Republic",
            identity.MinistryNameEnglish ?? "Ministry of Industry",
            identity.CompanyNameEnglish ?? "General Organization for Chemical Industries",
            identity.AdministrativeLevelOneEnglish,
            identity.AdministrativeLevelTwoEnglish,
            identity.AdministrativeLevelThreeEnglish
        }.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        var table = new XElement(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "bidiVisual"),
                new XElement(W + "tblW", new XAttribute(W + "w", "15138"), new XAttribute(W + "type", "dxa")),
                new XElement(W + "tblBorders",
                    new XElement(W + "top", new XAttribute(W + "val", "none")),
                    new XElement(W + "left", new XAttribute(W + "val", "none")),
                    new XElement(W + "bottom", new XAttribute(W + "val", "none")),
                    new XElement(W + "right", new XAttribute(W + "val", "none")),
                    new XElement(W + "insideH", new XAttribute(W + "val", "none")),
                    new XElement(W + "insideV", new XAttribute(W + "val", "none")))));

        var rightCellParagraphs = arLines.Select(line =>
            new XElement(W + "p",
                new XElement(W + "pPr",
                    new XElement(W + "bidi"),
                    new XElement(W + "jc", new XAttribute(W + "val", "right")),
                    new XElement(W + "spacing", new XAttribute(W + "after", "20"), new XAttribute(W + "line", "240"), new XAttribute(W + "lineRule", "auto"))),
                new XElement(W + "r",
                    new XElement(W + "rPr",
                        new XElement(W + "b"),
                        new XElement(W + "sz", new XAttribute(W + "val", "19")),
                        new XElement(W + "szCs", new XAttribute(W + "val", "19")),
                        new XElement(W + "color", new XAttribute(W + "val", InkHex))),
                    new XElement(W + "t", line))));

        var centerCellParagraphs = new[]
        {
            new XElement(W + "p",
                new XElement(W + "pPr",
                    new XElement(W + "bidi"),
                    new XElement(W + "jc", new XAttribute(W + "val", "center")),
                    new XElement(W + "spacing", new XAttribute(W + "after", "20"))),
                new XElement(W + "r",
                    new XElement(W + "rPr",
                        new XElement(W + "b"),
                        new XElement(W + "sz", new XAttribute(W + "val", "22")),
                        new XElement(W + "szCs", new XAttribute(W + "val", "22")),
                        new XElement(W + "color", new XAttribute(W + "val", GoldHex))),
                    new XElement(W + "t", "★ الجمهورية العربية السورية ★")))
        };

        var leftCellParagraphs = enLines.Select(line =>
            new XElement(W + "p",
                new XElement(W + "pPr",
                    new XElement(W + "jc", new XAttribute(W + "val", "left")),
                    new XElement(W + "spacing", new XAttribute(W + "after", "20"), new XAttribute(W + "line", "240"), new XAttribute(W + "lineRule", "auto"))),
                new XElement(W + "r",
                    new XElement(W + "rPr",
                        new XElement(W + "rFonts", new XAttribute(W + "ascii", "Segoe UI"), new XAttribute(W + "hAnsi", "Segoe UI")),
                        new XElement(W + "sz", new XAttribute(W + "val", "18")),
                        new XElement(W + "szCs", new XAttribute(W + "val", "18")),
                        new XElement(W + "color", new XAttribute(W + "val", MutedHex))),
                    new XElement(W + "t", line))));

        table.Add(new XElement(W + "tr",
            new XElement(W + "tc",
                new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", "5300"), new XAttribute(W + "type", "dxa"))),
                rightCellParagraphs),
            new XElement(W + "tc",
                new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", "4538"), new XAttribute(W + "type", "dxa")), new XElement(W + "vAlign", new XAttribute(W + "val", "center"))),
                centerCellParagraphs),
            new XElement(W + "tc",
                new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", "5300"), new XAttribute(W + "type", "dxa"))),
                leftCellParagraphs)));

        return table;
    }

    private static XElement BuildMetaRow(string? letterNumber, string filterSummary)
    {
        var table = new XElement(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "bidiVisual"),
                new XElement(W + "tblW", new XAttribute(W + "w", "15138"), new XAttribute(W + "type", "dxa")),
                new XElement(W + "tblBorders",
                    new XElement(W + "top", new XAttribute(W + "val", "none")),
                    new XElement(W + "left", new XAttribute(W + "val", "none")),
                    new XElement(W + "bottom", new XAttribute(W + "val", "none")),
                    new XElement(W + "right", new XAttribute(W + "val", "none")),
                    new XElement(W + "insideH", new XAttribute(W + "val", "none")),
                    new XElement(W + "insideV", new XAttribute(W + "val", "none")))));

        table.Add(new XElement(W + "tr",
            new XElement(W + "tc",
                new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", "7569"), new XAttribute(W + "type", "dxa"))),
                new XElement(W + "p",
                    new XElement(W + "pPr", new XElement(W + "bidi"), new XElement(W + "jc", new XAttribute(W + "val", "right"))),
                    new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "color", new XAttribute(W + "val", MutedHex))), new XElement(W + "t", "رقم الكتاب: ")),
                    new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "b")), new XElement(W + "t", letterNumber ?? "—")))),
            new XElement(W + "tc",
                new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", "7569"), new XAttribute(W + "type", "dxa"))),
                new XElement(W + "p",
                    new XElement(W + "pPr", new XElement(W + "bidi"), new XElement(W + "jc", new XAttribute(W + "val", "left"))),
                    new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "color", new XAttribute(W + "val", MutedHex))), new XElement(W + "t", "الفترة: ")),
                    new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "b")), new XElement(W + "t", filterSummary))))));

        return table;
    }

    private sealed record KpiItem(string Title, string Value, string Color);

    private static XElement BuildKpiCardsTable(CompanyMetrics m, decimal? form, decimal? mechanism, decimal? weighted, decimal? composite)
    {
        var items = new List<KpiItem>
        {
            new("إجمالي المذكرات", CompanyReportKpis.FormatInt(m.TotalNotes), InkHex),
            new("نسبة صحة البنود", CompanyReportKpis.FormatPercent(m.ItemQualityPercentage), GreenHex),
            new("نسبة صحة المذكرات", CompanyReportKpis.FormatPercent(m.CleanReviewedNotePercent), GreenHex),
            new("مذكرات سليمة بعد المراجعة", CompanyReportKpis.FormatInt(m.CleanReviewedNotes), GreenHex),
            new("مراجعة غير مكتملة", CompanyReportKpis.FormatInt(m.IncompleteReviewNotes), AmberHex),
            new("كلها غير منطبقة", CompanyReportKpis.FormatInt(m.AllNotApplicableNoteCount), AmberHex),
            new("مؤهلة للجودة", CompanyReportKpis.FormatInt(m.QualityEligibleNoteCount), BlueHex),
            new("بها أخطاء", CompanyReportKpis.FormatInt(m.ErrorNotes), RedHex),
            new("الالتزام بالمدة", CompanyReportKpis.FormatPercent(m.TimeCompliancePercentage), BlueHex),
            new("مذكرات متأثرة بالتكرار", CompanyReportKpis.FormatInt(m.AffectedNotes), AmberHex),
            new("نسبة عدم التكرار", CompanyReportKpis.FormatPercent(m.DuplicateFreePercentage), GreenHex),
            new("حوادث التكرار", CompanyReportKpis.FormatInt(m.DuplicateIncidents), AmberHex),
            new("إعادات إرسال الشركة", CompanyReportKpis.FormatInt(m.CompanyResendIncidents), AmberHex),
            new("مذكرات الإدخال", CompanyReportKpis.FormatInt(m.EntryNotes), InkHex),
            new("إدخالات غير مربوطة", CompanyReportKpis.FormatInt(m.UnlinkedEntryNotes), RedHex),
            new("تغطية المراجعة", CompanyReportKpis.FormatPercent(m.ReviewCoveragePercentage), BlueHex),
            new("بنود غير مراجعة", CompanyReportKpis.FormatInt(m.UnreviewedItems), RedHex),
            new("بنود غير منطبقة", CompanyReportKpis.FormatInt(m.NotApplicableItems), AmberHex),
            new("بلا بنود متوقعة", CompanyReportKpis.FormatInt(m.NoExpectedItemsNoteCount), MutedHex),
            new("متوسط التأخير", CompanyReportKpis.FormatDays(m.AverageLateDays), AmberHex),
            new("أقصى تأخير", CompanyReportKpis.FormatDays(m.MaximumLateDays), RedHex),
            new("كفاءة النموذج (يدوي)", form is null ? "غير مسجل" : CompanyReportKpis.FormatPercent(form), BlueHex),
            new("نسبة التكرار الشهري", CompanyReportKpis.FormatPercent(m.AffectedNoteRatePercentage), RedHex),
            new("مؤشر كفاءة الآليات", CompanyReportKpis.FormatPercent(mechanism), (mechanism ?? 0) >= 60 ? GreenHex : BlueHex),
            new("الالتزام المرجح", CompanyReportKpis.FormatPercent(weighted), BlueHex),
            new("المؤشر المركب للشركة", CompanyReportKpis.FormatPercent(composite), GreenHex),
        };

        var table = new XElement(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "bidiVisual"),
                new XElement(W + "tblW", new XAttribute(W + "w", "15138"), new XAttribute(W + "type", "dxa")),
                new XElement(W + "tblBorders",
                    new XElement(W + "top", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "6"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)),
                    new XElement(W + "left", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "6"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)),
                    new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "6"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)),
                    new XElement(W + "right", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "6"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)),
                    new XElement(W + "insideH", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)),
                    new XElement(W + "insideV", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)))));

        const int cols = 6;
        var colWidth = (15138 / cols).ToString(CultureInfo.InvariantCulture);

        for (var i = 0; i < items.Count; i += cols)
        {
            var chunk = items.Skip(i).Take(cols).ToList();

            // Row 1: Titles
            var trTitle = new XElement(W + "tr");
            foreach (var item in chunk)
            {
                trTitle.Add(new XElement(W + "tc",
                    new XElement(W + "tcPr",
                        new XElement(W + "tcW", new XAttribute(W + "w", colWidth), new XAttribute(W + "type", "dxa")),
                        new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", PaleGoldHex)),
                        new XElement(W + "vAlign", new XAttribute(W + "val", "center"))),
                    new XElement(W + "p",
                        new XElement(W + "pPr",
                            new XElement(W + "bidi"),
                            new XElement(W + "jc", new XAttribute(W + "val", "center")),
                            new XElement(W + "spacing", new XAttribute(W + "before", "40"), new XAttribute(W + "after", "40"))),
                        new XElement(W + "r",
                            new XElement(W + "rPr",
                                new XElement(W + "b"),
                                new XElement(W + "sz", new XAttribute(W + "val", "16")), // 8pt
                                new XElement(W + "szCs", new XAttribute(W + "val", "16")),
                                new XElement(W + "color", new XAttribute(W + "val", InkHex))),
                            new XElement(W + "t", item.Title)))));
            }
            // fill remaining cells in title row if chunk < cols
            for (var k = chunk.Count; k < cols; k++)
            {
                trTitle.Add(new XElement(W + "tc",
                    new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", colWidth), new XAttribute(W + "type", "dxa"))),
                    new XElement(W + "p")));
            }
            table.Add(trTitle);

            // Row 2: Values
            var trValue = new XElement(W + "tr");
            foreach (var item in chunk)
            {
                trValue.Add(new XElement(W + "tc",
                    new XElement(W + "tcPr",
                        new XElement(W + "tcW", new XAttribute(W + "w", colWidth), new XAttribute(W + "type", "dxa")),
                        new XElement(W + "vAlign", new XAttribute(W + "val", "center"))),
                    new XElement(W + "p",
                        new XElement(W + "pPr",
                            new XElement(W + "bidi"),
                            new XElement(W + "jc", new XAttribute(W + "val", "center")),
                            new XElement(W + "spacing", new XAttribute(W + "before", "50"), new XAttribute(W + "after", "50"))),
                        new XElement(W + "r",
                            new XElement(W + "rPr",
                                new XElement(W + "b"),
                                new XElement(W + "sz", new XAttribute(W + "val", "22")), // 11pt
                                new XElement(W + "szCs", new XAttribute(W + "val", "22")),
                                new XElement(W + "color", new XAttribute(W + "val", item.Color))),
                            new XElement(W + "t", item.Value)))));
            }
            // fill remaining cells in value row if chunk < cols
            for (var k = chunk.Count; k < cols; k++)
            {
                trValue.Add(new XElement(W + "tc",
                    new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", colWidth), new XAttribute(W + "type", "dxa"))),
                    new XElement(W + "p")));
            }
            table.Add(trValue);
        }

        return table;
    }

    private static XElement BuildExecutiveSummaryBox(CompanyReportData data, decimal? form, decimal? mechanism, decimal? weighted, decimal? composite)
    {
        var m = data.Metrics;
        var text = $"بلغ إجمالي مذكرات الشركة ({CompanyReportKpis.FormatInt(m.TotalNotes)}) مذكرة، "
            + $"محققة نسبة دقة بنود بلغت ({CompanyReportKpis.FormatPercent(m.ItemQualityPercentage)})، "
            + $"ومذكرات سليمة تماماً ({CompanyReportKpis.FormatInt(m.CleanReviewedNotes)})، "
            + $"مع نسبة الالتزام بالمهلة الزمنية بلغت ({CompanyReportKpis.FormatPercent(m.TimeCompliancePercentage)})، "
            + $"ومؤشر كفاءة ربط الآليات ({CompanyReportKpis.FormatPercent(mechanism)})، "
            + $"ليكون المؤشر المركب العام لتقييم أداء الشركة ({CompanyReportKpis.FormatPercent(composite)}).";

        var table = new XElement(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "bidiVisual"),
                new XElement(W + "tblW", new XAttribute(W + "w", "15138"), new XAttribute(W + "type", "dxa")),
                new XElement(W + "tblBorders",
                    new XElement(W + "top", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "10"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", GoldHex)),
                    new XElement(W + "left", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "10"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", GoldHex)),
                    new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "10"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", GoldHex)),
                    new XElement(W + "right", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "10"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", GoldHex)),
                    new XElement(W + "insideH", new XAttribute(W + "val", "none")),
                    new XElement(W + "insideV", new XAttribute(W + "val", "none")))));

        table.Add(new XElement(W + "tr",
            new XElement(W + "tc",
                new XElement(W + "tcPr",
                    new XElement(W + "tcW", new XAttribute(W + "w", "15138"), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", PaleGoldHex))),
                new XElement(W + "p",
                    new XElement(W + "pPr",
                        new XElement(W + "bidi"),
                        new XElement(W + "jc", new XAttribute(W + "val", "right")),
                        new XElement(W + "spacing", new XAttribute(W + "before", "100"), new XAttribute(W + "after", "100"), new XAttribute(W + "line", "320"), new XAttribute(W + "lineRule", "auto"))),
                    new XElement(W + "r",
                        new XElement(W + "rPr",
                            new XElement(W + "b"),
                            new XElement(W + "sz", new XAttribute(W + "val", "21")),
                            new XElement(W + "szCs", new XAttribute(W + "val", "21")),
                            new XElement(W + "color", new XAttribute(W + "val", BrownHex))),
                        new XElement(W + "t", "الملخص التنفيذي لتقييم الأداء: ")),
                    new XElement(W + "r",
                        new XElement(W + "rPr",
                            new XElement(W + "sz", new XAttribute(W + "val", "20")),
                            new XElement(W + "szCs", new XAttribute(W + "val", "20")),
                            new XElement(W + "color", new XAttribute(W + "val", InkHex))),
                        new XElement(W + "t", text))))));

        return table;
    }

    private static XElement BuildFactoryTable(List<CompanyReportFactoryRow> rows)
    {
        var table = CreateStandardTable();
        table.Add(CreateHeaderRow("المصنع", "إجمالي المذكرات", "سليمة", "نسبة الدقة"));

        if (rows.Count == 0)
        {
            table.Add(CreateEmptyRow(4, "لا توجد مصانع مسجلة ضمن الفترة."));
        }
        else
        {
            foreach (var r in rows)
            {
                var name = !string.IsNullOrWhiteSpace(r.FactoryName) ? r.FactoryName : (!string.IsNullOrWhiteSpace(r.FactoryLabel) ? r.FactoryLabel : "غير محدد");
                var total = r.TotalNotes > 0 ? r.TotalNotes : r.Total;
                var clean = r.CleanReviewedNotes > 0 ? r.CleanReviewedNotes : r.CleanNotes;
                var pct = r.CleanReviewedNotePercent ?? r.QualityPercentage ?? (total > 0 ? (clean * 100m / total) : 0m);

                table.Add(CreateDataRow(
                    (name, false, InkHex),
                    (CompanyReportKpis.FormatInt(total), true, InkHex),
                    (CompanyReportKpis.FormatInt(clean), true, GreenHex),
                    (CompanyReportKpis.FormatPercent(pct), true, GreenHex)));
            }
        }

        return table;
    }

    private static XElement BuildFieldTable(List<CompanyReportFieldRow> rows)
    {
        var table = CreateStandardTable();
        table.Add(CreateHeaderRow("البند / المؤشر", "أخطاء", "سليم", "نسبة الخطأ"));

        if (rows.Count == 0)
        {
            table.Add(CreateEmptyRow(4, "لا توجد أخطاء مسجلة في البنود."));
        }
        else
        {
            foreach (var r in rows)
            {
                table.Add(CreateDataRow(
                    (r.FieldLabel, false, InkHex),
                    (CompanyReportKpis.FormatInt(r.ErrorItems), true, RedHex),
                    (CompanyReportKpis.FormatInt(r.CorrectItems), true, InkHex),
                    (CompanyReportKpis.FormatPercent(r.ErrorRate), true, RedHex)));
            }
        }

        return table;
    }

    private static XElement BuildMonthlyTable(List<CompanyReportMonthRow> rows)
    {
        var table = CreateStandardTable();
        table.Add(CreateHeaderRow("الفترة الشهريّة", "إجمالي المذكرات", "صحة البنود"));

        if (rows.Count == 0)
        {
            table.Add(CreateEmptyRow(3, "لا توجد حركة شهرية مسجلة."));
        }
        else
        {
            foreach (var m in rows)
            {
                var quality = m.ItemQualityPercentage ?? m.CleanReviewedNotePercent;
                table.Add(CreateDataRow(
                    (m.PeriodLabel, false, InkHex),
                    (CompanyReportKpis.FormatInt(m.TotalNotes), true, BlueHex),
                    (CompanyReportKpis.FormatPercent(quality), true, GreenHex)));
            }
        }

        return table;
    }

    private static XElement CreateStandardTable() =>
        new(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "bidiVisual"),
                new XElement(W + "tblW", new XAttribute(W + "w", "15138"), new XAttribute(W + "type", "dxa")),
                new XElement(W + "tblBorders",
                    new XElement(W + "top", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "6"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)),
                    new XElement(W + "left", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "6"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)),
                    new XElement(W + "bottom", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "6"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)),
                    new XElement(W + "right", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "6"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)),
                    new XElement(W + "insideH", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)),
                    new XElement(W + "insideV", new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", BorderColorHex)))));

    private static XElement CreateHeaderRow(params string[] headers)
    {
        var tr = new XElement(W + "tr", new XElement(W + "trPr", new XElement(W + "tblHeader")));
        var colWidth = (15138 / headers.Length).ToString(CultureInfo.InvariantCulture);

        foreach (var h in headers)
        {
            tr.Add(new XElement(W + "tc",
                new XElement(W + "tcPr",
                    new XElement(W + "tcW", new XAttribute(W + "w", colWidth), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", PaleGoldHex)),
                    new XElement(W + "vAlign", new XAttribute(W + "val", "center"))),
                new XElement(W + "p",
                    new XElement(W + "pPr",
                        new XElement(W + "bidi"),
                        new XElement(W + "jc", new XAttribute(W + "val", "center")),
                        new XElement(W + "spacing", new XAttribute(W + "before", "40"), new XAttribute(W + "after", "40"))),
                    new XElement(W + "r",
                        new XElement(W + "rPr",
                            new XElement(W + "b"),
                            new XElement(W + "sz", new XAttribute(W + "val", "18")),
                            new XElement(W + "szCs", new XAttribute(W + "val", "18")),
                            new XElement(W + "color", new XAttribute(W + "val", BrownHex))),
                        new XElement(W + "t", h)))));
        }
        return tr;
    }

    private static XElement CreateDataRow(params (string Text, bool Center, string Color)[] cells)
    {
        var tr = new XElement(W + "tr");
        var colWidth = (15138 / cells.Length).ToString(CultureInfo.InvariantCulture);

        foreach (var c in cells)
        {
            tr.Add(new XElement(W + "tc",
                new XElement(W + "tcPr",
                    new XElement(W + "tcW", new XAttribute(W + "w", colWidth), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "vAlign", new XAttribute(W + "val", "center"))),
                new XElement(W + "p",
                    new XElement(W + "pPr",
                        new XElement(W + "bidi"),
                        new XElement(W + "jc", new XAttribute(W + "val", c.Center ? "center" : "right")),
                        new XElement(W + "spacing", new XAttribute(W + "before", "40"), new XAttribute(W + "after", "40"))),
                    new XElement(W + "r",
                        new XElement(W + "rPr",
                            new XElement(W + "sz", new XAttribute(W + "val", "18")),
                            new XElement(W + "szCs", new XAttribute(W + "val", "18")),
                            new XElement(W + "color", new XAttribute(W + "val", c.Color))),
                        new XElement(W + "t", c.Text)))));
        }
        return tr;
    }

    private static XElement CreateEmptyRow(int colSpan, string message)
    {
        return new XElement(W + "tr",
            new XElement(W + "tc",
                new XElement(W + "tcPr",
                    new XElement(W + "gridSpan", new XAttribute(W + "val", colSpan)),
                    new XElement(W + "vAlign", new XAttribute(W + "val", "center"))),
                new XElement(W + "p",
                    new XElement(W + "pPr",
                        new XElement(W + "bidi"),
                        new XElement(W + "jc", new XAttribute(W + "val", "center")),
                        new XElement(W + "spacing", new XAttribute(W + "before", "80"), new XAttribute(W + "after", "80"))),
                    new XElement(W + "r",
                        new XElement(W + "rPr", new XElement(W + "color", new XAttribute(W + "val", MutedHex))),
                        new XElement(W + "t", message)))));
    }

    private static XElement BuildFooterRow(CompanyReportData data)
    {
        var lastArrival = CompanyReportKpis.FormatDate(data.LastArrivalAt);
        var issueDate = data.Issue.IssuedAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);

        var table = new XElement(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "bidiVisual"),
                new XElement(W + "tblW", new XAttribute(W + "w", "15138"), new XAttribute(W + "type", "dxa")),
                new XElement(W + "tblBorders",
                    new XElement(W + "top", new XAttribute(W + "val", "none")),
                    new XElement(W + "left", new XAttribute(W + "val", "none")),
                    new XElement(W + "bottom", new XAttribute(W + "val", "none")),
                    new XElement(W + "right", new XAttribute(W + "val", "none")),
                    new XElement(W + "insideH", new XAttribute(W + "val", "none")),
                    new XElement(W + "insideV", new XAttribute(W + "val", "none")))));

        table.Add(new XElement(W + "tr",
            new XElement(W + "tc",
                new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", "8000"), new XAttribute(W + "type", "dxa"))),
                new XElement(W + "p",
                    new XElement(W + "pPr", new XElement(W + "bidi"), new XElement(W + "jc", new XAttribute(W + "val", "right"))),
                    new XElement(W + "r",
                        new XElement(W + "rPr", new XElement(W + "sz", new XAttribute(W + "val", "16")), new XElement(W + "color", new XAttribute(W + "val", MutedHex))),
                        new XElement(W + "t", $"آخر وصول: {lastArrival} | {CompanyReportKpis.FormatInt(data.FactoryCount)} مصنع مرتبط | الدقة تعني نسبة المذكرات بلا أخطاء.")))),
            new XElement(W + "tc",
                new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", "7138"), new XAttribute(W + "type", "dxa"))),
                new XElement(W + "p",
                    new XElement(W + "pPr", new XElement(W + "bidi"), new XElement(W + "jc", new XAttribute(W + "val", "left"))),
                    new XElement(W + "r",
                        new XElement(W + "rPr", new XElement(W + "sz", new XAttribute(W + "val", "16")), new XElement(W + "color", new XAttribute(W + "val", MutedHex))),
                        new XElement(W + "t", $"صدر بتاريخ: {issueDate} | أعده: {data.Issue.IssuerName} | بواسطة {data.ApplicationName}"))))));

        return table;
    }
}

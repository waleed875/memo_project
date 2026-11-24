using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MemoDashboard.Backend.Models;

namespace MemoDashboard.Backend.Services;

/// <summary>
/// Dependency-free OOXML workbook for CompanyDetailed (single RTL sheet).
/// Same numbers as the desktop report and the server HTML/PDF.
/// </summary>
public static class CompanyReportExcelBuilder
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static byte[] Build(CompanyReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            WriteXml(archive, "[Content_Types].xml", ContentTypes());
            WriteXml(archive, "_rels/.rels", PackageRels());
            WriteXml(archive, "xl/workbook.xml", Workbook());
            WriteXml(archive, "xl/_rels/workbook.xml.rels", WorkbookRels());
            WriteXml(archive, "xl/styles.xml", Styles());
            WriteXml(archive, "xl/worksheets/sheet1.xml", Sheet(data));
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
        new XElement(C + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
        new XElement(C + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
        new XElement(C + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));

    private static XElement PackageRels() => new(R + "Relationships",
        new XElement(R + "Relationship", new XAttribute("Id", "rId1"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
            new XAttribute("Target", "xl/workbook.xml")));

    private static XElement Workbook() => new(S + "workbook",
        new XAttribute(XNamespace.Xmlns + "r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"),
        new XElement(S + "sheets",
            new XElement(S + "sheet", new XAttribute("name", "التقرير التفصيلي"), new XAttribute("sheetId", 1),
                new XAttribute(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "id", "rId1"))));

    private static XElement WorkbookRels() => new(R + "Relationships",
        new XElement(R + "Relationship", new XAttribute("Id", "rId1"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
            new XAttribute("Target", "worksheets/sheet1.xml")),
        new XElement(R + "Relationship", new XAttribute("Id", "rId2"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
            new XAttribute("Target", "styles.xml")));

    private static XElement Styles() => new(S + "styleSheet",
        new XElement(S + "numFmts", new XAttribute("count", 0)),
        new XElement(S + "fonts", new XAttribute("count", 2),
            new XElement(S + "font", new XElement(S + "sz", new XAttribute("val", 11)), new XElement(S + "name", new XAttribute("val", "ITF Qomra Arabic"))),
            new XElement(S + "font", new XElement(S + "b"), new XElement(S + "sz", new XAttribute("val", 11)), new XElement(S + "name", new XAttribute("val", "ITF Qomra Arabic")))),
        new XElement(S + "fills", new XAttribute("count", 3),
            new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "none"))),
            new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "gray125"))),
            new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "solid")),
                new XElement(S + "fgColor", new XAttribute("rgb", "FFF8F5EB")), new XElement(S + "bgColor", new XAttribute("indexed", 64)))),
        new XElement(S + "borders", new XAttribute("count", 1),
            new XElement(S + "border", new XElement(S + "left"), new XElement(S + "right"), new XElement(S + "top"), new XElement(S + "bottom"), new XElement(S + "diagonal"))),
        new XElement(S + "cellStyleXfs", new XAttribute("count", 1),
            new XElement(S + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
        new XElement(S + "cellXfs", new XAttribute("count", 3),
            Xf(0, 0, null), Xf(1, 2, "right"), Xf(0, 0, "right")),
        new XElement(S + "cellStyles", new XAttribute("count", 1),
            new XElement(S + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0))),
        new XElement(S + "dxfs", new XAttribute("count", 0)),
        new XElement(S + "tableStyles", new XAttribute("count", 0), new XAttribute("defaultTableStyle", "TableStyleMedium2"), new XAttribute("defaultPivotStyle", "PivotStyleMedium9")));

    private static XElement Xf(int fontId, int fillId, string? horizontal)
    {
        var xf = new XElement(S + "xf",
            new XAttribute("numFmtId", 0), new XAttribute("fontId", fontId),
            new XAttribute("fillId", fillId), new XAttribute("borderId", 0));
        if (horizontal is not null)
        {
            xf.Add(new XAttribute("applyAlignment", 1),
                new XElement(S + "alignment", new XAttribute("horizontal", horizontal), new XAttribute("vertical", "center"), new XAttribute("wrapText", 1)));
        }
        return xf;
    }

    private static XElement Sheet(CompanyReportData data)
    {
        var rows = new List<XElement>();
        var row = 1;
        void Text(string col, string value, int style = 2) => rows.Add(Row(row++, (col, value, style, false)));
        void Title(string value) => rows.Add(Row(row++, ("A", value, 1, false)));
        void Blank() => row++;

        var title = string.IsNullOrWhiteSpace(data.Issue.Title) ? "التقرير التفصيلي لأداء الشركة" : data.Issue.Title.Trim();
        Title(title);
        Text("A", "الشركة: " + data.CompanyName, 1);
        Text("A", data.FilterSummary);
        Text("A", "رقم الكتاب: " + data.Issue.LetterNumber);
        Blank();

        Title("مؤشرات الأداء");
        foreach (var (label, value) in KpiRows(data))
        {
            rows.Add(Row(row++, ("A", label, 2, false), ("B", value, 2, false)));
        }
        Blank();

        Title("التطور الشهري");
        rows.Add(Row(row++, ("A", "الفترة", 1, false), ("B", "المذكرات", 1, true), ("C", "صحة البنود", 1, false)));
        foreach (var m in data.Monthly)
        {
            rows.Add(Row(row++,
                ("A", m.PeriodLabel, 2, false),
                ("B", m.TotalNotes.ToString(CultureInfo.InvariantCulture), 2, true),
                ("C", CompanyReportKpis.FormatPercent(m.ItemQualityPercentage ?? m.CleanReviewedNotePercent), 2, false)));
        }
        Blank();

        Title("البنود الأكثر خطأً");
        rows.Add(Row(row++, ("A", "البند", 1, false), ("B", "عدد الأخطاء", 1, true), ("C", "مرات الفحص", 1, true), ("D", "نسبة الخطأ", 1, false)));
        foreach (var f in data.FieldErrors)
        {
            rows.Add(Row(row++,
                ("A", f.FieldLabel, 2, false),
                ("B", f.ErrorItems.ToString(CultureInfo.InvariantCulture), 2, true),
                ("C", f.CheckedItems.ToString(CultureInfo.InvariantCulture), 2, true),
                ("D", CompanyReportKpis.FormatPercent(f.ErrorRate), 2, false)));
        }
        Blank();

        Title("أداء المصانع");
        rows.Add(Row(row++, ("A", "المصنع", 1, false), ("B", "الإجمالي", 1, true), ("C", "بلا أخطاء", 1, true), ("D", "بها أخطاء", 1, true), ("E", "الدقة", 1, false), ("F", "متأخرة", 1, true)));
        foreach (var f in data.Factories)
        {
            rows.Add(Row(row++,
                ("A", f.FactoryLabel, 2, false),
                ("B", f.Total.ToString(CultureInfo.InvariantCulture), 2, true),
                ("C", f.CleanNotes.ToString(CultureInfo.InvariantCulture), 2, true),
                ("D", f.ErrorNotes.ToString(CultureInfo.InvariantCulture), 2, true),
                ("E", CompanyReportKpis.FormatPercent(f.QualityPercentage), 2, false),
                ("F", f.Late.ToString(CultureInfo.InvariantCulture), 2, true)));
        }
        Blank();
        Text("A", "آخر وصول: " + CompanyReportKpis.FormatDate(data.LastArrivalAt) + " | " + data.FactoryCount.ToString(CultureInfo.InvariantCulture) + " مصنع مرتبط");
        Text("A", "صدر بتاريخ: " + data.Issue.IssuedAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) + " | أعده: " + data.Issue.IssuerName + " | بواسطة " + data.ApplicationName);

        return new XElement(S + "worksheet",
            new XElement(S + "sheetViews",
                new XElement(S + "sheetView", new XAttribute("workbookViewId", 0), new XAttribute("rightToLeft", 1))),
            new XElement(S + "cols",
                new XElement(S + "col", new XAttribute("min", 1), new XAttribute("max", 1), new XAttribute("width", 34), new XAttribute("customWidth", 1)),
                new XElement(S + "col", new XAttribute("min", 2), new XAttribute("max", 6), new XAttribute("width", 20), new XAttribute("customWidth", 1))),
            new XElement(S + "sheetData", rows));
    }

    private static XElement Row(int row, params (string Col, string Value, int Style, bool Numeric)[] cells)
    {
        var element = new XElement(S + "row", new XAttribute("r", row));
        foreach (var (col, value, style, numeric) in cells)
        {
            element.Add(Cell(col + row, value, style, numeric));
        }
        return element;
    }

    private static XElement Cell(string reference, string value, int style, bool numeric)
    {
        var safe = value.Length > 32767 ? value[..32767] : value;
        if (numeric)
        {
            return new XElement(S + "c", new XAttribute("r", reference), new XAttribute("s", style),
                new XElement(S + "v", safe));
        }
        return new XElement(S + "c", new XAttribute("r", reference), new XAttribute("s", style), new XAttribute("t", "inlineStr"),
            new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), safe)));
    }

    private static List<(string Label, string Value)> KpiRows(CompanyReportData data)
    {
        var m = data.Metrics;
        var form = CompanyReportKpis.FormEfficiencyPercent(data.Issue.FormEfficiencyIndex);
        var mechanism = CompanyReportKpis.MechanismUnlinkedPercent(m.EntryNotes, m.UnlinkedEntryNotes);
        var weighted = CompanyReportKpis.WeightedTimelinessPercent(m.TotalNotes, m.OnTimeNotes, m.LateNotes);
        var composite = CompanyReportKpis.OverallCompositePercent(form, m.ItemQualityPercentage, mechanism);
        return new List<(string, string)>
        {
            ("إجمالي المذكرات", CompanyReportKpis.FormatInt(m.TotalNotes)),
            ("صحة البنود", CompanyReportKpis.FormatPercent(m.ItemQualityPercentage)),
            ("صحة المذكرات", CompanyReportKpis.FormatPercent(m.CleanReviewedNotePercent)),
            ("سليمة بعد المراجعة", CompanyReportKpis.FormatInt(m.CleanReviewedNotes)),
            ("مراجعة غير مكتملة", CompanyReportKpis.FormatInt(m.IncompleteReviewNotes)),
            ("كلها غير منطبقة", CompanyReportKpis.FormatInt(m.AllNotApplicableNoteCount)),
            ("مؤهلة للجودة", CompanyReportKpis.FormatInt(m.QualityEligibleNoteCount)),
            ("بها أخطاء", CompanyReportKpis.FormatInt(m.ErrorNotes)),
            ("الالتزام بالمدة", CompanyReportKpis.FormatPercent(m.TimeCompliancePercentage)),
            ("نسبة المذكرات المتأثرة", CompanyReportKpis.FormatPercent(m.AffectedNoteRatePercentage)),
            ("نسبة عدم التكرار", CompanyReportKpis.FormatPercent(m.DuplicateFreePercentage)),
            ("حوادث التكرار", CompanyReportKpis.FormatInt(m.DuplicateIncidents)),
            ("إعادات إرسال الشركة", CompanyReportKpis.FormatInt(m.CompanyResendIncidents)),
            ("مذكرات الإدخال", CompanyReportKpis.FormatInt(m.EntryNotes)),
            ("إدخالات غير مربوطة", CompanyReportKpis.FormatInt(m.UnlinkedEntryNotes)),
            ("تغطية المراجعة", CompanyReportKpis.FormatPercent(m.ReviewCoveragePercentage)),
            ("بنود غير مراجعة", CompanyReportKpis.FormatInt(m.UnreviewedItems)),
            ("بنود غير منطبقة", CompanyReportKpis.FormatInt(m.NotApplicableItems)),
            ("بلا بنود متوقعة", CompanyReportKpis.FormatInt(m.NoExpectedItemsNoteCount)),
            ("متوسط التأخير", CompanyReportKpis.FormatDays(m.AverageLateDays)),
            ("أقصى تأخير", CompanyReportKpis.FormatDays(m.MaximumLateDays)),
            ("كفاءة النموذج (يدوي)", form is null ? "غير مسجل" : CompanyReportKpis.FormatPercent(form)),
            ("مؤشر كفاءة الآليات", CompanyReportKpis.FormatPercent(mechanism)),
            ("الالتزام المرجح", CompanyReportKpis.FormatPercent(weighted)),
            ("المؤشر المركب", CompanyReportKpis.FormatPercent(composite)),
        };
    }
}

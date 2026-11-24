using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace MemoDashboard.Backend.Services;

/// <summary>
/// Writes the memo-log data set as a small, dependency-free OOXML workbook.
/// Generates byte array directly in memory for web downloads.
/// </summary>
public static class ExcelMemoLogExporter
{
    private const int ExcelCellCharacterLimit = 32767;
    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    private static readonly (string Code, string Name)[] LegacySheets =
    {
        ("ENTRY", "الإدخال"),
        ("EXIT", "الإخراج"),
        ("TRANSFER", "المناقلة"),
        ("DELIVERY", "التسليم")
    };

    private static readonly (string Code, string Name)[] BusinessSheets =
    {
        ("SUMMARY", "ملخص عام"),
        ("COMPANIES", "الشركات"),
        ("NOTES", "المذكرات"),
        ("ITEMS", "بنود المذكرات"),
        ("AUDIT", "نتائج التدقيق"),
        ("STATISTICS", "الإحصاءات"),
        ("ENTRY", "الإدخال"),
        ("EXIT", "الإخراج"),
        ("TRANSFER", "المناقلة"),
        ("DELIVERY", "التسليم")
    };

    private static readonly (string Code, string Name)[] DuplicateSheets =
    {
        ("DUPLICATES", "المذكرات المكررة")
    };

    private static IReadOnlyList<(string Code, string Name)> GetSheets(DataSet data) =>
        data.Tables.Contains("DUPLICATES")
            ? DuplicateSheets
            : data.Tables.Contains("SUMMARY") ? BusinessSheets : LegacySheets;

    private static readonly IReadOnlyDictionary<string, string> BasicHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["note_id"] = "المعرف الثابت",
            ["note_type_code"] = "رمز نوع المذكرة",
            ["note_type_name"] = "نوع المذكرة",
            ["company_id"] = "معرف الشركة",
            ["company_name"] = "الشركة",
            ["factory_name"] = "المصنع",
            ["production_line_name"] = "خط الإنتاج",
            ["note_number"] = "رقم المذكرة",
            ["linked_note_number"] = "رقم المذكرة المرتبطة",
            ["writing_date"] = "تاريخ التحرير",
            ["arrival_at"] = "تاريخ الوصول",
            ["status"] = "الحالة",
            ["created_by_name"] = "المدخل",
            ["notes"] = "ملاحظات",
            ["duplicate_reason"] = "سبب التكرار",
            ["incident_id"] = "معرف الحادثة",
            ["incident_type"] = "نوع الحادثة",
            ["incident_classification"] = "رمز الحادثة",
            ["incident_created_at"] = "تاريخ ووقت الحادثة",
            ["affected_note_id"] = "معرف المذكرة المتأثرة",
            ["affected_note_number"] = "رقم المذكرة المتأثرة",
            ["affected_note_number_value"] = "القيمة الرقمية للرقم",
            ["matched_note_id"] = "معرف المذكرة المطابقة",
            ["matched_note_number"] = "رقم المذكرة المطابقة",
            ["created_note_id"] = "معرف المذكرة المنشأة",
            ["created_note_number"] = "رقم المذكرة المنشأة",
            ["incident_user"] = "مسجل الحادثة",
            ["incident_metadata"] = "البيانات الوصفية",
            ["date_basis"] = "أساس التاريخ",
            ["period_from"] = "بداية الفترة",
            ["period_to"] = "نهاية الفترة",
            ["arrival_cutoff_date"] = "قطع تاريخ الوصول"
        };

    public static byte[] ExportTableToBytes(DataTable table, string sheetCode = "DUPLICATES")
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetCode);

        DataTable copy = table.Copy();
        copy.TableName = sheetCode;
        DataSet data = new();
        data.Tables.Add(copy);
        return ExportToBytes(data);
    }

    public static byte[] ExportToBytes(DataSet data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteContentTypes(archive, data);
            WritePackageRelationships(archive);
            WriteWorkbook(archive, data);
            WriteWorkbookRelationships(archive, data);
            WriteStyles(archive);

            var sheets = GetSheets(data);
            for (var index = 0; index < sheets.Count; index++)
            {
                DataTable? table = data.Tables[sheets[index].Code];
                WriteWorksheet(archive, index + 1, sheets[index].Name, table, index == 0);
            }
        }

        return stream.ToArray();
    }

    private static void WriteWorksheet(
        ZipArchive archive,
        int sheetNumber,
        string sheetName,
        DataTable? table,
        bool selected)
    {
        DataTable source = table ?? new DataTable();
        XElement sheetData = new(SpreadsheetNamespace + "sheetData");

        if (source.Columns.Count > 0)
        {
            XElement header = new(SpreadsheetNamespace + "row", new XAttribute("r", 1));
            for (var columnIndex = 0; columnIndex < source.Columns.Count; columnIndex++)
            {
                DataColumn column = source.Columns[columnIndex];
                string caption = BasicHeaders.TryGetValue(column.ColumnName, out string? basicHeader)
                    ? basicHeader
                    : string.IsNullOrWhiteSpace(column.Caption) ? column.ColumnName : column.Caption;
                header.Add(InlineStringCell(CellReference(columnIndex, 1), caption, style: 1));
            }
            sheetData.Add(header);
        }

        for (var rowIndex = 0; rowIndex < source.Rows.Count; rowIndex++)
        {
            DataRow row = source.Rows[rowIndex];
            XElement outputRow = new(
                SpreadsheetNamespace + "row",
                new XAttribute("r", rowIndex + 2));

            for (var columnIndex = 0; columnIndex < source.Columns.Count; columnIndex++)
            {
                DataColumn column = source.Columns[columnIndex];
                string reference = CellReference(columnIndex, rowIndex + 2);
                if (IsFollowUpColumn(column))
                {
                    int value = ToFollowUpFlag(row[column]);
                    outputRow.Add(NumericCell(reference, value, value == 0 ? 4 : 5));
                }
                else if (IsNumericColumn(column) && row[column] is not DBNull)
                {
                    outputRow.Add(NumericCell(reference, row[column], style: 2));
                }
                else
                {
                    outputRow.Add(InlineStringCell(reference, FormatValue(row[column]), style: 2));
                }
            }

            sheetData.Add(outputRow);
        }

        int columnCount = Math.Max(source.Columns.Count, 1);
        XElement columns = new(
            SpreadsheetNamespace + "cols",
            new XElement(
                SpreadsheetNamespace + "col",
                new XAttribute("min", 1),
                new XAttribute("max", columnCount),
                new XAttribute("width", EstimateColumnWidth(source)),
                new XAttribute("customWidth", 1)));

        XElement worksheet = new(
            SpreadsheetNamespace + "worksheet",
            new XElement(
                SpreadsheetNamespace + "sheetViews",
                new XElement(
                    SpreadsheetNamespace + "sheetView",
                    new XAttribute("workbookViewId", 0),
                    new XAttribute("rightToLeft", 1),
                    new XAttribute("tabSelected", selected ? 1 : 0),
                    new XElement(
                        SpreadsheetNamespace + "pane",
                        new XAttribute("ySplit", 1),
                        new XAttribute("topLeftCell", "A2"),
                        new XAttribute("activePane", "bottomLeft"),
                        new XAttribute("state", "frozen")),
                    new XElement(
                        SpreadsheetNamespace + "selection",
                        new XAttribute("pane", "bottomLeft"),
                        new XAttribute("activeCell", "A2"),
                        new XAttribute("sqref", "A2")))),
            new XElement(
                SpreadsheetNamespace + "sheetFormatPr",
                new XAttribute("defaultRowHeight", 20)),
            columns,
            sheetData);
        WriteXml(archive, $"xl/worksheets/sheet{sheetNumber}.xml", worksheet);
    }

    private static void WriteWorkbook(ZipArchive archive, DataSet data)
    {
        XElement sheets = new(SpreadsheetNamespace + "sheets");
        var list = GetSheets(data);
        for (var index = 0; index < list.Count; index++)
        {
            sheets.Add(
                new XElement(
                    SpreadsheetNamespace + "sheet",
                    new XAttribute("name", list[index].Name),
                    new XAttribute("sheetId", index + 1),
                    new XAttribute(RelationshipsNamespace + "id", $"rId{index + 1}")));
        }

        XElement workbook = new(
            SpreadsheetNamespace + "workbook",
            new XAttribute(XNamespace.Xmlns + "r", RelationshipsNamespace),
            new XElement(
                SpreadsheetNamespace + "bookViews",
                new XElement(
                    SpreadsheetNamespace + "workbookView",
                    new XAttribute("activeTab", 0))),
            sheets,
            new XElement(
                SpreadsheetNamespace + "calcPr",
                new XAttribute("fullCalcOnLoad", 1),
                new XAttribute("forceFullCalc", 1)));

        WriteXml(archive, "xl/workbook.xml", workbook);
    }

    private static void WriteWorkbookRelationships(ZipArchive archive, DataSet data)
    {
        XElement relationships = new(PackageRelationshipsNamespace + "Relationships");
        var list = GetSheets(data);
        for (var index = 0; index < list.Count; index++)
        {
            relationships.Add(
                new XElement(
                    PackageRelationshipsNamespace + "Relationship",
                    new XAttribute("Id", $"rId{index + 1}"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", $"worksheets/sheet{index + 1}.xml")));
        }

        relationships.Add(
            new XElement(
                PackageRelationshipsNamespace + "Relationship",
                new XAttribute("Id", $"rId{list.Count + 1}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                new XAttribute("Target", "styles.xml")));

        WriteXml(archive, "xl/_rels/workbook.xml.rels", relationships);
    }

    private static void WritePackageRelationships(ZipArchive archive)
    {
        XElement relationships = new(
            PackageRelationshipsNamespace + "Relationships",
            new XElement(
                PackageRelationshipsNamespace + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                new XAttribute("Target", "xl/workbook.xml")));
        WriteXml(archive, "_rels/.rels", relationships);
    }

    private static void WriteContentTypes(ZipArchive archive, DataSet data)
    {
        XElement types = new(
            ContentTypesNamespace + "Types",
            new XElement(
                ContentTypesNamespace + "Default",
                new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(
                ContentTypesNamespace + "Default",
                new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            new XElement(
                ContentTypesNamespace + "Override",
                new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(
                ContentTypesNamespace + "Override",
                new XAttribute("PartName", "/xl/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));

        var list = GetSheets(data);
        for (var index = 1; index <= list.Count; index++)
        {
            types.Add(
                new XElement(
                    ContentTypesNamespace + "Override",
                    new XAttribute("PartName", $"/xl/worksheets/sheet{index}.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        }

        WriteXml(archive, "[Content_Types].xml", types);
    }

    private static void WriteStyles(ZipArchive archive)
    {
        XElement styleSheet = new(
            SpreadsheetNamespace + "styleSheet",
            new XElement(
                SpreadsheetNamespace + "numFmts",
                new XAttribute("count", 0)),
            new XElement(
                SpreadsheetNamespace + "fonts",
                new XAttribute("count", 2),
                new XElement(
                    SpreadsheetNamespace + "font",
                    new XElement(SpreadsheetNamespace + "sz", new XAttribute("val", 10)),
                    new XElement(SpreadsheetNamespace + "name", new XAttribute("val", "Simplified Arabic"))),
                new XElement(
                    SpreadsheetNamespace + "font",
                    new XElement(SpreadsheetNamespace + "b"),
                    new XElement(SpreadsheetNamespace + "sz", new XAttribute("val", 10)),
                    new XElement(SpreadsheetNamespace + "name", new XAttribute("val", "Simplified Arabic")))),
            new XElement(
                SpreadsheetNamespace + "fills",
                new XAttribute("count", 5),
                new XElement(
                    SpreadsheetNamespace + "fill",
                    new XElement(SpreadsheetNamespace + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(
                    SpreadsheetNamespace + "fill",
                    new XElement(SpreadsheetNamespace + "patternFill", new XAttribute("patternType", "gray125"))),
                SolidFill("FFD9EAF7"),
                SolidFill("FFC6EFCE"),
                SolidFill("FFFFC7CE")),
            new XElement(
                SpreadsheetNamespace + "borders",
                new XAttribute("count", 1),
                new XElement(
                    SpreadsheetNamespace + "border",
                    new XElement(SpreadsheetNamespace + "left"),
                    new XElement(SpreadsheetNamespace + "right"),
                    new XElement(SpreadsheetNamespace + "top"),
                    new XElement(SpreadsheetNamespace + "bottom"),
                    new XElement(SpreadsheetNamespace + "diagonal"))),
            new XElement(
                SpreadsheetNamespace + "cellStyleXfs",
                new XAttribute("count", 1),
                new XElement(
                    SpreadsheetNamespace + "xf",
                    new XAttribute("numFmtId", 0),
                    new XAttribute("fontId", 0),
                    new XAttribute("fillId", 0),
                    new XAttribute("borderId", 0))),
            new XElement(
                SpreadsheetNamespace + "cellXfs",
                new XAttribute("count", 6),
                CellFormat(fontId: 0, fillId: 0),
                CellFormat(fontId: 1, fillId: 2, horizontal: "right"),
                CellFormat(fontId: 0, fillId: 0, horizontal: "right"),
                CellFormat(fontId: 0, fillId: 2, horizontal: "right"),
                CellFormat(fontId: 0, fillId: 3, horizontal: "center"),
                CellFormat(fontId: 0, fillId: 4, horizontal: "center")),
            new XElement(
                SpreadsheetNamespace + "cellStyles",
                new XAttribute("count", 1),
                new XElement(
                    SpreadsheetNamespace + "cellStyle",
                    new XAttribute("name", "Normal"),
                    new XAttribute("xfId", 0),
                    new XAttribute("builtinId", 0))),
            new XElement(SpreadsheetNamespace + "dxfs", new XAttribute("count", 0)),
            new XElement(
                SpreadsheetNamespace + "tableStyles",
                new XAttribute("count", 0),
                new XAttribute("defaultTableStyle", "TableStyleMedium2"),
                new XAttribute("defaultPivotStyle", "PivotStyleMedium9")));

        WriteXml(archive, "xl/styles.xml", styleSheet);
    }

    private static XElement SolidFill(string color) =>
        new(
            SpreadsheetNamespace + "fill",
            new XElement(
                SpreadsheetNamespace + "patternFill",
                new XAttribute("patternType", "solid"),
                new XElement(SpreadsheetNamespace + "fgColor", new XAttribute("rgb", color)),
                new XElement(SpreadsheetNamespace + "bgColor", new XAttribute("indexed", 64))));

    private static XElement CellFormat(int fontId, int fillId, string? horizontal = null)
    {
        XElement format = new(
            SpreadsheetNamespace + "xf",
            new XAttribute("numFmtId", 0),
            new XAttribute("fontId", fontId),
            new XAttribute("fillId", fillId),
            new XAttribute("borderId", 0));

        if (horizontal is not null)
        {
            format.Add(
                new XAttribute("applyAlignment", 1),
                new XElement(
                    SpreadsheetNamespace + "alignment",
                    new XAttribute("horizontal", horizontal),
                    new XAttribute("vertical", "center"),
                    new XAttribute("wrapText", 1)));
        }

        return format;
    }

    private static XElement InlineStringCell(string reference, string value, int style)
    {
        string safeValue = SanitizeAndValidate(value);
        return new XElement(
            SpreadsheetNamespace + "c",
            new XAttribute("r", reference),
            new XAttribute("s", style),
            new XAttribute("t", "inlineStr"),
            new XElement(
                SpreadsheetNamespace + "is",
                new XElement(
                    SpreadsheetNamespace + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    safeValue)));
    }

    private static XElement NumericCell(string reference, object value, int style) =>
        new(
            SpreadsheetNamespace + "c",
            new XAttribute("r", reference),
            new XAttribute("s", style),
            new XElement(SpreadsheetNamespace + "v", Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0"));

    private static bool IsFollowUpColumn(DataColumn column) =>
        column.DataType == typeof(int)
        && column.ColumnName.StartsWith("field_", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumericColumn(DataColumn column) =>
        column.DataType == typeof(byte)
        || column.DataType == typeof(short)
        || column.DataType == typeof(int)
        || column.DataType == typeof(long)
        || column.DataType == typeof(float)
        || column.DataType == typeof(double)
        || column.DataType == typeof(decimal);

    private static int ToFollowUpFlag(object value)
    {
        if (value is DBNull || value is null)
        {
            return 1;
        }

        return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 0 ? 0 : 1;
    }

    private static string FormatValue(object value)
    {
        if (value is DBNull || value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly timeOnly => timeOnly.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string SanitizeAndValidate(string value)
    {
        StringBuilder builder = new(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (char.IsHighSurrogate(current))
            {
                if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    builder.Append(current);
                    builder.Append(value[++index]);
                }
                continue;
            }

            if (char.IsLowSurrogate(current))
            {
                continue;
            }

            if (current == '\t' || current == '\n' || current == '\r'
                || current is >= '\u0020' and <= '\uD7FF'
                || current is >= '\uE000' and <= '\uFFFD')
            {
                builder.Append(current);
            }
        }

        if (builder.Length > ExcelCellCharacterLimit)
        {
            throw new InvalidDataException(
                $"لا يمكن تصدير خلية يتجاوز طولها حد Excel البالغ {ExcelCellCharacterLimit} محرفًا.");
        }

        return builder.ToString();
    }

    private static double EstimateColumnWidth(DataTable source)
    {
        if (source.Columns.Count == 0) return 20;
        var maxHeader = 0;
        foreach (DataColumn column in source.Columns)
        {
            string caption = BasicHeaders.TryGetValue(column.ColumnName, out string? basicHeader)
                ? basicHeader
                : string.IsNullOrWhiteSpace(column.Caption) ? column.ColumnName : column.Caption;
            maxHeader = Math.Max(maxHeader, caption.Length);
        }

        return Math.Clamp(maxHeader * 1.8 + 6, 16, 42);
    }

    private static string CellReference(int zeroBasedColumn, int row)
    {
        var column = zeroBasedColumn + 1;
        StringBuilder letters = new();
        while (column > 0)
        {
            column--;
            letters.Insert(0, (char)('A' + (column % 26)));
            column /= 26;
        }

        return $"{letters}{row}";
    }

    private static void WriteXml(ZipArchive archive, string name, XElement root)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        XDocument document = new(new XDeclaration("1.0", "UTF-8", "yes"), root);
        document.Save(stream, SaveOptions.DisableFormatting);
    }
}

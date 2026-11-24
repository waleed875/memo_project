using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace MemoDashboard.Backend.Services;

/// <summary>
/// Dependency-free OOXML workbook generator for generic tabular reports (single RTL sheet).
/// </summary>
public static class GenericTabularReportExcelBuilder
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static byte[] Build(string reportTitle, DataTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            WriteXml(archive, "[Content_Types].xml", ContentTypes());
            WriteXml(archive, "_rels/.rels", PackageRels());
            WriteXml(archive, "xl/workbook.xml", Workbook(reportTitle));
            WriteXml(archive, "xl/_rels/workbook.xml.rels", WorkbookRels());
            WriteXml(archive, "xl/styles.xml", Styles());
            WriteXml(archive, "xl/worksheets/sheet1.xml", Sheet(reportTitle, table));
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

    private static XElement Workbook(string sheetName) => new(S + "workbook",
        new XAttribute(XNamespace.Xmlns + "r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"),
        new XElement(S + "sheets",
            new XElement(S + "sheet", new XAttribute("name", EscapeSheetName(sheetName)), new XAttribute("sheetId", 1),
                new XAttribute(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "id", "rId1"))));

    private static string EscapeSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "التقرير";
        var clean = name.Replace("/", "-").Replace("\\", "-").Replace("?", "").Replace("*", "").Replace("[", "").Replace("]", "");
        return clean.Length > 30 ? clean[..30] : clean;
    }

    private static XElement WorkbookRels() => new(R + "Relationships",
        new XElement(R + "Relationship", new XAttribute("Id", "rId1"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
            new XAttribute("Target", "worksheets/sheet1.xml")));

    private static XElement Styles() => new(S + "styleSheet",
        new XElement(S + "fonts", new XAttribute("count", 3),
            new XElement(S + "font", new XElement(S + "sz", new XAttribute("val", 11)), new XElement(S + "name", new XAttribute("val", "Segoe UI"))),
            new XElement(S + "font", new XElement(S + "b"), new XElement(S + "sz", new XAttribute("val", 11)), new XElement(S + "name", new XAttribute("val", "Segoe UI"))),
            new XElement(S + "font", new XElement(S + "b"), new XElement(S + "sz", new XAttribute("val", 14)), new XElement(S + "name", new XAttribute("val", "Segoe UI")))),
        new XElement(S + "fills", new XAttribute("count", 3),
            new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "none"))),
            new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "gray125"))),
            new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "solid"),
                new XElement(S + "fgColor", new XAttribute("rgb", "FFE2E8F0"))))),
        new XElement(S + "borders", new XAttribute("count", 2),
            new XElement(S + "border", new XElement(S + "left"), new XElement(S + "right"), new XElement(S + "top"), new XElement(S + "bottom")),
            new XElement(S + "border",
                new XElement(S + "left", new XAttribute("style", "thin"), new XElement(S + "color", new XAttribute("rgb", "FFCBD5E1"))),
                new XElement(S + "right", new XAttribute("style", "thin"), new XElement(S + "color", new XAttribute("rgb", "FFCBD5E1"))),
                new XElement(S + "top", new XAttribute("style", "thin"), new XElement(S + "color", new XAttribute("rgb", "FFCBD5E1"))),
                new XElement(S + "bottom", new XAttribute("style", "thin"), new XElement(S + "color", new XAttribute("rgb", "FFCBD5E1"))))),
        new XElement(S + "cellXfs", new XAttribute("count", 4),
            new XElement(S + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 1), new XAttribute("applyBorder", 1)),
            new XElement(S + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 1), new XAttribute("fillId", 2), new XAttribute("borderId", 1), new XAttribute("applyFont", 1), new XAttribute("applyFill", 1), new XAttribute("applyBorder", 1),
                new XElement(S + "alignment", new XAttribute("horizontal", "center"), new XAttribute("vertical", "center"))),
            new XElement(S + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 2), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("applyFont", 1),
                new XElement(S + "alignment", new XAttribute("horizontal", "right"))),
            new XElement(S + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 1), new XAttribute("applyBorder", 1),
                new XElement(S + "alignment", new XAttribute("horizontal", "right"), new XAttribute("vertical", "center")))));

    private static XElement Sheet(string reportTitle, DataTable table)
    {
        var sheetData = new XElement(S + "sheetData");
        int rowIndex = 1;

        // Title row
        var titleRow = new XElement(S + "row", new XAttribute("r", rowIndex), new XAttribute("ht", 28), new XAttribute("customHeight", 1),
            new XElement(S + "c", new XAttribute("r", $"A{rowIndex}"), new XAttribute("t", "inlineStr"), new XAttribute("s", 2),
                new XElement(S + "is", new XElement(S + "t", reportTitle))));
        sheetData.Add(titleRow);
        rowIndex += 2;

        // Headers row
        var headerRow = new XElement(S + "row", new XAttribute("r", rowIndex), new XAttribute("ht", 24), new XAttribute("customHeight", 1));
        for (int c = 0; c < table.Columns.Count; c++)
        {
            var colLetter = ColumnLetter(c + 1);
            var headerName = table.Columns[c].Caption ?? table.Columns[c].ColumnName;
            headerRow.Add(new XElement(S + "c", new XAttribute("r", $"{colLetter}{rowIndex}"), new XAttribute("t", "inlineStr"), new XAttribute("s", 1),
                new XElement(S + "is", new XElement(S + "t", headerName))));
        }
        sheetData.Add(headerRow);
        rowIndex++;

        // Data rows
        foreach (DataRow dr in table.Rows)
        {
            var row = new XElement(S + "row", new XAttribute("r", rowIndex), new XAttribute("ht", 20), new XAttribute("customHeight", 1));
            for (int c = 0; c < table.Columns.Count; c++)
            {
                var colLetter = ColumnLetter(c + 1);
                var val = dr[c];
                var cell = new XElement(S + "c", new XAttribute("r", $"{colLetter}{rowIndex}"), new XAttribute("s", 3));

                if (val is null || val == DBNull.Value)
                {
                    cell.Add(new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", "-")));
                }
                else if (val is int or long or short or byte)
                {
                    cell.Add(new XElement(S + "v", Convert.ToString(val, CultureInfo.InvariantCulture)));
                }
                else if (val is decimal or double or float)
                {
                    cell.Add(new XElement(S + "v", Convert.ToString(val, CultureInfo.InvariantCulture)));
                }
                else if (val is bool b)
                {
                    cell.Add(new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", b ? "نعم" : "لا")));
                }
                else if (val is DateTime dt)
                {
                    cell.Add(new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))));
                }
                else if (val is DateOnly d)
                {
                    cell.Add(new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
                }
                else
                {
                    cell.Add(new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", val.ToString() ?? "")));
                }

                row.Add(cell);
            }
            sheetData.Add(row);
            rowIndex++;
        }

        var sheetView = new XElement(S + "sheetViews",
            new XElement(S + "sheetView", new XAttribute("tabSelected", 1), new XAttribute("workbookViewId", 0), new XAttribute("rightToLeft", 1)));

        return new XElement(S + "worksheet",
            new XAttribute(XNamespace.Xmlns + "r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"),
            sheetView,
            sheetData);
    }

    private static string ColumnLetter(int colIndex)
    {
        var result = string.Empty;
        while (colIndex > 0)
        {
            var modulo = (colIndex - 1) % 26;
            result = Convert.ToChar('A' + modulo) + result;
            colIndex = (colIndex - modulo) / 26;
        }
        return result;
    }
}
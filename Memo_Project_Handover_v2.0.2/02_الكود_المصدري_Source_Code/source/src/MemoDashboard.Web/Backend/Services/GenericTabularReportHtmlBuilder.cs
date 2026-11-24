using System.Data;
using System.Globalization;
using System.Net;
using System.Text;

namespace MemoDashboard.Backend.Services;

public static class GenericTabularReportHtmlBuilder
{
    public static string Build(string reportTitle, DataTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var sb = new StringBuilder();

        sb.Append("""
            <!DOCTYPE html>
            <html lang="ar" dir="rtl">
            <head>
                <meta charset="utf-8" />
                <title>
            """);
        sb.Append(WebUtility.HtmlEncode(reportTitle));
        sb.Append("""
                </title>
                <style>
                    @page {
                        size: A4 landscape;
                        margin: 15mm 10mm 15mm 10mm;
                    }
                    * {
                        box-sizing: border-box;
                        font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
                    }
                    body {
                        margin: 0;
                        padding: 16px;
                        background-color: #ffffff;
                        color: #1e293b;
                        direction: rtl;
                    }
                    .header {
                        display: flex;
                        justify-content: space-between;
                        align-items: center;
                        border-bottom: 2px solid #0f766e;
                        padding-bottom: 12px;
                        margin-bottom: 16px;
                    }
                    .header h1 {
                        margin: 0;
                        font-size: 20px;
                        color: #0f766e;
                    }
                    .meta {
                        font-size: 12px;
                        color: #64748b;
                    }
                    table {
                        width: 100%;
                        border-collapse: collapse;
                        font-size: 11px;
                    }
                    th {
                        background-color: #0f766e;
                        color: #ffffff;
                        padding: 8px 6px;
                        text-align: right;
                        font-weight: 600;
                        border: 1px solid #0d9488;
                        white-space: nowrap;
                    }
                    td {
                        padding: 6px;
                        border: 1px solid #cbd5e1;
                        text-align: right;
                    }
                    tr:nth-child(even) {
                        background-color: #f8fafc;
                    }
                    .text-center { text-align: center; }
                    .badge {
                        display: inline-block;
                        padding: 2px 6px;
                        border-radius: 4px;
                        font-size: 10px;
                    }
                </style>
            </head>
            <body>
                <div class="header">
                    <div>
                        <h1>
            """);
        sb.Append(WebUtility.HtmlEncode(reportTitle));
        sb.Append("""
                        </h1>
                        <div class="meta">تاريخ الاستخراج: 
            """);
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        sb.Append("""
                         | عدد السجلات: 
            """);
        sb.Append(table.Rows.Count);
        sb.Append("""
                        </div>
                    </div>
                </div>
                <table>
                    <thead>
                        <tr>
            """);

        for (int c = 0; c < table.Columns.Count; c++)
        {
            var caption = table.Columns[c].Caption ?? table.Columns[c].ColumnName;
            sb.Append("<th>").Append(WebUtility.HtmlEncode(caption)).Append("</th>");
        }

        sb.Append("""
                        </tr>
                    </thead>
                    <tbody>
            """);

        foreach (DataRow dr in table.Rows)
        {
            sb.Append("<tr>");
            for (int c = 0; c < table.Columns.Count; c++)
            {
                var val = dr[c];
                var display = val is null || val == DBNull.Value
                    ? "-"
                    : val is bool b
                        ? (b ? "نعم" : "لا")
                        : val is DateTime dt
                            ? dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                            : val is DateOnly d
                                ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                : val.ToString() ?? "";
                sb.Append("<td>").Append(WebUtility.HtmlEncode(display)).Append("</td>");
            }
            sb.Append("</tr>");
        }

        sb.Append("""
                    </tbody>
                </table>
            </body>
            </html>
            """);

        return sb.ToString();
    }
}
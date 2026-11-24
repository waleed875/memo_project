using System.Data;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using MemoDashboard.Backend.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace MemoDashboard.Backend.Endpoints;

public static class ReportsEndpoints
{
    private static readonly Dictionary<string, (string Title, string Category, string Description)> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CompanyDetailed"] = ("التقرير التفصيلي للشركة", "التقارير التفصيلية", "تقرير شامل ومفصل للشركة مع مؤشرات الأداء والترويسة الرسمية"),
        ["MemoLogExport"] = ("تصدير سجل المذكرات الشامل لمطابقة البيانات (Excel)", "سجلات المطابقة", "تصدير شامل لكافة المذكرات إلى مصنف Excel متعدد الصفحات للمطابقة اليدوية"),
        ["Duplicates"] = ("تقرير المذكرات المكررة (Excel)", "الجودة والرقابة", "تقرير تفصيلي لحوادث التكرار والمذكرات المتأثرة والمطابقة")
    };

    public static void MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports");

        // ---------------- Reports Catalog ----------------
        group.MapGet("", (IWebAdminAuthService auth, HttpContext context) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();

            var list = Catalog.Select(kv => new
            {
                key = kv.Key,
                title = kv.Value.Title,
                category = kv.Value.Category,
                description = kv.Value.Description,
                isCompanyDetailed = kv.Key.Equals("CompanyDetailed", StringComparison.OrdinalIgnoreCase)
            });

            return Results.Ok(list);
        });

        // ---------------- Report Identity Settings ----------------
        group.MapGet("/identity-settings", async (
            IReportService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();
            var settings = await service.GetReportIdentitySettingsAsync(cancellationToken);
            return Results.Ok(settings);
        });

        group.MapPut("/identity-settings", async (
            ReportIdentitySettingsModel settings,
            IReportService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!user.Role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Forbid();
            }

            await service.SaveReportIdentitySettingsAsync(settings, user.Id, cancellationToken);
            return Results.Ok(new { message = "تم حفظ إعدادات قالب التقرير بنجاح." });
        });

        // ---------------- Tabular Report Data (JSON) ----------------
        group.MapPost("/{key}/data", async (
            string key,
            ReportFilterModel filter,
            IReportService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            if (!Catalog.TryGetValue(key, out var meta))
                return Results.NotFound(new { message = "التقرير المطلوب غير موجود." });

            DataTable table;
            try
            {
                var queryKey = key.Equals("MemoLogExport", StringComparison.OrdinalIgnoreCase)
                    ? "GeneralNotes"
                    : key;
                table = await service.GetReportTableAsync(queryKey, filter, user.Id, cancellationToken);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }

            var columns = new List<object>();
            for (int i = 0; i < table.Columns.Count; i++)
            {
                var col = table.Columns[i];
                columns.Add(new
                {
                    key = col.ColumnName,
                    name = col.ColumnName,
                    caption = string.IsNullOrWhiteSpace(col.Caption) ? col.ColumnName : col.Caption,
                    type = col.DataType.Name
                });
            }

            var rows = new List<Dictionary<string, object?>>();
            foreach (DataRow row in table.Rows)
            {
                var dict = new Dictionary<string, object?>();
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var val = row[i];
                    dict[table.Columns[i].ColumnName] = val == DBNull.Value ? null : val;
                }
                rows.Add(dict);
            }

            return Results.Ok(new
            {
                reportKey = key,
                title = meta.Title,
                columns,
                rows,
                rowCount = rows.Count
            });
        });

        // ---------------- Export Excel (.xlsx) ----------------
        group.MapPost("/{key}/export/excel", async (
            string key,
            ReportFilterModel filter,
            IReportService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            if (!Catalog.TryGetValue(key, out var meta))
                return Results.NotFound(new { message = "التقرير المطلوب غير موجود." });

            byte[] bytes;
            string fileName;
            if (key.Equals("MemoLogExport", StringComparison.OrdinalIgnoreCase))
            {
                var dataSet = await service.GetMemoLogExportAsync(filter, user.Id, cancellationToken);
                bytes = ExcelMemoLogExporter.ExportToBytes(dataSet);
                fileName = $"سجل_المذكرات_الشامل_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            }
            else if (key.Equals("Duplicates", StringComparison.OrdinalIgnoreCase))
            {
                var table = await service.GetReportTableAsync("Duplicates", filter, user.Id, cancellationToken);
                bytes = ExcelMemoLogExporter.ExportTableToBytes(table, "DUPLICATES");
                fileName = $"تقرير_المذكرات_المكررة_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            }
            else
            {
                var table = await service.GetReportTableAsync(key, filter, user.Id, cancellationToken);
                bytes = GenericTabularReportExcelBuilder.Build(meta.Title, table);
                fileName = $"{key}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            }

            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                Uri.EscapeDataString(fileName));
        });

        // ---------------- Export PDF (.pdf) ----------------
        group.MapPost("/{key}/export/pdf", async (
            string key,
            ReportFilterModel filter,
            IReportService service,
            IReportPdfRenderer pdfRenderer,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            if (!Catalog.TryGetValue(key, out var meta))
                return Results.NotFound(new { message = "التقرير المطلوب غير موجود." });

            var table = await service.GetReportTableAsync(key, filter, user.Id, cancellationToken);
            var html = GenericTabularReportHtmlBuilder.Build(meta.Title, table);
            var pdfBytes = await pdfRenderer.RenderPdfAsync(html, cancellationToken);
            var fileName = $"{key}_{DateTime.Now:yyyyMMdd_HHmm}.pdf";

            return Results.File(pdfBytes, "application/pdf", fileName);
        });

        // ---------------- Export HTML ----------------
        group.MapPost("/{key}/export/html", async (
            string key,
            ReportFilterModel filter,
            IReportService service,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();

            if (!Catalog.TryGetValue(key, out var meta))
                return Results.NotFound(new { message = "التقرير المطلوب غير موجود." });

            var table = await service.GetReportTableAsync(key, filter, user.Id, cancellationToken);
            var html = GenericTabularReportHtmlBuilder.Build(meta.Title, table);

            return Results.Content(html, "text/html; charset=utf-8");
        });
    }
}
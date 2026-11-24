using MemoDashboard.Backend.Models;
using MemoDashboard.Backend.Services;

namespace MemoDashboard.Backend.Endpoints;

public static class CompanyReportEndpoints
{
    public static void MapCompanyReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard")
            .RequireRateLimiting("dashboard");

        group.MapGet("/company-monthly", async (
            CompanyReportService reports,
            IWebAdminAuthService auth,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken,
            Guid? companyId = null,
            DateTime? from = null,
            DateTime? to = null,
            DateTime? entryCutoffDate = null,
            string? dateBasis = null) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";

            if (auth.GetCurrent(context) is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var rows = await reports.GetMonthlyAsync(
                    companyId ?? Guid.Empty, from?.Date, to?.Date, entryCutoffDate?.Date,
                    dateBasis ?? "WritingDate", cancellationToken);
                return Results.Json(rows);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: ex.Message,
                    extensions: new Dictionary<string, object?> { ["code"] = "company_monthly_invalid" });
            }
            catch (InvalidOperationException ex)
            {
                loggerFactory.CreateLogger(typeof(CompanyReportEndpoints))
                    .LogWarning(ex, "Company monthly unavailable.");
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "تعذر الوصول إلى قاعدة البيانات",
                    extensions: new Dictionary<string, object?> { ["code"] = "database_unavailable" });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(typeof(CompanyReportEndpoints))
                    .LogError(ex, "Company monthly failed.");
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "تعذر تحميل التطور الشهري.",
                    extensions: new Dictionary<string, object?> { ["code"] = "company_monthly_failed" });
            }
        });

        group.MapGet("/company-report.html", async (
            CompanyReportService reports,
            IWebAdminAuthService auth,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken,
            Guid? companyId = null,
            DateTime? from = null,
            DateTime? to = null,
            Guid? factoryId = null,
            Guid? productionLineId = null,
            Guid? noteTypeId = null,
            string? status = null,
            DateTime? entryCutoffDate = null,
            string? dateBasis = null,
            string? title = null,
            string? letterNumber = null,
            string? city = null,
            string? hijriDate = null,
            string? gregorianDate = null,
            string? recipient = null,
            string? issuerName = null,
            string? formEfficiency = null) =>
        {
            if (auth.GetCurrent(context) is null)
            {
                return Results.Unauthorized();
            }

            var build = await TryBuildAsync(
                reports, loggerFactory, companyId, from, to, factoryId, productionLineId, noteTypeId, status,
                entryCutoffDate, dateBasis, title, letterNumber, city, hijriDate, gregorianDate,
                recipient, issuerName, formEfficiency, cancellationToken);
            return build.ToResult(html => Results.Content(html, "text/html; charset=utf-8"));
        });

        group.MapGet("/company-report.pdf", async (
            CompanyReportService reports,
            IReportPdfRenderer renderer,
            IWebAdminAuthService auth,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken,
            Guid? companyId = null,
            DateTime? from = null,
            DateTime? to = null,
            Guid? factoryId = null,
            Guid? productionLineId = null,
            Guid? noteTypeId = null,
            string? status = null,
            DateTime? entryCutoffDate = null,
            string? dateBasis = null,
            string? title = null,
            string? letterNumber = null,
            string? city = null,
            string? hijriDate = null,
            string? gregorianDate = null,
            string? recipient = null,
            string? issuerName = null,
            string? formEfficiency = null) =>
        {
            if (auth.GetCurrent(context) is null)
            {
                return Results.Unauthorized();
            }

            var build = await TryBuildAsync(
                reports, loggerFactory, companyId, from, to, factoryId, productionLineId, noteTypeId, status,
                entryCutoffDate, dateBasis, title, letterNumber, city, hijriDate, gregorianDate,
                recipient, issuerName, formEfficiency, cancellationToken);
            if (!build.Succeeded)
            {
                return build.ToResult(_ => Results.Empty);
            }

            byte[] pdf;
            try
            {
                pdf = await renderer.RenderPdfAsync(build.Html!, cancellationToken);
            }
            catch (ChromiumNotAvailableException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: ex.Message,
                    extensions: new Dictionary<string, object?> { ["code"] = "chromium_unavailable" });
            }
            catch (TimeoutException ex)
            {
                loggerFactory.CreateLogger(typeof(CompanyReportEndpoints))
                    .LogWarning(ex, "Company report PDF render timed out.");
                return Results.Problem(
                    statusCode: StatusCodes.Status504GatewayTimeout,
                    title: ex.Message,
                    extensions: new Dictionary<string, object?> { ["code"] = "pdf_render_timeout" });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(typeof(CompanyReportEndpoints))
                    .LogError(ex, "Company report PDF render failed.");
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "تعذر توليد PDF للتقرير.",
                    extensions: new Dictionary<string, object?> { ["code"] = "pdf_render_failed" });
            }

            var fileName = $"CompanyDetailed-{build.Data!.CompanyName}-{build.Data.From:yyyyMMdd}-{build.Data.To:yyyyMMdd}.pdf";
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(pdf, "application/pdf", fileName);
        });

        group.MapGet("/company-report.xlsx", async (
            CompanyReportService reports,
            IWebAdminAuthService auth,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken,
            Guid? companyId = null,
            DateTime? from = null,
            DateTime? to = null,
            Guid? factoryId = null,
            Guid? productionLineId = null,
            Guid? noteTypeId = null,
            string? status = null,
            DateTime? entryCutoffDate = null,
            string? dateBasis = null,
            string? title = null,
            string? letterNumber = null,
            string? city = null,
            string? hijriDate = null,
            string? gregorianDate = null,
            string? recipient = null,
            string? issuerName = null,
            string? formEfficiency = null) =>
        {
            if (auth.GetCurrent(context) is null)
            {
                return Results.Unauthorized();
            }

            var build = await TryBuildAsync(
                reports, loggerFactory, companyId, from, to, factoryId, productionLineId, noteTypeId, status,
                entryCutoffDate, dateBasis, title, letterNumber, city, hijriDate, gregorianDate,
                recipient, issuerName, formEfficiency, cancellationToken, buildHtml: false);
            if (!build.Succeeded)
            {
                return build.ToResult(_ => Results.Empty);
            }

            byte[] xlsx;
            try
            {
                xlsx = CompanyReportExcelBuilder.Build(build.Data!);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(typeof(CompanyReportEndpoints))
                    .LogError(ex, "Company report Excel build failed.");
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "تعذر توليد Excel للتقرير.",
                    extensions: new Dictionary<string, object?> { ["code"] = "excel_build_failed" });
            }

            var fileName = $"CompanyDetailed-{build.Data!.CompanyName}-{build.Data.From:yyyyMMdd}-{build.Data.To:yyyyMMdd}.xlsx";
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(
                xlsx,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        });

        group.MapGet("/company-report.docx", async (
            CompanyReportService reports,
            IWebAdminAuthService auth,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken,
            Guid? companyId = null,
            DateTime? from = null,
            DateTime? to = null,
            Guid? factoryId = null,
            Guid? productionLineId = null,
            Guid? noteTypeId = null,
            string? status = null,
            DateTime? entryCutoffDate = null,
            string? dateBasis = null,
            string? title = null,
            string? letterNumber = null,
            string? city = null,
            string? hijriDate = null,
            string? gregorianDate = null,
            string? recipient = null,
            string? issuerName = null,
            string? formEfficiency = null) =>
        {
            if (auth.GetCurrent(context) is null)
            {
                return Results.Unauthorized();
            }

            var build = await TryBuildAsync(
                reports, loggerFactory, companyId, from, to, factoryId, productionLineId, noteTypeId, status,
                entryCutoffDate, dateBasis, title, letterNumber, city, hijriDate, gregorianDate,
                recipient, issuerName, formEfficiency, cancellationToken, buildHtml: false);
            if (!build.Succeeded)
            {
                return build.ToResult(_ => Results.Empty);
            }

            byte[] docx;
            try
            {
                docx = CompanyReportWordBuilder.Build(build.Data!);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(typeof(CompanyReportEndpoints))
                    .LogError(ex, "Company report Word build failed.");
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "تعذر توليد Word للتقرير.",
                    extensions: new Dictionary<string, object?> { ["code"] = "docx_build_failed" });
            }

            var fileName = $"CompanyDetailed-{build.Data!.CompanyName}-{build.Data.From:yyyyMMdd}-{build.Data.To:yyyyMMdd}.docx";
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(
                docx,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                fileName);
        });

        group.MapGet("/company-report.data", async (
            CompanyReportService reports,
            IWebAdminAuthService auth,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken,
            Guid? companyId = null,
            DateTime? from = null,
            DateTime? to = null,
            Guid? factoryId = null,
            Guid? productionLineId = null,
            Guid? noteTypeId = null,
            string? status = null,
            DateTime? entryCutoffDate = null,
            string? dateBasis = null,
            string? title = null,
            string? letterNumber = null,
            string? city = null,
            string? hijriDate = null,
            string? gregorianDate = null,
            string? recipient = null,
            string? issuerName = null,
            string? formEfficiency = null) =>
        {
            if (auth.GetCurrent(context) is null)
            {
                return Results.Unauthorized();
            }

            var build = await TryBuildAsync(
                reports, loggerFactory, companyId, from, to, factoryId, productionLineId, noteTypeId, status,
                entryCutoffDate, dateBasis, title, letterNumber, city, hijriDate, gregorianDate,
                recipient, issuerName, formEfficiency, cancellationToken, buildHtml: false);
            if (!build.Succeeded)
            {
                return build.ToResult(_ => Results.Empty);
            }

            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(build.Data);
        });
    }

    private sealed class BuildOutcome
    {
        public bool Succeeded { get; init; }

        public string? Html { get; init; }

        public CompanyReportData? Data { get; init; }

        public IResult? Error { get; init; }

        public IResult ToResult(Func<string, IResult> ok) => Succeeded ? ok(Html!) : Error!;
    }

    private static async Task<BuildOutcome> TryBuildAsync(
        CompanyReportService reports,
        ILoggerFactory loggerFactory,
        Guid? companyId,
        DateTime? from,
        DateTime? to,
        Guid? factoryId,
        Guid? productionLineId,
        Guid? noteTypeId,
        string? status,
        DateTime? entryCutoffDate,
        string? dateBasis,
        string? title,
        string? letterNumber,
        string? city,
        string? hijriDate,
        string? gregorianDate,
        string? recipient,
        string? issuerName,
        string? formEfficiency,
        CancellationToken cancellationToken,
        bool buildHtml = true)
    {
        var logger = loggerFactory.CreateLogger(typeof(CompanyReportEndpoints));
        var effectiveTo = (to ?? DashboardQueryService.GetBusinessNow()).Date;
        var effectiveFrom = (from ?? new DateTime(effectiveTo.Year, 1, 1)).Date;
        if (effectiveFrom > effectiveTo)
        {
            (effectiveFrom, effectiveTo) = (effectiveTo, effectiveFrom);
        }

        CompanyReportData data;
        try
        {
            data = await reports.GetAsync(new CompanyReportQuery
            {
                CompanyId = companyId,
                From = effectiveFrom,
                To = effectiveTo,
                DateBasis = dateBasis ?? "WritingDate",
                EntryCutoffDate = entryCutoffDate?.Date,
                FactoryId = factoryId,
                ProductionLineId = productionLineId,
                NoteTypeId = noteTypeId,
                Status = status
            }, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return new BuildOutcome
            {
                Error = Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: ex.Message,
                    extensions: new Dictionary<string, object?> { ["code"] = "company_report_invalid" })
            };
        }
        catch (InvalidOperationException ex)
        {
            // Only missing database configuration reaches here; every
            // request-validation failure above is an ArgumentException.
            logger.LogWarning(ex, "Company report database unavailable.");
            return new BuildOutcome
            {
                Error = Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: ex.Message,
                    extensions: new Dictionary<string, object?> { ["code"] = "database_unavailable" })
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Company report query failed.");
            return new BuildOutcome
            {
                Error = Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "تعذر تحميل بيانات تقرير الشركة.",
                    extensions: new Dictionary<string, object?> { ["code"] = "company_report_failed" })
            };
        }

        data.Issue = new CompanyReportIssue
        {
            Title = title?.Trim() ?? string.Empty,
            LetterNumber = letterNumber?.Trim() ?? string.Empty,
            City = string.IsNullOrWhiteSpace(city) ? data.Identity.DefaultCity : city.Trim(),
            HijriDate = hijriDate?.Trim() ?? string.Empty,
            GregorianDate = gregorianDate?.Trim() ?? string.Empty,
            Recipient = recipient?.Trim() ?? string.Empty,
            IssuerName = issuerName?.Trim() ?? string.Empty,
            IssuedAt = DashboardQueryService.GetBusinessNow(),
            FormEfficiencyIndex = CompanyReportKpis.ParseFormEfficiency(formEfficiency)
        };

        string? html = null;
        if (buildHtml)
        {
            html = CompanyReportHtmlBuilder.BuildHtml(data, ReportAssetLoader.Load());
        }
        return new BuildOutcome { Succeeded = true, Html = html, Data = data };
    }
}

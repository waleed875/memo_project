using MemoDashboard.Backend.Models;
using MemoDashboard.Backend.Services;
using Npgsql;

namespace MemoDashboard.Backend.Endpoints;

public static class DashboardEndpoints
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        "DRAFT",
        "NEEDS_REVIEW",
        "UNDER_REVIEW",
        "HAS_ERRORS",
        "COMPLETE",
        "APPROVED",
        "RETURNED_FOR_CORRECTION"
    };

    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard")
            .RequireRateLimiting("dashboard");

        group.MapGet("/snapshot", async (
            DashboardQueryService service,
            IWebAdminAuthService auth,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken,
            DateTime? from = null,
            DateTime? to = null,
            Guid? companyId = null,
            Guid? factoryId = null,
            Guid? productionLineId = null,
            Guid? noteTypeId = null,
            string? status = null,
            DateTime? entryCutoffDate = null,
            string? dateBasis = null,
            int minSample = 10) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";

            if (auth.GetCurrent(context) is null)
            {
                return Results.Unauthorized();
            }


            var effectiveTo = to?.Date;
            var effectiveFrom = from?.Date;
            if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveFrom > effectiveTo)
            {
                return Results.BadRequest(new { message = "يجب ألا يكون تاريخ البداية بعد تاريخ النهاية." });
            }
            if (minSample is < 1 or > 10_000)
            {
                return Results.BadRequest(new { message = "حجم العينة الأدنى غير صالح." });
            }

            var normalizedStatus = string.IsNullOrWhiteSpace(status)
                ? null
                : status.Trim().ToUpperInvariant();
            if (normalizedStatus is not null && !AllowedStatuses.Contains(normalizedStatus))
            {
                return Results.BadRequest(new { message = "حالة المذكرة غير صالحة." });
            }

            var normalizedDateBasis = string.Equals(dateBasis, "ArrivalDate", StringComparison.OrdinalIgnoreCase)
                ? "ArrivalDate"
                : "WritingDate";

            var filter = new FilterQuery
            {
                From = effectiveFrom,
                To = effectiveTo,
                DateBasis = normalizedDateBasis,
                CompanyId = companyId,
                FactoryId = factoryId,
                ProductionLineId = productionLineId,
                NoteTypeId = noteTypeId,
                Status = normalizedStatus,
                EntryCutoffDate = entryCutoffDate?.Date
            };

            var incomingCorrelationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
            var correlationId = Guid.TryParse(incomingCorrelationId, out var parsedCorrelationId)
                ? parsedCorrelationId.ToString("D")
                : Guid.NewGuid().ToString("N");
            context.Response.Headers["X-Correlation-ID"] = correlationId;
            var logger = loggerFactory.CreateLogger(typeof(DashboardEndpoints));
            using var logScope = logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });

            try
            {
                var snapshot = await service.GetSnapshotAsync(filter, minSample, cancellationToken);
                return Results.Json(snapshot);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Dashboard snapshot rejected. CorrelationId={CorrelationId}", correlationId);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "طلب لوحة المؤشرات غير صالح",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "snapshot_request_invalid",
                        ["correlationId"] = correlationId
                    });
            }
            catch (NpgsqlException ex)
            {
                var sqlState = (ex as PostgresException)?.SqlState;
                var databaseUnavailable = ex is not PostgresException
                    || (sqlState?.StartsWith("08", StringComparison.Ordinal) ?? false)
                    || sqlState is "57P01" or "57P02" or "57P03";
                var statusCode = databaseUnavailable
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status500InternalServerError;
                var code = databaseUnavailable ? "database_unavailable" : "snapshot_query_failed";
                var title = databaseUnavailable
                    ? "الخادم متصل، لكن تعذر الوصول إلى قاعدة البيانات"
                    : "تعذر تحميل بيانات لوحة المؤشرات";
                logger.LogError(ex, "Dashboard snapshot failed. CorrelationId={CorrelationId} SqlState={SqlState} StatusCode={StatusCode}", correlationId, sqlState, statusCode);
                return Results.Problem(
                    statusCode: statusCode,
                    title: title,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = code,
                        ["correlationId"] = correlationId
                    });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dashboard snapshot failed. CorrelationId={CorrelationId} StatusCode=500", correlationId);
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "تعذر تحميل بيانات لوحة المؤشرات",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "snapshot_query_failed",
                        ["correlationId"] = correlationId
                    });
            }
        });

        group.MapGet("/company-metrics", async (
            Guid companyId,
            ICompanyMetricsService service,
            IWebAdminAuthService auth,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken,
            DateTime? from = null,
            DateTime? to = null,
            DateTime? entryCutoffDate = null,
            string? dateBasis = null) =>
        {
            if (auth.GetCurrent(context) is null)
            {
                return Results.Unauthorized();
            }


            var effectiveTo = to?.Date;
            var effectiveFrom = from?.Date;
            if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveFrom > effectiveTo)
            {
                return Results.BadRequest(new { message = "يجب ألا يكون تاريخ البداية بعد تاريخ النهاية." });
            }

            try
            {
                return Results.Ok(await service.GetAsync(
                    companyId, effectiveFrom, effectiveTo, entryCutoffDate,
                    string.Equals(dateBasis, "ArrivalDate", StringComparison.OrdinalIgnoreCase) ? "ArrivalDate" : "WritingDate",
                    cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(typeof(DashboardEndpoints))
                    .LogWarning(ex, "تعذر تحميل عقد مؤشرات الشركة.");
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "تعذر الوصول إلى قاعدة البيانات");
            }
        });
        group.MapGet("/lookups", async (
            DashboardQueryService service,
            IWebAdminAuthService auth,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (auth.GetCurrent(context) is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await service.GetLookupsAsync(cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(typeof(DashboardEndpoints))
                    .LogWarning(ex, "تعذر تحميل قوائم لوحة المتابعة.");
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "تعذر الوصول إلى قاعدة البيانات");
            }
        });

        group.MapGet("/review-queue", async (
            DashboardQueryService service,
            IWebAdminAuthService auth,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken,
            DateTime? from = null,
            DateTime? to = null,
            Guid? companyId = null,
            Guid? factoryId = null,
            Guid? productionLineId = null,
            Guid? noteTypeId = null,
            DateTime? entryCutoffDate = null,
            string? dateBasis = null,
            int limit = 50,
            int offset = 0) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";

            if (auth.GetCurrent(context) is null)
            {
                return Results.Unauthorized();
            }

            var effectiveTo = to?.Date;
            var effectiveFrom = from?.Date;
            if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveFrom > effectiveTo)
            {
                return Results.BadRequest(new { message = "يجب ألا يكون تاريخ البداية بعد تاريخ النهاية." });
            }
            // Local LAN tool: silently clamp paging instead of rejecting it.
            limit = Math.Clamp(limit, 1, 200);
            offset = Math.Max(0, offset);

            var filter = new FilterQuery
            {
                From = effectiveFrom,
                To = effectiveTo,
                DateBasis = string.Equals(dateBasis, "ArrivalDate", StringComparison.OrdinalIgnoreCase)
                    ? "ArrivalDate"
                    : "WritingDate",
                CompanyId = companyId,
                FactoryId = factoryId,
                ProductionLineId = productionLineId,
                NoteTypeId = noteTypeId,
                Status = null,
                EntryCutoffDate = entryCutoffDate?.Date
            };

            var incomingCorrelationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
            var correlationId = Guid.TryParse(incomingCorrelationId, out var parsedCorrelationId)
                ? parsedCorrelationId.ToString("D")
                : Guid.NewGuid().ToString("N");
            context.Response.Headers["X-Correlation-ID"] = correlationId;

            try
            {
                var result = await service.GetReviewQueueAsync(filter, limit, offset, cancellationToken);
                return Results.Json(result);
            }
            catch (ArgumentException ex)
            {
                loggerFactory.CreateLogger(typeof(DashboardEndpoints))
                    .LogWarning(ex, "Review queue rejected. CorrelationId={CorrelationId}", correlationId);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "طلب طابور المراجعة غير صالح",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "review_queue_invalid",
                        ["correlationId"] = correlationId
                    });
            }
            catch (InvalidOperationException ex)
            {
                loggerFactory.CreateLogger(typeof(DashboardEndpoints))
                    .LogWarning(ex, "تعذر تحميل طابور المراجعة. CorrelationId={CorrelationId}", correlationId);
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "تعذر الوصول إلى قاعدة البيانات",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "database_unavailable",
                        ["correlationId"] = correlationId
                    });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(typeof(DashboardEndpoints))
                    .LogError(ex, "Review queue failed. CorrelationId={CorrelationId}", correlationId);
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "تعذر تحميل طابور المراجعة",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "review_queue_failed",
                        ["correlationId"] = correlationId
                    });
            }
        });
    }
}

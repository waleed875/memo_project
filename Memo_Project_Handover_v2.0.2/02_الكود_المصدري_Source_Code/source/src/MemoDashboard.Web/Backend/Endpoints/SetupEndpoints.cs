using MemoDashboard.Backend.Models;
using MemoDashboard.Backend.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace MemoDashboard.Backend.Endpoints;

public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup")
            .RequireRateLimiting("setup-read");

        group.MapGet("/status", (
            ConfigurationService configurationService,
            SetupAccessService access) =>
        {
            return Results.Ok(new
            {
                isConfigured = configurationService.HasConfiguration,
                internalNetworkSetup = access.InternalNetworkSetupEnabled
            });
        });

        group.MapGet("/config", (
            HttpContext context,
            ConfigurationService configurationService,
            SetupAccessService access,
            IWebAdminAuthService adminAuth) =>
        {
            if (!access.IsAllowed(context, configurationService.HasConfiguration, adminAuth))
            {
                return Results.Json(new { message = SetupDeniedMessage(configurationService.HasConfiguration) }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var info = configurationService.GetConfigurationInfo();
            return info is null
                ? Results.NotFound(new { message = "لم يتم حفظ الإعدادات بعد." })
                : Results.Ok(info);
        });

        group.MapPost("/test-connection", async (
            SetupRequest request,
            HttpContext context,
            ConfigurationService configurationService,
            SetupAccessService access,
            IWebAdminAuthService adminAuth,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح. أعد تحميل صفحة الإعداد." });
            if (!access.IsAllowed(context, configurationService.HasConfiguration, adminAuth))
            {
                return Results.Json(new { message = SetupDeniedMessage(configurationService.HasConfiguration) }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var validationError = Validate(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { message = validationError });
            }

            var result = await configurationService.TestConnectionAsync(request, cancellationToken);
            return Results.Json(result);
        }).RequireRateLimiting("setup");

        group.MapPost("/save", async (
            SetupRequest request,
            HttpContext context,
            ConfigurationService configurationService,
            SetupAccessService access,
            IWebAdminAuthService adminAuth,
            IAntiforgery antiforgery,
            DatabaseMigrationService migrationService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح. أعد تحميل صفحة الإعداد." });
            if (!access.IsAllowed(context, configurationService.HasConfiguration, adminAuth))
            {
                return Results.Json(new { message = SetupDeniedMessage(configurationService.HasConfiguration) }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var validationError = Validate(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { message = validationError });
            }

            var result = await configurationService.TestConnectionAsync(request, cancellationToken);
            if (!result.Success)
            {
                return Results.Json(
                    result,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (result.DatabaseIsNewer)
            {
                return Results.Json(new TestConnectionResult
                {
                    Success = false,
                    DatabaseIsNewer = true,
                    DatabaseVersion = result.DatabaseVersion,
                    ApplicationVersion = result.ApplicationVersion,
                    Message = $"إصدار قاعدة البيانات ({result.DatabaseVersion}) أحدث من إصدار البرنامج الحالي ({result.ApplicationVersion}). يرجى مراجعة دائرة نظم وقواعد البيانات للتأكد من التحديث."
                }, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                await configurationService.SaveAsync(request, cancellationToken);
                access.ConsumeFirstRunToken();

                if (result.RequiresUpgrade)
                {
                    if (request.ApplyMigrations)
                    {
                        var migrationResult = await migrationService.ApplyMigrationsAsync(cancellationToken: cancellationToken);
                        if (!migrationResult.Success)
                        {
                            return Results.Ok(new TestConnectionResult
                            {
                                Success = false,
                                RequiresUpgrade = true,
                                DatabaseVersion = migrationResult.CurrentVersion,
                                ApplicationVersion = migrationResult.TargetVersion,
                                Message = $"تم حفظ الإعدادات ولكن فشل تطبيق الترحيلات: {migrationResult.Message}"
                            });
                        }

                        return Results.Ok(new TestConnectionResult
                        {
                            Success = true,
                            DatabaseVersion = migrationResult.TargetVersion,
                            ApplicationVersion = migrationResult.TargetVersion,
                            Message = $"تم حفظ الإعدادات وتطبيق الترحيلات بنجاح إلى الإصدار {migrationResult.TargetVersion}."
                        });
                    }

                    return Results.Ok(new TestConnectionResult
                    {
                        Success = true,
                        RequiresUpgrade = true,
                        DatabaseVersion = result.DatabaseVersion,
                        ApplicationVersion = result.ApplicationVersion,
                        Message = $"تم حفظ الإعدادات بنجاح. قاعدة البيانات الحالية بالإصدار ({result.DatabaseVersion}) وهي أقدم من إصدار النظام ({result.ApplicationVersion}). يلزم موافقة مدير النظام لتطبيق الترحيلات لتحديثها."
                    });
                }

                return Results.Ok(new TestConnectionResult
                {
                    Success = true,
                    IsCompatible = true,
                    DatabaseVersion = result.DatabaseVersion,
                    ApplicationVersion = result.ApplicationVersion,
                    Message = "تم حفظ الإعدادات بنجاح. قاعدة البيانات متوافقة ومحدثة."
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(typeof(SetupEndpoints))
                    .LogError(ex, "تعذر حفظ إعدادات لوحة المتابعة.");
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "تعذر حفظ الإعدادات.");
            }
        }).RequireRateLimiting("setup");

        group.MapPost("/apply-migrations", async (
            HttpContext context,
            ConfigurationService configurationService,
            SetupAccessService access,
            IWebAdminAuthService adminAuth,
            IAntiforgery antiforgery,
            DatabaseMigrationService migrationService,
            CancellationToken cancellationToken) =>
        {
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح. أعد تحميل صفحة الإعداد." });
            if (!access.IsAllowed(context, configurationService.HasConfiguration, adminAuth))
            {
                return Results.Json(new { message = SetupDeniedMessage(configurationService.HasConfiguration) }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await migrationService.ApplyMigrationsAsync(cancellationToken: cancellationToken);
            return Results.Json(new TestConnectionResult
            {
                Success = result.Success,
                DatabaseVersion = result.TargetVersion ?? result.CurrentVersion,
                ApplicationVersion = result.TargetVersion,
                Message = result.Message
            }, statusCode: result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        }).RequireRateLimiting("setup");
    }

    private static string SetupDeniedMessage(bool isConfigured) => isConfigured
        ? "إعدادات الاتصال محفوظة مسبقًا. سجل الدخول كمدير أولًا ثم أعد فتح إعدادات الاتصال."
        : "الإعداد الأولي يتم من الخادم نفسه فقط: افتح اللوحة عبر localhost على جهاز الاستضافة ثم أعد المحاولة.";

    private static string? Validate(SetupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Host) || request.Host.Length > 255)
            return "اسم المضيف مطلوب ولا يجوز أن يتجاوز 255 محرفًا.";
        if (request.Port is < 1 or > 65535)
            return "رقم المنفذ غير صالح.";
        if (string.IsNullOrWhiteSpace(request.Database) || request.Database.Length > 128)
            return "اسم قاعدة البيانات مطلوب ولا يجوز أن يتجاوز 128 محرفًا.";
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 128)
            return "اسم المستخدم مطلوب ولا يجوز أن يتجاوز 128 محرفًا.";
        if (request.Password?.Length > 512)
            return "كلمة المرور أطول من الحد المسموح.";
        if (request.OrganizationName?.Length > 200 || request.SubOrganizationName?.Length > 200)
            return "اسم الجهة أطول من الحد المسموح.";
        if (request.DashboardVersion is not null and not 1 and not 2 and not 3)
            return "إصدار واجهة لوحة المتابعة غير صالح.";
        if (!string.IsNullOrWhiteSpace(request.SslMode)
            && !request.SslMode.Equals("Disable", StringComparison.OrdinalIgnoreCase)
            && !request.SslMode.Equals("Require", StringComparison.OrdinalIgnoreCase))
            return "وضع تشفير الاتصال غير صالح.";

        var values = new[]
        {
            request.Host,
            request.Database,
            request.Username,
            request.OrganizationName,
            request.SubOrganizationName
        };
        var controlCharacterError = values.Any(value => value?.Any(char.IsControl) == true);
        if (controlCharacterError)
            return "تحتوي إحدى القيم على محارف غير مسموحة.";

        return null;
    }
}

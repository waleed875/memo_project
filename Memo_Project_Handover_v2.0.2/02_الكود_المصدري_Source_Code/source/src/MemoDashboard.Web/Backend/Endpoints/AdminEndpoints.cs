using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using MemoDashboard.Backend.Services;
using MemoDashboard.Backend.Services.Updates;
using Microsoft.AspNetCore.Antiforgery;

namespace MemoDashboard.Backend.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin");

        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });

        group.MapGet("/session", (HttpContext context, IWebAdminAuthService auth) =>
        {
            var user = auth.GetCurrent(context);
            return Results.Ok(user is null
                ? new { authenticated = false, mustChangePassword = false }
                : new { authenticated = true, mustChangePassword = false, id = user.Id, username = user.Username, fullName = user.FullName, role = user.Role });
        });

        group.MapPost("/login", async (
            AdminLoginRequest request,
            HttpContext context,
            IWebAdminAuthService auth,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح. أعد تحميل صفحة الدخول." });

            var result = await auth.AuthenticateAsync(context, request.Username ?? string.Empty, request.Password ?? string.Empty, cancellationToken);
            if (result.MustChangePassword)
            {
                return Results.Ok(new
                {
                    authenticated = false,
                    mustChangePassword = true,
                    username = result.Username,
                    fullName = result.FullName,
                    role = result.Role,
                    message = result.Message
                });
            }

            var user = auth.GetCurrent(context);
            return result.Authenticated
                ? Results.Ok(new { authenticated = true, mustChangePassword = false, id = user?.Id ?? Guid.Empty, username = result.Username, fullName = result.FullName, role = result.Role })
                : Results.Unauthorized();
        }).RequireRateLimiting("admin-login");

        group.MapPost("/logout", async (HttpContext context, IWebAdminAuthService auth, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
        {
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });
            await auth.SignOutAsync(context, cancellationToken);
            return Results.NoContent();
        });

        // ---------------- User Management ----------------
        group.MapGet("/users", async (
            IUserService userService,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();

            var users = await userService.GetUsersAsync(user.Id, cancellationToken);
            return Results.Ok(users);
        });

        group.MapGet("/roles", async (
            IUserService userService,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();

            var roles = await userService.GetActiveRolesAsync(user.Id, cancellationToken);
            return Results.Ok(roles);
        });

        group.MapPost("/users", async (
            SaveUserRequest request,
            IUserService userService,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            if (request.User.Id == Guid.Empty) request.User.Id = Guid.NewGuid();
            await userService.SaveUserAsync(request.User, request.NewPassword, user.Id, cancellationToken);
            return Results.Ok(new { message = "تم حفظ المستخدم بنجاح." });
        });

        group.MapPut("/users/{id:guid}", async (
            Guid id,
            SaveUserRequest request,
            IUserService userService,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            request.User.Id = id;
            await userService.SaveUserAsync(request.User, request.NewPassword, user.Id, cancellationToken);
            return Results.Ok(new { message = "تم تعديل المستخدم بنجاح." });
        });

        group.MapPost("/users/{id:guid}/reset-password", async (
            Guid id,
            ResetPasswordRequest request,
            IUserService userService,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            await userService.ResetUserPasswordAsync(id, request.NewPassword, user.Id, cancellationToken);
            return Results.Ok(new { message = "تمت إعادة تعيين كلمة المرور بنجاح." });
        });

        // ---------------- Audit Logs ----------------
        group.MapGet("/audit-logs", async (
            IAuditService auditService,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken,
            Guid? userId = null,
            string? actionType = null,
            DuplicateClassification? duplicateClassification = null,
            Guid? companyId = null,
            string? noteNumber = null,
            string? descriptionSearch = null,
            bool duplicateAttemptsOnly = false,
            string? from = null,
            string? to = null,
            int pageNumber = 1,
            int pageSize = 100) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();

            var filter = new AuditLogFilterModel
            {
                UserId = userId,
                ActionType = actionType,
                DuplicateClassification = duplicateClassification,
                CompanyId = companyId,
                NoteNumber = noteNumber,
                DescriptionSearch = descriptionSearch,
                DuplicateAttemptsOnly = duplicateAttemptsOnly,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            if (DateOnly.TryParse(from, out var fDate)) filter.From = fDate;
            if (DateOnly.TryParse(to, out var tDate)) filter.To = tDate;

            var result = await auditService.SearchLogsAsync(filter, user.Id, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/audit-actions", async (
            IAuditService auditService,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();

            var types = await auditService.GetActionTypesAsync(user.Id, cancellationToken);
            return Results.Ok(types);
        });

        // ---------------- System Mode ----------------
        group.MapGet("/system-mode", async (
            ISystemModeService modeService,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            var mode = await modeService.LoadModeAsync(cancellationToken);
            return Results.Ok(new { mode = mode.ToString() });
        });

        group.MapPost("/system-mode", async (
            SwitchModeRequest request,
            ISystemModeService modeService,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            await modeService.SwitchModeAsync(request.Mode, user.Id, cancellationToken);
            return Results.Ok(new { mode = request.Mode.ToString(), message = "تم تغيير وضع النظام." });
        });

        // ---------------- Updates ----------------
        group.MapGet("/updates/health", (HttpContext context, IWebAdminAuthService auth, UnifiedUpdateService updates) =>
        {
            return auth.GetCurrent(context) is null
                ? Results.Unauthorized()
                : Results.Ok(updates.GetHealth());
        });

        group.MapPost("/updates/releases", async (
            HttpContext context,
            IWebAdminAuthService auth,
            IAntiforgery antiforgery,
            UnifiedUpdateService updates,
            CancellationToken cancellationToken) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            var parsed = await UpdateEndpoints.ReadPublishRequestAsync(context.Request, updates.MaxUploadRequestBytes, cancellationToken);
            if (parsed.Error is not null)
            {
                return parsed.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? Results.StatusCode(StatusCodes.Status413PayloadTooLarge)
                    : Results.BadRequest(parsed.Error);
            }

            return UpdateEndpoints.ToResult(await updates.PublishAsync(parsed.Request!, cancellationToken));
        }).RequireRateLimiting("publishing");

        group.MapPost("/updates/releases/{version}/publish", async (
            string version,
            HttpContext context,
            IWebAdminAuthService auth,
            IAntiforgery antiforgery,
            UnifiedUpdateService updates,
            CancellationToken cancellationToken) =>
        {
            if (auth.GetCurrent(context) is null) return Results.Unauthorized();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });
            return UpdateEndpoints.ToResult(await updates.PromoteAsync(version, cancellationToken));
        }).RequireRateLimiting("publishing");

        // ---------------- Database Version & Migrations ----------------
        group.MapGet("/database-status", async (
            DatabaseMigrationService migrationService,
            IWebAdminAuthService auth,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();

            try
            {
                var status = await migrationService.GetVersionStatusAsync(cancellationToken);
                return Results.Ok(new
                {
                    databaseVersion = status.DatabaseVersion,
                    applicationVersion = status.ApplicationVersion,
                    requiresUpgrade = status.RequiresUpgrade,
                    databaseIsNewer = status.DatabaseIsNewer,
                    isCompatible = !status.RequiresUpgrade && !status.DatabaseIsNewer
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = $"تعذر فحص إصدار قاعدة البيانات: {ex.Message}" });
            }
        });

        group.MapPost("/apply-migrations", async (
            DatabaseMigrationService migrationService,
            IWebAdminAuthService auth,
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            var user = auth.GetCurrent(context);
            if (user is null) return Results.Unauthorized();
            if (!string.Equals(user.Role, "ADMIN", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            var result = await migrationService.ApplyMigrationsAsync(user.Id, cancellationToken);
            return Results.Json(new
            {
                success = result.Success,
                databaseVersion = result.TargetVersion ?? result.CurrentVersion,
                applicationVersion = result.TargetVersion,
                message = result.Message
            }, statusCode: result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
        });
    }

    public sealed record AdminLoginRequest(string? Username, string? Password);
    public sealed record SaveUserRequest(UserModel User, string? NewPassword = null);
    public sealed record ResetPasswordRequest(string NewPassword);
    public sealed record SwitchModeRequest(AppMode Mode);
}
using MemoDashboard.Backend.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace MemoDashboard.Backend.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });

        group.MapGet("/me", (HttpContext context, IWebAdminAuthService auth) =>
        {
            var user = auth.GetCurrent(context);
            return Results.Ok(user is null
                ? new { authenticated = false, user = (object?)null }
                : new
                {
                    authenticated = true,
                    user = new
                    {
                        id = user.Id,
                        username = user.Username,
                        fullName = user.FullName,
                        role = user.Role
                    }
                });
        });

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext context,
            IWebAdminAuthService auth,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح. أعد تحميل الصفحة." });

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

            if (!result.Authenticated)
            {
                return Results.Json(new { message = result.Message ?? "بيانات الدخول غير صحيحة." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = auth.GetCurrent(context);
            return Results.Ok(new
            {
                authenticated = true,
                mustChangePassword = false,
                user = new
                {
                    id = user?.Id ?? Guid.Empty,
                    username = result.Username,
                    fullName = result.FullName,
                    role = result.Role
                }
            });
        }).RequireRateLimiting("admin-login");

        group.MapPost("/logout", async (
            HttpContext context,
            IWebAdminAuthService auth,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            await auth.SignOutAsync(context, cancellationToken);
            return Results.Ok(new { message = "تم تسجيل الخروج بنجاح." });
        });

        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            HttpContext context,
            IWebAdminAuthService auth,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            if (!await WebAdminSecurity.IsCsrfValidAsync(context, antiforgery))
                return Results.BadRequest(new { message = "رمز حماية الطلب غير صالح." });

            var currentUsername = request.Username?.Trim();
            if (string.IsNullOrWhiteSpace(currentUsername))
            {
                var currentUser = auth.GetCurrent(context);
                currentUsername = currentUser?.Username;
            }

            if (string.IsNullOrWhiteSpace(currentUsername))
                return Results.BadRequest(new { message = "اسم المستخدم مطلوب." });

            var (success, error) = await auth.ChangePasswordAsync(
                currentUsername,
                request.CurrentPassword ?? string.Empty,
                request.NewPassword ?? string.Empty,
                cancellationToken);

            return success
                ? Results.Ok(new { success = true, message = "تم تغيير كلمة المرور بنجاح." })
                : Results.BadRequest(new { message = error ?? "تعذر تغيير كلمة المرور." });
        }).RequireRateLimiting("admin-login");
    }

    public sealed record LoginRequest(string? Username, string? Password);
    public sealed record ChangePasswordRequest(string? Username, string? CurrentPassword, string? NewPassword);
}
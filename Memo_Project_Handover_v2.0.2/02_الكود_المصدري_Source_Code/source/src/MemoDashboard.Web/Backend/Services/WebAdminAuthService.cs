using MemoDashboard.Backend.Services.Updates;
using System.Collections.Concurrent;
using System.Globalization;
using BCrypt.Net;
using Npgsql;
using NpgsqlTypes;

namespace MemoDashboard.Backend.Services;

public sealed record WebAdminUser(Guid Id, string Username, string? FullName, string Role);

public sealed record WebAdminLoginResult(
    bool Authenticated,
    bool MustChangePassword,
    string? Username,
    string? FullName,
    string? Role,
    string? Message);

public interface IWebAdminAuthService
{
    Task<WebAdminLoginResult> AuthenticateAsync(HttpContext context, string username, string password, CancellationToken cancellationToken);
    Task<WebAdminUser?> ValidateCurrentAsync(HttpContext context, CancellationToken cancellationToken);
    WebAdminUser? GetCurrent(HttpContext context);
    void SignOut(HttpContext context);
    Task SignOutAsync(HttpContext context, CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> ChangePasswordAsync(string username, string currentPassword, string newPassword, CancellationToken cancellationToken);
}

public sealed class WebAdminAuthService : IWebAdminAuthService
{
    private const string UserIdKey = "MemoDashboard.Admin.UserId";
    private const string UserNameKey = "MemoDashboard.Admin.Username";
    private const string RoleKey = "MemoDashboard.Admin.Role";
    private const string SecurityStampKey = "MemoDashboard.Admin.SecurityStamp";
    private const string AuthenticatedAtKey = "MemoDashboard.Admin.AuthenticatedAt";
    private const string ValidatedUserKey = "MemoDashboard.Admin.ValidatedUser";
    private static readonly TimeSpan ValidationCacheLifetime = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan AbsoluteSessionLifetime = TimeSpan.FromHours(8);

    private readonly ConfigurationService _configuration;
    private readonly UpdateServiceOptions? _updateOptions;
    private readonly ILogger<WebAdminAuthService> _logger;
    private readonly ConcurrentDictionary<Guid, CachedSessionUser> _validationCache = new();

    public WebAdminAuthService(
        ConfigurationService configuration,
        UpdateServiceOptions updateOptions,
        ILogger<WebAdminAuthService> logger)
    {
        _configuration = configuration;
        _updateOptions = updateOptions;
        _logger = logger;
    }

    // Kept for focused session tests that do not exercise database audit writes.
    public WebAdminAuthService(ConfigurationService configuration, ILogger<WebAdminAuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<WebAdminLoginResult> AuthenticateAsync(
        HttpContext context,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || username.Length > 128 || password.Length > 512)
            return new(false, false, null, null, null, "بيانات الدخول غير صحيحة.");

        var connectionString = _configuration.BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return new(false, false, null, null, null, "لم يتم إعداد اتصال قاعدة البيانات.");

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                select u.id, u.username, u.full_name, u.password_hash,
                       u.must_change_password, u.updated_at, r.code
                from auth.users u
                join auth.roles r on r.id = u.role_id
                where lower(trim(u.username)) = lower(trim(@username))
                  and u.is_active = true and u.is_deleted = false
                  and r.is_active = true
                limit 1
                """;
            command.Parameters.AddWithValue("username", username.Trim());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return new(false, false, null, null, null, "بيانات الدخول غير صحيحة.");

            var id = reader.GetGuid(0);
            var normalizedUsername = reader.GetString(1);
            var fullName = reader.IsDBNull(2) ? null : reader.GetString(2);
            var hash = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var mustChangePassword = !reader.IsDBNull(4) && reader.GetBoolean(4);
            var securityStamp = ToSecurityStamp(reader.GetDateTime(5));
            var role = reader.GetString(6);
            if (string.IsNullOrWhiteSpace(hash) || !BCrypt.Net.BCrypt.Verify(password, hash))
                return new(false, false, null, null, null, "بيانات الدخول غير صحيحة.");

            if (mustChangePassword)
            {
                await WriteAuditAsync(id, "LOGIN_MUST_CHANGE_PASSWORD", "دخول مقيّد؛ يلزم تغيير كلمة المرور.", cancellationToken);
                return new(false, true, normalizedUsername, fullName, role, "يجب تغيير كلمة المرور قبل المتابعة.");
            }

            SetSession(context, id, normalizedUsername, role, securityStamp);
            var user = new WebAdminUser(id, normalizedUsername, fullName, role);
            _validationCache[id] = new CachedSessionUser(user, securityStamp, DateTimeOffset.UtcNow);
            context.Items[ValidatedUserKey] = user;
            await WriteAuditAsync(id, "LOGIN", "تسجيل دخول إلى النظام.", cancellationToken);
            return new(true, false, normalizedUsername, fullName, role, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "فشل التحقق من المستخدم");
            return new(false, false, null, null, null, "تعذر التحقق من بيانات الدخول.");
        }
    }

    public async Task<WebAdminUser?> ValidateCurrentAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Items.Remove(ValidatedUserKey);
        var idText = context.Session.GetString(UserIdKey);
        var username = context.Session.GetString(UserNameKey);
        var stampText = context.Session.GetString(SecurityStampKey);
        var authenticatedAtText = context.Session.GetString(AuthenticatedAtKey);

        if (!Guid.TryParse(idText, out var id)
            || string.IsNullOrWhiteSpace(username)
            || !long.TryParse(stampText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sessionStamp)
            || !long.TryParse(authenticatedAtText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var authenticatedAt))
        {
            if (idText is not null || username is not null) SignOut(context);
            return null;
        }

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(authenticatedAt);
        if (age < TimeSpan.Zero || age > AbsoluteSessionLifetime)
        {
            await RevokeAsync(context, id, "انتهت مدة الجلسة المطلقة.", cancellationToken);
            return null;
        }

        if (_validationCache.TryGetValue(id, out var cached)
            && DateTimeOffset.UtcNow - cached.CheckedAt <= ValidationCacheLifetime
            && cached.SecurityStamp == sessionStamp
            && string.Equals(cached.User.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            context.Items[ValidatedUserKey] = cached.User;
            return cached.User;
        }

        var connectionString = _configuration.BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            SignOut(context);
            return null;
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                select u.id, u.username, u.full_name, u.is_active, u.is_deleted,
                       u.must_change_password, u.updated_at, r.code, r.is_active
                from auth.users u
                join auth.roles r on r.id = u.role_id
                where u.id = @id
                limit 1
                """;
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await RevokeAsync(context, id, "لم يعد حساب المستخدم موجوداً.", cancellationToken);
                return null;
            }

            var currentStamp = ToSecurityStamp(reader.GetDateTime(6));
            var role = reader.GetString(7);
            var valid = reader.GetBoolean(3)
                && !reader.GetBoolean(4)
                && !reader.GetBoolean(5)
                && reader.GetBoolean(8)
                && currentStamp == sessionStamp
                && reader.GetString(1).Equals(username, StringComparison.OrdinalIgnoreCase);
            if (!valid)
            {
                await RevokeAsync(context, id, "تغيرت حالة حساب المستخدم أو ختم أمانه.", cancellationToken);
                return null;
            }

            var user = new WebAdminUser(id, reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), role);
            _validationCache[id] = new CachedSessionUser(user, currentStamp, DateTimeOffset.UtcNow);
            context.Items[ValidatedUserKey] = user;
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر إعادة التحقق من جلسة المستخدم");
            SignOut(context);
            return null;
        }
    }

    public WebAdminUser? GetCurrent(HttpContext context)
    {
        return context.Items.TryGetValue(ValidatedUserKey, out var value) && value is WebAdminUser user
            ? user
            : null;
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(
        string username,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
            return (false, "جميع الحقول مطلوبة.");

        try
        {
            AuditMemoSystem.Core.Validation.PasswordPolicy.EnsureValid(newPassword);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }

        var connectionString = _configuration.BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return (false, "لم يتم إعداد الاتصال بقاعدة البيانات.");

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                select u.id, u.password_hash
                from auth.users u
                where lower(trim(u.username)) = lower(trim(@username))
                  and u.is_active = true and u.is_deleted = false
                limit 1
                """;
            command.Parameters.AddWithValue("username", username.Trim());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return (false, "المستخدم غير موجود.");

            var id = reader.GetGuid(0);
            var hash = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            if (!BCrypt.Net.BCrypt.Verify(currentPassword, hash))
                return (false, "كلمة المرور الحالية غير صحيحة.");

            await reader.CloseAsync();

            var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = """
                update auth.users
                set password_hash = @hash,
                    must_change_password = false,
                    updated_at = now()
                where id = @id
                """;
            updateCmd.Parameters.AddWithValue("hash", newHash);
            updateCmd.Parameters.AddWithValue("id", id);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken);

            _validationCache.TryRemove(id, out _);
            await WriteAuditAsync(id, "PASSWORD_CHANGED", "تم تغيير كلمة المرور بنجاح.", cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل تغيير كلمة المرور للمستخدم {Username}", username);
            return (false, "تعذر تغيير كلمة المرور.");
        }
    }

    public void SignOut(HttpContext context)
    {
        context.Session.Remove(UserIdKey);
        context.Session.Remove(UserNameKey);
        context.Session.Remove(RoleKey);
        context.Session.Remove(SecurityStampKey);
        context.Session.Remove(AuthenticatedAtKey);
        context.Items.Remove(ValidatedUserKey);
    }

    public async Task SignOutAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var id = Guid.TryParse(context.Session.GetString(UserIdKey), out var parsed) ? parsed : (Guid?)null;
        SignOut(context);
        if (id.HasValue)
            await WriteAuditAsync(id.Value, "LOGOUT", "تسجيل خروج من النظام.", cancellationToken);
    }

    private async Task RevokeAsync(HttpContext context, Guid id, string description, CancellationToken cancellationToken)
    {
        SignOut(context);
        await WriteAuditAsync(id, "SESSION_REVOKED", description, cancellationToken);
    }

    private static void SetSession(HttpContext context, Guid id, string username, string role, long securityStamp)
    {
        context.Session.SetString(UserIdKey, id.ToString("D"));
        context.Session.SetString(UserNameKey, username);
        context.Session.SetString(RoleKey, role);
        context.Session.SetString(SecurityStampKey, securityStamp.ToString(CultureInfo.InvariantCulture));
        context.Session.SetString(AuthenticatedAtKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    private async Task WriteAuditAsync(Guid userId, string action, string description, CancellationToken cancellationToken)
    {
        var connectionString = _updateOptions?.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "insert into updates.audit_log(user_id, action, description) values (@user_id, @action, @description)";
            command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = userId;
            command.Parameters.AddWithValue("action", action);
            command.Parameters.AddWithValue("description", description);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر تسجيل حدث أمان لجلسة مدير الويب");
        }
    }

    private static long ToSecurityStamp(DateTime value)
    {
        var utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    private sealed record CachedSessionUser(WebAdminUser User, long SecurityStamp, DateTimeOffset CheckedAt);
}
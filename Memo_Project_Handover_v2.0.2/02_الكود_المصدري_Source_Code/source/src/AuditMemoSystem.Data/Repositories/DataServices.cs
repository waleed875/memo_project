using System.Data;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using AuditMemoSystem.Core.Validation;
using AuditMemoSystem.Data.Connection;
using Npgsql;
using NpgsqlTypes;

namespace AuditMemoSystem.Data.Repositories;

public abstract class RepositoryBase
{
    protected RepositoryBase(DbConnectionFactory factory)
    {
        Factory = factory;
    }

    protected DbConnectionFactory Factory { get; }

    protected static DateOnly ReadDate(NpgsqlDataReader reader, string name)
    {
        var value = reader[name];
        return value is DateOnly dateOnly ? dateOnly : DateOnly.FromDateTime((DateTime)value);
    }

    protected static DateOnly? ReadNullableDate(NpgsqlDataReader reader, string name)
    {
        return reader.IsDBNull(reader.GetOrdinal(name)) ? null : ReadDate(reader, name);
    }

    protected static T EnumValue<T>(string value) where T : struct => Enum.Parse<T>(value, ignoreCase: true);

    protected static async Task<UserRole> GetUserRoleAsync(DbConnectionFactory factory, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select r.code
            from auth.users u
            join auth.roles r on r.id = u.role_id
            where u.id = @user_id and u.is_active and not u.is_deleted and r.is_active
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        var code = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (!Enum.TryParse<UserRole>(code, ignoreCase: true, out var role))
        {
            throw new UnauthorizedAccessException("الجلسة غير صالحة أو لا تملك الصلاحية المطلوبة.");
        }
        return role;
    }

    protected static async Task EnsureRoleAsync(DbConnectionFactory factory, Guid userId, UserRole[] allowedRoles, CancellationToken cancellationToken)
    {
        var role = await GetUserRoleAsync(factory, userId, cancellationToken);
        if (!allowedRoles.Contains(role))
        {
            throw new UnauthorizedAccessException("ليس لديك صلاحية لتنفيذ هذه العملية.");
        }
    }
    protected static string NormalizeJsonForPostgres(string metadata)
    {
        return metadata.Replace("\\u0000", string.Empty, StringComparison.Ordinal).Replace("\u0000", string.Empty, StringComparison.Ordinal);
    }

    protected static async Task WriteAuditRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? userId,
        string actionType,
        string? schemaName,
        string? tableName,
        Guid? recordId,
        string? description,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into audit.logs(user_id, action_type, schema_name, table_name, record_id, description)
            values (@user_id, @action_type, @schema_name, @table_name, @record_id, @description)
            """, connection, transaction);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = (object?)userId ?? DBNull.Value;
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("schema_name", (object?)schemaName ?? DBNull.Value);
        command.Parameters.AddWithValue("table_name", (object?)tableName ?? DBNull.Value);
        command.Parameters.Add("record_id", NpgsqlDbType.Uuid).Value = (object?)recordId ?? DBNull.Value;
        command.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class AuditService : RepositoryBase, IAuditService
{
    private readonly ISystemModeService _modeService;

    public AuditService(DbConnectionFactory factory, ISystemModeService modeService) : base(factory)
    {
        _modeService = modeService;
    }

    public async Task WriteAsync(Guid? userId, string actionType, string? schemaName, string? tableName, Guid? recordId, string? description, CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            insert into audit.logs(user_id, action_type, schema_name, table_name, record_id, description)
            values (@user_id, @action_type, @schema_name, @table_name, @record_id, @description)
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("schema_name", (object?)schemaName ?? DBNull.Value);
        command.Parameters.AddWithValue("table_name", (object?)tableName ?? DBNull.Value);
        command.Parameters.AddWithValue("record_id", (object?)recordId ?? DBNull.Value);
        command.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetActionTypesAsync(Guid requesterId, CancellationToken cancellationToken = default)
    {
        await EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN], cancellationToken);
        var actions = new List<string>();
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select distinct action_type from audit.logs order by action_type", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) actions.Add(reader.GetString(0));
        return actions;
    }

    public async Task<PagedResult<AuditLogModel>> SearchLogsAsync(AuditLogFilterModel filter, Guid requesterId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN], cancellationToken);
        if (filter.From > filter.To) throw new InvalidOperationException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");

        var pageNumber = Math.Max(1, filter.PageNumber);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var normalizedNumber = NoteNumberNormalizer.Normalize(filter.NoteNumber);
        var actionType = string.IsNullOrWhiteSpace(filter.ActionType) ? null : filter.ActionType.Trim();
        var classification = filter.DuplicateClassification?.ToString();
        var description = string.IsNullOrWhiteSpace(filter.DescriptionSearch) ? null : filter.DescriptionSearch.Trim();

        const string joins = """
            from audit.logs l
            left join auth.users u on u.id = l.user_id
            left join core.notes n on n.id = l.record_id
            left join core.companies c on c.id = n.company_id
            """;
        const string where = """
            where l.created_at >= @from
              and l.created_at < (@to + interval '1 day')
              and (@user_id is null or l.user_id = @user_id)
              and (@action_type is null or l.action_type = @action_type)
              and (@classification is null or l.action_type = @classification)
              and (not @attempts_only or l.action_type = 'DATA_ENTRY_DUPLICATE_ATTEMPT')
              and (@company_id is null
                   or n.company_id = @company_id
                   or l.new_value @> jsonb_build_object('CompanyId', @company_id_text))
              and (@normalized_number = ''
                   or n.note_number_normalized = @normalized_number
                   or l.new_value @> jsonb_build_object('normalizedNumber', @normalized_number))
              and (@description is null or coalesce(l.description, '') ilike '%' || @description || '%')
            """;

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        int totalCount;
        await using (var countCommand = new NpgsqlCommand("select count(*) " + joins + " " + where, connection))
        {
            AddFilterParameters(countCommand, filter, actionType, classification, normalizedNumber, description);
            totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        }
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        if (totalPages > 0) pageNumber = Math.Min(pageNumber, totalPages);

        var items = new List<AuditLogModel>();
        var sql = """
            select l.id, l.user_id, u.username, l.action_type, l.schema_name, l.table_name,
                   l.record_id, l.description, l.created_at, n.id as note_id,
                   coalesce(n.note_number, l.new_value ->> 'noteNumber') as note_number,
                   coalesce(c.name, l.new_value ->> 'company') as company_name
            """ + " " + joins + " " + where + " order by l.created_at desc, l.id desc limit @limit offset @offset";
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            AddFilterParameters(command, filter, actionType, classification, normalizedNumber, description);
            command.Parameters.AddWithValue("limit", pageSize);
            command.Parameters.AddWithValue("offset", (pageNumber - 1) * pageSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new AuditLogModel
                {
                    Id = reader.GetInt64(0),
                    UserId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    UserName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ActionType = reader.GetString(3),
                    SchemaName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    TableName = reader.IsDBNull(5) ? null : reader.GetString(5),
                    RecordId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    Description = reader.IsDBNull(7) ? null : reader.GetString(7),
                    CreatedAt = reader.GetDateTime(8),
                    NoteId = reader.IsDBNull(9) ? null : reader.GetGuid(9),
                    NoteNumber = reader.IsDBNull(10) ? null : reader.GetString(10),
                    CompanyName = reader.IsDBNull(11) ? null : reader.GetString(11)
                });
            }
        }

        return new PagedResult<AuditLogModel>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    private static void AddFilterParameters(NpgsqlCommand command, AuditLogFilterModel filter, string? actionType, string? classification, string normalizedNumber, string? description)
    {
        command.Parameters.AddWithValue("from", filter.From);
        command.Parameters.AddWithValue("to", filter.To);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = (object?)filter.UserId ?? DBNull.Value;
        command.Parameters.Add("action_type", NpgsqlDbType.Varchar).Value = (object?)actionType ?? DBNull.Value;
        command.Parameters.Add("classification", NpgsqlDbType.Varchar).Value = (object?)classification ?? DBNull.Value;
        command.Parameters.AddWithValue("attempts_only", filter.DuplicateAttemptsOnly);
        command.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = (object?)filter.CompanyId ?? DBNull.Value;
        command.Parameters.Add("company_id_text", NpgsqlDbType.Varchar).Value = filter.CompanyId?.ToString() ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("normalized_number", normalizedNumber);
        command.Parameters.Add("description", NpgsqlDbType.Varchar).Value = (object?)description ?? DBNull.Value;
    }
}

public sealed class AuthService : RepositoryBase, IAuthService
{
    private readonly IAuditService _auditService;
    private readonly ISystemModeService _modeService;
    private readonly ConcurrentDictionary<string, int> _failedAttempts = new(StringComparer.OrdinalIgnoreCase);

    public AuthService(DbConnectionFactory factory, IAuditService auditService, ISystemModeService modeService) : base(factory)
    {
        _auditService = auditService;
        _modeService = modeService;
    }

    public UserModel? CurrentUser { get; private set; }

    public async Task EnsureBootstrapAdminHashAsync(CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select id, password_hash from auth.users where username = 'admin' and not is_deleted", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken) && reader.GetString(1) != "BOOTSTRAP_BCRYPT_ADMIN123" && BCrypt.Net.BCrypt.Verify("admin123", reader.GetString(1)))
        {
            var adminId = reader.GetGuid(0);
            await reader.CloseAsync();
            await using var update = new NpgsqlCommand("update auth.users set must_change_password = true where id = @id", connection);
            update.Parameters.AddWithValue("id", adminId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<bool> IsBootstrapAdminPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select exists(select 1 from auth.users where username = 'admin' and password_hash = 'BOOTSTRAP_BCRYPT_ADMIN123' and not is_deleted)", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task SetBootstrapAdminPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        PasswordPolicy.EnsureValid(password);
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("update auth.users set password_hash = @hash, must_change_password = false where username = 'admin' and password_hash = 'BOOTSTRAP_BCRYPT_ADMIN123'", connection);
        command.Parameters.AddWithValue("hash", BCrypt.Net.BCrypt.HashPassword(password));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("تم إعداد حساب المدير مسبقاً.");
        }
    }

    public async Task ChangeOwnPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        PasswordPolicy.EnsureValid(newPassword);
        var currentUser = CurrentUser ?? throw new UnauthorizedAccessException("انتهت جلسة المستخدم.");
        if (string.IsNullOrEmpty(currentPassword) || !BCrypt.Net.BCrypt.Verify(currentPassword, currentUser.PasswordHash))
        {
            throw new InvalidOperationException("كلمة المرور الحالية غير صحيحة.");
        }
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await using var command = new NpgsqlCommand("update auth.users set password_hash = @hash, must_change_password = false, updated_at = now() where id = @id and not is_deleted", connection);
        command.Parameters.AddWithValue("id", currentUser.Id);
        command.Parameters.AddWithValue("hash", newHash);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("تعذر تغيير كلمة المرور.");
        }

        // Keep the in-process Desktop session usable for a second password change.
        currentUser.PasswordHash = newHash;
        currentUser.MustChangePassword = false;
        try
        {
            await _auditService.WriteAsync(currentUser.Id, "PASSWORD_CHANGE", "auth", "users", currentUser.Id, "تغيير كلمة المرور الذاتية", cancellationToken);
        }
        catch
        {
            // A password change remains successful if only its audit write fails.
        }
    }

    public async Task<UserModel?> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username.Trim().ToLowerInvariant();
        var priorFailures = _failedAttempts.GetValueOrDefault(normalizedUsername);
        if (priorFailures > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(2000, 200 * priorFailures)), cancellationToken);
        }
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var hasMustChangePassword = await HasMustChangePasswordColumnAsync(connection, cancellationToken);
        var sql = hasMustChangePassword
            ? """
                select u.id, u.username, u.password_hash, u.full_name, u.role_id, r.code, r.name_ar, u.is_active, u.must_change_password
                from auth.users u
                join auth.roles r on r.id = u.role_id
                where lower(trim(u.username)) = @username
                  and u.is_deleted = false
                  and u.is_active = true
                  and r.is_active = true
                """
            : """
                select u.id, u.username, u.password_hash, u.full_name, u.role_id, r.code, r.name_ar, u.is_active, false as must_change_password
                from auth.users u
                join auth.roles r on r.id = u.role_id
                where lower(trim(u.username)) = @username
                  and u.is_deleted = false
                  and u.is_active = true
                  and r.is_active = true
                """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("username", normalizedUsername);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await RecordLoginFailureAsync(normalizedUsername, null, cancellationToken);
            return null;
        }

        var user = new UserModel
        {
            Id = reader.GetGuid(0),
            Username = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            FullName = reader.GetString(3),
            RoleId = reader.GetGuid(4),
            RoleCode = reader.GetString(5),
            RoleNameAr = reader.GetString(6),
            IsActive = reader.GetBoolean(7),
            MustChangePassword = reader.GetBoolean(8)
        };

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            await RecordLoginFailureAsync(normalizedUsername, user.Id, cancellationToken);
            return null;
        }

        try
        {
            if (_modeService.CurrentMode != AppMode.LOCAL_READONLY)
            {
                await _auditService.WriteAsync(user.Id, "LOGIN", "auth", "users", user.Id, "تسجيل دخول", cancellationToken);
            }
            CurrentUser = user; // Fail closed: publish a session only after auditing succeeds.
            _failedAttempts.TryRemove(normalizedUsername, out _);
            return user;
        }
        catch
        {
            CurrentUser = null;
            throw;
        }
    }

    private static async Task<bool> HasMustChangePasswordColumnAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select exists (
                select 1
                from information_schema.columns
                where table_schema = 'auth'
                  and table_name = 'users'
                  and column_name = 'must_change_password'
            )
            """, connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
    private async Task RecordLoginFailureAsync(string normalizedUsername, Guid? userId, CancellationToken cancellationToken)
    {
        _failedAttempts.AddOrUpdate(normalizedUsername, 1, (_, count) => Math.Min(count + 1, 10));
        if (_modeService.CurrentMode == AppMode.LOCAL_READONLY) return;
        await _auditService.WriteAsync(userId, "LOGIN_FAILURE", "auth", "users", userId, "فشل تسجيل الدخول", cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var user = CurrentUser;
        CurrentUser = null;
        if (user is null)
        {
            return;
        }

        if (_modeService.CurrentMode == AppMode.LOCAL_READONLY)
        {
            return;
        }

        try
        {
            await _auditService.WriteAsync(
                user.Id,
                "LOGOUT",
                "auth",
                "users",
                user.Id,
                "تسجيل خروج",
                cancellationToken);
        }
        catch
        {
            // Clearing the local session must not be blocked by an audit write failure.
        }
    }

    public bool HasRole(params UserRole[] roles)
    {
        if (CurrentUser is null)
        {
            return false;
        }
        return roles.Any(role => role.ToString().Equals(CurrentUser.RoleCode, StringComparison.OrdinalIgnoreCase));
    }

    public void InvalidateSession() => CurrentUser = null;

}

public sealed class SystemModeService : RepositoryBase, ISystemModeService
{
    private AppMode _currentMode = AppMode.SERVER;

    public SystemModeService(DbConnectionFactory factory) : base(factory) { }

    public AppMode CurrentMode => Factory.ActiveProfile.Type == ConnectionProfileType.LOCAL
        ? AppMode.LOCAL_READONLY
        : _currentMode;

    public async Task<AppMode> LoadModeAsync(CancellationToken cancellationToken = default)
    {
        if (Factory.ActiveProfile.Type == ConnectionProfileType.LOCAL)
        {
            _currentMode = AppMode.LOCAL_READONLY;
            return _currentMode;
        }

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select value from system.app_settings where key = 'APP_MODE'", connection);
        var value = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? "SERVER";
        _currentMode = EnumValue<AppMode>(value);
        return _currentMode;
    }

    public async Task SwitchModeAsync(AppMode mode, Guid requesterId, CancellationToken cancellationToken = default)
    {
        await EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN], cancellationToken);
        if (Factory.ActiveProfile.Type == ConnectionProfileType.LOCAL)
        {
            if (mode != AppMode.LOCAL_READONLY)
            {
                throw new InvalidOperationException("ملف الاتصال المحلي يفرض وضع القراءة فقط ولا يمكن تحويله إلى وضع الخادم.");
            }

            _currentMode = AppMode.LOCAL_READONLY;
            return;
        }

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            insert into system.app_settings(key, value) values ('APP_MODE', @mode)
            on conflict (key) do update set value = excluded.value, updated_at = now()
            """, connection);
        command.Parameters.AddWithValue("mode", mode.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
        _currentMode = mode;
    }

    public void EnsureWritable()
    {
        if (CurrentMode == AppMode.LOCAL_READONLY)
        {
            throw new InvalidOperationException("النظام يعمل على قاعدة محلية للقراءة فقط. لا يمكن تنفيذ عمليات التعديل.");
        }
    }
}

public sealed class SystemSettingsService : RepositoryBase, ISystemSettingsService
{
    private readonly ISystemModeService _modeService;

    public SystemSettingsService(DbConnectionFactory factory, ISystemModeService modeService) : base(factory)
    {
        _modeService = modeService;
    }

    public async Task<ServerAddressSettingsModel> GetServerAddressSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select key, value
            from system.app_settings
            where key in ('SERVER_BASE_ADDRESS', 'DASHBOARD_BASE_ADDRESS',
                          'UPDATE_SERVICE_BASE_ADDRESS', 'DEPLOYMENT_MODE',
                          'INTERACTIVE_DASHBOARD_URL', 'INTERACTIVE_DASHBOARD_DISPLAY_URL', 'UPDATE_SERVICE_URL')
            """, connection);

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);

        var explicitAddress = values.GetValueOrDefault("SERVER_BASE_ADDRESS");
        var deploymentMode = values.GetValueOrDefault("DEPLOYMENT_MODE") ?? "InternalLanHttps";
        var dashboardAddress = values.GetValueOrDefault("DASHBOARD_BASE_ADDRESS")
            ?? explicitAddress
            ?? values.GetValueOrDefault("INTERACTIVE_DASHBOARD_URL");
        var updateAddress = values.GetValueOrDefault("UPDATE_SERVICE_BASE_ADDRESS")
            ?? values.GetValueOrDefault("UPDATE_SERVICE_URL");
        if (!string.IsNullOrWhiteSpace(dashboardAddress))
        {
            var dashboardEndpoints = ServerEndpoints.ParseDashboard(
                dashboardAddress,
                string.Equals(deploymentMode, "InternalLanHttp", StringComparison.OrdinalIgnoreCase));
            ServerEndpoints? updateEndpoints = null;
            var conflict = false;
            if (!string.IsNullOrWhiteSpace(updateAddress))
            {
                try { updateEndpoints = ServerEndpoints.ParseUpdateService(updateAddress); }
                catch (InvalidOperationException) { conflict = true; }
            }
            return new ServerAddressSettingsModel
            {
                BaseAddress = dashboardEndpoints.BaseAddress.ToString(),
                DashboardBaseAddress = dashboardEndpoints.BaseAddress.ToString(),
                UpdateServiceBaseAddress = updateEndpoints?.BaseAddress.ToString() ?? updateAddress,
                DeploymentMode = deploymentMode,
                Endpoints = dashboardEndpoints,
                DashboardEndpoints = dashboardEndpoints,
                UpdateServiceEndpoints = updateEndpoints,
                HasConflictingLegacySettings = conflict
            };
        }

        var legacyDashboardUrl = values.GetValueOrDefault("INTERACTIVE_DASHBOARD_URL");
        var legacyUpdateUrl = values.GetValueOrDefault("UPDATE_SERVICE_BASE_ADDRESS")
            ?? values.GetValueOrDefault("UPDATE_SERVICE_URL");
        var isLanHttp = string.Equals(deploymentMode, "InternalLanHttp", StringComparison.OrdinalIgnoreCase);
        var legacyAddresses = new List<string>();
        if (!string.IsNullOrWhiteSpace(legacyDashboardUrl))
            legacyAddresses.Add(ServerEndpoints.ParseDashboard(legacyDashboardUrl, isLanHttp).BaseAddress.ToString());
        if (!string.IsNullOrWhiteSpace(legacyUpdateUrl))
            legacyAddresses.Add(ServerEndpoints.ParseUpdateService(legacyUpdateUrl).BaseAddress.ToString());
        var legacy = legacyAddresses
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        if (legacy.Length == 0)
            return new ServerAddressSettingsModel();
        if (legacy.Length > 1)
            throw new InvalidOperationException("إعدادات عناوين الخادم القديمة متعارضة. أدخل عنوان الخادم الموحد مرة واحدة.");

        var migrated = ServerEndpoints.ParseDashboard(legacy[0], string.Equals(deploymentMode, "InternalLanHttp", StringComparison.OrdinalIgnoreCase));
        return new ServerAddressSettingsModel
        {
            BaseAddress = migrated.BaseAddress.ToString(),
            DashboardBaseAddress = migrated.BaseAddress.ToString(),
            DeploymentMode = deploymentMode,
            Endpoints = migrated,
            DashboardEndpoints = migrated,
            LoadedFromLegacySettings = true
        };
    }

    public async Task SaveServerAddressSettingsAsync(ServerAddressSettingsModel settings, Guid requesterId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN], cancellationToken);
        var endpoints = ServerEndpoints.ParseDashboard(
            settings.DashboardBaseAddress ?? settings.BaseAddress ?? string.Empty,
            string.Equals(settings.DeploymentMode, "InternalLanHttp", StringComparison.OrdinalIgnoreCase));

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand("""
            insert into system.app_settings(key, value)
            values ('DASHBOARD_BASE_ADDRESS', @base_address),
                   ('DEPLOYMENT_MODE', @deployment_mode)
            on conflict (key) do update set value = excluded.value, updated_at = now()
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("base_address", endpoints.BaseAddress.ToString());
            command.Parameters.AddWithValue("deployment_mode", settings.DeploymentMode);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditRecordAsync(connection, transaction, requesterId, "UPDATE_DASHBOARD_BASE_ADDRESS", "system", "app_settings", null,
            "تم تحديث عنوان لوحة المعلومات ونمط الشبكة. عنوان خدمة التحديث مستقل ولا يُشتق تلقائياً.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<WebDashboardSettingsModel> GetWebDashboardSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select key, value from system.app_settings
            where key in ('INTERACTIVE_DASHBOARD_URL', 'INTERACTIVE_DASHBOARD_DISPLAY_URL')
            """, connection);
        var settings = new WebDashboardSettingsModel();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (reader.GetString(0) == "INTERACTIVE_DASHBOARD_URL") settings.InteractiveDashboardUrl = value;
            else settings.InteractiveDashboardDisplayUrl = value;
        }

        return settings;
    }

    public async Task SaveWebDashboardSettingsAsync(WebDashboardSettingsModel settings, Guid requesterId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN], cancellationToken);
        var dashboardUrl = NormalizeHttpUrl(settings.InteractiveDashboardUrl, "رابط لوحة المعلومات");
        var displayUrl = NormalizeHttpUrl(settings.InteractiveDashboardDisplayUrl, "رابط شاشة لوحة المعلومات");

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand("""
            insert into system.app_settings(key, value) values
                ('INTERACTIVE_DASHBOARD_URL', @dashboard_url),
                ('INTERACTIVE_DASHBOARD_DISPLAY_URL', @display_url)
            on conflict (key) do update set value = excluded.value, updated_at = now()
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("dashboard_url", (object?)dashboardUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("display_url", (object?)displayUrl ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditRecordAsync(connection, transaction, requesterId, "UPDATE_INTERACTIVE_DASHBOARD_URL", "system", "app_settings", null,
            "تم تحديث رابط لوحة المعلومات التفاعلية المركزي.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<UpdateServiceSettingsModel> GetUpdateServiceSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select key, value from system.app_settings
            where key in ('UPDATE_SERVICE_BASE_ADDRESS', 'UPDATE_SERVICE_URL', 'UPDATE_PUBLIC_KEY')
            """, connection);
        var settings = new UpdateServiceSettingsModel();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (reader.GetString(0) is "UPDATE_SERVICE_BASE_ADDRESS" or "UPDATE_SERVICE_URL") settings.ServiceUrl ??= value;
            else settings.PublicKeyPem = value;
        }
        return settings;
    }

    public async Task SaveUpdateServiceSettingsAsync(UpdateServiceSettingsModel settings, Guid requesterId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN], cancellationToken);
        var serviceUrl = NormalizeHttpUrl(settings.ServiceUrl, "رابط خادم التحديث");
        if (string.IsNullOrWhiteSpace(serviceUrl)) throw new InvalidOperationException("رابط خادم التحديث مطلوب.");
        var publicKey = ValidatePublicKey(settings.PublicKeyPem);

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand("""
            insert into system.app_settings(key, value) values
                ('UPDATE_SERVICE_BASE_ADDRESS', @service_url),
                ('UPDATE_SERVICE_URL', @service_url),
                ('UPDATE_PUBLIC_KEY', @public_key)
            on conflict (key) do update set value = excluded.value, updated_at = now()
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("service_url", serviceUrl);
            command.Parameters.AddWithValue("public_key", publicKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await WriteAuditRecordAsync(connection, transaction, requesterId, "UPDATE_UPDATE_SERVICE_SETTINGS", "system", "app_settings", null,
            "تم تحديث رابط خادم التحديث المركزي.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    private static string ValidatePublicKey(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("المفتاح العام لخدمة التحديث مطلوب. استخدم زر فحص الخادم لجلبه.");

        if (trimmed.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("يُسمح بحفظ المفتاح العام فقط؛ لا تلصق مفتاح RSA الخاص.");

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(trimmed);
            var der = rsa.ExportSubjectPublicKeyInfo();
            if (der.Length > 16_384)
                throw new InvalidOperationException("المفتاح العام أكبر من الحد المسموح.");
            return rsa.ExportSubjectPublicKeyInfoPem();
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("المفتاح العام لخدمة التحديث غير صالح.");
        }
    }
    private static string? NormalizeHttpUrl(string? value, string label)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (trimmed is null) return null;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{label} يجب أن يكون عنوان http أو https صالحاً.");
        }
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            throw new InvalidOperationException($"{label} يجب أن يستخدم HTTPS. يُسمح HTTP فقط مع localhost للاختبار.");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"{label} لا يجب أن يتضمن بيانات دخول أو query أو fragment.");

        return uri.AbsoluteUri;
    }
}
public sealed class CompanyService : RepositoryBase, ICompanyService
{
    private readonly ISystemModeService _modeService;
    private readonly IAuditService _auditService;

    public CompanyService(DbConnectionFactory factory, ISystemModeService modeService, IAuditService auditService) : base(factory)
    {
        _modeService = modeService;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<CompanyModel>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<CompanyModel>();
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, name, is_active
            from core.companies
            where is_deleted = false
            order by name
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new CompanyModel
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                IsActive = reader.GetBoolean(2)
            });
        }
        return list;
    }

    public async Task<CompanyModel?> GetCompanyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return (await GetCompaniesAsync(cancellationToken)).FirstOrDefault(c => c.Id == id);
    }

    public async Task SaveCompanyAsync(CompanyModel company, Guid userId, CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, userId, [UserRole.ADMIN], cancellationToken);
        if (string.IsNullOrWhiteSpace(company.Name))
        {
            throw new InvalidOperationException("اسم الشركة مطلوب.");
        }

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var isNew = company.Id == Guid.Empty;
        if (isNew)
        {
            company.Id = Guid.NewGuid();
        }

        if (!isNew)
        {
            await using var exists = new NpgsqlCommand("select exists(select 1 from core.companies where id = @id and not is_deleted for update)", connection, transaction);
            exists.Parameters.AddWithValue("id", company.Id);
            if (!Convert.ToBoolean(await exists.ExecuteScalarAsync(cancellationToken))) throw new InvalidOperationException("الشركة غير موجودة أو حُذفت مسبقاً.");
        }
        string sql = """
            insert into core.companies(id, name, is_active) values (@id, @name, @is_active)
            """;
        if (!isNew) sql = "update core.companies set name = @name, is_active = @is_active, updated_at = now() where id = @id and not is_deleted";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", company.Id);
        command.Parameters.AddWithValue("name", company.Name);
        command.Parameters.AddWithValue("is_active", company.IsActive);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteAuditRecordAsync(connection, transaction, userId, isNew ? "CREATE_COMPANY" : "UPDATE_COMPANY", "core", "companies", company.Id, company.Name, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteCompanyAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, userId, [UserRole.ADMIN], cancellationToken);

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var noteUsage = new NpgsqlCommand(
            "select exists(select 1 from core.notes where company_id = @id and not is_deleted)",
            connection,
            transaction))
        {
            noteUsage.Parameters.AddWithValue("id", id);
            if (Convert.ToBoolean(await noteUsage.ExecuteScalarAsync(cancellationToken)))
            {
                throw new InvalidOperationException("لا يمكن حذف شركة مرتبطة بمذكرات. يمكن تعطيلها بدلًا من ذلك.");
            }
        }

        await using (var factoryUsage = new NpgsqlCommand(
            "select exists(select 1 from core.factories where company_id = @id and not is_deleted)",
            connection,
            transaction))
        {
            factoryUsage.Parameters.AddWithValue("id", id);
            if (Convert.ToBoolean(await factoryUsage.ExecuteScalarAsync(cancellationToken)))
            {
                throw new InvalidOperationException("لا يمكن حذف شركة مرتبطة بمصانع. انقل المصانع أو احذفها أولًا.");
            }
        }

        await using (var command = new NpgsqlCommand(
            "update core.companies set is_deleted = true where id = @id and not is_deleted",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("id", id);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("الشركة غير موجودة أو حُذفت مسبقًا.");
            }
        }

        await WriteAuditRecordAsync(
            connection, transaction, userId, "DELETE_COMPANY", "core", "companies", id,
            "حذف منطقي للشركة", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

}

// Backup orchestration lives in AuditMemoSystem.Data.Backup.
public sealed class FactoryService : RepositoryBase, IFactoryService
{
    private readonly ISystemModeService _modeService;
    private readonly IAuditService _auditService;

    public FactoryService(
        DbConnectionFactory factory,
        ISystemModeService modeService,
        IAuditService auditService) : base(factory)
    {
        _modeService = modeService;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<FactoryModel>> GetFactoriesAsync(
        Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var list = new List<FactoryModel>();
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select f.id, f.name, f.company_id, c.name, f.is_active
            from core.factories f
            left join core.companies c on c.id = f.company_id
            where not f.is_deleted
              and (@company_id is null or f.company_id = @company_id)
            order by c.name, f.name
            """, connection);
        command.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = (object?)companyId ?? DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new FactoryModel
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                CompanyId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                CompanyName = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsActive = reader.GetBoolean(4)
            });
        }

        return list;
    }

    public async Task SaveFactoryAsync(
        FactoryModel factory,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, userId, [UserRole.ADMIN], cancellationToken);
        if (string.IsNullOrWhiteSpace(factory.Name) || factory.CompanyId is null)
        {
            throw new InvalidOperationException("اسم المصنع والشركة مطلوبان.");
        }

        var isNew = factory.Id == Guid.Empty;
        if (isNew)
        {
            factory.Id = Guid.NewGuid();
        }

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var companyCommand = new NpgsqlCommand(
            "select name from core.companies where id = @id and is_active and not is_deleted",
            connection,
            transaction))
        {
            companyCommand.Parameters.AddWithValue("id", factory.CompanyId.Value);
            factory.CompanyName = Convert.ToString(await companyCommand.ExecuteScalarAsync(cancellationToken))
                ?? throw new InvalidOperationException("الشركة المحددة غير موجودة أو غير فعالة.");
        }

        if (!isNew)
        {
            Guid existingCompanyId;
            await using (var existingCommand = new NpgsqlCommand(
                "select company_id from core.factories where id = @id and not is_deleted for update",
                connection,
                transaction))
            {
                existingCommand.Parameters.AddWithValue("id", factory.Id);
                var value = await existingCommand.ExecuteScalarAsync(cancellationToken);
                if (value is not Guid companyId)
                {
                    throw new InvalidOperationException("المصنع غير موجود أو حُذف مسبقًا.");
                }
                existingCompanyId = companyId;
            }

            if (existingCompanyId != factory.CompanyId.Value)
            {
                await using var usageCommand = new NpgsqlCommand(
                    "select exists(select 1 from core.notes where factory_id = @id and not is_deleted)",
                    connection,
                    transaction);
                usageCommand.Parameters.AddWithValue("id", factory.Id);
                if (Convert.ToBoolean(await usageCommand.ExecuteScalarAsync(cancellationToken)))
                {
                    throw new InvalidOperationException("لا يمكن نقل مصنع مستخدم في مذكرات إلى شركة أخرى.");
                }
            }
        }

        try
        {
            await using var command = new NpgsqlCommand("""
                insert into core.factories(id, name, company_id, is_active)
                values (@id, @name, @company_id, @is_active)
                on conflict (id) do update set
                    name = excluded.name,
                    company_id = excluded.company_id,
                    is_active = excluded.is_active,
                    updated_at = now()
                """, connection, transaction);
            command.Parameters.AddWithValue("id", factory.Id);
            command.Parameters.AddWithValue("name", factory.Name.Trim());
            command.Parameters.AddWithValue("company_id", factory.CompanyId.Value);
            command.Parameters.AddWithValue("is_active", factory.IsActive);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            throw new InvalidOperationException("يوجد مصنع بالاسم نفسه ضمن هذه الشركة.");
        }

        await WriteAuditRecordAsync(
            connection, transaction, userId, isNew ? "CREATE_FACTORY" : "UPDATE_FACTORY",
            "core", "factories", factory.Id,
            $"{factory.CompanyName} - {factory.Name.Trim()}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteFactoryAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, userId, [UserRole.ADMIN], cancellationToken);

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var usageCommand = new NpgsqlCommand(
            "select exists(select 1 from core.notes where factory_id = @id and not is_deleted)",
            connection,
            transaction))
        {
            usageCommand.Parameters.AddWithValue("id", id);
            if (Convert.ToBoolean(await usageCommand.ExecuteScalarAsync(cancellationToken)))
            {
                throw new InvalidOperationException("لا يمكن حذف مصنع مستخدم في مذكرات. يمكن تعطيله بدلًا من ذلك.");
            }
        }

        await using (var command = new NpgsqlCommand(
            "update core.factories set is_deleted = true, updated_at = now() where id = @id and not is_deleted",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("id", id);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("المصنع غير موجود أو حُذف مسبقًا.");
            }
        }

        await WriteAuditRecordAsync(
            connection, transaction, userId, "DELETE_FACTORY", "core", "factories", id,
            "حذف منطقي للمصنع", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
public sealed class ProductionLineService : RepositoryBase, IProductionLineService
{
    private readonly ISystemModeService _modeService;
    private readonly IAuditService _auditService;

    public ProductionLineService(DbConnectionFactory factory, ISystemModeService modeService, IAuditService auditService) : base(factory)
    {
        _modeService = modeService;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<ProductionLineModel>> GetProductionLinesAsync(Guid? factoryId = null, CancellationToken cancellationToken = default)
    {
        var result = new List<ProductionLineModel>();
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select pl.id, pl.factory_id, f.name, f.company_id, c.name, pl.name, pl.is_active
            from core.production_lines pl
            join core.factories f on f.id = pl.factory_id
            left join core.companies c on c.id = f.company_id
            where not pl.is_deleted and (@factory_id is null or pl.factory_id = @factory_id)
            order by c.name, f.name, pl.name
            """, connection);
        command.Parameters.Add("factory_id", NpgsqlDbType.Uuid).Value = (object?)factoryId ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProductionLineModel
            {
                Id = reader.GetGuid(0), FactoryId = reader.GetGuid(1), FactoryName = reader.GetString(2),
                CompanyId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
                CompanyName = reader.IsDBNull(4) ? null : reader.GetString(4),
                Name = reader.GetString(5), IsActive = reader.GetBoolean(6)
            });
        }
        return result;
    }

    public async Task SaveProductionLineAsync(ProductionLineModel line, Guid userId, CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, userId, [UserRole.ADMIN], cancellationToken);
        if (line.FactoryId == Guid.Empty || string.IsNullOrWhiteSpace(line.Name))
        {
            throw new InvalidOperationException("المصنع واسم خط الإنتاج مطلوبان.");
        }

        var isNew = line.Id == Guid.Empty;
        if (isNew) line.Id = Guid.NewGuid();
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var factoryCommand = new NpgsqlCommand("select name from core.factories where id = @id and is_active and not is_deleted", connection, transaction))
        {
            factoryCommand.Parameters.AddWithValue("id", line.FactoryId);
            line.FactoryName = Convert.ToString(await factoryCommand.ExecuteScalarAsync(cancellationToken))
                ?? throw new InvalidOperationException("المصنع المحدد غير موجود أو غير فعال.");
        }
        if (!isNew)
        {
            await using var existing = new NpgsqlCommand("select exists(select 1 from core.production_lines where id = @id and not is_deleted for update)", connection, transaction);
            existing.Parameters.AddWithValue("id", line.Id);
            if (!Convert.ToBoolean(await existing.ExecuteScalarAsync(cancellationToken))) throw new InvalidOperationException("خط الإنتاج غير موجود أو حُذف مسبقاً.");
        }
        try
        {
            var sql = isNew
                ? "insert into core.production_lines(id, factory_id, name, is_active) values (@id, @factory_id, @name, @is_active)"
                : "update core.production_lines set factory_id = @factory_id, name = @name, is_active = @is_active, updated_at = now() where id = @id and not is_deleted";
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", line.Id);
            command.Parameters.AddWithValue("factory_id", line.FactoryId);
            command.Parameters.AddWithValue("name", line.Name.Trim());
            command.Parameters.AddWithValue("is_active", line.IsActive);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            throw new InvalidOperationException("يوجد خط إنتاج بالاسم نفسه ضمن المصنع المحدد.");
        }
        await WriteAuditRecordAsync(connection, transaction, userId, isNew ? "CREATE_PRODUCTION_LINE" : "UPDATE_PRODUCTION_LINE", "core", "production_lines", line.Id, line.Name.Trim(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ToggleProductionLineAsync(Guid id, bool isActive, Guid userId, CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, userId, [UserRole.ADMIN], cancellationToken);
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand("update core.production_lines set is_active = @is_active, updated_at = now() where id = @id and not is_deleted", connection, transaction))
        {
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("is_active", isActive);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("خط الإنتاج غير موجود.");
            }
        }

        await WriteAuditRecordAsync(
            connection, transaction, userId, "TOGGLE_PRODUCTION_LINE", "core", "production_lines", id,
            isActive ? "تفعيل خط الإنتاج" : "تعطيل خط الإنتاج", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task DeleteProductionLineAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, userId, [UserRole.ADMIN], cancellationToken);
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var usage = new NpgsqlCommand("select exists(select 1 from core.notes where production_line_id = @id and not is_deleted)", connection, transaction))
        {
            usage.Parameters.AddWithValue("id", id);
            if (Convert.ToBoolean(await usage.ExecuteScalarAsync(cancellationToken)))
            {
                throw new InvalidOperationException("لا يمكن حذف خط إنتاج مستخدم في مذكرات.");
            }
        }
        await using (var command = new NpgsqlCommand("update core.production_lines set is_deleted = true, updated_at = now() where id = @id and not is_deleted", connection, transaction))
        {
            command.Parameters.AddWithValue("id", id);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("خط الإنتاج غير موجود.");
        }
        await WriteAuditRecordAsync(connection, transaction, userId, "DELETE_PRODUCTION_LINE", "core", "production_lines", id, "Soft delete production line", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class NoteTypeService : RepositoryBase, INoteTypeService
{
    private readonly ISystemModeService? _modeService;
    private readonly IAuditService? _auditService;

    public NoteTypeService(
        DbConnectionFactory factory,
        ISystemModeService? modeService = null,
        IAuditService? auditService = null) : base(factory)
    {
        _modeService = modeService;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<NoteTypeModel>> GetNoteTypesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<NoteTypeModel>();
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, code, name_ar, description, is_active
            from core.note_types
            where is_deleted = false
            order by code
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new NoteTypeModel
            {
                Id = reader.GetGuid(0),
                Code = reader.GetString(1),
                NameAr = reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsActive = reader.GetBoolean(4)
            });
        }
        return list;
    }

    public async Task<IReadOnlyList<NoteTypeFieldModel>> GetFieldsForTypeAsync(Guid noteTypeId, CancellationToken cancellationToken = default)
    {
        var list = new List<NoteTypeFieldModel>();
        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select ntf.id, ntf.note_type_id, nf.id, nf.code, nf.name_ar, ntf.sort_order, ntf.is_required,
                   (ntf.is_active and nf.is_active) as is_active
            from core.note_type_fields ntf
            join core.note_fields nf on nf.id = ntf.field_id
            where ntf.note_type_id = @note_type_id
              and ntf.is_deleted = false
              and nf.is_deleted = false
            order by ntf.sort_order, nf.id
            """, connection);
        command.Parameters.AddWithValue("note_type_id", noteTypeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new NoteTypeFieldModel
            {
                Id = reader.GetGuid(0),
                NoteTypeId = reader.GetGuid(1),
                FieldId = reader.GetGuid(2),
                FieldCode = reader.GetString(3),
                FieldNameAr = reader.GetString(4),
                SortOrder = reader.GetInt32(5),
                IsRequired = reader.GetBoolean(6),
                IsActive = reader.GetBoolean(7)
            });
        }
        return list;
    }

    public async Task<NoteTypeFieldImpactPreviewModel> PreviewFieldDefinitionAsync(
        NoteTypeFieldDefinitionModel definition,
        Guid requesterId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await EnsureAdminAsync(requesterId, cancellationToken);

        if (definition.NoteTypeId == Guid.Empty)
        {
            throw new InvalidOperationException("يجب تحديد نوع المذكرة.");
        }

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select count(*)::int,
                   count(nfv.id)::int,
                   count(*) filter (where nfv.id is null or nfv.field_value = 0)::int
            from core.notes n
            left join core.note_field_values nfv
              on nfv.note_id = n.id
             and (@field_id is not null and nfv.field_id = @field_id)
            where n.note_type_id = @note_type_id
              and not n.is_deleted
            """, connection);
        command.Parameters.Add("field_id", NpgsqlDbType.Uuid).Value = (object?)definition.FieldId ?? DBNull.Value;
        command.Parameters.AddWithValue("note_type_id", definition.NoteTypeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new NoteTypeFieldImpactPreviewModel
            {
                NoteTypeId = definition.NoteTypeId,
                FieldId = definition.FieldId,
                IsNewField = !definition.FieldId.HasValue
            };
        }

        return new NoteTypeFieldImpactPreviewModel
        {
            NoteTypeId = definition.NoteTypeId,
            FieldId = definition.FieldId,
            AffectedNoteCount = reader.GetInt32(2),
            ExistingValueCount = reader.GetInt32(1),
            MissingOrUnreviewedCount = reader.GetInt32(2),
            IsNewField = !definition.FieldId.HasValue
        };
    }

    public async Task<NoteTypeFieldDefinitionModel> SaveFieldDefinitionAsync(
        NoteTypeFieldDefinitionModel definition,
        Guid requesterId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await EnsureAdminAsync(requesterId, cancellationToken);
        _modeService?.EnsureWritable();

        var code = definition.FieldCode.Trim();
        var name = definition.FieldNameAr.Trim();
        if (definition.NoteTypeId == Guid.Empty || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("نوع المذكرة ورمز البند واسمه مطلوبة.");
        }

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@lock_key, 712))",
            connection,
            transaction))
        {
            lockCommand.Parameters.AddWithValue("lock_key", $"note-type-fields:{definition.NoteTypeId:N}");
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        string? beforeJson = null;
        Guid? existingFieldId = null;
        if (definition.Id != Guid.Empty)
        {
            await using var currentCommand = new NpgsqlCommand("""
                select ntf.field_id,
                       jsonb_build_object(
                           'fieldCode', nf.code,
                           'fieldNameAr', nf.name_ar,
                           'sortOrder', ntf.sort_order,
                           'isRequired', ntf.is_required,
                           'isActive', ntf.is_active
                       )::text
                from core.note_type_fields ntf
                join core.note_fields nf on nf.id = ntf.field_id
                where ntf.id = @id
                  and ntf.note_type_id = @note_type_id
                  and not ntf.is_deleted
                for update of ntf, nf
                """, connection, transaction);
            currentCommand.Parameters.AddWithValue("id", definition.Id);
            currentCommand.Parameters.AddWithValue("note_type_id", definition.NoteTypeId);
            await using var currentReader = await currentCommand.ExecuteReaderAsync(cancellationToken);
            if (!await currentReader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("تعريف البند غير موجود.");
            }
            existingFieldId = currentReader.GetGuid(0);
            beforeJson = currentReader.IsDBNull(1) ? null : currentReader.GetString(1);
        }

        var fieldId = existingFieldId ?? definition.FieldId;
        if (fieldId.HasValue && definition.FieldId.HasValue && existingFieldId.HasValue && fieldId != definition.FieldId)
        {
            throw new InvalidOperationException("لا يمكن تغيير معرّف البند المرتبط؛ أنشئ تعريفًا جديدًا بدلًا منه.");
        }

        if (!fieldId.HasValue)
        {
            await using var findField = new NpgsqlCommand(
                "select id from core.note_fields where lower(code) = lower(@code) for update",
                connection,
                transaction);
            findField.Parameters.AddWithValue("code", code);
            var found = await findField.ExecuteScalarAsync(cancellationToken);
            if (found is Guid existingId)
            {
                fieldId = existingId;
            }
            else
            {
                fieldId = Guid.NewGuid();
                await using var insertField = new NpgsqlCommand("""
                    insert into core.note_fields(id, code, name_ar, description, default_sort_order, is_active, is_deleted)
                    values (@id, @code, @name_ar, @description, @sort_order, true, false)
                    """, connection, transaction);
                insertField.Parameters.AddWithValue("id", fieldId.Value);
                insertField.Parameters.AddWithValue("code", code);
                insertField.Parameters.AddWithValue("name_ar", name);
                insertField.Parameters.AddWithValue("description", (object?)definition.Description ?? DBNull.Value);
                insertField.Parameters.AddWithValue("sort_order", definition.SortOrder);
                await insertField.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var updateField = new NpgsqlCommand("""
            update core.note_fields
            set name_ar = @name_ar,
                description = @description,
                default_sort_order = @sort_order,
                is_active = true,
                is_deleted = false,
                updated_at = now()
            where id = @field_id
            """, connection, transaction))
        {
            updateField.Parameters.AddWithValue("field_id", fieldId!.Value);
            updateField.Parameters.AddWithValue("name_ar", name);
            updateField.Parameters.AddWithValue("description", (object?)definition.Description ?? DBNull.Value);
            updateField.Parameters.AddWithValue("sort_order", definition.SortOrder);
            await updateField.ExecuteNonQueryAsync(cancellationToken);
        }

        Guid definitionId;
        await using (var upsert = new NpgsqlCommand("""
            insert into core.note_type_fields(
                id, note_type_id, field_id, sort_order, is_required, is_active, is_deleted)
            values (
                coalesce(@definition_id, gen_random_uuid()), @note_type_id, @field_id,
                @sort_order, @is_required, @is_active, false)
            on conflict (note_type_id, field_id) do update set
                sort_order = excluded.sort_order,
                is_required = excluded.is_required,
                is_active = excluded.is_active,
                is_deleted = false,
                updated_at = now()
            returning id
            """, connection, transaction))
        {
            upsert.Parameters.Add("definition_id", NpgsqlDbType.Uuid).Value =
                definition.Id == Guid.Empty ? DBNull.Value : definition.Id;
            upsert.Parameters.AddWithValue("note_type_id", definition.NoteTypeId);
            upsert.Parameters.AddWithValue("field_id", fieldId.Value);
            upsert.Parameters.AddWithValue("sort_order", definition.SortOrder);
            upsert.Parameters.AddWithValue("is_required", definition.IsRequired);
            upsert.Parameters.AddWithValue("is_active", definition.IsActive);
            definitionId = (Guid)(await upsert.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("تعذر حفظ تعريف البند."));
        }

        var afterJson = JsonSerializer.Serialize(new
        {
            fieldId,
            fieldCode = code,
            fieldNameAr = name,
            definition.SortOrder,
            definition.IsRequired,
            definition.IsActive
        });
        await WriteStructuredAuditRecordAsync(
            connection,
            transaction,
            requesterId,
            definition.Id == Guid.Empty ? "CREATE_NOTE_TYPE_FIELD" : "UPDATE_NOTE_TYPE_FIELD",
            "core",
            "note_type_fields",
            definitionId,
            "تغيير تعريف بند مع تسجيل الحالة السابقة واللاحقة",
            beforeJson,
            afterJson,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        definition.Id = definitionId;
        definition.FieldId = fieldId;
        definition.FieldCode = code;
        definition.FieldNameAr = name;
        return definition;
    }

    public async Task DeactivateFieldDefinitionAsync(
        Guid definitionId,
        Guid requesterId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAdminAsync(requesterId, cancellationToken);
        _modeService?.EnsureWritable();

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select ntf.note_type_id, ntf.field_id,
                   jsonb_build_object(
                       'isActive', ntf.is_active,
                       'isDeleted', ntf.is_deleted
                   )::text
            from core.note_type_fields ntf
            where ntf.id = @id
              and not ntf.is_deleted
            for update
            """, connection, transaction);
        command.Parameters.AddWithValue("id", definitionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("تعريف البند غير موجود.");
        }
        var noteTypeId = reader.GetGuid(0);
        var fieldId = reader.GetGuid(1);
        var beforeJson = reader.IsDBNull(2) ? null : reader.GetString(2);
        await reader.CloseAsync();

        await using (var update = new NpgsqlCommand("""
            update core.note_type_fields
            set is_active = false,
                is_deleted = false,
                updated_at = now()
            where id = @id
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("id", definitionId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var afterJson = JsonSerializer.Serialize(new { isActive = false, isDeleted = false, fieldId });
        await WriteStructuredAuditRecordAsync(
            connection,
            transaction,
            requesterId,
            "DEACTIVATE_NOTE_TYPE_FIELD",
            "core",
            "note_type_fields",
            definitionId,
            "تعطيل تعريف بند دون حذف القيم التاريخية",
            beforeJson,
            afterJson,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureAdminAsync(Guid requesterId, CancellationToken cancellationToken)
    {
        await EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN], cancellationToken);
    }

    private static async Task WriteStructuredAuditRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        string actionType,
        string schemaName,
        string tableName,
        Guid recordId,
        string description,
        string? beforeJson,
        string afterJson,
        CancellationToken cancellationToken)
    {
        beforeJson = NormalizeJsonForPostgres(beforeJson ?? "null");
        afterJson = NormalizeJsonForPostgres(afterJson);
        await using var command = new NpgsqlCommand("""
            insert into audit.logs(
                user_id, action_type, schema_name, table_name, record_id, description, old_value, new_value)
            values (
                @user_id, @action_type, @schema_name, @table_name, @record_id, @description,
                @old_value::jsonb, @new_value::jsonb)
            """, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("schema_name", schemaName);
        command.Parameters.AddWithValue("table_name", tableName);
        command.Parameters.AddWithValue("record_id", recordId);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue("old_value", beforeJson);
        command.Parameters.AddWithValue("new_value", afterJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
public sealed class NoteService : RepositoryBase, INoteService
{
	private readonly ISystemModeService _modeService;

	private readonly INoteTypeService _noteTypeService;

	private readonly IAuditService _auditService;

	public NoteService(DbConnectionFactory factory, ISystemModeService modeService, INoteTypeService noteTypeService, IAuditService auditService)
		: base(factory)
	{
		_modeService = modeService;
		_noteTypeService = noteTypeService;
		_auditService = auditService;
	}

	private async Task EnsureNoteEditorAsync(Guid userId, NoteModel? existing, CancellationToken cancellationToken)
	{
		switch (await RepositoryBase.GetUserRoleAsync(base.Factory, userId, cancellationToken))
		{
		case UserRole.ADMIN:
			break;
		default:
			throw new UnauthorizedAccessException("إدخال أو تعديل المذكرات متاح لمدخل البيانات أو مدير النظام فقط.");
		case UserRole.DATA_ENTRY:
		{
			Guid? guid = existing?.CreatedBy;
			int num;
			if (guid.HasValue)
			{
				Guid createdBy = guid.GetValueOrDefault();
				num = ((createdBy != userId) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			if (num != 0)
			{
				throw new UnauthorizedAccessException("لا يمكنك تعديل مذكرة أدخلها مستخدم آخر.");
			}
			break;
		}
		}
	}

	private async Task EnsureNoteReadAccessAsync(Guid userId, NoteModel note, CancellationToken cancellationToken)
	{
		if (await RepositoryBase.GetUserRoleAsync(base.Factory, userId, cancellationToken) == UserRole.DATA_ENTRY && note.CreatedBy != userId)
		{
			throw new UnauthorizedAccessException("لا يمكنك عرض مذكرة أدخلها مستخدم آخر.");
		}
	}

	public async Task<PagedResult<NoteDetailsModel>> SearchNotesAsync(
		NoteSearchFilterModel filter,
		Guid requesterId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(filter);

		if (await RepositoryBase.GetUserRoleAsync(Factory, requesterId, cancellationToken) == UserRole.DATA_ENTRY)
		{
			filter.CreatedBy = requesterId;
		}

		if (filter.From > filter.To)
		{
			throw new InvalidOperationException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");
		}

		var pageNumber = Math.Max(1, filter.PageNumber);
		var pageSize = Math.Clamp(filter.PageSize, 1, 200);

		const string joins = """
			from reporting.vw_note_details d
			join core.notes n on n.id = d.note_id and n.is_deleted = false
			join core.note_types note_type_scope on note_type_scope.id = d.note_type_id
			""";

		var useWritingDate = string.Equals(filter.DateBasis, "WritingDate", StringComparison.OrdinalIgnoreCase);
		var dateCondition = useWritingDate
			? "(case when d.writing_date is not null then d.writing_date between @from and @to else (@has_writing_date is null and d.arrival_at::date between @from and @to) end)"
			: "d.arrival_at::date between @from and @to";

		var where = $"""
			where {dateCondition}
			  and (@has_writing_date is null
			       or (@has_writing_date = true and d.writing_date is not null)
			       or (@has_writing_date = false and d.writing_date is null))
			  and (@company_id is null or d.company_id = @company_id)
			  and (@factory_id is null or d.factory_id = @factory_id)
			  and (@production_line_id is null or d.production_line_id = @production_line_id)
			  and (@note_type_id is null or d.note_type_id = @note_type_id)
			  and (@created_by is null or d.created_by = @created_by)
			  and (@status is null or d.status = @status)
			  and (@note_number is null or d.note_number ilike @note_number)
			  and (@entry_cutoff_date is null or d.arrival_at::date <= @entry_cutoff_date)
			  and (@is_loot is null or (
			      case when @is_loot = true
			           then (coalesce(n.is_loot, false) = true or exists (select 1 from core.note_sources s where s.note_id = d.note_id and s.source_type = 'LOOT'))
			           else (coalesce(n.is_loot, false) = false and not exists (select 1 from core.note_sources s where s.note_id = d.note_id and s.source_type = 'LOOT'))
			      end
			  ))
			  and (@is_invoice is null or n.is_invoice = @is_invoice)
			  and (@unlinked_only is not true or (
			      note_type_scope.code = 'ENTRY'
			      and not coalesce(n.is_loot, false)
			      and not exists (select 1 from core.note_sources s where s.note_id = d.note_id and s.source_type = 'LOOT')
			      and not exists (
			          select 1
			          from core.v_note_links_compat unlinked_link
			          where unlinked_link.source_note_id = d.note_id
			             or unlinked_link.related_note_id = d.note_id
			      )
			  ))
			""";

		await using var connection = Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);

		void AddParameters(NpgsqlCommand command)
		{
			command.Parameters.AddWithValue("from", filter.From);
			command.Parameters.AddWithValue("to", filter.To);
			command.Parameters.Add("has_writing_date", NpgsqlDbType.Boolean).Value = (object?)filter.HasWritingDate ?? DBNull.Value;
			command.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = (object?)filter.CompanyId ?? DBNull.Value;
			command.Parameters.Add("factory_id", NpgsqlDbType.Uuid).Value = (object?)filter.FactoryId ?? DBNull.Value;
			command.Parameters.Add("production_line_id", NpgsqlDbType.Uuid).Value = (object?)filter.ProductionLineId ?? DBNull.Value;
			command.Parameters.Add("note_type_id", NpgsqlDbType.Uuid).Value = (object?)filter.NoteTypeId ?? DBNull.Value;
			command.Parameters.Add("created_by", NpgsqlDbType.Uuid).Value = (object?)filter.CreatedBy ?? DBNull.Value;
			command.Parameters.Add("status", NpgsqlDbType.Varchar).Value = (object?)filter.Status?.ToString() ?? DBNull.Value;
			command.Parameters.Add("note_number", NpgsqlDbType.Varchar).Value = string.IsNullOrWhiteSpace(filter.NoteNumber) ? DBNull.Value : $"%{filter.NoteNumber.Trim()}%";
			command.Parameters.Add("entry_cutoff_date", NpgsqlDbType.Date).Value = (object?)filter.EntryCutoffDate ?? DBNull.Value;
			command.Parameters.Add("unlinked_only", NpgsqlDbType.Boolean).Value = (object?)filter.UnlinkedOnly ?? DBNull.Value;
			command.Parameters.Add("is_loot", NpgsqlDbType.Boolean).Value = (object?)filter.IsLoot ?? DBNull.Value;
			command.Parameters.Add("is_invoice", NpgsqlDbType.Boolean).Value = (object?)filter.IsInvoice ?? DBNull.Value;
		}

		int totalCount;
		await using (var countCommand = new NpgsqlCommand("select count(*) " + joins + " " + where, connection))
		{
			AddParameters(countCommand);
			totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
		}

		var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
		if (totalPages > 0)
		{
			pageNumber = Math.Min(pageNumber, totalPages);
		}

		var items = new List<NoteDetailsModel>(Math.Min(pageSize, totalCount));
		var sql = """
			select d.note_id, d.note_number, d.note_type_name, d.company_name,
			       d.writing_date, d.arrival_at, d.status,
			       d.total_fields, d.active_count, d.inactive_count, d.unknown_count,
			       d.accuracy_percentage, d.error_percentage,
			       d.created_by, d.created_by_name, d.created_at, d.review_started_at,
			       d.approved_at, d.approved_by_name, n.updated_at,
			       d.factory_id, d.factory_name, d.production_line_id, d.production_line_name,
			       n.is_loot, n.is_invoice,
			       (select count(*) from core.note_links nl where nl.source_note_id = d.note_id or nl.related_note_id = d.note_id) as link_count
			""" + " " + joins + " " + where + """
			order by d.arrival_at desc, d.created_at desc, d.note_id desc
			limit @page_size offset @offset
			""";

		await using (var command = new NpgsqlCommand(sql, connection))
		{
			AddParameters(command);
			command.Parameters.AddWithValue("page_size", pageSize);
			command.Parameters.AddWithValue("offset", (pageNumber - 1) * pageSize);

			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				items.Add(new NoteDetailsModel
				{
					NoteId = reader.GetGuid(0),
					NoteNumber = reader.GetString(1),
					NoteTypeName = reader.GetString(2),
					CompanyName = reader.GetString(3),
					WritingDate = RepositoryBase.ReadNullableDate(reader, "writing_date"),
					ArrivalAt = RepositoryBase.ReadDate(reader, "arrival_at"),
					Status = RepositoryBase.EnumValue<NoteStatus>(reader.GetString(6)),
					ImagePath = null,
					TotalFields = reader.GetInt32(7),
					ActiveCount = reader.GetInt32(8),
					InactiveCount = reader.GetInt32(9),
					UnknownCount = reader.GetInt32(10),
					AccuracyPercentage = reader.GetDecimal(11),
					ErrorPercentage = reader.GetDecimal(12),
					CreatedBy = reader.IsDBNull(13) ? null : reader.GetGuid(13),
					CreatedByName = reader.IsDBNull(14) ? null : reader.GetString(14),
					CreatedAt = reader.GetDateTime(15),
					ReviewStartedAt = reader.IsDBNull(16) ? null : reader.GetDateTime(16),
					ApprovedAt = reader.IsDBNull(17) ? null : reader.GetDateTime(17),
					ApprovedByName = reader.IsDBNull(18) ? null : reader.GetString(18),
					UpdatedAt = reader.GetDateTime(19),
					FactoryId = reader.IsDBNull(20) ? null : reader.GetGuid(20),
					FactoryName = reader.IsDBNull(21) ? null : reader.GetString(21),
					ProductionLineId = reader.IsDBNull(22) ? null : reader.GetGuid(22),
					ProductionLineName = reader.IsDBNull(23) ? null : reader.GetString(23),
					IsLoot = !reader.IsDBNull(24) && reader.GetBoolean(24),
					IsInvoice = !reader.IsDBNull(25) && reader.GetBoolean(25),
					LinkCount = reader.IsDBNull(26) ? 0 : Convert.ToInt32(reader.GetValue(26))
				});
			}
		}

		return new PagedResult<NoteDetailsModel>
		{
			Items = items,
			TotalCount = totalCount,
			PageNumber = pageNumber,
			PageSize = pageSize
		};
	}

	public async Task<PagedResult<IncompleteIndicatorQueueItemModel>> SearchIncompleteIndicatorsAsync(
		IncompleteIndicatorFilterModel filter,
		Guid requesterId,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(filter);
		await EnsureRoleAsync(Factory, requesterId, [UserRole.REVIEWER, UserRole.ADMIN], cancellationToken);

		var requestedPageNumber = Math.Max(1, filter.PageNumber);
		var pageSize = Math.Clamp(filter.PageSize, 1, 200);
		var items = new List<IncompleteIndicatorQueueItemModel>();

		const string incompleteCte = """
			with incomplete as (
				select
					n.id as note_id,
					n.updated_at,
					n.note_number,
					nt.name_ar as note_type_name,
					c.name as company_name,
					factory.name as factory_name,
					production_line.name as production_line_name,
					n.arrival_at,
					n.created_at,
					n.created_by,
					creator.full_name as created_by_name,
					n.image_path,
					n.attachment_file_name,
					nf.id as field_id,
					nf.name_ar as field_name,
					ntf.sort_order,
					coalesce(nfv.field_value, 0)::smallint as current_value,
					count(*) over (partition by n.id)::int as incomplete_field_count
				from core.notes n
				join core.note_types nt
				  on nt.id = n.note_type_id
				 and not nt.is_deleted
				join core.companies c
				  on c.id = n.company_id
				left join core.factories factory
				  on factory.id = n.factory_id
				left join core.production_lines production_line
				  on production_line.id = n.production_line_id
				left join auth.users creator
				  on creator.id = n.created_by
				join core.note_type_fields ntf
				  on ntf.note_type_id = n.note_type_id
				 and ntf.is_active
				 and not ntf.is_deleted
				join core.note_fields nf
				  on nf.id = ntf.field_id
				 and nf.is_active
				 and not nf.is_deleted
				left join core.note_field_values nfv
				  on nfv.note_id = n.id
				 and nfv.field_id = nf.id
				where not n.is_deleted
				  and (nfv.id is null or nfv.field_value = 0)
				  and (@company_id is null or n.company_id = @company_id)
				  and (@note_type_id is null or n.note_type_id = @note_type_id)
				  and (@field_id is null or nf.id = @field_id)
				  and (@created_by is null or n.created_by = @created_by)
				  and (@from_date is null or n.arrival_at::date >= @from_date)
				  and (@to_date is null or n.arrival_at::date <= @to_date)
			)
			""";

		await using var connection = Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
		await using var transactionModeCommand = new NpgsqlCommand("SET TRANSACTION READ ONLY;", connection, transaction);
		await transactionModeCommand.ExecuteNonQueryAsync(cancellationToken);

		void AddFilterParameters(NpgsqlCommand target)
		{
			target.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = (object?)filter.CompanyId ?? DBNull.Value;
			target.Parameters.Add("note_type_id", NpgsqlDbType.Uuid).Value = (object?)filter.NoteTypeId ?? DBNull.Value;
			target.Parameters.Add("field_id", NpgsqlDbType.Uuid).Value = (object?)filter.FieldId ?? DBNull.Value;
			target.Parameters.Add("created_by", NpgsqlDbType.Uuid).Value = (object?)filter.CreatedBy ?? DBNull.Value;
			target.Parameters.Add("from_date", NpgsqlDbType.Date).Value = (object?)filter.From ?? DBNull.Value;
			target.Parameters.Add("to_date", NpgsqlDbType.Date).Value = (object?)filter.To ?? DBNull.Value;
		}

		await using var countCommand = new NpgsqlCommand(incompleteCte + "select count(*)::int from incomplete;", connection, transaction);
		AddFilterParameters(countCommand);
		var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
		var pageCount = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
		var pageNumber = Math.Min(requestedPageNumber, pageCount);
		var offset = checked((pageNumber - 1) * pageSize);

		const string pageSelect = """
			select
				note_id,
				updated_at,
				note_number,
				note_type_name,
				company_name,
				factory_name,
				production_line_name,
				arrival_at,
				created_at,
				created_by,
				created_by_name,
				image_path,
				attachment_file_name,
				field_id,
				field_name,
				sort_order,
				current_value,
				incomplete_field_count,
				count(*) over()::int as queue_total_count
			from incomplete
			order by arrival_at desc, created_at desc, note_id desc, sort_order, field_id
			limit @page_size
			offset @offset
			""";
		await using var command = new NpgsqlCommand(incompleteCte + pageSelect, connection, transaction);
		AddFilterParameters(command);
		command.Parameters.AddWithValue("page_size", pageSize);
		command.Parameters.AddWithValue("offset", offset);

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			items.Add(new IncompleteIndicatorQueueItemModel
			{
				NoteId = reader.GetGuid(reader.GetOrdinal("note_id")),
				UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
				NoteNumber = reader.GetString(reader.GetOrdinal("note_number")),
				NoteTypeName = reader.GetString(reader.GetOrdinal("note_type_name")),
				CompanyName = reader.GetString(reader.GetOrdinal("company_name")),
				FactoryName = reader.IsDBNull(reader.GetOrdinal("factory_name")) ? null : reader.GetString(reader.GetOrdinal("factory_name")),
				ProductionLineName = reader.IsDBNull(reader.GetOrdinal("production_line_name")) ? null : reader.GetString(reader.GetOrdinal("production_line_name")),
				ArrivalAt = ReadDate(reader, "arrival_at"),
				CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
				CreatedBy = reader.IsDBNull(reader.GetOrdinal("created_by")) ? null : reader.GetGuid(reader.GetOrdinal("created_by")),
				CreatedByName = reader.IsDBNull(reader.GetOrdinal("created_by_name")) ? null : reader.GetString(reader.GetOrdinal("created_by_name")),
				ImagePath = reader.IsDBNull(reader.GetOrdinal("image_path")) ? null : reader.GetString(reader.GetOrdinal("image_path")),
				AttachmentFileName = reader.IsDBNull(reader.GetOrdinal("attachment_file_name")) ? null : reader.GetString(reader.GetOrdinal("attachment_file_name")),
				FieldId = reader.GetGuid(reader.GetOrdinal("field_id")),
				FieldName = reader.GetString(reader.GetOrdinal("field_name")),
				SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
				CurrentValue = (IndicatorValue)reader.GetInt16(reader.GetOrdinal("current_value")),
				IncompleteFieldCount = reader.GetInt32(reader.GetOrdinal("incomplete_field_count")),
				QueueTotalCount = reader.GetInt32(reader.GetOrdinal("queue_total_count"))
			});
		}

		await reader.CloseAsync();
		await transaction.CommitAsync(cancellationToken);
		return new PagedResult<IncompleteIndicatorQueueItemModel>
		{
			Items = items,
			TotalCount = totalCount,
			PageNumber = pageNumber,
			PageSize = pageSize
		};
	}
	public async Task CompleteUnreviewedFieldAsync(
		Guid noteId,
		Guid fieldId,
		IndicatorValue value,
		DateTime expectedUpdatedAt,
		Guid reviewerId,
		string? reason,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		if (value is IndicatorValue.Unreviewed or < IndicatorValue.Correct or > IndicatorValue.NotApplicable)
		{
			throw new InvalidOperationException("يجب اختيار قيمة مراجعة من 1 إلى 6، أو ترك البند دون حفظ.");
		}

		if (value is IndicatorValue.Incorrect or IndicatorValue.Missing && string.IsNullOrWhiteSpace(reason))
		{
			throw new InvalidOperationException("سبب القيمة غير الصحيحة أو المفقودة مطلوب.");
		}

		await EnsureRoleAsync(Factory, reviewerId, [UserRole.REVIEWER, UserRole.ADMIN], cancellationToken);
		_modeService.EnsureWritable();

		await using var connection = Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

		Guid noteTypeId;
		DateTime actualUpdatedAt;
		await using (var noteCommand = new NpgsqlCommand("""
			select note_type_id, updated_at
			from core.notes
			where id = @note_id and not is_deleted
			for update
			""", connection, transaction))
		{
			noteCommand.Parameters.AddWithValue("note_id", noteId);
			await using var reader = await noteCommand.ExecuteReaderAsync(cancellationToken);
			if (!await reader.ReadAsync(cancellationToken))
			{
				throw new InvalidOperationException("المذكرة حُذفت أو لم تعد متاحة في قائمة الاستكمال.");
			}

			noteTypeId = reader.GetGuid(0);
			actualUpdatedAt = reader.GetDateTime(1);
		}

		if (actualUpdatedAt != expectedUpdatedAt)
		{
			throw new InvalidOperationException("تغيرت المذكرة منذ تحميلها. أعد تحميل القائمة واعرض القيم الجديدة قبل الحفظ.");
		}

		string fieldName;
		await using (var fieldCommand = new NpgsqlCommand("""
			select nf.name_ar
			from core.note_type_fields ntf
			join core.note_fields nf on nf.id = ntf.field_id
			where ntf.note_type_id = @note_type_id
			  and ntf.field_id = @field_id
			  and ntf.is_active
			  and not ntf.is_deleted
			  and nf.is_active
			  and not nf.is_deleted
			""", connection, transaction))
		{
			fieldCommand.Parameters.AddWithValue("note_type_id", noteTypeId);
			fieldCommand.Parameters.AddWithValue("field_id", fieldId);
			fieldName = (await fieldCommand.ExecuteScalarAsync(cancellationToken) as string)
				?? throw new InvalidOperationException("تعطل البند أو لم يعد مرتبطًا بنوع المذكرة؛ أعد تحميل القائمة.");
		}

		short beforeValue;
		await using (var valueCommand = new NpgsqlCommand("""
			select field_value
			from core.note_field_values
			where note_id = @note_id and field_id = @field_id
			for update
			""", connection, transaction))
		{
			valueCommand.Parameters.AddWithValue("note_id", noteId);
			valueCommand.Parameters.AddWithValue("field_id", fieldId);
			var current = await valueCommand.ExecuteScalarAsync(cancellationToken);
			beforeValue = current is null or DBNull ? (short)0 : Convert.ToInt16(current);
		}

		if (beforeValue != (short)IndicatorValue.Unreviewed)
		{
			throw new InvalidOperationException("تغيرت قيمة هذا البند بواسطة مراجع آخر. أعد تحميل القائمة قبل الحفظ.");
		}

		var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
		await using (var upsertCommand = new NpgsqlCommand("""
			insert into core.note_field_values(
				note_id, field_id, field_value, inactive_reason, created_by, updated_by)
			values (@note_id, @field_id, @field_value, @reason, @reviewer_id, @reviewer_id)
			on conflict (note_id, field_id) do update set
				field_value = excluded.field_value,
				inactive_reason = excluded.inactive_reason,
				updated_by = excluded.updated_by,
				updated_at = now()
			""", connection, transaction))
		{
			upsertCommand.Parameters.AddWithValue("note_id", noteId);
			upsertCommand.Parameters.AddWithValue("field_id", fieldId);
			upsertCommand.Parameters.AddWithValue("field_value", (short)value);
			upsertCommand.Parameters.AddWithValue("reason", (object?)normalizedReason ?? DBNull.Value);
			upsertCommand.Parameters.AddWithValue("reviewer_id", reviewerId);
			await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		await using (var noteUpdateCommand = new NpgsqlCommand("""
			update core.notes
			set updated_at = now(), updated_by = @reviewer_id
			where id = @note_id and not is_deleted and updated_at = @expected_updated_at
			""", connection, transaction))
		{
			noteUpdateCommand.Parameters.AddWithValue("note_id", noteId);
			noteUpdateCommand.Parameters.AddWithValue("reviewer_id", reviewerId);
			noteUpdateCommand.Parameters.AddWithValue("expected_updated_at", expectedUpdatedAt);
			if (await noteUpdateCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
			{
				throw new InvalidOperationException("تغيرت المذكرة أثناء الحفظ. لم تُعتمد القيمة؛ أعد تحميل القائمة.");
			}
		}

		var now = DateTime.UtcNow;
		var oldJson = NormalizeJsonForPostgres(JsonSerializer.Serialize(new
		{
			noteId,
			fieldId,
			fieldName,
			value = (IndicatorValue)beforeValue,
			reviewerId,
			timestamp = now
		}));
		var newJson = NormalizeJsonForPostgres(JsonSerializer.Serialize(new
		{
			noteId,
			fieldId,
			fieldName,
			value,
			reason = normalizedReason,
			reviewerId,
			timestamp = now
		}));
		await using (var auditCommand = new NpgsqlCommand("""
			insert into audit.logs(
				user_id, action_type, schema_name, table_name, record_id, description, old_value, new_value)
			values (
				@reviewer_id, 'COMPLETE_UNREVIEWED_FIELD', 'core', 'note_field_values', @note_id,
				@description, @old_value::jsonb, @new_value::jsonb)
			""", connection, transaction))
		{
			auditCommand.Parameters.AddWithValue("reviewer_id", reviewerId);
			auditCommand.Parameters.AddWithValue("note_id", noteId);
			auditCommand.Parameters.AddWithValue("description", $"استكمال البند «{fieldName}» من قائمة البنود غير المراجعة.");
			auditCommand.Parameters.AddWithValue("old_value", oldJson);
			auditCommand.Parameters.AddWithValue("new_value", newJson);
			await auditCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		await WriteReviewHistoryAsync(
			connection,
			transaction,
			noteId,
			"COMPLETE_UNREVIEWED_FIELD",
			$"البند: {fieldName}; السابق: {(IndicatorValue)beforeValue}; الجديد: {value}",
			reviewerId,
			cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<NoteModel?> GetNoteAsync(Guid id, Guid requesterId, CancellationToken cancellationToken = default(CancellationToken))
	{
		NoteModel note = await GetNoteInternalAsync(id, cancellationToken);
		if (note != null)
		{
			await EnsureNoteReadAccessAsync(requesterId, note, cancellationToken);
		}
		return note;
	}

	private async Task<NoteModel?> GetNoteInternalAsync(Guid id, CancellationToken cancellationToken = default(CancellationToken))
	{
		NoteModel result;
		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			NoteModel noteModel6;
			await using (NpgsqlCommand command = new NpgsqlCommand("select n.id, n.note_number, n.note_type_id, nt.name_ar, n.company_id, c.name, n.writing_date, n.arrival_at,\r\n       n.image_path, n.file_path, n.attachment_file_name, n.attachment_content_type, n.attachment_data,\r\n       n.notes, n.status, n.created_by, n.updated_by, n.approved_by, n.approved_at,\r\n       n.reviewer_id, reviewer.full_name, n.review_started_at, n.return_reason, n.duplicate_reason,\r\n        n.factory_id, factory.name, n.production_line_id, production_line.name, n.created_at, creator.full_name, approver.full_name, n.updated_at,\r\n       (n.is_loot or exists (select 1 from core.note_sources s where s.note_id = n.id and s.source_type = 'LOOT')) as is_loot, n.is_invoice\r\nfrom core.notes n\r\njoin core.note_types nt on nt.id = n.note_type_id\r\njoin core.companies c on c.id = n.company_id\r\nleft join auth.users reviewer on reviewer.id = n.reviewer_id\r\nleft join core.factories factory on factory.id = n.factory_id\r\n left join core.production_lines production_line on production_line.id = n.production_line_id\r\n left join auth.users creator on creator.id = n.created_by\r\n left join auth.users approver on approver.id = n.approved_by\r\nwhere n.id = @id and n.is_deleted = false", connection))
			{
				command.Parameters.AddWithValue("id", id);
				NoteModel noteModel;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					if (!(await reader.ReadAsync(cancellationToken)))
					{
						noteModel = null;
					}
					else
					{
						NoteModel note = new NoteModel
						{
							Id = reader.GetGuid(0),
							NoteNumber = reader.GetString(1),
							NoteTypeId = reader.GetGuid(2),
							NoteTypeName = reader.GetString(3),
							CompanyId = reader.GetGuid(4),
							CompanyName = reader.GetString(5),
							WritingDate = RepositoryBase.ReadNullableDate(reader, "writing_date"),
							ArrivalAt = RepositoryBase.ReadDate(reader, "arrival_at"),
							ImagePath = (reader.IsDBNull(8) ? null : reader.GetString(8)),
							FilePath = (reader.IsDBNull(9) ? null : reader.GetString(9)),
							AttachmentFileName = (reader.IsDBNull(10) ? null : reader.GetString(10)),
							AttachmentContentType = (reader.IsDBNull(11) ? null : reader.GetString(11)),
							AttachmentData = (reader.IsDBNull(12) ? null : ((byte[])reader[12])),
							Notes = (reader.IsDBNull(13) ? null : reader.GetString(13)),
							Status = RepositoryBase.EnumValue<NoteStatus>(reader.GetString(14)),
							CreatedBy = (reader.IsDBNull(15) ? ((Guid?)null) : new Guid?(reader.GetGuid(15))),
							UpdatedBy = (reader.IsDBNull(16) ? ((Guid?)null) : new Guid?(reader.GetGuid(16))),
							ApprovedBy = (reader.IsDBNull(17) ? ((Guid?)null) : new Guid?(reader.GetGuid(17))),
							ApprovedAt = (reader.IsDBNull(18) ? ((DateTime?)null) : new DateTime?(reader.GetDateTime(18))),
							ReviewerId = (reader.IsDBNull(19) ? ((Guid?)null) : new Guid?(reader.GetGuid(19))),
							ReviewerName = (reader.IsDBNull(20) ? null : reader.GetString(20)),
							ReviewStartedAt = (reader.IsDBNull(21) ? ((DateTime?)null) : new DateTime?(reader.GetDateTime(21))),
							ReturnReason = (reader.IsDBNull(22) ? null : reader.GetString(22)),
							DuplicateReason = (reader.IsDBNull(23) ? null : reader.GetString(23)),
							FactoryId = (reader.IsDBNull(24) ? ((Guid?)null) : new Guid?(reader.GetGuid(24))),
							FactoryName = (reader.IsDBNull(25) ? null : reader.GetString(25)),
							ProductionLineId = (reader.IsDBNull(26) ? ((Guid?)null) : new Guid?(reader.GetGuid(26))),
							ProductionLineName = (reader.IsDBNull(27) ? null : reader.GetString(27)),
							CreatedAt = reader.GetDateTime(28),
							CreatedByName = (reader.IsDBNull(29) ? null : reader.GetString(29)),
							ApprovedByName = (reader.IsDBNull(30) ? null : reader.GetString(30)),
							UpdatedAt = reader.GetDateTime(31),
							IsLoot = !reader.IsDBNull(32) && reader.GetBoolean(32),
							IsInvoice = !reader.IsDBNull(33) && reader.GetBoolean(33)
						};
						await reader.CloseAsync();
						await using (NpgsqlCommand linksCommand = new NpgsqlCommand("select distinct case when source_note_id = @id then related_note_id else source_note_id end as counterpart_id from core.v_note_links_compat where source_note_id = @id or related_note_id = @id", connection))
						{
							linksCommand.Parameters.AddWithValue("id", id);
							await using NpgsqlDataReader linksReader = await linksCommand.ExecuteReaderAsync(cancellationToken);
							while (await linksReader.ReadAsync(cancellationToken))
							{
								note.RelatedNoteIds.Add(linksReader.GetGuid(0));
							}
						}
						NoteModel noteModel5;
						await using (NpgsqlCommand valueCommand = new NpgsqlCommand("select nfv.id, nfv.note_id, nfv.field_id, nf.name_ar, nfv.field_value, nfv.inactive_reason\r\nfrom core.note_field_values nfv\r\njoin core.note_fields nf on nf.id = nfv.field_id\r\nwhere nfv.note_id = @id\r\norder by nf.default_sort_order", connection))
						{
							valueCommand.Parameters.AddWithValue("id", id);
							NoteModel noteModel4;
							await using (NpgsqlDataReader valueReader = await valueCommand.ExecuteReaderAsync(cancellationToken))
							{
								while (await valueReader.ReadAsync(cancellationToken))
								{
									note.FieldValues.Add(new NoteFieldValueModel
									{
										Id = valueReader.GetGuid(0),
										NoteId = valueReader.GetGuid(1),
										FieldId = valueReader.GetGuid(2),
										FieldName = valueReader.GetString(3),
										FieldValue = (IndicatorValue)valueReader.GetInt16(4),
										InactiveReason = (valueReader.IsDBNull(5) ? null : valueReader.GetString(5))
									});
								}
								await valueReader.CloseAsync();
								NoteModel noteModel3;
								await using (NpgsqlCommand historyCommand = new NpgsqlCommand("select h.action, h.reason, u.full_name, r.name_ar, h.created_at\r\nfrom core.note_review_history h\r\nleft join auth.users u on u.id = h.user_id\r\nleft join auth.roles r on r.id = u.role_id\r\nwhere h.note_id = @id\r\norder by h.created_at desc", connection))
								{
									historyCommand.Parameters.AddWithValue("id", id);
									NoteModel noteModel2;
									await using (NpgsqlDataReader historyReader = await historyCommand.ExecuteReaderAsync(cancellationToken))
									{
										while (await historyReader.ReadAsync(cancellationToken))
										{
											note.ReviewHistory.Add(new NoteReviewHistoryModel
											{
												Action = historyReader.GetString(0),
												Reason = (historyReader.IsDBNull(1) ? null : historyReader.GetString(1)),
												UserName = (historyReader.IsDBNull(2) ? null : historyReader.GetString(2)),
														UserRoleName = (historyReader.IsDBNull(3) ? null : historyReader.GetString(3)),
														CreatedAt = historyReader.GetDateTime(4)
											});
										}
										noteModel2 = note;
									}
									noteModel3 = noteModel2;
								}
								noteModel4 = noteModel3;
							}
							noteModel5 = noteModel4;
						}
						noteModel = noteModel5;
					}
				}
				noteModel6 = noteModel;
			}
			result = noteModel6;
		}
		return result;
	}

	public async Task<IReadOnlyList<NoteDuplicateModel>> FindDuplicateNotesAsync(Guid? currentNoteId, string noteNumber, Guid noteTypeId, Guid companyId, DateOnly? writingDate, Guid requesterId, CancellationToken cancellationToken = default(CancellationToken), bool isInvoice = false)
	{
		await RepositoryBase.EnsureRoleAsync(base.Factory, requesterId, new UserRole[2]
		{
			UserRole.DATA_ENTRY,
			UserRole.ADMIN
		}, cancellationToken);
		string normalizedNumber = NoteNumberNormalizer.Normalize(noteNumber);
		long? numericNumberValue = NoteNumberNormalizer.TryParsePositive(noteNumber, out var parsedNumber) ? parsedNumber : null;
		if (normalizedNumber.Length == 0 || noteTypeId == Guid.Empty || companyId == Guid.Empty)
		{
			return Array.Empty<NoteDuplicateModel>();
		}
		List<NoteDuplicateModel> list = new List<NoteDuplicateModel>();
		IReadOnlyList<NoteDuplicateModel> result;
		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			bool hasNumericContract = await HasNumericNoteNumberContractAsync(connection, cancellationToken);
			IReadOnlyList<NoteDuplicateModel> readOnlyList2;
			await using (NpgsqlCommand command = new NpgsqlCommand("select n.id, n.note_number, n.company_id, c.name,\r\n       n.factory_id, f.name as factory_name,\r\n       n.production_line_id, pl.name as production_line_name,\r\n       n.note_type_id, nt.name_ar, n.writing_date, n.arrival_at, n.created_at,\r\n       n.attachment_file_name, n.attachment_content_type\r\nfrom core.notes n\r\njoin core.companies c on c.id = n.company_id\r\njoin core.note_types nt on nt.id = n.note_type_id\r\nleft join core.factories f on f.id = n.factory_id\r\nleft join core.production_lines pl on pl.id = n.production_line_id\r\nwhere n.is_deleted = false\r\n  and n.company_id = @company_id\r\n  and n.note_type_id = @note_type_id\r\n  and n.note_number_normalized = @normalized_number\r\n  and (@current_note_id is null or n.id <> @current_note_id)\r\n  and (@is_invoice = false or (n.writing_date is not distinct from @exact_writing_date))\r\norder by n.created_at desc", connection))
			{
				command.CommandText = hasNumericContract
					? """
select n.id, n.note_number, n.company_id, c.name,
       n.factory_id, f.name as factory_name,
       n.production_line_id, pl.name as production_line_name,
       n.note_type_id, nt.name_ar, n.writing_date, n.arrival_at, n.created_at,
       n.attachment_file_name, n.attachment_content_type
from core.notes n
join core.companies c on c.id = n.company_id
join core.note_types nt on nt.id = n.note_type_id
left join core.factories f on f.id = n.factory_id
left join core.production_lines pl on pl.id = n.production_line_id
where n.is_deleted = false
  and n.company_id = @company_id
  and n.note_type_id = @note_type_id
  and ((@note_number_value is not null and n.note_number_value = @note_number_value and (@note_year is null or n.note_year = @note_year))
       or (n.note_number_value is null and n.note_number_normalized = @normalized_number
           and (@note_year is null or extract(year from n.writing_date)::int = @note_year)))
  and (@current_note_id is null or n.id <> @current_note_id)
  and (@is_invoice = false or (n.writing_date is not distinct from @exact_writing_date))
order by n.created_at desc
"""
				: """
select n.id, n.note_number, n.company_id, c.name,
       n.factory_id, f.name as factory_name,
       n.production_line_id, pl.name as production_line_name,
       n.note_type_id, nt.name_ar, n.writing_date, n.arrival_at, n.created_at,
       n.attachment_file_name, n.attachment_content_type
from core.notes n
join core.companies c on c.id = n.company_id
join core.note_types nt on nt.id = n.note_type_id
left join core.factories f on f.id = n.factory_id
left join core.production_lines pl on pl.id = n.production_line_id
where n.is_deleted = false
  and n.company_id = @company_id
  and n.note_type_id = @note_type_id
  and n.note_number_normalized = @normalized_number
  and (@current_note_id is null or n.id <> @current_note_id)
  and (@is_invoice = false or (n.writing_date is not distinct from @exact_writing_date))
order by n.created_at desc
""";
				command.Parameters.AddWithValue("company_id", companyId);
				command.Parameters.AddWithValue("note_type_id", noteTypeId);
				command.Parameters.AddWithValue("normalized_number", normalizedNumber);
				command.Parameters.AddWithValue("is_invoice", isInvoice);
				command.Parameters.Add("exact_writing_date", NpgsqlDbType.Date).Value = (object?)writingDate ?? DBNull.Value;
				command.CommandText = command.CommandText.Replace(
					"\r\n          and (\r\n              @current_note_id is null\r\n              or not exists(\r\n                  select 1 from core.note_links source_link\r\n                  where source_link.source_note_id = @current_note_id\r\n                     or source_link.related_note_id = @current_note_id\r\n              )\r\n          )",
					string.Empty,
					StringComparison.Ordinal);
				command.Parameters.Add("current_note_id", NpgsqlDbType.Uuid).Value = ((object)currentNoteId) ?? DBNull.Value;
				if (hasNumericContract)
				{
					command.Parameters.Add("note_number_value", NpgsqlDbType.Bigint).Value = (object?)numericNumberValue ?? DBNull.Value;
					command.Parameters.Add("note_year", NpgsqlDbType.Integer).Value = (object?)writingDate?.Year ?? DBNull.Value;
				}
				IReadOnlyList<NoteDuplicateModel> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						list.Add(new NoteDuplicateModel
						{
							NoteId = reader.GetGuid(reader.GetOrdinal("id")),
							NoteNumber = reader.GetString(reader.GetOrdinal("note_number")),
							CompanyId = reader.GetGuid(reader.GetOrdinal("company_id")),
							CompanyName = reader.GetString(reader.GetOrdinal("name")),
							FactoryId = (reader.IsDBNull(reader.GetOrdinal("factory_id")) ? ((Guid?)null) : new Guid?(reader.GetGuid(reader.GetOrdinal("factory_id")))),
							FactoryName = (reader.IsDBNull(reader.GetOrdinal("factory_name")) ? null : reader.GetString(reader.GetOrdinal("factory_name"))),
							ProductionLineId = (reader.IsDBNull(reader.GetOrdinal("production_line_id")) ? ((Guid?)null) : new Guid?(reader.GetGuid(reader.GetOrdinal("production_line_id")))),
							ProductionLineName = (reader.IsDBNull(reader.GetOrdinal("production_line_name")) ? null : reader.GetString(reader.GetOrdinal("production_line_name"))),
							NoteTypeId = reader.GetGuid(reader.GetOrdinal("note_type_id")),
							NoteTypeName = reader.GetString(reader.GetOrdinal("name_ar")),
							WritingDate = RepositoryBase.ReadNullableDate(reader, "writing_date"),
							ArrivalAt = RepositoryBase.ReadDate(reader, "arrival_at"),
							CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
							AttachmentFileName = (reader.IsDBNull(reader.GetOrdinal("attachment_file_name")) ? null : reader.GetString(reader.GetOrdinal("attachment_file_name"))),
							AttachmentContentType = (reader.IsDBNull(reader.GetOrdinal("attachment_content_type")) ? null : reader.GetString(reader.GetOrdinal("attachment_content_type")))
						});
					}
					readOnlyList = list;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<DuplicateAttachmentModel?> GetDuplicateAttachmentAsync(Guid noteId, string noteNumber, Guid noteTypeId, Guid companyId, DateOnly? writingDate, Guid requesterId, CancellationToken cancellationToken = default(CancellationToken))
	{
		await RepositoryBase.EnsureRoleAsync(base.Factory, requesterId, new UserRole[2]
		{
			UserRole.DATA_ENTRY,
			UserRole.ADMIN
		}, cancellationToken);
		string normalizedNumber = NoteNumberNormalizer.Normalize(noteNumber);
		long? numericNumberValue = NoteNumberNormalizer.TryParsePositive(noteNumber, out var parsedNumber) ? parsedNumber : null;
		DuplicateAttachmentModel result;
		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			bool hasNumericContract = await HasNumericNoteNumberContractAsync(connection, cancellationToken);
			DuplicateAttachmentModel duplicateAttachmentModel2;
			await using (NpgsqlCommand command = new NpgsqlCommand("select attachment_file_name, attachment_content_type, attachment_data\r\nfrom core.notes\r\nwhere id = @id and is_deleted = false\r\n  and company_id = @company_id and note_type_id = @note_type_id\r\n  and note_number_normalized = @normalized_number\r\n", connection))
			{
			command.CommandText = hasNumericContract
				? "select attachment_file_name, attachment_content_type, attachment_data from core.notes where id = @id and is_deleted = false and company_id = @company_id and note_type_id = @note_type_id and ((@note_number_value is not null and note_number_value = @note_number_value and (@note_year is null or note_year = @note_year)) or (note_number_value is null and note_number_normalized = @normalized_number and (@note_year is null or extract(year from writing_date)::int = @note_year)))"
				: "select attachment_file_name, attachment_content_type, attachment_data from core.notes where id = @id and is_deleted = false and company_id = @company_id and note_type_id = @note_type_id and note_number_normalized = @normalized_number";
				command.Parameters.AddWithValue("id", noteId);
				command.Parameters.AddWithValue("company_id", companyId);
				command.Parameters.AddWithValue("note_type_id", noteTypeId);
				command.Parameters.AddWithValue("normalized_number", normalizedNumber);
				if (hasNumericContract)
				{
					command.Parameters.Add("note_number_value", NpgsqlDbType.Bigint).Value = (object?)numericNumberValue ?? DBNull.Value;
					command.Parameters.Add("note_year", NpgsqlDbType.Integer).Value = (object?)writingDate?.Year ?? DBNull.Value;
				}
				DuplicateAttachmentModel duplicateAttachmentModel;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					duplicateAttachmentModel = ((await reader.ReadAsync(cancellationToken)) ? new DuplicateAttachmentModel
					{
						FileName = (reader.IsDBNull(0) ? null : reader.GetString(0)),
						ContentType = (reader.IsDBNull(1) ? null : reader.GetString(1)),
						Data = (reader.IsDBNull(2) ? null : ((byte[])reader.GetValue(2)))
					} : null);
				}
				duplicateAttachmentModel2 = duplicateAttachmentModel;
			}
			result = duplicateAttachmentModel2;
		}
		return result;
	}

	public async Task<DuplicateResolutionResult> ResolveDuplicateAsync(NoteModel note, Guid userId, bool saveAsDraft, DuplicateResolutionRequest resolution, Guid? relatedNoteId = null, CancellationToken cancellationToken = default(CancellationToken), bool markAllIndicatorsCorrect = false, bool synchronizeRelatedNote = false, Guid? operationId = null)
	{
		ArgumentNullException.ThrowIfNull(resolution, "resolution");
		if (resolution.MatchedNoteId == Guid.Empty)
		{
			throw new InvalidOperationException("يجب تحديد المذكرة الأصلية المطابقة.");
		}
		if (resolution.Classification == DuplicateClassification.COMPANY_DUPLICATE_NUMBER)
		{
			Guid id = await SaveNoteAsync(note, userId, saveAsDraft, relatedNoteId, resolution, cancellationToken, markAllIndicatorsCorrect, synchronizeRelatedNote, operationId);
			return new DuplicateResolutionResult
			{
				NoteSaved = true,
				NoteId = id,
				Message = "تم حفظ المذكرة ورصد تكرار الرقم من الشركة."
			};
		}
		_modeService.EnsureWritable();
		await EnsureNoteEditorAsync(userId, null, cancellationToken);
		string normalizedNumber = NoteNumberNormalizer.Normalize(note.NoteNumber);
		long? numericNumberValue = NoteNumberNormalizer.TryParsePositive(note.NoteNumber, out var parsedNumber) ? parsedNumber : null;
		if (string.IsNullOrWhiteSpace(normalizedNumber))
		{
			throw new InvalidOperationException("رقم المذكرة يجب أن يحتوي على حرف أو رقم واحد على الأقل بعد التطبيع.");
		}
		if (normalizedNumber.Length == 0 || note.CompanyId == Guid.Empty || note.NoteTypeId == Guid.Empty)
		{
			throw new InvalidOperationException("بيانات مفتاح التكرار غير مكتملة.");
		}
		DuplicateResolutionResult result;
		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			bool hasNumericContract = await HasNumericNoteNumberContractAsync(connection, cancellationToken);
			DuplicateResolutionResult duplicateResolutionResult;
			await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
			{
				await AcquireDuplicateLockAsync(connection, transaction, note.CompanyId, note.NoteTypeId, normalizedNumber, note.WritingDate, numericNumberValue, cancellationToken, note.IsInvoice);
				if (!(await FindDuplicateIdsAsync(connection, transaction, null, note.CompanyId, note.NoteTypeId, normalizedNumber, note.WritingDate, numericNumberValue, hasNumericContract, cancellationToken, note.IsInvoice)).Contains(resolution.MatchedNoteId))
				{
					throw new InvalidOperationException("المذكرة المحددة لم تعد مطابقة. أعد البحث ثم حاول مجددا\u064b.");
				}
				string metadata = BuildDuplicateMetadata(note, resolution.MatchedNoteId);
				if (resolution.Classification == DuplicateClassification.COMPANY_RESEND)
				{
					await InsertDuplicateIncidentAsync(connection, transaction, resolution.Classification, resolution.MatchedNoteId, null, note, userId, metadata, cancellationToken);
					await WriteStructuredAuditRecordAsync(connection, transaction, userId, "COMPANY_RESEND", "core", "duplicate_incidents", resolution.MatchedNoteId, "أعادت الشركة إرسال المذكرة نفسها؛ لم ت\u064fحفظ نسخة ثانية أو مرفق جديد.", metadata, cancellationToken);
					await transaction.CommitAsync(cancellationToken);
					duplicateResolutionResult = new DuplicateResolutionResult
					{
						NoteSaved = false,
						Message = "تم رصد إعادة الإرسال دون حفظ مذكرة أو صورة مكررة."
					};
				}
				else
				{
					if (resolution.Classification != DuplicateClassification.DATA_ENTRY_DUPLICATE_ATTEMPT)
					{
						throw new InvalidOperationException("تصنيف التكرار غير مدعوم.");
					}
					await WriteStructuredAuditRecordAsync(connection, transaction, userId, "DATA_ENTRY_DUPLICATE_ATTEMPT", "core", "notes", resolution.MatchedNoteId, "حاول مدخل البيانات إدخال مذكرة سبق إدخالها؛ لم ت\u064fحفظ مذكرة أو صورة أو حادثة على الشركة.", metadata, cancellationToken);
					await transaction.CommitAsync(cancellationToken);
					duplicateResolutionResult = new DuplicateResolutionResult
					{
						NoteSaved = false,
						Message = "تم تسجيل محاولة الإدخال المكرر في سجل العمليات دون حفظ بيانات مكررة."
					};
				}
			}
			result = duplicateResolutionResult;
		}
		return result;
	}

	private static async Task AcquireDuplicateLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId, Guid noteTypeId, string normalizedNumber, DateOnly? writingDate, long? numericNumberValue, CancellationToken cancellationToken, bool isInvoice = false)
	{
		await using NpgsqlCommand command = new NpgsqlCommand("select pg_advisory_xact_lock(hashtextextended(@key, 127))", connection, transaction);
		string datePart = isInvoice ? (writingDate?.ToString("yyyyMMdd") ?? "invoicedate_none") : (writingDate?.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "legacy");
		command.Parameters.AddWithValue("key", $"{companyId:N}|{noteTypeId:N}|{datePart}|{numericNumberValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? normalizedNumber}");
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task<bool> HasNumericNoteNumberContractAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand("""
select exists(
    select 1
    from information_schema.columns
    where table_schema = 'core'
      and table_name = 'notes'
      and column_name = 'note_number_value')
""", connection);
		return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
	}

	private static async Task<List<Guid>> FindDuplicateIdsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid? currentNoteId, Guid companyId, Guid noteTypeId, string normalizedNumber, DateOnly? writingDate, long? numericNumberValue, bool hasNumericContract, CancellationToken cancellationToken, bool isInvoice = false)
	{
		List<Guid> ids = new List<Guid>();
		List<Guid> result;
		await using (NpgsqlCommand command = new NpgsqlCommand(hasNumericContract
			? "select id from core.notes\r\nwhere is_deleted = false and company_id = @company_id and note_type_id = @note_type_id\r\n  and ((@note_number_value is not null and note_number_value = @note_number_value and (@note_year is null or note_year = @note_year))\r\n       or (@note_number_value is null and note_number_normalized = @normalized_number\r\n           and (@note_year is null or extract(year from writing_date)::int = @note_year))\r\n       or (note_number_value is null and note_number_normalized = @normalized_number\r\n           and (@note_year is null or extract(year from writing_date)::int = @note_year)))\r\n  and (@current_note_id is null or id <> @current_note_id)\r\n  and (@is_invoice = false or (writing_date is not distinct from @exact_writing_date))\r\norder by created_at desc"
			: "select id from core.notes\r\nwhere is_deleted = false and company_id = @company_id and note_type_id = @note_type_id\r\n  and note_number_normalized = @normalized_number\r\n  and (@current_note_id is null or id <> @current_note_id)\r\n  and (@is_invoice = false or (writing_date is not distinct from @exact_writing_date))\r\norder by created_at desc", connection, transaction))
		{
			command.Parameters.AddWithValue("company_id", companyId);
			command.Parameters.AddWithValue("note_type_id", noteTypeId);
			command.Parameters.AddWithValue("normalized_number", normalizedNumber);
			command.Parameters.AddWithValue("is_invoice", isInvoice);
			command.Parameters.Add("exact_writing_date", NpgsqlDbType.Date).Value = (object?)writingDate ?? DBNull.Value;
			if (hasNumericContract)
			{
				command.Parameters.Add("note_number_value", NpgsqlDbType.Bigint).Value = (object?)numericNumberValue ?? DBNull.Value;
				command.Parameters.Add("note_year", NpgsqlDbType.Integer).Value = (object?)writingDate?.Year ?? DBNull.Value;
			}
			command.Parameters.Add("current_note_id", NpgsqlDbType.Uuid).Value = ((object)currentNoteId) ?? DBNull.Value;
			List<Guid> list;
			await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
			{
				while (await reader.ReadAsync(cancellationToken))
				{
					ids.Add(reader.GetGuid(0));
				}
				list = ids;
			}
			result = list;
		}
		return result;
	}

	private static string BuildDuplicateMetadata(NoteModel note, Guid matchedNoteId)
	{
		return JsonSerializer.Serialize(new
		{
			noteNumber = note.NoteNumber,
			normalizedNumber = NoteNumberNormalizer.Normalize(note.NoteNumber),
			CompanyId = note.CompanyId,
			company = (string.IsNullOrWhiteSpace(note.CompanyName) ? note.CompanyId.ToString() : note.CompanyName),
			FactoryId = note.FactoryId,
			factory = (string.IsNullOrWhiteSpace(note.FactoryName) ? "غير محدد" : note.FactoryName),
			ProductionLineId = note.ProductionLineId,
			productionLine = (string.IsNullOrWhiteSpace(note.ProductionLineName) ? "غير محدد" : note.ProductionLineName),
			NoteTypeId = note.NoteTypeId,
			noteType = (string.IsNullOrWhiteSpace(note.NoteTypeName) ? note.NoteTypeId.ToString() : note.NoteTypeName),
			WritingDate = note.WritingDate,
			matchedNoteId = matchedNoteId
		});
	}

	private static async Task<bool> InsertDuplicateIncidentAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DuplicateClassification classification, Guid matchedNoteId, Guid? createdNoteId, NoteModel note, Guid userId, string metadata, CancellationToken cancellationToken)
	{
		metadata = NormalizeJsonForPostgres(metadata);
		string conflictClause = classification == DuplicateClassification.COMPANY_DUPLICATE_NUMBER
			? "\r\non conflict (created_note_id) where classification = 'COMPANY_DUPLICATE_NUMBER' do nothing"
			: string.Empty;
		await using NpgsqlCommand command = new NpgsqlCommand("insert into core.duplicate_incidents(classification, matched_note_id, created_note_id, company_id, factory_id,\r\n    production_line_id, note_type_id, note_number, note_number_normalized, writing_date, decided_by, metadata)\r\nvalues (@classification, @matched_note_id, @created_note_id, @company_id, @factory_id,\r\n    @production_line_id, @note_type_id, @note_number, @normalized_number, @writing_date, @decided_by, @metadata::jsonb)" + conflictClause, connection, transaction);
		command.Parameters.AddWithValue("classification", classification.ToString());
		command.Parameters.AddWithValue("matched_note_id", matchedNoteId);
		command.Parameters.Add("created_note_id", NpgsqlDbType.Uuid).Value = ((object)createdNoteId) ?? DBNull.Value;
		command.Parameters.AddWithValue("company_id", note.CompanyId);
		command.Parameters.Add("factory_id", NpgsqlDbType.Uuid).Value = ((object)note.FactoryId) ?? DBNull.Value;
		command.Parameters.Add("production_line_id", NpgsqlDbType.Uuid).Value = ((object)note.ProductionLineId) ?? DBNull.Value;
		command.Parameters.AddWithValue("note_type_id", note.NoteTypeId);
		command.Parameters.AddWithValue("note_number", note.NoteNumber);
		command.Parameters.AddWithValue("normalized_number", NoteNumberNormalizer.Normalize(note.NoteNumber));
		command.Parameters.Add("writing_date", NpgsqlDbType.Date).Value = ((object)note.WritingDate) ?? DBNull.Value;
		command.Parameters.AddWithValue("decided_by", userId);
		command.Parameters.AddWithValue("metadata", metadata);
		return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
	}

	private sealed record NoteCreateOperationRecord(Guid OperationId, Guid NoteId, Guid RequestedBy, string Fingerprint);

	private static string BuildNoteCreateFingerprint(
		NoteModel note,
		bool saveAsDraft,
		Guid? relatedNoteId,
		DuplicateResolutionRequest? duplicateResolution,
		bool markAllIndicatorsCorrect,
		bool synchronizeRelatedNote,
		IReadOnlyList<Guid>? relatedNoteIds = null)
	{
		string? attachmentContentType = string.IsNullOrWhiteSpace(note.AttachmentFileName)
			? null
			: AttachmentFilePolicy.GetContentType(note.AttachmentFileName);
		string? attachmentHash = note.AttachmentData is null
			? null
			: Convert.ToHexString(SHA256.HashData(note.AttachmentData)).ToLowerInvariant();
		var payload = new
		{
			version = 2,
			noteNumber = note.NoteNumber,
			noteTypeId = note.NoteTypeId,
			companyId = note.CompanyId,
			factoryId = note.FactoryId,
			productionLineId = note.ProductionLineId,
			writingDate = note.WritingDate,
			arrivalAt = note.ArrivalAt,
			imagePath = note.ImagePath,
			filePath = note.FilePath,
			attachmentFileName = note.AttachmentFileName,
			attachmentContentType,
			attachmentHash,
			notes = note.Notes,
			isLoot = note.IsLoot,
			isInvoice = note.IsInvoice,
			saveAsDraft,
			relatedNoteId,
			relatedNoteIds = relatedNoteIds ?? (note.RelatedNoteIds.Count > 0 ? note.RelatedNoteIds : (relatedNoteId.HasValue ? new[] { relatedNoteId.Value } : Array.Empty<Guid>())),
			duplicateResolution = duplicateResolution is null
				? null
				: new
				{
					classification = duplicateResolution.Classification.ToString(),
					matchedNoteId = duplicateResolution.MatchedNoteId
				},
			markAllIndicatorsCorrect,
			synchronizeRelatedNote,
			fieldValues = note.FieldValues
				.OrderBy(value => value.FieldId)
				.ThenBy(value => value.FieldValue)
				.ThenBy(value => value.InactiveReason, StringComparer.Ordinal)
				.Select(value => new
				{
					fieldId = value.FieldId,
					fieldValue = (short)value.FieldValue,
					inactiveReason = value.InactiveReason
				})
				.ToArray()
		};

		return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload))).ToLowerInvariant();
	}

	private async Task<NoteCreateOperationRecord?> FindNoteCreateOperationAsync(
		Guid operationId,
		Guid noteId,
		CancellationToken cancellationToken)
	{
		await using NpgsqlConnection connection = base.Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		return await FindNoteCreateOperationAsync(connection, null, operationId, noteId, cancellationToken);
	}

	private static async Task<NoteCreateOperationRecord?> FindNoteCreateOperationAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction? transaction,
		Guid operationId,
		Guid noteId,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = new NpgsqlCommand("select operation_id, note_id, requested_by, fingerprint\r\nfrom core.note_create_operations\r\nwhere operation_id = @operation_id or note_id = @note_id\r\norder by case when operation_id = @operation_id then 0 else 1 end\r\nlimit 1", connection, transaction);
		command.Parameters.AddWithValue("operation_id", operationId);
		command.Parameters.AddWithValue("note_id", noteId);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken)
			? new NoteCreateOperationRecord(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3))
			: null;
	}

	private static Guid ValidateNoteCreateReplay(
		NoteCreateOperationRecord operation,
		Guid operationId,
		Guid noteId,
		Guid requestedBy,
		string fingerprint)
	{
		if (operation.OperationId != operationId
			|| operation.NoteId != noteId
			|| operation.RequestedBy != requestedBy
			|| !string.Equals(operation.Fingerprint, fingerprint, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("لا يمكن إعادة استخدام معرّف عملية إنشاء المذكرة مع مستخدم أو مذكرة أو بيانات مختلفة.");
		}

		return operation.NoteId;
	}

	private static async Task<bool> TryReserveNoteCreateOperationAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		Guid operationId,
		Guid noteId,
		Guid requestedBy,
		string fingerprint,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = new NpgsqlCommand("insert into core.note_create_operations(operation_id, note_id, requested_by, fingerprint)\r\nvalues (@operation_id, @note_id, @requested_by, @fingerprint)\r\non conflict do nothing", connection, transaction);
		command.Parameters.AddWithValue("operation_id", operationId);
		command.Parameters.AddWithValue("note_id", noteId);
		command.Parameters.AddWithValue("requested_by", requestedBy);
		command.Parameters.AddWithValue("fingerprint", fingerprint);
		return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
	}

	private static async Task AcquireNoteCreateOperationLockAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		Guid operationId,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = new NpgsqlCommand(
			"select pg_advisory_xact_lock(hashtextextended(@operation_key, 571))",
			connection,
			transaction);
		command.Parameters.AddWithValue("operation_key", operationId.ToString("N"));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task WriteStructuredAuditRecordAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, string actionType, string schemaName, string tableName, Guid recordId, string description, string metadata, CancellationToken cancellationToken)
	{
		await WriteStructuredAuditRecordAsync(connection, transaction, userId, actionType, schemaName, tableName, recordId, description, null, metadata, cancellationToken);
	}

	private static async Task WriteStructuredAuditRecordAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, string actionType, string schemaName, string tableName, Guid recordId, string description, string? beforeJson, string afterJson, CancellationToken cancellationToken)
	{
		beforeJson = NormalizeJsonForPostgres(beforeJson ?? "null");
		afterJson = NormalizeJsonForPostgres(afterJson);
		await using NpgsqlCommand command = new NpgsqlCommand("insert into audit.logs(user_id, action_type, schema_name, table_name, record_id, description, new_value)\r\nvalues (@user_id, @action_type, @schema_name, @table_name, @record_id, @description, @metadata::jsonb)", connection, transaction);
		command.CommandText = "insert into audit.logs(user_id, action_type, schema_name, table_name, record_id, description, old_value, new_value)\r\nvalues (@user_id, @action_type, @schema_name, @table_name, @record_id, @description, @old_value::jsonb, @new_value::jsonb)";
		command.Parameters.AddWithValue("user_id", userId);
		command.Parameters.AddWithValue("action_type", actionType);
		command.Parameters.AddWithValue("schema_name", schemaName);
		command.Parameters.AddWithValue("table_name", tableName);
		command.Parameters.AddWithValue("record_id", recordId);
		command.Parameters.AddWithValue("description", description);
		command.Parameters.AddWithValue("old_value", beforeJson);
		command.Parameters.AddWithValue("new_value", afterJson);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static string BuildNoteAuditSnapshot(NoteModel note, IEnumerable<NoteFieldValueModel>? additionalFieldValues = null)
	{
		var fieldValues = (additionalFieldValues ?? note.FieldValues)
			.GroupBy(value => value.FieldId)
			.Select(group => group.Last())
			.OrderBy(value => value.FieldId)
			.ToArray();
		return JsonSerializer.Serialize(new
		{
			note = new
			{
				id = note.Id,
				noteNumber = note.NoteNumber,
				companyId = note.CompanyId,
				companyName = note.CompanyName,
				noteTypeId = note.NoteTypeId,
				noteTypeName = note.NoteTypeName,
				factoryId = note.FactoryId,
				factoryName = note.FactoryName,
				productionLineId = note.ProductionLineId,
				productionLineName = note.ProductionLineName,
				writingDate = note.WritingDate,
				arrivalAt = note.ArrivalAt,
				notes = note.Notes,
				status = note.Status.ToString(),
				createdBy = note.CreatedBy,
				updatedBy = note.UpdatedBy,
				approvedBy = note.ApprovedBy,
				approvedAt = note.ApprovedAt,
				reviewerId = note.ReviewerId,
				reviewStartedAt = note.ReviewStartedAt,
				returnReason = note.ReturnReason,
				duplicateReason = note.DuplicateReason,
				attachmentFileName = note.AttachmentFileName ?? note.ImagePath,
				attachmentContentType = note.AttachmentContentType,
				attachmentSha256 = note.AttachmentData is { Length: > 0 } data ? Convert.ToHexString(SHA256.HashData(data)) : null,
				isLoot = note.IsLoot,
				isInvoice = note.IsInvoice
			},
			fieldValues = fieldValues
				.Select(value => new { fieldId = value.FieldId, fieldName = value.FieldName, value = value.FieldValue.ToString(), inactiveReason = value.InactiveReason })
				.ToArray()
		});
	}

	public async Task<IReadOnlyList<NoteLinkCandidateModel>> SearchLinkCandidatesAsync(Guid? currentNoteId, Guid companyId, Guid noteTypeId, string? noteNumberSearch, Guid requesterId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if ((uint)(await RepositoryBase.GetUserRoleAsync(base.Factory, requesterId, cancellationToken)) > 1u)
		{
			throw new UnauthorizedAccessException("البحث والربط متاحان لمدخل البيانات أو مدير النظام فقط.");
		}
		Guid sourceId = default(Guid);
		int num;
		if (currentNoteId.HasValue)
		{
			sourceId = currentNoteId.GetValueOrDefault();
			num = 1;
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			NoteModel source = (await GetNoteInternalAsync(sourceId, cancellationToken)) ?? throw new InvalidOperationException("المذكرة غير موجودة أو محذوفة.");
			await EnsureNoteEditorAsync(requesterId, source, cancellationToken);
		}
		string normalizedSearch = NoteNumberNormalizer.Normalize(noteNumberSearch ?? string.Empty);
		if (string.IsNullOrWhiteSpace(normalizedSearch))
		{
			return Array.Empty<NoteLinkCandidateModel>();
		}
		return await QueryLinkCandidatesAsync(currentNoteId, companyId, noteTypeId, normalizedSearch, includeUnlinked: true, requesterId, cancellationToken);
	}

	public async Task<IReadOnlyList<NoteLinkCandidateModel>> GetLinkCandidatesAsync(Guid noteId, Guid requesterId, bool includeUnlinked = false, CancellationToken cancellationToken = default(CancellationToken))
	{
		NoteModel source = (await GetNoteInternalAsync(noteId, cancellationToken)) ?? throw new InvalidOperationException("المذكرة غير موجودة أو محذوفة.");
		if (!includeUnlinked)
		{
			await EnsureNoteReadAccessAsync(requesterId, source, cancellationToken);
		}
		else
		{
			await EnsureNoteEditorAsync(requesterId, source, cancellationToken);
		}
		return await QueryLinkCandidatesAsync(noteId, source.CompanyId, source.NoteTypeId, null, includeUnlinked, requesterId, cancellationToken);
	}

	private async Task<IReadOnlyList<NoteLinkCandidateModel>> QueryLinkCandidatesAsync(Guid? currentNoteId, Guid companyId, Guid noteTypeId, string? normalizedSearch, bool includeUnlinked, Guid requesterId, CancellationToken cancellationToken)
	{
		List<NoteLinkCandidateModel> list = new List<NoteLinkCandidateModel>();
		IReadOnlyList<NoteLinkCandidateModel> result;
		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			IReadOnlyList<NoteLinkCandidateModel> readOnlyList2;
			await using (NpgsqlCommand command = new NpgsqlCommand(@"select n.id, n.note_number, company.name, candidate_type.name_ar,
       factory.name, production_line.name, n.writing_date, n.arrival_at, n.status,
       exists(
           select 1 from core.v_note_links_compat current_link
           where @current_note_id is not null
             and ((current_link.source_note_id = @current_note_id and current_link.related_note_id = n.id)
               or (current_link.related_note_id = @current_note_id and current_link.source_note_id = n.id))
       ) as is_linked,
       coalesce(n.is_invoice, false) as is_invoice
from core.notes n
join core.companies company on company.id = n.company_id
join core.note_types candidate_type on candidate_type.id = n.note_type_id
join core.note_types source_type on source_type.id = @note_type_id
left join core.factories factory on factory.id = n.factory_id
left join core.production_lines production_line on production_line.id = n.production_line_id
where not n.is_deleted
  and not coalesce(n.is_loot, false)
  and not exists (select 1 from core.note_sources s where s.note_id = n.id and s.source_type = 'LOOT')
  and n.company_id = @company_id
  and (@current_note_id is null or n.id <> @current_note_id)
  and ((source_type.code = 'ENTRY' and candidate_type.code = 'EXIT')
    or (source_type.code = 'EXIT' and candidate_type.code = 'ENTRY'))
  and (@normalized_search is null or position(@normalized_search in n.note_number_normalized) > 0)
  and (
      exists(
          select 1 from core.v_note_links_compat exact_link
          where @current_note_id is not null
            and ((exact_link.source_note_id = @current_note_id and exact_link.related_note_id = n.id)
              or (exact_link.related_note_id = @current_note_id and exact_link.source_note_id = n.id))
      )
      or (
          @include_unlinked
          and (
              candidate_type.code = 'ENTRY'
              or not exists(
                  select 1 from core.v_note_links_compat candidate_link
                  where candidate_link.source_note_id = n.id or candidate_link.related_note_id = n.id
              )
          )
      )
  )
order by is_linked desc, n.arrival_at desc, n.note_number
limit 100", connection))
			{
				command.Parameters.Add("current_note_id", NpgsqlDbType.Uuid).Value = ((object)currentNoteId) ?? DBNull.Value;
				command.Parameters.AddWithValue("company_id", companyId);
				command.Parameters.AddWithValue("note_type_id", noteTypeId);
				command.Parameters.Add("normalized_search", NpgsqlDbType.Varchar).Value = ((object)normalizedSearch) ?? ((object)DBNull.Value);
				command.Parameters.AddWithValue("include_unlinked", includeUnlinked);
				IReadOnlyList<NoteLinkCandidateModel> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						list.Add(new NoteLinkCandidateModel
						{
							NoteId = reader.GetGuid(0),
							NoteNumber = reader.GetString(1),
							CompanyName = reader.GetString(2),
							NoteTypeName = reader.GetString(3),
							FactoryName = (reader.IsDBNull(4) ? null : reader.GetString(4)),
							ProductionLineName = (reader.IsDBNull(5) ? null : reader.GetString(5)),
							WritingDate = RepositoryBase.ReadNullableDate(reader, "writing_date"),
							ArrivalAt = RepositoryBase.ReadDate(reader, "arrival_at"),
							Status = RepositoryBase.EnumValue<NoteStatus>(reader.GetString(8)),
							IsLinked = reader.GetBoolean(9),
							IsInvoice = reader.GetBoolean(10)
						});
					}
					readOnlyList = list;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public Task LinkNotesAsync(Guid noteId, Guid relatedNoteId, Guid userId, CancellationToken cancellationToken = default(CancellationToken))
		=> LinkNotesAsync(noteId, new[] { relatedNoteId }, userId, cancellationToken);

	public async Task LinkNotesAsync(Guid noteId, IReadOnlyList<Guid> relatedNoteIds, Guid userId, CancellationToken cancellationToken = default(CancellationToken))
	{
		_modeService.EnsureWritable();
		if (relatedNoteIds == null || relatedNoteIds.Count == 0) return;
		var targets = relatedNoteIds.Where(id => id != Guid.Empty && id != noteId).Distinct().ToList();
		if (targets.Count == 0)
		{
			throw new InvalidOperationException("لا يمكن ربط المذكرة بنفسها.");
		}
		NoteModel note = (await GetNoteInternalAsync(noteId, cancellationToken)) ?? throw new InvalidOperationException("المذكرة غير موجودة أو محذوفة.");
		await EnsureNoteEditorAsync(userId, note, cancellationToken);

		await using NpgsqlConnection connection = base.Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		try
		{
			foreach (var relatedNoteId in targets)
			{
				NoteModel related = (await GetNoteInternalAsync(relatedNoteId, cancellationToken)) ?? throw new InvalidOperationException("المذكرة المرتبطة غير موجودة أو محذوفة.");
				await using NpgsqlCommand command = new NpgsqlCommand("""
					insert into core.note_links(source_note_id, related_note_id, created_by)
					select @note_id, @related_note_id, @user_id
					where not exists (
					    select 1 from core.note_links existing
					    where (existing.source_note_id = @note_id and existing.related_note_id = @related_note_id)
					       or (existing.source_note_id = @related_note_id and existing.related_note_id = @note_id)
					);
					""", connection, transaction);
				command.Parameters.AddWithValue("note_id", noteId);
				command.Parameters.AddWithValue("related_note_id", relatedNoteId);
				command.Parameters.AddWithValue("user_id", userId);
				if (await command.ExecuteNonQueryAsync(cancellationToken) > 0)
				{
					await RepositoryBase.WriteAuditRecordAsync(connection, transaction, userId, "LINK_NOTES", "core", "note_links", noteId, "ربط المذكرة " + note.NoteNumber + " مع المذكرة " + related.NoteNumber, cancellationToken);
				}
			}
			await transaction.CommitAsync(cancellationToken);
		}
		catch (PostgresException ex) when (ex.SqlState is "23503" or "23505" or "23514")
		{
			throw CreateLinkValidationException(ex);
		}
	}

	public async Task UnlinkNotesAsync(Guid noteId, Guid relatedNoteId, Guid userId, CancellationToken cancellationToken = default(CancellationToken))
	{
		_modeService.EnsureWritable();
		NoteModel note = (await GetNoteInternalAsync(noteId, cancellationToken)) ?? throw new InvalidOperationException("المذكرة غير موجودة أو محذوفة.");
		await EnsureNoteEditorAsync(userId, note, cancellationToken);
		NoteModel related = (await GetNoteInternalAsync(relatedNoteId, cancellationToken)) ?? throw new InvalidOperationException("المذكرة المرتبطة غير موجودة أو محذوفة.");
		await using NpgsqlConnection connection = base.Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await using NpgsqlCommand command = new NpgsqlCommand("delete from core.note_links\r\nwhere ((source_note_id = @note_id and related_note_id = @related_note_id)\r\n   or (source_note_id = @related_note_id and related_note_id = @note_id))", connection, transaction);
		command.Parameters.AddWithValue("note_id", noteId);
		command.Parameters.AddWithValue("related_note_id", relatedNoteId);
		if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
		{
			throw new InvalidOperationException("الرابط المطلوب غير موجود.");
		}
		await RepositoryBase.WriteAuditRecordAsync(connection, transaction, userId, "UNLINK_NOTES", "core", "note_links", noteId, "إلغاء ربط المذكرة " + note.NoteNumber + " من المذكرة " + related.NoteNumber, cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<NoteSourceModel?> GetNoteSourceAsync(
		Guid noteId,
		Guid requesterId,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		var note = (await GetNoteInternalAsync(noteId, cancellationToken))
			?? throw new InvalidOperationException("المذكرة غير موجودة أو محذوفة.");
		await EnsureNoteReadAccessAsync(requesterId, note, cancellationToken);

		await using var connection = Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		if (!await HasNoteSourceContractAsync(connection, cancellationToken))
		{
			throw new InvalidOperationException("يتطلب نموذج مصادر المذكرة ترقية قاعدة البيانات إلى الإصدار 063.");
		}

		NoteSourceModel? source;
		await using (var command = new NpgsqlCommand("""
			select id, note_id, source_type, other_description, updated_at
			from core.note_sources
			where note_id = @note_id
			""", connection))
		{
			command.Parameters.AddWithValue("note_id", noteId);
			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			if (!await reader.ReadAsync(cancellationToken))
			{
				return null;
			}

			source = new NoteSourceModel
			{
				Id = reader.GetGuid(0),
				NoteId = reader.GetGuid(1),
				SourceType = ParseNoteSourceType(reader.GetString(2)),
				OtherDescription = reader.IsDBNull(3) ? null : reader.GetString(3),
				UpdatedAt = reader.GetDateTime(4)
			};
		}

		await using (var command = new NpgsqlCommand("""
			select r.exit_note_id, r.reference_order, n.note_number
			from core.note_source_exit_refs r
			left join core.notes n on n.id = r.exit_note_id
			where r.source_id = @source_id
			order by r.reference_order, r.id
			""", connection))
		{
			command.Parameters.AddWithValue("source_id", source.Id);
			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				source.ExitReferences.Add(new NoteSourceExitReferenceModel
				{
					ExitNoteId = reader.GetGuid(0),
					ReferenceOrder = reader.GetInt32(1),
					NoteNumber = reader.IsDBNull(2) ? null : reader.GetString(2)
				});
			}
		}

		await using (var command = new NpgsqlCommand("""
			select id, invoice_number, invoice_date, destination,
			       attachment_file_name, attachment_content_type, attachment_data
			from core.note_source_invoices
			where source_id = @source_id
			order by invoice_date nulls last, invoice_number, id
			""", connection))
		{
			command.Parameters.AddWithValue("source_id", source.Id);
			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				source.Invoices.Add(new NoteSourceInvoiceModel
				{
					Id = reader.GetGuid(0),
					InvoiceNumber = reader.GetString(1),
					InvoiceDate = reader.IsDBNull(2) ? null : ReadDate(reader, "invoice_date"),
					Destination = reader.IsDBNull(3) ? null : reader.GetString(3),
					AttachmentFileName = reader.IsDBNull(4) ? null : reader.GetString(4),
					AttachmentContentType = reader.IsDBNull(5) ? null : reader.GetString(5),
					AttachmentData = reader.IsDBNull(6) ? null : (byte[])reader.GetValue(6)
				});
			}
		}

		await using (var command = new NpgsqlCommand("""
			select description
			from core.note_source_loot
			where source_id = @source_id
			""", connection))
		{
			command.Parameters.AddWithValue("source_id", source.Id);
			source.LootDescription = await command.ExecuteScalarAsync(cancellationToken) as string;
		}

		return source;
	}

	public async Task SaveNoteSourceAsync(
		NoteSourceModel source,
		Guid requesterId,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(source);
		_modeService.EnsureWritable();
		var note = (await GetNoteInternalAsync(source.NoteId, cancellationToken))
			?? throw new InvalidOperationException("المذكرة غير موجودة أو محذوفة.");
		await EnsureNoteEditorAsync(requesterId, note, cancellationToken);
		if (source.SourceType == NoteSourceType.Exit && source.ExitReferences.Count == 0)
		{
			throw new InvalidOperationException("يجب تحديد مذكرة إخراج واحدة على الأقل كمصدر.");
		}
		if (source.SourceType == NoteSourceType.Invoice && source.Invoices.Count == 0)
		{
			throw new InvalidOperationException("يجب تحديد فاتورة واحدة على الأقل كمصدر.");
		}
		if (source.SourceType == NoteSourceType.Loot && string.IsNullOrWhiteSpace(source.LootDescription))
		{
			throw new InvalidOperationException("وصف المضبوطات مطلوب.");
		}
		if (source.SourceType == NoteSourceType.Other && string.IsNullOrWhiteSpace(source.OtherDescription))
		{
			throw new InvalidOperationException("وصف المصدر الآخر مطلوب.");
		}
		if (source.SourceType == NoteSourceType.Exit && source.ExitReferences.Select(reference => reference.ExitNoteId).Distinct().Count() != source.ExitReferences.Count)
		{
			throw new InvalidOperationException("لا يجوز تكرار مذكرة الإخراج في قائمة المصادر.");
		}
		foreach (var invoice in source.Invoices)
		{
			if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
			{
				throw new InvalidOperationException("رقم الفاتورة مطلوب.");
			}
		}

		await using var connection = Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		if (!await HasNoteSourceContractAsync(connection, cancellationToken))
		{
			throw new InvalidOperationException("يتطلب نموذج مصادر المذكرة ترقية قاعدة البيانات إلى الإصدار 063.");
		}

		var before = await ReadNoteSourceForAuditAsync(connection, source.NoteId, cancellationToken);
		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
		try
		{
			Guid sourceId;
			DateTime updatedAt;
			await using (var command = new NpgsqlCommand("""
				insert into core.note_sources(note_id, source_type, other_description, created_by)
				values (@note_id, @source_type, @other_description, @requester_id)
				on conflict (note_id) do update set
				    source_type = excluded.source_type,
				    other_description = excluded.other_description,
				    updated_at = now()
				returning id, updated_at
				""", connection, transaction))
			{
				command.Parameters.AddWithValue("note_id", source.NoteId);
				command.Parameters.AddWithValue("source_type", source.SourceType.ToString().ToUpperInvariant());
				command.Parameters.AddWithValue("other_description", source.SourceType == NoteSourceType.Other
					? source.OtherDescription!.Trim()
					: DBNull.Value);
				command.Parameters.AddWithValue("requester_id", requesterId);
				await using var reader = await command.ExecuteReaderAsync(cancellationToken);
				if (!await reader.ReadAsync(cancellationToken))
				{
					throw new InvalidOperationException("تعذر إنشاء مصدر المذكرة.");
				}
				sourceId = reader.GetGuid(0);
				updatedAt = reader.GetDateTime(1);
			}

			await using (var command = new NpgsqlCommand("""
				delete from core.note_source_exit_refs where source_id = @source_id;
				delete from core.note_source_invoices where source_id = @source_id;
				delete from core.note_source_loot where source_id = @source_id;
				""", connection, transaction))
			{
				command.Parameters.AddWithValue("source_id", sourceId);
				await command.ExecuteNonQueryAsync(cancellationToken);
			}

			if (source.SourceType == NoteSourceType.Exit)
			{
				for (var index = 0; index < source.ExitReferences.Count; index++)
				{
					await using var command = new NpgsqlCommand("""
						insert into core.note_source_exit_refs(source_id, exit_note_id, reference_order)
						values (@source_id, @exit_note_id, @reference_order)
						""", connection, transaction);
					command.Parameters.AddWithValue("source_id", sourceId);
					command.Parameters.AddWithValue("exit_note_id", source.ExitReferences[index].ExitNoteId);
					command.Parameters.AddWithValue("reference_order", index + 1);
					await command.ExecuteNonQueryAsync(cancellationToken);
				}
			}
			else if (source.SourceType == NoteSourceType.Invoice)
			{
				foreach (var invoice in source.Invoices)
				{
					await using var command = new NpgsqlCommand("""
						insert into core.note_source_invoices(
						    source_id, invoice_number, invoice_date, destination,
						    attachment_file_name, attachment_content_type, attachment_data)
						values (@source_id, @invoice_number, @invoice_date, @destination,
						    @attachment_file_name, @attachment_content_type, @attachment_data)
						""", connection, transaction);
					command.Parameters.AddWithValue("source_id", sourceId);
					command.Parameters.AddWithValue("invoice_number", invoice.InvoiceNumber.Trim());
					command.Parameters.Add("invoice_date", NpgsqlDbType.Date).Value = (object?)invoice.InvoiceDate ?? DBNull.Value;
					command.Parameters.AddWithValue("destination", (object?)invoice.Destination?.Trim() ?? DBNull.Value);
					command.Parameters.AddWithValue("attachment_file_name", (object?)invoice.AttachmentFileName ?? DBNull.Value);
					command.Parameters.AddWithValue("attachment_content_type", (object?)invoice.AttachmentContentType ?? DBNull.Value);
					command.Parameters.Add("attachment_data", NpgsqlDbType.Bytea).Value = (object?)invoice.AttachmentData ?? DBNull.Value;
					await command.ExecuteNonQueryAsync(cancellationToken);
				}
			}
			else if (source.SourceType == NoteSourceType.Loot)
			{
				await using var command = new NpgsqlCommand("""
					insert into core.note_source_loot(source_id, description)
					values (@source_id, @description)
					""", connection, transaction);
				command.Parameters.AddWithValue("source_id", sourceId);
				command.Parameters.AddWithValue("description", source.LootDescription!.Trim());
				await command.ExecuteNonQueryAsync(cancellationToken);
			}

			await using (var syncNoteCmd = new NpgsqlCommand(
				source.SourceType == NoteSourceType.Loot
					? "update core.notes set is_loot = true where id = @note_id and not is_loot;"
					: "update core.notes set is_loot = false where id = @note_id and is_loot;",
				connection, transaction))
			{
				syncNoteCmd.Parameters.AddWithValue("note_id", note.Id);
				await syncNoteCmd.ExecuteNonQueryAsync(cancellationToken);
			}

			source.Id = sourceId;
			source.NoteId = note.Id;
			source.UpdatedAt = updatedAt;
			await WriteStructuredAuditRecordAsync(
				connection,
				transaction,
				requesterId,
				"UPDATE_NOTE_SOURCE",
				"core",
				"note_sources",
				sourceId,
				"تحديث مصدر المذكرة مع حفظ الحالة السابقة واللاحقة",
				before,
				BuildNoteSourceAuditSnapshot(source),
				cancellationToken);
			await transaction.CommitAsync(cancellationToken);
		}
		catch
		{
			await transaction.RollbackAsync(CancellationToken.None);
			throw;
		}
	}

	public async Task RemoveNoteSourceAsync(
		Guid noteId,
		Guid requesterId,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		_modeService.EnsureWritable();
		var note = (await GetNoteInternalAsync(noteId, cancellationToken))
			?? throw new InvalidOperationException("المذكرة غير موجودة أو محذوفة.");
		await EnsureNoteEditorAsync(requesterId, note, cancellationToken);

		await using var connection = Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		if (!await HasNoteSourceContractAsync(connection, cancellationToken))
		{
			throw new InvalidOperationException("يتطلب نموذج مصادر المذكرة ترقية قاعدة البيانات إلى الإصدار 063.");
		}
		var before = await ReadNoteSourceForAuditAsync(connection, noteId, cancellationToken)
			?? throw new InvalidOperationException("مصدر المذكرة غير موجود.");

		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
		await using var command = new NpgsqlCommand("delete from core.note_sources where note_id = @note_id", connection, transaction);
		command.Parameters.AddWithValue("note_id", noteId);
		if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
		{
			throw new InvalidOperationException("مصدر المذكرة غير موجود.");
		}
		await using (var unlootCmd = new NpgsqlCommand("update core.notes set is_loot = false where id = @note_id and is_loot;", connection, transaction))
		{
			unlootCmd.Parameters.AddWithValue("note_id", noteId);
			await unlootCmd.ExecuteNonQueryAsync(cancellationToken);
		}
		await WriteStructuredAuditRecordAsync(
			connection,
			transaction,
			requesterId,
			"REMOVE_NOTE_SOURCE",
			"core",
			"note_sources",
			noteId,
			"إزالة مصدر المذكرة مع حفظ الحالة السابقة",
			before,
			"null",
			cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	private static async Task<bool> HasNoteSourceContractAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand("""
			select exists(
			    select 1 from information_schema.tables
			    where table_schema = 'core' and table_name = 'note_sources')
			""", connection);
		return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
	}

	private static NoteSourceType ParseNoteSourceType(string value)
	{
		return Enum.TryParse<NoteSourceType>(value, true, out var result)
			? result
			: throw new InvalidOperationException($"نوع مصدر غير معروف: {value}");
	}

	private static async Task<string?> ReadNoteSourceForAuditAsync(
		NpgsqlConnection connection,
		Guid noteId,
		CancellationToken cancellationToken)
	{
		NoteSourceModel? source;
		await using (var command = new NpgsqlCommand("""
			select id, note_id, source_type, other_description, updated_at
			from core.note_sources where note_id = @note_id
			""", connection))
		{
			command.Parameters.AddWithValue("note_id", noteId);
			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			if (!await reader.ReadAsync(cancellationToken))
			{
				return null;
			}
			source = new NoteSourceModel
			{
				Id = reader.GetGuid(0),
				NoteId = reader.GetGuid(1),
				SourceType = ParseNoteSourceType(reader.GetString(2)),
				OtherDescription = reader.IsDBNull(3) ? null : reader.GetString(3),
				UpdatedAt = reader.GetDateTime(4)
			};
		}

		await using (var command = new NpgsqlCommand("""
			select exit_note_id, reference_order
			from core.note_source_exit_refs where source_id = @source_id
			order by reference_order, id
			""", connection))
		{
			command.Parameters.AddWithValue("source_id", source.Id);
			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				source.ExitReferences.Add(new NoteSourceExitReferenceModel
				{
					ExitNoteId = reader.GetGuid(0),
					ReferenceOrder = reader.GetInt32(1)
				});
			}
		}

		await using (var command = new NpgsqlCommand("""
			select id, invoice_number, invoice_date, destination,
			       attachment_file_name, attachment_content_type,
			       octet_length(attachment_data)
			from core.note_source_invoices where source_id = @source_id
			order by invoice_date nulls last, invoice_number, id
			""", connection))
		{
			command.Parameters.AddWithValue("source_id", source.Id);
			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				source.Invoices.Add(new NoteSourceInvoiceModel
				{
					Id = reader.GetGuid(0),
					InvoiceNumber = reader.GetString(1),
					InvoiceDate = reader.IsDBNull(2) ? null : ReadDate(reader, "invoice_date"),
					Destination = reader.IsDBNull(3) ? null : reader.GetString(3),
					AttachmentFileName = reader.IsDBNull(4) ? null : reader.GetString(4),
					AttachmentContentType = reader.IsDBNull(5) ? null : reader.GetString(5),
					AttachmentData = reader.IsDBNull(6) ? null : new byte[reader.GetInt32(6)]
				});
			}
		}

		await using (var command = new NpgsqlCommand("""
			select description from core.note_source_loot where source_id = @source_id
			""", connection))
		{
			command.Parameters.AddWithValue("source_id", source.Id);
			source.LootDescription = await command.ExecuteScalarAsync(cancellationToken) as string;
		}

		return BuildNoteSourceAuditSnapshot(source);
	}

	private static string BuildNoteSourceAuditSnapshot(NoteSourceModel source)
	{
		return JsonSerializer.Serialize(new
		{
			source.Id,
			source.NoteId,
			source.SourceType,
			source.OtherDescription,
			source.LootDescription,
			exitReferences = source.ExitReferences.Select(reference => new { reference.ExitNoteId, reference.ReferenceOrder }).ToArray(),
			invoices = source.Invoices.Select(invoice => new
			{
				invoice.Id,
				invoice.InvoiceNumber,
				invoice.InvoiceDate,
				invoice.Destination,
				invoice.AttachmentFileName,
				invoice.AttachmentContentType,
				attachmentBytes = invoice.AttachmentData?.Length ?? 0
			}).ToArray(),
			source.UpdatedAt
		});
	}

	private static InvalidOperationException CreateLinkValidationException(PostgresException exception)
	{
		string sqlState = exception.SqlState;
		if (1 == 0)
		{
		}
		string text = ((sqlState == "23505") ? "إحدى المذكرتين مرتبطة مسبقا\u064b بمذكرة أخرى. اختر مذكرة متاحة ثم حاول مجددا\u064b." : ((!(sqlState == "23503")) ? ((!exception.MessageText.Contains("same company", StringComparison.OrdinalIgnoreCase)) ? "الربط متاح بين مذكرة إدخال ومذكرة إخراج فقط." : "يجب أن تنتمي المذكرتان إلى الشركة نفسها.") : "إحدى المذكرتين غير موجودة أو محذوفة."));
		if (1 == 0)
		{
		}
		string message = text;
		return new InvalidOperationException(message, exception);
	}

	public async Task<IReadOnlyList<ReviewQueueItemModel>> GetReviewQueueAsync(Guid reviewerId, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<ReviewQueueItemModel> list = new List<ReviewQueueItemModel>();
		IReadOnlyList<ReviewQueueItemModel> result;
		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			await EnsureReviewerAsync(connection, reviewerId, cancellationToken);
			IReadOnlyList<ReviewQueueItemModel> readOnlyList2;
			await using (NpgsqlCommand command = new NpgsqlCommand("select n.id, n.note_number, c.name, nt.name_ar, factory.name, production_line.name, n.arrival_at, n.status,\r\n       n.created_by, creator.full_name, n.created_at, n.reviewer_id, reviewer.full_name, n.review_started_at, n.updated_at\r\nfrom core.notes n\r\njoin core.companies c on c.id = n.company_id\r\njoin core.note_types nt on nt.id = n.note_type_id\r\nleft join core.factories factory on factory.id = n.factory_id\r\nleft join core.production_lines production_line on production_line.id = n.production_line_id\r\nleft join auth.users creator on creator.id = n.created_by\r\nleft join auth.users reviewer on reviewer.id = n.reviewer_id\r\nwhere n.is_deleted = false\r\n  and (n.created_by is null or created_by <> @reviewer_id)\r\n  and (\r\n        n.status in ('COMPLETE', 'HAS_ERRORS', 'NEEDS_REVIEW')\r\n        or (n.status = 'UNDER_REVIEW' and n.reviewer_id = @reviewer_id)\r\n      )\r\norder by case when n.reviewer_id = @reviewer_id then 0 else 1 end, n.arrival_at", connection))
			{
				command.Parameters.AddWithValue("reviewer_id", reviewerId);
				IReadOnlyList<ReviewQueueItemModel> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						list.Add(new ReviewQueueItemModel
						{
							NoteId = reader.GetGuid(0),
							NoteNumber = reader.GetString(1),
							CompanyName = reader.GetString(2),
							NoteTypeName = reader.GetString(3),
							FactoryName = (reader.IsDBNull(4) ? null : reader.GetString(4)),
							ProductionLineName = (reader.IsDBNull(5) ? null : reader.GetString(5)),
							ArrivalAt = RepositoryBase.ReadDate(reader, "arrival_at"),
							Status = RepositoryBase.EnumValue<NoteStatus>(reader.GetString(7)),
							CreatedBy = (reader.IsDBNull(8) ? ((Guid?)null) : new Guid?(reader.GetGuid(8))),
							CreatedByName = (reader.IsDBNull(9) ? null : reader.GetString(9)),
							CreatedAt = reader.GetDateTime(10),
							ReviewerId = (reader.IsDBNull(11) ? ((Guid?)null) : new Guid?(reader.GetGuid(11))),
							ReviewerName = (reader.IsDBNull(12) ? null : reader.GetString(12)),
							ReviewStartedAt = (reader.IsDBNull(13) ? ((DateTime?)null) : new DateTime?(reader.GetDateTime(13)))
							,UpdatedAt = reader.GetDateTime(14)
						});
					}
					readOnlyList = list;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task StartReviewAsync(Guid id, Guid reviewerId, DateTime? expectedUpdatedAt = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		_modeService.EnsureWritable();
		await using NpgsqlConnection connection = base.Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await EnsureReviewerAsync(connection, reviewerId, cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await using NpgsqlCommand command = new NpgsqlCommand("update core.notes\r\nset status = 'UNDER_REVIEW', reviewer_id = @reviewer_id, review_started_at = now(),\r\n    return_reason = null, updated_by = @reviewer_id\r\nwhere id = @id\r\n  and is_deleted = false\r\n  and (created_by is null or created_by <> @reviewer_id)\r\n  and reviewer_id is null\r\n  and status in ('COMPLETE', 'HAS_ERRORS', 'NEEDS_REVIEW')", connection, transaction);
		command.CommandText += "\r\n  and (@expected_updated_at is null or updated_at = @expected_updated_at)";
		command.Parameters.AddWithValue("id", id);
		command.Parameters.AddWithValue("reviewer_id", reviewerId);
		command.Parameters.Add("expected_updated_at", NpgsqlDbType.Timestamp).Value = (object?)expectedUpdatedAt ?? DBNull.Value;
		if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
		{
			throw new InvalidOperationException("تعذر بدء التدقيق لأن المذكرة ح\u064fجزت أو تغيرت حالتها بواسطة مستخدم آخر.");
		}
		await WriteReviewHistoryAsync(connection, transaction, id, "START_REVIEW", null, reviewerId, cancellationToken);
		await RepositoryBase.WriteAuditRecordAsync(connection, transaction, reviewerId, "START_REVIEW", "core", "notes", id, "بدء تدقيق مذكرة", cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task ReturnForCorrectionAsync(Guid id, Guid reviewerId, string reason, DateTime? expectedUpdatedAt = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		_modeService.EnsureWritable();
		if (string.IsNullOrWhiteSpace(reason))
		{
			throw new InvalidOperationException("سبب الإعادة للتصحيح مطلوب.");
		}
		await using NpgsqlConnection connection = base.Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await EnsureReviewerAsync(connection, reviewerId, cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await using NpgsqlCommand command = new NpgsqlCommand("update core.notes\r\nset status = 'RETURNED_FOR_CORRECTION', reviewer_id = null, review_started_at = null,\r\n    return_reason = @reason, updated_by = @reviewer_id\r\nwhere id = @id and is_deleted = false\r\n  and status = 'UNDER_REVIEW' and reviewer_id = @reviewer_id", connection, transaction);
		command.CommandText += "\r\n  and (@expected_updated_at is null or updated_at = @expected_updated_at)";
		command.Parameters.AddWithValue("id", id);
		command.Parameters.AddWithValue("reviewer_id", reviewerId);
		command.Parameters.AddWithValue("reason", reason.Trim());
		command.Parameters.Add("expected_updated_at", NpgsqlDbType.Timestamp).Value = (object?)expectedUpdatedAt ?? DBNull.Value;
		if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
		{
			throw new InvalidOperationException("لا يمكن إعادة المذكرة لأنها ليست محجوزة للتدقيق من قبلك.");
		}
		await WriteReviewHistoryAsync(connection, transaction, id, "RETURN_FOR_CORRECTION", reason.Trim(), reviewerId, cancellationToken);
		await RepositoryBase.WriteAuditRecordAsync(connection, transaction, reviewerId, "RETURN_FOR_CORRECTION", "core", "notes", id, reason.Trim(), cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task ReleaseReviewAsync(Guid id, Guid adminUserId, string reason, CancellationToken cancellationToken = default(CancellationToken))
	{
		_modeService.EnsureWritable();
		if (string.IsNullOrWhiteSpace(reason))
		{
			throw new InvalidOperationException("سبب تحرير المراجعة مطلوب.");
		}
		await RepositoryBase.EnsureRoleAsync(base.Factory, adminUserId, new[] { UserRole.ADMIN }, cancellationToken);
		await using NpgsqlConnection connection = base.Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await using NpgsqlCommand command = new NpgsqlCommand("update core.notes set status = 'NEEDS_REVIEW', reviewer_id = null, review_started_at = null, updated_by = @user_id\r\nwhere id = @id and not is_deleted and status = 'UNDER_REVIEW'", connection, transaction);
		command.Parameters.AddWithValue("id", id);
		command.Parameters.AddWithValue("user_id", adminUserId);
		if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
		{
			throw new InvalidOperationException("المذكرة ليست قيد مراجعة حاليا\u064b.");
		}
		await WriteReviewHistoryAsync(connection, transaction, id, "ADMIN_RELEASE_REVIEW", reason.Trim(), adminUserId, cancellationToken);
		await RepositoryBase.WriteAuditRecordAsync(connection, transaction, adminUserId, "ADMIN_RELEASE_REVIEW", "core", "notes", id, reason.Trim(), cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<Guid> SaveNoteAsync(NoteModel note, Guid userId, bool saveAsDraft = false, Guid? relatedNoteId = null, DuplicateResolutionRequest? duplicateResolution = null, CancellationToken cancellationToken = default(CancellationToken), bool markAllIndicatorsCorrect = false, bool synchronizeRelatedNote = false, Guid? operationId = null, IReadOnlyList<Guid>? relatedNoteIds = null)
	{
		_modeService.EnsureWritable();
		bool isOperationCreate = operationId.HasValue;
		string? operationFingerprint = null;
		if (isOperationCreate)
		{
			if (operationId == Guid.Empty)
			{
				throw new InvalidOperationException("معرّف عملية إنشاء المذكرة غير صالح.");
			}
			if (note.Id == Guid.Empty)
			{
				throw new InvalidOperationException("يجب تثبيت معرّف المذكرة قبل بدء عملية الإنشاء.");
			}
			operationFingerprint = BuildNoteCreateFingerprint(
				note,
				saveAsDraft,
				relatedNoteId,
				duplicateResolution,
				markAllIndicatorsCorrect,
				synchronizeRelatedNote,
				relatedNoteIds);
			NoteCreateOperationRecord? completedOperation = await FindNoteCreateOperationAsync(
				operationId!.Value,
				note.Id,
				cancellationToken);
			if (completedOperation is not null)
			{
				return ValidateNoteCreateReplay(
					completedOperation,
					operationId.Value,
					note.Id,
					userId,
					operationFingerprint);
			}
		}
		bool createsNewNote = isOperationCreate || note.Id == Guid.Empty;
		NoteModel noteModel = ((!createsNewNote) ? (await GetNoteInternalAsync(note.Id, cancellationToken)) : null);
		NoteModel existing = noteModel;
		if (!createsNewNote && existing == null)
		{
			throw new InvalidOperationException("المذكرة غير موجودة أو ح\u064fذفت.");
		}
		var requesterRole = await RepositoryBase.GetUserRoleAsync(base.Factory, userId, cancellationToken);
		if (existing is not null)
		{
			NoteValidator.EnsureApprovedEditAllowed(existing, requesterRole);
		}
		NoteValidator.EnsureDraftSaveAllowed(existing, saveAsDraft);
		await EnsureNoteEditorAsync(userId, existing, cancellationToken);
		bool isAdminExistingEdit = NoteValidator.IsAdminExistingEdit(existing, requesterRole);
		bool canEditExisting = existing is null
			|| existing.Status is NoteStatus.DRAFT or NoteStatus.RETURNED_FOR_CORRECTION
			|| isAdminExistingEdit;
		if (!canEditExisting)
		{
			throw new InvalidOperationException("لا يمكن تعديل المذكرة بعد إرسالها للتدقيق إلا إذا أعادها المدقق للتصحيح أو كان التعديل من مدير النظام.");
		}
		bool isResubmission = existing != null && existing.Status == NoteStatus.RETURNED_FOR_CORRECTION;
		string normalizedNumber = NoteNumberNormalizer.Normalize(note.NoteNumber);
		long? numericNumberValue = NoteNumberNormalizer.TryParsePositive(note.NoteNumber, out var parsedNumber) ? parsedNumber : null;
		note.DuplicateReason = null;
		if (duplicateResolution != null && duplicateResolution.Classification != DuplicateClassification.COMPANY_DUPLICATE_NUMBER)
		{
			throw new InvalidOperationException("حفظ مذكرة جديدة مسموح فقط عند تصنيف الحالة كتكرار رقم صادر عن الشركة.");
		}
		List<NoteTypeFieldModel> fields = (await _noteTypeService.GetFieldsForTypeAsync(note.NoteTypeId, cancellationToken)).Where((NoteTypeFieldModel field) => field.IsActive).ToList();
		HashSet<Guid> activeFieldIds = fields.Select((NoteTypeFieldModel field) => field.FieldId).ToHashSet();
		note.FieldValues = note.FieldValues.Where((NoteFieldValueModel noteFieldValueModel) => activeFieldIds.Contains(noteFieldValueModel.FieldId)).ToList();
		if (markAllIndicatorsCorrect && note.FieldValues.Any((NoteFieldValueModel noteFieldValueModel) => noteFieldValueModel.FieldValue != IndicatorValue.Correct))
		{
			throw new InvalidOperationException("لا يمكن تسجيل اعتبار جميع المؤشرات صحيحة لأن بعض القيم ليست صحيحة.");
		}
		NoteValidator.Validate(note, fields, !saveAsDraft);
		if (note.AttachmentData != null || !string.IsNullOrWhiteSpace(note.AttachmentFileName))
		{
			AttachmentFilePolicy.Validate(note.AttachmentFileName, note.AttachmentData);
			note.AttachmentContentType = AttachmentFilePolicy.GetContentType(note.AttachmentFileName);
		}
		if (isAdminExistingEdit)
		{
			note.Status = existing!.Status;
		}
		else
		{
			note.Status = ((!saveAsDraft) ? NoteValidator.CalculateStatus(fields, note.FieldValues) : NoteStatus.DRAFT);
		}
		if (!saveAsDraft && note.Status == NoteStatus.DRAFT)
		{
			throw new InvalidOperationException("لا يمكن إرسال المذكرة قبل مراجعة جميع البنود الإلزامية.");
		}
		Guid result;
		await using (NpgsqlConnection connection = base.Factory.CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			var hasNumericContract = await HasNumericNoteNumberContractAsync(connection, cancellationToken);
			if (hasNumericContract)
			{
				if (!numericNumberValue.HasValue && !(isAdminExistingEdit && existing!.NoteNumber == note.NoteNumber))
				{
					throw new InvalidOperationException("رقم المذكرة يجب أن يكون عددًا صحيحًا موجبًا، ويمكن إدخال الأرقام العربية أو اللاتينية فقط.");
				}
				// تاريخ الكتابة اختياري (ترحيل 010): سنة التسلسل تُشتق منه عند وجوده وتبقى NULL عند غيابه.
			}
			await using (NpgsqlCommand catalogCommand = new NpgsqlCommand("select\r\n    (exists(select 1 from core.companies where id = @company_id and is_active and not is_deleted)\r\n     or (@preserve_existing_catalog and exists(select 1 from core.companies where id = @company_id and not is_deleted)))\r\n    and (exists(select 1 from core.note_types where id = @note_type_id and is_active and not is_deleted)\r\n     or (@preserve_existing_catalog and exists(select 1 from core.note_types where id = @note_type_id and not is_deleted)))", connection))
			{
				catalogCommand.Parameters.AddWithValue("company_id", note.CompanyId);
				catalogCommand.Parameters.AddWithValue("note_type_id", note.NoteTypeId);
				catalogCommand.Parameters.AddWithValue("preserve_existing_catalog", isAdminExistingEdit
					&& existing!.CompanyId == note.CompanyId
					&& existing.NoteTypeId == note.NoteTypeId);
				if (!Convert.ToBoolean(await catalogCommand.ExecuteScalarAsync(cancellationToken)))
				{
					throw new InvalidOperationException("الشركة أو نوع المذكرة غير موجود أو غير فعال.");
				}
			}
			Guid? factoryId = note.FactoryId;
			Guid factoryId2 = default(Guid);
			int num2;
			if (factoryId.HasValue)
			{
				factoryId2 = factoryId.GetValueOrDefault();
				num2 = 1;
			}
			else
			{
				num2 = 0;
			}
			if (num2 != 0)
			{
				bool preservesExistingFactory = isAdminExistingEdit
					&& existing!.FactoryId == factoryId2
					&& existing.CompanyId == note.CompanyId;
				await using NpgsqlCommand factoryCheck = new NpgsqlCommand("select exists(select 1 from core.factories where id = @id and company_id = @company_id and not is_deleted and (is_active or @preserves_existing_factory))", connection);
				factoryCheck.Parameters.AddWithValue("id", factoryId2);
				factoryCheck.Parameters.AddWithValue("company_id", note.CompanyId);
				factoryCheck.Parameters.AddWithValue("preserves_existing_factory", preservesExistingFactory);
				if (!Convert.ToBoolean(await factoryCheck.ExecuteScalarAsync(cancellationToken)))
				{
					throw new InvalidOperationException("المصنع المحدد لا يتبع للشركة المختارة.");
				}
			}
			else if (note.ProductionLineId.HasValue)
			{
				throw new InvalidOperationException("لا يمكن اختيار خط إنتاج دون تحديد مصنع.");
			}
			Guid productionLineId = default(Guid);
			if (note.ProductionLineId.HasValue)
			{
				productionLineId = note.ProductionLineId.Value;
			}
			if (note.ProductionLineId.HasValue)
			{
				bool preservesExistingLine = existing != null && existing.ProductionLineId == productionLineId && existing.FactoryId == note.FactoryId;
				await using NpgsqlCommand lineCheck = new NpgsqlCommand("select exists(\r\n    select 1\r\n    from core.production_lines\r\n    where id = @id\r\n      and factory_id = @factory_id\r\n      and not is_deleted\r\n      and (is_active or @preserves_existing_line)\r\n)", connection);
				lineCheck.Parameters.AddWithValue("id", productionLineId);
				lineCheck.Parameters.AddWithValue("factory_id", note.FactoryId.Value);
				lineCheck.Parameters.AddWithValue("preserves_existing_line", preservesExistingLine);
				if (!Convert.ToBoolean(await lineCheck.ExecuteScalarAsync(cancellationToken)))
				{
					throw new InvalidOperationException("خط الإنتاج المحدد لا يتبع للمصنع المختار أو أنه غير فعال.");
				}
			}
			Guid id;
			await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
			{
				bool isNew = createsNewNote;
				if (isOperationCreate)
				{
					await AcquireNoteCreateOperationLockAsync(
						connection,
						transaction,
						operationId!.Value,
						cancellationToken);
					bool reserved = await TryReserveNoteCreateOperationAsync(
						connection,
						transaction,
						operationId!.Value,
						note.Id,
						userId,
						operationFingerprint!,
						cancellationToken);
					if (!reserved)
					{
						NoteCreateOperationRecord replay = await FindNoteCreateOperationAsync(
							connection,
							transaction,
							operationId.Value,
							note.Id,
							cancellationToken)
							?? throw new InvalidOperationException("تعذر التحقق من عملية إنشاء المذكرة المتزامنة.");
						Guid replayedNoteId = ValidateNoteCreateReplay(
							replay,
							operationId.Value,
							note.Id,
							userId,
							operationFingerprint!);
						await transaction.CommitAsync(cancellationToken);
						return replayedNoteId;
					}

					await using NpgsqlCommand noteIdCheck = new NpgsqlCommand("select exists(select 1 from core.notes where id = @note_id)", connection, transaction);
					noteIdCheck.Parameters.AddWithValue("note_id", note.Id);
					if (Convert.ToBoolean(await noteIdCheck.ExecuteScalarAsync(cancellationToken)))
					{
						throw new InvalidOperationException("معرّف المذكرة المثبّت مستخدم مسبقاً في عملية أخرى.");
					}
				}
				await AcquireDuplicateLockAsync(connection, transaction, note.CompanyId, note.NoteTypeId, normalizedNumber, note.WritingDate, numericNumberValue, cancellationToken, note.IsInvoice);
				List<Guid> duplicateMatches = await FindDuplicateIdsAsync(connection, transaction, isNew ? ((Guid?)null) : new Guid?(note.Id), note.CompanyId, note.NoteTypeId, normalizedNumber, note.WritingDate, numericNumberValue, hasNumericContract, cancellationToken, note.IsInvoice);
				if (duplicateMatches.Count > 0)
				{
					if (duplicateResolution == null || duplicateResolution.Classification != DuplicateClassification.COMPANY_DUPLICATE_NUMBER || !duplicateMatches.Contains(duplicateResolution.MatchedNoteId))
					{
						throw new InvalidOperationException("توجد مذكرة مطابقة. يجب اختيار التصنيف والمذكرة الأصلية من شاشة معالجة التكرار.");
					}
				}
				if (duplicateResolution != null && duplicateMatches.Count == 0)
				{
					throw new InvalidOperationException("لم تعد توجد مذكرة مطابقة للقرار المحدد. أعد المحاولة.");
				}
				if (isNew && note.Id == Guid.Empty)
				{
					note.Id = Guid.NewGuid();
				}
				await using (NpgsqlCommand command = new NpgsqlCommand("insert into core.notes(\r\n    id, note_number, note_type_id, company_id, writing_date, arrival_at,\r\n    image_path, file_path, attachment_file_name, attachment_content_type, attachment_data,\r\n    notes, status, note_number_normalized, duplicate_reason, factory_id, production_line_id, created_by, updated_by, is_loot, is_invoice)\r\nvalues (\r\n    @id, @note_number, @note_type_id, @company_id, @writing_date, @arrival_at,\r\n    @image_path, @file_path, @attachment_file_name, @attachment_content_type, @attachment_data,\r\n    @notes, @status, @note_number_normalized, @duplicate_reason, @factory_id, @production_line_id, @user_id, @user_id, @is_loot, @is_invoice)\r\non conflict (id) do update set\r\n    note_number = excluded.note_number,\r\n    note_type_id = excluded.note_type_id,\r\n    company_id = excluded.company_id,\r\n    writing_date = excluded.writing_date,\r\n    arrival_at = excluded.arrival_at,\r\n    image_path = excluded.image_path,\r\n    file_path = excluded.file_path,\r\n    attachment_file_name = excluded.attachment_file_name,\r\n    attachment_content_type = excluded.attachment_content_type,\r\n    attachment_data = excluded.attachment_data,\r\n    notes = excluded.notes,\r\n    status = excluded.status,\r\n    note_number_normalized = excluded.note_number_normalized,\r\n    duplicate_reason = excluded.duplicate_reason,\r\n    factory_id = excluded.factory_id,\r\n    updated_by = excluded.updated_by,\r\n    production_line_id = excluded.production_line_id,\r\n    reviewer_id = case when @clear_review then null else core.notes.reviewer_id end,\r\n    review_started_at = case when @clear_review then null else core.notes.review_started_at end,\r\n    return_reason = case when @clear_review then null else core.notes.return_reason end,\r\n    is_loot = excluded.is_loot,\r\n    is_invoice = excluded.is_invoice\r\nwhere (core.notes.status in ('DRAFT', 'RETURNED_FOR_CORRECTION')\r\n       or (@allow_approved_edit and core.notes.status = 'APPROVED'))\r\n  and core.notes.updated_at = @expected_updated_at", connection, transaction))
				{
					command.CommandText = """
insert into core.notes(
    id, note_number, note_type_id, company_id, writing_date, arrival_at,
    image_path, file_path, attachment_file_name, attachment_content_type, attachment_data,
    notes, status, note_number_normalized, duplicate_reason, factory_id, production_line_id, created_by, updated_by,
    is_loot, is_invoice)
values (
    @id, @note_number, @note_type_id, @company_id, @writing_date, @arrival_at,
    @image_path, @file_path, @attachment_file_name, @attachment_content_type, @attachment_data,
    @notes, @status, @note_number_normalized, @duplicate_reason, @factory_id, @production_line_id, @user_id, @user_id,
    @is_loot, @is_invoice)
on conflict (id) do update set
    note_number = excluded.note_number,
    note_type_id = excluded.note_type_id,
    company_id = excluded.company_id,
    writing_date = excluded.writing_date,
    arrival_at = excluded.arrival_at,
    image_path = excluded.image_path,
    file_path = excluded.file_path,
    attachment_file_name = excluded.attachment_file_name,
    attachment_content_type = excluded.attachment_content_type,
    attachment_data = excluded.attachment_data,
    notes = excluded.notes,
    status = case when @preserve_workflow_state then core.notes.status else excluded.status end,
    note_number_normalized = excluded.note_number_normalized,
    duplicate_reason = excluded.duplicate_reason,
    factory_id = excluded.factory_id,
    updated_by = excluded.updated_by,
    production_line_id = excluded.production_line_id,
    approved_by = case when @preserve_workflow_state then core.notes.approved_by else excluded.approved_by end,
    approved_at = case when @preserve_workflow_state then core.notes.approved_at else excluded.approved_at end,
    reviewer_id = case when @preserve_workflow_state then core.notes.reviewer_id when @clear_review then null else core.notes.reviewer_id end,
    review_started_at = case when @preserve_workflow_state then core.notes.review_started_at when @clear_review then null else core.notes.review_started_at end,
    return_reason = case when @preserve_workflow_state then core.notes.return_reason when @clear_review then null else core.notes.return_reason end,
    is_loot = excluded.is_loot,
    is_invoice = excluded.is_invoice
where (@preserve_workflow_state
       or core.notes.status in ('DRAFT', 'RETURNED_FOR_CORRECTION'))
  and core.notes.updated_at = @expected_updated_at
""";
					command.Parameters.AddWithValue("id", note.Id);
					command.Parameters.AddWithValue("note_number", note.NoteNumber);
					command.Parameters.AddWithValue("note_type_id", note.NoteTypeId);
					command.Parameters.AddWithValue("company_id", note.CompanyId);
					command.Parameters.Add("writing_date", NpgsqlDbType.Date).Value = ((object)note.WritingDate) ?? DBNull.Value;
					command.Parameters.AddWithValue("arrival_at", note.ArrivalAt);
					command.Parameters.AddWithValue("image_path", ((object)note.ImagePath) ?? ((object)DBNull.Value));
					command.Parameters.AddWithValue("file_path", ((object)note.FilePath) ?? ((object)DBNull.Value));
					command.Parameters.AddWithValue("attachment_file_name", ((object)note.AttachmentFileName) ?? ((object)DBNull.Value));
					command.Parameters.AddWithValue("attachment_content_type", ((object)note.AttachmentContentType) ?? ((object)DBNull.Value));
					command.Parameters.Add("attachment_data", NpgsqlDbType.Bytea).Value = ((object)note.AttachmentData) ?? ((object)DBNull.Value);
					command.Parameters.AddWithValue("notes", ((object)note.Notes) ?? ((object)DBNull.Value));
					command.Parameters.AddWithValue("status", note.Status.ToString());
					command.Parameters.AddWithValue("note_number_normalized", normalizedNumber);
					command.Parameters.AddWithValue("duplicate_reason", DBNull.Value);
					command.Parameters.Add("factory_id", NpgsqlDbType.Uuid).Value = ((object)note.FactoryId) ?? DBNull.Value;
					command.Parameters.Add("production_line_id", NpgsqlDbType.Uuid).Value = ((object)note.ProductionLineId) ?? DBNull.Value;
					command.Parameters.AddWithValue("user_id", userId);
					command.Parameters.AddWithValue("clear_review", isResubmission);
					command.Parameters.AddWithValue("preserve_workflow_state", isAdminExistingEdit);
					command.Parameters.Add("expected_updated_at", NpgsqlDbType.Timestamp).Value = (isNew ? DBNull.Value : ((object)note.UpdatedAt));
					command.Parameters.AddWithValue("is_loot", note.IsLoot);
					command.Parameters.AddWithValue("is_invoice", note.IsInvoice);
					if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
					{
						throw new InvalidOperationException("تعذر الحفظ لأن حالة المذكرة تغي\u0651رت. أعد تحميلها ثم حاول مجددا\u064b.");
					}
				}
			if (hasNumericContract)
			{
				await using NpgsqlCommand numericKeyCommand = new NpgsqlCommand("update core.notes set note_number_value = @note_number_value, note_year = @note_year where id = @id", connection, transaction);
				numericKeyCommand.Parameters.Add("note_number_value", NpgsqlDbType.Bigint).Value = (object?)numericNumberValue ?? DBNull.Value;
				numericKeyCommand.Parameters.Add("note_year", NpgsqlDbType.Integer).Value = (object?)note.WritingDate?.Year ?? DBNull.Value;
				numericKeyCommand.Parameters.AddWithValue("id", note.Id);
				await numericKeyCommand.ExecuteNonQueryAsync(cancellationToken);
			}
				if (!isAdminExistingEdit)
				{
					await using NpgsqlCommand staleValueCommand = new NpgsqlCommand("delete from core.note_field_values\r\nwhere note_id = @note_id\r\n  and not (field_id = any(@active_field_ids))", connection, transaction);
					staleValueCommand.Parameters.AddWithValue("note_id", note.Id);
					staleValueCommand.Parameters.Add("active_field_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = activeFieldIds.ToArray();
					await staleValueCommand.ExecuteNonQueryAsync(cancellationToken);
				}
				foreach (NoteFieldValueModel value in note.FieldValues)
				{
					await using NpgsqlCommand valueCommand = new NpgsqlCommand("insert into core.note_field_values(note_id, field_id, field_value, inactive_reason, created_by, updated_by)\r\nvalues (@note_id, @field_id, @field_value, @inactive_reason, @user_id, @user_id)\r\non conflict (note_id, field_id) do update set\r\n    field_value = excluded.field_value,\r\n    inactive_reason = excluded.inactive_reason,\r\n    updated_by = excluded.updated_by,\r\n    updated_at = now()", connection, transaction);
					valueCommand.Parameters.AddWithValue("note_id", note.Id);
					valueCommand.Parameters.AddWithValue("field_id", value.FieldId);
					valueCommand.Parameters.AddWithValue("field_value", (short)value.FieldValue);
					valueCommand.Parameters.AddWithValue("inactive_reason", ((object)value.InactiveReason) ?? ((object)DBNull.Value));
					valueCommand.Parameters.AddWithValue("user_id", userId);
					await valueCommand.ExecuteNonQueryAsync(cancellationToken);
				}
				if (!isAdminExistingEdit)
				{
					string historyAction = ((!saveAsDraft) ? (isResubmission ? "RESUBMIT_FOR_REVIEW" : "SUBMIT_FOR_REVIEW") : (isNew ? "CREATE_DRAFT" : "SAVE_DRAFT"));
					await WriteReviewHistoryAsync(connection, transaction, note.Id, historyAction, null, userId, cancellationToken);
				}
				Dictionary<Guid, NoteFieldValueModel> originalValues = existing?.FieldValues.ToDictionary((NoteFieldValueModel noteFieldValueModel) => noteFieldValueModel.FieldId) ?? new Dictionary<Guid, NoteFieldValueModel>();
				Dictionary<Guid, NoteFieldValueModel> currentValues = note.FieldValues.ToDictionary((NoteFieldValueModel noteFieldValueModel) => noteFieldValueModel.FieldId);
				List<string> indicatorChanges = (from fieldId in originalValues.Keys.Union(currentValues.Keys)
					select (Original: originalValues.GetValueOrDefault(fieldId), Current: currentValues.GetValueOrDefault(fieldId)) into pair
					where pair.Original == null || pair.Current == null || pair.Original.FieldValue != pair.Current.FieldValue || !string.Equals(pair.Original.InactiveReason, pair.Current.InactiveReason, StringComparison.Ordinal)
					select (pair.Current == null) ? ("حذف تاريخي: " + pair.Original.FieldName) : $"{pair.Current.FieldName}: {pair.Current.FieldValue}").ToList();
				string indicatorAudit = ((indicatorChanges.Count == 0) ? $"لم تتغير المؤشرات. العدد: {note.FieldValues.Count}" : $"تم تغيير {indicatorChanges.Count} مؤشر: {string.Join("؛ ", indicatorChanges)}");
                if (isAdminExistingEdit)
                {
                    await WriteStructuredAuditRecordAsync(
                        connection,
                        transaction,
                        userId,
                        "ADMIN_EDIT_NOTE",
                        "core",
                        "notes",
                        note.Id,
                        "تعديل إداري مع حفظ الحالة وبيانات سير العمل",
                        BuildNoteAuditSnapshot(existing!),
						BuildNoteAuditSnapshot(
							note,
							existing!.FieldValues
								.Where(value => !activeFieldIds.Contains(value.FieldId))
								.Concat(note.FieldValues)),
                        cancellationToken);
                }
                else
                {
                    await RepositoryBase.WriteAuditRecordAsync(connection, transaction, userId, isNew ? "CREATE_NOTE" : "UPDATE_NOTE", "core", "notes", note.Id, note.NoteNumber, cancellationToken);
                }
				await RepositoryBase.WriteAuditRecordAsync(connection, transaction, userId, "SAVE_INDICATORS", "core", "note_field_values", note.Id, indicatorAudit, cancellationToken);
				if (markAllIndicatorsCorrect)
				{
					await RepositoryBase.WriteAuditRecordAsync(connection, transaction, userId, "MARK_ALL_INDICATORS_CORRECT", "core", "note_field_values", note.Id, $"تم اعتبار جميع المؤشرات صحيحة. العدد: {note.FieldValues.Count}", cancellationToken);
				}
				if (duplicateResolution != null)
				{
					string metadata = BuildDuplicateMetadata(note, duplicateResolution.MatchedNoteId);
					if (await InsertDuplicateIncidentAsync(connection, transaction, duplicateResolution.Classification, duplicateResolution.MatchedNoteId, note.Id, note, userId, metadata, cancellationToken))
					{
						await WriteStructuredAuditRecordAsync(connection, transaction, userId, "COMPANY_DUPLICATE_NUMBER", "core", "duplicate_incidents", note.Id, "ح\u064fفظت مذكرة مختلفة تحمل رقما\u064b مكررا\u064b من الشركة.", metadata, cancellationToken);
					}
				}
				var targetRelatedIds = new HashSet<Guid>();
				if (relatedNoteIds != null)
				{
					foreach (var rid in relatedNoteIds.Where(id => id != Guid.Empty && id != note.Id))
					{
						targetRelatedIds.Add(rid);
					}
				}
				else if (relatedNoteId.HasValue)
				{
					if (relatedNoteId.Value != Guid.Empty && relatedNoteId.Value != note.Id)
					{
						targetRelatedIds.Add(relatedNoteId.Value);
					}
				}
				else if (synchronizeRelatedNote)
				{
					// Explicit synchronization requested with no related note specified -> clear existing links
				}
				else if (note.RelatedNoteIds != null && note.RelatedNoteIds.Count > 0)
				{
					foreach (var rid in note.RelatedNoteIds.Where(id => id != Guid.Empty && id != note.Id))
					{
						targetRelatedIds.Add(rid);
					}
				}

				if (note.IsLoot)
				{
					targetRelatedIds.Clear();
				}

				if (synchronizeRelatedNote && !isNew)
				{
					if (targetRelatedIds.Count == 0)
					{
						await using NpgsqlCommand unlinkAllCommand = new NpgsqlCommand("delete from core.note_links where source_note_id = @note_id or related_note_id = @note_id", connection, transaction);
						unlinkAllCommand.Parameters.AddWithValue("note_id", note.Id);
						if (await unlinkAllCommand.ExecuteNonQueryAsync(cancellationToken) > 0)
						{
							await RepositoryBase.WriteAuditRecordAsync(connection, transaction, userId, "UNLINK_NOTES", "core", "note_links", note.Id, "إزالة روابط المذكرات المقابلة أثناء الحفظ", cancellationToken);
						}
					}
					else
					{
						await using NpgsqlCommand unlinkCommand = new NpgsqlCommand("delete from core.note_links where (source_note_id = @note_id or related_note_id = @note_id) and not (case when source_note_id = @note_id then related_note_id else source_note_id end = any(@keep_ids))", connection, transaction);
						unlinkCommand.Parameters.AddWithValue("note_id", note.Id);
						unlinkCommand.Parameters.Add("keep_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = targetRelatedIds.ToArray();
						if (await unlinkCommand.ExecuteNonQueryAsync(cancellationToken) > 0)
						{
							await RepositoryBase.WriteAuditRecordAsync(connection, transaction, userId, "UNLINK_NOTES", "core", "note_links", note.Id, "تحديث روابط المذكرات المقابلة أثناء الحفظ", cancellationToken);
						}
					}
				}

				foreach (var targetId in targetRelatedIds)
				{
					try
					{
						await using NpgsqlCommand linkCommand = new NpgsqlCommand("insert into core.note_links(source_note_id, related_note_id, created_by)\r\nselect @note_id, @related_note_id, @user_id\r\nwhere not exists (\r\n    select 1 from core.note_links existing\r\n    where (existing.source_note_id = @note_id and existing.related_note_id = @related_note_id)\r\n       or (existing.source_note_id = @related_note_id and existing.related_note_id = @note_id)\r\n)", connection, transaction);
						linkCommand.Parameters.AddWithValue("note_id", note.Id);
						linkCommand.Parameters.AddWithValue("related_note_id", targetId);
						linkCommand.Parameters.AddWithValue("user_id", userId);
						if (await linkCommand.ExecuteNonQueryAsync(cancellationToken) == 1)
						{
							await RepositoryBase.WriteAuditRecordAsync(connection, transaction, userId, "LINK_NOTES", "core", "note_links", note.Id, "ربط المذكرة " + note.NoteNumber + " بمذكرة مقابلة أثناء الحفظ", cancellationToken);
						}
					}
					catch (PostgresException ex) when (ex.SqlState is "23503" or "23505" or "23514")
					{
						throw CreateLinkValidationException(ex);
					}
				}

				if (await HasNoteSourceContractAsync(connection, cancellationToken))
				{
					if (note.IsLoot)
					{
						Guid srcId;
						await using (NpgsqlCommand lootSourceCmd = new NpgsqlCommand("""
							insert into core.note_sources(note_id, source_type, other_description, created_by)
							values (@note_id, 'LOOT', 'غنائم ومستوردات', @user_id)
							on conflict (note_id) do update set source_type = 'LOOT', other_description = 'غنائم ومستوردات', updated_at = now()
							returning id;
							""", connection, transaction))
						{
							lootSourceCmd.Parameters.AddWithValue("note_id", note.Id);
							lootSourceCmd.Parameters.AddWithValue("user_id", userId);
							srcId = (Guid)(await lootSourceCmd.ExecuteScalarAsync(cancellationToken))!;
						}
						await using (NpgsqlCommand lootDetailCmd = new NpgsqlCommand("""
							delete from core.note_source_exit_refs where source_id = @source_id;
							delete from core.note_source_invoices where source_id = @source_id;
							insert into core.note_source_loot(source_id, description)
							values (@source_id, 'غنائم ومستوردات')
							on conflict (source_id) do update set description = 'غنائم ومستوردات';
							""", connection, transaction))
						{
							lootDetailCmd.Parameters.AddWithValue("source_id", srcId);
							await lootDetailCmd.ExecuteNonQueryAsync(cancellationToken);
						}
					}
					else if (!isNew)
					{
						await using NpgsqlCommand cleanLootCmd = new NpgsqlCommand("""
							delete from core.note_source_loot
							where source_id in (select id from core.note_sources where note_id = @note_id and source_type = 'LOOT');
							delete from core.note_sources where note_id = @note_id and source_type = 'LOOT';
							""", connection, transaction);
						cleanLootCmd.Parameters.AddWithValue("note_id", note.Id);
						await cleanLootCmd.ExecuteNonQueryAsync(cancellationToken);
					}
				}
				await transaction.CommitAsync(cancellationToken);
				id = note.Id;
			}
			result = id;
		}
		return result;
	}

	public async Task DeleteNoteAsync(Guid id, Guid userId, DateTime expectedUpdatedAt, CancellationToken cancellationToken = default(CancellationToken))
	{
		_modeService.EnsureWritable();
		NoteModel note = (await GetNoteInternalAsync(id, cancellationToken)) ?? throw new InvalidOperationException("المذكرة غير موجودة أو محذوفة.");
		await RepositoryBase.EnsureRoleAsync(base.Factory, userId, new[] { UserRole.ADMIN }, cancellationToken);
		await using NpgsqlConnection connection = base.Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		string deletedByName;
		await using (var identityCommand = new NpgsqlCommand("select coalesce(nullif(full_name, ''), username) from auth.users where id = @user_id", connection, transaction))
		{
			identityCommand.Parameters.AddWithValue("user_id", userId);
			deletedByName = (await identityCommand.ExecuteScalarAsync(cancellationToken) as string) ?? userId.ToString();
		}
		await using (NpgsqlCommand unlinkCommand = new NpgsqlCommand("delete from core.note_links where source_note_id = @id or related_note_id = @id", connection, transaction))
		{
			unlinkCommand.Parameters.AddWithValue("id", id);
			int removed = await unlinkCommand.ExecuteNonQueryAsync(cancellationToken);
			if (removed > 0)
			{
				await RepositoryBase.WriteAuditRecordAsync(connection, transaction, userId, "DELETE_LINK", "core", "note_links", id, $"إزالة {removed} رابط قبل حذف المذكرة", cancellationToken);
			}
		}
		await using (NpgsqlCommand command = new NpgsqlCommand("update core.notes\r\nset is_deleted = true, deleted_at = now(), deleted_by = @user_id, updated_by = @user_id\r\nwhere id = @id\r\n  and not is_deleted\r\n  and updated_at = @expected_updated_at", connection, transaction))
		{
			command.Parameters.AddWithValue("id", id);
			command.Parameters.AddWithValue("user_id", userId);
			command.Parameters.Add("expected_updated_at", NpgsqlDbType.Timestamp).Value = expectedUpdatedAt;
			if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
			{
				throw new InvalidOperationException("تعذر الحذف لأن حالة المذكرة تغي\u0651رت. أعد تحميلها ثم حاول مجدد\u064bا.");
			}
		}
		var deleteMetadata = JsonSerializer.Serialize(new
		{
			action = "حذف مذكرة",
			noteNumber = note.NoteNumber,
			company = note.CompanyName,
			arrivalAt = note.ArrivalAt,
			deletedBy = deletedByName,
			deletedByUserId = userId,
			deletedAt = DateTime.UtcNow
		});
		await WriteStructuredAuditRecordAsync(connection, transaction, userId, "DELETE_NOTE", "core", "notes", id, "حذف منطقي لمذكرة من مدير النظام", deleteMetadata, cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task ApproveNoteAsync(Guid id, Guid userId, DateTime? expectedUpdatedAt = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		_modeService.EnsureWritable();
		NoteModel note = (await GetNoteInternalAsync(id, cancellationToken)) ?? throw new InvalidOperationException("المذكرة غير موجودة أو محذوفة.");
		if (note.CreatedBy == userId)
		{
			throw new InvalidOperationException("لا يمكن اعتماد المذكرة من المستخدم الذي أدخلها.");
		}
		if (note.Status != NoteStatus.UNDER_REVIEW || note.ReviewerId != userId)
		{
			throw new InvalidOperationException("يجب أن تكون المذكرة محجوزة للتدقيق من قبلك قبل اعتمادها.");
		}
		List<NoteTypeFieldModel> fields = (await _noteTypeService.GetFieldsForTypeAsync(note.NoteTypeId, cancellationToken)).Where((NoteTypeFieldModel field) => field.IsActive).ToList();
		NoteValidator.Validate(note, fields);
		await using NpgsqlConnection connection = base.Factory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		await EnsureReviewerAsync(connection, userId, cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await using NpgsqlCommand command = new NpgsqlCommand("update core.notes\r\nset status = 'APPROVED', approved_by = @user_id, approved_at = now(), updated_by = @user_id,\r\n    reviewer_id = null, return_reason = null\r\nwhere id = @id and is_deleted = false\r\n  and status = 'UNDER_REVIEW' and reviewer_id = @user_id", connection, transaction);
		command.CommandText += "\r\n  and (@expected_updated_at is null or updated_at = @expected_updated_at)";
		command.Parameters.AddWithValue("id", id);
		command.Parameters.AddWithValue("user_id", userId);
		command.Parameters.Add("expected_updated_at", NpgsqlDbType.Timestamp).Value = (object?)expectedUpdatedAt ?? DBNull.Value;
		if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
		{
			throw new InvalidOperationException("تعذر اعتماد المذكرة لأن حالة التدقيق تغيرت.");
		}
		await WriteReviewHistoryAsync(connection, transaction, id, "APPROVE", null, userId, cancellationToken);
		await RepositoryBase.WriteAuditRecordAsync(connection, transaction, userId, "APPROVE_NOTE", "core", "notes", id, "اعتماد مذكرة", cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	private static async Task EnsureReviewerAsync(NpgsqlConnection connection, Guid userId, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = new NpgsqlCommand("select r.code\r\nfrom auth.users u\r\njoin auth.roles r on r.id = u.role_id\r\nwhere u.id = @user_id and u.is_active and not u.is_deleted and r.is_active", connection);
		command.Parameters.AddWithValue("user_id", userId);
		string role = (await command.ExecuteScalarAsync(cancellationToken)) as string;
		string text = role;
		if ((!(text == "REVIEWER") && !(text == "ADMIN")) || 1 == 0)
		{
			throw new InvalidOperationException("هذه العملية متاحة للمدقق أو مدير النظام فقط.");
		}
	}

	private static async Task WriteReviewHistoryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid noteId, string action, string? reason, Guid userId, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = new NpgsqlCommand("insert into core.note_review_history(note_id, action, reason, user_id)\r\nvalues (@note_id, @action, @reason, @user_id)", connection, transaction);
		command.Parameters.AddWithValue("note_id", noteId);
		command.Parameters.AddWithValue("action", action);
		command.Parameters.AddWithValue("reason", ((object)reason) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("user_id", userId);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}
}

public sealed class UserService : RepositoryBase, IUserService
{
    private readonly ISystemModeService _modeService;

    public UserService(
        DbConnectionFactory factory,
        ISystemModeService modeService,
        IAuditService? auditService = null) : base(factory)
    {
        _modeService = modeService;
    }

    public async Task<IReadOnlyList<UserModel>> GetUsersAsync(
        Guid requesterId,
        CancellationToken cancellationToken = default)
    {
        await EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN], cancellationToken);
        var users = new List<UserModel>();

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select u.id, u.username, u.full_name, u.role_id, r.code, r.name_ar, u.is_active
            from auth.users u
            join auth.roles r on r.id = u.role_id
            where not u.is_deleted
            order by u.username
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new UserModel
            {
                Id = reader.GetGuid(0),
                Username = reader.GetString(1),
                FullName = reader.GetString(2),
                RoleId = reader.GetGuid(3),
                RoleCode = reader.GetString(4),
                RoleNameAr = reader.GetString(5),
                IsActive = reader.GetBoolean(6)
            });
        }

        return users;
    }

    public async Task<IReadOnlyList<RoleModel>> GetActiveRolesAsync(
        Guid requesterId,
        CancellationToken cancellationToken = default)
    {
        await EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN], cancellationToken);
        var roles = new List<RoleModel>();

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, code, name_ar, is_active
            from auth.roles
            where is_active
            order by name_ar
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(new RoleModel
            {
                Id = reader.GetGuid(0),
                Code = reader.GetString(1),
                NameAr = reader.GetString(2),
                IsActive = reader.GetBoolean(3)
            });
        }

        return roles;
    }

    public async Task SaveUserAsync(
        UserModel user,
        string? newPassword,
        Guid requesterId,
        CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN], cancellationToken);

        if (string.IsNullOrWhiteSpace(user.Username)
            || string.IsNullOrWhiteSpace(user.FullName)
            || user.RoleId == Guid.Empty)
        {
            throw new InvalidOperationException("اسم المستخدم والاسم الكامل والدور مطلوبة.");
        }

        var isNew = user.Id == Guid.Empty;
        if (isNew && string.IsNullOrWhiteSpace(newPassword))
        {
            throw new InvalidOperationException("كلمة المرور مطلوبة عند إنشاء مستخدم جديد.");
        }

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            PasswordPolicy.EnsureValid(newPassword);
        }

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Serializes all operations which could remove the final active administrator.
        await using (var guard = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtext('audit_memo.active_admin_guard'))",
            connection,
            transaction))
        {
            await guard.ExecuteNonQueryAsync(cancellationToken);
        }

        string newRoleCode;
        await using (var roleCommand = new NpgsqlCommand(
            "select code from auth.roles where id = @id and is_active",
            connection,
            transaction))
        {
            roleCommand.Parameters.AddWithValue("id", user.RoleId);
            newRoleCode = Convert.ToString(await roleCommand.ExecuteScalarAsync(cancellationToken))
                ?? throw new InvalidOperationException("الدور المحدد غير موجود أو غير فعال.");
        }

        string passwordHash;
        string? existingRoleCode = null;
        if (isNew)
        {
            user.Id = Guid.NewGuid();
            passwordHash = BCrypt.Net.BCrypt.HashPassword(newPassword!);
        }
        else
        {
            await using var existingCommand = new NpgsqlCommand("""
                select u.password_hash, r.code
                from auth.users u
                join auth.roles r on r.id = u.role_id
                where u.id = @id and not u.is_deleted
                for update of u
                """, connection, transaction);
            existingCommand.Parameters.AddWithValue("id", user.Id);
            await using var reader = await existingCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("المستخدم غير موجود.");
            }

            passwordHash = reader.GetString(0);
            existingRoleCode = reader.GetString(1);
            await reader.CloseAsync();

            var removesAdministrator = existingRoleCode == "ADMIN"
                && (!user.IsActive || newRoleCode != "ADMIN");
            if (user.Id == requesterId && removesAdministrator)
            {
                throw new InvalidOperationException("لا يمكن تعطيل المدير الحالي أو إزالة دور الإدارة من جلسته.");
            }

            if (removesAdministrator)
            {
                await using var countCommand = new NpgsqlCommand("""
                    select count(*)
                    from auth.users u
                    join auth.roles r on r.id = u.role_id
                    where r.code = 'ADMIN' and u.is_active and not u.is_deleted
                    """, connection, transaction);
                var activeAdministratorCount = Convert.ToInt64(
                    await countCommand.ExecuteScalarAsync(cancellationToken));
                if (activeAdministratorCount <= 1)
                {
                    throw new InvalidOperationException("لا يمكن تعطيل أو تغيير دور آخر مدير نشط.");
                }
            }

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                passwordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            }
        }

        try
        {
            await using var saveCommand = new NpgsqlCommand("""
                insert into auth.users(id, username, password_hash, full_name, role_id, is_active, must_change_password)
                values(@id, @username, @hash, @full_name, @role_id, @is_active, @must_change_password)
                on conflict(id) do update set
                    username = excluded.username,
                    full_name = excluded.full_name,
                    role_id = excluded.role_id,
                    is_active = excluded.is_active,
                    password_hash = excluded.password_hash,
                    updated_at = now()
                """, connection, transaction);
            saveCommand.Parameters.AddWithValue("id", user.Id);
            saveCommand.Parameters.AddWithValue("username", user.Username.Trim().ToLowerInvariant());
            saveCommand.Parameters.AddWithValue("hash", passwordHash);
            saveCommand.Parameters.AddWithValue("full_name", user.FullName.Trim());
            saveCommand.Parameters.AddWithValue("role_id", user.RoleId);
            saveCommand.Parameters.AddWithValue("is_active", user.IsActive);
            saveCommand.Parameters.AddWithValue("must_change_password", isNew);
            await saveCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            throw new InvalidOperationException("اسم المستخدم مستخدم بالفعل.");
        }

        await WriteAuditRecordAsync(
            connection,
            transaction,
            requesterId,
            isNew ? "CREATE_USER" : "UPDATE_USER",
            "auth",
            "users",
            user.Id,
            $"حفظ المستخدم: {user.Username.Trim()}",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResetUserPasswordAsync(
        Guid userId,
        string newPassword,
        Guid requesterId,
        CancellationToken cancellationToken = default)
    {
        _modeService.EnsureWritable();
        await EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN], cancellationToken);
        PasswordPolicy.EnsureValid(newPassword);

        await using var connection = Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update auth.users
            set password_hash = @hash,
                must_change_password = true,
                updated_at = now()
            where id = @id and not is_deleted
            """, connection, transaction);
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("hash", BCrypt.Net.BCrypt.HashPassword(newPassword));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("المستخدم غير موجود.");
        }

        await WriteAuditRecordAsync(
            connection,
            transaction,
            requesterId,
            "RESET_USER_PASSWORD",
            "auth",
            "users",
            userId,
            "إعادة تعيين كلمة المرور",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
} // End UserService.

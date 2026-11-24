using System.Net;
using AuditMemoSystem.Core.Models;
using Npgsql;

namespace AuditMemoSystem.Data.Connection;

public sealed class DbConnectionFactory
{
    private readonly AppConfig _config;
    private readonly Func<AppConfig>? _configProvider;

    public DbConnectionFactory(AppConfig config)
    {
        _config = config;
    }

    public DbConnectionFactory(Func<AppConfig> configProvider)
    {
        _configProvider = configProvider;
        _config = new AppConfig();
    }

    private AppConfig CurrentConfig => _configProvider?.Invoke() ?? _config;

    public ConnectionProfileModel ActiveProfile =>
        CurrentConfig.Database.Profiles.FirstOrDefault(p => p.Name.Equals(CurrentConfig.Database.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        ?? CurrentConfig.Database.Profiles.FirstOrDefault()
        ?? new ConnectionProfileModel();

    public string CreateConnectionString(ConnectionProfileModel? profile = null)
    {
        profile ??= ActiveProfile;
        return CreateConnectionString(profile, profile.Database);
    }

    private static string CreateConnectionString(ConnectionProfileModel profile, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = profile.Host,
            Port = profile.Port,
            Database = database,
            Username = profile.Username,
            Password = profile.Password,
            Pooling = true,
            IncludeErrorDetail = false,
            Timeout = 10,
            CommandTimeout = 30
        };

        // الوضع الافتراضي شبكة داخلية موثوقة بلا تشفير. التشفير opt-in لكل ملف
        // اتصال عبر خاصية الملف (SslMode=Require) ولا يغير سلوك الملفات القديمة.
        // ملاحظة: Npgsql 8 تجاهل TrustServerCertificate، لذا التحقق يتم عبر مخزن
        // شهادات النظام — الشهادة الذاتية تحتاج تثبيت CA فيه مسبقاً.
        builder.SslMode = ResolveSslMode(profile.SslMode);

        return builder.ConnectionString;
    }

    /// <summary>يقبل "Disable" و"Require" فقط؛ أي قيمة أخرى تعود إلى Disable بأمان.</summary>
    public static SslMode ResolveSslMode(string? value)
    {
        if (string.Equals(value?.Trim(), "Require", StringComparison.OrdinalIgnoreCase))
        {
            return SslMode.Require;
        }

        return SslMode.Disable;
    }


    public NpgsqlConnection CreateConnection(ConnectionProfileModel? profile = null)
    {
        return new NpgsqlConnection(CreateConnectionString(profile));
    }

    public NpgsqlConnection CreateMaintenanceConnection(ConnectionProfileModel profile)
    {
        return new NpgsqlConnection(CreateConnectionString(profile, "postgres"));
    }

    public async Task<bool> TestConnectionAsync(ConnectionProfileModel? profile = null, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select 1", connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    public async Task TestServerConnectionAsync(ConnectionProfileModel profile, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateMaintenanceConnection(profile);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<bool> DatabaseExistsAsync(ConnectionProfileModel profile, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateMaintenanceConnection(profile);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select exists(select 1 from pg_database where datname = @database)", connection);
        command.Parameters.AddWithValue("database", profile.Database);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> EnsureDatabaseExistsAsync(ConnectionProfileModel profile, CancellationToken cancellationToken = default)
    {
        if (await DatabaseExistsAsync(profile, cancellationToken))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(profile.Database) || profile.Database.Contains('\0'))
        {
            throw new InvalidOperationException("اسم قاعدة البيانات غير صالح.");
        }

        var quotedDatabase = $"\"{profile.Database.Replace("\"", "\"\"")}\"";
        await using var connection = CreateMaintenanceConnection(profile);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"create database {quotedDatabase}", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }
}

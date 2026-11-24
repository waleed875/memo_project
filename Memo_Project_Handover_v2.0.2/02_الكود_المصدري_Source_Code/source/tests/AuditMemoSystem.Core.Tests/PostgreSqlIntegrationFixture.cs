using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Database;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[CollectionDefinition(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class PostgreSqlIntegrationCollection : ICollectionFixture<PostgreSqlIntegrationFixture>
{
}

public sealed class PostgreSqlIntegrationFixture : IAsyncLifetime
{
    public const string CollectionName = "PostgreSQL integration database";
    private const string DefaultDatabaseName = "audit_memo_integration";
    private static string? _databaseName;

    public static string DatabaseName => _databaseName ??= CreateRunDatabaseName();

    private static string CreateRunDatabaseName()
    {
        var configuredPrefix = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_DATABASE")?.Trim();
        var prefix = string.IsNullOrWhiteSpace(configuredPrefix) ? DefaultDatabaseName : configuredPrefix;
        if (!prefix.StartsWith(DefaultDatabaseName, StringComparison.OrdinalIgnoreCase)
            || prefix.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException("AUDIT_MEMO_INTEGRATION_DATABASE must be a safe prefix beginning with 'audit_memo_integration'.");
        }

        prefix = prefix.TrimEnd('_');
        if (prefix.Length > 30) prefix = prefix[..30].TrimEnd('_');
        return string.Concat(
            prefix,
            "_",
            DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            "_",
            Environment.ProcessId,
            "_",
            Guid.NewGuid().ToString("N")[..8]);
    }

    public static ConnectionProfileModel CreateProfile(
        string profileName = "PostgreSqlIntegration",
        string? passwordOverride = null,
        string? databaseOverride = null)
    {
        var database = string.IsNullOrWhiteSpace(databaseOverride) ? DatabaseName : databaseOverride.Trim();
        if (!database.StartsWith("audit_memo_integration", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "PostgreSQL integration databases must begin with 'audit_memo_integration' to prevent using an application database.");
        }

        var password = string.IsNullOrWhiteSpace(passwordOverride)
            ? Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD")
            : passwordOverride;
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run PostgreSQL integration tests.");
        }

        return new ConnectionProfileModel
        {
            Name = profileName,
            Type = ConnectionProfileType.SERVER,
            Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres",
            Password = password
        };
    }

    public static DbConnectionFactory CreateFactory(
        string profileName = "PostgreSqlIntegration",
        string? passwordOverride = null,
        string? databaseOverride = null)
    {
        var profile = CreateProfile(profileName, passwordOverride, databaseOverride);
        return new DbConnectionFactory(new AppConfig
        {
            Database = new DatabaseSection { ActiveProfile = profile.Name, Profiles = [profile] }
        });
    }

    public async Task InitializeAsync()
    {
        var profile = CreateProfile();
        var factory = CreateFactory();

        await factory.EnsureDatabaseExistsAsync(profile);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        await new MigrationRunner(factory, Path.Combine(root, "database")).RunAsync();

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            insert into core.companies(name, is_active)
            select 'QA Integration Seed Company', true
            where not exists (
                select 1 from core.companies where name = 'QA Integration Seed Company' and not is_deleted
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD")))
        {
            return;
        }

        var profile = CreateProfile();
        var factory = CreateFactory();
        await using var connection = factory.CreateMaintenanceConnection(profile);
        await connection.OpenAsync();

        var quotedDatabase = "\"" + DatabaseName.Replace("\"", "\"\"") + "\"";
        await using var command = new NpgsqlCommand(
            "drop database if exists " + quotedDatabase + " with (force);",
            connection);
        await command.ExecuteNonQueryAsync();
    }
}

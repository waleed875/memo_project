using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Database;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class MigrationRunnerSupportedRangeIntegrationTests
{
    [Fact]
    public async Task CloneAt014UpgradesToLatestAndLatestRerunIsNoOp()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run PostgreSQL integration tests.");

        var prefix = "audit_memo_integration_014_to_latest_";
        var profile = new ConnectionProfileModel
        {
            Name = "MigrationRunnerSupportedRange",
            Type = ConnectionProfileType.SERVER,
            Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var port) ? port : 5432,
            Database = (prefix + Guid.NewGuid().ToString("N"))[..55],
            Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres",
            Password = password
        };
        var factory = new DbConnectionFactory(new AppConfig
        {
            Database = new DatabaseSection { ActiveProfile = profile.Name, Profiles = [profile] }
        });
        var repositoryDatabase = FindRepositoryDirectory("database");
        var latestVersion = Directory.GetFiles(repositoryDatabase, "*.sql")
            .Select(path => Path.GetFileName(path)[..3])
            .OrderBy(version => version, StringComparer.Ordinal)
            .Last();
        var stage014 = Path.Combine(Path.GetTempPath(), "AuditMemoSystem", "migration-runner-014", Guid.NewGuid().ToString("N"));
        var fullLatest = Path.Combine(Path.GetTempPath(), "AuditMemoSystem", "migration-runner-latest", Guid.NewGuid().ToString("N"));

        await factory.EnsureDatabaseExistsAsync(profile);
        Directory.CreateDirectory(stage014);
        Directory.CreateDirectory(fullLatest);
        try
        {
            foreach (var source in Directory.GetFiles(repositoryDatabase, "*.sql")
                         .Where(path => int.Parse(Path.GetFileName(path)[..3]) <= 14))
            {
                File.Copy(source, Path.Combine(stage014, Path.GetFileName(source)));
            }
            foreach (var source in Directory.GetFiles(repositoryDatabase, "*.sql"))
            {
                File.Copy(source, Path.Combine(fullLatest, Path.GetFileName(source)));
            }

            var runner014 = new MigrationRunner(factory, stage014);
            await runner014.RunAsync();
            Assert.Equal("014", (await runner014.GetVersionStatusAsync()).DatabaseVersion);

            var runnerLatest = new MigrationRunner(factory, fullLatest);
            await runnerLatest.RunAsync();
            var statusLatest = await runnerLatest.GetVersionStatusAsync();
            Assert.Equal(latestVersion, statusLatest.DatabaseVersion);
            Assert.False(statusLatest.RequiresUpgrade);

            var before = await ReadSchemaMigrationSnapshotAsync(factory);
            await runnerLatest.RunAsync();
            var after = await ReadSchemaMigrationSnapshotAsync(factory);
            Assert.Equal(before, after);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await DropDisposableDatabaseAsync(factory, profile, prefix);
            if (Directory.Exists(stage014)) Directory.Delete(stage014, recursive: true);
            if (Directory.Exists(fullLatest)) Directory.Delete(fullLatest, recursive: true);
        }
    }

    private static async Task<(long Count, string MaxAppliedAt)> ReadSchemaMigrationSnapshotAsync(DbConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select count(*), coalesce(max(applied_at)::text, '') from system.schema_migrations", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetString(1));
    }

    private static string FindRepositoryDirectory(string directoryName)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, directoryName);
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException($"تعذر تحديد مجلد {directoryName} للاختبار.");
    }

    private static async Task DropDisposableDatabaseAsync(DbConnectionFactory factory, ConnectionProfileModel profile, string prefix)
    {
        if (!profile.Database.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the supported-range migration test database may be removed.");

        await using var connection = factory.CreateMaintenanceConnection(profile);
        await connection.OpenAsync();
        await using (var terminate = new NpgsqlCommand("select pg_terminate_backend(pid) from pg_stat_activity where datname = @database and pid <> pg_backend_pid()", connection))
        {
            terminate.Parameters.AddWithValue("database", profile.Database);
            await terminate.ExecuteNonQueryAsync();
        }
        var quotedDatabase = $"\"{profile.Database.Replace("\"", "\"\"")}\"";
        await using var drop = new NpgsqlCommand($"drop database if exists {quotedDatabase}", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
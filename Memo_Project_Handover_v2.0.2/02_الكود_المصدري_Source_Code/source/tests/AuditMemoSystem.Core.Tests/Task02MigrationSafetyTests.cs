using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Database;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class Task02MigrationSafetyTests
{
    [Fact]
    public void MinimumSupportedDatabaseVersionIsExplicitAndPre015LoginHasACompatibleFallback()
    {
        Assert.Equal("014", MigrationSupportPolicy.MinimumSupportedDatabaseVersion);
        Assert.True(new MigrationVersionStatus("013", "060", true).IsBelowMinimumSupported);
        Assert.False(new MigrationVersionStatus("014", "060", true).IsBelowMinimumSupported);

        var source = File.ReadAllText(FindRepositoryFile("src/AuditMemoSystem.Data/Repositories/DataServices.cs"));
        Assert.Contains("HasMustChangePasswordColumnAsync", source, StringComparison.Ordinal);
        Assert.Contains("false as must_change_password", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationScriptsPreservePublishedRangeWhenAdditive060Through064Exist()
    {
        var databaseDirectory = FindRepositoryDirectory("database");
        var scripts = Directory.GetFiles(databaseDirectory, "*.sql")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Select(name => int.Parse(name![..3]))
            .ToArray();

        Assert.Equal(59, scripts.Where(version => version <= 59).Max());
        Assert.Contains(59, scripts);
        Assert.Contains(60, scripts);
        Assert.Contains(61, scripts);
        Assert.Contains(62, scripts);
        Assert.Contains(63, scripts);
        Assert.Contains(64, scripts);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var root = FindRepositoryDirectory("database");
        return Path.Combine(root, "..", relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FindRepositoryDirectory(string directoryName)
    {
        var candidates = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            .Concat(Directory.GetParent(AppContext.BaseDirectory)?.FullName is { } parent ? [parent] : []);
        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                var match = Path.Combine(directory.FullName, directoryName);
                if (Directory.Exists(match)) return match;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException($"تعذر تحديد مجلد {directoryName} للاختبار.");
    }
}

public sealed class MigrationRunnerPostCommitRecoveryIntegrationTests
{
    [Fact]
    public async Task HistoryFailureAfterCommitIsReconciledWithoutReapplyingSql()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run PostgreSQL integration tests.");

        var prefix = "audit_memo_integration_post_commit_";
        var profile = new ConnectionProfileModel
        {
            Name = "MigrationRunnerPostCommit",
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
        var scriptsDirectory = Path.Combine(Path.GetTempPath(), "AuditMemoSystem", "migration-runner-post-commit", Guid.NewGuid().ToString("N"));

        await factory.EnsureDatabaseExistsAsync(profile);
        Directory.CreateDirectory(scriptsDirectory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(scriptsDirectory, "001_create_once.sql"), """
                create table system.database_migration_history (
                    migration_version varchar(30) primary key,
                    previous_version varchar(30),
                    new_version varchar(30) not null,
                    started_at timestamp not null,
                    completed_at timestamp,
                    status varchar(20) not null,
                    description text not null,
                    error_details text);
                create table qa_commit_once (id integer primary key);
                """);
            await File.WriteAllTextAsync(Path.Combine(scriptsDirectory, "002_create_twice.sql"), "create table qa_commit_twice (id integer primary key);");

            var failOnce = 1;
            var runner = new MigrationRunner(
                factory,
                scriptsDirectory,
                (version, _) =>
                {
                    if (version == "001" && Interlocked.Exchange(ref failOnce, 0) == 1)
                        throw new InvalidOperationException("injected history write failure");
                    return Task.CompletedTask;
                });

            await Assert.ThrowsAsync<MigrationHistoryPendingException>(() => runner.RunAsync());
            Assert.Equal("001", await ReadScalarAsync(factory, "select version from system.schema_migrations order by version::int desc limit 1"));
            Assert.True(await TableExistsAsync(factory, "qa_commit_once"));

            var recoveryRunner = new MigrationRunner(factory, scriptsDirectory);
            await recoveryRunner.RunAsync();
            await recoveryRunner.RunAsync();

            Assert.Equal("002", await ReadScalarAsync(factory, "select version from system.schema_migrations order by version::int desc limit 1"));
            Assert.Equal("SUCCESS", await ReadScalarAsync(factory, "select status from system.database_migration_history where migration_version = '001'"));
            Assert.True(await TableExistsAsync(factory, "qa_commit_twice"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await DropDisposableDatabaseAsync(factory, profile, prefix);
            if (Directory.Exists(scriptsDirectory)) Directory.Delete(scriptsDirectory, recursive: true);
        }
    }

    private static async Task<string> ReadScalarAsync(DbConnectionFactory factory, string sql)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static async Task<bool> TableExistsAsync(DbConnectionFactory factory, string tableName)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select to_regclass(@table_name) is not null", connection);
        command.Parameters.AddWithValue("table_name", tableName);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task DropDisposableDatabaseAsync(DbConnectionFactory factory, ConnectionProfileModel profile, string prefix)
    {
        if (!profile.Database.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the post-commit migration test database may be removed.");

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

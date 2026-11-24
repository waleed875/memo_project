using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Database;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class MigrationRunnerLifecycleIntegrationTests
{
    [Fact]
    public async Task Runner_AppliesPendingScriptsOnce_RecordsFailures_AndPreservesExistingData()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run PostgreSQL integration tests.");
        }

        var profile = new ConnectionProfileModel
        {
            Name = "MigrationRunnerLifecycle",
            Type = ConnectionProfileType.SERVER,
            Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var port) ? port : 5432,
            // PostgreSQL identifiers are limited to 63 bytes; keep the disposable name below that limit.
            Database = $"audit_memo_integration_migrations_{Guid.NewGuid().ToString("N")[..20]}",
            Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres",
            Password = password
        };
        var factory = new DbConnectionFactory(new AppConfig
        {
            Database = new DatabaseSection { ActiveProfile = profile.Name, Profiles = [profile] }
        });
        var scriptsDirectory = Path.Combine(Path.GetTempPath(), "AuditMemoSystem", "migration-runner-tests", Guid.NewGuid().ToString("N"));

        await factory.EnsureDatabaseExistsAsync(profile);
        Directory.CreateDirectory(scriptsDirectory);
        try
        {
            await WriteScriptAsync(scriptsDirectory, "001_create_history.sql", """
                create table system.database_migration_history (
                    migration_version varchar(30) primary key,
                    previous_version varchar(30), new_version varchar(30) not null,
                    started_at timestamp not null, completed_at timestamp,
                    status varchar(20) not null, description text not null, error_details text);
                create table qa_preserved_data (id integer primary key, value text not null);
                insert into qa_preserved_data(id, value) values (1, 'kept');
                """);
            var runner = new MigrationRunner(factory, scriptsDirectory);
            await runner.RunAsync();
            Assert.Equal("001", (await runner.GetVersionStatusAsync()).DatabaseVersion);

            await WriteScriptAsync(scriptsDirectory, "002_add_stage_two.sql", "create table qa_stage_two (id integer primary key);");
            await runner.RunAsync();
            Assert.Equal("002", (await runner.GetVersionStatusAsync()).DatabaseVersion);

            await WriteScriptAsync(scriptsDirectory, "003_add_stage_three.sql", "create table qa_stage_three (id integer primary key);");
            await WriteScriptAsync(scriptsDirectory, "004_add_stage_four.sql", "create table qa_stage_four (id integer primary key);");
            await runner.RunAsync();
            Assert.Equal("004", (await runner.GetVersionStatusAsync()).DatabaseVersion);

            await AssertDataAndHistoryAsync(factory, "004", "SUCCESS", expectedRows: 1);

            await WriteScriptAsync(scriptsDirectory, "005_fail_mid_upgrade.sql", "select 1 / 0;");
            var migrationFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());
            Assert.IsType<PostgresException>(migrationFailure.InnerException);
            Assert.Equal("004", (await runner.GetVersionStatusAsync()).DatabaseVersion);
            await AssertDataAndHistoryAsync(factory, "005", "FAILED", expectedRows: 1);

            await WriteScriptAsync(scriptsDirectory, "005_fail_mid_upgrade.sql", "create table qa_stage_five (id integer primary key);");
            await runner.RunAsync();
            await runner.RunAsync();
            Assert.Equal("005", (await runner.GetVersionStatusAsync()).DatabaseVersion);
            await AssertDataAndHistoryAsync(factory, "005", "SUCCESS", expectedRows: 1);

            await CreateEmptyVersionSettingsAsync(factory);
            await runner.RunAsync();
            Assert.Equal("005", await ReadTrackedVersionAsync(factory));

            var olderApplicationScripts = Path.Combine(Path.GetTempPath(), "AuditMemoSystem", "migration-runner-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(olderApplicationScripts);
            try
            {
                for (var version = 1; version <= 4; version++)
                {
                    var source = Directory.GetFiles(scriptsDirectory, $"{version:000}_*.sql").Single();
                    File.Copy(source, Path.Combine(olderApplicationScripts, Path.GetFileName(source)));
                }

                var olderApplicationRunner = new MigrationRunner(factory, olderApplicationScripts);
                var status = await olderApplicationRunner.GetVersionStatusAsync();
                Assert.True(status.DatabaseIsNewer);
                Assert.False(status.RequiresUpgrade);
            }
            finally
            {
                Directory.Delete(olderApplicationScripts, recursive: true);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await DropDisposableDatabaseAsync(factory, profile);
            if (Directory.Exists(scriptsDirectory))
            {
                Directory.Delete(scriptsDirectory, recursive: true);
            }
        }
    }

    private static async Task WriteScriptAsync(string directory, string fileName, string sql) =>
        await File.WriteAllTextAsync(Path.Combine(directory, fileName), sql);

    private static async Task AssertDataAndHistoryAsync(DbConnectionFactory factory, string migrationVersion, string status, int expectedRows)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            select
                (select count(*) from qa_preserved_data),
                (select status from system.database_migration_history where migration_version = @version)
            """, connection);
        command.Parameters.AddWithValue("version", migrationVersion);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(expectedRows, reader.GetInt64(0));
        Assert.Equal(status, reader.GetString(1));
    }

    private static async Task CreateEmptyVersionSettingsAsync(DbConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            create table system.database_settings (
                singleton boolean primary key default true check (singleton),
                database_version varchar(30) not null,
                last_updated_at timestamp not null default now(),
                last_updated_by uuid
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadTrackedVersionAsync(DbConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select database_version from system.database_settings where singleton = true",
            connection);
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static async Task DropDisposableDatabaseAsync(DbConnectionFactory factory, ConnectionProfileModel profile)
    {
        if (!profile.Database.StartsWith("audit_memo_integration_migrations_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only the migration test disposable database may be removed.");
        }

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

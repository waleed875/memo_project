using System.Security.Cryptography;
using System.Text;
using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Database;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class MigrationRunnerLegacyCompatibilityIntegrationTests
{
    private const string Legacy052Checksum = "ac51119f54518a8e75ecb07ba7cf59d781000c81b3ff8c8c439ea5edadcf0c84";

    [Fact]
    public async Task Runner_RebaselinesKnownLegacy052AndContinuesTo053()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run PostgreSQL integration tests.");
        }

        var profile = new ConnectionProfileModel
        {
            Name = "MigrationRunnerLegacy052",
            Type = ConnectionProfileType.SERVER,
            Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var port) ? port : 5432,
            Database = $"audit_memo_integration_migrations_legacy052_{Guid.NewGuid():N}"[..55],
            Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres",
            Password = password
        };
        var factory = new DbConnectionFactory(new AppConfig
        {
            Database = new DatabaseSection { ActiveProfile = profile.Name, Profiles = [profile] }
        });
        var scriptsDirectory = Path.Combine(Path.GetTempPath(), "AuditMemoSystem", "migration-runner-legacy-tests", Guid.NewGuid().ToString("N"));

        await factory.EnsureDatabaseExistsAsync(profile);
        Directory.CreateDirectory(scriptsDirectory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(scriptsDirectory, "052_fix_company_metrics_duplicate_scope.sql"),
                "select 1;");
            await File.WriteAllTextAsync(
                Path.Combine(scriptsDirectory, "053_set_application_version_1031.sql"),
                "create table qa_after_legacy_052 (id integer primary key);");

            await using (var connection = factory.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("""
                    create schema system;
                    create schema reporting;
                    create table system.schema_migrations (
                        version varchar(30) primary key,
                        script_name text not null,
                        checksum varchar(64),
                        applied_at timestamp not null default now()
                    );
                    create or replace function reporting.fn_company_advanced_metrics(
                        p_company_id uuid,
                        p_from date,
                        p_to date
                    )
                    returns table(
                        row_kind text,
                        period_label text,
                        written_count int,
                        total_notes int,
                        on_time_notes int,
                        late_notes int,
                        duplicate_notes int,
                        entry_notes int,
                        unlinked_entry_notes int,
                        memo_items_efficiency_index numeric,
                        time_stability_index numeric,
                        recurrence_rate_index numeric,
                        mechanism_efficiency_index numeric,
                        overall_general_efficiency_index numeric
                    )
                    language sql
                    stable
                    as $function$
                    with duplicate_values as (
                        select count(coalesce(di.created_note_id, di.matched_note_id))::int as duplicate_notes
                        from (select null::uuid as created_note_id, null::uuid as matched_note_id) di
                    )
                    select 'metrics'::text, null::text, 0, 0, 0, 0, d.duplicate_notes, 0, 0,
                           0::numeric, 0::numeric, 0::numeric, 0::numeric, 0::numeric
                    from duplicate_values d;
                    $function$;
                    insert into system.schema_migrations(version, script_name, checksum)
                    values ('052', '052_fix_company_metrics_duplicate_scope.sql', @legacy_checksum);
                    """, connection);
                command.Parameters.AddWithValue("legacy_checksum", Legacy052Checksum);
                await command.ExecuteNonQueryAsync();
            }

            var runner = new MigrationRunner(factory, scriptsDirectory);
            await runner.RunAsync();

            var status = await runner.GetVersionStatusAsync();
            Assert.Equal("053", status.DatabaseVersion);
            Assert.Equal("053", await ReadScalarAsync(factory, "select version from system.schema_migrations order by version::int desc limit 1"));
            Assert.True(await TableExistsAsync(factory, "qa_after_legacy_052"));

            var expectedChecksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("select 1;"))).ToLowerInvariant();
            Assert.Equal(expectedChecksum, await ReadScalarAsync(factory, "select checksum from system.schema_migrations where version = '052'"));
            Assert.NotEqual(Legacy052Checksum, await ReadScalarAsync(factory, "select checksum from system.schema_migrations where version = '052'"));
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

    private static async Task DropDisposableDatabaseAsync(DbConnectionFactory factory, ConnectionProfileModel profile)
    {
        if (!profile.Database.StartsWith("audit_memo_integration_migrations_legacy052_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only the legacy 052 migration test database may be removed.");
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


using System.Diagnostics;
using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Database;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class MigrationRunnerBackupRestoreIntegrationTests
{
    [Fact]
    public async Task CloneAt059RestoreAndUpgradePreservesSentinelData()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run PostgreSQL integration tests.");

        var host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost";
        var port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var configuredPort) ? configuredPort : 5432;
        var username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres";
        var prefix = "audit_memo_integration_059_restore_";
        var sourceProfile = CreateProfile("source", prefix, host, port, username, password);
        var cloneProfile = CreateProfile("clone", prefix, host, port, username, password);
        var sourceFactory = CreateFactory(sourceProfile);
        var cloneFactory = CreateFactory(cloneProfile);
        var repositoryDatabase = FindRepositoryDirectory("database");
        var root = Path.Combine(Path.GetTempPath(), "AuditMemoSystem", "migration-runner-059-restore", Guid.NewGuid().ToString("N"));
        var stage059 = Path.Combine(root, "stage-059");
        var stageLatest = Path.Combine(root, "stage-latest");
        var backupFile = Path.Combine(root, "source-059.dump");

        await sourceFactory.EnsureDatabaseExistsAsync(sourceProfile);
        await cloneFactory.EnsureDatabaseExistsAsync(cloneProfile);
        Directory.CreateDirectory(stage059);
        Directory.CreateDirectory(stageLatest);
        try
        {
            foreach (var source in Directory.GetFiles(repositoryDatabase, "*.sql"))
            {
                var version = int.Parse(Path.GetFileName(source)[..3]);
                if (version <= 59)
                    File.Copy(source, Path.Combine(stage059, Path.GetFileName(source)));
                File.Copy(source, Path.Combine(stageLatest, Path.GetFileName(source)));
            }

            var runner059 = new MigrationRunner(sourceFactory, stage059);
            await runner059.RunAsync();
            Assert.Equal("059", (await runner059.GetVersionStatusAsync()).DatabaseVersion);

            var sentinel = await SeedSentinelDataAsync(sourceFactory);
            await RunPostgresToolAsync("pg_dump.exe", password, [
                "--host", host, "--port", port.ToString(), "--username", username,
                "--format", "custom", "--file", backupFile, sourceProfile.Database]);

            await RunPostgresToolAsync("pg_restore.exe", password, [
                "--host", host, "--port", port.ToString(), "--username", username,
                "--dbname", cloneProfile.Database, "--no-owner", "--no-privileges", backupFile]);

            Assert.Equal("059", await ReadDatabaseVersionAsync(cloneFactory));
            Assert.Equal(sentinel, await ReadSentinelDataAsync(cloneFactory));

            var runnerLatest = new MigrationRunner(cloneFactory, stageLatest);
            await runnerLatest.RunAsync();
            Assert.Equal("070", (await runnerLatest.GetVersionStatusAsync()).DatabaseVersion);
            Assert.Equal(sentinel, await ReadSentinelDataAsync(cloneFactory));

            var before = await ReadSchemaMigrationSnapshotAsync(cloneFactory);
            await runnerLatest.RunAsync();
            var after = await ReadSchemaMigrationSnapshotAsync(cloneFactory);
            Assert.Equal(before, after);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await DropDisposableDatabaseAsync(sourceFactory, sourceProfile, prefix);
            await DropDisposableDatabaseAsync(cloneFactory, cloneProfile, prefix);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ConnectionProfileModel CreateProfile(string suffix, string prefix, string host, int port, string username, string password) => new()
    {
        Name = $"MigrationRunnerBackupRestore-{suffix}",
        Type = ConnectionProfileType.SERVER,
        Host = host,
        Port = port,
        Database = (prefix + suffix + "_" + Guid.NewGuid().ToString("N"))[..55],
        Username = username,
        Password = password
    };

    private static DbConnectionFactory CreateFactory(ConnectionProfileModel profile) => new(new AppConfig
    {
        Database = new DatabaseSection { ActiveProfile = profile.Name, Profiles = [profile] }
    });

    private static async Task<(long Companies, long Users, long Notes, long Values, long AuditLogs, string CompanyName)> SeedSentinelDataAsync(DbConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var companyId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var companyName = "Acceptance Clone " + companyId.ToString("N");
        const string sql = """
            insert into core.companies(id, name) values (@company_id, @company_name);
            insert into core.notes(id, note_number, note_number_normalized, note_type_id, company_id, writing_date, status, created_by)
            select @note_id, @note_number, @note_number_normalized, nt.id, @company_id, date '2026-08-15', 'COMPLETE', u.id
            from core.note_types nt
            cross join auth.users u
            where nt.code = 'ENTRY' and u.username = 'admin';
            insert into core.note_field_values(note_id, field_id, field_value, created_by)
            select @note_id, nf.id, 1, u.id
            from core.note_fields nf
            join core.note_type_fields ntf on ntf.field_id = nf.id
            join core.note_types nt on nt.id = ntf.note_type_id and nt.code = 'ENTRY'
            cross join auth.users u
            where nf.code = 'factory' and u.username = 'admin';
            insert into audit.logs(user_id, action_type, schema_name, table_name, record_id, description)
            select u.id, 'ACCEPTANCE_CLONE_SENTINEL', 'core', 'notes', @note_id, @company_name
            from auth.users u where u.username = 'admin';
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("company_name", companyName);
            command.Parameters.AddWithValue("note_id", noteId);
            command.Parameters.AddWithValue("note_number", "ACCEPTANCE-059-" + companyId.ToString("N"));
            command.Parameters.AddWithValue("note_number_normalized", "ACCEPTANCE059" + companyId.ToString("N").ToUpperInvariant());
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return await ReadSentinelDataAsync(factory, companyName);
    }

    private static async Task<(long Companies, long Users, long Notes, long Values, long AuditLogs, string CompanyName)> ReadSentinelDataAsync(DbConnectionFactory factory, string? expectedCompanyName = null)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            select
                (select count(*) from core.companies where name like 'Acceptance Clone %'),
                (select count(*) from auth.users),
                (select count(*) from core.notes where note_number like 'ACCEPTANCE-059-%'),
                (select count(*) from core.note_field_values where note_id in (select id from core.notes where note_number like 'ACCEPTANCE-059-%')),
                (select count(*) from audit.logs where action_type = 'ACCEPTANCE_CLONE_SENTINEL'),
                coalesce((select name from core.companies where name like 'Acceptance Clone %' order by created_at desc limit 1), '')
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5));
        if (expectedCompanyName is not null) Assert.Equal(expectedCompanyName, result.Item6);
        return result;
    }

    private static async Task<string> ReadDatabaseVersionAsync(DbConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select database_version from system.database_settings where singleton", connection);
        return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
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

    private static async Task RunPostgresToolAsync(string fileName, string password, IReadOnlyList<string> arguments)
    {
        var toolDirectory = Environment.GetEnvironmentVariable("AUDIT_MEMO_PG_BIN") ?? @"C:\Program Files\PostgreSQL\18\bin";
        var toolPath = Path.Combine(toolDirectory, fileName);
        if (!File.Exists(toolPath)) throw new FileNotFoundException($"تعذر العثور على أداة PostgreSQL: {toolPath}");
        var startInfo = new ProcessStartInfo(toolPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        startInfo.Environment["PGPASSWORD"] = password;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"تعذر تشغيل {fileName}.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{fileName} فشل ({process.ExitCode}). {output} {error}");
    }

    private static async Task DropDisposableDatabaseAsync(DbConnectionFactory factory, ConnectionProfileModel profile, string prefix)
    {
        if (!profile.Database.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidOperationException("Only the supported acceptance database may be removed.");
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

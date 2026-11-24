using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AuditMemoSystem.Data.Connection;
using Npgsql;

namespace AuditMemoSystem.Data.Database;

public sealed class MigrationRunner
{
    // 052 was applied by the 1.0.3.1 field package with this checksum. The
    // function contract is validated before this one-time metadata rebaseline;
    // an arbitrary checksum mismatch must still fail closed.
    private const string Legacy052Checksum = "ac51119f54518a8e75ecb07ba7cf59d781000c81b3ff8c8c439ea5edadcf0c84";
    private static readonly Regex ScriptName = new("^(?<version>\\d{3,})_[A-Za-z0-9_]+\\.sql$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly DbConnectionFactory _factory;
    private readonly string _scriptDirectory;
    private readonly Func<string, CancellationToken, Task>? _beforeHistorySuccessWriteAsync;

    public MigrationRunner(
        DbConnectionFactory factory,
        string scriptDirectory,
        Func<string, CancellationToken, Task>? beforeHistorySuccessWriteAsync = null)
    {
        _factory = factory;
        _scriptDirectory = scriptDirectory;
        _beforeHistorySuccessWriteAsync = beforeHistorySuccessWriteAsync;
    }

    public Task RunAsync(CancellationToken cancellationToken = default) => RunAsync(null, cancellationToken);

    public async Task RunAsync(Guid? updatedBy, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_scriptDirectory)) throw new DirectoryNotFoundException("لم يتم العثور على مجلد سكربتات قاعدة البيانات.");
        var scripts = LoadAndValidateScripts();
        await using var connection = _factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var lockCommand = new NpgsqlCommand("select pg_advisory_lock(hashtext('audit_memo_schema_migrations'))", connection);
        await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        try
        {
            await EnsureMigrationTableAsync(connection, cancellationToken);
            foreach (var script in scripts)
            {
                await using var lookupTransaction = await connection.BeginTransactionAsync(cancellationToken);
                var applied = await GetAppliedMigrationAsync(connection, lookupTransaction, script.Version, cancellationToken);
                if (applied is not null)
                {
                    var nameMatches = string.Equals(applied.Value.Name, script.Name, StringComparison.Ordinal);
                    var checksumMatches = string.IsNullOrEmpty(applied.Value.Checksum) ||
                        string.Equals(applied.Value.Checksum, script.Checksum, StringComparison.Ordinal);
                    if (!nameMatches || (!checksumMatches && !await TryRebaselineLegacy052Async(connection, lookupTransaction, script, applied.Value.Checksum, cancellationToken)))
                    {
                        throw new InvalidOperationException($"تم تعديل ترحيل مطبق أو تغيير اسمه: {script.Name}");
                    }
                    if (string.IsNullOrEmpty(applied.Value.Checksum))
                    {
                        await UpdateChecksumAsync(connection, lookupTransaction, script, cancellationToken);
                    }
                    await lookupTransaction.CommitAsync(cancellationToken);
                    if (await IsMigrationHistoryAvailableAsync(connection, cancellationToken))
                    {
                        try
                        {
                            await MarkMigrationSucceededAsync(
                                connection,
                                script,
                                GetPreviousVersion(scripts, script),
                                cancellationToken);
                        }
                        catch (Exception historyException)
                        {
                            throw new MigrationHistoryPendingException(script.Version, historyException);
                        }
                    }
                    continue;
                }

                await lookupTransaction.CommitAsync(cancellationToken);
                var previousVersion = await GetCurrentDatabaseVersionAsync(connection, cancellationToken);
                var historyIsAvailable = await IsMigrationHistoryAvailableAsync(connection, cancellationToken);
                if (historyIsAvailable)
                {
                    await MarkMigrationRunningAsync(connection, script, previousVersion, cancellationToken);
                }

                var committed = false;
                try
                {
                    var sql = await File.ReadAllTextAsync(script.Path, cancellationToken);
                    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                    await using (var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 300 })
                    {
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }
                    await using (var mark = new NpgsqlCommand("insert into system.schema_migrations(version, script_name, checksum) values (@version, @name, @checksum)", connection, transaction))
                    {
                        mark.Parameters.AddWithValue("version", script.Version);
                        mark.Parameters.AddWithValue("name", script.Name);
                        mark.Parameters.AddWithValue("checksum", script.Checksum);
                        await mark.ExecuteNonQueryAsync(cancellationToken);
                    }
                    await transaction.CommitAsync(cancellationToken);
                    committed = true;
                }
                catch (Exception exception)
                {
                    if (historyIsAvailable)
                    {
                        await MarkMigrationFailedAsync(connection, script, previousVersion, exception, cancellationToken);
                    }

                    var databaseError = exception is PostgresException postgresException
                        ? $" رمز قاعدة البيانات: {postgresException.SqlState}. التفاصيل: {SafePostgresMessage(postgresException.MessageText)}"
                        : string.Empty;
                    throw new InvalidOperationException(
                        $"فشل تنفيذ ترحيل قاعدة البيانات {script.Version} ({script.Name}).{databaseError} راجع سجل system.database_migration_history.",
                        exception);
                }

                if (committed && (historyIsAvailable || await IsMigrationHistoryAvailableAsync(connection, cancellationToken)))
                {
                    try
                    {
                        if (_beforeHistorySuccessWriteAsync is not null)
                        {
                            await _beforeHistorySuccessWriteAsync(script.Version, cancellationToken);
                        }
                        await MarkMigrationSucceededAsync(connection, script, previousVersion, cancellationToken);
                    }
                    catch (Exception historyException)
                    {
                        throw new MigrationHistoryPendingException(script.Version, historyException);
                    }
                }
            }
            await UpdateVersionTrackingAsync(connection, scripts[^1].Version, updatedBy, cancellationToken);
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("select pg_advisory_unlock(hashtext('audit_memo_schema_migrations'))", connection);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    public async Task<MigrationVersionStatus> GetVersionStatusAsync(CancellationToken cancellationToken = default)
    {
        var scripts = LoadAndValidateScripts();
        var applicationVersion = scripts[^1].Version;
        await using var connection = _factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var exists = new NpgsqlCommand("select to_regclass('system.schema_migrations') is not null", connection);
        if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken) ?? false))
        {
            return new MigrationVersionStatus("000", applicationVersion, false);
        }

        await using var command = new NpgsqlCommand("""
            select coalesce((
                select version
                from system.schema_migrations
                where version ~ '^[0-9]+$'
                order by version::numeric desc
                limit 1
            ), '000')
            """, connection);
        var databaseVersion = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? "000";
        return new MigrationVersionStatus(databaseVersion, applicationVersion, true);
    }

    private IReadOnlyList<MigrationScript> LoadAndValidateScripts()
    {
        var scripts = Directory.GetFiles(_scriptDirectory, "*.sql")
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var match = ScriptName.Match(name);
                if (!match.Success) throw new InvalidOperationException($"اسم ملف الترحيل غير صالح: {name}");
                return new MigrationScript(path, name, match.Groups["version"].Value, Convert.ToInt32(match.Groups["version"].Value), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            })
            .OrderBy(script => script.Number)
            .ToArray();
        if (scripts.Length == 0) throw new InvalidOperationException("لا توجد ملفات ترحيل.");
        for (var index = 1; index < scripts.Length; index++)
        {
            if (scripts[index].Number != scripts[index - 1].Number + 1)
            {
                throw new InvalidOperationException("تسلسل ملفات الترحيل يحتوي فجوة أو تكراراً.");
            }
        }
        return scripts;
    }

    private static async Task EnsureMigrationTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            create schema if not exists system;
            create table if not exists system.schema_migrations (
                version varchar(30) primary key,
                script_name text not null,
                checksum varchar(64),
                applied_at timestamp not null default now()
            );
            alter table system.schema_migrations add column if not exists checksum varchar(64);
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(string Name, string? Checksum)?> GetAppliedMigrationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string version, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select script_name, checksum from system.schema_migrations where version = @version", connection, transaction);
        command.Parameters.AddWithValue("version", version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1))
            : null;
    }

    private static async Task UpdateChecksumAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, MigrationScript script, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("update system.schema_migrations set checksum = @checksum where version = @version and checksum is null", connection, transaction);
        command.Parameters.AddWithValue("version", script.Version);
        command.Parameters.AddWithValue("checksum", script.Checksum);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> TryRebaselineLegacy052Async(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MigrationScript script,
        string? appliedChecksum,
        CancellationToken cancellationToken)
    {
        if (script.Version != "052" || !string.Equals(script.Name, "052_fix_company_metrics_duplicate_scope.sql", StringComparison.Ordinal) ||
            !string.Equals(appliedChecksum, Legacy052Checksum, StringComparison.Ordinal))
        {
            return false;
        }

        await using var functionDefinition = new NpgsqlCommand(
            "select coalesce(pg_get_functiondef('reporting.fn_company_advanced_metrics(uuid,date,date)'::regprocedure), '')",
            connection,
            transaction);
        var definition = Convert.ToString(await functionDefinition.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
        var normalized = definition.ToLowerInvariant();
        if (!normalized.Contains("duplicate_values as", StringComparison.Ordinal) ||
            !normalized.Contains("coalesce(di.created_note_id, di.matched_note_id)", StringComparison.Ordinal) ||
            !normalized.Contains("duplicate_notes", StringComparison.Ordinal))
        {
            return false;
        }

        await using var update = new NpgsqlCommand(
            "update system.schema_migrations set checksum = @checksum where version = @version and checksum = @legacy_checksum",
            connection,
            transaction);
        update.Parameters.AddWithValue("version", script.Version);
        update.Parameters.AddWithValue("checksum", script.Checksum);
        update.Parameters.AddWithValue("legacy_checksum", Legacy052Checksum);
        return await update.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static string GetPreviousVersion(IReadOnlyList<MigrationScript> scripts, MigrationScript script)
    {
        var index = Array.IndexOf(scripts.ToArray(), script);
        return index <= 0 ? "000" : scripts[index - 1].Version;
    }

    private static async Task<string> GetCurrentDatabaseVersionAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select coalesce((
                select version
                from system.schema_migrations
                where version ~ '^[0-9]+$'
                order by version::numeric desc
                limit 1
            ), '000')
            """, connection);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? "000";
    }

    private static async Task<bool> IsMigrationHistoryAvailableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select to_regclass('system.database_migration_history') is not null", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task MarkMigrationRunningAsync(NpgsqlConnection connection, MigrationScript script, string previousVersion, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into system.database_migration_history (
                migration_version, previous_version, new_version, started_at, completed_at, status, description, error_details)
            values (@version, @previous_version, @new_version, now(), null, 'RUNNING', @description, null)
            on conflict (migration_version) do update
            set previous_version = excluded.previous_version,
                new_version = excluded.new_version,
                started_at = excluded.started_at,
                completed_at = null,
                status = 'RUNNING',
                description = excluded.description,
                error_details = null
            """, connection);
        command.Parameters.AddWithValue("version", script.Version);
        command.Parameters.AddWithValue("previous_version", previousVersion);
        command.Parameters.AddWithValue("new_version", script.Version);
        command.Parameters.AddWithValue("description", script.Name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkMigrationSucceededAsync(NpgsqlConnection connection, MigrationScript script, string previousVersion, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into system.database_migration_history (
                migration_version, previous_version, new_version, started_at, completed_at, status, description, error_details)
            values (@version, @previous_version, @new_version, now(), now(), 'SUCCESS', @description, null)
            on conflict (migration_version) do update
            set previous_version = excluded.previous_version,
                new_version = excluded.new_version,
                completed_at = now(),
                status = 'SUCCESS',
                description = excluded.description,
                error_details = null
            """, connection);
        command.Parameters.AddWithValue("version", script.Version);
        command.Parameters.AddWithValue("previous_version", previousVersion);
        command.Parameters.AddWithValue("new_version", script.Version);
        command.Parameters.AddWithValue("description", script.Name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkMigrationFailedAsync(NpgsqlConnection connection, MigrationScript script, string previousVersion, Exception exception, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            update system.database_migration_history
            set completed_at = now(),
                status = 'FAILED',
                error_details = @error_details
            where migration_version = @version
            """, connection);
        command.Parameters.AddWithValue("version", script.Version);
        command.Parameters.AddWithValue("error_details", SafeErrorDetails(exception));
        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated == 0)
        {
            await MarkMigrationRunningAsync(connection, script, previousVersion, cancellationToken);
            await MarkMigrationFailedAsync(connection, script, previousVersion, exception, cancellationToken);
        }
    }

    private static string SafePostgresMessage(string? message)
    {
        var normalized = (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= 220 ? normalized : normalized[..220];
    }

    private static string SafeErrorDetails(Exception exception)
    {
        var message = $"{exception.GetType().Name}: {exception.Message}".Replace("\r", " ").Replace("\n", " ").Trim();
        return message.Length <= 2_000 ? message : message[..2_000];
    }

    private static async Task UpdateVersionTrackingAsync(NpgsqlConnection connection, string version, Guid? updatedBy, CancellationToken cancellationToken)
    {
        await using var settingsExists = new NpgsqlCommand("select to_regclass('system.database_settings') is not null", connection);
        if (!(bool)(await settingsExists.ExecuteScalarAsync(cancellationToken) ?? false)) return;

        await using var update = new NpgsqlCommand("""
            insert into system.database_settings (
                singleton, database_version, last_updated_at, last_updated_by)
            values (true, @version, now(), @updated_by)
            on conflict (singleton) do update
            set database_version = excluded.database_version,
                last_updated_at = excluded.last_updated_at,
                last_updated_by = excluded.last_updated_by
            """, connection);
        update.Parameters.AddWithValue("version", version);
        update.Parameters.Add("updated_by", NpgsqlTypes.NpgsqlDbType.Uuid).Value = (object?)updatedBy ?? DBNull.Value;
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record MigrationScript(string Path, string Name, string Version, int Number, string Checksum);
}

public sealed class MigrationHistoryPendingException : InvalidOperationException
{
    public MigrationHistoryPendingException(string migrationVersion, Exception innerException)
        : base($"تم تثبيت ترحيل قاعدة البيانات {migrationVersion} بنجاح، لكن تعذر تحديث سجل history. سيُستكمل تصحيح السجل في التشغيل التالي دون إعادة تطبيق SQL.", innerException)
    {
        MigrationVersion = migrationVersion;
    }

    public string MigrationVersion { get; }
}
public static class MigrationSupportPolicy
{
    // 014 is the oldest database clone that 1.0.7 explicitly supports.
    public const string MinimumSupportedDatabaseVersion = "014";
}

public sealed record MigrationVersionStatus(string DatabaseVersion, string ApplicationVersion, bool IsInitialized)
{
    public bool IsBelowMinimumSupported =>
        IsInitialized
        && !string.Equals(DatabaseVersion, "000", StringComparison.Ordinal)
        && Compare(DatabaseVersion, MigrationSupportPolicy.MinimumSupportedDatabaseVersion) < 0;

    public bool RequiresUpgrade => IsInitialized && Compare(DatabaseVersion, ApplicationVersion) < 0;
    public bool DatabaseIsNewer => IsInitialized && Compare(DatabaseVersion, ApplicationVersion) > 0;

    private static int Compare(string left, string right) =>
        int.TryParse(left, out var leftValue) && int.TryParse(right, out var rightValue)
            ? leftValue.CompareTo(rightValue)
            : string.Compare(left, right, StringComparison.Ordinal);
}

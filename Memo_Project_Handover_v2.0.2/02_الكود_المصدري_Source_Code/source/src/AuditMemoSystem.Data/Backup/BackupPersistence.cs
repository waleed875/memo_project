using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using AuditMemoSystem.Data.Connection;
using Npgsql;
using NpgsqlTypes;

namespace AuditMemoSystem.Data.Backup;

public sealed class NpgsqlBackupAuthorization : IBackupAuthorization
{
    private readonly DbConnectionFactory _factory;

    public NpgsqlBackupAuthorization(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task EnsureAdministratorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_factory.ActiveProfile.Type != ConnectionProfileType.SERVER)
        {
            throw new InvalidOperationException("يجب تنفيذ إدارة النسخ والاستعادة من جلسة SERVER موثقة، وليس من جلسة LOCAL.");
        }

        await using var connection = _factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select r.code
            from auth.users u
            join auth.roles r on r.id = u.role_id
            where u.id = @user_id and u.is_active and not u.is_deleted and r.is_active
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        var roleCode = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (!string.Equals(roleCode, UserRole.ADMIN.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("عملية النسخ أو الاستعادة متاحة لمدير النظام فقط.");
        }
    }
}

public sealed class NpgsqlBackupHistoryStore : IBackupHistoryStore
{
    private readonly DbConnectionFactory _factory;

    public NpgsqlBackupHistoryStore(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<Guid> StartAsync(BackupHistoryOperation operation, CancellationToken cancellationToken = default)
    {
        operation.Id = operation.Id == Guid.Empty ? Guid.NewGuid() : operation.Id;
        await using var connection = _factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            insert into system.backup_history(
                id, backup_type, source_profile_name, target_profile_name, file_path, status, started_at, created_by)
            values (
                @id, @backup_type, @source_profile_name, @target_profile_name, @file_path, 'RUNNING', now(), @created_by)
            """, connection);
        command.Parameters.AddWithValue("id", operation.Id);
        command.Parameters.AddWithValue("backup_type", operation.BackupType);
        command.Parameters.Add("source_profile_name", NpgsqlDbType.Varchar).Value = (object?)operation.SourceProfileName ?? DBNull.Value;
        command.Parameters.Add("target_profile_name", NpgsqlDbType.Varchar).Value = (object?)operation.TargetProfileName ?? DBNull.Value;
        command.Parameters.Add("file_path", NpgsqlDbType.Text).Value = (object?)operation.FilePath ?? DBNull.Value;
        command.Parameters.AddWithValue("created_by", operation.CreatedBy);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return operation.Id;
    }

    public async Task CompleteAsync(Guid operationId, BackupOperationStatus status, string? errorMessage, CancellationToken cancellationToken = default)
    {
        if (status == BackupOperationStatus.RUNNING)
        {
            throw new InvalidOperationException("لا يمكن إكمال سجل النسخ بحالة RUNNING.");
        }

        await using var connection = _factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string backupType;
        Guid createdBy;
        string? filePath;
        await using (var update = new NpgsqlCommand("""
            update system.backup_history
            set status = @status, finished_at = now(), error_message = @error_message
            where id = @id and status = 'RUNNING'
            returning backup_type, created_by, file_path
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("id", operationId);
            update.Parameters.AddWithValue("status", status.ToString());
            update.Parameters.Add("error_message", NpgsqlDbType.Text).Value = (object?)errorMessage ?? DBNull.Value;
            await using var reader = await update.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("سجل عملية النسخ غير موجود أو أُغلق مسبقًا.");
            }
            backupType = reader.GetString(0);
            createdBy = reader.GetGuid(1);
            filePath = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        await using (var audit = new NpgsqlCommand("""
            insert into audit.logs(user_id, action_type, schema_name, table_name, record_id, description)
            values (@user_id, @action_type, 'system', 'backup_history', @record_id, @description)
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("user_id", createdBy);
            audit.Parameters.AddWithValue("action_type", $"{backupType}_{status}");
            audit.Parameters.AddWithValue("record_id", operationId);
            audit.Parameters.AddWithValue("description", $"الحالة: {status}; الملف: {Path.GetFileName(filePath) ?? "غير محدد"}");
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

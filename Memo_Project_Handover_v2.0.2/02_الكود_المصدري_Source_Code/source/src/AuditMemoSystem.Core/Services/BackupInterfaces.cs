using AuditMemoSystem.Core.Models;

namespace AuditMemoSystem.Core.Services;

public interface IPostgresToolRunner
{
    Task EnsureAvailableAsync(IReadOnlyCollection<string> toolNames, CancellationToken cancellationToken = default);
    Task<PostgresToolResult> RunAsync(PostgresToolCommand command, CancellationToken cancellationToken = default);
}

public interface IPostgresToolDiagnostics
{
    Task RecordFailureAsync(string toolName, string technicalError, CancellationToken cancellationToken = default);
}

public interface IBackupHistoryStore
{
    Task<Guid> StartAsync(BackupHistoryOperation operation, CancellationToken cancellationToken = default);
    Task CompleteAsync(Guid operationId, BackupOperationStatus status, string? errorMessage, CancellationToken cancellationToken = default);
}

public interface IBackupAuthorization
{
    Task EnsureAdministratorAsync(Guid userId, CancellationToken cancellationToken = default);
}

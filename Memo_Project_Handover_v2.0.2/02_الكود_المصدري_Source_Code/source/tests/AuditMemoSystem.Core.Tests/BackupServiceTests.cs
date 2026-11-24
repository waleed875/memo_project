using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using AuditMemoSystem.Core.Validation;
using AuditMemoSystem.Data.Backup;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public void EndpointIdentity_NormalizesEquivalentLoopbackEndpoints()
    {
        var first = Server("Primary", "localhost", "Audit_Memo");
        var second = Local("Replica", "127.0.0.1", "audit_memo");

        Assert.Equal(DatabaseEndpointIdentity.From(first), DatabaseEndpointIdentity.From(second));
    }

    [Fact]
    public async Task Copy_RejectsSameEndpointBeforeAuthorizationOrProcess()
    {
        var authorization = new FakeAuthorization();
        var history = new FakeHistoryStore();
        var runner = new FakeToolRunner();
        var service = CreateService(authorization, history, runner, Local("Allowed", "localhost", "audit_memo"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CopyServerToLocalAsync(
            Server("Source", "localhost", "audit_memo"),
            Local("Target", "127.0.0.1", "AUDIT_MEMO"),
            "unused",
            Guid.NewGuid()));

        Assert.Contains("نفسها", exception.Message);
        Assert.Equal(0, authorization.CallCount);
        Assert.Equal(0, runner.RunCount);
        Assert.Empty(history.Started);
    }

    [Fact]
    public async Task Copy_RejectsLocalEnumWhenEndpointIsNotAllowlisted()
    {
        var authorization = new FakeAuthorization();
        var history = new FakeHistoryStore();
        var runner = new FakeToolRunner();
        var service = CreateService(authorization, history, runner, Local("Allowed", "localhost", "allowed_db"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CopyServerToLocalAsync(
            Server("Source", "server.internal", "audit_memo"),
            Local("Target", "localhost", "other_local_db"),
            "unused",
            Guid.NewGuid()));

        Assert.Contains("قائمة السماح", exception.Message);
        Assert.Equal(0, authorization.CallCount);
        Assert.Equal(0, runner.RunCount);
    }

    [Fact]
    public async Task Copy_ToAllowedDistinctTarget_CreatesRecoveryCopyRestoresAndVerifies()
    {
        using var directory = new TemporaryDirectory();
        var authorization = new FakeAuthorization();
        var history = new FakeHistoryStore();
        var runner = new FakeToolRunner
        {
            Handler = command =>
            {
                if (command.ToolName == "pg_dump") File.WriteAllText(command.OutputFile!, "backup");
                return Task.FromResult(new PostgresToolResult(""));
            }
        };
        var target = Local("Allowed", "localhost", "audit_memo_local");
        var service = CreateService(authorization, history, runner, target);

        var serverBackup = await service.CopyServerToLocalAsync(
            Server("Source", "server.internal", "audit_memo"),
            target,
            directory.Path,
            Guid.NewGuid());

        Assert.Equal(1, authorization.CallCount);
        Assert.Equal(5, runner.RunCount);
        Assert.True(File.Exists(serverBackup));
        Assert.Single(history.Started);
        Assert.Equal(BackupOperationStatus.SUCCESS, Assert.Single(history.Completed).Status);
        Assert.Equal(["pg_dump", "pg_restore", "pg_dump", "pg_restore", "psql"], runner.Commands.Select(command => command.ToolName));
    }

    [Fact]
    public async Task Restore_ToAllowedTarget_CreatesRecoveryCopyAndVerifiesResult()
    {
        using var directory = new TemporaryDirectory();
        var backupFile = System.IO.Path.Combine(directory.Path, "input.backup");
        File.WriteAllText(backupFile, "test");
        var history = new FakeHistoryStore();
        var runner = new FakeToolRunner
        {
            Handler = command =>
            {
                if (command.ToolName == "pg_dump") File.WriteAllText(command.OutputFile!, "backup");
                return Task.FromResult(new PostgresToolResult(""));
            }
        };
        var target = Local("Allowed", "localhost", "audit_memo_local");
        var service = CreateService(new FakeAuthorization(), history, runner, target);

        await service.RestoreFileToLocalAsync(target, backupFile, Guid.NewGuid());

        Assert.Equal(4, runner.RunCount);
        Assert.Equal(BackupOperationStatus.SUCCESS, Assert.Single(history.Completed).Status);
    }

    [Fact]
    public async Task Backup_SuccessUsesUniqueCreateNewPathsAndRecordsSuccess()
    {
        using var directory = new TemporaryDirectory();
        var authorization = new FakeAuthorization();
        var history = new FakeHistoryStore();
        var runner = new FakeToolRunner
        {
            Handler = command =>
            {
                if (command.OutputFile is not null) File.WriteAllText(command.OutputFile, "backup");
                return Task.FromResult(new PostgresToolResult(""));
            }
        };
        var service = CreateService(authorization, history, runner, Local("Allowed", "localhost", "audit_memo_local"));
        var source = Server("Source", "server.internal", "audit_memo");

        var first = await service.BackupServerToFileAsync(source, directory.Path, Guid.NewGuid());
        var second = await service.BackupServerToFileAsync(source, directory.Path, Guid.NewGuid());

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.All(history.Completed, item => Assert.Equal(BackupOperationStatus.SUCCESS, item.Status));
        Assert.Equal(["pg_dump", "pg_restore", "pg_dump", "pg_restore"], runner.Commands.Select(command => command.ToolName));
    }

    [Fact]
    public async Task Backup_UnreadableArchiveFailsBeforeUpgradeCanUseIt()
    {
        using var directory = new TemporaryDirectory();
        var history = new FakeHistoryStore();
        var runner = new FakeToolRunner
        {
            Handler = command =>
            {
                if (command.OutputFile is not null) File.WriteAllText(command.OutputFile, "not-a-postgres-archive");
                if (command.ToolName == "pg_restore") throw new PostgresToolExecutionException("pg_restore", "unreadable archive");
                return Task.FromResult(new PostgresToolResult(""));
            }
        };
        var service = CreateService(new FakeAuthorization(), history, runner, Local("Allowed", "localhost", "audit_memo_local"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BackupServerToFileAsync(
            Server("Source", "server.internal", "audit_memo"), directory.Path, Guid.NewGuid()));

        Assert.Equal(BackupOperationStatus.FAILED, Assert.Single(history.Completed).Status);
        Assert.DoesNotContain(Directory.GetFiles(directory.Path), path => path.EndsWith(".backup", StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public async Task Backup_FailureRecordsSanitizedErrorWithoutRawStderr()
    {
        using var directory = new TemporaryDirectory();
        var history = new FakeHistoryStore();
        var runner = new FakeToolRunner
        {
            Handler = _ => throw new PostgresToolExecutionException("pg_dump", "RAW_DATABASE_SECRET")
        };
        var service = CreateService(new FakeAuthorization(), history, runner, Local("Allowed", "localhost", "audit_memo_local"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BackupServerToFileAsync(
            Server("Source", "server.internal", "audit_memo"), directory.Path, Guid.NewGuid()));

        Assert.DoesNotContain("RAW_DATABASE_SECRET", exception.Message);
        var completion = Assert.Single(history.Completed);
        Assert.Equal(BackupOperationStatus.FAILED, completion.Status);
        Assert.DoesNotContain("RAW_DATABASE_SECRET", completion.ErrorMessage ?? "");
    }

    [Fact]
    public async Task Backup_CancellationRecordsCancelled()
    {
        using var directory = new TemporaryDirectory();
        var history = new FakeHistoryStore();
        var runner = new FakeToolRunner
        {
            Handler = _ => throw new OperationCanceledException()
        };
        var service = CreateService(new FakeAuthorization(), history, runner, Local("Allowed", "localhost", "audit_memo_local"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.BackupServerToFileAsync(
            Server("Source", "server.internal", "audit_memo"), directory.Path, Guid.NewGuid()));

        Assert.Equal(BackupOperationStatus.CANCELLED, Assert.Single(history.Completed).Status);
    }

    private static BackupService CreateService(
        FakeAuthorization authorization,
        FakeHistoryStore history,
        FakeToolRunner runner,
        ConnectionProfileModel allowedTarget)
    {
        return new BackupService(
            authorization,
            history,
            runner,
            new BackupRecoverySection
            {
                AllowedRestoreTargets =
                [
                    new DatabaseEndpointModel
                    {
                        Host = allowedTarget.Host,
                        Port = allowedTarget.Port,
                        Database = allowedTarget.Database
                    }
                ]
            });
    }

    private static ConnectionProfileModel Server(string name, string host, string database) => new()
    {
        Name = name,
        Type = ConnectionProfileType.SERVER,
        Host = host,
        Port = 5432,
        Database = database,
        Username = "user"
    };

    private static ConnectionProfileModel Local(string name, string host, string database) => new()
    {
        Name = name,
        Type = ConnectionProfileType.LOCAL,
        Host = host,
        Port = 5432,
        Database = database,
        Username = "user"
    };

    private sealed class FakeAuthorization : IBackupAuthorization
    {
        public int CallCount { get; private set; }

        public Task EnsureAdministratorAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHistoryStore : IBackupHistoryStore
    {
        public List<BackupHistoryOperation> Started { get; } = [];
        public List<(Guid Id, BackupOperationStatus Status, string? ErrorMessage)> Completed { get; } = [];

        public Task<Guid> StartAsync(BackupHistoryOperation operation, CancellationToken cancellationToken = default)
        {
            operation.Id = Guid.NewGuid();
            Started.Add(operation);
            return Task.FromResult(operation.Id);
        }

        public Task CompleteAsync(Guid operationId, BackupOperationStatus status, string? errorMessage, CancellationToken cancellationToken = default)
        {
            Completed.Add((operationId, status, errorMessage));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeToolRunner : IPostgresToolRunner
    {
        public Func<PostgresToolCommand, Task<PostgresToolResult>> Handler { get; set; } =
            _ => Task.FromResult(new PostgresToolResult(""));
        public int RunCount { get; private set; }
        public List<PostgresToolCommand> Commands { get; } = [];

        public Task EnsureAvailableAsync(IReadOnlyCollection<string> toolNames, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PostgresToolResult> RunAsync(PostgresToolCommand command, CancellationToken cancellationToken = default)
        {
            RunCount++;
            Commands.Add(command);
            return Handler(command);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "audit-memo-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

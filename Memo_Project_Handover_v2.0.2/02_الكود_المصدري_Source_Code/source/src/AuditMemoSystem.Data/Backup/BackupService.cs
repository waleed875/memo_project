using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using AuditMemoSystem.Core.Validation;

namespace AuditMemoSystem.Data.Backup;

public sealed class BackupService : IBackupService
{
    private static readonly string[] RequiredTools = ["pg_dump", "pg_restore", "psql"];
    private readonly IBackupAuthorization _authorization;
    private readonly IBackupHistoryStore _history;
    private readonly IPostgresToolRunner _toolRunner;
    private readonly BackupRecoverySection _settings;

    public BackupService(
        IBackupAuthorization authorization,
        IBackupHistoryStore history,
        IPostgresToolRunner toolRunner,
        BackupRecoverySection settings)
    {
        _authorization = authorization;
        _history = history;
        _toolRunner = toolRunner;
        _settings = settings;
    }

    public Task EnsureToolsReadyAsync(CancellationToken cancellationToken = default) =>
        _toolRunner.EnsureAvailableAsync(RequiredTools, cancellationToken);

    public async Task<string> BackupServerToFileAsync(
        ConnectionProfileModel serverProfile,
        string backupDirectory,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverProfile);
        if (serverProfile.Type != ConnectionProfileType.SERVER)
        {
            throw new InvalidOperationException("يجب اختيار اتصال من نوع SERVER للنسخ الاحتياطي.");
        }

        _ = DatabaseEndpointIdentity.From(serverProfile);
        await _authorization.EnsureAdministratorAsync(userId, cancellationToken);
        await _toolRunner.EnsureAvailableAsync(["pg_dump"], cancellationToken);

        var filePath = BackupFileNameFactory.CreatePath(
            backupDirectory,
            serverProfile.Database,
            DateTimeOffset.Now,
            Guid.NewGuid());
        var historyId = await _history.StartAsync(
            new BackupHistoryOperation
            {
                BackupType = "SERVER_TO_FILE",
                SourceProfileName = serverProfile.Name,
                FilePath = filePath,
                CreatedBy = userId
            },
            cancellationToken);

        try
        {
            await _toolRunner.RunAsync(
                new PostgresToolCommand(
                    "pg_dump",
                    [
                        "--host", serverProfile.Host,
                        "--port", serverProfile.Port.ToString(),
                        "--username", serverProfile.Username,
                        "--format", "custom",
                        "--blobs",
                        "--verbose",
                        "--no-password",
                        serverProfile.Database
                    ],
                    serverProfile.Password,
                    filePath),
                cancellationToken);
            if (!File.Exists(filePath) || new FileInfo(filePath).Length <= 0)
            {
                throw new InvalidOperationException("أنشأت أداة PostgreSQL ملف نسخة احتياطية فارغًا أو غير موجود.");
            }
            await EnsureArchiveCanBeReadAsync(filePath, cancellationToken);            await _history.CompleteAsync(historyId, BackupOperationStatus.SUCCESS, null, CancellationToken.None);
            return filePath;
        }
        catch (OperationCanceledException)
        {
            await _history.CompleteAsync(historyId, BackupOperationStatus.CANCELLED, "أُلغيت العملية.", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            try
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
            catch
            {
                // Keep the original backup error; a failed archive must never be reported as usable.
            }
            var safeError = BackupErrorMessage.ForFailure(exception);
            await _history.CompleteAsync(historyId, BackupOperationStatus.FAILED, safeError, CancellationToken.None);
            throw new InvalidOperationException(safeError, exception);
        }
    }

    public async Task RestoreFileToLocalAsync(
        ConnectionProfileModel localProfile,
        string backupFile,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localProfile);
        EnsureRestoreTargetAllowed(localProfile);
        if (string.IsNullOrWhiteSpace(backupFile) || !File.Exists(backupFile))
        {
            throw new FileNotFoundException("ملف النسخة الاحتياطية غير موجود.", backupFile);
        }

        await _authorization.EnsureAdministratorAsync(userId, cancellationToken);
        await _toolRunner.EnsureAvailableAsync(RequiredTools, cancellationToken);
        var fullBackupFile = Path.GetFullPath(backupFile);
        var backupDirectory = Path.GetDirectoryName(fullBackupFile)
            ?? throw new InvalidOperationException("مسار ملف النسخة الاحتياطية غير صالح.");
        var historyId = await _history.StartAsync(
            new BackupHistoryOperation
            {
                BackupType = "FILE_TO_LOCAL",
                TargetProfileName = localProfile.Name,
                FilePath = fullBackupFile,
                CreatedBy = userId
            },
            cancellationToken);

        var recoveryFile = BackupFileNameFactory.CreatePath(backupDirectory, $"{localProfile.Database}_before_restore", DateTimeOffset.Now, Guid.NewGuid());
        var restoreStarted = false;
        try
        {
            await EnsureArchiveCanBeReadAsync(fullBackupFile, cancellationToken);
            EnsureFreeSpace(backupDirectory, new FileInfo(fullBackupFile).Length);
            await DumpAsync(localProfile, recoveryFile, cancellationToken);
            restoreStarted = true;
            await RestoreArchiveAsync(localProfile, fullBackupFile, cancellationToken);
            await VerifyLocalDatabaseAsync(localProfile, cancellationToken);
            await _history.CompleteAsync(historyId, BackupOperationStatus.SUCCESS, null, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await TryRollbackAsync(localProfile, recoveryFile, restoreStarted);
            await _history.CompleteAsync(historyId, BackupOperationStatus.CANCELLED, "أُلغيت العملية وتمت محاولة استعادة القاعدة المحلية.", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            var rollbackSucceeded = await TryRollbackAsync(localProfile, recoveryFile, restoreStarted);
            var safeError = BackupErrorMessage.ForRestoreFailure(exception, rollbackSucceeded);
            await _history.CompleteAsync(historyId, BackupOperationStatus.FAILED, safeError, CancellationToken.None);
            throw new InvalidOperationException(safeError, exception);
        }
    }

    public async Task<string> CopyServerToLocalAsync(
        ConnectionProfileModel serverProfile,
        ConnectionProfileModel localProfile,
        string backupDirectory,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverProfile);
        ArgumentNullException.ThrowIfNull(localProfile);
        if (serverProfile.Type != ConnectionProfileType.SERVER)
        {
            throw new InvalidOperationException("مصدر النسخ يجب أن يكون اتصال SERVER.");
        }

        EnsureRestoreTargetAllowed(localProfile);
        RestoreTargetPolicy.EnsureSourceAndTargetAreDistinct(serverProfile, localProfile);
        await _authorization.EnsureAdministratorAsync(userId, cancellationToken);

        if (string.IsNullOrWhiteSpace(backupDirectory))
        {
            throw new InvalidOperationException("مسار حفظ النسخ المحلية غير مهيأ.");
        }

        await _toolRunner.EnsureAvailableAsync(RequiredTools, cancellationToken);
        var fullBackupDirectory = Path.GetFullPath(backupDirectory);
        Directory.CreateDirectory(fullBackupDirectory);
        var serverBackupFile = BackupFileNameFactory.CreatePath(fullBackupDirectory, serverProfile.Database, DateTimeOffset.Now, Guid.NewGuid());
        var historyId = await _history.StartAsync(
            new BackupHistoryOperation
            {
                BackupType = "SERVER_TO_LOCAL",
                SourceProfileName = serverProfile.Name,
                TargetProfileName = localProfile.Name,
                FilePath = serverBackupFile,
                CreatedBy = userId
            },
            cancellationToken);

        var recoveryFile = BackupFileNameFactory.CreatePath(fullBackupDirectory, $"{localProfile.Database}_before_sync", DateTimeOffset.Now, Guid.NewGuid());
        var restoreStarted = false;
        try
        {
            await DumpAsync(serverProfile, serverBackupFile, cancellationToken);
            await EnsureArchiveCanBeReadAsync(serverBackupFile, cancellationToken);
            EnsureFreeSpace(fullBackupDirectory, new FileInfo(serverBackupFile).Length);
            await DumpAsync(localProfile, recoveryFile, cancellationToken);
            restoreStarted = true;
            await RestoreArchiveAsync(localProfile, serverBackupFile, cancellationToken);
            await VerifyLocalDatabaseAsync(localProfile, cancellationToken);
            await _history.CompleteAsync(historyId, BackupOperationStatus.SUCCESS, null, CancellationToken.None);
            return serverBackupFile;
        }
        catch (OperationCanceledException)
        {
            await TryRollbackAsync(localProfile, recoveryFile, restoreStarted);
            await _history.CompleteAsync(historyId, BackupOperationStatus.CANCELLED, "أُلغيت العملية وتمت محاولة استعادة القاعدة المحلية.", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            var rollbackSucceeded = await TryRollbackAsync(localProfile, recoveryFile, restoreStarted);
            var safeError = BackupErrorMessage.ForRestoreFailure(exception, rollbackSucceeded);
            await _history.CompleteAsync(historyId, BackupOperationStatus.FAILED, safeError, CancellationToken.None);
            throw new InvalidOperationException(safeError, exception);
        }
    }

    private async Task DumpAsync(ConnectionProfileModel profile, string outputFile, CancellationToken cancellationToken)
    {
        _ = await _toolRunner.RunAsync(
            new PostgresToolCommand(
                "pg_dump",
                [
                    "--host", profile.Host,
                    "--port", profile.Port.ToString(),
                    "--username", profile.Username,
                    "--format", "custom",
                    "--blobs",
                    "--verbose",
                    "--no-password",
                    profile.Database
                ],
                profile.Password,
                outputFile),
            cancellationToken);
    }

    private async Task EnsureArchiveCanBeReadAsync(string backupFile, CancellationToken cancellationToken)
    {
        _ = await _toolRunner.RunAsync(
            new PostgresToolCommand("pg_restore", ["--list", "--no-password", backupFile], ""),
            cancellationToken);
    }

    private async Task RestoreArchiveAsync(ConnectionProfileModel localProfile, string backupFile, CancellationToken cancellationToken)
    {
        _ = await _toolRunner.RunAsync(
            new PostgresToolCommand(
                "pg_restore",
                [
                    "--host", localProfile.Host,
                    "--port", localProfile.Port.ToString(),
                    "--username", localProfile.Username,
                    "--dbname", localProfile.Database,
                    "--clean",
                    "--if-exists",
                    "--no-owner",
                    "--no-privileges",
                    "--exit-on-error",
                    "--no-password",
                    backupFile
                ],
                localProfile.Password),
            cancellationToken);
    }

    private async Task VerifyLocalDatabaseAsync(ConnectionProfileModel localProfile, CancellationToken cancellationToken)
    {
        _ = await _toolRunner.RunAsync(
            new PostgresToolCommand(
                "psql",
                [
                    "--host", localProfile.Host,
                    "--port", localProfile.Port.ToString(),
                    "--username", localProfile.Username,
                    "--dbname", localProfile.Database,
                    "--no-password",
                    "--set", "ON_ERROR_STOP=1",
                    "--command", "select 1 from core.notes limit 1;"
                ],
                localProfile.Password),
            cancellationToken);
    }

    private async Task<bool> TryRollbackAsync(ConnectionProfileModel localProfile, string recoveryFile, bool restoreStarted)
    {
        if (!restoreStarted || !File.Exists(recoveryFile))
        {
            return false;
        }

        try
        {
            await RestoreArchiveAsync(localProfile, recoveryFile, CancellationToken.None);
            await VerifyLocalDatabaseAsync(localProfile, CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureFreeSpace(string directory, long sourceBackupSize)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(directory));
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("تعذر تحديد مساحة التخزين المحلية المتاحة.");
        }

        var requiredBytes = Math.Max(64L * 1024 * 1024, sourceBackupSize * 2);
        if (new DriveInfo(root).AvailableFreeSpace < requiredBytes)
        {
            throw new InvalidOperationException("لا توجد مساحة محلية كافية لإتمام النسخ والاستعادة بأمان.");
        }
    }

    private void EnsureRestoreTargetAllowed(ConnectionProfileModel profile) =>
        new RestoreTargetPolicy(_settings.AllowedRestoreTargets).EnsureAllowed(profile);
}

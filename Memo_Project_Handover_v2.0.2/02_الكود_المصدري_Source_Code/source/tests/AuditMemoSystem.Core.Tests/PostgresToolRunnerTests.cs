using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using AuditMemoSystem.Data.Backup;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class PostgresToolRunnerTests
{
    [Fact]
    public async Task MissingConfiguredTool_FailsReadinessBeforeAnyProcessStarts()
    {
        using var directory = new TemporaryDirectory();
        var runner = new ProcessPostgresToolRunner(new BackupRecoverySection
        {
            PostgreSqlToolsDirectory = directory.Path,
            ToolTimeoutSeconds = 5
        });

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            runner.EnsureAvailableAsync(["pg_dump"]));
    }

    [Fact]
    public async Task ExistingOutputFile_IsNotOverwrittenOrDeleted()
    {
        using var directory = new TemporaryDirectory();
        var fakeTool = System.IO.Path.Combine(directory.Path, "pg_dump.exe");
        var output = System.IO.Path.Combine(directory.Path, "existing.backup");
        File.WriteAllText(fakeTool, "not executed");
        File.WriteAllText(output, "original");
        var runner = new ProcessPostgresToolRunner(new BackupRecoverySection
        {
            PostgreSqlToolsDirectory = directory.Path,
            ToolTimeoutSeconds = 5
        });

        await Assert.ThrowsAsync<IOException>(() => runner.RunAsync(
            new PostgresToolCommand("pg_dump", [], "unused", output)));

        Assert.Equal("original", File.ReadAllText(output));
    }

    [Fact]
    public async Task FailedProcess_DeletesPartialOutputAndKeepsRawStderrOutOfMessage()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = new TemporaryDirectory();
        var output = System.IO.Path.Combine(directory.Path, "partial.backup");
        var diagnostics = new CaptureDiagnostics();
        var runner = WindowsCommandRunner(timeoutSeconds: 5, diagnostics);

        var exception = await Assert.ThrowsAsync<PostgresToolExecutionException>(() => runner.RunAsync(
            new PostgresToolCommand(
                "cmd",
                ["/c", "echo partial-output & echo credential-value 1>&2 & exit /b 7"],
                "credential-value",
                output)));

        Assert.False(File.Exists(output));
        Assert.DoesNotContain("credential-value", exception.Message);
        Assert.Contains("credential-value", exception.TechnicalError);
        Assert.DoesNotContain("credential-value", Assert.Single(diagnostics.Errors));
        Assert.Contains("[REDACTED]", Assert.Single(diagnostics.Errors));
    }

    [Fact]
    public async Task Timeout_TerminatesProcessTreeAndThrowsSafeTimeout()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = WindowsCommandRunner(timeoutSeconds: 1);
        var started = DateTime.UtcNow;

        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(
            new PostgresToolCommand("cmd", ["/c", "ping 127.0.0.1 -n 20 > nul"], "unused")));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(8));
    }

    private static ProcessPostgresToolRunner WindowsCommandRunner(int timeoutSeconds, IPostgresToolDiagnostics? diagnostics = null) => new(
        new BackupRecoverySection
        {
            PostgreSqlToolsDirectory = Environment.SystemDirectory,
            ToolTimeoutSeconds = timeoutSeconds
        },
        diagnostics);

    private sealed class CaptureDiagnostics : IPostgresToolDiagnostics
    {
        public List<string> Errors { get; } = [];

        public Task RecordFailureAsync(string toolName, string technicalError, CancellationToken cancellationToken = default)
        {
            Errors.Add(technicalError);
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "audit-memo-runner-tests", Guid.NewGuid().ToString("N"));
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

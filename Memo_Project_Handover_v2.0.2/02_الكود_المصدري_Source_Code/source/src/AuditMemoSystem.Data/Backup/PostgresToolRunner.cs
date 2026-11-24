using System.Diagnostics;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;

namespace AuditMemoSystem.Data.Backup;

public sealed class PostgresToolExecutionException : InvalidOperationException
{
    public PostgresToolExecutionException(string toolName, string technicalError)
        : base($"فشل تشغيل أداة PostgreSQL المطلوبة: {toolName}.")
    {
        TechnicalError = technicalError;
    }

    public string TechnicalError { get; }
}

public sealed class ProcessPostgresToolRunner : IPostgresToolRunner
{
    private readonly string _configuredDirectory;
    private readonly TimeSpan _timeout;
    private readonly IPostgresToolDiagnostics? _diagnostics;

    public ProcessPostgresToolRunner(BackupRecoverySection settings, IPostgresToolDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _configuredDirectory = settings.PostgreSqlToolsDirectory.Trim();
        _timeout = TimeSpan.FromSeconds(Math.Clamp(settings.ToolTimeoutSeconds, 1, 3600));
        _diagnostics = diagnostics;
    }

    public Task EnsureAvailableAsync(IReadOnlyCollection<string> toolNames, CancellationToken cancellationToken = default)
    {
        foreach (var toolName in toolNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = ResolveToolPath(toolName);
        }

        return Task.CompletedTask;
    }

    public async Task<PostgresToolResult> RunAsync(PostgresToolCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var toolPath = ResolveToolPath(command.ToolName);
        var startInfo = new ProcessStartInfo(toolPath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["PGPASSWORD"] = command.Password;

        FileStream? outputFile = null;
        var outputFileCreated = false;
        Process? process = null;
        try
        {
            if (command.OutputFile is not null)
            {
                var parent = Path.GetDirectoryName(command.OutputFile)
                    ?? throw new InvalidOperationException("مسار ملف النسخة الاحتياطية غير صالح.");
                Directory.CreateDirectory(parent);
                outputFile = new FileStream(command.OutputFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                outputFileCreated = true;
            }

            process = Process.Start(startInfo) ?? throw new InvalidOperationException($"تعذر تشغيل أداة PostgreSQL: {command.ToolName}.");
            var standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var standardOutputTask = outputFile is null
                ? process.StandardOutput.ReadToEndAsync(CancellationToken.None)
                : CopyOutputAsync(process.StandardOutput.BaseStream, outputFile);

            using var timeoutSource = new CancellationTokenSource(_timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            try
            {
                await process.WaitForExitAsync(linkedSource.Token);
            }
            catch (OperationCanceledException)
            {
                await TerminateProcessTreeAsync(process);
                await DrainAsync(standardOutputTask, standardErrorTask);
                if (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
                {
                    throw new TimeoutException("انتهت مهلة تشغيل أداة PostgreSQL.");
                }

                throw;
            }

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            if (outputFile is not null)
            {
                await outputFile.FlushAsync(CancellationToken.None);
            }
            if (process.ExitCode != 0)
            {
                if (_diagnostics is not null)
                {
                    var redactedError = string.IsNullOrEmpty(command.Password)
                        ? standardError
                        : standardError.Replace(command.Password, "[REDACTED]", StringComparison.Ordinal);
                    try
                    {
                        await _diagnostics.RecordFailureAsync(command.ToolName, redactedError, CancellationToken.None);
                    }
                    catch
                    {
                    }
                }
                throw new PostgresToolExecutionException(command.ToolName, standardError);
            }

            return new PostgresToolResult(standardOutput);
        }
        catch
        {
            if (process is { HasExited: false })
            {
                await TerminateProcessTreeAsync(process);
            }

            if (command.OutputFile is not null && outputFileCreated)
            {
                await DisposeOutputAsync(outputFile);
                outputFile = null;
                if (File.Exists(command.OutputFile))
                {
                    File.Delete(command.OutputFile);
                }
            }
            throw;
        }
        finally
        {
            await DisposeOutputAsync(outputFile);
            process?.Dispose();
        }
    }

    private string ResolveToolPath(string toolName)
    {
        var executableName = OperatingSystem.IsWindows() && !toolName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? toolName + ".exe"
            : toolName;

        if (!string.IsNullOrWhiteSpace(_configuredDirectory))
        {
            var configuredPath = Path.Combine(Path.GetFullPath(_configuredDirectory), executableName);
            if (File.Exists(configuredPath))
            {
                return configuredPath;
            }
            throw new FileNotFoundException($"أداة PostgreSQL غير موجودة في المسار المهيأ: {toolName}.", configuredPath);
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var standardInstallPath = FindStandardWindowsInstallPath(executableName);
        if (standardInstallPath is not null)
        {
            return standardInstallPath;
        }

        throw new FileNotFoundException($"أداة PostgreSQL غير متوفرة عبر PATH: {toolName}.", executableName);
    }

    private static string? FindStandardWindowsInstallPath(string executableName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var programFilesDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetEnvironmentVariable("ProgramW6432")
        }
        .Where(directory => !string.IsNullOrWhiteSpace(directory))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var programFilesDirectory in programFilesDirectories)
        {
            var postgresRoot = Path.Combine(programFilesDirectory!, "PostgreSQL");
            if (!Directory.Exists(postgresRoot))
            {
                continue;
            }

            foreach (var versionDirectory in Directory.GetDirectories(postgresRoot)
                         .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(versionDirectory, "bin", executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static async Task<string> CopyOutputAsync(Stream source, Stream destination)
    {
        await source.CopyToAsync(destination, CancellationToken.None);
        return string.Empty;
    }

    private static async Task TerminateProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task DrainAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
        }
    }

    private static async ValueTask DisposeOutputAsync(FileStream? output)
    {
        if (output is not null)
        {
            await output.DisposeAsync();
        }
    }
}

public sealed class LocalPostgresToolDiagnostics : IPostgresToolDiagnostics
{
    private readonly string _logFile;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LocalPostgresToolDiagnostics(string logFile)
    {
        _logFile = Path.GetFullPath(logFile);
    }

    public async Task RecordFailureAsync(string toolName, string technicalError, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_logFile)
                ?? throw new InvalidOperationException("مسار السجل التقني غير صالح.");
            Directory.CreateDirectory(directory);
            var normalized = technicalError.Replace("\r", " ").Replace("\n", " ").Trim();
            var line = $"{DateTimeOffset.UtcNow:O}\t{toolName}\t{normalized}{Environment.NewLine}";
            await File.AppendAllTextAsync(_logFile, line, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}

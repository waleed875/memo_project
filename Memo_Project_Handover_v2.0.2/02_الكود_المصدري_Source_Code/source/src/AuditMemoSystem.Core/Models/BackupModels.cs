namespace AuditMemoSystem.Core.Models;

public enum BackupOperationStatus
{
    RUNNING,
    SUCCESS,
    FAILED,
    CANCELLED
}

public sealed class BackupHistoryOperation
{
    public Guid Id { get; set; }
    public string BackupType { get; set; } = "";
    public string? SourceProfileName { get; set; }
    public string? TargetProfileName { get; set; }
    public string? FilePath { get; set; }
    public Guid CreatedBy { get; set; }
}

public sealed record PostgresToolCommand(
    string ToolName,
    IReadOnlyList<string> Arguments,
    string Password,
    string? OutputFile = null);

public sealed record PostgresToolResult(string StandardOutput);

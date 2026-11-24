using AuditMemoSystem.Core.Enums;

namespace AuditMemoSystem.Core.Models;

public sealed class AppConfig
{
    public AppSection App { get; set; } = new();
    public DatabaseSection Database { get; set; } = new();
    public StorageSection Storage { get; set; } = new();
    public BackupRecoverySection BackupRecovery { get; set; } = new();
}

public sealed class BackupRecoverySection
{
    public string PostgreSqlToolsDirectory { get; set; } = "";
    public int ToolTimeoutSeconds { get; set; } = 300;
    public string ExternalEncryptedBackupPath { get; set; } = "";
    public int? RpoHours { get; set; }
    public int? RtoMinutes { get; set; }
    public int? RetentionDays { get; set; }
    public List<DatabaseEndpointModel> AllowedRestoreTargets { get; set; } = [];
}

public sealed class DatabaseEndpointModel
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "";
}

public sealed class AppSection
{
    public string Name { get; set; } = "AuditMemo System";
    public string DefaultLanguage { get; set; } = "ar";
    public string Direction { get; set; } = "RTL";
    /// <summary>SHA-256 of a one-time deployment token; the token itself is supplied through the environment.</summary>
    public string InitialSetupTokenSha256 { get; set; } = "";
}

public sealed class WebDashboardSettingsModel
{
    public string? InteractiveDashboardUrl { get; set; }
    public string? InteractiveDashboardDisplayUrl { get; set; }
}
public sealed class UpdateServiceSettingsModel
{
    public string? ServiceUrl { get; set; }
    public string? PublicKeyPem { get; set; }
}
public sealed class DatabaseSection
{
    public string ActiveProfile { get; set; } = "Server";
    public List<ConnectionProfileModel> Profiles { get; set; } = [];
}

public sealed class StorageSection
{
    public string ImagesPath { get; set; } = "Data/Images";
    public string BackupsPath { get; set; } = "Data/Backups";
    public string ExportsPath { get; set; } = "Data/Exports";
}

public sealed class ConnectionProfileModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public ConnectionProfileType Type { get; set; } = ConnectionProfileType.SERVER;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "audit_memo";
    public string DatabaseName
    {
        get => Database;
        set => Database = value;
    }
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "";

    /// <summary>
    /// وضع تشفير الاتصال: "Disable" (افتراضي، شبكة داخلية موثوقة) أو "Require" (تشفير إلزامي
    /// مع تحقق النظام من شهادة الخادم — الشهادة الذاتية تحتاج تثبيت CA في مخزن النظام).
    /// أي قيمة غير معروفة تعود إلى "Disable" بأمان.
    /// </summary>
    public string SslMode { get; set; } = "Disable";

    public bool IsActive { get; set; }
}

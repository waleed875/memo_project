namespace MemoDashboard.Backend.Models;

public sealed class AppSettings
{
    public string? Host { get; set; }

    public int Port { get; set; } = 5432;

    public string? Database { get; set; }

    public string? Username { get; set; }

    public string? OrganizationName { get; set; }

    public string? SubOrganizationName { get; set; }

    public string? EncryptedPassword { get; set; }

    /// <summary>وضع تشفير اتصال القاعدة: "Disable" افتراضياً أو "Require".</summary>
    public string SslMode { get; set; } = "Disable";

    public int DashboardVersion { get; set; } = 1;
}

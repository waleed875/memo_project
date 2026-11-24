namespace MemoDashboard.Backend.Models;

public sealed class SetupRequest
{
    public string? Host { get; set; }

    public int Port { get; set; } = 5432;

    public string? Database { get; set; }

    public string? Username { get; set; }

    public string? OrganizationName { get; set; }

    public string? SubOrganizationName { get; set; }

    public string? Password { get; set; }

    public string? SslMode { get; set; }

    public int? DashboardVersion { get; set; }

    public bool ApplyMigrations { get; set; }
}

public sealed class TestConnectionResult
{
    public bool Success { get; set; }

    public string? Message { get; set; }

    public string? DatabaseVersion { get; set; }

    public string? ApplicationVersion { get; set; }

    public bool RequiresUpgrade { get; set; }

    public bool DatabaseIsNewer { get; set; }

    public bool IsCompatible { get; set; }
}

public sealed class SetupStatus
{
    public bool IsConfigured { get; set; }
}

public sealed class SetupConfig
{
    public string? Host { get; set; }

    public int Port { get; set; }

    public string? Database { get; set; }

    public string? Username { get; set; }

    public string? OrganizationName { get; set; }

    public string? SubOrganizationName { get; set; }


    public int DashboardVersion { get; set; } = 1;

    public string SslMode { get; set; } = "Disable";
}
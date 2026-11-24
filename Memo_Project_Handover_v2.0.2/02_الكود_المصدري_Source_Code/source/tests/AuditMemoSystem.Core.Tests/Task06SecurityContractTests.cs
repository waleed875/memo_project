using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class Task06SecurityContractTests
{
    [Fact]
    public void SecurityRolesDefineSeparateCredentialLanesWithoutEmbeddedPasswords()
    {
        var roles = File.ReadAllText(FindRepositoryFile("scripts/security_roles.sql"));
        var reader = File.ReadAllText(FindRepositoryFile("src/MemoDashboard.Web/deploy/create_db_reader_user.sql"));
        var webAuth = File.ReadAllText(FindRepositoryFile("src/MemoDashboard.Web/Backend/Services/WebAdminAuthService.cs"));

        foreach (var role in new[]
        {
            "audit_memo_dashboard_reader",
            "audit_memo_update_writer",
            "audit_memo_runtime",
            "audit_memo_migrator",
            "audit_memo_backup"
        })
        {
            Assert.Contains(role, roles, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(@"(?im)\bPASSWORD\s+'", roles);
        Assert.DoesNotMatch(@"(?im)\bPASSWORD\s+'", reader);
        Assert.DoesNotContain("GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES", roles, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GRANT SELECT, INSERT ON TABLE audit.logs", roles, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REVOKE UPDATE, DELETE, TRUNCATE", roles, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("updates.releases", roles, StringComparison.Ordinal);
        Assert.DoesNotContain("GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA auth, core, audit, reporting, system TO audit_memo_runtime", roles, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_updateOptions?.ConnectionString", webAuth, StringComparison.Ordinal);
        Assert.DoesNotContain("var connectionString = _configuration.BuildConnectionString();", webAuth[webAuth.IndexOf("private async Task WriteAuditAsync", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisioningDocumentationKeepsTlsAndCutoverBoundariesExplicit()
    {
        var security = File.ReadAllText(FindRepositoryFile("docs/8_SECURITY_PROVISIONING_AND_UPDATE_KEYS.md"));
        var runbook = File.ReadAllText(FindRepositoryFile("docs/9_DATABASE_UPGRADE_RUNBOOK.md"));

        Assert.Contains("SslMode=Disable", security, StringComparison.Ordinal);
        Assert.DoesNotContain("VerifyFull", security, StringComparison.Ordinal);
        Assert.DoesNotContain("Trust Server Certificate", security, StringComparison.Ordinal);
        Assert.Contains("خروج آخر عميل 1.0.5", security, StringComparison.Ordinal);
        Assert.Contains("UPDATE_CONNECTION_STRING", security, StringComparison.Ordinal);
        Assert.Contains("pg_restore --list", runbook, StringComparison.Ordinal);
        Assert.Contains("UPDATE/DELETE", runbook, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"تعذر تحديد ملف الاختبار: {relativePath}");
    }
}

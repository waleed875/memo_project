using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class Task12IndicatorDefaultsAndAdminContractTests
{
    [Fact]
    public void TypeFieldAdministrationIsAdminOnlyPreviewedAuditedAndSoftDeletes()
    {
        var service = File.ReadAllText(FindRepositoryFile("src/AuditMemoSystem.Data/Repositories/DataServices.cs"));
        var contract = File.ReadAllText(FindRepositoryFile("src/AuditMemoSystem.Core/Services/ServiceInterfaces.cs"));

        Assert.Contains("PreviewFieldDefinitionAsync", contract, StringComparison.Ordinal);
        Assert.Contains("SaveFieldDefinitionAsync", contract, StringComparison.Ordinal);
        Assert.Contains("DeactivateFieldDefinitionAsync", contract, StringComparison.Ordinal);
        Assert.Contains("EnsureRoleAsync(Factory, requesterId, [UserRole.ADMIN]", service, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", service, StringComparison.Ordinal);
        Assert.Contains("old_value", service, StringComparison.Ordinal);
        Assert.Contains("new_value", service, StringComparison.Ordinal);
        Assert.Contains("MissingOrUnreviewedCount", service, StringComparison.Ordinal);
        Assert.Contains("set is_active = false", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from core.note_type_fields", service, StringComparison.OrdinalIgnoreCase);
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

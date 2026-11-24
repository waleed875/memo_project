using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class Task13IncompleteReviewContractTests
{
    [Fact]
    public void QueueModelsDefaultToServerPagedUnreviewedWork()
    {
        var filter = new IncompleteIndicatorFilterModel();
        var item = new IncompleteIndicatorQueueItemModel();

        Assert.Equal(1, filter.PageNumber);
        Assert.Equal(100, filter.PageSize);
        Assert.Equal(IndicatorValue.Unreviewed, item.CurrentValue);
        Assert.Null(filter.CompanyId);
        Assert.Null(filter.FieldId);
    }

    [Fact]
    public void EveryReviewedValueOneThroughSixRemainsAlegalChoice()
    {
        var legalValues = Enum.GetValues<IndicatorValue>()
            .Where(value => value is >= IndicatorValue.Correct and <= IndicatorValue.NotApplicable)
            .Select(value => (short)value)
            .ToArray();

        Assert.Equal(new short[] { 1, 2, 3, 4, 5, 6 }, legalValues);
    }

    [Fact]
    public void QueueServiceUsesMissingRowsZeroValuesStablePagingAndAllowedRoles()
    {
        var service = File.ReadAllText(FindRepositoryFile("src/AuditMemoSystem.Data/Repositories/DataServices.cs"));
        var contract = File.ReadAllText(FindRepositoryFile("src/AuditMemoSystem.Core/Services/ServiceInterfaces.cs"));

        Assert.Contains("SearchIncompleteIndicatorsAsync", contract, StringComparison.Ordinal);
        Assert.Contains("CompleteUnreviewedFieldAsync", contract, StringComparison.Ordinal);
        Assert.Contains("string? reason", contract, StringComparison.Ordinal);
        Assert.Contains("EnsureRoleAsync(Factory, requesterId, [UserRole.REVIEWER, UserRole.ADMIN]", service, StringComparison.Ordinal);
        Assert.Contains("nfv.id is null or nfv.field_value = 0", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("limit @page_size offset @offset", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("join core.note_type_fields ntf", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("join core.note_fields nf", service, StringComparison.OrdinalIgnoreCase);
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

using MemoDashboard.Backend.Models;
using MemoDashboard.Backend.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoDashboard.Backend.Tests;

public sealed class ReviewQueueContractTests : IDisposable
{
    private readonly string _tempRoot;

    public ReviewQueueContractTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "memo-dash-review-queue-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    private DashboardQueryService CreateService()
    {
        var paths = new DashboardDataPaths(
            _tempRoot,
            Path.Combine(_tempRoot, "keys"),
            Path.Combine(_tempRoot, "App_Data", "app_settings.json"));
        var configuration = new ConfigurationService(
            paths,
            new EphemeralDataProtectionProvider(),
            NullLogger<ConfigurationService>.Instance);
        return new DashboardQueryService(configuration, NullLogger<DashboardQueryService>.Instance);
    }

    [Fact]
    public void ReviewQueueResultCarriesPagingContract()
    {
        var resultType = typeof(ReviewQueueResult);
        Assert.NotNull(resultType.GetProperty("Items"));
        Assert.NotNull(resultType.GetProperty("Total"));
        Assert.NotNull(resultType.GetProperty("Limit"));
        Assert.NotNull(resultType.GetProperty("Offset"));

        var itemType = typeof(ReviewQueueItem);
        foreach (var name in new[]
                 {
                     "Id", "NoteNumber", "CompanyId", "CompanyName", "FactoryName",
                     "Status", "StatusLabel", "WritingDate", "ArrivalAt", "DaysInQueue", "IsLate"
                 })
        {
            Assert.NotNull(itemType.GetProperty(name));
        }
    }

    [Fact]
    public void ReviewQueueMethodIsAvailableBesideSnapshot()
    {
        var method = typeof(DashboardQueryService).GetMethod("GetReviewQueueAsync");
        Assert.NotNull(method);
        Assert.NotNull(typeof(DashboardQueryService).GetMethod("GetSnapshotAsync"));
        Assert.NotNull(typeof(DashboardQueryService).GetMethod("GetLookupsAsync"));
    }

    [Fact]
    public async Task ReviewQueueRejectsInvertedPeriodBeforeTouchingDatabase()
    {
        var service = CreateService();
        var filter = new FilterQuery
        {
            From = new DateTime(2026, 9, 7),
            To = new DateTime(2026, 9, 1),
            DateBasis = "WritingDate"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetReviewQueueAsync(filter));
    }
}

using MemoDashboard.Backend.Models;
using MemoDashboard.Backend.Services;
using Xunit;

namespace MemoDashboard.Backend.Tests;

public sealed class DashboardContractTests
{
    [Fact]
    public void TypedViewsPreserveStableDimensionsAndSharedValues()
    {
        var companies = Enumerable.Range(1, 8)
            .Select(index => new CompanyTopItem
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"),
                Name = $"شركة {index}",
                NoteCount = index == 8 ? 0 : index
            })
            .ToList();

        var snapshot = new DashboardSnapshot
        {
            Summary = new DashboardSummary { TotalNotes = 7, ActiveCompaniesTotal = 8, CompaniesWithNotes = 7 },
            Companies = companies,
            MonthlyActivity = new List<MonthlyActivityItem>
            {
                new() { Month = "2026-08", MonthLabel = "أغسطس 2026", Count = 7 }
            },
            StatusDistribution = new List<StatusDistributionItem>
            {
                new() { Status = "APPROVED", StatusLabel = "معتمدة", Count = 2 }
            },
            EffectiveFilters = new DashboardEffectiveFilters
            {
                From = new DateTime(2026, 8, 1),
                To = new DateTime(2026, 8, 31),
                CompanyId = companies[0].Id
            }
        };

        snapshot.Views = DashboardContractBuilder.Build(snapshot);

        Assert.Equal("available", snapshot.Views.ExecutiveSummary.Availability.State);
        Assert.True(snapshot.Views.ExecutiveSummary.Availability.HasRows);
        Assert.Equal(8, snapshot.Views.ExecutiveSummary.CompanyComparison.Count);
        Assert.Contains(snapshot.Views.ExecutiveSummary.CompanyComparison, company => company.NoteCount == 0);
        Assert.Equal("أغسطس 2026", snapshot.Views.ExecutiveSummary.MonthlyTrend.Single().MonthLabel);
        Assert.Equal(snapshot.Summary.TotalNotes, snapshot.Views.OperationsSummary.Summary.TotalNotes);
        Assert.NotNull(snapshot.Views.CompanySummary);
        Assert.Equal(companies[0].Id, snapshot.Views.Context.Scope.CompanyId);
        Assert.Equal(new DateTime(2026, 8, 1), snapshot.Views.Context.Period.From);
        Assert.Equal(new DateTime(2026, 8, 31), snapshot.Views.Context.Period.To);
    }

    [Fact]
    public void NoDataIsExplicitAndDoesNotBecomeAnAvailableZero()
    {
        var snapshot = new DashboardSnapshot
        {
            Summary = new DashboardSummary { TotalNotes = 0 },
            EffectiveFilters = new DashboardEffectiveFilters
            {
                To = new DateTime(2026, 8, 31)
            }
        };

        snapshot.Views = DashboardContractBuilder.Build(snapshot);

        Assert.Equal("no-data", snapshot.Views.ExecutiveSummary.Availability.State);
        Assert.False(snapshot.Views.ExecutiveSummary.Availability.HasRows);
        Assert.Contains("لا توجد", snapshot.Views.ExecutiveSummary.Availability.Explanation);
    }

    [Theory]
    [InlineData(0, "لم تتم المراجعة")]
    [InlineData(1, "صحيح")]
    [InlineData(2, "غير صحيح")]
    [InlineData(6, "لا ينطبق")]
    public void IndicatorStatesExposeArabicSemanticLabels(int code, string expected)
    {
        Assert.Equal(expected, StatusTranslations.IndicatorStateLabel(code));
    }
}
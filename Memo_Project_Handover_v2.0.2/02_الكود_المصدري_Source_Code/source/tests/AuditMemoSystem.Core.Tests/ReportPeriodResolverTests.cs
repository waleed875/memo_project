using AuditMemoSystem.Core.Services;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class ReportPeriodResolverTests
{
    [Fact]
    public void ResolvesLeapYearMonthAndYearBoundaries()
    {
        ReportPeriodRange february = ReportPeriodResolver.Resolve(
            new ReportPeriodSelection(ReportPeriodMode.Month, 2024, Month: 2));
        Assert.Equal(new DateOnly(2024, 2, 1), february.From);
        Assert.Equal(new DateOnly(2024, 2, 29), february.To);

        ReportPeriodRange year = ReportPeriodResolver.Resolve(
            new ReportPeriodSelection(ReportPeriodMode.Year, 2025));
        Assert.Equal(new DateOnly(2025, 1, 1), year.From);
        Assert.Equal(new DateOnly(2025, 12, 31), year.To);
    }

    [Fact]
    public void ResolvesQuarterHalfYearAndCustomRange()
    {
        Assert.Equal(
            (new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)),
            (ReportPeriodResolver.Resolve(new ReportPeriodSelection(ReportPeriodMode.Quarter, 2026, Quarter: 2)).From,
             ReportPeriodResolver.Resolve(new ReportPeriodSelection(ReportPeriodMode.Quarter, 2026, Quarter: 2)).To));

        ReportPeriodRange half = ReportPeriodResolver.Resolve(
            new ReportPeriodSelection(ReportPeriodMode.HalfYear, 2026, Half: 2));
        Assert.Equal(new DateOnly(2026, 7, 1), half.From);
        Assert.Equal(new DateOnly(2026, 12, 31), half.To);

        ReportPeriodRange custom = ReportPeriodResolver.Resolve(
            new ReportPeriodSelection(
                ReportPeriodMode.CustomRange,
                2026,
                From: new DateOnly(2026, 8, 17),
                To: new DateOnly(2026, 8, 21)));
        Assert.Equal("فترة مخصصة: 2026/08/17 - 2026/08/21", custom.Label);
    }

    [Fact]
    public void RejectsInvertedOrIncompleteRanges()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ReportPeriodResolver.Resolve(new ReportPeriodSelection(
                ReportPeriodMode.CustomRange,
                2026,
                From: new DateOnly(2026, 8, 22),
                To: new DateOnly(2026, 8, 17))));

        Assert.Throws<InvalidOperationException>(() =>
            ReportPeriodResolver.Resolve(new ReportPeriodSelection(ReportPeriodMode.Month, 2026)));
    }

    [Fact]
    public void NormalizesLegacyOpenFromToOperationalToday()
    {
        var today = ReportPeriodResolver.OperationalToday();
        var normalized = ReportPeriodResolver.NormalizeFilter(DateOnly.MinValue, today);

        Assert.Null(normalized.From);
        Assert.Equal(today, normalized.To);
    }

    [Fact]
    public void NormalizesLegacyMaxValueToOperationalToday()
    {
        var today = ReportPeriodResolver.OperationalToday();
        var normalized = ReportPeriodResolver.NormalizeFilter(DateOnly.MinValue, DateOnly.MaxValue);

        Assert.Null(normalized.From);
        Assert.Equal(today, normalized.To);
        Assert.NotEqual(DateOnly.MaxValue, normalized.To);
    }
    [Fact]
    public void RejectsInvertedNormalizedRange()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ReportPeriodResolver.NormalizeFilter(new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 17)));
    }
}

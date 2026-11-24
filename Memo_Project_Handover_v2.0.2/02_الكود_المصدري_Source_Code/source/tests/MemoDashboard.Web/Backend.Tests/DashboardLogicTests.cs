using MemoDashboard.Backend.Services;
using Xunit;

namespace MemoDashboard.Backend.Tests;

public class DashboardLogicTests
{
    private static DateTime D(string value) => DateTime.Parse(value);

    [Theory]
    [InlineData("2026-08-01", "2026-08-01")]
    [InlineData("2026-08-01", "2026-08-05")]
    [InlineData("2026-08-01", "2026-08-06")]
    public void ArrivalWithinFiveDaysIsNotLate(string writing, string arrival)
    {
        Assert.False(DelayCalculator.IsLate(D(writing), D(arrival)));
        Assert.True(DelayCalculator.IsOnTime(D(writing), D(arrival)));
    }

    [Theory]
    [InlineData("2026-08-01", "2026-08-07")]
    [InlineData("2026-01-01", "2026-12-31")]
    public void MoreThanFiveDaysIsLate(string writing, string arrival)
    {
        Assert.True(DelayCalculator.IsLate(D(writing), D(arrival)));
        Assert.False(DelayCalculator.IsOnTime(D(writing), D(arrival)));
    }

    [Fact]
    public void NullWritingDateIsNeverLate()
    {
        Assert.False(DelayCalculator.IsLate(null, D("2026-08-30")));
        Assert.False(DelayCalculator.IsOnTime(null, D("2026-08-30")));
        Assert.False(DelayCalculator.IsSameMonth(null, D("2026-08-30")));
        Assert.Equal(DelayClass.Unknown, DelayCalculator.Classify(null, D("2026-08-30")));
        Assert.Equal(0, DelayCalculator.LateDays(null, D("2026-08-30")));
    }

    [Fact]
    public void LateWithinSameMonthIsClassifiedSameMonth()
    {
        var writing = D("2026-08-01");
        var arrival = D("2026-08-10");
        Assert.True(DelayCalculator.IsLate(writing, arrival));
        Assert.True(DelayCalculator.IsSameMonth(writing, arrival));
        Assert.Equal(DelayClass.LateSameMonth, DelayCalculator.Classify(writing, arrival));
    }

    [Fact]
    public void LateAcrossMonthBoundaryIsClassifiedCrossMonth()
    {
        var writing = D("2026-08-28");
        var arrival = D("2026-09-04");
        Assert.True(DelayCalculator.IsLate(writing, arrival));
        Assert.False(DelayCalculator.IsSameMonth(writing, arrival));
        Assert.Equal(DelayClass.LateCrossMonth, DelayCalculator.Classify(writing, arrival));
    }

    [Fact]
    public void LateAcrossYearBoundaryIsClassifiedCrossMonth()
    {
        var writing = D("2026-12-28");
        var arrival = D("2027-01-04");
        Assert.True(DelayCalculator.IsLate(writing, arrival));
        Assert.Equal(DelayClass.LateCrossMonth, DelayCalculator.Classify(writing, arrival));
    }

    [Fact]
    public void ExactlyFiveDaysIsOnTime()
    {
        var writing = D("2026-08-01");
        var arrival = D("2026-08-06");
        Assert.Equal(DelayClass.OnTime, DelayCalculator.Classify(writing, arrival));
        Assert.True(DelayCalculator.IsOnTime(writing, arrival));
    }

    [Fact]
    public void LateDaysCalculatesDuration()
    {
        var writing = D("2026-08-01");
        var arrival = D("2026-08-10");
        Assert.Equal(9, DelayCalculator.LateDays(writing, arrival));
        Assert.Equal(0, DelayCalculator.LateDays(writing, D("2026-08-05")));
    }

    [Fact]
    public void ArrivalBeforeWritingDateIsInvalidDateOrder()
    {
        var writing = D("2026-08-10");
        var arrival = D("2026-08-01");
        Assert.False(DelayCalculator.IsLate(writing, arrival));
        Assert.False(DelayCalculator.IsOnTime(writing, arrival));
        Assert.True(DelayCalculator.IsInvalidDateOrder(writing, arrival));
        Assert.Equal(DelayClass.InvalidDateOrder, DelayCalculator.Classify(writing, arrival));
        Assert.Equal(0, DelayCalculator.LateDays(writing, arrival));
    }

    [Fact]
    public void EmptyWritingAndArrivalIsNotInvalidDateOrder()
    {
        Assert.False(DelayCalculator.IsInvalidDateOrder(null, D("2026-08-01")));
        Assert.False(DelayCalculator.IsInvalidDateOrder(D("2026-08-01"), D("2026-08-01")));
    }

    [Fact]
    public void MonthStartNormalizesToFirstDay()
    {
        Assert.Equal(D("2026-08-01"), DelayCalculator.MonthStart(D("2026-08-31")));
        Assert.Equal(D("2026-01-01"), DelayCalculator.MonthStart(D("2026-01-15")));
        Assert.Equal(D("2026-12-01"), DelayCalculator.MonthStart(D("2026-12-25")));
    }

    [Theory]
    [InlineData("DRAFT", "مسودة")]
    [InlineData("COMPLETE", "مكتملة")]
    [InlineData("HAS_ERRORS", "تحتوي أخطاء")]
    [InlineData("NEEDS_REVIEW", "تحتاج مراجعة")]
    [InlineData("UNDER_REVIEW", "قيد التدقيق")]
    [InlineData("RETURNED_FOR_CORRECTION", "معادة للتصحيح")]
    [InlineData("APPROVED", "معتمدة")]
    public void StatusTranslationsMapToArabic(string status, string expected)
    {
        Assert.Equal(expected, StatusTranslations.StatusLabel(status));
    }

    [Fact]
    public void UnknownStatusFallsBackToArabicDefault()
    {
        Assert.Equal("غير معروف", StatusTranslations.StatusLabel(null));
        Assert.Equal("غير معروف", StatusTranslations.StatusLabel("SOMETHING_ELSE"));
    }

    [Fact]
    public void ClassificationTranslationsMapToArabic()
    {
        Assert.Equal("إعادة إرسال من الشركة", StatusTranslations.ClassificationLabel("COMPANY_RESEND"));
        Assert.Equal("أرقام مكررة", StatusTranslations.ClassificationLabel("COMPANY_DUPLICATE_NUMBER"));
        Assert.Equal("غير مصنف", StatusTranslations.ClassificationLabel(null));
    }

    [Fact]
    public void MonthNamesAreArabic()
    {
        Assert.Equal("يناير", StatusTranslations.MonthName(1));
        Assert.Equal("ديسمبر", StatusTranslations.MonthName(12));
        Assert.Equal(string.Empty, StatusTranslations.MonthName(0));
        Assert.Equal(string.Empty, StatusTranslations.MonthName(13));
    }
}

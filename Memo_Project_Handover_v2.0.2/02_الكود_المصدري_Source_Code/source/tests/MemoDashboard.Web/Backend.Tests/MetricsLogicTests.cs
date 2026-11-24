using MemoDashboard.Backend.Services;
using Xunit;

namespace MemoDashboard.Backend.Tests;

public class MetricsLogicTests
{
    [Fact]
    public void SlaRateComputedFromOnTimeOverEligible()
    {
        var sla = MetricsCalculator.Sla(6, 4, 2, 0);
        Assert.Equal(6, sla.EligibleNotes);
        Assert.Equal(4, sla.OnTimeNotes);
        Assert.Equal(2, sla.LateNotes);
        Assert.Equal(0, sla.InvalidDateOrderNotes);
        Assert.Equal(66.67, sla.OnTimeRate);
    }

    [Fact]
    public void SlaRateIsNullWhenNoEligibleNotes()
    {
        var sla = MetricsCalculator.Sla(0, 0, 0, 1);
        Assert.Null(sla.OnTimeRate);
    }

    [Fact]
    public void InvalidDateOrderNotesAreCountedSeparately()
    {
        var sla = MetricsCalculator.Sla(5, 4, 1, 3);
        Assert.Equal(3, sla.InvalidDateOrderNotes);
        Assert.Equal(80.0, sla.OnTimeRate);
    }

    [Fact]
    public void AccuracyIsCorrectOverAssessedOnly()
    {
        var (accuracy, error) = MetricsCalculator.QualityRates(13, 8, 4);
        Assert.Equal(61.54, accuracy);
        Assert.Equal(30.77, error);
    }

    [Fact]
    public void QualityRatesAreNullWhenNoAssessedIndicators()
    {
        var (accuracy, error) = MetricsCalculator.QualityRates(0, 0, 0);
        Assert.Null(accuracy);
        Assert.Null(error);
    }

    [Fact]
    public void QualityRatesHandlePartialSamples()
    {
        var (accuracy, error) = MetricsCalculator.QualityRates(10, 0, 0);
        Assert.Equal(0.0, accuracy);
        Assert.Equal(0.0, error);
    }

    [Fact]
    public void AccuracyUsesReviewedApplicableDenominator()
    {
        var (accuracy, error) = MetricsCalculator.QualityRates(7, 4, 3);
        Assert.Equal(57.14, accuracy);
        Assert.Equal(42.86, error);
    }

    [Fact]
    public void RateIsNullWhenDenominatorIsZero()
    {
        Assert.Null(MetricsCalculator.Rate(5, 0));
        Assert.Null(MetricsCalculator.Rate(0, 0));
        Assert.Equal(50.0, MetricsCalculator.Rate(1, 2));
    }
}

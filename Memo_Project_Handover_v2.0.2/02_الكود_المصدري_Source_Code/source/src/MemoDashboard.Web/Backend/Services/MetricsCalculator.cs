using MemoDashboard.Backend.Models;

namespace MemoDashboard.Backend.Services;

public static class MetricsCalculator
{
    public static double? Rate(long numerator, long denominator)
    {
        if (denominator <= 0)
        {
            return null;
        }

        return Math.Round((double)numerator / denominator * 100, 2, MidpointRounding.AwayFromZero);
    }

    public static SlaStats Sla(long eligible, long onTime, long late, long invalidDateOrder)
    {
        return new SlaStats
        {
            EligibleNotes = eligible,
            OnTimeNotes = onTime,
            LateNotes = late,
            OnTimeRate = Rate(onTime, eligible),
            InvalidDateOrderNotes = invalidDateOrder
        };
    }

    public static (double? Accuracy, double? Error) QualityRates(long sampleSize, long correctCount, long errorCount)
    {
        return (Rate(correctCount, sampleSize), Rate(errorCount, sampleSize));
    }
}

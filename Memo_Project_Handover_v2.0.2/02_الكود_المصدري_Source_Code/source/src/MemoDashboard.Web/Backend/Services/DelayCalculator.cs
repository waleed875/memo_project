namespace MemoDashboard.Backend.Services;

public enum DelayClass
{
    Unknown,
    InvalidDateOrder,
    OnTime,
    LateSameMonth,
    LateCrossMonth
}

public static class DelayCalculator
{
    public static DateTime MonthStart(DateTime value)
    {
        return new DateTime(value.Year, value.Month, 1);
    }

    public static bool IsLate(DateTime? writingDate, DateTime arrivalAt)
    {
        if (writingDate is null)
        {
            return false;
        }

        return (arrivalAt - writingDate.Value).TotalDays > 5;
    }

    public static bool IsOnTime(DateTime? writingDate, DateTime arrivalAt)
    {
        if (writingDate is null)
        {
            return false;
        }

        var days = (arrivalAt - writingDate.Value).TotalDays;
        return days >= 0 && days <= 5;
    }

    public static bool IsSameMonth(DateTime? writingDate, DateTime arrivalAt)
    {
        if (writingDate is null)
        {
            return false;
        }

        return MonthStart(writingDate.Value) == MonthStart(arrivalAt);
    }

    public static DelayClass Classify(DateTime? writingDate, DateTime arrivalAt)
    {
        if (writingDate is null)
        {
            return DelayClass.Unknown;
        }

        if (arrivalAt < writingDate.Value)
        {
            return DelayClass.InvalidDateOrder;
        }

        if (!IsLate(writingDate, arrivalAt))
        {
            return DelayClass.OnTime;
        }

        return IsSameMonth(writingDate, arrivalAt)
            ? DelayClass.LateSameMonth
            : DelayClass.LateCrossMonth;
    }

    public static bool IsInvalidDateOrder(DateTime? writingDate, DateTime arrivalAt)
    {
        return writingDate is not null && arrivalAt < writingDate.Value;
    }

    public static double LateDays(DateTime? writingDate, DateTime arrivalAt)
    {
        if (writingDate is null || !IsLate(writingDate, arrivalAt))
        {
            return 0;
        }

        return Math.Round((arrivalAt - writingDate.Value).TotalDays, 1);
    }
}

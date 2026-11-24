using AuditMemoSystem.Core.Models;

namespace AuditMemoSystem.Core.Services;

public enum ReportPeriodMode
{
    Month,
    CustomRange,
    Quarter,
    HalfYear,
    Year
}

public enum ReportDateBasis
{
    WritingDate,
    ArrivalDate
}

public sealed record BusinessDateScope(
    DateOnly From,
    DateOnly To,
    ReportDateBasis DateBasis = ReportDateBasis.WritingDate,
    DateOnly? ArrivalCutoffDate = null)
{
    public void Validate()
    {
        if (From > To)
        {
            throw new InvalidOperationException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");
        }
    }

    public string BasisLabel => DateBasis == ReportDateBasis.WritingDate ? "حسب تاريخ الكتابة" : "حسب تاريخ الوصول";
}

public sealed record ReportPeriodSelection(
    ReportPeriodMode Mode,
    int Year,
    int? Month = null,
    int? Quarter = null,
    int? Half = null,
    DateOnly? From = null,
    DateOnly? To = null);

public sealed record ReportPeriodRange(DateOnly From, DateOnly To, string Label)
{
    public void Validate()
    {
        if (From > To) throw new InvalidOperationException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");
    }
}

public static class ReportPeriodResolver
{
    /// <summary>
    /// Returns the operational date used by every dashboard/report surface.
    /// The application is deployed in Syria; keep the fallback explicit so a
    /// host without the IANA time-zone database still uses the same day.
    /// </summary>
    public static DateOnly OperationalToday()
    {
        var utcNow = DateTime.UtcNow;
        foreach (var timeZoneId in new[] { "Asia/Damascus", "Syria Standard Time" })
        {
            try
            {
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                    utcNow,
                    TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)));
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the platform-specific identifier below.
            }
            catch (InvalidTimeZoneException)
            {
                // Try the platform-specific identifier below.
            }
        }

        return DateOnly.FromDateTime(utcNow.AddHours(3));
    }

    /// <summary>
    /// Normalizes the legacy desktop sentinels without allowing an open-ended
    /// upper bound to include future arrivals. DateOnly.MinValue means "from
    /// the first available row" and DateOnly.MaxValue is a legacy upper-bound
    /// sentinel for today in Damascus.
    /// </summary>
    public static (DateOnly? From, DateOnly To) NormalizeFilter(DateOnly from, DateOnly to)
    {
        DateOnly? effectiveFrom = from == DateOnly.MinValue ? null : from;
        var effectiveTo = to == DateOnly.MaxValue ? OperationalToday() : to;
        if (effectiveFrom.HasValue && effectiveFrom.Value > effectiveTo)
        {
            throw new InvalidOperationException("تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.");
        }

        return (effectiveFrom, effectiveTo);
    }

    public static BusinessDateScope CreateBusinessScope(
        DateOnly from,
        DateOnly to,
        ReportDateBasis dateBasis = ReportDateBasis.WritingDate,
        DateOnly? arrivalCutoffDate = null)
    {
        var scope = new BusinessDateScope(from, to, dateBasis, arrivalCutoffDate);
        scope.Validate();
        return scope;
    }

    public static string DefaultScopeLabel(DateOnly to, ReportDateBasis dateBasis = ReportDateBasis.WritingDate) =>
        $"حتى {to:yyyy/MM/dd} — {(dateBasis == ReportDateBasis.WritingDate ? "حسب تاريخ الكتابة" : "حسب تاريخ الوصول")}";
    public static ReportPeriodRange Resolve(ReportPeriodSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ReportPeriodRange range = selection.Mode switch
        {
            ReportPeriodMode.Month => Month(selection.Year, Required(selection.Month, "الشهر")),
            ReportPeriodMode.Quarter => Quarter(selection.Year, Required(selection.Quarter, "الربع")),
            ReportPeriodMode.HalfYear => HalfYear(selection.Year, Required(selection.Half, "النصف")),
            ReportPeriodMode.Year => Year(selection.Year),
            ReportPeriodMode.CustomRange => Custom(selection.From, selection.To),
            _ => throw new ArgumentOutOfRangeException(nameof(selection.Mode))
        };
        range.Validate();
        return range;
    }

    private static ReportPeriodRange Month(int year, int month)
    {
        ValidateYear(year);
        if (month is < 1 or > 12) throw new InvalidOperationException("الشهر غير صالح.");
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        return new(from, to, $"شهر {month:00}/{year:0000}");
    }

    private static ReportPeriodRange Quarter(int year, int quarter)
    {
        ValidateYear(year);
        if (quarter is < 1 or > 4) throw new InvalidOperationException("الربع غير صالح.");
        var month = ((quarter - 1) * 3) + 1;
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(3).AddDays(-1);
        return new(from, to, $"الربع {quarter} لعام {year:0000}");
    }

    private static ReportPeriodRange HalfYear(int year, int half)
    {
        ValidateYear(year);
        if (half is < 1 or > 2) throw new InvalidOperationException("النصف السنوي غير صالح.");
        var from = new DateOnly(year, half == 1 ? 1 : 7, 1);
        var to = from.AddMonths(6).AddDays(-1);
        return new(from, to, $"النصف {(half == 1 ? "الأول" : "الثاني")} لعام {year:0000}");
    }

    private static ReportPeriodRange Year(int year)
    {
        ValidateYear(year);
        return new(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), $"العام {year:0000}");
    }

    private static ReportPeriodRange Custom(DateOnly? from, DateOnly? to)
    {
        if (!from.HasValue || !to.HasValue) throw new InvalidOperationException("أدخل تاريخ البداية والنهاية.");
        return new(from.Value, to.Value, $"فترة مخصصة: {from:yyyy/MM/dd} - {to:yyyy/MM/dd}");
    }

    private static int Required(int? value, string label) =>
        value ?? throw new InvalidOperationException($"اختر {label}.");

    private static void ValidateYear(int year)
    {
        if (year is < 1 or > 9999) throw new InvalidOperationException("السنة غير صالحة.");
    }
}



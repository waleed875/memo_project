using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;

namespace AuditMemoSystem.Core.Validation;

/// <summary>قاعدة موحدة لتصنيف تأخر المذكرات في كل طبقات النظام.</summary>
public static class NoteDelayCalculator
{
    public const int AllowedDays = 5;
    public const int DefaultAllowedDelayDays = AllowedDays;

    public static NoteDelayClassification Classify(DateOnly? writingDate, DateOnly arrivalDate)
    {
        if (writingDate is null) return NoteDelayClassification.MISSING_WRITING_DATE;
        var days = arrivalDate.DayNumber - writingDate.Value.DayNumber;
        if (days < 0) return NoteDelayClassification.INVALID_DATES;
        if (days <= AllowedDays) return NoteDelayClassification.ON_TIME;
        return arrivalDate.Year == writingDate.Value.Year && arrivalDate.Month == writingDate.Value.Month
            ? NoteDelayClassification.LATE_SAME_MONTH
            : NoteDelayClassification.LATE_CROSS_MONTH;
    }

    public static int? DaysBetween(DateOnly? writingDate, DateOnly arrivalDate) =>
        writingDate is null ? null : arrivalDate.DayNumber - writingDate.Value.DayNumber;

    public static int? ExcessDays(DateOnly? writingDate, DateOnly arrivalDate)
    {
        var days = DaysBetween(writingDate, arrivalDate);
        if (days is null) return null;
        return days is > AllowedDays ? days.Value - AllowedDays : 0;
    }

    // واجهة 1.0.1 تبقى متوافقة مع الشاشات والاختبارات السابقة.
    public static NoteDelayAssessment Calculate(
        DateOnly? writingDate,
        DateOnly arrivalAt,
        int allowedDelayDays = DefaultAllowedDelayDays)
    {
        if (allowedDelayDays < 0) throw new ArgumentOutOfRangeException(nameof(allowedDelayDays));
        if (!writingDate.HasValue) return new NoteDelayAssessment(NoteDelayState.CannotCalculate, null);

        var days = arrivalAt.DayNumber - writingDate.Value.DayNumber;
        if (days < 0) return new NoteDelayAssessment(NoteDelayState.InvalidDateOrder, null);
        return days > allowedDelayDays
            ? new NoteDelayAssessment(NoteDelayState.Late, days)
            : new NoteDelayAssessment(NoteDelayState.WithinAllowedPeriod, days);
    }

    public static NoteDelayResult Describe(DateOnly? writingDate, DateOnly arrivalDate)
    {
        var classification = Classify(writingDate, arrivalDate);
        var daysBetween = DaysBetween(writingDate, arrivalDate);
        return classification switch
        {
            NoteDelayClassification.MISSING_WRITING_DATE => new(classification, false, null, null, "تاريخ كتابة المذكرة مفقود"),
            NoteDelayClassification.INVALID_DATES => new(classification, false, daysBetween, null, "تاريخ الوصول قبل تاريخ كتابة المذكرة"),
            NoteDelayClassification.ON_TIME => new(classification, false, daysBetween, 0, $"غير متأخرة — وصلت خلال {daysBetween} أيام"),
            NoteDelayClassification.LATE_SAME_MONTH => new(classification, true, daysBetween, ExcessDays(writingDate, arrivalDate), $"متأخرة ضمن الشهر — التأخير {daysBetween} أيام"),
            NoteDelayClassification.LATE_CROSS_MONTH => new(classification, true, daysBetween, ExcessDays(writingDate, arrivalDate), $"متأخرة خارج الشهر — التأخير {daysBetween} أيام"),
            _ => throw new ArgumentOutOfRangeException(nameof(classification))
        };
    }
}

public sealed record NoteDelayResult(NoteDelayClassification Classification, bool IsLate, int? DaysBetween, int? ExcessDays, string DisplayText);

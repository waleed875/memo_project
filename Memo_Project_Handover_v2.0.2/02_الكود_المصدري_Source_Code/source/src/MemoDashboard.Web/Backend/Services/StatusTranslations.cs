namespace MemoDashboard.Backend.Services;

public static class StatusTranslations
{
    private static readonly Dictionary<string, string> Statuses = new()
    {
        ["DRAFT"] = "مسودة",
        ["COMPLETE"] = "مكتملة",
        ["HAS_ERRORS"] = "تحتوي أخطاء",
        ["NEEDS_REVIEW"] = "تحتاج مراجعة",
        ["UNDER_REVIEW"] = "قيد التدقيق",
        ["RETURNED_FOR_CORRECTION"] = "معادة للتصحيح",
        ["APPROVED"] = "معتمدة"
    };

    private static readonly Dictionary<string, string> Classifications = new()
    {
        ["COMPANY_RESEND"] = "إعادة إرسال من الشركة",
        ["COMPANY_DUPLICATE_NUMBER"] = "أرقام مكررة"
    };

    private static readonly string[] Months =
    {
        "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
        "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
    };

    public static string StatusLabel(string? status)
    {
        return status is not null && Statuses.TryGetValue(status, out var label) ? label : "غير معروف";
    }

    public static string ClassificationLabel(string? classification)
    {
        return classification is not null && Classifications.TryGetValue(classification, out var label) ? label : "غير مصنف";
    }

    public static string IndicatorStateLabel(int code)
    {
        return code switch
        {
            0 => "لم تتم المراجعة",
            1 => "صحيح",
            2 => "غير صحيح",
            3 => "مفقود",
            4 => "غير واضح",
            5 => "غير مكتمل",
            6 => "لا ينطبق",
            _ => "غير معروف"
        };
    }

    public static string MonthName(int month)
    {
        return month is >= 1 and <= 12 ? Months[month - 1] : string.Empty;
    }
}

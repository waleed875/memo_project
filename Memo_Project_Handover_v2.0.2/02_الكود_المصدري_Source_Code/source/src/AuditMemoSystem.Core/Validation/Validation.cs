using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;

namespace AuditMemoSystem.Core.Validation;

public static class NoteNumberNormalizer
{
    private const string ArabicIndicDigits = "٠١٢٣٤٥٦٧٨٩";
    private const string PersianDigits = "۰۱۲۳۴۵۶۷۸۹";
    private const string LatinDigits = "0123456789";

    public static string Normalize(string? noteNumber)
    {
        if (string.IsNullOrWhiteSpace(noteNumber))
        {
            return string.Empty;
        }

        return string.Concat(noteNumber.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }

    public static bool TryParsePositive(string? noteNumber, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(noteNumber))
        {
            return false;
        }

        var normalized = noteNumber.Trim()
            .ReplaceArabicDigits(ArabicIndicDigits, LatinDigits)
            .ReplaceArabicDigits(PersianDigits, LatinDigits);
        if (normalized.Length == 0 || normalized.Any(character => character is < '0' or > '9'))
        {
            return false;
        }

        return long.TryParse(normalized, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value)
            && value > 0;
    }

    private static string ReplaceArabicDigits(this string value, string source, string target)
    {
        for (var index = 0; index < source.Length; index++)
        {
            value = value.Replace(source[index], target[index]);
        }

        return value;
    }
}

public static class FieldValueValidator
{
    public static void Validate(NoteFieldValueModel value)
    {
        if (!Enum.IsDefined(value.FieldValue))
        {
            throw new InvalidOperationException("قيمة البند غير صحيحة.");
        }
    }
}

public static class NoteValidator
{
    public static bool IsAdminExistingEdit(NoteModel? existing, UserRole requesterRole) =>
        existing is not null && requesterRole == UserRole.ADMIN;

    public static void EnsureApprovedEditAllowed(NoteModel existing, UserRole requesterRole)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (requesterRole != UserRole.ADMIN
            && existing.Status is not (NoteStatus.DRAFT or NoteStatus.RETURNED_FOR_CORRECTION))
        {
            throw new UnauthorizedAccessException("تعديل المذكرات بعد إرسالها للتدقيق متاح لمدير النظام فقط أو بعد إعادتها للتصحيح.");
        }
    }

    public static void EnsureDraftSaveAllowed(NoteModel? existing, bool saveAsDraft)
    {
        if (saveAsDraft && existing?.Status == NoteStatus.APPROVED)
        {
            throw new InvalidOperationException("لا يمكن تحويل مذكرة معتمدة إلى مسودة. استخدم الحفظ الصريح لتعديلها مع بقاء أثر الاعتماد محفوظاً.");
        }
    }

    public static void Validate(NoteModel note, IReadOnlyList<NoteTypeFieldModel> requiredFields, bool validateRequiredFields = true)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(requiredFields);
        if (note.FieldValues is null)
        {
            throw new InvalidOperationException("قيم البنود مفقودة.");
        }

        if (string.IsNullOrWhiteSpace(note.NoteNumber))
        {
            throw new InvalidOperationException("رقم المذكرة مطلوب.");
        }

        if (note.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("الشركة مطلوبة.");
        }

        if (note.NoteTypeId == Guid.Empty)
        {
            throw new InvalidOperationException("نوع المذكرة مطلوب.");
        }

        if (note.ArrivalAt == default)
        {
            throw new InvalidOperationException("تاريخ الوصول مطلوب ولا يمكن افتراض تاريخ اليوم تلقائياً.");
        }

        if (note.WritingDate.HasValue && note.ArrivalAt < note.WritingDate.Value)
        {
            throw new InvalidOperationException("لا يمكن أن يكون تاريخ كتابة المذكرة أحدث من تاريخ الوصول.");
        }

        if (note.ArrivalAt > ReportPeriodResolver.OperationalToday())
        {
            throw new InvalidOperationException("لا يمكن أن يكون تاريخ الوصول في المستقبل.");
        }

        foreach (var value in note.FieldValues)
        {
            FieldValueValidator.Validate(value);
        }

        if (!validateRequiredFields)
        {
            return;
        }

        foreach (var field in requiredFields.Where(f => f.IsRequired))
        {
            var value = note.FieldValues.FirstOrDefault(v => v.FieldId == field.FieldId);
            if (value is null)
            {
                throw new InvalidOperationException($"يجب إدخال قيمة للبند: {field.FieldNameAr}");
            }

            value.FieldName = field.FieldNameAr;
            if (value.FieldValue == IndicatorValue.Unreviewed)
            {
                throw new InvalidOperationException($"يجب مراجعة البند الإلزامي قبل الحفظ: {field.FieldNameAr}");
            }
        }
    }

    public static NoteStatus CalculateStatus(int requiredCount, IReadOnlyList<NoteFieldValueModel> values)
    {
        if (values.Count < requiredCount || values.Any(value => value.FieldValue == IndicatorValue.Unreviewed))
        {
            return NoteStatus.DRAFT;
        }

        return values.Any(value => value.FieldValue != IndicatorValue.Correct)
            ? NoteStatus.HAS_ERRORS
            : NoteStatus.COMPLETE;
    }
    public static NoteStatus CalculateStatus(IReadOnlyList<NoteTypeFieldModel> fields, IReadOnlyList<NoteFieldValueModel> values)
    {
        var activeFields = fields.Where(field => field.IsActive).ToList();
        foreach (var requiredField in activeFields.Where(field => field.IsRequired))
        {
            var value = values.FirstOrDefault(item => item.FieldId == requiredField.FieldId);
            if (value is null || value.FieldValue == IndicatorValue.Unreviewed)
            {
                return NoteStatus.DRAFT;
            }
        }
        var activeFieldIds = activeFields.Select(field => field.FieldId).ToHashSet();
        return values.Any(value => activeFieldIds.Contains(value.FieldId) && value.FieldValue is not (IndicatorValue.Correct or IndicatorValue.Unreviewed))
            ? NoteStatus.HAS_ERRORS
            : NoteStatus.COMPLETE;
    }
}

public sealed class ReadOnlyModeGuard
{
    private readonly Func<AppMode> _modeAccessor;

    public ReadOnlyModeGuard(Func<AppMode> modeAccessor)
    {
        _modeAccessor = modeAccessor;
    }

    public void EnsureWritable()
    {
        if (_modeAccessor() == AppMode.LOCAL_READONLY)
        {
            throw new InvalidOperationException("النظام يعمل على قاعدة محلية للقراءة فقط. لا يمكن تنفيذ عمليات التعديل.");
        }
    }
}

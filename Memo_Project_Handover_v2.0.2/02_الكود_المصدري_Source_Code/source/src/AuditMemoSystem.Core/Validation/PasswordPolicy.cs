namespace AuditMemoSystem.Core.Validation;

/// <summary>
/// سياسة كلمات المرور: غير فارغة وبطول أدنى 8 محارف.
/// تطبق على الكلمات الجديدة والمعدلة فقط ولا تمس المخزنة سابقاً.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    public static void EnsureValid(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("كلمة المرور مطلوبة.");
        }

        if (password.Length < MinimumLength)
        {
            throw new InvalidOperationException($"كلمة المرور قصيرة: الحد الأدنى {MinimumLength} محارف.");
        }
    }

    public static string ValidateConfirmation(string? password, string? confirmation)
    {
        EnsureValid(password);
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("كلمتا المرور غير متطابقتين.");
        }

        return password!;
    }
}

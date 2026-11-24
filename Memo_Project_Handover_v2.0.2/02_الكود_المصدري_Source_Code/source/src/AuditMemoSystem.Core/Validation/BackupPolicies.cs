using System.Globalization;
using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;

namespace AuditMemoSystem.Core.Validation;

public readonly record struct DatabaseEndpointIdentity(string Host, int Port, string Database)
{
    public static DatabaseEndpointIdentity From(ConnectionProfileModel profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Create(profile.Host, profile.Port, profile.Database);
    }

    public static DatabaseEndpointIdentity From(DatabaseEndpointModel endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return Create(endpoint.Host, endpoint.Port, endpoint.Database);
    }

    private static DatabaseEndpointIdentity Create(string host, int port, string database)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("هوية نقطة اتصال قاعدة البيانات غير مكتملة أو غير صالحة.");
        }

        var normalizedHost = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalizedHost is "localhost" or "127.0.0.1" or "::1" or "[::1]")
        {
            normalizedHost = "loopback";
        }

        return new DatabaseEndpointIdentity(
            normalizedHost,
            port,
            database.Trim().ToLowerInvariant());
    }
}

public sealed class RestoreTargetPolicy
{
    private readonly HashSet<DatabaseEndpointIdentity> _allowedTargets;

    public RestoreTargetPolicy(IEnumerable<DatabaseEndpointModel> allowedTargets)
    {
        ArgumentNullException.ThrowIfNull(allowedTargets);
        _allowedTargets = allowedTargets.Select(DatabaseEndpointIdentity.From).ToHashSet();
    }

    public void EnsureAllowed(ConnectionProfileModel target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Type != ConnectionProfileType.LOCAL)
        {
            throw new InvalidOperationException("وجهة الاستعادة يجب أن تكون ملف اتصال LOCAL مصرحًا.");
        }

        var identity = DatabaseEndpointIdentity.From(target);
        if (!_allowedTargets.Contains(identity))
        {
            throw new InvalidOperationException("نقطة اتصال وجهة الاستعادة غير موجودة في قائمة السماح الصريحة.");
        }
    }

    public static void EnsureSourceAndTargetAreDistinct(ConnectionProfileModel source, ConnectionProfileModel target)
    {
        if (DatabaseEndpointIdentity.From(source) == DatabaseEndpointIdentity.From(target))
        {
            throw new InvalidOperationException("مصدر النسخ ووجهة الاستعادة يشيران إلى قاعدة البيانات نفسها.");
        }
    }
}

public static class BackupFileNameFactory
{
    public static string CreatePath(string backupDirectory, string databaseName, DateTimeOffset timestamp, Guid uniqueId)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
        {
            throw new InvalidOperationException("مسار حفظ النسخ الاحتياطية غير مهيأ.");
        }

        var safeDatabaseName = string.Concat(databaseName.Where(char.IsLetterOrDigit));
        if (string.IsNullOrWhiteSpace(safeDatabaseName))
        {
            safeDatabaseName = "database";
        }

        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{safeDatabaseName}_{timestamp:yyyyMMdd_HHmmss_fffffff}_{uniqueId:N}.backup");
        return Path.Combine(Path.GetFullPath(backupDirectory), fileName);
    }
}

public static class BackupErrorMessage
{
    public const string RestoreDisabled =
        "الاستعادة المباشرة معطلة حفاظًا على سلامة البيانات إلى أن يتوفر مسار staging والتحقق والتبديل الذري.";

    public static string ForFailure(Exception exception) => exception switch
    {
        TimeoutException => "انتهت مهلة تشغيل أداة PostgreSQL قبل اكتمال العملية.",
        FileNotFoundException => "أداة PostgreSQL المطلوبة غير متوفرة في المسار المهيأ.",
        _ => "فشلت عملية النسخ أو الاستعادة. راجع السجل التقني المحمي."
    };

    public static string ForRestoreFailure(Exception exception, bool rollbackSucceeded)
    {
        var rollbackMessage = rollbackSucceeded
            ? " تمت إعادة القاعدة المحلية إلى نسخة الإنقاذ السابقة."
            : " لم يكتمل التحقق من استعادة نسخة الإنقاذ؛ راجع المسؤول فورًا.";
        return exception switch
        {
            TimeoutException => "انتهت مهلة النسخ أو الاستعادة قبل اكتمال العملية." + rollbackMessage,
            FileNotFoundException => "أداة PostgreSQL المطلوبة غير متوفرة في المسار المهيأ." + rollbackMessage,
            InvalidOperationException { Message: var message } when message.Contains("pg_dump", StringComparison.OrdinalIgnoreCase) => "تعذر إنشاء نسخة من قاعدة البيانات؛ تحقق من اتصال الخادم وصلاحية القراءة ومسار أدوات PostgreSQL." + rollbackMessage,
            InvalidOperationException { Message: var message } when message.Contains("pg_restore", StringComparison.OrdinalIgnoreCase) => "تعذرت استعادة النسخة في القاعدة المحلية؛ تحقق من صلاحيات الاستعادة وتوافق إصدار PostgreSQL." + rollbackMessage,
            InvalidOperationException { Message: var message } when message.Contains("psql", StringComparison.OrdinalIgnoreCase) => "تعذر التحقق من سلامة القاعدة المحلية بعد الاستعادة." + rollbackMessage,
            _ => "فشلت مزامنة قاعدة البيانات المحلية. راجع السجل التقني المحمي." + rollbackMessage
        };
    }
}

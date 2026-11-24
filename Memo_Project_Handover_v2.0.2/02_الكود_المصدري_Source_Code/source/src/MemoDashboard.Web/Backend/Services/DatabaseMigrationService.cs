using System.Text.RegularExpressions;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Database;
using MemoDashboard.Backend.Models;
using Npgsql;

namespace MemoDashboard.Backend.Services;

public sealed record MigrationExecutionResult(bool Success, string Message, string? CurrentVersion = null, string? TargetVersion = null);

public sealed class DatabaseMigrationService
{
    private static readonly Regex ScriptName = new("^(?<version>\\d{3,})_[A-Za-z0-9_]+\\.sql$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly DbConnectionFactory _factory;
    private readonly ILogger<DatabaseMigrationService> _logger;
    private readonly string? _scriptsDirectory;

    public MigrationVersionStatus? LastKnownStatus { get; private set; }

    public DatabaseMigrationService(
        DbConnectionFactory factory,
        ILogger<DatabaseMigrationService> logger)
    {
        _factory = factory;
        _logger = logger;
        _scriptsDirectory = FindDatabaseScriptsDirectory();
    }

    public static string? FindDatabaseScriptsDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "database"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "database"),
            Path.Combine(Directory.GetCurrentDirectory(), "database"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "database"),
            @"D:\Memo\Memo 2.0.0\source\database"
        };

        return candidates.FirstOrDefault(d => Directory.Exists(d) && Directory.EnumerateFiles(d, "*.sql").Any());
    }

    public static string GetApplicationVersion(string? scriptsDirectory = null)
    {
        var dir = scriptsDirectory ?? FindDatabaseScriptsDirectory();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return "069";

        var versions = Directory.GetFiles(dir, "*.sql")
            .Select(p => ScriptName.Match(Path.GetFileName(p)))
            .Where(m => m.Success)
            .Select(m => m.Groups["version"].Value)
            .OrderBy(v => int.TryParse(v, out var n) ? n : 0)
            .ToList();

        return versions.Count > 0 ? versions[^1] : "069";
    }

    public async Task<MigrationVersionStatus> GetVersionStatusAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_scriptsDirectory) || !Directory.Exists(_scriptsDirectory))
        {
            throw new DirectoryNotFoundException("لم يتم العثور على مجلد سكربتات قاعدة البيانات.");
        }

        var runner = new MigrationRunner(_factory, _scriptsDirectory);
        var status = await runner.GetVersionStatusAsync(cancellationToken);
        LastKnownStatus = status;
        return status;
    }

    public async Task<MigrationExecutionResult> ApplyMigrationsAsync(
        Guid? updatedBy = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_scriptsDirectory) || !Directory.Exists(_scriptsDirectory))
        {
            _logger.LogWarning("لم يتم العثور على مجلد سكربتات قاعدة البيانات لتطبيق الترحيلات.");
            return new MigrationExecutionResult(false, "مجلد سكربتات الترحيل غير موجود.");
        }

        try
        {
            var runner = new MigrationRunner(_factory, _scriptsDirectory);
            var status = await runner.GetVersionStatusAsync(cancellationToken);
            LastKnownStatus = status;

            if (status.DatabaseIsNewer)
            {
                var newerMsg = $"إصدار قاعدة البيانات ({status.DatabaseVersion}) أحدث من إصدار البرنامج الحالي ({status.ApplicationVersion}). يرجى مراجعة دائرة نظم وقواعد البيانات للتأكد من التحديث.";
                _logger.LogError(newerMsg);
                return new MigrationExecutionResult(false, newerMsg, status.DatabaseVersion, status.ApplicationVersion);
            }

            _logger.LogInformation(
                "جارٍ تطبيق الترحيلات بتأكيد مدير النظام (من الإصدار {CurrentVersion} إلى {TargetVersion})...",
                status.DatabaseVersion,
                status.ApplicationVersion);

            await runner.RunAsync(updatedBy, cancellationToken);
            var updatedStatus = await runner.GetVersionStatusAsync(cancellationToken);
            LastKnownStatus = updatedStatus;

            _logger.LogInformation("تم تطبيق كافة الترحيلات وتحديث قاعدة البيانات بنجاح إلى الإصدار {TargetVersion}.", updatedStatus.ApplicationVersion);
            return new MigrationExecutionResult(
                true,
                $"تم تحديث قاعدة البيانات وتطبيق الترحيلات بنجاح إلى الإصدار {updatedStatus.ApplicationVersion}.",
                updatedStatus.DatabaseVersion,
                updatedStatus.ApplicationVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل أثناء تطبيق ترحيلات قاعدة البيانات.");
            return new MigrationExecutionResult(false, $"فشل ترحيل قاعدة البيانات: {ex.Message}");
        }
    }

    public async Task<MigrationExecutionResult> EnsureMigrationsAppliedAsync(
        Guid? updatedBy = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_scriptsDirectory) || !Directory.Exists(_scriptsDirectory))
        {
            _logger.LogWarning("لم يتم العثور على مجلد سكربتات قاعدة البيانات للترحيل.");
            return new MigrationExecutionResult(false, "مجلد سكربتات الترحيل غير موجود.");
        }

        try
        {
            var runner = new MigrationRunner(_factory, _scriptsDirectory);
            var status = await runner.GetVersionStatusAsync(cancellationToken);
            LastKnownStatus = status;

            _logger.LogInformation(
                "فحص إصدار قاعدة البيانات: الإصدار الحالي = {CurrentVersion}, إصدار التطبيق المطلوب = {TargetVersion}",
                status.DatabaseVersion,
                status.ApplicationVersion);

            if (status.DatabaseIsNewer)
            {
                var newerMsg = $"إصدار قاعدة البيانات ({status.DatabaseVersion}) أحدث من إصدار البرنامج الحالي ({status.ApplicationVersion}). يرجى مراجعة دائرة نظم وقواعد البيانات للتأكد من التحديث.";
                _logger.LogError(newerMsg);
                return new MigrationExecutionResult(false, newerMsg, status.DatabaseVersion, status.ApplicationVersion);
            }

            if (!status.IsInitialized || status.RequiresUpgrade || string.Compare(status.DatabaseVersion, status.ApplicationVersion, StringComparison.Ordinal) < 0)
            {
                var warningMsg = $"قاعدة البيانات الحالية بالإصدار ({status.DatabaseVersion}) وهي أقدم من إصدار النظام ({status.ApplicationVersion}). يلزم موافقة مدير النظام لتطبيق الترحيلات.";
                _logger.LogWarning(warningMsg);
                return new MigrationExecutionResult(false, warningMsg, status.DatabaseVersion, status.ApplicationVersion);
            }

            return new MigrationExecutionResult(
                true,
                $"قاعدة البيانات متوافقة ومحدثة مسبقاً ({status.DatabaseVersion}).",
                status.DatabaseVersion,
                status.ApplicationVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "فشل أثناء التحقق من ترحيلات قاعدة البيانات.");
            return new MigrationExecutionResult(false, $"فشل فحص إصدار قاعدة البيانات: {ex.Message}");
        }
    }

    public static async Task<TestConnectionResult> InspectConnectionVersionAsync(
        string connectionString,
        string? scriptsDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var applicationVersion = GetApplicationVersion(scriptsDirectory);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var existsCmd = new NpgsqlCommand("select to_regclass('system.schema_migrations') is not null", connection);
            var tableExists = (bool)(await existsCmd.ExecuteScalarAsync(cancellationToken) ?? false);

            if (!tableExists)
            {
                return new TestConnectionResult
                {
                    Success = true,
                    RequiresUpgrade = true,
                    IsCompatible = false,
                    DatabaseVersion = "000",
                    ApplicationVersion = applicationVersion,
                    Message = $"تم الاتصال بنجاح. قاعدة البيانات غير مهيأة بعد (الإصدار 000)، وتتطلب ترحيلاً إلى الإصدار ({applicationVersion}) بموافقة مدير النظام."
                };
            }

            await using var versionCmd = new NpgsqlCommand("""
                select coalesce((
                    select version
                    from system.schema_migrations
                    where version ~ '^[0-9]+$'
                    order by version::numeric desc
                    limit 1
                ), '000')
                """, connection);
            var databaseVersion = Convert.ToString(await versionCmd.ExecuteScalarAsync(cancellationToken)) ?? "000";

            var status = new MigrationVersionStatus(databaseVersion, applicationVersion, true);

            if (status.DatabaseIsNewer)
            {
                return new TestConnectionResult
                {
                    Success = false,
                    DatabaseIsNewer = true,
                    IsCompatible = false,
                    DatabaseVersion = status.DatabaseVersion,
                    ApplicationVersion = status.ApplicationVersion,
                    Message = $"إصدار قاعدة البيانات ({status.DatabaseVersion}) أحدث من إصدار البرنامج الحالي ({status.ApplicationVersion}). يرجى مراجعة دائرة نظم وقواعد البيانات للتأكد من التحديث."
                };
            }

            if (status.RequiresUpgrade)
            {
                return new TestConnectionResult
                {
                    Success = true,
                    RequiresUpgrade = true,
                    IsCompatible = false,
                    DatabaseVersion = status.DatabaseVersion,
                    ApplicationVersion = status.ApplicationVersion,
                    Message = $"تم الاتصال بنجاح. قاعدة البيانات الحالية بالإصدار ({status.DatabaseVersion}) وهي أقدم من إصدار النظام ({status.ApplicationVersion}). يلزم موافقة مدير النظام لتطبيق الترحيلات."
                };
            }

            return new TestConnectionResult
            {
                Success = true,
                IsCompatible = true,
                DatabaseVersion = status.DatabaseVersion,
                ApplicationVersion = status.ApplicationVersion,
                Message = $"تم الاتصال بنجاح. قاعدة البيانات متوافقة ومحدثة للإصدار {status.DatabaseVersion}."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new TestConnectionResult
            {
                Success = false,
                Message = ex is InvalidOperationException ? ex.Message : "تعذر الاتصال بقاعدة البيانات باستخدام الإعدادات المقدمة."
            };
        }
    }
}


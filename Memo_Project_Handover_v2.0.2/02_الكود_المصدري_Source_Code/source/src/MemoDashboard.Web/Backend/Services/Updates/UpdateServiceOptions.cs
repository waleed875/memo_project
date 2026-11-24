using Microsoft.AspNetCore.Http;
using MemoUpdateContracts;
using System.Net;
using System.Security.Cryptography;

namespace MemoDashboard.Backend.Services.Updates;

public sealed class UpdateServiceOptions
{
    public required string PackageRoot { get; init; }
    public string? ConnectionString { get; init; }
    public string? AdminKey { get; init; }
    public string? CheckInKey { get; init; }
    public string? SigningPrivateKeyPem { get; init; }
    public string? SigningPublicKeyPem { get; init; }
    public Uri? PublicBaseUri { get; init; }
    public long MaxPackageBytes { get; init; }
    public int MaxZipEntries { get; init; }
    public long MaxUncompressedPackageBytes { get; init; }
    public int MaxCompressionRatio { get; init; }
    public long MaxUploadRequestBytes => checked(MaxPackageBytes + 128 * 1024);

    public bool SigningPrivateKeyReady => IsValidRsaPrivateKey(SigningPrivateKeyPem);
    public bool PublicKeyMatchesPrivate => SigningPrivateKeyReady
        && MatchesPrivateKey(SigningPrivateKeyPem!, SigningPublicKeyPem);
    public bool SigningKeyReady => PublicKeyMatchesPrivate;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString)
        && SigningKeyReady
        && PublicBaseUri is not null;

    public static UpdateServiceOptions Create(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var packageRoot = ResolvePackageRoot(configuration, environment);
        var maxPackageBytes = configuration.GetValue<long?>("MaxPackageBytes") ?? 1_073_741_824;
        var maxZipEntries = configuration.GetValue<int?>("MaxZipEntries") ?? 10_000;
        var maxUncompressed = configuration.GetValue<long?>("MaxUncompressedPackageBytes")
            ?? checked(maxPackageBytes * 4);
        var maxCompressionRatio = configuration.GetValue<int?>("MaxCompressionRatio") ?? 100;
        if (maxPackageBytes <= 0 || maxZipEntries <= 0 || maxUncompressed <= 0 || maxCompressionRatio <= 0)
            throw new InvalidOperationException("حدود حزمة التحديث يجب أن تكون أكبر من صفر.");

        var publicBaseUrl = configuration["PublicBaseUrl"] ?? string.Empty;
        return new UpdateServiceOptions
        {
            PackageRoot = packageRoot,
            ConnectionString = GetValue(configuration, "UPDATE_CONNECTION_STRING", "UPDATE_CONNECTION_STRING_FILE"),
            AdminKey = GetValue(configuration, "MEMO_UPDATE_ADMIN_KEY", "MEMO_UPDATE_ADMIN_KEY_FILE"),
            CheckInKey = GetValue(configuration, "MEMO_UPDATE_CHECKIN_KEY", "MEMO_UPDATE_CHECKIN_KEY_FILE"),
            SigningPrivateKeyPem = GetValue(configuration, "MEMO_UPDATE_SIGNING_PRIVATE_KEY", "MEMO_UPDATE_SIGNING_PRIVATE_KEY_FILE"),
            SigningPublicKeyPem = GetValue(configuration, "MEMO_UPDATE_SIGNING_PUBLIC_KEY", "MEMO_UPDATE_SIGNING_PUBLIC_KEY_FILE"),
            PublicBaseUri = TryGetHttpsBaseUri(publicBaseUrl),
            MaxPackageBytes = maxPackageBytes,
            MaxZipEntries = maxZipEntries,
            MaxUncompressedPackageBytes = maxUncompressed,
            MaxCompressionRatio = maxCompressionRatio
        };
    }

    public string? ExportCurrentPublicKeyPem()
    {
        if (!SigningKeyReady || string.IsNullOrWhiteSpace(SigningPrivateKeyPem))
            return null;

        using var rsa = RSA.Create();
        rsa.ImportFromPem(SigningPrivateKeyPem);
        return rsa.ExportSubjectPublicKeyInfoPem();
    }

    private static string ResolvePackageRoot(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredRoot = configuration["PackageRoot"]?.Trim();
        var persistentRoot = configuration["MEMO_UPDATE_DATA_DIR"]?.Trim()
            ?? configuration["UpdateDataDirectory"]?.Trim();
        if (string.IsNullOrWhiteSpace(persistentRoot))
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData))
                throw new InvalidOperationException("يلزم ضبط MEMO_UPDATE_DATA_DIR عندما لا يتوفر مجلد بيانات محلي.");
            persistentRoot = Path.Combine(localData, "AuditMemoSystem", "MemoDashboard", "updates");
        }

        var contentRoot = Path.GetFullPath(environment.ContentRootPath);
        var root = GetAbsolutePath(persistentRoot, "MEMO_UPDATE_DATA_DIR");
        EnsureOutsideApplicationRoot(root, contentRoot, "MEMO_UPDATE_DATA_DIR");

        var packageRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(root, "packages")
            : GetAbsolutePath(configuredRoot, "PackageRoot");
        EnsureOutsideApplicationRoot(packageRoot, contentRoot, "PackageRoot");
        Directory.CreateDirectory(packageRoot);
        return packageRoot;
    }

    private static string GetAbsolutePath(string value, string name)
    {
        if (!Path.IsPathRooted(value))
            throw new InvalidOperationException($"{name} يجب أن يكون مسارًا مطلقًا خارج مجلد التطبيق.");
        return Path.GetFullPath(value);
    }

    private static void EnsureOutsideApplicationRoot(string candidate, string applicationRoot, string name)
    {
        var relative = Path.GetRelativePath(applicationRoot, candidate);
        if (relative == "." || (!Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"{name} يجب أن يبقى خارج مجلد الإصدار المنشور.");
        }
    }

    private static string? GetValue(IConfiguration configuration, string directName, string fileName)
    {
        var filePath = configuration[fileName] ?? Environment.GetEnvironmentVariable(fileName);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
                throw new InvalidOperationException($"ملف السر {fileName} غير موجود.");
            var value = File.ReadAllText(fullPath).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        var direct = configuration[directName] ?? Environment.GetEnvironmentVariable(directName);
        return string.IsNullOrWhiteSpace(direct) ? null : direct.Trim();
    }

    private static bool IsValidRsaPrivateKey(string? privateKeyPem)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPem)) return false;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            return rsa.ExportParameters(includePrivateParameters: true).D is { Length: > 0 };
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool MatchesPrivateKey(string privateKeyPem, string? publicKeyPem)
    {
        try
        {
            using var privateRsa = RSA.Create();
            privateRsa.ImportFromPem(privateKeyPem);
            var expected = privateRsa.ExportSubjectPublicKeyInfo();

            if (string.IsNullOrWhiteSpace(publicKeyPem))
                return true;

            if (publicKeyPem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
                return false;

            using var publicRsa = RSA.Create();
            publicRsa.ImportFromPem(publicKeyPem);
            var actual = publicRsa.ExportSubjectPublicKeyInfo();
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static Uri? TryGetHttpsBaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)) return null;
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + '/', UriKind.Absolute);
    }
}

public sealed record UnifiedUpdateHealth(
    string Service,
    bool Configured,
    bool PublishingConfigured,
    bool CheckInConfigured,
    bool PublicBaseUrlValid,
    string Status);

public sealed record UpdatePublicKeyResult(
    int StatusCode,
    string? PublicKeyPem,
    string Status,
    string? Error = null);

public sealed record UpdatePublishRequest(
    IFormFile Package,
    string Version,
    string OperatingSystem,
    string Architecture,
    string MinimumVersion,
    string MinimumDatabaseVersion,
    string TargetDatabaseVersion,
    string Notes);

public sealed record UpdateOperationResult(int StatusCode, object? Body, string? Location = null);

public sealed record UpdateManifestResult(int StatusCode, UpdateManifest? Manifest, string? Error = null);

public sealed record UpdateCheckInRequest(
    Guid DeviceId,
    string DeviceName,
    string OperatingSystem,
    string Architecture,
    string InstalledVersion,
    string? LastUpdateStatus,
    string? LastUpdateMessage);
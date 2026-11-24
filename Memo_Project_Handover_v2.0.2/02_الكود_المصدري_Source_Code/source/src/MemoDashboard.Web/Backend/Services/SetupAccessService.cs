using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace MemoDashboard.Backend.Services;

public sealed class SetupAccessService
{
    public const string TokenHeader = "X-Setup-Token";
    public const string TokenConfigurationKey = "MEMO_DASHBOARD_SETUP_TOKEN";
    public const string TokenFileConfigurationKey = "MEMO_DASHBOARD_SETUP_TOKEN_FILE";
    public const string InternalNetworkSetupConfigurationKey = "MEMO_DASHBOARD_INTERNAL_SETUP";
    private const int MinimumTokenLength = 32;
    private const int MaximumTokenLength = 512;
    private byte[]? _expectedTokenHash;
    private readonly ILogger<SetupAccessService> _logger;
    private readonly string? _tokenFilePath;
    private readonly bool _allowInternalNetworkSetup;
    private string _tokenSource = "none";

    public SetupAccessService(
        IConfiguration configuration,
        ILogger<SetupAccessService> logger)
    {
        _logger = logger;
        var mode = configuration[InternalNetworkSetupConfigurationKey]?.Trim();
        _allowInternalNetworkSetup = string.Equals(mode, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "on", StringComparison.OrdinalIgnoreCase);

        var filePath = configuration[TokenFileConfigurationKey]?.Trim();
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            if (!Path.IsPathRooted(filePath))
            {
                logger.LogWarning(
                    "First-run setup token file is ignored because {ConfigurationKey} must be an absolute secret-file path.",
                    TokenFileConfigurationKey);
            }
            else
            {
                _tokenFilePath = Path.GetFullPath(filePath);
                TryLoadToken(File.ReadAllText, _tokenFilePath, "file");
            }
        }

        if (_expectedTokenHash is null)
        {
            var token = configuration[TokenConfigurationKey]?.Trim();
            if (!string.IsNullOrWhiteSpace(token))
                SetToken(token, "environment");
        }

        if (_expectedTokenHash is null && !_allowInternalNetworkSetup)
        {
            logger.LogWarning(
                "First-run setup is disabled until {ConfigurationKey}, {TokenFileConfigurationKey}, or {InternalNetworkKey} is configured.",
                TokenConfigurationKey,
                TokenFileConfigurationKey,
                InternalNetworkSetupConfigurationKey);
        }
        else if (_expectedTokenHash is null)
        {
            logger.LogWarning(
                "First-run setup is enabled for private internal network addresses; no manual setup token is required.");
        }
    }

    public bool InternalNetworkSetupEnabled => _allowInternalNetworkSetup;

    public SetupTokenStatus GetStatus() => new(
        _expectedTokenHash is not null,
        _tokenSource,
        _allowInternalNetworkSetup ? "private-network" : "loopback-only");

    public bool IsAllowed(
        HttpContext context,
        bool isConfigured,
        IWebAdminAuthService adminAuth)
    {
        if (isConfigured)
        {
            return adminAuth.GetCurrent(context) is not null || IsFirstRunAllowed(context);
        }

        return IsFirstRunAllowed(context);
    }

    public bool IsFirstRunAllowed(HttpContext context)
    {
        // Whoever reaches this host over loopback already controls the machine,
        // so first-run setup from localhost never needs a token.
        if (IsLoopback(context.Connection.RemoteIpAddress))
        {
            return true;
        }

        // A valid setup token authorizes first-run setup from any address.
        // Without this check a configured token could never be presented,
        // silently locking out every non-loopback caller.
        if (IsTokenValid(context))
        {
            return true;
        }

        if (_expectedTokenHash is null)
        {
            return _allowInternalNetworkSetup
                && ConfigurationService.IsPrivateNetworkAddress(context.Connection.RemoteIpAddress);
        }

        return false;
    }

    public void ConsumeFirstRunToken()
    {
        _expectedTokenHash = null;
        _tokenSource = "consumed";
        if (_tokenFilePath is null) return;

        try
        {
            if (File.Exists(_tokenFilePath))
                File.Delete(_tokenFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر حذف ملف رمز التهيئة بعد إكمال الإعداد الأولي.");
        }
    }

    private void TryLoadToken(
        Func<string, string> reader,
        string path,
        string source)
    {
        try
        {
            var token = reader(path).Trim();
            if (!SetToken(token, source))
            {
                _logger.LogWarning("ملف رمز التهيئة لا يحتوي قيمة بطول صالح.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "تعذر قراءة ملف رمز التهيئة؛ سيُستخدم وضع الشبكة الداخلية إن كان مفعلاً.");
        }
    }

    private bool SetToken(string token, string source)
    {
        if (token.Length is < MinimumTokenLength or > MaximumTokenLength)
            return false;

        _expectedTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        _tokenSource = source;
        return true;
    }

    private bool IsTokenValid(HttpContext context)
    {
        if (_expectedTokenHash is null) return false;
        var presented = context.Request.Headers[TokenHeader].ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(presented)) return false;
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(presentedHash, _expectedTokenHash);
    }

    private static bool IsLoopback(IPAddress? address)
    {
        if (address is null) return false;
        if (IPAddress.IsLoopback(address)) return true;
        return address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4());
    }
}

public sealed record SetupTokenStatus(
    bool Ready,
    string Source,
    string Scope);
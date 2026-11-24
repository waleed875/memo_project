namespace AuditMemoSystem.Core.Models;

/// <summary>Typed routes derived from the single owner-facing server address.</summary>
public sealed class ServerEndpoints
{
    private ServerEndpoints(Uri baseAddress)
    {
        BaseAddress = baseAddress;
        Dashboard = new Uri(baseAddress, "");
        Health = new Uri(baseAddress, "health");
        UpdateHealth = new Uri(baseAddress, "api/updates/health");
        PublicKey = new Uri(baseAddress, "api/updates/public-key");
    }

    public Uri BaseAddress { get; }
    public Uri Dashboard { get; }
    public Uri Health { get; }
    public Uri UpdateHealth { get; }
    public Uri PublicKey { get; }

    public Uri Manifest(string operatingSystem, string architecture) =>
        new(BaseAddress, $"api/updates/manifest/{Uri.EscapeDataString(operatingSystem)}/{Uri.EscapeDataString(architecture)}");

    public static ServerEndpoints Parse(string value)
        => ParseUpdateService(value);

    public static ServerEndpoints ParseDashboard(string value, bool internalLanHttp = false)
    {
        var uri = ParseUri(value, "عنوان لوحة المعلومات");
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback && !internalLanHttp)
            throw new InvalidOperationException("عنوان لوحة المعلومات HTTP على الشبكة يتطلب تفعيل نمط InternalLanHttp صراحة.");
        return new ServerEndpoints(uri);
    }

    public static ServerEndpoints ParseUpdateService(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("عنوان الخادم مطلوب.");

        var uri = ParseUri(value, "عنوان خادم التحديث");

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            throw new InvalidOperationException("عنوان خادم التحديث الشبكي يجب أن يستخدم HTTPS. يُسمح HTTP فقط مع localhost للاختبار.");

        return new ServerEndpoints(uri);
    }

    private static Uri ParseUri(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label} مطلوب.");

        var text = value.Trim();
        if (!text.EndsWith('/')) text += "/";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"{label} يجب أن يكون عنوان http أو https صالحاً دون بيانات دخول أو query أو fragment.");

        return uri;
    }
}

public sealed class ServerAddressSettingsModel
{
    public string? BaseAddress { get; set; }
    public string? DashboardBaseAddress { get; set; }
    public string? UpdateServiceBaseAddress { get; set; }
    public string DeploymentMode { get; set; } = "InternalLanHttps";
    public bool LoadedFromLegacySettings { get; set; }
    public bool HasConflictingLegacySettings { get; set; }
    public ServerEndpoints? Endpoints { get; set; }
    public ServerEndpoints? DashboardEndpoints { get; set; }
    public ServerEndpoints? UpdateServiceEndpoints { get; set; }
}

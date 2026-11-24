using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MemoUpdateContracts;

public sealed record UpdateManifest(
    string Version,
    string MinimumVersion,
    string OperatingSystem,
    string Architecture,
    Uri PackageUrl,
    long PackageSize,
    string Sha256,
    string RequiredDatabaseVersion,
    string TargetDatabaseVersion,
    string ExecutableRelativePath,
    DateTimeOffset PublishedAt,
    string? Signature = null);

public sealed record SignedManifestPayload(
    string Version, string MinimumVersion, string OperatingSystem, string Architecture, string PackageUrl, long PackageSize,
    string Sha256, string RequiredDatabaseVersion, string TargetDatabaseVersion, string ExecutableRelativePath,
    DateTimeOffset PublishedAt);

public static class ManifestSecurity
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Sign(SignedManifestPayload payload, RSA privateKey) =>
        Convert.ToBase64String(privateKey.SignData(CanonicalBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    public static bool Verify(UpdateManifest manifest, RSA publicKey)
    {
        if (string.IsNullOrWhiteSpace(manifest.Signature)) return false;
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(manifest.Signature);
        }
        catch (FormatException)
        {
            return false;
        }
        var payload = new SignedManifestPayload(manifest.Version, manifest.MinimumVersion, manifest.OperatingSystem, manifest.Architecture,
            manifest.PackageUrl.ToString(), manifest.PackageSize, manifest.Sha256, manifest.RequiredDatabaseVersion,
            manifest.TargetDatabaseVersion, manifest.ExecutableRelativePath, manifest.PublishedAt);
        return publicKey.VerifyData(CanonicalBytes(payload), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public static async Task<string> Sha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        using var hash = SHA256.Create();
        return Convert.ToHexString(await hash.ComputeHashAsync(stream, cancellationToken));
    }

    private static byte[] CanonicalBytes(SignedManifestPayload payload) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Options));
}
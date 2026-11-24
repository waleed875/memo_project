using System.Security.Cryptography;
using MemoDashboard.Backend.Services.Updates;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoDashboard.Backend.Tests;

public sealed class UnifiedUpdateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "memo-unified-updates", Guid.NewGuid().ToString("N"));

    public UnifiedUpdateServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void HealthExposesLegacyUpdateFieldsWithoutSecrets()
    {
        using var rsa = RSA.Create(2048);
        var service = CreateService(new UpdateServiceOptions
        {
            PackageRoot = _root,
            ConnectionString = "Host=localhost;Database=memo",
            AdminKey = "admin-secret",
            CheckInKey = "device-secret",
            SigningPrivateKeyPem = rsa.ExportRSAPrivateKeyPem(),
            PublicBaseUri = new Uri("https://updates.example.test/"),
            MaxPackageBytes = 1024,
            MaxZipEntries = 10,
            MaxUncompressedPackageBytes = 4096,
            MaxCompressionRatio = 10
        });

        var health = service.GetHealth();

        Assert.Equal("MemoUpdateService", health.Service);
        Assert.True(health.Configured);
        Assert.True(health.PublishingConfigured);
        Assert.True(health.CheckInConfigured);
        Assert.True(health.PublicBaseUrlValid);
        Assert.DoesNotContain("admin-secret", System.Text.Json.JsonSerializer.Serialize(health));
        Assert.DoesNotContain("device-secret", System.Text.Json.JsonSerializer.Serialize(health));
    }

    [Fact]
    public void SharedSecretUsesHeaderAndRejectsWrongValue()
    {
        var service = CreateService(new UpdateServiceOptions
        {
            PackageRoot = _root,
            AdminKey = "admin-secret",
            MaxPackageBytes = 1024,
            MaxZipEntries = 10,
            MaxUncompressedPackageBytes = 4096,
            MaxCompressionRatio = 10
        });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Update-Admin-Key"] = "wrong";
        Assert.False(service.IsPublisherAllowed(context.Request));
        context.Request.Headers["X-Update-Admin-Key"] = "admin-secret";
        Assert.True(service.IsPublisherAllowed(context.Request));
    }

    [Fact]
    public void PackageResolverRejectsTraversalAndAcceptsOnlyFilesUnderPackageRoot()
    {
        var safePath = Path.Combine(_root, "1.0.6-windows-x64.zip");
        File.WriteAllBytes(safePath, [1, 2, 3]);
        var service = CreateService(new UpdateServiceOptions
        {
            PackageRoot = _root,
            MaxPackageBytes = 1024,
            MaxZipEntries = 10,
            MaxUncompressedPackageBytes = 4096,
            MaxCompressionRatio = 10
        });

        Assert.Equal(safePath, service.ResolvePackagePath("1.0.6-windows-x64.zip"));
        Assert.Null(service.ResolvePackagePath("..\\secret.zip"));
        Assert.Null(service.ResolvePackagePath("nested\\package.zip"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static UnifiedUpdateService CreateService(UpdateServiceOptions options) =>
        new(options, NullLogger<UnifiedUpdateService>.Instance);
}
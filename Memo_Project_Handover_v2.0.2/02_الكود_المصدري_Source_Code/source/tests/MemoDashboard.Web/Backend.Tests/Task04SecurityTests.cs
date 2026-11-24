using System.Net;
using System.Security.Cryptography;
using MemoDashboard.Backend.Services;
using MemoDashboard.Backend.Services.Updates;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoDashboard.Backend.Tests;

public sealed class Task04SecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "memo-task04-security", Guid.NewGuid().ToString("N"));

    public Task04SecurityTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void LoopbackFirstRunNeedsNoTokenAndTokenFileIsConsumedAfterSave()
    {
        var tokenPath = Path.Combine(_root, "setup-token");
        var token = new string('T', 48);
        File.WriteAllText(tokenPath, token);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SetupAccessService.TokenFileConfigurationKey] = tokenPath
            })
            .Build();
        var access = new SetupAccessService(configuration, NullLogger<SetupAccessService>.Instance);
        var loopbackNoHeader = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Loopback }
        };

        Assert.True(access.GetStatus().Ready);
        Assert.Equal("file", access.GetStatus().Source);
        Assert.True(access.IsFirstRunAllowed(loopbackNoHeader));

        access.ConsumeFirstRunToken();

        Assert.False(access.GetStatus().Ready);
        Assert.False(File.Exists(tokenPath));
        Assert.True(access.IsFirstRunAllowed(loopbackNoHeader));
    }

    [Fact]
    public void ExplicitInternalNetworkSetupAllowsPrivateLanWithoutToken()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SetupAccessService.InternalNetworkSetupConfigurationKey] = "1"
            })
            .Build();
        var access = new SetupAccessService(configuration, NullLogger<SetupAccessService>.Instance);
        var context = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse("192.168.20.40") }
        };

        Assert.False(access.GetStatus().Ready);
        Assert.Equal("private-network", access.GetStatus().Scope);
        Assert.True(access.IsFirstRunAllowed(context));
    }

    [Fact]
    public void InternalNetworkSetupStaysClosedWithoutExplicitOptIn()
    {
        var configuration = new ConfigurationBuilder().Build();
        var access = new SetupAccessService(configuration, NullLogger<SetupAccessService>.Instance);
        var context = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse("192.168.20.40") }
        };

        Assert.False(access.IsFirstRunAllowed(context));
    }

    [Fact]
    public void ValidSetupTokenAuthorizesFirstRunFromLan()
    {
        var token = new string('T', 48);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SetupAccessService.TokenConfigurationKey] = token
            })
            .Build();
        var access = new SetupAccessService(configuration, NullLogger<SetupAccessService>.Instance);

        var withToken = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse("192.168.20.40") }
        };
        withToken.Request.Headers[SetupAccessService.TokenHeader] = token;

        var withoutToken = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse("192.168.20.40") }
        };

        var wrongToken = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse("192.168.20.40") }
        };
        wrongToken.Request.Headers[SetupAccessService.TokenHeader] = new string('X', 48);

        Assert.True(access.IsFirstRunAllowed(withToken));
        Assert.False(access.IsFirstRunAllowed(withoutToken));
        Assert.False(access.IsFirstRunAllowed(wrongToken));
    }
    [Fact]
    public void SigningPublicKeyMismatchBlocksPublishingAndPublicKeyEndpoint()
    {
        using var privateRsa = RSA.Create(2048);
        using var otherRsa = RSA.Create(2048);
        var options = CreateOptions(
            privateRsa.ExportRSAPrivateKeyPem(),
            otherRsa.ExportSubjectPublicKeyInfoPem());
        var service = new UnifiedUpdateService(options, NullLogger<UnifiedUpdateService>.Instance);

        Assert.False(options.PublicKeyMatchesPrivate);
        Assert.False(options.SigningKeyReady);
        var result = service.GetPublicKey();
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("mismatch", result.Status);
        Assert.Null(result.PublicKeyPem);
    }

    [Fact]
    public void MatchingPublicKeyIsReturnedWithoutPrivateMaterial()
    {
        using var rsa = RSA.Create(2048);
        var options = CreateOptions(
            rsa.ExportRSAPrivateKeyPem(),
            rsa.ExportSubjectPublicKeyInfoPem());
        var service = new UnifiedUpdateService(options, NullLogger<UnifiedUpdateService>.Instance);

        var result = service.GetPublicKey();

        Assert.Equal(200, result.StatusCode);
        Assert.Contains("PUBLIC KEY", result.PublicKeyPem);
        Assert.DoesNotContain("PRIVATE KEY", result.PublicKeyPem);
        Assert.DoesNotContain("D:", result.PublicKeyPem);
    }

    [Fact]
    public void ReleaseMetadataMustMatchAcrossArtifacts()
    {
        var same = UnifiedUpdateService.GetMetadataDifferences(
            "notes", "1.0.5", "059", "060",
            "notes", "1.0.5", "059", "060");
        var different = UnifiedUpdateService.GetMetadataDifferences(
            "notes", "1.0.5", "059", "060",
            "changed", "1.0.5", "060", "060");

        Assert.Empty(same);
        Assert.Equal(new[] { "notes", "minimumDatabaseVersion" }, different);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static UpdateServiceOptions CreateOptions(string privateKey, string publicKey) => new()
    {
        PackageRoot = Path.Combine(Path.GetTempPath(), "memo-task04-packages", Guid.NewGuid().ToString("N")),
        ConnectionString = "Host=localhost;Database=memo",
        AdminKey = "admin-secret",
        SigningPrivateKeyPem = privateKey,
        SigningPublicKeyPem = publicKey,
        PublicBaseUri = new Uri("https://updates.example.test/"),
        MaxPackageBytes = 1024,
        MaxZipEntries = 10,
        MaxUncompressedPackageBytes = 4096,
        MaxCompressionRatio = 10
    };
}
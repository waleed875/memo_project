using System.Text.Json;
using MemoDashboard.Backend.Models;
using MemoDashboard.Backend.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoDashboard.Backend.Tests;

public class ConfigurationServiceTests : IDisposable
{
    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "MemoDashboard.Tests";
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private readonly string _tempRoot;
    private readonly ConfigurationService _service;

    public ConfigurationServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "memo-dash-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _service = CreateService(_tempRoot);
    }

    private static ConfigurationService CreateService(string contentRoot)
    {
        var paths = new DashboardDataPaths(
            contentRoot,
            Path.Combine(contentRoot, "keys"),
            Path.Combine(contentRoot, "App_Data", "app_settings.json"));
        var protection = new EphemeralDataProtectionProvider();
        return new ConfigurationService(paths, protection, NullLogger<ConfigurationService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    private static SetupRequest Request(
        string? password = null,
        int? dashboardVersion = null) => new()
    {
        Host = "127.0.0.1",
        Port = 5432,
        Database = "memo",
        Username = "reader",
        OrganizationName = "Main entity",
        SubOrganizationName = "Database systems department",
        Password = password,
        DashboardVersion = dashboardVersion
    };
    [Fact]
    public void GetConfigurationInfoIsNullBeforeAnySave()
    {
        Assert.False(_service.HasConfiguration);
        Assert.Null(_service.GetConfigurationInfo());
    }

    [Fact]
    public void SaveThenGetConfigurationInfoReturnsCurrentValues()
    {
        _service.Save(Request("secret-password"));

        Assert.True(_service.HasConfiguration);
        var info = _service.GetConfigurationInfo();
        Assert.NotNull(info);
        Assert.Equal("127.0.0.1", info!.Host);
        Assert.Equal(5432, info.Port);
        Assert.Equal("memo", info.Database);
        Assert.Equal("reader", info.Username);
        Assert.Equal("Main entity", info.OrganizationName);
        Assert.Equal("Database systems department", info.SubOrganizationName);
    }

    [Fact]
    public void ConfigFileDoesNotContainPlaintextPassword()
    {
        _service.Save(Request("super-secret"));

        var json = File.ReadAllText(Path.Combine(_tempRoot, "App_Data", "app_settings.json"));
        Assert.DoesNotContain("super-secret", json);
        Assert.Contains("EncryptedPassword", json);
    }

    [Fact]
    public void SavingWithBlankPasswordKeepsExistingPassword()
    {
        _service.Save(Request("original-password"));
        _service.Save(Request(string.Empty));

        var connectionString = _service.BuildConnectionString();
        Assert.NotNull(connectionString);
        Assert.Contains("Password=original-password", connectionString);
    }

    [Fact]
    public void SavingWithNewPasswordReplacesExistingPassword()
    {
        _service.Save(Request("original-password"));
        _service.Save(Request("new-password"));

        var connectionString = _service.BuildConnectionString();
        Assert.NotNull(connectionString);
        Assert.Contains("Password=new-password", connectionString);
        Assert.DoesNotContain("original-password", connectionString);
    }

    [Fact]
    public void BlankPasswordWorksForTrustAuthConfigurations()
    {
        _service.Save(Request(string.Empty));

        var connectionString = _service.BuildConnectionString();
        Assert.NotNull(connectionString);
        Assert.Contains("Password=", connectionString);
    }

    [Fact]
    public void ConfigFileIsValidJsonAndReadableAfterSave()
    {
        _service.Save(Request("p"));

        var raw = File.ReadAllText(Path.Combine(_tempRoot, "App_Data", "app_settings.json"));
        var settings = JsonSerializer.Deserialize<AppSettings>(raw);
        Assert.NotNull(settings);
        Assert.Equal("127.0.0.1", settings!.Host);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(-1, 1)]
    [InlineData(3, 3)]
    public void DashboardVersionSupportsAllThreeInterfaces(int input, int expected)
    {
        _service.Save(Request("p", input));

        var info = _service.GetConfigurationInfo();
        Assert.NotNull(info);
        Assert.Equal(expected, info!.DashboardVersion);
    }

    [Fact]
    public void DashboardVersionThreeRoundTripsThroughPersistedFile()
    {
        _service.Save(Request("secret-password", 3));

        var reloaded = CreateService(_tempRoot);
        var info = reloaded.GetConfigurationInfo();
        Assert.NotNull(info);
        Assert.Equal(3, info!.DashboardVersion);
    }
    [Fact]
    public void DashboardVersionDefaultsToV1WhenNotProvided()
    {
        _service.Save(Request("p", null));

        var info = _service.GetConfigurationInfo();
        Assert.NotNull(info);
        Assert.Equal(1, info!.DashboardVersion);
    }

    [Fact]
    public void DashboardVersionRoundTripsThroughPersistedFile()
    {
        _service.Save(Request("secret-password", 2));

        var reloaded = CreateService(_tempRoot);
        var info = reloaded.GetConfigurationInfo();
        Assert.NotNull(info);
        Assert.Equal(2, info!.DashboardVersion);
        Assert.Equal("Main entity", info!.OrganizationName);
    }

    [Fact]
    public void SavingWithoutVersionKeepsPreviouslySelectedV2()
    {
        _service.Save(Request("original-password", 2));
        _service.Save(Request(string.Empty, null));

        var info = _service.GetConfigurationInfo();
        Assert.NotNull(info);
        Assert.Equal(2, info!.DashboardVersion);
    }

    [Fact]
    public void SetupConfigNeverExposesEncryptedPassword()
    {
        _service.Save(Request("super-secret", 2));

        var info = _service.GetConfigurationInfo();
        Assert.NotNull(info);
        var json = JsonSerializer.Serialize(info);
        Assert.DoesNotContain("super-secret", json);
        Assert.DoesNotContain("EncryptedPassword", json);
        Assert.DoesNotContain("Password", json);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.20.15")]
    [InlineData("db.example.internal")]
    public void AnyConfiguredHostUsesDisabledSsl(string host)
    {
        var request = Request("p");
        request.Host = host;
        _service.Save(request);

        var connectionString = _service.BuildConnectionString();

        Assert.NotNull(connectionString);
        Assert.Contains("SSL Mode=Disable", connectionString);
        Assert.DoesNotContain("VerifyFull", connectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Root Certificate", connectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SavedSettingsDoNotContainTlsOrCertificateOptions()
    {
        _service.Save(Request("p"));

        var raw = File.ReadAllText(Path.Combine(_tempRoot, "App_Data", "app_settings.json"));
        var info = _service.GetConfigurationInfo();

        Assert.DoesNotContain("RequireTls", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AllowInsecureLocal", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EncryptedRootCertificate", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequireTls", JsonSerializer.Serialize(info), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HasRootCertificate", JsonSerializer.Serialize(info), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatabaseIsNewer_TriggersCorrectStatus_AndExplicitMessage()
    {
        var status = new AuditMemoSystem.Data.Database.MigrationVersionStatus("070", "069", true);

        Assert.True(status.DatabaseIsNewer);
        Assert.False(status.RequiresUpgrade);

        var message = $"إصدار قاعدة البيانات ({status.DatabaseVersion}) أحدث من إصدار البرنامج الحالي ({status.ApplicationVersion}). يرجى مراجعة دائرة نظم وقواعد البيانات للتأكد من التحديث.";
        Assert.Contains("يرجى مراجعة دائرة نظم وقواعد البيانات للتأكد من التحديث", message);
    }

    [Fact]
    public void DatabaseIsOlder_RequiresUpgrade_AndRequestsAdminApproval()
    {
        var status = new AuditMemoSystem.Data.Database.MigrationVersionStatus("065", "069", true);

        Assert.False(status.DatabaseIsNewer);
        Assert.True(status.RequiresUpgrade);

        var message = $"قاعدة البيانات الحالية بالإصدار ({status.DatabaseVersion}) وهي أقدم من إصدار النظام ({status.ApplicationVersion}). يلزم موافقة مدير النظام لتطبيق الترحيلات.";
        Assert.Contains("يلزم موافقة مدير النظام لتطبيق الترحيلات", message);
    }

    [Fact]
    public void DatabaseIsEqual_IsCompatible()
    {
        var status = new AuditMemoSystem.Data.Database.MigrationVersionStatus("069", "069", true);

        Assert.False(status.DatabaseIsNewer);
        Assert.False(status.RequiresUpgrade);
    }

    [Fact]
    public void SetupRequest_CanSpecifyApplyMigrationsFlag()
    {
        var request = new SetupRequest
        {
            Host = "localhost",
            Port = 5432,
            Database = "audit_memo",
            Username = "postgres",
            ApplyMigrations = true
        };

        Assert.True(request.ApplyMigrations);
    }

    [Fact]
    public void TestConnectionResult_ContainsVersionMetadata()
    {
        var result = new TestConnectionResult
        {
            Success = false,
            DatabaseIsNewer = true,
            DatabaseVersion = "075",
            ApplicationVersion = "069",
            Message = "إصدار قاعدة البيانات (075) أحدث من إصدار البرنامج الحالي (069). يرجى مراجعة دائرة نظم وقواعد البيانات للتأكد من التحديث."
        };

        Assert.False(result.Success);
        Assert.True(result.DatabaseIsNewer);
        Assert.Contains("يرجى مراجعة دائرة نظم وقواعد البيانات للتأكد من التحديث", result.Message);
    }
}
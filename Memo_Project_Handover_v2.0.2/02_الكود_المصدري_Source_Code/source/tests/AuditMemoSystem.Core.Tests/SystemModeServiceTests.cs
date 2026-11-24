using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Repositories;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class SystemModeServiceTests
{
    [Fact]
    public void LocalProfile_AlwaysReportsReadOnlyAndRejectsWrites()
    {
        var service = CreateService(ConnectionProfileType.LOCAL);

        Assert.Equal(AppMode.LOCAL_READONLY, service.CurrentMode);
        var exception = Assert.Throws<InvalidOperationException>(service.EnsureWritable);
        Assert.Contains("قراءة فقط", exception.Message);
    }

    [Fact]
    public void AuthorizedServerProfile_RemainsWritableByDefault()
    {
        var service = CreateService(ConnectionProfileType.SERVER);

        Assert.Equal(AppMode.SERVER, service.CurrentMode);
        Assert.Null(Record.Exception(service.EnsureWritable));
    }

    [Fact]
    public async Task AuditWrite_IsRejectedBeforeConnectingWhenProfileIsLocal()
    {
        var modeService = CreateService(ConnectionProfileType.LOCAL);
        var config = CreateConfig(ConnectionProfileType.LOCAL);
        var auditService = new AuditService(new DbConnectionFactory(config), modeService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auditService.WriteAsync(null, "TEST", null, null, null, null));

        Assert.Contains("قراءة فقط", exception.Message);
    }

    [Fact]
    public async Task BusinessWrite_IsRejectedBeforeConnectingWhenProfileIsLocal()
    {
        var config = CreateConfig(ConnectionProfileType.LOCAL);
        var factory = new DbConnectionFactory(config);
        var modeService = new SystemModeService(factory);
        var auditService = new AuditService(factory, modeService);
        var companyService = new CompanyService(factory, modeService, auditService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            companyService.SaveCompanyAsync(new CompanyModel { Name = "اختبار" }, Guid.NewGuid()));

        Assert.Contains("قراءة فقط", exception.Message);
    }

    [Fact]
    public async Task AuthenticationWrite_IsRejectedBeforeConnectingWhenProfileIsLocal()
    {
        var config = CreateConfig(ConnectionProfileType.LOCAL);
        var factory = new DbConnectionFactory(config);
        var modeService = new SystemModeService(factory);
        var auditService = new AuditService(factory, modeService);
        var authService = new AuthService(factory, auditService, modeService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authService.ChangeOwnPasswordAsync("temporary-value", "another-temporary-value"));

        Assert.Contains("قراءة فقط", exception.Message);
    }

    private static SystemModeService CreateService(ConnectionProfileType profileType)
    {
        return new SystemModeService(new DbConnectionFactory(CreateConfig(profileType)));
    }

    private static AppConfig CreateConfig(ConnectionProfileType profileType)
    {
        return new AppConfig
        {
            Database = new DatabaseSection
            {
                ActiveProfile = "Test",
                Profiles =
                [
                    new ConnectionProfileModel
                    {
                        Name = "Test",
                        Type = profileType,
                        Host = "localhost",
                        Database = "not_connected",
                        Username = "test"
                    }
                ]
            }
        };
    }
}

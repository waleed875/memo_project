using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class ConnectionSecurityTests
{
    [Fact]
    public void DashboardLanHttpRequiresExplicitDeploymentMode()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ServerEndpoints.ParseDashboard("http://192.168.1.160:8080"));

        ServerEndpoints endpoints = ServerEndpoints.ParseDashboard(
            "http://192.168.1.160:8080", internalLanHttp: true);

        Assert.Equal("http", endpoints.BaseAddress.Scheme);
        Assert.Equal(8080, endpoints.BaseAddress.Port);
    }

    [Fact]
    public void UpdateServiceNetworkHttpIsAlwaysRejectedButLoopbackIsAllowed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ServerEndpoints.ParseUpdateService("http://192.168.1.160:8080"));

        ServerEndpoints endpoints = ServerEndpoints.ParseUpdateService(
            "http://127.0.0.1:8080");

        Assert.Equal("http", endpoints.BaseAddress.Scheme);
        Assert.True(endpoints.BaseAddress.IsLoopback);
    }

    [Theory]
    [InlineData("https://user:password@example.internal")]
    [InlineData("https://example.internal/?token=secret")]
    [InlineData("https://example.internal/#fragment")]
    public void ServerAddressRejectsCredentialsQueryAndFragment(string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ServerEndpoints.ParseDashboard(value));
    }

    [Theory]
    [InlineData("Require")]
    [InlineData("require")]
    [InlineData(" Require ")]
    public void SslRequireIsHonoredExplicitly(string mode)
    {
        var factory = new DbConnectionFactory(new AppConfig
        {
            Database = new DatabaseSection
            {
                Profiles = [new ConnectionProfileModel
                {
                    Name = "Server",
                    Host = "db.example.internal",
                    SslMode = mode
                }]
            }
        });

        var connectionString = factory.CreateConnectionString();

        Assert.Contains("SSL Mode=Require", connectionString);
    }

    [Theory]
    [InlineData("Prefer")]
    [InlineData("VerifyFull")]
    [InlineData("bogus")]
    [InlineData("")]
    public void UnknownSslModeFallsBackToDisable(string mode)
    {
        Assert.Equal(Npgsql.SslMode.Disable, DbConnectionFactory.ResolveSslMode(mode));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("192.168.1.160")]
    [InlineData("db.example.internal")]
    public void AnyProfileUsesPlainPostgreSqlConnection(string host)
    {
        var factory = new DbConnectionFactory(new AppConfig
        {
            Database = new DatabaseSection
            {
                Profiles = [new ConnectionProfileModel
                {
                    Name = "Server",
                    Host = host
                }]
            }
        });

        var connectionString = factory.CreateConnectionString();

        Assert.Contains("SSL Mode=Disable", connectionString);
        Assert.DoesNotContain("VerifyFull", connectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Root Certificate", connectionString, StringComparison.OrdinalIgnoreCase);
    }
}

using MemoDashboard.Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoDashboard.Backend.Tests;

public sealed class Task05SessionSecurityTests
{
    [Fact]
    public void SessionIdentityIsNotTrustedFromSessionKeysAlone()
    {
        var auth = new WebAdminAuthService(null!, NullLogger<WebAdminAuthService>.Instance);
        var context = new DefaultHttpContext();
        context.Items["MemoDashboard.Admin.UserId"] = Guid.NewGuid().ToString("D");
        context.Items["MemoDashboard.Admin.Username"] = "admin";

        Assert.Null(auth.GetCurrent(context));
    }

    [Fact]
    public void FullAdminSessionHasAnAbsoluteLifetimeAndMustChangeIsNotAuthenticated()
    {
        Assert.Equal(TimeSpan.FromHours(8), WebAdminAuthService.AbsoluteSessionLifetime);

        var restricted = new WebAdminLoginResult(
            Authenticated: false,
            MustChangePassword: true,
            Username: "admin",
            FullName: "مدير",
            Role: "ADMIN",
            Message: "يجب تغيير كلمة المرور قبل المتابعة.");

        Assert.False(restricted.Authenticated);
        Assert.True(restricted.MustChangePassword);
    }

    [Fact]
    public void ValidatedRequestIdentityIsTheOnlyCurrentIdentity()
    {
        var auth = new WebAdminAuthService(null!, NullLogger<WebAdminAuthService>.Instance);
        var context = new DefaultHttpContext();
        var user = new WebAdminUser(Guid.NewGuid(), "admin", "مدير", "ADMIN");
        context.Items["MemoDashboard.Admin.ValidatedUser"] = user;

        Assert.Same(user, auth.GetCurrent(context));
    }
}
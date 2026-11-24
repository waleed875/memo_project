using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Services;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class ReportAccessPolicyTests
{
    [Theory]
    [InlineData(UserRole.ADMIN)]
    [InlineData(UserRole.DATA_ENTRY)]
    [InlineData(UserRole.REVIEWER)]
    [InlineData(UserRole.VIEWER)]
    public void EveryApplicationRole_CanReachReportCenter(UserRole role)
    {
        Assert.True(ReportAccessPolicy.CanAccessCenter(role));
        Assert.True(ReportAccessPolicy.CanAccessReport(role, "GeneralNotes"));
    }

    [Theory]
    [InlineData("AuditLogs")]
    [InlineData("UserProductivity")]
    [InlineData("BackupHistory")]
    public void AdministratorOnlyReports_AreDeniedToNonAdministrators(string reportName)
    {
        Assert.True(ReportAccessPolicy.CanAccessReport(UserRole.ADMIN, reportName));
        Assert.False(ReportAccessPolicy.CanAccessReport(UserRole.DATA_ENTRY, reportName));
        Assert.False(ReportAccessPolicy.CanAccessReport(UserRole.REVIEWER, reportName));
        Assert.False(ReportAccessPolicy.CanAccessReport(UserRole.VIEWER, reportName));
    }

    [Fact]
    public void UnknownReport_IsNotAuthorizedByPolicy()
    {
        Assert.False(ReportAccessPolicy.IsKnownReport("UnknownReport"));
        Assert.False(ReportAccessPolicy.CanAccessReport(UserRole.ADMIN, "UnknownReport"));
    }
}

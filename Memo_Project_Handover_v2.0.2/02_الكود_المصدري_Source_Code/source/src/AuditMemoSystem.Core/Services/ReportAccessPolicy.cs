using AuditMemoSystem.Core.Enums;

namespace AuditMemoSystem.Core.Services;

public static class ReportAccessPolicy
{
    private static readonly HashSet<string> StandardReports = new(StringComparer.Ordinal)
    {
        "GeneralNotes",
        "DashboardSummary",
        "DelaySummary",
        "DelayDetails",
        "SingleNote",
        "ErrorsByCompany",
        "ErrorsByField",
        "CompanyPerformance",
        "CompanyDetailed",
        "FactoryPerformance",
        "MonthlySummary",
        "UnknownFields",
        "Duplicates",
        "PendingReview",
        "ReturnedNotes",
        "NoteLinks",
        "MemoLogExport"
    };

    private static readonly HashSet<string> AdministratorReports = new(StringComparer.Ordinal)
    {
        "UserProductivity",
        "AuditLogs",
        "BackupHistory"
    };

    public static bool IsKnownReport(string reportName) =>
        StandardReports.Contains(reportName) || AdministratorReports.Contains(reportName);

    public static bool CanAccessReport(UserRole role, string reportName) =>
        StandardReports.Contains(reportName) ||
        (role == UserRole.ADMIN && AdministratorReports.Contains(reportName));

    public static bool CanAccessCenter(UserRole role) =>
        StandardReports.Any(reportName => CanAccessReport(role, reportName)) ||
        AdministratorReports.Any(reportName => CanAccessReport(role, reportName));
}

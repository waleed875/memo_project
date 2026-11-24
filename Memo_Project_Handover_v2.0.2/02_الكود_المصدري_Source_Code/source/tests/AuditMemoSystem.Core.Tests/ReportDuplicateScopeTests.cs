using System.Reflection;
using AuditMemoSystem.Data.Repositories;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class ReportDuplicateScopeTests
{
    private static string Correct(string reportName, string sql)
    {
        var method = typeof(ReportService).GetMethod(
            "CorrectDuplicateReferenceScope",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string)(method?.Invoke(null, new object[] { reportName, sql })
            ?? throw new InvalidOperationException("Duplicate scope corrector was not found."));
    }

    [Fact]
    public void DashboardSummaryUsesTheClassificationAwareReferenceNote()
    {
        const string input = """
            join core.notes matched_note on matched_note.id = di.matched_note_id
            where not matched_note.is_deleted
              and matched_note.id = @note_id
            """;

        var corrected = Correct("DashboardSummary", input);

        Assert.Contains(
            "coalesce(di.created_note_id, di.matched_note_id)",
            corrected,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("matched_note.note_id = @note_id", corrected);
        Assert.DoesNotContain("where not matched_note.is_deleted", corrected);
    }

    [Fact]
    public void CompanyDetailedPreservesResendsByFallingBackToMatchedNote()
    {
        const string input =
            "join core.notes resend_note on resend_note.id = di.created_note_id";

        var corrected = Correct("CompanyDetailed", input);

        Assert.Contains(
            "coalesce(di.created_note_id, di.matched_note_id)",
            corrected,
            StringComparison.OrdinalIgnoreCase);
    }
}

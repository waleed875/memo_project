using System.Reflection;
using MemoDashboard.Backend.Models;
using MemoDashboard.Backend.Services;
using Xunit;

namespace MemoDashboard.Backend.Tests;

public sealed class DashboardQueryContractTests
{
    private static string SnapshotSql()
    {
        var field = typeof(DashboardQueryService).GetField(
            "SnapshotSql",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string)(field?.GetValue(null)
            ?? throw new InvalidOperationException("SnapshotSql was not found."));
    }

    [Fact]
    public void DuplicateQueriesUseClassificationSpecificReferenceNotes()
    {
        var sql = SnapshotSql();

        Assert.Contains("CASE d.classification", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHEN 'COMPANY_DUPLICATE_NUMBER' THEN d.created_note_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHEN 'COMPANY_RESEND' THEN d.matched_note_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reference_note.is_deleted = FALSE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FieldAccuracyUsesCanonicalIndicatorFacts()
    {
        var sql = SnapshotSql();

        Assert.Contains("vw_note_indicator_facts_v3", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TotalFieldCount", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CorrectFieldCount", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UnknownFieldCount", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY f.company_id, c.name", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TopErrorFieldsUseCanonicalFactFlags()
    {
        var sql = SnapshotSql();

        Assert.Contains("f.is_error", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("f.is_applicable_reviewed", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("field_value BETWEEN 2 AND 6", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnapshotUsesOneCanonicalV4Contract()
    {
        var sql = SnapshotSql();

        Assert.Null(typeof(DashboardQueryService).GetField("ContractSummarySql", BindingFlags.NonPublic | BindingFlags.Static));
        Assert.Contains("fn_company_metrics_snapshot_v4_filtered", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("total_notes AS \"TotalNotes\"", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review_coverage_percentage AS \"ReviewCoveragePercentage\"", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("link_efficiency_percentage AS \"LinkEfficiencyPercentage\"", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@FactoryId, @ProductionLineId, @NoteTypeId, @Status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@EntryCutoffDate", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("date_trunc('month', n.arrival_at)::date", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY 1", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentStatusApprovalAndApprovedAtEventsRemainSeparate()
    {
        var sql = SnapshotSql();

        Assert.Contains("COUNT(*) FILTER (WHERE status = 'APPROVED') AS Approved", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("date_trunc('month', n.approved_at)::date AS Label", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("n.approved_at IS NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("n.approved_at >= @From", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("n.approved_at < @To + 1", sql, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void SnapshotDoesNotImposeHiddenRollingChartWindows()
    {
        var sql = SnapshotSql();

        Assert.DoesNotContain("WindowStart", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WindowEnd", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INTERVAL '5 months'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INTERVAL '11 months'", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompanyDirectoryKeepsActiveCompaniesWithZeroNotes()
    {
        var sql = SnapshotSql();

        Assert.Contains("FROM core.companies c", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT JOIN core.notes n ON n.company_id = c.id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT(n.id) AS NoteCount", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("c.is_active = TRUE AND c.is_deleted = FALSE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ActiveCompaniesTotal", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CompaniesWithNotes", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnapshotCarriesPreviousCompanyMetricsContract()
    {
        var sql = SnapshotSql();

        Assert.Contains("PreviousCompanyId", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PreviousEntryCutoffDate", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE @PreviousCompanyId IS NOT NULL AND @PreviousFrom IS NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(typeof(DashboardSnapshot).GetProperty(nameof(DashboardSnapshot.PreviousCompanyMetrics)));
        Assert.NotNull(typeof(DashboardSummary).GetProperty(nameof(DashboardSummary.ActiveCompaniesTotal)));
        Assert.NotNull(typeof(DashboardSummary).GetProperty(nameof(DashboardSummary.CompaniesWithNotes)));
        Assert.NotNull(typeof(DashboardSnapshot).GetProperty(nameof(DashboardSnapshot.Companies)));
        Assert.NotNull(typeof(DashboardSnapshot).GetProperty(nameof(DashboardSnapshot.FieldQuality)));
        Assert.NotNull(typeof(DashboardSnapshot).GetProperty(nameof(DashboardSnapshot.PreviousPeriod)));
    }
    [Fact]
    public void SnapshotResponseCarriesEffectiveFiltersAndCompanyMetrics()
    {
        Assert.NotNull(typeof(DashboardSnapshot).GetProperty(nameof(DashboardSnapshot.EffectiveFilters)));
        Assert.NotNull(typeof(DashboardSnapshot).GetProperty(nameof(DashboardSnapshot.CompanyMetrics)));
        Assert.NotNull(typeof(DashboardSnapshot).GetProperty(nameof(DashboardSnapshot.PreviousCompanyMetrics)));
        Assert.NotNull(typeof(DashboardSummary).GetProperty(nameof(DashboardSummary.ActiveCompaniesTotal)));
        Assert.NotNull(typeof(DashboardSummary).GetProperty(nameof(DashboardSummary.CompaniesWithNotes)));
        Assert.NotNull(typeof(DashboardSnapshot).GetProperty(nameof(DashboardSnapshot.Companies)));
        Assert.NotNull(typeof(DashboardSnapshot).GetProperty(nameof(DashboardSnapshot.FieldQuality)));
        Assert.NotNull(typeof(DashboardSnapshot).GetProperty(nameof(DashboardSnapshot.PreviousPeriod)));
        Assert.NotNull(typeof(DashboardEffectiveFilters).GetProperty(nameof(DashboardEffectiveFilters.EntryCutoffDate)));
    }
}


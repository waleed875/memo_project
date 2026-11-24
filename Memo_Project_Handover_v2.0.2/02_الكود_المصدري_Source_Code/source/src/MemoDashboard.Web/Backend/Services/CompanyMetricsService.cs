using System.Data;
using Dapper;
using MemoDashboard.Backend.Models;
using Npgsql;

namespace MemoDashboard.Backend.Services;

public interface ICompanyMetricsService
{
    Task<CompanyMetrics> GetAsync(Guid companyId, DateTime? from, DateTime? to, DateTime? entryCutoffDate, string dateBasis, CancellationToken cancellationToken);
}

public sealed class CompanyMetricsService : ICompanyMetricsService
{
    private const string MetricsSql = """
        select
            m.total_notes as TotalNotes,
            m.expected_items as ExpectedItems,
            m.reviewed_items as ReviewedItems,
            m.unreviewed_items as UnreviewedItems,
            m.applicable_reviewed_items as ApplicableReviewedItems,
            m.correct_items as CorrectItems,
            m.error_items as ErrorItems,
            m.not_applicable_items as NotApplicableItems,
            m.review_coverage_percentage as ReviewCoveragePercentage,
            m.item_quality_percentage as ItemQualityPercentage,
            m.errors_per_100_checked as ErrorsPer100Checked,
            m.quality_eligible_note_count as QualityEligibleNoteCount,
            m.clean_reviewed_notes as CleanReviewedNotes,
            m.error_notes as ErrorNotes,
            m.incomplete_review_notes as IncompleteReviewNotes,
            m.all_not_applicable_note_count as AllNotApplicableNoteCount,
            m.no_expected_items_note_count as NoExpectedItemsNoteCount,
            m.clean_reviewed_note_percent as CleanReviewedNotePercent,
            m.time_eligible_notes as TimeEligibleNotes,
            m.on_time_notes as OnTimeNotes,
            m.late_notes as LateNotes,
            m.late_6_to_10_notes as Late6To10Notes,
            m.late_11_to_20_notes as Late11To20Notes,
            m.late_over_20_notes as LateOver20Notes,
            m.missing_writing_date_notes as MissingWritingDateNotes,
            m.invalid_date_order_notes as InvalidDateOrderNotes,
            m.time_compliance_percentage as TimeCompliancePercentage,
            m.average_late_days as AverageLateDays,
            m.median_late_days as MedianLateDays,
            m.p90_late_days as P90LateDays,
            m.maximum_late_days as MaximumLateDays,
            m.average_excess_delay_days as AverageExcessDelayDays,
            m.duplicate_number_incidents as DuplicateNumberIncidents,
            m.company_resend_incidents as CompanyResendIncidents,
            m.duplicate_incidents as DuplicateIncidents,
            m.affected_notes as AffectedNotes,
            m.incidents_per_100_notes as IncidentsPer100Notes,
            m.affected_note_rate_percentage as AffectedNoteRatePercentage,
            m.duplicate_free_percentage as DuplicateFreePercentage,
            m.entry_notes as EntryNotes,
            m.linked_entry_notes as LinkedEntryNotes,
            m.unlinked_entry_notes as UnlinkedEntryNotes,
            m.link_efficiency_percentage as LinkEfficiencyPercentage
        from reporting.fn_company_metrics_snapshot_v4(@CompanyId, @From, @To, @EntryCutoffDate, @DateBasis) m;
        """;

    private readonly ConfigurationService _configuration;

    public CompanyMetricsService(ConfigurationService configuration)
    {
        _configuration = configuration;
    }

    public async Task<CompanyMetrics> GetAsync(
        Guid companyId,
        DateTime? from,
        DateTime? to,
        DateTime? entryCutoffDate,
        string dateBasis,
        CancellationToken cancellationToken)
    {
        var connectionString = _configuration.BuildConnectionString()
            ?? throw new InvalidOperationException("Dashboard database connection is not configured.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        // NOTE: DynamicParameters with explicit DbTypes — Dapper cannot infer
        // DateOnly members of anonymous objects (NotSupportedException).
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId, DbType.Guid);
        parameters.Add("From", from?.Date, DbType.Date);
        parameters.Add("To", to?.Date, DbType.Date);
        parameters.Add("EntryCutoffDate", entryCutoffDate?.Date, DbType.Date);
        parameters.Add("DateBasis", dateBasis, DbType.String);
        var command = new CommandDefinition(
            MetricsSql,
            parameters,
            commandTimeout: 30,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<CompanyMetrics>(command);
    }
}

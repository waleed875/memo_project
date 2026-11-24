using System.Data;
using Dapper;
using MemoDashboard.Backend.Models;
using Npgsql;

namespace MemoDashboard.Backend.Services;

/// <summary>
/// Web counterpart of ReportService.GetCompanyDetailedTableV3Async (desktop).
/// Same SQL semantics (V3 scope: company + period + cutoff + basis only),
/// same formulas via <see cref="CompanyReportKpis"/>. Read-only: no migrations.
/// </summary>
public sealed class CompanyReportService
{
    static CompanyReportService()
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    private const string ScopeSql = """
        select count(distinct n.factory_id)::int as factory_count,
               max(n.arrival_at)::timestamp as last_arrival_at
        from core.notes n
        where n.company_id = @company_id
          and not n.is_deleted
          and n.arrival_at between @from and @to
          and (@entry_cutoff_date is null or n.created_at < (@entry_cutoff_date + 1)::timestamp)
        """;

    private const string CompanyNameSql = """
        select c.name from core.companies c where c.id = @company_id
        """;

    private const string MonthlySql = """
        select month_start, period_label, total_notes,
               item_quality_percentage, clean_reviewed_note_percent
        from reporting.fn_company_month_series_v4(@company_id, @from, @to, @entry_cutoff_date, @date_basis)
        order by month_start
        """;

    private const string MonthlyCleanSql = """
        with scoped_notes as (
            select n.id, n.arrival_at, n.writing_date
            from core.notes n
            where n.company_id = @company_id
              and not n.is_deleted
              and n.arrival_at between @from and @to
              and (@entry_cutoff_date is null or n.created_at < (@entry_cutoff_date + 1)::timestamp)
        ), note_quality as (
            select
                n.id,
                date_trunc('month', n.arrival_at)::date as month_start,
                count(f.field_id)::int as expected_items,
                count(f.field_id) filter (where f.is_unreviewed)::int as unreviewed_items,
                count(f.field_id) filter (where f.is_applicable_reviewed)::int as applicable_items,
                count(f.field_id) filter (where f.is_error)::int as error_items
            from scoped_notes n
            left join reporting.vw_note_indicator_facts_v3 f on f.note_id = n.id
            group by n.id, date_trunc('month', n.arrival_at)::date
        )
        select
            to_char(q.month_start, 'YYYY/MM') as period_label,
            count(*) filter (
                where q.expected_items > 0
                  and q.unreviewed_items = 0
                  and q.error_items = 0
                  and q.applicable_items > 0
            )::int as clean_reviewed_notes
        from note_quality q
        where q.month_start is not null
        group by q.month_start
        order by q.month_start
        """;

    private const string FieldErrorSql = """
        select
            f.field_name as field_label,
            count(*) filter (where f.is_correct)::int as CorrectItems,
            count(*) filter (where f.is_error)::int as ErrorItems,
            count(*) filter (where f.is_unreviewed)::int as UnreviewedItems,
            count(*) filter (where f.is_not_applicable)::int as NotApplicableItems,
            count(*) filter (where f.is_applicable_reviewed)::int as CheckedItems,
            round(
                count(*) filter (where f.is_error)::numeric * 100 /
                nullif(count(*) filter (where f.is_applicable_reviewed), 0), 2) as ErrorRate
        from reporting.vw_note_indicator_facts_v3 f
        where f.company_id = @company_id
          and f.arrival_at between @from and @to
          and (@entry_cutoff_date is null or f.created_at < (@entry_cutoff_date + 1)::timestamp)
        group by f.field_id, f.field_name
        having count(*) filter (where f.is_error) > 0
        order by ErrorItems desc, f.field_name
        limit 8
        """;

    private const string FactorySql = """
        with scoped_notes as (
            select n.id, n.factory_id, n.writing_date, n.arrival_at
            from core.notes n
            where n.company_id = @company_id
              and not n.is_deleted
              and n.arrival_at between @from and @to
              and (@entry_cutoff_date is null or n.created_at < (@entry_cutoff_date + 1)::timestamp)
        ), note_quality as (
            select
                n.id,
                n.factory_id,
                n.writing_date,
                n.arrival_at,
                count(f.field_id)::int as expected_items,
                count(f.field_id) filter (where f.is_unreviewed)::int as unreviewed_items,
                count(f.field_id) filter (where f.is_applicable_reviewed)::int as applicable_items,
                count(f.field_id) filter (where f.is_correct)::int as correct_items,
                count(f.field_id) filter (where f.is_error)::int as error_items
            from scoped_notes n
            left join reporting.vw_note_indicator_facts_v3 f on f.note_id = n.id
            group by n.id, n.factory_id, n.writing_date, n.arrival_at
        )
        select
            coalesce(factory.name, 'غير محدد') as factory_label,
            coalesce(factory.name, 'غير محدد') as factory_name,
            factory.id as factory_id,
            count(q.id)::int as total,
            count(q.id)::int as total_notes,
            count(q.id) filter (
                where q.expected_items > 0
                  and q.unreviewed_items = 0
                  and q.error_items = 0
                  and q.applicable_items > 0
            )::int as clean_notes,
            count(q.id) filter (
                where q.expected_items > 0
                  and q.unreviewed_items = 0
                  and q.error_items = 0
                  and q.applicable_items > 0
            )::int as clean_reviewed_notes,
            round(
                count(q.id) filter (
                    where q.expected_items > 0
                      and q.unreviewed_items = 0
                      and q.error_items = 0
                      and q.applicable_items > 0
                )::numeric * 100 / nullif(count(q.id), 0), 2) as clean_reviewed_note_percent,
            count(q.id) filter (where q.error_items > 0)::int as error_notes,
            count(q.id) filter (where q.error_items = 0 and q.unreviewed_items > 0)::int as incomplete_notes,
            count(q.id) filter (
                where q.writing_date is not null
                  and q.arrival_at >= q.writing_date
                  and q.arrival_at - q.writing_date between 0 and 5
            )::int as on_time,
            count(q.id) filter (
                where q.writing_date is not null
                  and q.arrival_at >= q.writing_date
                  and q.arrival_at - q.writing_date > 5
            )::int as late,
            round(sum(q.correct_items)::numeric * 100 / nullif(sum(q.applicable_items), 0), 2)
                as quality_percentage
        from note_quality q
        left join core.factories factory on factory.id = q.factory_id
        group by factory.id, factory.name
        order by total desc, factory_label
        limit 12
        """;

    private const string IdentitySql = """
        select republic_name_ar as RepublicNameArabic, republic_name_en as RepublicNameEnglish,
               ministry_name as MinistryName, ministry_name_en as MinistryNameEnglish,
               company_name as CompanyName, company_name_en as CompanyNameEnglish,
               administrative_level_one as AdministrativeLevelOne, administrative_level_one_en as AdministrativeLevelOneEnglish,
               administrative_level_two as AdministrativeLevelTwo, administrative_level_two_en as AdministrativeLevelTwoEnglish,
               administrative_level_three as AdministrativeLevelThree, administrative_level_three_en as AdministrativeLevelThreeEnglish,
               issuing_entity_name as IssuingEntityName, default_city as DefaultCity
        from system.report_identity_settings
        where singleton = true
        """;

    private readonly ConfigurationService _configuration;
    private readonly ICompanyMetricsService _metrics;
    private readonly ILogger<CompanyReportService> _logger;

    public CompanyReportService(
        ConfigurationService configuration,
        ICompanyMetricsService metrics,
        ILogger<CompanyReportService> logger)
    {
        _configuration = configuration;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<CompanyReportData> GetAsync(CompanyReportQuery query, CancellationToken cancellationToken = default)
    {
        if (query.CompanyId is null)
        {
            throw new ArgumentException("اختر شركة أولاً لعرض التقرير التفصيلي.", nameof(query));
        }
        if (query.FactoryId.HasValue || query.ProductionLineId.HasValue || query.NoteTypeId.HasValue ||
            !string.IsNullOrWhiteSpace(query.Status))
        {
            throw new ArgumentException(
                "التقرير التفصيلي للشركة يستخدم نطاق الشركة والفترة فقط؛ أزل المرشحات الإضافية (مصنع/خط/نوع/حالة).", nameof(query));
        }
        if (!query.From.HasValue || !query.To.HasValue)
        {
            throw new ArgumentException("التقرير التفصيلي يتطلب فترة محددة (من/إلى).", nameof(query));
        }
        var from = query.From.Value.Date;
        var to = query.To.Value.Date;
        if (from > to)
        {
            throw new ArgumentException("يجب ألا يكون تاريخ البداية بعد تاريخ النهاية.", nameof(query));
        }

        var dateBasis = string.Equals(query.DateBasis, "ArrivalDate", StringComparison.OrdinalIgnoreCase)
            ? "ArrivalDate"
            : "WritingDate";
        var entryCutoff = query.EntryCutoffDate?.Date;
        var companyId = query.CompanyId.Value;

        var connectionString = _configuration.BuildConnectionString()
            ?? throw new InvalidOperationException("لم يتم إعداد الاتصال بقاعدة البيانات بعد.");

        // Desktop V3 month series spans the 6 months ending at the To month.
        var selectedMonth = new DateOnly(to.Year, to.Month, 1);
        var seriesFrom = selectedMonth.AddMonths(-5);
        var seriesTo = selectedMonth.AddMonths(1).AddDays(-1);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var companyName = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(CompanyNameSql, new { company_id = companyId }, commandTimeout: 30, cancellationToken: cancellationToken));
        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new ArgumentException("تعذر تحميل الشركة المحددة للتقرير التفصيلي.", nameof(query));
        }

        var metrics = await _metrics.GetAsync(companyId, from, to, entryCutoff, dateBasis, cancellationToken);

        var scope = await connection.QuerySingleAsync<ScopeRaw>(
            new CommandDefinition(ApplyDateBasis(ScopeSql, dateBasis), PeriodParameters(companyId, from, to, entryCutoff, dateBasis), commandTimeout: 30, cancellationToken: cancellationToken));

        var monthly = await QueryMonthlyAsync(connection, companyId, seriesFrom, seriesTo, entryCutoff, dateBasis, cancellationToken);

        var fields = (await connection.QueryAsync<CompanyReportFieldRow>(
            new CommandDefinition(ApplyDateBasis(FieldErrorSql, dateBasis), PeriodParameters(companyId, from, to, entryCutoff, dateBasis), commandTimeout: 30, cancellationToken: cancellationToken))).ToList();

        var factories = (await connection.QueryAsync<CompanyReportFactoryRow>(
            new CommandDefinition(ApplyDateBasis(FactorySql, dateBasis), PeriodParameters(companyId, from, to, entryCutoff, dateBasis), commandTimeout: 30, cancellationToken: cancellationToken))).ToList();

        var identity = await connection.QuerySingleOrDefaultAsync<CompanyReportIdentity>(
            new CommandDefinition(IdentitySql, commandTimeout: 30, cancellationToken: cancellationToken))
            ?? new CompanyReportIdentity();

        return new CompanyReportData
        {
            CompanyId = companyId,
            CompanyName = companyName,
            From = from,
            To = to,
            DateBasis = dateBasis,
            EntryCutoffDate = entryCutoff,
            Metrics = metrics,
            FactoryCount = scope.FactoryCount,
            LastArrivalAt = scope.LastArrivalAt,
            Monthly = monthly,
            FieldErrors = fields,
            Factories = factories,
            Identity = identity,
            FilterSummary = BuildFilterSummary(from, to, dateBasis, entryCutoff)
        };
    }

    /// <summary>
    /// Monthly quality/volume series for the Phase-3 charts. Unlike the full
    /// report, the period is optional and defaults to the 6 months ending now.
    /// </summary>
    public async Task<List<CompanyReportMonthRow>> GetMonthlyAsync(
        Guid companyId,
        DateTime? from,
        DateTime? to,
        DateTime? entryCutoffDate,
        string dateBasis,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("اختر شركة أولاً لعرض التطور الشهري.", nameof(companyId));
        }
        var effectiveTo = (to ?? DashboardQueryService.GetBusinessNow()).Date;
        var effectiveFrom = (from ?? new DateTime(effectiveTo.Year, effectiveTo.Month, 1).AddMonths(-5)).Date;
        if (effectiveFrom > effectiveTo)
        {
            throw new ArgumentException("يجب ألا يكون تاريخ البداية بعد تاريخ النهاية.", nameof(from));
        }

        var normalizedBasis = string.Equals(dateBasis, "ArrivalDate", StringComparison.OrdinalIgnoreCase)
            ? "ArrivalDate"
            : "WritingDate";
        var cutoff = entryCutoffDate?.Date;
        var selectedMonth = new DateOnly(effectiveTo.Year, effectiveTo.Month, 1);
        var seriesFrom = selectedMonth.AddMonths(-5);
        var seriesTo = selectedMonth.AddMonths(1).AddDays(-1);

        var connectionString = _configuration.BuildConnectionString()
            ?? throw new InvalidOperationException("لم يتم إعداد الاتصال بقاعدة البيانات بعد.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await QueryMonthlyAsync(connection, companyId, seriesFrom, seriesTo, cutoff, normalizedBasis, cancellationToken);
    }

    private static async Task<List<CompanyReportMonthRow>> QueryMonthlyAsync(
        NpgsqlConnection connection,
        Guid companyId,
        DateOnly seriesFrom,
        DateOnly seriesTo,
        DateTime? entryCutoff,
        string dateBasis,
        CancellationToken cancellationToken)
    {
        var monthly = (await connection.QueryAsync<CompanyReportMonthRow>(
            new CommandDefinition(MonthlySql, SeriesParameters(companyId, seriesFrom, seriesTo, entryCutoff, dateBasis), commandTimeout: 30, cancellationToken: cancellationToken))).ToList();

        var cleanCounts = await connection.QueryAsync<CleanRaw>(
            new CommandDefinition(ApplyDateBasis(MonthlyCleanSql, dateBasis), SeriesParameters(companyId, seriesFrom, seriesTo, entryCutoff, dateBasis), commandTimeout: 30, cancellationToken: cancellationToken));
        var cleanByLabel = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in cleanCounts)
        {
            if (!string.IsNullOrWhiteSpace(r.PeriodLabel))
            {
                cleanByLabel[r.PeriodLabel] = r.CleanReviewedNotes;
            }
        }
        foreach (var row in monthly)
        {
            row.CleanReviewedNotes = cleanByLabel.TryGetValue(row.PeriodLabel, out var clean) ? clean : 0;
        }
        return monthly;
    }

    /// <summary>Desktop-parity filter summary (FrmReportViewer, CompanyDetailed).</summary>
    public static string BuildFilterSummary(DateTime from, DateTime to, string dateBasis, DateTime? entryCutoff)
    {
        // Byte-parity with FrmReportViewer.BuildFilterSummaryAsync for CompanyDetailed.
        var parts = new List<string> { $"الفترة: {from:yyyy/MM/dd} - {to:yyyy/MM/dd}" };
        parts.Add(string.Equals(dateBasis, "ArrivalDate", StringComparison.Ordinal)
            ? "أساس التاريخ: حسب تاريخ الوصول"
            : "أساس التاريخ: حسب تاريخ الكتابة");
        if (entryCutoff.HasValue) parts.Add($"تاريخ قطع الوصول: {entryCutoff.Value:yyyy/MM/dd}");
        return string.Join(" | ", parts);
    }

    private static DynamicParameters PeriodParameters(Guid companyId, DateTime from, DateTime to, DateTime? cutoff, string dateBasis)
    {
        var parameters = new DynamicParameters();
        parameters.Add("company_id", companyId, DbType.Guid);
        parameters.Add("from", DateOnly.FromDateTime(from), DbType.Date);
        parameters.Add("to", DateOnly.FromDateTime(to), DbType.Date);
        parameters.Add("entry_cutoff_date", cutoff.HasValue ? DateOnly.FromDateTime(cutoff.Value) : null, DbType.Date);
        parameters.Add("date_basis", dateBasis, DbType.String);
        return parameters;
    }

    private static DynamicParameters SeriesParameters(Guid companyId, DateOnly from, DateOnly to, DateTime? cutoff, string dateBasis)
    {
        var parameters = new DynamicParameters();
        parameters.Add("company_id", companyId, DbType.Guid);
        parameters.Add("from", from, DbType.Date);
        parameters.Add("to", to, DbType.Date);
        parameters.Add("entry_cutoff_date", cutoff.HasValue ? DateOnly.FromDateTime(cutoff.Value) : null, DbType.Date);
        parameters.Add("date_basis", dateBasis, DbType.String);
        return parameters;
    }

    /// <summary>Mirrors ReportService.ApplyCompanyDateBasis (desktop) anchor-for-anchor.</summary>
    public static string ApplyDateBasis(string sql, string dateBasis)
    {
        var period = string.Equals(dateBasis, "WritingDate", StringComparison.Ordinal) ? "writing_date::date" : "arrival_at::date";
        return sql
            .Replace("n.arrival_at between @from and @to", $"n.{period} between @from and @to", StringComparison.Ordinal)
            .Replace("f.arrival_at between @from and @to", $"f.{period} between @from and @to", StringComparison.Ordinal)
            .Replace("n.created_at < (@entry_cutoff_date + 1)::timestamp", "n.arrival_at::date <= @entry_cutoff_date", StringComparison.Ordinal)
            .Replace("f.created_at < (@entry_cutoff_date + 1)::timestamp", "f.arrival_at::date <= @entry_cutoff_date", StringComparison.Ordinal)
            .Replace("date_trunc('month', n.arrival_at)", $"date_trunc('month', n.{period})", StringComparison.Ordinal);
    }

    public sealed class ScopeRaw
    {
        public int FactoryCount { get; set; }

        public DateTime? LastArrivalAt { get; set; }
    }

    public sealed class CleanRaw
    {
        public string PeriodLabel { get; set; } = string.Empty;

        public int CleanReviewedNotes { get; set; }
    }
}

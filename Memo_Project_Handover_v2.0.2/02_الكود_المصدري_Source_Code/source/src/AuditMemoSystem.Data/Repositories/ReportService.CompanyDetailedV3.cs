using System.Data;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using Npgsql;
using NpgsqlTypes;

namespace AuditMemoSystem.Data.Repositories;

public sealed partial class ReportService
{
    private static readonly (string Name, Type Type)[] CompanyDetailedV3Columns =
    [
        ("row_kind", typeof(string)),
        ("company_name", typeof(string)),
        ("month_start", typeof(DateTime)),
        ("period_label", typeof(string)),
        ("total_notes", typeof(int)),
        ("expected_items", typeof(int)),
        ("reviewed_items", typeof(int)),
        ("unreviewed_items", typeof(int)),
        ("applicable_reviewed_items", typeof(int)),
        ("correct_items", typeof(int)),
        ("error_items", typeof(int)),
        ("not_applicable_items", typeof(int)),
        ("review_coverage_percentage", typeof(decimal)),
        ("item_quality_percentage", typeof(decimal)),
        ("errors_per_100_checked", typeof(decimal)),
        ("quality_eligible_note_count", typeof(int)),
        ("clean_reviewed_notes", typeof(int)),
        ("all_not_applicable_note_count", typeof(int)),
        ("no_expected_items_note_count", typeof(int)),
        ("clean_reviewed_note_percent", typeof(decimal)),
        ("clean_notes", typeof(int)),
        ("average_accuracy", typeof(decimal)),
        ("company_resend_notes", typeof(int)),
        ("duplicate_notes", typeof(int)),
        ("error_notes", typeof(int)),
        ("incomplete_review_notes", typeof(int)),
        ("time_eligible_notes", typeof(int)),
        ("on_time_notes", typeof(int)),
        ("late_notes", typeof(int)),
        ("late_6_to_10_notes", typeof(int)),
        ("late_11_to_20_notes", typeof(int)),
        ("late_over_20_notes", typeof(int)),
        ("missing_writing_date_notes", typeof(int)),
        ("invalid_date_order_notes", typeof(int)),
        ("time_compliance_percentage", typeof(decimal)),
        ("average_late_days", typeof(decimal)),
        ("median_late_days", typeof(decimal)),
        ("p90_late_days", typeof(decimal)),
        ("maximum_late_days", typeof(int)),
        ("average_excess_delay_days", typeof(decimal)),
        ("duplicate_number_incidents", typeof(int)),
        ("company_resend_incidents", typeof(int)),
        ("duplicate_incidents", typeof(int)),
        ("affected_notes", typeof(int)),
        ("incidents_per_100_notes", typeof(decimal)),
        ("affected_note_rate_percentage", typeof(decimal)),
        ("duplicate_free_percentage", typeof(decimal)),
        ("entry_notes", typeof(int)),
        ("linked_entry_notes", typeof(int)),
        ("unlinked_entry_notes", typeof(int)),
        ("link_efficiency_percentage", typeof(decimal)),
        ("factory_count", typeof(int)),
        ("last_arrival_at", typeof(DateTime)),
        ("field_label", typeof(string)),
        ("field_correct_items", typeof(int)),
        ("field_error_items", typeof(int)),
        ("field_unreviewed_items", typeof(int)),
        ("field_not_applicable_items", typeof(int)),
        ("field_checked_items", typeof(int)),
        ("field_error_rate", typeof(decimal)),
        ("factory_label", typeof(string)),
        ("factory_total", typeof(int)),
        ("factory_clean_notes", typeof(int)),
        ("factory_error_notes", typeof(int)),
        ("factory_incomplete_notes", typeof(int)),
        ("factory_on_time", typeof(int)),
        ("factory_late", typeof(int)),
        ("factory_quality_percentage", typeof(decimal))
    ];

    private async Task<DataTable> GetCompanyDetailedTableV3Async(
        ReportFilterModel filter,
        CancellationToken cancellationToken)
    {
        if (filter.FactoryId.HasValue || filter.ProductionLineId.HasValue || filter.NoteTypeId.HasValue ||
            filter.NoteId.HasValue || filter.CreatedBy.HasValue || filter.Status.HasValue ||
            filter.HasWritingDate.HasValue || filter.UnlinkedOnly.HasValue)
        {
            throw new InvalidOperationException(
                "التقرير التفصيلي للشركة يستخدم نطاق الشركة وتاريخ الوصول فقط؛ أزل المرشحات الإضافية لتجنب خلط نطاقات المؤشرات.");
        }

        var companyId = filter.CompanyId!.Value;
        var selectedMonth = new DateOnly(filter.To.Year, filter.To.Month, 1);
        var seriesFrom = selectedMonth.AddMonths(-5);
        var seriesTo = selectedMonth.AddMonths(1).AddDays(-1);
        var table = CreateCompanyDetailedV3Table();

        await using var connection = base.Factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await AppendCompanyDetailedRowsAsyncV3(connection, table, "summary", """
select
    c.name as company_name,
    m.*,
    scope.factory_count,
    scope.last_arrival_at
from core.companies c
cross join lateral reporting.fn_company_metrics_snapshot_v4(@company_id, @from, @to, @entry_cutoff_date, @date_basis) m
cross join lateral (
    select count(distinct n.factory_id)::int as factory_count,
           max(n.arrival_at)::timestamp as last_arrival_at
    from core.notes n
    where n.company_id = @company_id
      and not n.is_deleted
      and n.arrival_at between @from and @to
      and (@entry_cutoff_date is null or n.created_at < (@entry_cutoff_date + 1)::timestamp)
) scope
where c.id = @company_id
""", companyId, filter.From, filter.To, filter.EntryCutoffDate, filter.DateBasis, cancellationToken);

        if (!table.AsEnumerable().Any(row => FieldText(row, "row_kind") == "summary"))
        {
            throw new InvalidOperationException("تعذر تحميل الشركة المحددة للتقرير التفصيلي.");
        }

        await AppendCompanyDetailedRowsAsyncV3(connection, table, "monthly", """
select *
from reporting.fn_company_month_series_v4(@company_id, @from, @to, @entry_cutoff_date, @date_basis)
order by month_start
""", companyId, seriesFrom, seriesTo, filter.EntryCutoffDate, filter.DateBasis, cancellationToken);

        await PopulateMonthlyCleanCountsAsyncV3(connection, table, companyId, seriesFrom, seriesTo, filter.EntryCutoffDate, filter.DateBasis, cancellationToken);
        await AppendCompanyDetailedRowsAsyncV3(connection, table, "field_error", """
select
    f.field_name as field_label,
    count(*) filter (where f.is_correct)::int as field_correct_items,
    count(*) filter (where f.is_error)::int as field_error_items,
    count(*) filter (where f.is_unreviewed)::int as field_unreviewed_items,
    count(*) filter (where f.is_not_applicable)::int as field_not_applicable_items,
    count(*) filter (where f.is_applicable_reviewed)::int as field_checked_items,
    round(
        count(*) filter (where f.is_error)::numeric * 100 /
        nullif(count(*) filter (where f.is_applicable_reviewed), 0), 2) as field_error_rate
from reporting.vw_note_indicator_facts_v3 f
where f.company_id = @company_id
  and f.arrival_at between @from and @to
  and (@entry_cutoff_date is null or f.created_at < (@entry_cutoff_date + 1)::timestamp)
group by f.field_id, f.field_name
having count(*) filter (where f.is_error) > 0
order by field_error_items desc, f.field_name
limit 8
""", companyId, filter.From, filter.To, filter.EntryCutoffDate, filter.DateBasis, cancellationToken);

        await AppendCompanyDetailedRowsAsyncV3(connection, table, "factory", """
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
    count(*)::int as factory_total,
    count(*) filter (
        where q.expected_items > 0
          and q.unreviewed_items = 0
          and q.error_items = 0
          and q.applicable_items > 0
    )::int as factory_clean_notes,
    count(*) filter (where q.error_items > 0)::int as factory_error_notes,
    count(*) filter (where q.error_items = 0 and q.unreviewed_items > 0)::int as factory_incomplete_notes,
    count(*) filter (
        where q.writing_date is not null
          and q.arrival_at >= q.writing_date
          and q.arrival_at - q.writing_date between 0 and 5
    )::int as factory_on_time,
    count(*) filter (
        where q.writing_date is not null
          and q.arrival_at >= q.writing_date
          and q.arrival_at - q.writing_date > 5
    )::int as factory_late,
    round(sum(q.correct_items)::numeric * 100 / nullif(sum(q.applicable_items), 0), 2)
        as factory_quality_percentage
from note_quality q
left join core.factories factory on factory.id = q.factory_id
group by factory.id, factory.name
order by factory_total desc, factory_label
limit 8
""", companyId, filter.From, filter.To, filter.EntryCutoffDate, filter.DateBasis, cancellationToken);

        ApplyCompanyDetailedCompatibilityAliasesV3(table);
        return table;
    }

    private static DataTable CreateCompanyDetailedV3Table()
    {
        var table = new DataTable("CompanyDetailed");
        foreach (var (name, type) in CompanyDetailedV3Columns)
        {
            table.Columns.Add(name, type);
        }
        return table;
    }

    private static void ApplyCompanyDetailedCompatibilityAliasesV3(DataTable table)
    {
        var summary = table.AsEnumerable().First(row => FieldText(row, "row_kind") == "summary");
        CopyCompanyDetailedAliasV3(summary, "clean_reviewed_notes", "clean_notes");
        CopyCompanyDetailedAliasV3(summary, "item_quality_percentage", "average_accuracy");
        CopyCompanyDetailedAliasV3(summary, "company_resend_incidents", "company_resend_notes");
        CopyCompanyDetailedAliasV3(summary, "duplicate_incidents", "duplicate_notes");
    }

    private static void CopyCompanyDetailedAliasV3(DataRow row, string source, string target)
    {
        if (!row.IsNull(source))
        {
            row[target] = row[source];
        }
    }

    private static async Task AppendCompanyDetailedRowsAsyncV3(
        NpgsqlConnection connection,
        DataTable table,
        string rowKind,
        string sql,
        Guid companyId,
        DateOnly from,
        DateOnly to,
        DateOnly? entryCutoffDate,
        ReportDateBasis dateBasis,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ApplyCompanyDateBasis(sql, dateBasis), connection) { CommandTimeout = 30 };
        AddCompanyDetailedPeriodParametersV3(command, companyId, from, to, entryCutoffDate, dateBasis);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var fields = Enumerable.Range(0, reader.FieldCount)
            .Select(index => (Name: reader.GetName(index), Index: index))
            .Where(field => table.Columns.Contains(field.Name))
            .ToArray();

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = table.NewRow();
            row["row_kind"] = rowKind;
            foreach (var field in fields)
            {
                if (reader.IsDBNull(field.Index)) continue;
                SetCompanyDetailedValueV3(row, field.Name, reader.GetValue(field.Index));
            }
            table.Rows.Add(row);
        }
    }

    private static async Task PopulateMonthlyCleanCountsAsyncV3(
        NpgsqlConnection connection,
        DataTable table,
        Guid companyId,
        DateOnly from,
        DateOnly to,
        DateOnly? entryCutoffDate,
        ReportDateBasis dateBasis,
        CancellationToken cancellationToken)
    {
        const string sql = """
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
group by q.month_start
order by q.month_start
""";

        var monthlyRows = table.AsEnumerable()
            .Where(row => FieldText(row, "row_kind") == "monthly")
            .ToDictionary(row => FieldText(row, "period_label"), StringComparer.Ordinal);
        foreach (var row in monthlyRows.Values) row["clean_reviewed_notes"] = 0;

        await using var command = new NpgsqlCommand(ApplyCompanyDateBasis(sql, dateBasis), connection) { CommandTimeout = 30 };
        AddCompanyDetailedPeriodParametersV3(command, companyId, from, to, entryCutoffDate, dateBasis);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var labelOrdinal = reader.GetOrdinal("period_label");
        var cleanOrdinal = reader.GetOrdinal("clean_reviewed_notes");
        while (await reader.ReadAsync(cancellationToken))
        {
            var label = reader.GetString(labelOrdinal);
            if (monthlyRows.TryGetValue(label, out var row))
            {
                row["clean_reviewed_notes"] = reader.GetInt32(cleanOrdinal);
            }
        }
    }

    private static void AddCompanyDetailedPeriodParametersV3(
        NpgsqlCommand command,
        Guid companyId,
        DateOnly from,
        DateOnly to,
        DateOnly? entryCutoffDate,
        ReportDateBasis dateBasis)
    {
        command.Parameters.Add("company_id", NpgsqlDbType.Uuid).Value = companyId;
        command.Parameters.Add("from", NpgsqlDbType.Date).Value = from;
        command.Parameters.Add("to", NpgsqlDbType.Date).Value = to;
        command.Parameters.Add("entry_cutoff_date", NpgsqlDbType.Date).Value = entryCutoffDate.HasValue ? (object)entryCutoffDate.Value : DBNull.Value;
        command.Parameters.Add("date_basis", NpgsqlDbType.Varchar).Value = dateBasis == ReportDateBasis.WritingDate ? "WritingDate" : "ArrivalDate";
    }

    private static string ApplyCompanyDateBasis(string sql, ReportDateBasis dateBasis)
    {
        var period = dateBasis == ReportDateBasis.WritingDate ? "writing_date::date" : "arrival_at::date";
        return sql
            .Replace("n.arrival_at between @from and @to", $"n.{period} between @from and @to", StringComparison.Ordinal)
            .Replace("f.arrival_at between @from and @to", $"f.{period} between @from and @to", StringComparison.Ordinal)
            .Replace("n.created_at < (@entry_cutoff_date + 1)::timestamp", "n.arrival_at::date <= @entry_cutoff_date", StringComparison.Ordinal)
            .Replace("f.created_at < (@entry_cutoff_date + 1)::timestamp", "f.arrival_at::date <= @entry_cutoff_date", StringComparison.Ordinal)
            .Replace("date_trunc('month', n.arrival_at)", $"date_trunc('month', n.{period})", StringComparison.Ordinal);
    }

    private static void SetCompanyDetailedValueV3(DataRow row, string columnName, object value)
    {
        var targetType = row.Table.Columns[columnName]!.DataType;
        row[columnName] = value switch
        {
            DateOnly date when targetType == typeof(DateTime) => date.ToDateTime(TimeOnly.MinValue),
            long number when targetType == typeof(int) => checked((int)number),
            short number when targetType == typeof(int) => (int)number,
            _ when targetType.IsInstanceOfType(value) => value,
            _ => Convert.ChangeType(value, targetType)
        };
    }
}

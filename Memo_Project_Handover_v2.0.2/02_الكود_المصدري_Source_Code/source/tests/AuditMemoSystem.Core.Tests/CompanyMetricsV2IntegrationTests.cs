using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class CompanyMetricsV2IntegrationTests
{
    private static readonly DateOnly From = new(2026, 7, 1);
    private static readonly DateOnly To = new(2026, 7, 31);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MetricsV2_GoldenDatasetPreservesDenominatorsScopesAndClassificationReferences()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var metricType = Guid.NewGuid();
            await InsertNoteTypeAsync(connection, transaction, metricType, $"M54_{suffix}", "نوع اختبار عقد المؤشرات");

            var fields = new List<Guid>();
            for (var index = 0; index < 10; index++)
            {
                var field = Guid.NewGuid();
                fields.Add(field);
                await InsertFieldAsync(connection, transaction, metricType, field, $"M54_{suffix}_{index}", index);
            }

            var mixedCompany = await InsertCompanyAsync(connection, transaction, $"M54 mixed {suffix}");
            var mixedNote = await InsertNoteAsync(connection, transaction, mixedCompany, metricType,
                $"M54-{suffix}-MIX", new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 10));
            var mixedValues = new short[] { 1, 1, 1, 1, 1, 1, 2, 5, 0, 6 };
            for (var index = 0; index < mixedValues.Length; index++)
            {
                await InsertFieldValueAsync(connection, transaction, mixedNote, fields[index], mixedValues[index]);
            }

            var mixedFacts = await ReadRowAsync(connection, transaction, """
                select count(*)::int as expected_items,
                       count(*) filter (where is_reviewed)::int as reviewed_items,
                       count(*) filter (where is_unreviewed)::int as unreviewed_items,
                       count(*) filter (where is_applicable_reviewed)::int as applicable_items,
                       count(*) filter (where is_correct)::int as correct_items,
                       count(*) filter (where is_error)::int as error_items,
                       count(*) filter (where is_not_applicable)::int as not_applicable_items
                from reporting.vw_note_indicator_facts_v2
                where note_id = @note
                """, ("note", NpgsqlDbType.Uuid, mixedNote));
            Assert.Equal(10, Int(mixedFacts, "expected_items"));
            Assert.Equal(9, Int(mixedFacts, "reviewed_items"));
            Assert.Equal(1, Int(mixedFacts, "unreviewed_items"));
            Assert.Equal(8, Int(mixedFacts, "applicable_items"));
            Assert.Equal(6, Int(mixedFacts, "correct_items"));
            Assert.Equal(2, Int(mixedFacts, "error_items"));
            Assert.Equal(1, Int(mixedFacts, "not_applicable_items"));

            var mixedMetrics = await ReadMetricsAsync(connection, transaction, mixedCompany);
            Assert.Equal(90.00m, Decimal(mixedMetrics, "review_coverage_percentage"));
            Assert.Equal(75.00m, Decimal(mixedMetrics, "item_quality_percentage"));
            Assert.Equal(25.00m, Decimal(mixedMetrics, "errors_per_100_checked"));
            Assert.Equal(1, Int(mixedMetrics, "error_notes"));
            Assert.Equal(1, Int(mixedMetrics, "incomplete_review_notes"));

            var unreviewedCompany = await InsertCompanyAsync(connection, transaction, $"M54 unreviewed {suffix}");
            var unreviewedNote = await InsertNoteAsync(connection, transaction, unreviewedCompany, metricType,
                $"M54-{suffix}-UNR", new DateOnly(2026, 7, 11), new DateOnly(2026, 7, 11));
            // Five explicit zeroes plus five absent value rows prove that both states are unreviewed.
            for (var index = 0; index < 5; index++)
            {
                await InsertFieldValueAsync(connection, transaction, unreviewedNote, fields[index], 0);
            }

            var unreviewedMetrics = await ReadMetricsAsync(connection, transaction, unreviewedCompany);
            Assert.Equal(10, Int(unreviewedMetrics, "expected_items"));
            Assert.Equal(0, Int(unreviewedMetrics, "reviewed_items"));
            Assert.Equal(10, Int(unreviewedMetrics, "unreviewed_items"));
            Assert.Equal(0.00m, Decimal(unreviewedMetrics, "review_coverage_percentage"));
            Assert.Null(unreviewedMetrics["item_quality_percentage"]);
            Assert.Null(unreviewedMetrics["errors_per_100_checked"]);
            Assert.Equal(0, Int(unreviewedMetrics, "clean_reviewed_notes"));
            Assert.Equal(1, Int(unreviewedMetrics, "incomplete_review_notes"));

            var notApplicableCompany = await InsertCompanyAsync(connection, transaction, $"M54 NA {suffix}");
            var notApplicableNote = await InsertNoteAsync(connection, transaction, notApplicableCompany, metricType,
                $"M54-{suffix}-NA", new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 12));
            foreach (var field in fields)
            {
                await InsertFieldValueAsync(connection, transaction, notApplicableNote, field, 6);
            }

            var notApplicableMetrics = await ReadMetricsAsync(connection, transaction, notApplicableCompany);
            Assert.Equal(10, Int(notApplicableMetrics, "reviewed_items"));
            Assert.Equal(10, Int(notApplicableMetrics, "not_applicable_items"));
            Assert.Equal(0, Int(notApplicableMetrics, "error_items"));
            Assert.Equal(100.00m, Decimal(notApplicableMetrics, "review_coverage_percentage"));
            Assert.Null(notApplicableMetrics["item_quality_percentage"]);
            Assert.Null(notApplicableMetrics["errors_per_100_checked"]);
            Assert.Equal(0, Int(notApplicableMetrics, "clean_reviewed_notes"));

            var timeCompany = await InsertCompanyAsync(connection, transaction, $"M54 time {suffix}");
            await InsertNoteAsync(connection, transaction, timeCompany, metricType,
                $"M54-{suffix}-D0", new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 10));
            await InsertNoteAsync(connection, transaction, timeCompany, metricType,
                $"M54-{suffix}-D5", new DateOnly(2026, 7, 5), new DateOnly(2026, 7, 10));
            await InsertNoteAsync(connection, transaction, timeCompany, metricType,
                $"M54-{suffix}-D6", new DateOnly(2026, 7, 4), new DateOnly(2026, 7, 10));
            await InsertNoteAsync(connection, transaction, timeCompany, metricType,
                $"M54-{suffix}-NULL", null, new DateOnly(2026, 7, 10));
            await InsertNoteAsync(connection, transaction, timeCompany, metricType,
                $"M54-{suffix}-INV", new DateOnly(2026, 7, 11), new DateOnly(2026, 7, 10));
            await InsertNoteAsync(connection, transaction, timeCompany, metricType,
                $"M54-{suffix}-DEL", new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 10), isDeleted: true);

            var timeMetrics = await ReadMetricsAsync(connection, transaction, timeCompany);
            Assert.Equal(5, Int(timeMetrics, "total_notes"));
            Assert.Equal(3, Int(timeMetrics, "time_eligible_notes"));
            Assert.Equal(2, Int(timeMetrics, "on_time_notes"));
            Assert.Equal(1, Int(timeMetrics, "late_notes"));
            Assert.Equal(1, Int(timeMetrics, "late_6_to_10_notes"));
            Assert.Equal(0, Int(timeMetrics, "late_11_to_20_notes"));
            Assert.Equal(0, Int(timeMetrics, "late_over_20_notes"));
            Assert.Equal(1, Int(timeMetrics, "missing_writing_date_notes"));
            Assert.Equal(1, Int(timeMetrics, "invalid_date_order_notes"));
            Assert.Equal(66.67m, Decimal(timeMetrics, "time_compliance_percentage"));
            Assert.Equal(6.00m, Decimal(timeMetrics, "average_late_days"));
            Assert.Equal(6.00m, Decimal(timeMetrics, "median_late_days"));
            Assert.Equal(6.00m, Decimal(timeMetrics, "p90_late_days"));
            Assert.Equal(6, Int(timeMetrics, "maximum_late_days"));
            Assert.Equal(1.00m, Decimal(timeMetrics, "average_excess_delay_days"));

            var timeSeries = await ReadRowAsync(connection, transaction, """
                select count(*)::int as row_count,
                       max(time_compliance_percentage) as time_compliance_percentage
                from reporting.fn_company_month_series_v2(@company, @from, @to)
                """,
                ("company", NpgsqlDbType.Uuid, timeCompany),
                ("from", NpgsqlDbType.Date, From),
                ("to", NpgsqlDbType.Date, To));
            Assert.Equal(1, Int(timeSeries, "row_count"));
            Assert.Equal(66.67m, Decimal(timeSeries, "time_compliance_percentage"));

            var duplicateCompany = await InsertCompanyAsync(connection, transaction, $"M54 duplicate {suffix}");
            var matchedPast = await InsertNoteAsync(connection, transaction, duplicateCompany, metricType,
                $"M54-{suffix}-PAST", new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 20));
            var duplicateA = await InsertNoteAsync(connection, transaction, duplicateCompany, metricType,
                $"M54-{suffix}-DA", new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 15));
            var duplicateB = await InsertNoteAsync(connection, transaction, duplicateCompany, metricType,
                $"M54-{suffix}-DB", new DateOnly(2026, 7, 16), new DateOnly(2026, 7, 16));
            var deletedDuplicate = await InsertNoteAsync(connection, transaction, duplicateCompany, metricType,
                $"M54-{suffix}-DD", new DateOnly(2026, 7, 17), new DateOnly(2026, 7, 17), isDeleted: true);

            await InsertIncidentAsync(connection, transaction, "COMPANY_DUPLICATE_NUMBER", matchedPast, duplicateA,
                duplicateCompany, metricType, $"M54-{suffix}-DA");
            await InsertIncidentAsync(connection, transaction, "COMPANY_DUPLICATE_NUMBER", matchedPast, duplicateB,
                duplicateCompany, metricType, $"M54-{suffix}-DB");
            await InsertIncidentAsync(connection, transaction, "COMPANY_RESEND", duplicateA, null,
                duplicateCompany, metricType, $"M54-{suffix}-DA");
            await InsertIncidentAsync(connection, transaction, "COMPANY_DUPLICATE_NUMBER", matchedPast, deletedDuplicate,
                duplicateCompany, metricType, $"M54-{suffix}-DD");

            var duplicateMetrics = await ReadMetricsAsync(connection, transaction, duplicateCompany);
            Assert.Equal(2, Int(duplicateMetrics, "total_notes"));
            Assert.Equal(2, Int(duplicateMetrics, "duplicate_number_incidents"));
            Assert.Equal(1, Int(duplicateMetrics, "company_resend_incidents"));
            Assert.Equal(3, Int(duplicateMetrics, "duplicate_incidents"));
            Assert.Equal(2, Int(duplicateMetrics, "affected_notes"));
            Assert.Equal(150.00m, Decimal(duplicateMetrics, "incidents_per_100_notes"));
            Assert.Equal(100.00m, Decimal(duplicateMetrics, "affected_note_rate_percentage"));
            Assert.Equal(0.00m, Decimal(duplicateMetrics, "duplicate_free_percentage"));
            Assert.Equal(0, Int(duplicateMetrics, "entry_notes"));
            Assert.Null(duplicateMetrics["link_efficiency_percentage"]);

            var dashboardPayload = await ReadScalarAsync<string>(connection, transaction, """
                select reporting.fn_dashboard_snapshot(
                    @from, @to, @company, null, null, null, null, 'Asia/Damascus')::text
                """,
                ("from", NpgsqlDbType.Date, From),
                ("to", NpgsqlDbType.Date, To),
                ("company", NpgsqlDbType.Uuid, duplicateCompany));
            using (var dashboard = JsonDocument.Parse(dashboardPayload))
            {
                var summary = dashboard.RootElement.GetProperty("summary");
                Assert.Equal(2, summary.GetProperty("totalNotes").GetInt32());
                Assert.Equal(2, summary.GetProperty("duplicateNumberNotes").GetInt32());
                Assert.Equal(1, summary.GetProperty("companyResendNotes").GetInt32());
            }

            var linkCompany = await InsertCompanyAsync(connection, transaction, $"M54 links {suffix}");
            var entryType = await ReadTypeIdAsync(connection, transaction, "ENTRY");
            var exitType = await ReadTypeIdAsync(connection, transaction, "EXIT");
            var entryOne = await InsertNoteAsync(connection, transaction, linkCompany, entryType,
                $"M54-{suffix}-E1", new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20));
            var entryTwo = await InsertNoteAsync(connection, transaction, linkCompany, entryType,
                $"M54-{suffix}-E2", new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 21));
            var exitOne = await InsertNoteAsync(connection, transaction, linkCompany, exitType,
                $"M54-{suffix}-X1", new DateOnly(2026, 7, 22), new DateOnly(2026, 7, 22));
            var exitTwo = await InsertNoteAsync(connection, transaction, linkCompany, exitType,
                $"M54-{suffix}-X2", new DateOnly(2026, 7, 23), new DateOnly(2026, 7, 23));
            await InsertLinkAsync(connection, transaction, entryOne, exitOne);
            await InsertLinkAsync(connection, transaction, entryTwo, exitTwo);
            await SetNoteDeletedAsync(connection, transaction, exitTwo);

            var linkMetrics = await ReadMetricsAsync(connection, transaction, linkCompany);
            Assert.Equal(3, Int(linkMetrics, "total_notes"));
            Assert.Equal(2, Int(linkMetrics, "entry_notes"));
            Assert.Equal(1, Int(linkMetrics, "linked_entry_notes"));
            Assert.Equal(1, Int(linkMetrics, "unlinked_entry_notes"));
            Assert.Equal(50.00m, Decimal(linkMetrics, "link_efficiency_percentage"));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static NpgsqlConnection CreateConnection()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("AUDIT_MEMO_ACCEPTANCE_PASSWORD is required.");
        }

        return new NpgsqlConnection(new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var port) ? port : 5432,
            Database = PostgreSqlIntegrationFixture.DatabaseName,
            Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres",
            Password = password,
            Timeout = 5
        }.ConnectionString);
    }

    private static async Task<Guid> InsertCompanyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string name)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(connection, transaction,
            "insert into core.companies(id, name, is_active, is_deleted) values (@id, @name, true, false)",
            ("id", NpgsqlDbType.Uuid, id), ("name", NpgsqlDbType.Text, name));
        return id;
    }

    private static Task InsertNoteTypeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, string code, string name) =>
        ExecuteAsync(connection, transaction,
            "insert into core.note_types(id, code, name_ar, is_active, is_deleted) values (@id, @code, @name, true, false)",
            ("id", NpgsqlDbType.Uuid, id), ("code", NpgsqlDbType.Varchar, code), ("name", NpgsqlDbType.Varchar, name));

    private static Task InsertFieldAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid noteTypeId, Guid fieldId, string code, int sortOrder) =>
        ExecuteAsync(connection, transaction, """
            insert into core.note_fields(id, code, name_ar, default_sort_order, is_active, is_deleted)
            values (@field, @code, @name, @sort, true, false);
            insert into core.note_type_fields(note_type_id, field_id, sort_order, is_required, is_active, is_deleted)
            values (@type, @field, @sort, true, true, false);
            """,
            ("field", NpgsqlDbType.Uuid, fieldId),
            ("type", NpgsqlDbType.Uuid, noteTypeId),
            ("code", NpgsqlDbType.Varchar, code),
            ("name", NpgsqlDbType.Varchar, $"بند {sortOrder + 1}"),
            ("sort", NpgsqlDbType.Integer, sortOrder));

    private static async Task<Guid> InsertNoteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid companyId,
        Guid noteTypeId,
        string number,
        DateOnly? writingDate,
        DateOnly arrivalDate,
        bool isDeleted = false)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(connection, transaction, """
            insert into core.notes(
                id, note_number, note_number_normalized, note_type_id, company_id,
                writing_date, arrival_at, status, is_deleted)
            values (
                @id, @number, @normalized, @type, @company,
                @writing, @arrival, 'COMPLETE', @deleted)
            """,
            ("id", NpgsqlDbType.Uuid, id),
            ("number", NpgsqlDbType.Varchar, number),
            ("normalized", NpgsqlDbType.Varchar, number.Replace("-", string.Empty)),
            ("type", NpgsqlDbType.Uuid, noteTypeId),
            ("company", NpgsqlDbType.Uuid, companyId),
            ("writing", NpgsqlDbType.Date, writingDate),
            ("arrival", NpgsqlDbType.Date, arrivalDate),
            ("deleted", NpgsqlDbType.Boolean, isDeleted));
        return id;
    }

    private static Task InsertFieldValueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid noteId, Guid fieldId, short value) =>
        ExecuteAsync(connection, transaction,
            "insert into core.note_field_values(note_id, field_id, field_value) values (@note, @field, @value)",
            ("note", NpgsqlDbType.Uuid, noteId),
            ("field", NpgsqlDbType.Uuid, fieldId),
            ("value", NpgsqlDbType.Smallint, value));

    private static Task InsertIncidentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string classification,
        Guid matchedNoteId,
        Guid? createdNoteId,
        Guid companyId,
        Guid noteTypeId,
        string number) =>
        ExecuteAsync(connection, transaction, """
            insert into core.duplicate_incidents(
                classification, matched_note_id, created_note_id, company_id,
                note_type_id, note_number, note_number_normalized, writing_date, metadata)
            values (
                @classification, @matched, @created, @company,
                @type, @number, @normalized, @writing, '{}'::jsonb)
            """,
            ("classification", NpgsqlDbType.Varchar, classification),
            ("matched", NpgsqlDbType.Uuid, matchedNoteId),
            ("created", NpgsqlDbType.Uuid, createdNoteId),
            ("company", NpgsqlDbType.Uuid, companyId),
            ("type", NpgsqlDbType.Uuid, noteTypeId),
            ("number", NpgsqlDbType.Varchar, number),
            ("normalized", NpgsqlDbType.Varchar, number.Replace("-", string.Empty)),
            ("writing", NpgsqlDbType.Date, From));

    private static Task InsertLinkAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid entryId, Guid exitId) =>
        ExecuteAsync(connection, transaction,
            "insert into core.note_links(source_note_id, related_note_id) values (@entry, @exit)",
            ("entry", NpgsqlDbType.Uuid, entryId),
            ("exit", NpgsqlDbType.Uuid, exitId));

    private static Task SetNoteDeletedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid noteId) =>
        ExecuteAsync(connection, transaction,
            "update core.notes set is_deleted = true, deleted_at = now() where id = @id",
            ("id", NpgsqlDbType.Uuid, noteId));

    private static async Task<Guid> ReadTypeIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string code) =>
        await ReadScalarAsync<Guid>(connection, transaction,
            "select id from core.note_types where code = @code and is_active and not is_deleted",
            ("code", NpgsqlDbType.Varchar, code));

    private static Task<Dictionary<string, object?>> ReadMetricsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid companyId) =>
        ReadRowAsync(connection, transaction,
            "select * from reporting.fn_company_metrics_snapshot_v2(@company, @from, @to)",
            ("company", NpgsqlDbType.Uuid, companyId),
            ("from", NpgsqlDbType.Date, From),
            ("to", NpgsqlDbType.Date, To));

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        params (string Name, NpgsqlDbType Type, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<string, object?>> ReadRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        params (string Name, NpgsqlDbType Type, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "The metrics query must return exactly one row.");
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            row[reader.GetName(index)] = await reader.IsDBNullAsync(index) ? null : reader.GetValue(index);
        }
        Assert.False(await reader.ReadAsync(), "The metrics query returned more than one row.");
        return row;
    }

    private static async Task<T> ReadScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        params (string Name, NpgsqlDbType Type, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddParameters(command, parameters);
        var value = await command.ExecuteScalarAsync();
        return (T)(value ?? throw new InvalidOperationException("The scalar query returned NULL."));
    }

    private static void AddParameters(NpgsqlCommand command, IEnumerable<(string Name, NpgsqlDbType Type, object? Value)> parameters)
    {
        foreach (var (name, type, value) in parameters)
        {
            command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
        }
    }

    private static int Int(IReadOnlyDictionary<string, object?> row, string name) =>
        Convert.ToInt32(row[name]);

    private static decimal Decimal(IReadOnlyDictionary<string, object?> row, string name) =>
        Convert.ToDecimal(row[name]);
}

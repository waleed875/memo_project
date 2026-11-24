using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class Task07CanonicalMetricsContractTests
{
    [Fact]
    public void V3ContractIsAdditiveAndDefinesLegalClassificationAndCutoff()
    {
        var sql = File.ReadAllText(FindRepositoryFile("database/060_add_company_metrics_snapshot_v3_contract.sql"));
        Assert.Contains("fn_company_metrics_snapshot_v3", sql, StringComparison.Ordinal);
        Assert.Contains("fn_company_month_series_v3", sql, StringComparison.Ordinal);
        Assert.Contains("p_entry_cutoff_date date default null", sql, StringComparison.Ordinal);
        Assert.Contains("when q.error_items > 0 then 'ERROR'", sql, StringComparison.Ordinal);
        Assert.Contains("when q.unreviewed_items > 0 then 'INCOMPLETE'", sql, StringComparison.Ordinal);
        Assert.Contains("when q.not_applicable_items = q.expected_items then 'ALL_NOT_APPLICABLE'", sql, StringComparison.Ordinal);
        Assert.Contains("quality_eligible_note_count", sql, StringComparison.Ordinal);
        Assert.Contains("all_not_applicable_note_count", sql, StringComparison.Ordinal);
        Assert.Contains("no_expected_items_note_count", sql, StringComparison.Ordinal);
        Assert.Contains("clean_reviewed_note_percent", sql, StringComparison.Ordinal);
        Assert.Contains("q.clean_reviewed_notes::numeric * 100 / nullif(q.clean_reviewed_notes + q.error_notes, 0)", sql, StringComparison.Ordinal);
        Assert.Contains("n.created_at < (p_entry_cutoff_date + 1)::timestamp", sql, StringComparison.Ordinal);

        var service = File.ReadAllText(FindRepositoryFile("src/MemoDashboard.Web/Backend/Services/CompanyMetricsService.cs"));
        Assert.Contains("fn_company_metrics_snapshot_v4", service, StringComparison.Ordinal);
        Assert.DoesNotContain("fn_company_metrics_snapshot_v2", service, StringComparison.Ordinal);

        var numericContract = File.ReadAllText(FindRepositoryFile("database/062_add_numeric_note_number_contract.sql"));
        Assert.Contains("fn_company_metrics_snapshot_v4", numericContract, StringComparison.Ordinal);
        Assert.Contains("p_date_basis", numericContract, StringComparison.Ordinal);

        var reportService = File.ReadAllText(FindRepositoryFile("src/AuditMemoSystem.Data/Repositories/ReportService.cs"));
        Assert.Contains("GetCompanyDetailedTableV3Async", reportService, StringComparison.Ordinal);
        var filter = File.ReadAllText(FindRepositoryFile("src/AuditMemoSystem.Core/Models/DomainModels.cs"));
        Assert.Contains("EntryCutoffDate", filter, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"تعذر تحديد ملف الاختبار: {relativePath}");
    }
}

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class CompanyMetricsV3IntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task GoldenDatasetUsesMutuallyExclusiveClassesAndInclusiveCreatedCutoff()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("AUDIT_MEMO_ACCEPTANCE_PASSWORD is required.");

        var factory = PostgreSqlIntegrationFixture.CreateFactory("CompanyMetricsV3");
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await InsertCompanyAsync(connection, transaction, $"M60 {suffix}");
        var metricType = Guid.NewGuid();
        var emptyType = Guid.NewGuid();
        await InsertNoteTypeAsync(connection, transaction, metricType, $"M60_{suffix}");
        await InsertNoteTypeAsync(connection, transaction, emptyType, $"M60_EMPTY_{suffix}");
        var fields = new[] { Guid.NewGuid(), Guid.NewGuid() };
        for (var index = 0; index < fields.Length; index++)
        {
            await InsertFieldAsync(connection, transaction, metricType, fields[index], $"M60_{suffix}_{index}", index);
        }

        try
        {
            var from = new DateOnly(2026, 1, 1);
            var to = new DateOnly(2026, 1, 31);
            var cutoff = new DateOnly(2026, 1, 31);
            var createdBeforeCutoff = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Unspecified);
            await InsertNoteAsync(connection, transaction, company, metricType, $"{suffix}-CLEAN", from.AddDays(1), createdBeforeCutoff, fields, [1, 1]);
            await InsertNoteAsync(connection, transaction, company, metricType, $"{suffix}-ERROR", from.AddDays(2), createdBeforeCutoff, fields, [2, 1]);
            await InsertNoteAsync(connection, transaction, company, metricType, $"{suffix}-MIXED", from.AddDays(3), createdBeforeCutoff, fields, [2, 0]);
            await InsertNoteAsync(connection, transaction, company, metricType, $"{suffix}-INCOMPLETE", from.AddDays(4), createdBeforeCutoff, fields, [0, null]);
            await InsertNoteAsync(connection, transaction, company, metricType, $"{suffix}-NA", from.AddDays(5), createdBeforeCutoff, fields, [6, 6]);
            await InsertNoteAsync(connection, transaction, company, emptyType, $"{suffix}-EMPTY", from.AddDays(6), createdBeforeCutoff, [], []);
            await InsertNoteAsync(connection, transaction, company, metricType, $"{suffix}-AFTER-CUTOFF", from.AddDays(7), new DateTime(2026, 2, 1, 0, 0, 0), fields, [1, 1]);

            var row = await ReadRowAsync(connection, transaction, "select * from reporting.fn_company_metrics_snapshot_v3(@company, @from, @to, @cutoff)",
                ("company", NpgsqlDbType.Uuid, company), ("from", NpgsqlDbType.Date, from), ("to", NpgsqlDbType.Date, to), ("cutoff", NpgsqlDbType.Date, cutoff));
            Assert.Equal(6, Int(row, "total_notes"));
            Assert.Equal(10, Int(row, "expected_items"));
            Assert.Equal(7, Int(row, "reviewed_items"));
            Assert.Equal(3, Int(row, "unreviewed_items"));
            Assert.Equal(5, Int(row, "applicable_reviewed_items"));
            Assert.Equal(3, Int(row, "correct_items"));
            Assert.Equal(2, Int(row, "error_items"));
            Assert.Equal(2, Int(row, "not_applicable_items"));
            Assert.Equal(70.00m, Decimal(row, "review_coverage_percentage"));
            Assert.Equal(60.00m, Decimal(row, "item_quality_percentage"));
            Assert.Equal(40.00m, Decimal(row, "errors_per_100_checked"));
            Assert.Equal(3, Int(row, "quality_eligible_note_count"));
            Assert.Equal(1, Int(row, "clean_reviewed_notes"));
            Assert.Equal(2, Int(row, "error_notes"));
            Assert.Equal(1, Int(row, "incomplete_review_notes"));
            Assert.Equal(1, Int(row, "all_not_applicable_note_count"));
            Assert.Equal(1, Int(row, "no_expected_items_note_count"));
            Assert.Equal(33.33m, Decimal(row, "clean_reviewed_note_percent"));

            var noQualityCompany = await InsertCompanyAsync(connection, transaction, $"M60-NQ {suffix}");
            await InsertNoteAsync(connection, transaction, noQualityCompany, metricType, $"{suffix}-ALL-UNREVIEWED", from.AddDays(8), createdBeforeCutoff, fields, [0, 0]);
            var noQuality = await ReadRowAsync(connection, transaction, "select * from reporting.fn_company_metrics_snapshot_v3(@company, @from, @to)",
                ("company", NpgsqlDbType.Uuid, noQualityCompany), ("from", NpgsqlDbType.Date, from), ("to", NpgsqlDbType.Date, to));
            Assert.Equal(0, Int(noQuality, "quality_eligible_note_count"));
            Assert.Null(noQuality["item_quality_percentage"]);
            Assert.Null(noQuality["clean_reviewed_note_percent"]);

            var noCutoff = await ReadRowAsync(connection, transaction, "select * from reporting.fn_company_metrics_snapshot_v3(@company, @from, @to)",
                ("company", NpgsqlDbType.Uuid, company), ("from", NpgsqlDbType.Date, from), ("to", NpgsqlDbType.Date, to));
            Assert.Equal(7, Int(noCutoff, "total_notes"));

            var series = await ReadRowAsync(connection, transaction, "select * from reporting.fn_company_month_series_v3(@company, @from, @to, @cutoff)",
                ("company", NpgsqlDbType.Uuid, company), ("from", NpgsqlDbType.Date, from), ("to", NpgsqlDbType.Date, to), ("cutoff", NpgsqlDbType.Date, cutoff));
            Assert.Equal("2026/01", Convert.ToString(series["period_label"]));
            Assert.Equal(33.33m, Decimal(series, "clean_reviewed_note_percent"));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<Guid> InsertCompanyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string name)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(connection, transaction, "insert into core.companies(id, name, is_active, is_deleted) values (@id, @name, true, false)",
            ("id", NpgsqlDbType.Uuid, id), ("name", NpgsqlDbType.Text, name));
        return id;
    }

    private static Task InsertNoteTypeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, string code) =>
        ExecuteAsync(connection, transaction, "insert into core.note_types(id, code, name_ar, is_active, is_deleted) values (@id, @code, @code, true, false)",
            ("id", NpgsqlDbType.Uuid, id), ("code", NpgsqlDbType.Varchar, code));

    private static Task InsertFieldAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid typeId, Guid fieldId, string code, int sortOrder) =>
        ExecuteAsync(connection, transaction, """
            insert into core.note_fields(id, code, name_ar, default_sort_order, is_active, is_deleted)
            values (@field, @code, @code, @sort, true, false);
            insert into core.note_type_fields(note_type_id, field_id, sort_order, is_required, is_active, is_deleted)
            values (@type, @field, @sort, true, true, false);
            """,
            ("field", NpgsqlDbType.Uuid, fieldId), ("type", NpgsqlDbType.Uuid, typeId),
            ("code", NpgsqlDbType.Varchar, code), ("sort", NpgsqlDbType.Integer, sortOrder));

    private static async Task InsertNoteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid company,
        Guid type,
        string number,
        DateOnly arrival,
        DateTime createdAt,
        IReadOnlyList<Guid> fields,
        IReadOnlyList<short?> values)
    {
        var note = Guid.NewGuid();
        await ExecuteAsync(connection, transaction, """
            insert into core.notes(id, note_number, note_number_normalized, note_type_id, company_id,
                                   writing_date, arrival_at, created_at, status, is_deleted)
            values (@id, @number, @normalized, @type, @company, @arrival, @arrival, @created, 'COMPLETE', false)
            """,
            ("id", NpgsqlDbType.Uuid, note), ("number", NpgsqlDbType.Varchar, number),
            ("normalized", NpgsqlDbType.Varchar, number), ("type", NpgsqlDbType.Uuid, type),
            ("company", NpgsqlDbType.Uuid, company), ("arrival", NpgsqlDbType.Date, arrival),
            ("created", NpgsqlDbType.Timestamp, createdAt));
        for (var index = 0; index < fields.Count; index++)
        {
            if (!values[index].HasValue) continue;
            await ExecuteAsync(connection, transaction,
                "insert into core.note_field_values(note_id, field_id, field_value) values (@note, @field, @value)",
                ("note", NpgsqlDbType.Uuid, note), ("field", NpgsqlDbType.Uuid, fields[index]),
                ("value", NpgsqlDbType.Smallint, values[index]!.Value));
        }
    }

    private static async Task<Dictionary<string, object?>> ReadRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        params (string Name, NpgsqlDbType Type, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, type, value) in parameters) command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.FieldCount; index++)
            row[reader.GetName(index)] = await reader.IsDBNullAsync(index) ? null : reader.GetValue(index);
        Assert.False(await reader.ReadAsync());
        return row;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        params (string Name, NpgsqlDbType Type, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, type, value) in parameters) command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
        await command.ExecuteNonQueryAsync();
    }

    private static int Int(IReadOnlyDictionary<string, object?> row, string name) => Convert.ToInt32(row[name]);
    private static decimal Decimal(IReadOnlyDictionary<string, object?> row, string name) => Convert.ToDecimal(row[name]);
}

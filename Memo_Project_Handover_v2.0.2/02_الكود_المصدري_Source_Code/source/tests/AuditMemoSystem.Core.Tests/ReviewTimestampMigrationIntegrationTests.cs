using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class ReviewTimestampMigrationIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Migration033_KeepsOnlyProvableCurrentOrApprovedReviewStart()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password), "AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run this integration test.");

        var factory = CreateFactory(password!);
        var companyId = await ReadIdAsync(factory, "select id from core.companies where is_active and not is_deleted limit 1");
        var noteTypeId = await ReadIdAsync(factory, "select id from core.note_types where is_active and not is_deleted limit 1");
        var userId = await ReadIdAsync(factory, """
            select u.id from auth.users u join auth.roles r on r.id = u.role_id
            where r.code = 'ADMIN' and u.is_active and not u.is_deleted limit 1
            """);
        var anchor = DateTime.SpecifyKind(DateTime.Today.AddDays(-1).AddHours(12), DateTimeKind.Unspecified);
        var returnedId = Guid.NewGuid();
        var currentId = Guid.NewGuid();
        var currentWithoutHistoryId = Guid.NewGuid();
        var approvedId = Guid.NewGuid();
        var approvedWithoutHistoryId = Guid.NewGuid();
        var ids = new[] { returnedId, currentId, currentWithoutHistoryId, approvedId, approvedWithoutHistoryId };
        var currentExpected = anchor.AddHours(-2);
        var approvedExpected = anchor.AddHours(-4);
        var approvedAt = anchor.AddHours(-3);

        try
        {
            await InsertNoteAsync(factory, returnedId, "RETURNED_FOR_CORRECTION", companyId, noteTypeId, userId, null, null, anchor.AddDays(-10));
            await InsertHistoryAsync(factory, returnedId, userId, anchor.AddDays(-5));

            await InsertNoteAsync(factory, currentId, "UNDER_REVIEW", companyId, noteTypeId, userId, userId, null, anchor.AddDays(-10));
            await InsertHistoryAsync(factory, currentId, userId, anchor.AddHours(-8));
            await InsertHistoryAsync(factory, currentId, userId, currentExpected);

            await InsertNoteAsync(factory, currentWithoutHistoryId, "UNDER_REVIEW", companyId, noteTypeId, userId, userId, null, anchor.AddDays(-10));

            await InsertNoteAsync(factory, approvedId, "APPROVED", companyId, noteTypeId, userId, null, approvedAt, anchor.AddDays(-10));
            await InsertHistoryAsync(factory, approvedId, userId, approvedExpected);
            await InsertHistoryAsync(factory, approvedId, userId, anchor.AddHours(-1));

            await InsertNoteAsync(factory, approvedWithoutHistoryId, "APPROVED", companyId, noteTypeId, userId, null, approvedAt, anchor.AddDays(-10));

            var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var migrationSql = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "database", "033_correct_review_started_timestamp_semantics.sql"));
            await ExecuteAsync(factory, migrationSql);

            var actual = await ReadReviewStartsAsync(factory, ids);
            Assert.Null(actual[returnedId]);
            Assert.Equal(currentExpected, actual[currentId]);
            Assert.Null(actual[currentWithoutHistoryId]);
            Assert.Equal(approvedExpected, actual[approvedId]);
            Assert.Null(actual[approvedWithoutHistoryId]);
        }
        finally
        {
            await using var connection = factory.CreateConnection();
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand("delete from core.notes where id = any(@ids)", connection);
            cleanup.Parameters.AddWithValue("ids", ids);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertNoteAsync(
        DbConnectionFactory factory,
        Guid id,
        string status,
        Guid companyId,
        Guid noteTypeId,
        Guid createdBy,
        Guid? reviewerId,
        DateTime? approvedAt,
        DateTime reviewStartedAt)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            insert into core.notes(
                id, note_number, note_number_normalized, note_type_id, company_id,
                writing_date, arrival_at, status, created_by, reviewer_id,
                approved_by, approved_at, review_started_at)
            values (
                @id, @number, @normalized, @note_type_id, @company_id,
                @writing_date, @arrival_at, @status, @created_by, @reviewer_id,
                @approved_by, @approved_at, @review_started_at)
            """, connection);
        var number = $"E33-{id:N}";
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("number", number);
        command.Parameters.AddWithValue("normalized", number.Replace("-", "").ToUpperInvariant());
        command.Parameters.AddWithValue("note_type_id", noteTypeId);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("writing_date", DateOnly.FromDateTime(DateTime.Today));
        command.Parameters.AddWithValue("arrival_at", DateOnly.FromDateTime(DateTime.Today));
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("created_by", createdBy);
        command.Parameters.Add("reviewer_id", NpgsqlDbType.Uuid).Value = (object?)reviewerId ?? DBNull.Value;
        command.Parameters.Add("approved_by", NpgsqlDbType.Uuid).Value = status == "APPROVED" ? createdBy : DBNull.Value;
        command.Parameters.Add("approved_at", NpgsqlDbType.Timestamp).Value = (object?)approvedAt ?? DBNull.Value;
        command.Parameters.AddWithValue("review_started_at", reviewStartedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertHistoryAsync(DbConnectionFactory factory, Guid noteId, Guid userId, DateTime createdAt)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            insert into core.note_review_history(note_id, action, user_id, created_at)
            values (@note_id, 'START_REVIEW', @user_id, @created_at)
            """, connection);
        command.Parameters.AddWithValue("note_id", noteId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("created_at", createdAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<Guid, DateTime?>> ReadReviewStartsAsync(DbConnectionFactory factory, Guid[] ids)
    {
        var result = new Dictionary<Guid, DateTime?>();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select id, review_started_at from core.notes where id = any(@ids)", connection);
        command.Parameters.AddWithValue("ids", ids);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetGuid(0)] = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
        }
        return result;
    }

    private static async Task ExecuteAsync(DbConnectionFactory factory, string sql)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> ReadIdAsync(DbConnectionFactory factory, string sql)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("بيانات الاختبار المرجعية غير متاحة."));
    }

    private static DbConnectionFactory CreateFactory(string password) => new(new AppConfig
    {
        Database = new DatabaseSection
        {
            ActiveProfile = "LocalAcceptance",
            Profiles = [new ConnectionProfileModel
            {
                Name = "LocalAcceptance",
                Type = ConnectionProfileType.SERVER,
                Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost",
                Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var integrationPort) ? integrationPort : 5432,
                Database = PostgreSqlIntegrationFixture.DatabaseName,
                Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres",
                Password = password
            }]
        }
    });
}

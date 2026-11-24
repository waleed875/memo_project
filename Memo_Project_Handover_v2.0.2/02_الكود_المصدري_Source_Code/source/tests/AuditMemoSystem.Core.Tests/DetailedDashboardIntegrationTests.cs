using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Repositories;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class DetailedDashboardIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Snapshot_UsesRealDataAndProducesTheExpectedDashboardSeries()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password), "AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run this integration test.");

        var factory = new DbConnectionFactory(new AppConfig
        {
            Database = new DatabaseSection
            {
                ActiveProfile = "DashboardIntegration",
                Profiles =
                [
                    new ConnectionProfileModel
                    {
                        Name = "DashboardIntegration",
                        Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost",
                        Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var integrationPort) ? integrationPort : 5432,
                        Database = PostgreSqlIntegrationFixture.DatabaseName,
                        Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres",
                        Password = password
                    }
                ]
            }
        });
        var adminId = await ReadAdminIdAsync(factory);

        var snapshot = await new ReportService(factory).GetDetailedDashboardSnapshotAsync(adminId);

        Assert.Equal(6, snapshot.MonthlyNotes.Count);
        Assert.True(snapshot.Summary.TotalNotes >= snapshot.RecentNotes.Count);
        Assert.True(snapshot.Summary.TotalNotes >= snapshot.Summary.ApprovedNotes);
        Assert.True(snapshot.Summary.TotalNotes >= snapshot.Summary.PendingReviewNotes);
        Assert.All(snapshot.NotesByCompany, point => Assert.True(point.Value >= 0));
        Assert.All(snapshot.NotesByStatus, point => Assert.True(point.Value >= 0));
        Assert.All(snapshot.DuplicatesByType, point => Assert.True(point.Value >= 0));
    }

    private static async Task<Guid> ReadAdminIdAsync(DbConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            select u.id
            from auth.users u
            join auth.roles r on r.id = u.role_id
            where r.code = 'ADMIN' and u.is_active and not u.is_deleted
            limit 1
            """, connection);
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("An active administrator is required for the dashboard integration test."));
    }
}

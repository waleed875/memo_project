using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Database;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

/// <summary>
/// Creates the isolated database used by the owner-acceptance run.
/// This test intentionally keeps the database after a successful run so the
/// desktop/dashboard can be exercised manually against the same deterministic data.
/// </summary>
public sealed class GoldenDatasetAcceptanceSetupTests
{
    private const string DatabaseName = "audit_memo_integration_golden_20260816";

    [Fact]
    public async Task CreatesGoldenDatabaseAtLatestMigration()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var profile = new ConnectionProfileModel
        {
            Name = "GoldenAcceptance",
            Type = ConnectionProfileType.SERVER,
            Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "127.0.0.1",
            Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var port) ? port : 5432,
            Database = DatabaseName,
            Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres",
            Password = password
        };
        var factory = new DbConnectionFactory(new AppConfig
        {
            Database = new DatabaseSection { ActiveProfile = profile.Name, Profiles = [profile] }
        });

        await DropIsolatedDatabaseIfPresentAsync(factory, profile);
        await factory.EnsureDatabaseExistsAsync(profile);

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        await new MigrationRunner(factory, Path.Combine(root, "database")).RunAsync();

        var seedPath = Path.Combine(root, "tests", "acceptance", "golden-dataset-061.sql");
        var seed = await File.ReadAllTextAsync(seedPath);
        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(seed, connection, transaction) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        await using var verification = factory.CreateConnection();
        await verification.OpenAsync();
        await using var versionCommand = new NpgsqlCommand(
            "select database_version from system.database_settings where singleton = true",
            verification);
        Assert.Equal("070", Convert.ToString(await versionCommand.ExecuteScalarAsync()));

        await using var countCommand = new NpgsqlCommand(
            "select count(*) from core.notes where not is_deleted",
            verification);
        Assert.Equal(7L, Convert.ToInt64(await countCommand.ExecuteScalarAsync()));

        Console.WriteLine($"Golden acceptance database ready: {DatabaseName} (kept for owner UAT)");
    }

    private static async Task DropIsolatedDatabaseIfPresentAsync(DbConnectionFactory factory, ConnectionProfileModel profile)
    {
        await using var connection = factory.CreateMaintenanceConnection(profile);
        await connection.OpenAsync();
        await using var exists = new NpgsqlCommand(
            "select exists(select 1 from pg_database where datname = @database)",
            connection);
        exists.Parameters.AddWithValue("database", profile.Database);
        if (!Convert.ToBoolean(await exists.ExecuteScalarAsync()))
        {
            return;
        }

        var quotedDatabase = "\"" + profile.Database.Replace("\"", "\"\"") + "\"";
        await using var drop = new NpgsqlCommand("drop database " + quotedDatabase + " with (force)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

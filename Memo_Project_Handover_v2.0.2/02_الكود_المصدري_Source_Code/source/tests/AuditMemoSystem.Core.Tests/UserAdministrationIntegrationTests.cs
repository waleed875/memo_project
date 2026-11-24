using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Repositories;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class UserAdministrationIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentCrossDemotion_CannotRemoveEveryActiveAdministrator()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password),
            "AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run this integration test.");

        var factory = CreateFactory(password!);
        var modeService = new SystemModeService(factory);
        await modeService.LoadModeAsync();
        var service = new UserService(factory, modeService);
        var rootAdministratorId = await ReadIdAsync(factory, """
            select u.id
            from auth.users u
            join auth.roles r on r.id = u.role_id
            where r.code = 'ADMIN' and u.is_active and not u.is_deleted
            order by u.created_at
            limit 1
            """);
        var administratorRoleId = await ReadIdAsync(
            factory,
            "select id from auth.roles where code = 'ADMIN' and is_active");
        var dataEntryRoleId = await ReadIdAsync(
            factory,
            "select id from auth.roles where code = 'DATA_ENTRY' and is_active");
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var administratorA = NewAdministrator($"qa_admin_a_{suffix}", administratorRoleId);
        var administratorB = NewAdministrator($"qa_admin_b_{suffix}", administratorRoleId);

        try
        {
            await service.SaveUserAsync(administratorA, CreatePassword(), rootAdministratorId);
            await service.SaveUserAsync(administratorB, CreatePassword(), rootAdministratorId);

            var root = (await service.GetUsersAsync(administratorA.Id))
                .Single(user => user.Id == rootAdministratorId);
            root.RoleId = dataEntryRoleId;
            await service.SaveUserAsync(root, null, administratorA.Id);

            var targetB = CloneWithRole(administratorB, dataEntryRoleId);
            var targetA = CloneWithRole(administratorA, dataEntryRoleId);
            var attempts = await Task.WhenAll(
                CaptureAsync(() => service.SaveUserAsync(targetB, null, administratorA.Id)),
                CaptureAsync(() => service.SaveUserAsync(targetA, null, administratorB.Id)));

            Assert.Single(attempts, result => result is null);
            Assert.Single(attempts, result => result is not null);
            Assert.Equal(1, await CountActiveAdministratorsAsync(factory));
        }
        finally
        {
            var survivorId = await ReadIdAsync(factory, """
                select u.id
                from auth.users u
                join auth.roles r on r.id = u.role_id
                where r.code = 'ADMIN' and u.is_active and not u.is_deleted
                limit 1
                """);
            var users = await service.GetUsersAsync(survivorId);
            var root = users.Single(user => user.Id == rootAdministratorId);
            root.RoleId = administratorRoleId;
            root.IsActive = true;
            await service.SaveUserAsync(root, null, survivorId);

            foreach (var testAdministrator in new[] { administratorA, administratorB })
            {
                var current = (await service.GetUsersAsync(rootAdministratorId))
                    .Single(user => user.Id == testAdministrator.Id);
                current.IsActive = false;
                await service.SaveUserAsync(current, null, rootAdministratorId);
            }
        }
    }

    private static DbConnectionFactory CreateFactory(string password)
    {
        return new DbConnectionFactory(new AppConfig
        {
            Database = new DatabaseSection
            {
                ActiveProfile = "LocalAcceptance",
                Profiles =
                [
                    new ConnectionProfileModel
                    {
                        Name = "LocalAcceptance",
                        Type = ConnectionProfileType.SERVER,
                        Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost",
                        Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var integrationPort) ? integrationPort : 5432,
                        Database = PostgreSqlIntegrationFixture.DatabaseName,
                        Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres",
                        Password = password
                    }
                ]
            }
        });
    }

    private static UserModel NewAdministrator(string username, Guid roleId)
    {
        return new UserModel
        {
            Username = username,
            FullName = $"مدير اختبار {username}",
            RoleId = roleId,
            RoleCode = "ADMIN",
            IsActive = true
        };
    }

    private static UserModel CloneWithRole(UserModel source, Guid roleId)
    {
        return new UserModel
        {
            Id = source.Id,
            Username = source.Username,
            FullName = source.FullName,
            RoleId = roleId,
            IsActive = true
        };
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<Guid> ReadIdAsync(DbConnectionFactory factory, string sql)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("بيانات الاختبار المرجعية غير متاحة."));
    }

    private static async Task<long> CountActiveAdministratorsAsync(DbConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select count(*)
            from auth.users u
            join auth.roles r on r.id = u.role_id
            where r.code = 'ADMIN' and u.is_active and not u.is_deleted
            """;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string CreatePassword() => $"Qa-{Guid.NewGuid():N}";
}

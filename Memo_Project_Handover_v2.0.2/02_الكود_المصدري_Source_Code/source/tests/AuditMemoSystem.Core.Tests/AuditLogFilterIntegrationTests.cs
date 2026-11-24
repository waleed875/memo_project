using System.Security.Cryptography;
using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Repositories;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class AuditLogFilterIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminFilters_AreServerSidePagedAndDeniedToNonAdmin()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password), "AUDIT_MEMO_ACCEPTANCE_PASSWORD is required.");
        var factory = CreateFactory(password!);
        var mode = new SystemModeService(factory);
        await mode.LoadModeAsync();
        var audit = new AuditService(factory, mode);
        var users = new UserService(factory, mode);
        var admin = await ScalarGuid(factory, "select u.id from auth.users u join auth.roles r on r.id=u.role_id where r.code='ADMIN' and u.is_active and not u.is_deleted limit 1");
        var dataRole = await ScalarGuid(factory, "select id from auth.roles where code='DATA_ENTRY' and is_active limit 1");
        var type = await ScalarGuid(factory, "select id from core.note_types where code='ENTRY' and is_active and not is_deleted limit 1");
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = Guid.NewGuid();
        var note = Guid.NewGuid();
        var entry = new UserModel { Username = $"i_entry_{suffix}", FullName = "I Filter Entry", RoleId = dataRole, IsActive = true };
        var marker = $"I-FILTER-{suffix}";

        try
        {
            await users.SaveUserAsync(entry, Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)), admin);
            await Execute(factory, "insert into core.companies(id,name) values (@id,@name)", ("id", company), ("name", "I Company " + suffix));
            await Execute(factory, """
                insert into core.notes(id,note_number,note_number_normalized,note_type_id,company_id,arrival_at,status,created_by,updated_by)
                values (@id,@number,@normalized,@type,@company,current_date,'DRAFT',@user,@user)
                """, ("id", note), ("number", marker), ("normalized", AuditMemoSystem.Core.Validation.NoteNumberNormalizer.Normalize(marker)), ("type", type), ("company", company), ("user", entry.Id));
            await Execute(factory, """
                insert into audit.logs(user_id,action_type,schema_name,table_name,record_id,description,new_value) values
                (@user,'DATA_ENTRY_DUPLICATE_ATTEMPT','core','notes',@note,@d1,jsonb_build_object('CompanyId',@company::text,'normalizedNumber',@normalized,'noteNumber',@number)),
                (@user,'COMPANY_RESEND','core','notes',@note,@d2,jsonb_build_object('CompanyId',@company::text,'normalizedNumber',@normalized,'noteNumber',@number)),
                (@user,'CREATE_NOTE','core','notes',@note,@d3,null),
                (@user,'LOGIN','auth','users',@user,@d4,null),
                (@user,'DATA_ENTRY_DUPLICATE_ATTEMPT','core','notes',@note,@d5,jsonb_build_object('CompanyId',@company::text,'normalizedNumber',@normalized,'noteNumber',@number))
                """, ("user", entry.Id), ("note", note), ("company", company), ("normalized", AuditMemoSystem.Core.Validation.NoteNumberNormalizer.Normalize(marker)), ("number", marker),
                ("d1", marker + " attempt one"), ("d2", marker + " resend"), ("d3", marker + " create"), ("d4", marker + " login"), ("d5", marker + " attempt two"));

            var today = DateOnly.FromDateTime(DateTime.Today);
            var fromDate = today.AddDays(-1);
            var toDate = today.AddDays(1);
            var attempts = await audit.SearchLogsAsync(new AuditLogFilterModel { From = fromDate, To = toDate, UserId = entry.Id, DuplicateAttemptsOnly = true, PageSize = 20 }, admin);
            Assert.Equal(2, attempts.TotalCount);
            Assert.All(attempts.Items, x => Assert.Equal("DATA_ENTRY_DUPLICATE_ATTEMPT", x.ActionType));

            var byCompanyAndNumber = await audit.SearchLogsAsync(new AuditLogFilterModel { From = fromDate, To = toDate, CompanyId = company, NoteNumber = "i / filter / " + suffix, PageSize = 20 }, admin);
            Assert.Equal(4, byCompanyAndNumber.TotalCount);
            Assert.All(byCompanyAndNumber.Items, x => Assert.Equal(note, x.NoteId));

            var classification = await audit.SearchLogsAsync(new AuditLogFilterModel { From = fromDate, To = toDate, DuplicateClassification = DuplicateClassification.COMPANY_RESEND }, admin);
            Assert.Contains(classification.Items, x => x.Description == marker + " resend");

            var description = await audit.SearchLogsAsync(new AuditLogFilterModel { From = fromDate, To = toDate, DescriptionSearch = marker + " create" }, admin);
            Assert.Single(description.Items);

            var firstPage = await audit.SearchLogsAsync(new AuditLogFilterModel { From = fromDate, To = toDate, UserId = entry.Id, DescriptionSearch = marker, PageSize = 2, PageNumber = 1 }, admin);
            var secondPage = await audit.SearchLogsAsync(new AuditLogFilterModel { From = fromDate, To = toDate, UserId = entry.Id, DescriptionSearch = marker, PageSize = 2, PageNumber = 2 }, admin);
            Assert.Equal(5, firstPage.TotalCount);
            Assert.Equal(3, firstPage.TotalPages);
            Assert.Equal(2, firstPage.Items.Count);
            Assert.Equal(2, secondPage.Items.Count);
            Assert.Empty(firstPage.Items.Select(x => x.Id).Intersect(secondPage.Items.Select(x => x.Id)));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => audit.SearchLogsAsync(new AuditLogFilterModel { From = fromDate, To = toDate }, entry.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => audit.GetActionTypesAsync(entry.Id));
        }
        finally
        {
            await Execute(factory, "delete from audit.logs where description like @marker", ("marker", marker + "%"));
            await Execute(factory, "delete from core.notes where id=@id", ("id", note));
            await Execute(factory, "delete from core.companies where id=@id", ("id", company));
            if (entry.Id != Guid.Empty) await Execute(factory, "update auth.users set is_active=false,is_deleted=true where id=@id", ("id", entry.Id));
        }
    }

    private static DbConnectionFactory CreateFactory(string password) => new(new AppConfig { Database = new DatabaseSection { ActiveProfile = "I", Profiles = [new ConnectionProfileModel { Name = "I", Type = ConnectionProfileType.SERVER, Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost", Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var integrationPort) ? integrationPort : 5432, Database = PostgreSqlIntegrationFixture.DatabaseName, Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres", Password = password }] } });

    private static async Task<Guid> ScalarGuid(DbConnectionFactory factory, string sql)
    {
        await using var connection = factory.CreateConnection(); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing reference data"));
    }

    private static async Task Execute(DbConnectionFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = factory.CreateConnection(); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}

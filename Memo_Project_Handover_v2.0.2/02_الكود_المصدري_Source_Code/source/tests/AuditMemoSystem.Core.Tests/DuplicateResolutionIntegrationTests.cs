using System.Security.Cryptography;
using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Repositories;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class DuplicateResolutionIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task DuplicateWorkflow_CoversDecisionsAndLossless055Upgrade()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password), "AUDIT_MEMO_ACCEPTANCE_PASSWORD is required.");
        var factory = CreateFactory(password!);
        var mode = new SystemModeService(factory);
        await mode.LoadModeAsync();
        var audit = new AuditService(factory, mode);
        var types = new NoteTypeService(factory);
        var service = new NoteService(factory, mode, types, audit);
        var reports = new ReportService(factory);
        var users = new UserService(factory, mode);
        var admin = await ScalarGuid(factory, "select u.id from auth.users u join auth.roles r on r.id=u.role_id where r.code='ADMIN' and u.is_active and not u.is_deleted limit 1");
        var role = await ScalarGuid(factory, "select id from auth.roles where code='DATA_ENTRY' and is_active limit 1");
        var type = (await types.GetNoteTypesAsync()).First(x => x.Code == "ENTRY" && x.IsActive);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = Guid.NewGuid();
        var userOne = new UserModel { Username = $"h_one_{suffix}", FullName = "H One", RoleId = role, IsActive = true };
        var userTwo = new UserModel { Username = $"h_two_{suffix}", FullName = "H Two", RoleId = role, IsActive = true };
        var createdNotes = new List<Guid>();
        var writingDate = DateOnly.FromDateTime(DateTime.Today);
        // Same-year neighbor that can never cross a year boundary (Jan 1 / Dec 31 safe).
        var sameYearNeighbor = writingDate.DayOfYear > 180 ? writingDate.AddDays(-1) : writingDate.AddDays(1);
        // L5 numeric contract: note numbers must be positive integers (Arabic/Latin digits only).
        // Derive deterministic 6-digit numbers from the run suffix hex.
        var suffixDigits = new string(suffix.Where(char.IsDigit).ToArray());
        suffixDigits = (suffixDigits + "123456").Substring(0, 6);
        if (suffixDigits[0] == '0') suffixDigits = "1" + suffixDigits.Substring(1);
        var baseNumber = suffixDigits;
        var rollbackDigits = new string(suffix.Reverse().Where(char.IsDigit).ToArray());
        rollbackDigits = ((rollbackDigits + "654321").Substring(0, 6));
        if (rollbackDigits == baseNumber) rollbackDigits = "9" + rollbackDigits.Substring(1);
        if (rollbackDigits[0] == '0') rollbackDigits = "2" + rollbackDigits.Substring(1);
        var failureFunction = $"h_fail_{suffix}";
        var failureTrigger = $"h_trigger_{suffix}";
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var migration055Sql = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "database",
            "055_enforce_unique_company_duplicate_incident.sql"));

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveNoteAsync(
                new NoteModel { Id = Guid.NewGuid() }, userOne.Id, saveAsDraft: true));

            await Execute(factory, "insert into core.companies(id,name) values (@id,@name)", ("id", company), ("name", "H Company " + suffix));
            await users.SaveUserAsync(userOne, RandomPassword(), admin);
            await users.SaveUserAsync(userTwo, RandomPassword(), admin);

            var original = await BuildNote(types, type, company, baseNumber, writingDate, [1, 2, 3]);
            var originalId = await service.SaveNoteAsync(original, userOne.Id, saveAsDraft: true);
            createdNotes.Add(originalId);

            var crossUser = await service.FindDuplicateNotesAsync(null, $" {baseNumber} ", type.Id, company, writingDate, userTwo.Id);
            Assert.Contains(crossUser, x => x.NoteId == originalId);
            Assert.Contains(await service.FindDuplicateNotesAsync(null, baseNumber, type.Id, company, sameYearNeighbor, userTwo.Id), x => x.NoteId == originalId);
            Assert.Contains(await service.FindDuplicateNotesAsync(null, baseNumber, type.Id, company, null, userTwo.Id), x => x.NoteId == originalId);
            // L5 NUM-001: same number in a different writing year is NOT a duplicate.
            Assert.DoesNotContain(await service.FindDuplicateNotesAsync(null, baseNumber, type.Id, company, writingDate.AddYears(1), userTwo.Id), x => x.NoteId == originalId);

            var differentDocumentSameNumber = await BuildNote(types, type, company, baseNumber, writingDate, [9, 8, 7]);
            var caseOne = await service.ResolveDuplicateAsync(differentDocumentSameNumber, userTwo.Id, true,
                new DuplicateResolutionRequest { Classification = DuplicateClassification.COMPANY_DUPLICATE_NUMBER, MatchedNoteId = originalId });
            Assert.True(caseOne.NoteSaved);
            var secondId = Assert.IsType<Guid>(caseOne.NoteId);
            createdNotes.Add(secondId);
            Assert.Equal(1, await Count(factory, "select count(*) from core.duplicate_incidents where created_note_id=@id and classification='COMPANY_DUPLICATE_NUMBER'", ("id", secondId)));
            Assert.Equal(9, await Count(factory, "select octet_length(attachment_data) from core.notes where id=@id", ("id", secondId)));

            var survivorIncidentId = await ScalarGuid(
                factory,
                "select id from core.duplicate_incidents where created_note_id=@id and classification='COMPANY_DUPLICATE_NUMBER'",
                ("id", secondId));
            var legacyDuplicateId = Guid.NewGuid();
            await Execute(factory, "drop index if exists core.ux_duplicate_incidents_company_duplicate_created_note");
            await Execute(factory, """
                insert into core.duplicate_incidents (
                    id, classification, matched_note_id, created_note_id, company_id, factory_id,
                    production_line_id, note_type_id, note_number, note_number_normalized,
                    writing_date, decided_by, metadata, created_at)
                select
                    @legacy_id, classification, matched_note_id, created_note_id, company_id, factory_id,
                    production_line_id, note_type_id, note_number, note_number_normalized,
                    writing_date, decided_by, metadata, created_at + interval '1 second'
                from core.duplicate_incidents
                where id = @survivor_id
                """, ("legacy_id", legacyDuplicateId), ("survivor_id", survivorIncidentId));
            var legacySnapshot = await ScalarString(
                factory,
                "select to_jsonb(incident)::text from core.duplicate_incidents incident where id=@id",
                ("id", legacyDuplicateId));

            await ExecuteInTransaction(factory, migration055Sql);

            Assert.Equal(1, await Count(factory, "select count(*) from core.duplicate_incidents where created_note_id=@id and classification='COMPANY_DUPLICATE_NUMBER'", ("id", secondId)));
            Assert.Equal(1, await Count(factory, "select count(*) from core.duplicate_incidents_archive where id=@id", ("id", legacyDuplicateId)));
            Assert.Equal(survivorIncidentId, await ScalarGuid(factory, "select duplicate_of from core.duplicate_incidents_archive where id=@id", ("id", legacyDuplicateId)));
            Assert.Equal("HISTORICAL_DUPLICATE_CREATED_NOTE_ID", await ScalarString(factory, "select reason from core.duplicate_incidents_archive where id=@id", ("id", legacyDuplicateId)));
            Assert.Equal(
                legacySnapshot,
                await ScalarString(
                    factory,
                    "select (to_jsonb(archive) - 'duplicate_of' - 'archived_at' - 'reason')::text from core.duplicate_incidents_archive archive where id=@id",
                    ("id", legacyDuplicateId)));
            var archivedAt = await ScalarString(factory, "select archived_at::text from core.duplicate_incidents_archive where id=@id", ("id", legacyDuplicateId));

            await ExecuteInTransaction(factory, migration055Sql);

            Assert.Equal(1, await Count(factory, "select count(*) from core.duplicate_incidents_archive where id=@id", ("id", legacyDuplicateId)));
            Assert.Equal(archivedAt, await ScalarString(factory, "select archived_at::text from core.duplicate_incidents_archive where id=@id", ("id", legacyDuplicateId)));

            var savedDuplicate = await service.GetNoteAsync(secondId, userTwo.Id)
                ?? throw new InvalidOperationException("Saved duplicate note was not found.");
            var repeatedDecision = await service.ResolveDuplicateAsync(
                savedDuplicate,
                userTwo.Id,
                true,
                new DuplicateResolutionRequest
                {
                    Classification = DuplicateClassification.COMPANY_DUPLICATE_NUMBER,
                    MatchedNoteId = originalId
                });
            Assert.True(repeatedDecision.NoteSaved);
            Assert.Equal(secondId, repeatedDecision.NoteId);
            Assert.Equal(1, await Count(factory, "select count(*) from core.duplicate_incidents where created_note_id=@id and classification='COMPANY_DUPLICATE_NUMBER'", ("id", secondId)));
            Assert.Equal(1, await Count(factory, "select count(*) from audit.logs where record_id=@id and action_type='COMPANY_DUPLICATE_NUMBER'", ("id", secondId)));

            var reportFilter = new ReportFilterModel { CompanyId = company, From = writingDate, To = writingDate };
            var dashboard = await reports.GetReportTableAsync("DashboardSummary", reportFilter, admin);
            var dashboardSnapshot = await reports.GetDashboardSnapshotAsync(reportFilter, admin);
            Assert.Equal(dashboardSnapshot.Summary.TotalNotes, Convert.ToInt32(dashboard.Rows[0]["total_notes"]));
            Assert.Equal(dashboardSnapshot.Summary.CompanyResendNotes, Convert.ToInt32(dashboard.Rows[0]["company_resend_notes"]));
            Assert.Equal(1, Convert.ToInt32(dashboard.Rows[0]["duplicate_notes"]));
            var duplicateReport = await reports.GetReportTableAsync("Duplicates", reportFilter, admin);
            Assert.Equal("DuplicateIncidentReport", duplicateReport.TableName);
            Assert.Contains(Enumerable.Range(0, duplicateReport.Rows.Count), index => string.Equals(Convert.ToString(duplicateReport.Rows[index]["affected_note_number"]), baseNumber, StringComparison.Ordinal));

            var companyReport = await reports.GetReportTableAsync("CompanyDetailed", new ReportFilterModel { CompanyId = company, From = DateOnly.FromDateTime(DateTime.Today), To = DateOnly.FromDateTime(DateTime.Today) }, admin);
            var companySummary = companyReport.Rows.Cast<System.Data.DataRow>().Single(row => string.Equals(Convert.ToString(row["row_kind"]), "summary", StringComparison.Ordinal));
            Assert.Equal(2, Convert.ToInt32(companySummary["total_notes"]));
            Assert.Equal(2, Convert.ToInt32(companySummary["clean_notes"]));
            Assert.Equal(0, Convert.ToInt32(companySummary["error_notes"]));
            Assert.Equal(100M, Convert.ToDecimal(companySummary["average_accuracy"]));
            Assert.Equal(0, Convert.ToInt32(companySummary["company_resend_notes"]));
            Assert.Equal(1, Convert.ToInt32(companySummary["duplicate_notes"]));
            Assert.Contains(companyReport.Rows.Cast<System.Data.DataRow>(), row => string.Equals(Convert.ToString(row["row_kind"]), "monthly", StringComparison.Ordinal) && string.Equals(Convert.ToString(row["period_label"]), DateTime.Today.ToString("yyyy/MM"), StringComparison.Ordinal));
            var allMatches = await service.FindDuplicateNotesAsync(null, baseNumber, type.Id, company, writingDate, userOne.Id);
            Assert.True(allMatches.Count >= 2);
            Assert.True(allMatches[0].CreatedAt >= allMatches[1].CreatedAt);

            var resent = await BuildNote(types, type, company, baseNumber, writingDate, [6, 6, 6]);
            var notesBeforeResend = await Count(factory, "select count(*) from core.notes where company_id=@company", ("company", company));
            var caseTwo = await service.ResolveDuplicateAsync(resent, userOne.Id, true,
                new DuplicateResolutionRequest { Classification = DuplicateClassification.COMPANY_RESEND, MatchedNoteId = originalId });
            Assert.False(caseTwo.NoteSaved);
            Assert.Equal(notesBeforeResend, await Count(factory, "select count(*) from core.notes where company_id=@company", ("company", company)));
            Assert.Equal(1, await Count(factory, "select count(*) from core.duplicate_incidents where company_id=@company and classification='COMPANY_RESEND'", ("company", company)));
            var dashboardAfterResend = await reports.GetReportTableAsync("DashboardSummary", reportFilter, admin);
            Assert.Equal(1, Convert.ToInt32(dashboardAfterResend.Rows[0]["company_resend_notes"]));

            var attempted = await BuildNote(types, type, company, baseNumber, writingDate, [5, 5, 5]);
            var incidentsBeforeAttempt = await Count(factory, "select count(*) from core.duplicate_incidents where company_id=@company", ("company", company));
            var caseThree = await service.ResolveDuplicateAsync(attempted, userTwo.Id, true,
                new DuplicateResolutionRequest { Classification = DuplicateClassification.DATA_ENTRY_DUPLICATE_ATTEMPT, MatchedNoteId = originalId });
            Assert.False(caseThree.NoteSaved);
            Assert.Equal(incidentsBeforeAttempt, await Count(factory, "select count(*) from core.duplicate_incidents where company_id=@company", ("company", company)));
            Assert.Equal(1, await Count(factory, "select count(*) from audit.logs where user_id=@user and action_type='DATA_ENTRY_DUPLICATE_ATTEMPT' and new_value->>'matchedNoteId'=@matched", ("user", userTwo.Id), ("matched", originalId.ToString())));

            var notesBeforeInvalid = await Count(factory, "select count(*) from core.notes where company_id=@company", ("company", company));
            var invalid = await BuildNote(types, type, company, baseNumber, writingDate, [4]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveNoteAsync(invalid, userOne.Id, true, null,
                new DuplicateResolutionRequest { Classification = DuplicateClassification.COMPANY_DUPLICATE_NUMBER, MatchedNoteId = Guid.NewGuid() }));
            Assert.Equal(notesBeforeInvalid, await Count(factory, "select count(*) from core.notes where company_id=@company", ("company", company)));

            var rollbackNumber = rollbackDigits;
            var rollbackOriginal = await BuildNote(types, type, company, rollbackNumber, writingDate, [7]);
            createdNotes.Add(await service.SaveNoteAsync(rollbackOriginal, userOne.Id, saveAsDraft: true));
            var beforeForcedIncidentFailure = await Count(factory, "select count(*) from core.notes where company_id=@company", ("company", company));
            await Execute(factory, $"""
                create function core.{failureFunction}() returns trigger language plpgsql as $$
                begin
                    if new.note_number = '{rollbackNumber}' then raise exception 'forced H incident failure'; end if;
                    return new;
                end $$;
                create trigger {failureTrigger} before insert on core.duplicate_incidents
                for each row execute function core.{failureFunction}();
                """);
            try
            {
                var rollbackCandidate = await BuildNote(types, type, company, rollbackNumber, writingDate, [8]);
                await Assert.ThrowsAsync<PostgresException>(() => service.SaveNoteAsync(rollbackCandidate, userTwo.Id, true, null,
                    new DuplicateResolutionRequest { Classification = DuplicateClassification.COMPANY_DUPLICATE_NUMBER, MatchedNoteId = rollbackOriginal.Id }));
                Assert.Equal(beforeForcedIncidentFailure, await Count(factory, "select count(*) from core.notes where company_id=@company", ("company", company)));
            }
            finally
            {
                await Execute(factory, $"drop trigger if exists {failureTrigger} on core.duplicate_incidents; drop function if exists core.{failureFunction}();");
            }
        }
        finally
        {
            await Execute(factory, $"drop trigger if exists {failureTrigger} on core.duplicate_incidents; drop function if exists core.{failureFunction}();");
            await Execute(factory, "delete from audit.logs where (user_id=@u1 or user_id=@u2) and action_type in ('CREATE_NOTE','UPDATE_NOTE','SAVE_INDICATORS','COMPANY_DUPLICATE_NUMBER','COMPANY_RESEND','DATA_ENTRY_DUPLICATE_ATTEMPT')", ("u1", userOne.Id), ("u2", userTwo.Id));
            await Execute(factory, "delete from core.duplicate_incidents where company_id=@company", ("company", company));
            await Execute(factory, "delete from core.duplicate_incidents_archive where company_id=@company", ("company", company));
            await ExecuteInTransaction(factory, migration055Sql);
            await Execute(factory, "delete from core.notes where company_id=@company", ("company", company));
            await Execute(factory, "delete from core.companies where id=@company", ("company", company));
            await Execute(factory, "update auth.users set is_active=false,is_deleted=true where id in (@u1,@u2)", ("u1", userOne.Id), ("u2", userTwo.Id));
        }
    }

    private static DbConnectionFactory CreateFactory(string password) => new(new AppConfig { Database = new DatabaseSection { ActiveProfile = "H", Profiles = [new ConnectionProfileModel { Name = "H", Type = ConnectionProfileType.SERVER, Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost", Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var integrationPort) ? integrationPort : 5432, Database = PostgreSqlIntegrationFixture.DatabaseName, Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres", Password = password }] } });

    private static async Task<NoteModel> BuildNote(NoteTypeService types, NoteTypeModel type, Guid company, string number, DateOnly? writingDate, byte[] attachment)
    {
        var fields = await types.GetFieldsForTypeAsync(type.Id);
        return new NoteModel
        {
            NoteNumber = number, CompanyId = company, NoteTypeId = type.Id, WritingDate = writingDate,
            ArrivalAt = DateOnly.FromDateTime(DateTime.Today), AttachmentFileName = "memo.gif", AttachmentContentType = "image/gif", AttachmentData = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, .. attachment],
            FieldValues = fields.Where(x => x.IsActive).Select(x => new NoteFieldValueModel { FieldId = x.FieldId, FieldName = x.FieldNameAr, FieldValue = IndicatorValue.Correct }).ToList()
        };
    }

    private static async Task<Guid> ScalarGuid(DbConnectionFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = factory.CreateConnection(); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection); Add(command, parameters);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing reference data"));
    }

    private static async Task<string> ScalarString(DbConnectionFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = factory.CreateConnection(); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection); Add(command, parameters);
        return Convert.ToString(await command.ExecuteScalarAsync())
            ?? throw new InvalidOperationException("Missing scalar value");
    }

    private static async Task<int> Count(DbConnectionFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = factory.CreateConnection(); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection); Add(command, parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task Execute(DbConnectionFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        if (parameters.Any(x => x.Value is Guid value && value == Guid.Empty)) return;
        await using var connection = factory.CreateConnection(); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection); Add(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteInTransaction(DbConnectionFactory factory, string sql)
    {
        await using var connection = factory.CreateConnection(); await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static void Add(NpgsqlCommand command, IEnumerable<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
    }

    private static string RandomPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
}

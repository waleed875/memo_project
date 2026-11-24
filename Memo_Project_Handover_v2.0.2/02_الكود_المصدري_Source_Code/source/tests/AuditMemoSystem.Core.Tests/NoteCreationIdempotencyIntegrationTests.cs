using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Repositories;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class NoteCreationIdempotencyIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateOperation_ResponseLossAndConcurrency_WriteEveryEffectOnce()
    {
        var factory = PostgreSqlIntegrationFixture.CreateFactory("NoteCreateIdempotency");
        var firstService = await CreateServiceAsync(factory);
        var secondService = await CreateServiceAsync(factory);
        var adminId = await ScalarGuidAsync(factory, """
            select u.id from auth.users u
            join auth.roles r on r.id = u.role_id
            where r.code = 'ADMIN' and u.is_active and not u.is_deleted
            limit 1
            """);
        var otherUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var trackedNoteIds = new HashSet<Guid>();

        try
        {
            await ExecuteAsync(factory, "insert into core.companies(id, name) values (@id, @name)",
                ("id", companyId), ("name", $"Idempotency {suffix}"));
            await ExecuteAsync(factory, """
                insert into auth.users(id, username, password_hash, full_name, role_id)
                select @id, @username, 'TEST_ONLY', 'Idempotency other user', role.id
                from auth.roles role where role.code = 'DATA_ENTRY'
                """, ("id", otherUserId), ("username", $"idem_{suffix}"));

            var noteTypeService = new NoteTypeService(factory);
            var types = await noteTypeService.GetNoteTypesAsync();
            var entryType = types.First(type => type.Code == "ENTRY" && type.IsActive);
            var exitType = types.First(type => type.Code == "EXIT" && type.IsActive);
            var entryFields = (await noteTypeService.GetFieldsForTypeAsync(entryType.Id)).Where(field => field.IsActive).ToList();
            var exitFields = (await noteTypeService.GetFieldsForTypeAsync(exitType.Id)).Where(field => field.IsActive).ToList();
            Assert.NotEmpty(entryFields);
            Assert.NotEmpty(exitFields);
            var writingDate = DateOnly.FromDateTime(DateTime.Today);

            var originalNumber = NextNumericNumber();
            var originalId = await firstService.SaveNoteAsync(
                BuildNote(Guid.Empty, originalNumber, companyId, entryType.Id, entryFields, writingDate, "original-a"),
                adminId,
                saveAsDraft: true);
            trackedNoteIds.Add(originalId);
            var relatedId = await firstService.SaveNoteAsync(
                BuildNote(Guid.Empty, NextNumericNumber(), companyId, exitType.Id, exitFields, writingDate, "related-a"),
                adminId,
                saveAsDraft: true);
            trackedNoteIds.Add(relatedId);

            var lostResponseNoteId = Guid.NewGuid();
            var lostResponseOperationId = Guid.NewGuid();
            trackedNoteIds.Add(lostResponseNoteId);
            var lostResponseResolution = new DuplicateResolutionRequest
            {
                Classification = DuplicateClassification.COMPANY_DUPLICATE_NUMBER,
                MatchedNoteId = originalId
            };

            await Assert.ThrowsAsync<SimulatedResponseLossException>(async () =>
            {
                var committed = await firstService.ResolveDuplicateAsync(
                    BuildNote(lostResponseNoteId, originalNumber, companyId, entryType.Id, entryFields, writingDate, "candidate-a"),
                    adminId,
                    saveAsDraft: true,
                    lostResponseResolution,
                    relatedId,
                    synchronizeRelatedNote: true,
                    operationId: lostResponseOperationId);
                Assert.Equal(lostResponseNoteId, committed.NoteId);
                throw new SimulatedResponseLossException();
            });

            var fieldSnapshot = await ScalarStringAsync(factory, """
                select coalesce(jsonb_agg(jsonb_build_object(
                    'id', id,
                    'field_id', field_id,
                    'created_at', created_at,
                    'updated_at', updated_at) order by field_id)::text, '[]')
                from core.note_field_values where note_id = @note_id
                """, ("note_id", lostResponseNoteId));
            var replay = await firstService.ResolveDuplicateAsync(
                BuildNote(lostResponseNoteId, originalNumber, companyId, entryType.Id, entryFields, writingDate, "candidate-a"),
                adminId,
                saveAsDraft: true,
                lostResponseResolution,
                relatedId,
                synchronizeRelatedNote: true,
                operationId: lostResponseOperationId);

            Assert.Equal(lostResponseNoteId, replay.NoteId);
            await AssertSingleCreateEffectsAsync(
                factory,
                lostResponseOperationId,
                lostResponseNoteId,
                originalId,
                relatedId,
                entryFields.Count);
            Assert.Equal(fieldSnapshot, await ScalarStringAsync(factory, """
                select coalesce(jsonb_agg(jsonb_build_object(
                    'id', id,
                    'field_id', field_id,
                    'created_at', created_at,
                    'updated_at', updated_at) order by field_id)::text, '[]')
                from core.note_field_values where note_id = @note_id
                """, ("note_id", lostResponseNoteId)));

            var wrongUser = await Assert.ThrowsAsync<InvalidOperationException>(() => firstService.ResolveDuplicateAsync(
                BuildNote(lostResponseNoteId, originalNumber, companyId, entryType.Id, entryFields, writingDate, "candidate-a"),
                otherUserId,
                saveAsDraft: true,
                lostResponseResolution,
                relatedId,
                synchronizeRelatedNote: true,
                operationId: lostResponseOperationId));
            Assert.Contains("إعادة استخدام", wrongUser.Message);

            var wrongNoteId = Guid.NewGuid();
            var wrongNote = await Assert.ThrowsAsync<InvalidOperationException>(() => firstService.ResolveDuplicateAsync(
                BuildNote(wrongNoteId, originalNumber, companyId, entryType.Id, entryFields, writingDate, "candidate-a"),
                adminId,
                saveAsDraft: true,
                lostResponseResolution,
                relatedId,
                synchronizeRelatedNote: true,
                operationId: lostResponseOperationId));
            Assert.Contains("إعادة استخدام", wrongNote.Message);

            var wrongPayload = await Assert.ThrowsAsync<InvalidOperationException>(() => firstService.ResolveDuplicateAsync(
                BuildNote(lostResponseNoteId, originalNumber, companyId, entryType.Id, entryFields, writingDate, "changed-payload"),
                adminId,
                saveAsDraft: true,
                lostResponseResolution,
                relatedId,
                synchronizeRelatedNote: true,
                operationId: lostResponseOperationId));
            Assert.Contains("إعادة استخدام", wrongPayload.Message);

            var concurrentNumber = NextNumericNumber();
            var concurrentOriginalId = await firstService.SaveNoteAsync(
                BuildNote(Guid.Empty, concurrentNumber, companyId, entryType.Id, entryFields, writingDate, "original-b"),
                adminId,
                saveAsDraft: true);
            trackedNoteIds.Add(concurrentOriginalId);
            var concurrentRelatedId = await firstService.SaveNoteAsync(
                BuildNote(Guid.Empty, NextNumericNumber(), companyId, exitType.Id, exitFields, writingDate, "related-b"),
                adminId,
                saveAsDraft: true);
            trackedNoteIds.Add(concurrentRelatedId);
            var concurrentNoteId = Guid.NewGuid();
            var concurrentOperationId = Guid.NewGuid();
            trackedNoteIds.Add(concurrentNoteId);
            var concurrentResolution = new DuplicateResolutionRequest
            {
                Classification = DuplicateClassification.COMPANY_DUPLICATE_NUMBER,
                MatchedNoteId = concurrentOriginalId
            };
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<Guid?> CreateConcurrentlyAsync(NoteService service)
            {
                await gate.Task;
                var result = await service.ResolveDuplicateAsync(
                    BuildNote(concurrentNoteId, concurrentNumber, companyId, entryType.Id, entryFields, writingDate, "candidate-b"),
                    adminId,
                    saveAsDraft: true,
                    concurrentResolution,
                    concurrentRelatedId,
                    synchronizeRelatedNote: true,
                    operationId: concurrentOperationId);
                return result.NoteId;
            }

            var firstRequest = CreateConcurrentlyAsync(firstService);
            var secondRequest = CreateConcurrentlyAsync(secondService);
            gate.SetResult();
            var concurrentResults = await Task.WhenAll(firstRequest, secondRequest);

            Assert.All(concurrentResults, noteId => Assert.Equal(concurrentNoteId, noteId));
            await AssertSingleCreateEffectsAsync(
                factory,
                concurrentOperationId,
                concurrentNoteId,
                concurrentOriginalId,
                concurrentRelatedId,
                entryFields.Count);
        }
        finally
        {
            await CleanupAsync(factory, trackedNoteIds, companyId, otherUserId);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApprovedNote_SaveAsDraftIsRejectedWithoutChangingApprovalStamps()
    {
        var factory = PostgreSqlIntegrationFixture.CreateFactory("ApprovedDraftInvariant");
        var service = await CreateServiceAsync(factory);
        var adminId = await ScalarGuidAsync(factory, """
            select u.id from auth.users u
            join auth.roles r on r.id = u.role_id
            where r.code = 'ADMIN' and u.is_active and not u.is_deleted
            limit 1
            """);
        var companyId = await ScalarGuidAsync(factory, "select id from core.companies where is_active and not is_deleted limit 1");
        var typeService = new NoteTypeService(factory);
        var type = (await typeService.GetNoteTypesAsync()).First(item => item.Code == "ENTRY" && item.IsActive);
        var fields = (await typeService.GetFieldsForTypeAsync(type.Id)).Where(field => field.IsActive).ToList();
        var noteId = Guid.Empty;
        var approvedAt = new DateTime(2026, 8, 11, 10, 30, 0, DateTimeKind.Unspecified);

        try
        {
            noteId = await service.SaveNoteAsync(
                BuildNote(Guid.Empty, NextNumericNumber(), companyId, type.Id, fields, DateOnly.FromDateTime(DateTime.Today), "approved"),
                adminId,
                saveAsDraft: true);
            await ExecuteAsync(factory, """
                update core.notes
                set status = 'APPROVED', approved_by = @approved_by, approved_at = @approved_at
                where id = @id
                """, ("approved_by", adminId), ("approved_at", approvedAt), ("id", noteId));
            var approved = await service.GetNoteAsync(noteId, adminId)
                ?? throw new InvalidOperationException("Approved test note not found.");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SaveNoteAsync(approved, adminId, saveAsDraft: true));

            Assert.Equal("APPROVED", await ScalarStringAsync(factory, "select status from core.notes where id = @id", ("id", noteId)));
            Assert.Equal(adminId, await ScalarGuidAsync(factory, "select approved_by from core.notes where id = @id", ("id", noteId)));
            Assert.Equal(approvedAt, await ScalarDateTimeAsync(factory, "select approved_at from core.notes where id = @id", ("id", noteId)));
        }
        finally
        {
            if (noteId != Guid.Empty)
            {
                await CleanupAsync(factory, [noteId], Guid.Empty, Guid.Empty);
            }
        }
    }

    private static async Task<NoteService> CreateServiceAsync(DbConnectionFactory factory)
    {
        var mode = new SystemModeService(factory);
        await mode.LoadModeAsync();
        var audit = new AuditService(factory, mode);
        return new NoteService(factory, mode, new NoteTypeService(factory), audit);
    }

    private static NoteModel BuildNote(
        Guid id,
        string number,
        Guid companyId,
        Guid typeId,
        IReadOnlyList<NoteTypeFieldModel> fields,
        DateOnly writingDate,
        string notes) => new()
    {
        Id = id,
        NoteNumber = number,
        CompanyId = companyId,
        NoteTypeId = typeId,
        WritingDate = writingDate,
        ArrivalAt = writingDate,
        Notes = notes,
        ImagePath = "memo.gif",
        AttachmentFileName = "memo.gif",
        AttachmentContentType = "image/gif",
        AttachmentData = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 2, 3],
        FieldValues = fields.Select(field => new NoteFieldValueModel
        {
            FieldId = field.FieldId,
            FieldName = field.FieldNameAr,
            FieldValue = IndicatorValue.Correct
        }).ToList()
    };

    private static async Task AssertSingleCreateEffectsAsync(
        DbConnectionFactory factory,
        Guid operationId,
        Guid noteId,
        Guid matchedNoteId,
        Guid relatedNoteId,
        int expectedFieldCount)
    {
        Assert.Equal(1, await CountAsync(factory, "select count(*) from core.note_create_operations where operation_id = @id and note_id = @note_id", ("id", operationId), ("note_id", noteId)));
        Assert.Equal(1, await CountAsync(factory, "select count(*) from core.notes where id = @id", ("id", noteId)));
        Assert.Equal(expectedFieldCount, await CountAsync(factory, "select count(*) from core.note_field_values where note_id = @id", ("id", noteId)));
        Assert.Equal(1, await CountAsync(factory, "select count(*) from core.note_review_history where note_id = @id", ("id", noteId)));
        Assert.Equal(1, await CountAsync(factory, "select count(*) from core.note_review_history where note_id = @id and action = 'CREATE_DRAFT'", ("id", noteId)));
        Assert.Equal(1, await CountAsync(factory, "select count(*) from core.duplicate_incidents where created_note_id = @id and matched_note_id = @matched", ("id", noteId), ("matched", matchedNoteId)));
        Assert.Equal(1, await CountAsync(factory, "select count(*) from core.note_links where (source_note_id = @id and related_note_id = @related) or (source_note_id = @related and related_note_id = @id)", ("id", noteId), ("related", relatedNoteId)));
        foreach (var action in new[] { "CREATE_NOTE", "SAVE_INDICATORS", "COMPANY_DUPLICATE_NUMBER", "LINK_NOTES" })
        {
            Assert.Equal(1, await CountAsync(factory, "select count(*) from audit.logs where record_id = @id and action_type = @action", ("id", noteId), ("action", action)));
        }
        Assert.Equal(4, await CountAsync(factory, "select count(*) from audit.logs where record_id = @id", ("id", noteId)));
    }

    private static async Task CleanupAsync(
        DbConnectionFactory factory,
        IReadOnlyCollection<Guid> noteIds,
        Guid companyId,
        Guid otherUserId)
    {
        var ids = noteIds.Count == 0 ? [Guid.Empty] : noteIds.ToArray();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            delete from core.note_create_operations where note_id = any(@note_ids);
            delete from audit.logs where record_id = any(@note_ids);
            delete from core.duplicate_incidents where created_note_id = any(@note_ids) or matched_note_id = any(@note_ids);
            delete from core.duplicate_incidents_archive where created_note_id = any(@note_ids) or matched_note_id = any(@note_ids);
            delete from core.note_links where source_note_id = any(@note_ids) or related_note_id = any(@note_ids);
            delete from core.notes where id = any(@note_ids);
            delete from core.companies where id = @company_id;
            delete from auth.users where id = @other_user_id;
            """, connection);
        command.Parameters.AddWithValue("note_ids", ids);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("other_user_id", otherUserId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAsync(DbConnectionFactory factory, string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt32(await ScalarAsync(factory, sql, parameters));

    private static async Task<Guid> ScalarGuidAsync(DbConnectionFactory factory, string sql, params (string Name, object Value)[] parameters) =>
        (Guid)(await ScalarAsync(factory, sql, parameters) ?? throw new InvalidOperationException("Missing GUID scalar."));

    private static async Task<string> ScalarStringAsync(DbConnectionFactory factory, string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToString(await ScalarAsync(factory, sql, parameters)) ?? throw new InvalidOperationException("Missing string scalar.");

    private static async Task<DateTime> ScalarDateTimeAsync(DbConnectionFactory factory, string sql, params (string Name, object Value)[] parameters) =>
        (DateTime)(await ScalarAsync(factory, sql, parameters) ?? throw new InvalidOperationException("Missing timestamp scalar."));

    private static async Task<object?> ScalarAsync(DbConnectionFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddParameters(command, parameters);
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(DbConnectionFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameters(NpgsqlCommand command, IEnumerable<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
    }

    // L5 numeric contract: new note numbers must be positive integers.
    private static string NextNumericNumber()
    {
        var digits = new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).ToArray());
        digits = (digits + "123456789").Substring(0, 6);
        return digits[0] == '0' ? "1" + digits.Substring(1) : digits;
    }

    private sealed class SimulatedResponseLossException : Exception;
}

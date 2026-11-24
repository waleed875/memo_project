using System.Security.Cryptography;
using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Repositories;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class AcceptanceWorkflowIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task StaleNoteSaveAndDelete_AreRejected()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password), "AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run this integration test.");

        var factory = PostgreSqlIntegrationFixture.CreateFactory("LocalAcceptance", password);
        var modeService = new SystemModeService(factory);
        await modeService.LoadModeAsync();
        var auditService = new AuditService(factory, modeService);
        var noteTypeService = new NoteTypeService(factory);
        var noteService = new NoteService(factory, modeService, noteTypeService, auditService);
        var editorId = await ReadIdAsync(factory, """
            select u.id
            from auth.users u join auth.roles r on r.id = u.role_id
            where r.code = 'ADMIN' and u.is_active and not u.is_deleted
            limit 1
            """);
        var companyId = await ReadIdAsync(factory, "select id from core.companies where is_active and not is_deleted limit 1");
        var noteTypeId = await ReadIdAsync(factory, "select id from core.note_types where is_active and not is_deleted limit 1");
        var fields = await noteTypeService.GetFieldsForTypeAsync(noteTypeId);
        Guid? noteId = null;

        try
        {
            var draft = new NoteModel
            {
                NoteNumber = NextNumericNumber(),
                CompanyId = companyId,
                NoteTypeId = noteTypeId,
                ArrivalAt = DateOnly.FromDateTime(DateTime.Today),
                FieldValues = fields.Select(field => new NoteFieldValueModel
                {
                    FieldId = field.FieldId,
                    FieldName = field.FieldNameAr,
                    FieldValue = IndicatorValue.Unreviewed
                }).ToList()
            };
            noteId = await noteService.SaveNoteAsync(draft, editorId, saveAsDraft: true);

            var stale = await noteService.GetNoteAsync(noteId.Value, editorId);
            var current = await noteService.GetNoteAsync(noteId.Value, editorId);
            Assert.NotNull(stale);
            Assert.NotNull(current);

            current!.Notes = "تعديل متزامن صالح";
            await noteService.SaveNoteAsync(current, editorId, saveAsDraft: true);

            stale!.Notes = "تعديل قديم يجب رفضه";
            await Assert.ThrowsAsync<InvalidOperationException>(() => noteService.SaveNoteAsync(stale, editorId, saveAsDraft: true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => noteService.DeleteNoteAsync(noteId.Value, editorId, stale.UpdatedAt));

            var latest = await noteService.GetNoteAsync(noteId.Value, editorId);
            Assert.NotNull(latest);
            await noteService.DeleteNoteAsync(noteId.Value, editorId, latest!.UpdatedAt);
            noteId = null;
        }
        finally
        {
            if (noteId.HasValue)
            {
                await DeleteTestNoteAsync(factory, noteId.Value);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MissingWritingDate_CanBeReturnedResubmittedReviewedAndApproved()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password), "AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run this integration test.");

        var factory = PostgreSqlIntegrationFixture.CreateFactory("LocalAcceptance", password);
        var modeService = new SystemModeService(factory);
        await modeService.LoadModeAsync();
        var auditService = new AuditService(factory, modeService);
        var noteTypeService = new NoteTypeService(factory);
        var companyService = new CompanyService(factory, modeService, auditService);
        var userService = new UserService(factory, modeService);
        var noteService = new NoteService(factory, modeService, noteTypeService, auditService);
        var reportService = new ReportService(factory);

        var adminId = await ReadIdAsync(factory, """
            select u.id
            from auth.users u join auth.roles r on r.id = u.role_id
            where r.code = 'ADMIN' and u.is_active and not u.is_deleted
            limit 1
            """);
        var dataEntryRoleId = await ReadIdAsync(factory, "select id from auth.roles where code = 'DATA_ENTRY' and is_active");
        var reviewerRoleId = await ReadIdAsync(factory, "select id from auth.roles where code = 'REVIEWER' and is_active");
        var company = (await companyService.GetCompaniesAsync()).First(company => company.IsActive);
        var noteType = (await noteTypeService.GetNoteTypesAsync()).First(type => type.IsActive && type.Code == "ENTRY");
        var fields = await noteTypeService.GetFieldsForTypeAsync(noteType.Id);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var dataEntry = new UserModel
        {
            Username = $"qa_entry_{suffix}",
            FullName = "قبول آلي - مدخل بيانات",
            RoleId = dataEntryRoleId,
            IsActive = true
        };
        var reviewer = new UserModel
        {
            Username = $"qa_reviewer_{suffix}",
            FullName = "قبول آلي - مدقق",
            RoleId = reviewerRoleId,
            IsActive = true
        };
        Guid? noteId = null;
        Guid? relatedNoteId = null;

        try
        {
            await userService.SaveUserAsync(dataEntry, CreateRandomPassword(), adminId);
            await userService.SaveUserAsync(reviewer, CreateRandomPassword(), adminId);
            var note = new NoteModel
            {
                NoteNumber = NextNumericNumber(),
                CompanyId = company.Id,
                NoteTypeId = noteType.Id,
                WritingDate = null,
                ArrivalAt = DateOnly.FromDateTime(DateTime.Today),
                Notes = "مذكرة قبول آلية بتاريخ كتابة مفقود.",
                FieldValues = fields.Select(field => new NoteFieldValueModel
                {
                    FieldId = field.FieldId,
                    FieldName = field.FieldNameAr,
                    FieldValue = IndicatorValue.Correct
                }).ToList()
            };

            noteId = await noteService.SaveNoteAsync(note, dataEntry.Id, saveAsDraft: true);
            Assert.Equal(NoteStatus.DRAFT, note.Status);

            var createdSnapshot = await noteService.GetNoteAsync(noteId.Value, dataEntry.Id);
            Assert.NotNull(createdSnapshot);
            Assert.Equal(dataEntry.Id, createdSnapshot!.CreatedBy);
            Assert.Equal(dataEntry.FullName, createdSnapshot.CreatedByName);
            Assert.True(createdSnapshot.CreatedAt > DateTime.MinValue);
            var originalCreatedAt = createdSnapshot.CreatedAt;

            var queueBeforeSubmission = await noteService.GetReviewQueueAsync(reviewer.Id);
            Assert.DoesNotContain(queueBeforeSubmission, item => item.NoteId == noteId.Value);

            var savedDraft = await noteService.GetNoteAsync(noteId.Value, dataEntry.Id);
            Assert.NotNull(savedDraft);
            await noteService.SaveNoteAsync(savedDraft!, dataEntry.Id);
            Assert.Equal(NoteStatus.COMPLETE, savedDraft.Status);

            var queueAfterSubmission = await noteService.GetReviewQueueAsync(reviewer.Id);
            var queuedItem = Assert.Single(queueAfterSubmission.Where(item => item.NoteId == noteId.Value));
            Assert.Equal(dataEntry.FullName, queuedItem.CreatedByName);
            Assert.Equal(originalCreatedAt, queuedItem.CreatedAt);

            var searchResult = await noteService.SearchNotesAsync(new NoteSearchFilterModel
            {
                From = DateOnly.FromDateTime(DateTime.Today),
                To = DateOnly.FromDateTime(DateTime.Today)
            }, reviewer.Id);
            var searchItem = Assert.Single(searchResult.Items.Where(item => item.NoteId == noteId.Value));
            Assert.Equal(dataEntry.FullName, searchItem.CreatedByName);
            Assert.Equal(originalCreatedAt, searchItem.CreatedAt);
            Assert.Null(searchItem.ReviewStartedAt);
            Assert.Null(searchItem.ApprovedAt);
            Assert.Null(searchItem.ApprovedByName);

            savedDraft.Notes = "تعديل يجب رفضه بعد الإرسال";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => noteService.SaveNoteAsync(savedDraft, dataEntry.Id, saveAsDraft: true));

            var dashboard = await reportService.GetReportTableAsync("DashboardSummary", new ReportFilterModel
            {
                From = DateOnly.FromDateTime(DateTime.Today),
                To = DateOnly.FromDateTime(DateTime.Today)
            }, adminId);
            Assert.Single(dashboard.Rows);
            var factoryPerformance = await reportService.GetReportTableAsync("FactoryPerformance", new ReportFilterModel
            {
                From = DateOnly.FromDateTime(DateTime.Today),
                To = DateOnly.FromDateTime(DateTime.Today)
            }, adminId);
            Assert.NotNull(factoryPerformance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => reportService.GetReportTableAsync("UNAPPROVED_REPORT", new ReportFilterModel
            {
                From = DateOnly.FromDateTime(DateTime.Today),
                To = DateOnly.FromDateTime(DateTime.Today)
            }, dataEntry.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reportService.GetReportTableAsync("UserProductivity", new ReportFilterModel
            {
                From = DateOnly.FromDateTime(DateTime.Today),
                To = DateOnly.FromDateTime(DateTime.Today)
            }, dataEntry.Id));

            await noteService.StartReviewAsync(noteId.Value, reviewer.Id);
            var underReview = await noteService.GetNoteAsync(noteId.Value, reviewer.Id);
            Assert.NotNull(underReview);
            Assert.Equal(NoteStatus.UNDER_REVIEW, underReview!.Status);
            Assert.NotNull(underReview.ReviewStartedAt);
            var firstReviewStartedAt = underReview.ReviewStartedAt!.Value;

            await noteService.ReturnForCorrectionAsync(noteId.Value, reviewer.Id, "ملاحظة اختبار للإعادة");
            var returned = await noteService.GetNoteAsync(noteId.Value, dataEntry.Id);
            Assert.NotNull(returned);
            Assert.Equal(NoteStatus.RETURNED_FOR_CORRECTION, returned!.Status);
            Assert.Equal("ملاحظة اختبار للإعادة", returned.ReturnReason);
            Assert.Null(returned.ReviewStartedAt);

            await noteService.SaveNoteAsync(returned, dataEntry.Id);
            var resubmitted = await noteService.GetNoteAsync(noteId.Value, reviewer.Id);
            Assert.NotNull(resubmitted);
            Assert.Equal(NoteStatus.COMPLETE, resubmitted!.Status);
            Assert.Null(resubmitted.ReviewStartedAt);

            await noteService.StartReviewAsync(noteId.Value, reviewer.Id);
            var secondReview = await noteService.GetNoteAsync(noteId.Value, reviewer.Id);
            Assert.NotNull(secondReview);
            Assert.NotNull(secondReview!.ReviewStartedAt);
            Assert.True(secondReview.ReviewStartedAt.Value >= firstReviewStartedAt);
            var approvedCycleStartedAt = secondReview.ReviewStartedAt.Value;

            await noteService.ApproveNoteAsync(noteId.Value, reviewer.Id);
            var approved = await noteService.GetNoteAsync(noteId.Value, reviewer.Id);
            Assert.NotNull(approved);
            Assert.Equal(NoteStatus.APPROVED, approved!.Status);
            Assert.Null(approved.WritingDate);
            Assert.Equal(originalCreatedAt, approved.CreatedAt);
            Assert.Equal(dataEntry.FullName, approved.CreatedByName);
            Assert.Equal(approvedCycleStartedAt, approved.ReviewStartedAt!.Value);
            Assert.NotNull(approved.ApprovedAt);
            Assert.Equal(reviewer.Id, approved.ApprovedBy);
            Assert.Equal(reviewer.FullName, approved.ApprovedByName);
            Assert.Contains(approved.ReviewHistory, item => item.Action == "SUBMIT_FOR_REVIEW");
            Assert.Contains(approved.ReviewHistory, item => item.Action == "START_REVIEW");
            Assert.Contains(approved.ReviewHistory, item => item.Action == "RETURN_FOR_CORRECTION");
            Assert.Contains(approved.ReviewHistory, item => item.Action == "RESUBMIT_FOR_REVIEW");
            Assert.Contains(approved.ReviewHistory, item => item.Action == "APPROVE");

            var approvedSearch = await noteService.SearchNotesAsync(new NoteSearchFilterModel
            {
                From = DateOnly.FromDateTime(DateTime.Today),
                To = DateOnly.FromDateTime(DateTime.Today)
            }, reviewer.Id);
            var approvedSearchItem = Assert.Single(approvedSearch.Items.Where(item => item.NoteId == noteId.Value));
            Assert.Equal(originalCreatedAt, approvedSearchItem.CreatedAt);
            Assert.Equal(dataEntry.FullName, approvedSearchItem.CreatedByName);
            Assert.Equal(approvedCycleStartedAt, approvedSearchItem.ReviewStartedAt!.Value);
            Assert.Equal(approved.ApprovedAt, approvedSearchItem.ApprovedAt);
            Assert.Equal(reviewer.FullName, approvedSearchItem.ApprovedByName);

            var relatedTypeCode = noteType.Code.Equals("ENTRY", StringComparison.OrdinalIgnoreCase)
                ? "EXIT"
                : "ENTRY";
            var relatedType = (await noteTypeService.GetNoteTypesAsync())
                .First(type => type.IsActive && type.Code.Equals(relatedTypeCode, StringComparison.OrdinalIgnoreCase));
            var relatedFields = await noteTypeService.GetFieldsForTypeAsync(relatedType.Id);
            var relatedNote = new NoteModel
            {
                NoteNumber = NextNumericNumber(),
                CompanyId = company.Id,
                NoteTypeId = relatedType.Id,
                ArrivalAt = DateOnly.FromDateTime(DateTime.Today),
                Notes = "مذكرة مقابلة لاختبار ربط مذكرة معتمدة.",
                FieldValues = relatedFields.Select(field => new NoteFieldValueModel
                {
                    FieldId = field.FieldId,
                    FieldName = field.FieldNameAr,
                    FieldValue = IndicatorValue.Correct
                }).ToList()
            };
            relatedNoteId = await noteService.SaveNoteAsync(relatedNote, dataEntry.Id, saveAsDraft: true);

            await noteService.LinkNotesAsync(noteId.Value, relatedNoteId.Value, dataEntry.Id);
            var linksAfterCreate = await noteService.GetLinkCandidatesAsync(
                noteId.Value,
                dataEntry.Id,
                includeUnlinked: true);
            Assert.Contains(linksAfterCreate, candidate =>
                candidate.NoteId == relatedNoteId.Value && candidate.IsLinked);

            await noteService.UnlinkNotesAsync(noteId.Value, relatedNoteId.Value, dataEntry.Id);
            var linksAfterDelete = await noteService.GetLinkCandidatesAsync(
                noteId.Value,
                dataEntry.Id,
                includeUnlinked: true);
            Assert.DoesNotContain(linksAfterDelete, candidate =>
                candidate.NoteId == relatedNoteId.Value && candidate.IsLinked);

        }
        finally
        {
            if (relatedNoteId.HasValue)
            {
                await DeleteTestNoteAsync(factory, relatedNoteId.Value);
            }

            if (noteId.HasValue)
            {
                await DeleteTestNoteAsync(factory, noteId.Value);
            }

            if (dataEntry.Id != Guid.Empty)
            {
                dataEntry.IsActive = false;
                await userService.SaveUserAsync(dataEntry, null, adminId);
            }

            if (reviewer.Id != Guid.Empty)
            {
                reviewer.IsActive = false;
                await userService.SaveUserAsync(reviewer, null, adminId);
            }
        }
    }

    private static async Task<Guid> ReadIdAsync(DbConnectionFactory factory, string sql)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("بيانات الاختبار المرجعية غير متاحة."));
    }

    private static async Task DeleteTestNoteAsync(DbConnectionFactory factory, Guid noteId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from core.note_links where source_note_id = @id or related_note_id = @id; update core.notes set is_deleted = true where id = @id";
        command.Parameters.Add(new Npgsql.NpgsqlParameter("id", noteId));
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateRandomPassword()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    }

    // L5 numeric contract: new note numbers must be positive integers.
    private static string NextNumericNumber()
    {
        var digits = new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).ToArray());
        digits = (digits + "123456789").Substring(0, 6);
        return digits[0] == '0' ? "1" + digits.Substring(1) : digits;
    }
}

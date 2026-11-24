using System.Security.Cryptography;
using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Repositories;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class NoteLinkWorkflowIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SingleEntryExitLink_IsSearchableAtomicAndProtectedAcrossBothSides()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password), "AUDIT_MEMO_ACCEPTANCE_PASSWORD is required.");

        var factory = CreateFactory(password!);
        var modeService = new SystemModeService(factory);
        await modeService.LoadModeAsync();
        var auditService = new AuditService(factory, modeService);
        var typeService = new NoteTypeService(factory);
        var noteService = new NoteService(factory, modeService, typeService, auditService);
        var userService = new UserService(factory, modeService);
        var adminId = await ReadGuidAsync(factory, """
            select u.id from auth.users u
            join auth.roles r on r.id = u.role_id
            where r.code = 'ADMIN' and u.is_active and not u.is_deleted limit 1
            """);
        var entryRoleId = await ReadGuidAsync(factory, "select id from auth.roles where code = 'DATA_ENTRY' and is_active");
        var entryType = (await typeService.GetNoteTypesAsync()).Single(type => type.Code == "ENTRY" && type.IsActive);
        var exitType = (await typeService.GetNoteTypesAsync()).Single(type => type.Code == "EXIT" && type.IsActive);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var factoryA1 = Guid.NewGuid();
        var factoryA2 = Guid.NewGuid();
        var lineA1 = Guid.NewGuid();
        var lineA2 = Guid.NewGuid();
        var createdNotes = new List<Guid>();
        var entryUser = new UserModel
        {
            Username = $"f_entry_a_{suffix}", FullName = "اختبار F - المدخل الأول", RoleId = entryRoleId, IsActive = true
        };
        var otherEntryUser = new UserModel
        {
            Username = $"f_entry_b_{suffix}", FullName = "اختبار F - المدخل الثاني", RoleId = entryRoleId, IsActive = true
        };

        try
        {
            await SeedCatalogAsync(factory, companyA, companyB, factoryA1, factoryA2, lineA1, lineA2, suffix);
            await userService.SaveUserAsync(entryUser, RandomPassword(), adminId);
            await userService.SaveUserAsync(otherEntryUser, RandomPassword(), adminId);

            var exitFromOtherUser = await CreateNoteAsync(
                noteService, typeService, otherEntryUser.Id, exitType, companyA, factoryA2, lineA2,
                "1001");
            createdNotes.Add(exitFromOtherUser);

            var candidates = await noteService.SearchLinkCandidatesAsync(
                null, companyA, entryType.Id, "1001", entryUser.Id);
            var visibleAcrossUsers = Assert.Single(candidates.Where(item => item.NoteId == exitFromOtherUser));
            Assert.Equal(factoryA2, await ReadNullableGuidAsync(factory, "select factory_id from core.notes where id = @id", exitFromOtherUser));
            Assert.Equal("مصنع F الثاني " + suffix, visibleAcrossUsers.FactoryName);
            Assert.Equal("خط F الثاني " + suffix, visibleAcrossUsers.ProductionLineName);

            var entryWithDifferentFactory = await BuildNoteAsync(
                typeService, entryType, companyA, factoryA1, lineA1, "2001");
            var entryId = await noteService.SaveNoteAsync(
                entryWithDifferentFactory, entryUser.Id, saveAsDraft: true, relatedNoteId: exitFromOtherUser);
            createdNotes.Add(entryId);

            var currentLink = await noteService.GetLinkCandidatesAsync(entryId, entryUser.Id, includeUnlinked: false);
            Assert.Equal(exitFromOtherUser, Assert.Single(currentLink).NoteId);

            var secondExit = await CreateNoteAsync(
                noteService, typeService, entryUser.Id, exitType, companyA, factoryA1, lineA1,
                "1002");
            createdNotes.Add(secondExit);
            await noteService.LinkNotesAsync(entryId, secondExit, entryUser.Id);
            Assert.Equal(2, (await noteService.GetLinkCandidatesAsync(
                entryId, entryUser.Id, includeUnlinked: false)).Count);

            var thirdExit = await CreateNoteAsync(
                noteService, typeService, entryUser.Id, exitType, companyA, factoryA1, lineA1,
                "1003");
            createdNotes.Add(thirdExit);
            var availableReferences = await noteService.GetLinkCandidatesAsync(
                entryId, entryUser.Id, includeUnlinked: true);
            Assert.Contains(availableReferences, item => item.NoteId == thirdExit && !item.IsLinked);
            await noteService.LinkNotesAsync(entryId, thirdExit, entryUser.Id);
            Assert.Equal(3, (await noteService.GetLinkCandidatesAsync(
                entryId, entryUser.Id, includeUnlinked: false)).Count);

            await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var connection = factory.CreateConnection();
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    "insert into core.note_links(source_note_id, related_note_id, created_by) values (@source, @related, @user)",
                    connection);
                command.Parameters.AddWithValue("source", secondExit);
                command.Parameters.AddWithValue("related", entryId);
                command.Parameters.AddWithValue("user", entryUser.Id);
                await command.ExecuteNonQueryAsync();
            });

            var sameTypeOne = await CreateNoteAsync(
                noteService, typeService, entryUser.Id, entryType, companyA, null, null,
                "2002");
            var sameTypeTwo = await CreateNoteAsync(
                noteService, typeService, entryUser.Id, entryType, companyA, null, null,
                "2003");
            createdNotes.AddRange([sameTypeOne, sameTypeTwo]);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => noteService.LinkNotesAsync(sameTypeOne, sameTypeTwo, entryUser.Id));

            var otherCompanyExit = await CreateNoteAsync(
                noteService, typeService, entryUser.Id, exitType, companyB, null, null,
                "3001");
            createdNotes.Add(otherCompanyExit);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => noteService.LinkNotesAsync(sameTypeOne, otherCompanyExit, entryUser.Id));

            var rolledBackNote = await BuildNoteAsync(
                typeService, entryType, companyA, null, null, "2004");
            var rolledBackId = await noteService.SaveNoteAsync(
                rolledBackNote, entryUser.Id, saveAsDraft: true, relatedNoteId: exitFromOtherUser);
            createdNotes.Add(rolledBackId);
            Assert.NotEqual(Guid.Empty, rolledBackNote.Id);
            Assert.Equal(1, await CountAsync(factory, "select count(*) from core.notes where id = @id", rolledBackNote.Id));

            var preserveLink = await noteService.GetNoteAsync(entryId, entryUser.Id)
                ?? throw new InvalidOperationException("Saved entry note was not found.");
            await noteService.SaveNoteAsync(preserveLink, entryUser.Id, saveAsDraft: true);
            Assert.Equal(
                3,
                (await noteService.GetLinkCandidatesAsync(entryId, entryUser.Id, includeUnlinked: false)).Count);

            var replaceLink = await noteService.GetNoteAsync(entryId, entryUser.Id)
                ?? throw new InvalidOperationException("Saved entry note was not found.");
            await noteService.SaveNoteAsync(
                replaceLink,
                entryUser.Id,
                saveAsDraft: true,
                relatedNoteId: secondExit,
                synchronizeRelatedNote: true);
            Assert.Equal(secondExit, Assert.Single(await noteService.GetLinkCandidatesAsync(
                entryId, entryUser.Id, includeUnlinked: false)).NoteId);

            var clearLink = await noteService.GetNoteAsync(entryId, entryUser.Id)
                ?? throw new InvalidOperationException("Saved entry note was not found.");
            await noteService.SaveNoteAsync(
                clearLink,
                entryUser.Id,
                saveAsDraft: true,
                synchronizeRelatedNote: true);
            Assert.Empty(await noteService.GetLinkCandidatesAsync(entryId, entryUser.Id, includeUnlinked: false));

            await noteService.SaveNoteSourceAsync(new NoteSourceModel
            {
                NoteId = entryId,
                SourceType = NoteSourceType.Invoice,
                Invoices =
                [
                    new NoteSourceInvoiceModel
                    {
                        InvoiceNumber = $"INV-{suffix}-001",
                        InvoiceDate = new DateOnly(2026, 8, 20),
                        Destination = "وجهة اختبارية"
                    },
                    new NoteSourceInvoiceModel
                    {
                        InvoiceNumber = $"INV-{suffix}-002",
                        InvoiceDate = new DateOnly(2026, 8, 21),
                        Destination = "وجهة ثانية",
                        AttachmentFileName = "invoice.pdf",
                        AttachmentContentType = "application/pdf",
                        AttachmentData = [1, 2, 3]
                    }
                ]
            }, entryUser.Id);
            var invoiceSource = await noteService.GetNoteSourceAsync(entryId, entryUser.Id);
            Assert.NotNull(invoiceSource);
            Assert.Equal(NoteSourceType.Invoice, invoiceSource!.SourceType);
            Assert.Equal(2, invoiceSource.Invoices.Count);
            Assert.Equal("invoice.pdf", invoiceSource.Invoices[1].AttachmentFileName);
            await noteService.RemoveNoteSourceAsync(entryId, entryUser.Id);
            Assert.Null(await noteService.GetNoteSourceAsync(entryId, entryUser.Id));

            var entryFields = (await typeService.GetFieldsForTypeAsync(entryType.Id)).Where(field => field.IsActive).ToList();
            var exitFields = (await typeService.GetFieldsForTypeAsync(exitType.Id)).Where(field => field.IsActive).ToList();
            var exitFieldIds = exitFields.Select(field => field.FieldId).ToHashSet();
            var staleFieldId = entryFields.Select(field => field.FieldId).First(fieldId => !exitFieldIds.Contains(fieldId));
            Assert.Equal(1, await CountAsync(
                factory,
                "select count(*) from core.note_field_values where note_id = @id and field_id = @field_id",
                entryId,
                staleFieldId));

            var changeType = await noteService.GetNoteAsync(entryId, entryUser.Id)
                ?? throw new InvalidOperationException("Saved entry note was not found.");
            changeType.NoteTypeId = exitType.Id;
            changeType.FieldValues = exitFields.Select(field => new NoteFieldValueModel
            {
                FieldId = field.FieldId,
                FieldName = field.FieldNameAr,
                FieldValue = IndicatorValue.Correct
            }).ToList();
            await noteService.SaveNoteAsync(
                changeType,
                entryUser.Id,
                saveAsDraft: true,
                synchronizeRelatedNote: true);
            Assert.Equal(0, await CountAsync(
                factory,
                "select count(*) from core.note_field_values where note_id = @id and field_id = @field_id",
                entryId,
                staleFieldId));
        }
        finally
        {
            await CleanupAsync(factory, createdNotes, entryUser.Id, otherEntryUser.Id, companyA, companyB);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultiExitLink_LootExemption_AndInvoiceDuplicateWorkflow_AreEnforced()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password), "AUDIT_MEMO_ACCEPTANCE_PASSWORD is required.");

        var factory = CreateFactory(password!);
        var modeService = new SystemModeService(factory);
        await modeService.LoadModeAsync();
        var auditService = new AuditService(factory, modeService);
        var typeService = new NoteTypeService(factory);
        var noteService = new NoteService(factory, modeService, typeService, auditService);
        var userService = new UserService(factory, modeService);
        var adminId = await ReadGuidAsync(factory, """
            select u.id from auth.users u
            join auth.roles r on r.id = u.role_id
            where r.code = 'ADMIN' and u.is_active and not u.is_deleted limit 1
            """);
        var entryRoleId = await ReadGuidAsync(factory, "select id from auth.roles where code = 'DATA_ENTRY' and is_active");
        var entryType = (await typeService.GetNoteTypesAsync()).Single(type => type.Code == "ENTRY" && type.IsActive);
        var exitType = (await typeService.GetNoteTypesAsync()).Single(type => type.Code == "EXIT" && type.IsActive);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var factoryA1 = Guid.NewGuid();
        var factoryA2 = Guid.NewGuid();
        var lineA1 = Guid.NewGuid();
        var lineA2 = Guid.NewGuid();
        var createdNotes = new List<Guid>();
        var entryUser = new UserModel
        {
            Username = $"m_entry_a_{suffix}", FullName = "اختبار M - المدخل الأول", RoleId = entryRoleId, IsActive = true
        };
        var otherUser = new UserModel
        {
            Username = $"m_entry_b_{suffix}", FullName = "اختبار M - المدخل الثاني", RoleId = entryRoleId, IsActive = true
        };

        try
        {
            await SeedCatalogAsync(factory, companyA, companyB, factoryA1, factoryA2, lineA1, lineA2, suffix);
            await userService.SaveUserAsync(entryUser, RandomPassword(), adminId);
            await userService.SaveUserAsync(otherUser, RandomPassword(), adminId);

            // 1. One Entry Note linked to Multiple Exit Notes simultaneously
            var exitNote1 = await CreateNoteAsync(
                noteService, typeService, entryUser.Id, exitType, companyA, factoryA1, lineA1, "5001");
            var exitNote2 = await CreateNoteAsync(
                noteService, typeService, entryUser.Id, exitType, companyA, factoryA1, lineA1, "5002");
            createdNotes.AddRange([exitNote1, exitNote2]);

            var entryNote = await BuildNoteAsync(
                typeService, entryType, companyA, factoryA1, lineA1, "6001");
            var entryId = await noteService.SaveNoteAsync(
                entryNote, entryUser.Id, saveAsDraft: true, relatedNoteIds: [exitNote1, exitNote2]);
            createdNotes.Add(entryId);

            // Verify entry note has both related IDs populated
            var loadedEntry = await noteService.GetNoteAsync(entryId, entryUser.Id);
            Assert.NotNull(loadedEntry);
            Assert.NotNull(loadedEntry!.RelatedNoteIds);
            Assert.Equal(2, loadedEntry.RelatedNoteIds!.Count);
            Assert.Contains(exitNote1, loadedEntry.RelatedNoteIds);
            Assert.Contains(exitNote2, loadedEntry.RelatedNoteIds);

            // Verify link candidates show both exit notes as linked
            var linkedCandidates = await noteService.GetLinkCandidatesAsync(entryId, entryUser.Id, includeUnlinked: false);
            Assert.Equal(2, linkedCandidates.Count);
            Assert.Contains(linkedCandidates, c => c.NoteId == exitNote1 && c.IsLinked);
            Assert.Contains(linkedCandidates, c => c.NoteId == exitNote2 && c.IsLinked);

            // Verify search notes returns LinkCount == 2
            var searchResults = await noteService.SearchNotesAsync(new NoteSearchFilterModel
            {
                CompanyId = companyA,
                NoteTypeId = entryType.Id
            }, entryUser.Id);
            var entrySummary = Assert.Single(searchResults.Items.Where(n => n.NoteId == entryId));
            Assert.Equal(2, entrySummary.LinkCount);
            Assert.False(entrySummary.IsLoot);
            Assert.False(entrySummary.IsInvoice);

            // 2. Loot Note (غنائم ومستوردات):
            // - Marked as IsLoot = true
            // - Excluded from link candidate queries
            // - Excluded from unlinked_only searches
            // - Included in machinery efficiency calculation numerator
            var lootNote = await BuildNoteAsync(
                typeService, entryType, companyA, factoryA1, lineA1, "6002");
            lootNote.IsLoot = true;
            var lootId = await noteService.SaveNoteAsync(lootNote, entryUser.Id, saveAsDraft: true);
            createdNotes.Add(lootId);

            var loadedLoot = await noteService.GetNoteAsync(lootId, entryUser.Id);
            Assert.NotNull(loadedLoot);
            Assert.True(loadedLoot!.IsLoot);
            Assert.Empty(loadedLoot.RelatedNoteIds ?? []);

            // Search link candidates with exit note search should NOT return the loot note
            var candidateSearch = await noteService.SearchLinkCandidatesAsync(
                null, companyA, exitType.Id, "6002", entryUser.Id);
            Assert.DoesNotContain(candidateSearch, c => c.NoteId == lootId);

            // Search with UnlinkedOnly = true must NOT return the loot note (it's exempt, not an unlinked defect)
            var unlinkedSearch = await noteService.SearchNotesAsync(new NoteSearchFilterModel
            {
                CompanyId = companyA,
                UnlinkedOnly = true
            }, entryUser.Id);
            Assert.DoesNotContain(unlinkedSearch.Items, n => n.NoteId == lootId);

            // Search with IsLoot = true should return the loot note
            var lootFilterSearch = await noteService.SearchNotesAsync(new NoteSearchFilterModel
            {
                CompanyId = companyA,
                IsLoot = true
            }, entryUser.Id);
            Assert.Contains(lootFilterSearch.Items, n => n.NoteId == lootId && n.IsLoot);

            // Verify machinery efficiency formula in fn_company_metrics_snapshot_v4
            // Total entry notes for companyA = 2 (entry1 which is linked, and lootNote which is loot)
            // Both are compliant: machinery efficiency = ((1 + 1) / 2) * 100 = 100%
            await using (var connection = factory.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("""
                    select link_efficiency_percentage, linked_entry_notes, entry_notes
                    from reporting.fn_company_metrics_snapshot_v4(@company_id, null, null);
                    """, connection);
                command.Parameters.AddWithValue("company_id", companyA);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var machineryEfficiency = reader.GetDecimal(0);
                var compliantLinkedCount = reader.GetInt32(1);
                var totalEntries = reader.GetInt32(2);

                Assert.Equal(2, totalEntries);
                Assert.Equal(2, compliantLinkedCount);
                Assert.Equal(100.00m, machineryEfficiency);
            }

            // 3. Invoice Note (خروج بموجب فاتورة) and duplicate rule:
            // An invoice exit note can reuse numbers across different dates.
            // Duplicate detection only flags if it's the exact same writing date.
            var today = DateOnly.FromDateTime(DateTime.Today);
            var tomorrow = today.AddDays(1);
            var invoiceExitNote = await BuildNoteAsync(
                typeService, exitType, companyA, factoryA1, lineA1, "7001");
            invoiceExitNote.IsInvoice = true;
            invoiceExitNote.WritingDate = today;
            var invoiceId = await noteService.SaveNoteAsync(invoiceExitNote, entryUser.Id, saveAsDraft: true);
            createdNotes.Add(invoiceId);

            var loadedInvoice = await noteService.GetNoteAsync(invoiceId, entryUser.Id);
            Assert.NotNull(loadedInvoice);
            Assert.True(loadedInvoice!.IsInvoice);

            // Check duplicate with isInvoice = true on a DIFFERENT date -> Should NOT be flagged as duplicate
            var differentDateInvoiceDups = await noteService.FindDuplicateNotesAsync(
                null, "7001", exitType.Id, companyA, tomorrow, entryUser.Id, isInvoice: true);
            Assert.DoesNotContain(differentDateInvoiceDups, d => d.NoteId == invoiceId);

            // Check duplicate with isInvoice = true on the SAME date -> MUST be flagged as duplicate (exact same invoice)
            var sameDateInvoiceDups = await noteService.FindDuplicateNotesAsync(
                null, "7001", exitType.Id, companyA, today, entryUser.Id, isInvoice: true);
            Assert.Contains(sameDateInvoiceDups, d => d.NoteId == invoiceId);

            // Check duplicate with isInvoice = false (standard note) on different date -> flagged because standard notes check the whole year
            var standardNoteDups = await noteService.FindDuplicateNotesAsync(
                null, "7001", exitType.Id, companyA, tomorrow, entryUser.Id, isInvoice: false);
            Assert.Contains(standardNoteDups, d => d.NoteId == invoiceId);

            // Search with IsInvoice = true should return the invoice note
            var invoiceFilterSearch = await noteService.SearchNotesAsync(new NoteSearchFilterModel
            {
                CompanyId = companyA,
                IsInvoice = true
            }, entryUser.Id);
            Assert.Contains(invoiceFilterSearch.Items, n => n.NoteId == invoiceId && n.IsInvoice);
        }
        finally
        {
            await CleanupAsync(factory, createdNotes, entryUser.Id, otherUser.Id, companyA, companyB);
        }
    }

    private static DbConnectionFactory CreateFactory(string password) => new(new AppConfig
    {
        Database = new DatabaseSection
        {
            ActiveProfile = "LinkAcceptance",
            Profiles =
            [
                new ConnectionProfileModel
                {
                    Name = "LinkAcceptance", Type = ConnectionProfileType.SERVER, Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost", Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var integrationPort) ? integrationPort : 5432,
                    Database = PostgreSqlIntegrationFixture.DatabaseName, Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres", Password = password
                }
            ]
        }
    });

    private static async Task SeedCatalogAsync(
        DbConnectionFactory factory, Guid companyA, Guid companyB, Guid factoryA1, Guid factoryA2,
        Guid lineA1, Guid lineA2, string suffix)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            insert into core.companies(id, name) values
                (@company_a, @company_a_name), (@company_b, @company_b_name);
            insert into core.factories(id, company_id, name) values
                (@factory_a1, @company_a, @factory_a1_name), (@factory_a2, @company_a, @factory_a2_name);
            insert into core.production_lines(id, factory_id, name) values
                (@line_a1, @factory_a1, @line_a1_name), (@line_a2, @factory_a2, @line_a2_name);
            """, connection);
        command.Parameters.AddWithValue("company_a", companyA);
        command.Parameters.AddWithValue("company_b", companyB);
        command.Parameters.AddWithValue("company_a_name", "شركة F الأولى " + suffix);
        command.Parameters.AddWithValue("company_b_name", "شركة F الثانية " + suffix);
        command.Parameters.AddWithValue("factory_a1", factoryA1);
        command.Parameters.AddWithValue("factory_a2", factoryA2);
        command.Parameters.AddWithValue("factory_a1_name", "مصنع F الأول " + suffix);
        command.Parameters.AddWithValue("factory_a2_name", "مصنع F الثاني " + suffix);
        command.Parameters.AddWithValue("line_a1", lineA1);
        command.Parameters.AddWithValue("line_a2", lineA2);
        command.Parameters.AddWithValue("line_a1_name", "خط F الأول " + suffix);
        command.Parameters.AddWithValue("line_a2_name", "خط F الثاني " + suffix);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> CreateNoteAsync(
        NoteService service, NoteTypeService typeService, Guid userId, NoteTypeModel type, Guid companyId,
        Guid? factoryId, Guid? lineId, string number)
    {
        var note = await BuildNoteAsync(typeService, type, companyId, factoryId, lineId, number);
        return await service.SaveNoteAsync(note, userId, saveAsDraft: true);
    }

    private static async Task<NoteModel> BuildNoteAsync(
        NoteTypeService typeService, NoteTypeModel type, Guid companyId, Guid? factoryId, Guid? lineId, string number)
    {
        var fields = await typeService.GetFieldsForTypeAsync(type.Id);
        return new NoteModel
        {
            NoteNumber = number,
            CompanyId = companyId,
            FactoryId = factoryId,
            ProductionLineId = lineId,
            NoteTypeId = type.Id,
            WritingDate = DateOnly.FromDateTime(DateTime.Today),
            ArrivalAt = DateOnly.FromDateTime(DateTime.Today),
            FieldValues = fields.Where(field => field.IsActive).Select(field => new NoteFieldValueModel
            {
                FieldId = field.FieldId,
                FieldName = field.FieldNameAr,
                FieldValue = IndicatorValue.Correct
            }).ToList()
        };
    }

    private static async Task<Guid> ReadGuidAsync(DbConnectionFactory factory, string sql)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing reference data."));
    }

    private static async Task<Guid?> ReadNullableGuidAsync(DbConnectionFactory factory, string sql, Guid id)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync() as Guid?;
    }

    private static async Task<int> CountAsync(DbConnectionFactory factory, string sql, Guid id)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountAsync(DbConnectionFactory factory, string sql, Guid id, Guid fieldId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("field_id", fieldId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task CleanupAsync(
        DbConnectionFactory factory, IReadOnlyCollection<Guid> noteIds, Guid userOne, Guid userTwo, Guid companyA, Guid companyB)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            delete from core.note_links where source_note_id = any(@note_ids) or related_note_id = any(@note_ids);
            delete from audit.logs where record_id = any(@note_ids);
            delete from core.notes where company_id in (@company_a, @company_b);
            delete from core.production_lines where factory_id in (select id from core.factories where company_id in (@company_a, @company_b));
            delete from core.factories where company_id in (@company_a, @company_b);
            delete from core.companies where id in (@company_a, @company_b);
            update auth.users set is_active = false, is_deleted = true where id in (@user_one, @user_two);
            """, connection);
        command.Parameters.AddWithValue("note_ids", noteIds.ToArray());
        command.Parameters.AddWithValue("company_a", companyA);
        command.Parameters.AddWithValue("company_b", companyB);
        command.Parameters.AddWithValue("user_one", userOne);
        command.Parameters.AddWithValue("user_two", userTwo);
        await command.ExecuteNonQueryAsync();
    }

    private static string RandomPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
}

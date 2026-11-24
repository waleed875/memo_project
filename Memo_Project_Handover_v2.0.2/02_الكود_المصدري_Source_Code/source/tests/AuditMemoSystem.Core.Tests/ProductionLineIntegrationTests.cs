using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Services;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Repositories;
using Npgsql;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class ProductionLineIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProductionLines_EnforceFactoryOwnershipAndPreserveHistoricNotes()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password), "AUDIT_MEMO_ACCEPTANCE_PASSWORD is required to run this integration test.");

        var connectionFactory = CreateFactory(password!);
        var modeService = new SystemModeService(connectionFactory);
        await modeService.LoadModeAsync();
        var auditService = new AuditService(connectionFactory, modeService);
        var companyService = new CompanyService(connectionFactory, modeService, auditService);
        var factoryService = new FactoryService(connectionFactory, modeService, auditService);
        var productionLineService = new ProductionLineService(connectionFactory, modeService, auditService);
        var noteTypeService = new NoteTypeService(connectionFactory);
        var noteService = new NoteService(connectionFactory, modeService, noteTypeService, auditService);
        var adminId = await ReadIdAsync(connectionFactory, """
            select u.id from auth.users u join auth.roles r on r.id = u.role_id
            where r.code = 'ADMIN' and u.is_active and not u.is_deleted limit 1
            """);
        var company = (await companyService.GetCompaniesAsync()).First(item => item.IsActive);
        var noteType = (await noteTypeService.GetNoteTypesAsync()).First(item => item.IsActive);
        var fields = await noteTypeService.GetFieldsForTypeAsync(noteType.Id);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var firstFactory = new FactoryModel { CompanyId = company.Id, Name = $"QA Line A {suffix}", IsActive = true };
        var secondFactory = new FactoryModel { CompanyId = company.Id, Name = $"QA Line B {suffix}", IsActive = true };
        var firstLine = new ProductionLineModel { Name = "خط الاختبار", IsActive = true };
        var secondLine = new ProductionLineModel { Name = "خط الاختبار", IsActive = true };
        var createdNoteIds = new List<Guid>();

        try
        {
            await factoryService.SaveFactoryAsync(firstFactory, adminId);
            await factoryService.SaveFactoryAsync(secondFactory, adminId);
            firstLine.FactoryId = firstFactory.Id;
            secondLine.FactoryId = secondFactory.Id;
            await productionLineService.SaveProductionLineAsync(firstLine, adminId);
            await productionLineService.SaveProductionLineAsync(secondLine, adminId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => productionLineService.SaveProductionLineAsync(
                new ProductionLineModel { FactoryId = firstFactory.Id, Name = "خط الاختبار", IsActive = true }, adminId));

            createdNoteIds.Add(await SaveDraftAsync(noteService, fields, adminId, company.Id, noteType.Id, suffix, null, null));
            createdNoteIds.Add(await SaveDraftAsync(noteService, fields, adminId, company.Id, noteType.Id, suffix, firstFactory.Id, null));
            var validNoteId = await SaveDraftAsync(noteService, fields, adminId, company.Id, noteType.Id, suffix, firstFactory.Id, firstLine.Id);
            createdNoteIds.Add(validNoteId);

            await Assert.ThrowsAsync<InvalidOperationException>(() => SaveDraftAsync(
                noteService, fields, adminId, company.Id, noteType.Id, suffix, firstFactory.Id, secondLine.Id));

            await Assert.ThrowsAsync<PostgresException>(() => AssignLineDirectlyAsync(connectionFactory, validNoteId, secondLine.Id));
            await productionLineService.ToggleProductionLineAsync(firstLine.Id, false, adminId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => SaveDraftAsync(
                noteService, fields, adminId, company.Id, noteType.Id, suffix, firstFactory.Id, firstLine.Id));
            var historic = await noteService.GetNoteAsync(validNoteId, adminId);
            Assert.NotNull(historic);
            Assert.Equal(firstLine.Id, historic!.ProductionLineId);
            historic.Notes = "تعديل مع إبقاء الخط التاريخي المعطل";
            await noteService.SaveNoteAsync(historic, adminId, saveAsDraft: true);
            await AssignLineDirectlyAsync(connectionFactory, validNoteId, firstLine.Id);
            var preserved = await noteService.GetNoteAsync(validNoteId, adminId);
            Assert.Equal(firstLine.Id, preserved?.ProductionLineId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => productionLineService.DeleteProductionLineAsync(firstLine.Id, adminId));
        }
        finally
        {
            await CleanupAsync(connectionFactory, createdNoteIds, firstLine.Id, secondLine.Id, firstFactory.Id, secondFactory.Id);
        }
    }

    private static async Task<Guid> SaveDraftAsync(INoteService service, IReadOnlyList<NoteTypeFieldModel> fields, Guid userId, Guid companyId, Guid typeId, string suffix, Guid? factoryId, Guid? lineId)
    {
        var note = new NoteModel
        {
            NoteNumber = NextNumericNumber(),
            CompanyId = companyId,
            FactoryId = factoryId,
            ProductionLineId = lineId,
            NoteTypeId = typeId,
            WritingDate = DateOnly.FromDateTime(DateTime.Today),
            ArrivalAt = DateOnly.FromDateTime(DateTime.Today),
            FieldValues = fields.Select(field => new NoteFieldValueModel { FieldId = field.FieldId, FieldName = field.FieldNameAr, FieldValue = IndicatorValue.Unreviewed }).ToList()
        };
        return await service.SaveNoteAsync(note, userId, saveAsDraft: true);
    }

    // L5 numeric contract: new note numbers must be positive integers.
    private static string NextNumericNumber()
    {
        var digits = new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).ToArray());
        digits = (digits + "123456789").Substring(0, 6);
        return digits[0] == '0' ? "1" + digits.Substring(1) : digits;
    }

    private static async Task AssignLineDirectlyAsync(DbConnectionFactory factory, Guid noteId, Guid lineId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("update core.notes set production_line_id = @line_id where id = @note_id", connection);
        command.Parameters.AddWithValue("line_id", lineId);
        command.Parameters.AddWithValue("note_id", noteId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupAsync(DbConnectionFactory factory, IEnumerable<Guid> noteIds, Guid firstLineId, Guid secondLineId, Guid firstFactoryId, Guid secondFactoryId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("""
            update core.notes set is_deleted = true where id = any(@note_ids);
            update core.production_lines set is_deleted = true where id = any(@line_ids);
            update core.factories set is_deleted = true where id = any(@factory_ids);
            """, connection, transaction);
        command.Parameters.AddWithValue("note_ids", noteIds.ToArray());
        command.Parameters.AddWithValue("line_ids", new[] { firstLineId, secondLineId });
        command.Parameters.AddWithValue("factory_ids", new[] { firstFactoryId, secondFactoryId });
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
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
            Profiles = [new ConnectionProfileModel { Name = "LocalAcceptance", Type = ConnectionProfileType.SERVER, Host = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_HOST") ?? "localhost", Port = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_PORT"), out var integrationPort) ? integrationPort : 5432, Database = PostgreSqlIntegrationFixture.DatabaseName, Username = Environment.GetEnvironmentVariable("AUDIT_MEMO_INTEGRATION_USERNAME") ?? "postgres", Password = password }]
        }
    });
}

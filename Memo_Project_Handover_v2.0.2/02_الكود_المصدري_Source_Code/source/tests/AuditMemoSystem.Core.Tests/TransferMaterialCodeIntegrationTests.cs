using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Data.Connection;
using AuditMemoSystem.Data.Repositories;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

[Collection(PostgreSqlIntegrationFixture.CollectionName)]
public sealed class TransferMaterialCodeIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Transfer_HasOneRequiredMaterialCodeBetweenNameAndQuantity()
    {
        var password = Environment.GetEnvironmentVariable("AUDIT_MEMO_ACCEPTANCE_PASSWORD");
        Assert.False(string.IsNullOrWhiteSpace(password), "AUDIT_MEMO_ACCEPTANCE_PASSWORD is required.");

        var factory = new DbConnectionFactory(new AppConfig
        {
            Database = new DatabaseSection
            {
                ActiveProfile = "TransferAcceptance",
                Profiles =
                [
                    new ConnectionProfileModel
                    {
                        Name = "TransferAcceptance",
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
        var service = new NoteTypeService(factory);
        var types = await service.GetNoteTypesAsync();
        var transfer = types.Single(type => type.Code == "TRANSFER");
        var entry = types.Single(type => type.Code == "ENTRY");
        var exit = types.Single(type => type.Code == "EXIT");

        var transferFields = (await service.GetFieldsForTypeAsync(transfer.Id)).ToList();
        var materialCode = Assert.Single(transferFields.Where(field => field.FieldCode == "material_code"));
        Assert.True(materialCode.IsActive);
        Assert.True(materialCode.IsRequired);
        var materialNameIndex = transferFields.FindIndex(field => field.FieldCode == "material_name");
        var materialCodeIndex = transferFields.FindIndex(field => field.FieldCode == "material_code");
        var quantityIndex = transferFields.FindIndex(field => field.FieldCode == "quantity");
        Assert.True(materialNameIndex >= 0);
        Assert.Equal(materialNameIndex + 1, materialCodeIndex);
        Assert.Equal(materialCodeIndex + 1, quantityIndex);

        var entryFields = await service.GetFieldsForTypeAsync(entry.Id);
        var exitFields = await service.GetFieldsForTypeAsync(exit.Id);
        Assert.Single(entryFields.Where(field => field.FieldCode == "material_code"));
        Assert.Single(exitFields.Where(field => field.FieldCode == "material_code"));
    }
}

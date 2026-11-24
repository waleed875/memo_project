using AuditMemoSystem.Core.Models;
using Xunit;

namespace MemoDashboard.Backend.Tests;

public sealed class MasterDataSecurityAndContractTests
{
    [Fact]
    public void CompanyModel_DefaultsIdToEmptyAndActiveToTrue()
    {
        var model = new CompanyModel { Name = "شركة جديدة" };
        Assert.Equal(Guid.Empty, model.Id);
        Assert.True(model.IsActive);
        Assert.Equal("شركة جديدة", model.Name);
    }

    [Fact]
    public void FactoryModel_RequiresCompanyIdAndDefaultsToActive()
    {
        var companyId = Guid.NewGuid();
        var model = new FactoryModel { Name = "معمل جديد", CompanyId = companyId };
        Assert.Equal(Guid.Empty, model.Id);
        Assert.True(model.IsActive);
        Assert.Equal(companyId, model.CompanyId);
    }

    [Fact]
    public void ProductionLineModel_RequiresFactoryIdAndDefaultsToActive()
    {
        var factoryId = Guid.NewGuid();
        var model = new ProductionLineModel { Name = "خط جديد", FactoryId = factoryId };
        Assert.Equal(Guid.Empty, model.Id);
        Assert.True(model.IsActive);
        Assert.Equal(factoryId, model.FactoryId);
    }
}

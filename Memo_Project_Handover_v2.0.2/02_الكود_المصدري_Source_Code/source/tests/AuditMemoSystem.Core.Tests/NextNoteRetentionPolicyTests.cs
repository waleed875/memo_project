using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Notes;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class NextNoteRetentionPolicyTests
{
    [Fact]
    public void PrepareNext_WhenEnabled_RetainsOnlySafeContextAndResetsIndicators()
    {
        var companyId = Guid.NewGuid();
        var factoryId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var source = CreatePopulatedSource(companyId, factoryId, lineId, typeId, fieldId);

        var next = NextNoteRetentionPolicy.PrepareNext(source, retainSafeContext: true);

        Assert.Equal(companyId, next.CompanyId);
        Assert.Equal(factoryId, next.FactoryId);
        Assert.Equal(lineId, next.ProductionLineId);
        Assert.Equal(typeId, next.NoteTypeId);
        Assert.Equal(source.WritingDate, next.WritingDate);
        Assert.Equal(source.ArrivalAt, next.ArrivalAt);
        var indicator = Assert.Single(next.FieldValues);
        Assert.Equal(Guid.Empty, indicator.Id);
        Assert.Equal(Guid.Empty, indicator.NoteId);
        Assert.Equal(fieldId, indicator.FieldId);
        Assert.Equal(IndicatorValue.Unreviewed, indicator.FieldValue);
        Assert.Null(indicator.InactiveReason);
        AssertDocumentAndWorkflowStateIsEmpty(next);
    }

    [Fact]
    public void PrepareNext_WhenDisabled_ClearsAllContextAndDocumentState()
    {
        var source = CreatePopulatedSource(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var next = NextNoteRetentionPolicy.PrepareNext(source, retainSafeContext: false);

        Assert.Equal(Guid.Empty, next.CompanyId);
        Assert.Null(next.FactoryId);
        Assert.Null(next.ProductionLineId);
        Assert.Equal(Guid.Empty, next.NoteTypeId);
        Assert.Null(next.WritingDate);
        Assert.Equal(default, next.ArrivalAt);
        Assert.Empty(next.FieldValues);
        AssertDocumentAndWorkflowStateIsEmpty(next);
    }

    private static NoteModel CreatePopulatedSource(
        Guid companyId,
        Guid factoryId,
        Guid lineId,
        Guid typeId,
        Guid fieldId) => new()
    {
        Id = Guid.NewGuid(),
        NoteNumber = "MEMO-SECRET",
        CompanyId = companyId,
        CompanyName = "شركة",
        FactoryId = factoryId,
        FactoryName = "مصنع",
        ProductionLineId = lineId,
        ProductionLineName = "خط",
        NoteTypeId = typeId,
        NoteTypeName = "إدخال",
        WritingDate = new DateOnly(2026, 8, 1),
        ArrivalAt = new DateOnly(2026, 8, 2),
        ImagePath = "memo.png",
        FilePath = "legacy-path",
        AttachmentFileName = "memo.png",
        AttachmentContentType = "image/png",
        AttachmentData = [1, 2, 3],
        Notes = "ملاحظات لا يجوز نقلها",
        Status = NoteStatus.APPROVED,
        CreatedBy = Guid.NewGuid(),
        UpdatedBy = Guid.NewGuid(),
        ApprovedBy = Guid.NewGuid(),
        ApprovedAt = DateTime.UtcNow,
        ReviewerId = Guid.NewGuid(),
        ReviewStartedAt = DateTime.UtcNow,
        ReturnReason = "سبب",
        DuplicateReason = "تكرار",
        FieldValues =
        [
            new NoteFieldValueModel
            {
                Id = Guid.NewGuid(),
                NoteId = Guid.NewGuid(),
                FieldId = fieldId,
                FieldName = "بند",
                FieldValue = IndicatorValue.Incorrect,
                InactiveReason = "قيمة قديمة"
            }
        ],
        ReviewHistory = [new NoteReviewHistoryModel { Action = "APPROVE", Reason = "قديم", CreatedAt = DateTime.UtcNow }]
    };

    private static void AssertDocumentAndWorkflowStateIsEmpty(NoteModel next)
    {
        Assert.Equal(Guid.Empty, next.Id);
        Assert.Empty(next.NoteNumber);
        Assert.Empty(next.CompanyName);
        Assert.Null(next.FactoryName);
        Assert.Null(next.ProductionLineName);
        Assert.Empty(next.NoteTypeName);
        Assert.Null(next.ImagePath);
        Assert.Null(next.FilePath);
        Assert.Null(next.AttachmentFileName);
        Assert.Null(next.AttachmentContentType);
        Assert.Null(next.AttachmentData);
        Assert.Null(next.Notes);
        Assert.Equal(NoteStatus.DRAFT, next.Status);
        Assert.Null(next.CreatedBy);
        Assert.Null(next.UpdatedBy);
        Assert.Null(next.ApprovedBy);
        Assert.Null(next.ApprovedAt);
        Assert.Null(next.ReviewerId);
        Assert.Null(next.ReviewStartedAt);
        Assert.Null(next.ReturnReason);
        Assert.Null(next.DuplicateReason);
        Assert.Empty(next.ReviewHistory);
    }
}

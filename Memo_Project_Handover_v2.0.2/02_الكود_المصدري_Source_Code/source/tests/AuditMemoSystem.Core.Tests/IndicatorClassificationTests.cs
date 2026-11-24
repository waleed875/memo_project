using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Validation;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class IndicatorClassificationTests
{
    [Theory]
    [InlineData(IndicatorValue.Incorrect)]
    [InlineData(IndicatorValue.Missing)]
    [InlineData(IndicatorValue.Unclear)]
    [InlineData(IndicatorValue.Incomplete)]
    [InlineData(IndicatorValue.NotApplicable)]
    public void CalculateStatus_ClassifiesAllNonCorrectReviewedValuesAsErrors(IndicatorValue value)
    {
        Assert.Equal(NoteStatus.HAS_ERRORS, NoteValidator.CalculateStatus(1, [new NoteFieldValueModel { FieldValue = value }]));
    }

    [Theory]
    [InlineData(IndicatorValue.Correct)]
    [InlineData(IndicatorValue.Incorrect)]
    [InlineData(IndicatorValue.Missing)]
    [InlineData(IndicatorValue.Unclear)]
    [InlineData(IndicatorValue.Incomplete)]
    [InlineData(IndicatorValue.NotApplicable)]
    [InlineData(IndicatorValue.Unreviewed)]
    public void FieldValueValidator_AllowsMissingReasonForEveryValidValue(IndicatorValue value)
    {
        var fieldValue = new NoteFieldValueModel
        {
            FieldName = "اختبار",
            FieldValue = value,
            InactiveReason = null
        };

        Assert.Null(Record.Exception(() => FieldValueValidator.Validate(fieldValue)));
    }

    [Fact]
    public void FieldValueValidator_PreservesOptionalReasonWhenProvided()
    {
        var fieldValue = new NoteFieldValueModel
        {
            FieldValue = IndicatorValue.Missing,
            InactiveReason = "لم يرد في المذكرة"
        };

        FieldValueValidator.Validate(fieldValue);

        Assert.Equal("لم يرد في المذكرة", fieldValue.InactiveReason);
    }

    [Fact]
    public void FieldValueValidator_RejectsUndefinedValue()
    {
        var fieldValue = new NoteFieldValueModel { FieldValue = (IndicatorValue)short.MaxValue };

        Assert.Throws<InvalidOperationException>(() => FieldValueValidator.Validate(fieldValue));
    }

    [Fact]
    public void NoteValidator_AllowsRequiredIncorrectIndicatorWithoutReason()
    {
        var fieldId = Guid.NewGuid();
        var note = new NoteModel
        {
            NoteNumber = "123",
            CompanyId = Guid.NewGuid(),
            NoteTypeId = Guid.NewGuid(),
            WritingDate = new DateOnly(2026, 7, 20),
            ArrivalAt = new DateOnly(2026, 7, 23),
            FieldValues =
            [
                new NoteFieldValueModel
                {
                    FieldId = fieldId,
                    FieldValue = IndicatorValue.Incorrect,
                    InactiveReason = null
                }
            ]
        };
        var fields = new[]
        {
            new NoteTypeFieldModel
            {
                FieldId = fieldId,
                FieldNameAr = "رمز المادة",
                IsRequired = true,
                IsActive = true
            }
        };

        Assert.Null(Record.Exception(() => NoteValidator.Validate(note, fields)));
    }

    [Fact]
    public void NewIndicatorValue_DefaultsToUnreviewed()
    {
        Assert.Equal(IndicatorValue.Unreviewed, new NoteFieldValueModel().FieldValue);
    }

    [Fact]
    public void NoteValidator_RejectsSubmissionWhileRequiredIndicatorIsUnreviewed()
    {
        var fieldId = Guid.NewGuid();
        var note = new NoteModel
        {
            NoteNumber = "123",
            CompanyId = Guid.NewGuid(),
            NoteTypeId = Guid.NewGuid(),
            ArrivalAt = new DateOnly(2026, 7, 24),
            FieldValues = [new NoteFieldValueModel { FieldId = fieldId, FieldValue = IndicatorValue.Unreviewed }]
        };
        var fields = new[]
        {
            new NoteTypeFieldModel
            {
                FieldId = fieldId,
                FieldNameAr = "البند الإلزامي",
                IsRequired = true,
                IsActive = true
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => NoteValidator.Validate(note, fields));

        Assert.Contains("يجب مراجعة البند الإلزامي", exception.Message);
    }

    [Fact]
    public void CalculateStatus_AllowsOptionalUnreviewedFieldWhenRequiredFieldsAreCorrect()
    {
        var requiredId = Guid.NewGuid();
        var optionalId = Guid.NewGuid();
        var fields = new[]
        {
            new NoteTypeFieldModel { FieldId = requiredId, IsRequired = true, IsActive = true },
            new NoteTypeFieldModel { FieldId = optionalId, IsRequired = false, IsActive = true }
        };
        var values = new[]
        {
            new NoteFieldValueModel { FieldId = requiredId, FieldValue = IndicatorValue.Correct },
            new NoteFieldValueModel { FieldId = optionalId, FieldValue = IndicatorValue.Unreviewed }
        };

        Assert.Equal(NoteStatus.COMPLETE, NoteValidator.CalculateStatus(fields, values));
    }

    [Fact]
    public void CalculateStatus_RejectsRequiredUnreviewedFieldAsDraft()
    {
        var requiredId = Guid.NewGuid();
        var fields = new[]
        {
            new NoteTypeFieldModel { FieldId = requiredId, IsRequired = true, IsActive = true }
        };
        var values = new[]
        {
            new NoteFieldValueModel { FieldId = requiredId, FieldValue = IndicatorValue.Unreviewed }
        };

        Assert.Equal(NoteStatus.DRAFT, NoteValidator.CalculateStatus(fields, values));
    }

    [Fact]
    public void CalculateStatus_IgnoresInactiveFields()
    {
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var fields = new[]
        {
            new NoteTypeFieldModel { FieldId = activeId, IsRequired = true, IsActive = true },
            new NoteTypeFieldModel { FieldId = inactiveId, IsRequired = true, IsActive = false }
        };
        var values = new[]
        {
            new NoteFieldValueModel { FieldId = activeId, FieldValue = IndicatorValue.Correct },
            new NoteFieldValueModel
            {
                FieldId = inactiveId,
                FieldValue = IndicatorValue.Incorrect,
                InactiveReason = "قديم"
            }
        };

        Assert.Equal(NoteStatus.COMPLETE, NoteValidator.CalculateStatus(fields, values));
    }

    [Fact]
    public void ApprovedMemoEdit_IsAllowedForAdmin()
    {
        var note = new NoteModel { Status = NoteStatus.APPROVED };

        var exception = Record.Exception(() =>
            NoteValidator.EnsureApprovedEditAllowed(note, UserRole.ADMIN));

        Assert.Null(exception);
    }

    [Fact]
    public void ApprovedMemo_CannotBeSavedAsDraft()
    {
        var note = new NoteModel { Status = NoteStatus.APPROVED };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NoteValidator.EnsureDraftSaveAllowed(note, saveAsDraft: true));

        Assert.Contains("معتمدة", exception.Message);
    }

    [Theory]
    [InlineData(NoteStatus.DRAFT)]
    [InlineData(NoteStatus.RETURNED_FOR_CORRECTION)]
    public void EditableMemo_CanBeSavedAsDraft(NoteStatus status)
    {
        var note = new NoteModel { Status = status };

        Assert.Null(Record.Exception(() =>
            NoteValidator.EnsureDraftSaveAllowed(note, saveAsDraft: true)));
    }

    [Theory]
    [InlineData(UserRole.DATA_ENTRY)]
    [InlineData(UserRole.REVIEWER)]
    public void ApprovedMemoEdit_IsRejectedForNonAdmin(UserRole role)
    {
        var note = new NoteModel { Status = NoteStatus.APPROVED };

        Assert.Throws<UnauthorizedAccessException>(() =>
            NoteValidator.EnsureApprovedEditAllowed(note, role));
    }
}

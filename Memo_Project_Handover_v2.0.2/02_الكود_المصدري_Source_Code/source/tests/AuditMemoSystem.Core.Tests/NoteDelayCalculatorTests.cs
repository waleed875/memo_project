using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;
using AuditMemoSystem.Core.Validation;
using Xunit;

namespace AuditMemoSystem.Core.Tests;

public sealed class NoteDelayCalculatorTests
{
    [Fact]
    public void Calculate_FiveDaysIsWithinAllowedPeriod()
    {
        var assessment = NoteDelayCalculator.Calculate(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 6));
        Assert.Equal(NoteDelayState.WithinAllowedPeriod, assessment.State);
        Assert.Equal(5, assessment.Days);
        Assert.False(assessment.IsLate);
    }

    [Fact]
    public void Calculate_SixDaysIsLate()
    {
        var assessment = NoteDelayCalculator.Calculate(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 7));
        Assert.Equal(NoteDelayState.Late, assessment.State);
        Assert.Equal(6, assessment.Days);
        Assert.True(assessment.IsLate);
    }

    [Fact]
    public void Calculate_ReturnsNotCalculableWhenWritingDateIsMissing()
    {
        var assessment = NoteDelayCalculator.Calculate(null, new DateOnly(2026, 7, 8));
        Assert.Equal(NoteDelayState.CannotCalculate, assessment.State);
        Assert.Null(assessment.Days);
        Assert.False(assessment.IsLate);
    }

    [Fact]
    public void NoteModel_DefaultArrivalDateRequiresExplicitInput() => Assert.Equal(default, new NoteModel().ArrivalAt);

    [Theory]
    [InlineData(typeof(NoteDuplicateModel))]
    [InlineData(typeof(NoteLinkCandidateModel))]
    [InlineData(typeof(ReviewQueueItemModel))]
    [InlineData(typeof(NoteDetailsModel))]
    public void ArrivalDateContractsUseDateOnly(Type modelType) => Assert.Equal(typeof(DateOnly), modelType.GetProperty(nameof(NoteModel.ArrivalAt))!.PropertyType);

    [Fact]
    public void Validate_RejectsArrivalDateOlderThanWritingDate()
    {
        var note = new NoteModel { NoteNumber = "N-01", CompanyId = Guid.NewGuid(), NoteTypeId = Guid.NewGuid(), WritingDate = new DateOnly(2026, 7, 8), ArrivalAt = new DateOnly(2026, 7, 7), FieldValues = [] };
        var exception = Assert.Throws<InvalidOperationException>(() => NoteValidator.Validate(note, []));
        Assert.Contains("تاريخ الوصول", exception.Message);
    }

    [Fact]
    public void Validate_RejectsArrivalDateInTheFuture()
    {
        var note = new NoteModel { NoteNumber = "N-02", CompanyId = Guid.NewGuid(), NoteTypeId = Guid.NewGuid(), ArrivalAt = DateOnly.FromDateTime(DateTime.Today.AddDays(1)), FieldValues = [] };
        var exception = Assert.Throws<InvalidOperationException>(() => NoteValidator.Validate(note, []));
        Assert.Contains("المستقبل", exception.Message);
    }

    [Fact]
    public void Validate_AllowsWritingDateOnOrBeforeArrivalDate()
    {
        var note = new NoteModel
        {
            NoteNumber = "N-03",
            CompanyId = Guid.NewGuid(),
            NoteTypeId = Guid.NewGuid(),
            WritingDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
            ArrivalAt = DateOnly.FromDateTime(DateTime.Today),
            FieldValues = []
        };

        NoteValidator.Validate(note, []);
    }
    [Fact]
    public void NormalizeNoteNumber_IgnoresSeparatorsAndCase() => Assert.Equal("AB012", NoteNumberNormalizer.Normalize(" ab- 01 / 2 "));

    [Theory]
    [InlineData("2026-07-12", "2026-07-16", NoteDelayClassification.ON_TIME)]
    [InlineData("2026-07-12", "2026-07-17", NoteDelayClassification.ON_TIME)]
    [InlineData("2026-07-10", "2026-07-16", NoteDelayClassification.LATE_SAME_MONTH)]
    [InlineData("2026-07-29", "2026-08-03", NoteDelayClassification.ON_TIME)]
    [InlineData("2026-07-24", "2026-08-01", NoteDelayClassification.LATE_CROSS_MONTH)]
    [InlineData("2025-12-27", "2026-01-03", NoteDelayClassification.LATE_CROSS_MONTH)]
    [InlineData("2024-02-24", "2024-02-29", NoteDelayClassification.ON_TIME)]
    public void Classify_AppliesFiveDayRuleAcrossMonthAndYearBoundaries(string writing, string arrival, NoteDelayClassification expected) => Assert.Equal(expected, NoteDelayCalculator.Classify(DateOnly.Parse(writing), DateOnly.Parse(arrival)));

    [Fact]
    public void Classify_HandlesMissingAndInvalidDatesExplicitly()
    {
        Assert.Equal(NoteDelayClassification.MISSING_WRITING_DATE, NoteDelayCalculator.Classify(null, new DateOnly(2026, 7, 30)));
        Assert.Equal(NoteDelayClassification.INVALID_DATES, NoteDelayCalculator.Classify(new DateOnly(2026, 7, 30), new DateOnly(2026, 7, 29)));
    }

    [Fact]
    public void ExcessDays_StartsOnlyAfterAllowedFiveDays()
    {
        Assert.Equal(0, NoteDelayCalculator.ExcessDays(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 17)));
        Assert.Equal(1, NoteDelayCalculator.ExcessDays(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 18)));
        Assert.Null(NoteDelayCalculator.ExcessDays(null, new DateOnly(2026, 7, 17)));
    }
}

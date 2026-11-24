namespace MemoDashboard.Backend.Models;

public sealed class CompanyMetrics
{
    public int TotalNotes { get; set; }
    public int ExpectedItems { get; set; }
    public int ReviewedItems { get; set; }
    public int UnreviewedItems { get; set; }
    public int ApplicableReviewedItems { get; set; }
    public int CorrectItems { get; set; }
    public int ErrorItems { get; set; }
    public int NotApplicableItems { get; set; }
    public decimal? ReviewCoveragePercentage { get; set; }
    public decimal? ItemQualityPercentage { get; set; }
    public decimal? ErrorsPer100Checked { get; set; }
    public int QualityEligibleNoteCount { get; set; }
    public int CleanReviewedNotes { get; set; }
    public int AllNotApplicableNoteCount { get; set; }
    public int NoExpectedItemsNoteCount { get; set; }
    public decimal? CleanReviewedNotePercent { get; set; }
    public int ErrorNotes { get; set; }
    public int IncompleteReviewNotes { get; set; }
    public int TimeEligibleNotes { get; set; }
    public int OnTimeNotes { get; set; }
    public int LateNotes { get; set; }
    public int Late6To10Notes { get; set; }
    public int Late11To20Notes { get; set; }
    public int LateOver20Notes { get; set; }
    public int MissingWritingDateNotes { get; set; }
    public int InvalidDateOrderNotes { get; set; }
    public decimal? TimeCompliancePercentage { get; set; }
    public decimal? AverageLateDays { get; set; }
    public decimal? MedianLateDays { get; set; }
    public decimal? P90LateDays { get; set; }
    public int? MaximumLateDays { get; set; }
    public decimal? AverageExcessDelayDays { get; set; }
    public int DuplicateNumberIncidents { get; set; }
    public int CompanyResendIncidents { get; set; }
    public int DuplicateIncidents { get; set; }
    public int AffectedNotes { get; set; }
    public decimal? IncidentsPer100Notes { get; set; }
    public decimal? AffectedNoteRatePercentage { get; set; }
    public decimal? DuplicateFreePercentage { get; set; }
    public int EntryNotes { get; set; }
    public int LinkedEntryNotes { get; set; }
    public int UnlinkedEntryNotes { get; set; }
    public decimal? LinkEfficiencyPercentage { get; set; }
}

namespace MemoDashboard.Backend.Models;

public sealed class DashboardSummary
{
    public long TotalNotes { get; set; }

    public long NotesToday { get; set; }

    public long NotesCurrentMonth { get; set; }

    public long ActiveCompaniesTotal { get; set; }

    public long CompaniesWithNotes { get; set; }

    public long Approved { get; set; }

    public long PendingReview { get; set; }

    public long ReturnedForCorrection { get; set; }

    public long CompanyResend { get; set; }

    public long DuplicateNumbers { get; set; }

    public long DuplicateIncidentCount { get; set; }

    public long DuplicateAffectedNotes { get; set; }

    public long NonDuplicateNotes { get; set; }

    public long LateTotal { get; set; }

    public long LateSameMonth { get; set; }

    public long LateCrossMonth { get; set; }

    public long OnTime { get; set; }

    public double? AvgLateDays { get; set; }

    public double? MaxLateDays { get; set; }

    public DateTime? LatestArrivalDate { get; set; }
}

public sealed class CompanyTopItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public long NoteCount { get; set; }
}

public sealed class MonthlyActivityItem
{
    public string Month { get; set; } = string.Empty;

    public string MonthLabel { get; set; } = string.Empty;

    public long Count { get; set; }
}

public sealed class StatusDistributionItem
{
    public string Status { get; set; } = string.Empty;

    public string StatusLabel { get; set; } = string.Empty;

    public long Count { get; set; }
}

public sealed class DuplicateBreakdownItem
{
    public string Classification { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public long Count { get; set; }
}

public sealed class RecentNoteItem
{
    public Guid Id { get; set; }

    public string NoteNumber { get; set; } = string.Empty;

    public string? CompanyName { get; set; }

    public string? FactoryName { get; set; }

    public string? ProductionLineName { get; set; }

    public string? NoteTypeName { get; set; }

    public string Status { get; set; } = string.Empty;

    public string StatusLabel { get; set; } = string.Empty;

    public DateTime? WritingDate { get; set; }

    public DateTime? ArrivalAt { get; set; }

    public bool IsLate { get; set; }
}

public sealed class DashboardSnapshot
{
    public DashboardSummary Summary { get; set; } = new();

    public List<CompanyTopItem> Companies { get; set; } = new();

    public List<PerformanceItem> TimelinessByCompany { get; set; } = new();

    public List<MonthlyActivityItem> MonthlyActivity { get; set; } = new();

    public List<StatusDistributionItem> StatusDistribution { get; set; } = new();

    public List<DuplicateBreakdownItem> DuplicateBreakdown { get; set; } = new();

    public List<RecentNoteItem> RecentNotes { get; set; } = new();

    public bool IsHealthy { get; set; } = true;

    public DateTime ServerTime { get; set; }

    public DateTime GeneratedAt { get; set; }

    public int MinSample { get; set; } = 10;

    public string? ErrorMessage { get; set; }

    public SlaStats Sla { get; set; } = new();

    public List<BacklogAgeItem> BacklogByAge { get; set; } = new();

    public List<IncomingVsApprovedItem> IncomingVsApproved { get; set; } = new();

    public List<QualityByCompanyItem> QualityByCompany { get; set; } = new();

    public List<TopErrorFieldItem> TopErrorFields { get; set; } = new();

    public FieldQualitySummary? FieldQuality { get; set; }

    public List<IndicatorStateItem> IndicatorStateDistribution { get; set; } = new();

    public List<PerformanceItem> PerformanceByFactory { get; set; } = new();

    public List<PerformanceItem> PerformanceByLine { get; set; } = new();

    public List<DuplicateTrendItem> DuplicateTrend { get; set; } = new();

    public List<DataCompletenessItem> DataCompleteness { get; set; } = new();

    public DashboardEffectiveFilters EffectiveFilters { get; set; } = new();

    public CompanyMetrics? CompanyMetrics { get; set; }

    public CompanyMetrics? PreviousCompanyMetrics { get; set; }

    public DashboardPeriod? PreviousPeriod { get; set; }

    // Additive, typed contract for the three approved purposes. The active
    // dashboard still consumes the legacy fields until the owner approves
    // Phase C navigation and composition.
    public DashboardViewsContract Views { get; set; } = new();
}

public sealed class DashboardPeriod
{
    public DateTime From { get; set; }

    public DateTime? To { get; set; }

    public string DateBasis { get; set; } = "WritingDate";

    public DateTime? EntryCutoffDate { get; set; }
}
public sealed class DashboardEffectiveFilters
{
    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    public string DateBasis { get; set; } = "WritingDate";

    public Guid? CompanyId { get; set; }

    public Guid? FactoryId { get; set; }

    public Guid? ProductionLineId { get; set; }

    public Guid? NoteTypeId { get; set; }

    public string? Status { get; set; }

    public DateTime? EntryCutoffDate { get; set; }
}
public sealed class SlaStats
{
    public long EligibleNotes { get; set; }

    public long OnTimeNotes { get; set; }

    public long LateNotes { get; set; }

    public double? OnTimeRate { get; set; }

    public long InvalidDateOrderNotes { get; set; }
}

public sealed class BacklogAgeItem
{
    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public long Value { get; set; }
}

public sealed class IncomingVsApprovedItem
{
        public string PeriodKey { get; set; } = string.Empty;

    public string PeriodLabel { get; set; } = string.Empty;

    // Kept for compatibility with the rejected dashboard payload. New clients
    // must use PeriodKey/PeriodLabel instead of treating a formatted date as a
    // business identifier.
public string Label { get; set; } = string.Empty;

    public long Incoming { get; set; }

    public long Approved { get; set; }
}

public sealed class QualityByCompanyItem
{
        public Guid CompanyId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    // Kept for compatibility; it mirrors CompanyName and is not an identity.
public string Label { get; set; } = string.Empty;

    public long SampleSize { get; set; }

    public long CorrectCount { get; set; }

    public long ErrorCount { get; set; }

    public long QualityEligibleCount { get; set; }

    public long AllNotApplicableCount { get; set; }

    public double? AccuracyRate { get; set; }

    public double? ErrorRate { get; set; }
    public long TotalFieldCount { get; set; }

    public long CorrectFieldCount { get; set; }

    public long ErrorFieldCount { get; set; }

    public long UnknownFieldCount { get; set; }

    public double? FieldAccuracyRate { get; set; }
}

public sealed class FieldQualitySummary
{
    public long TotalFieldCount { get; set; }
    public long CorrectFieldCount { get; set; }
    public long ErrorFieldCount { get; set; }
    public long UnknownFieldCount { get; set; }
    public double? AccuracyRate { get; set; }
}

public sealed class TopErrorFieldItem
{
        public Guid FieldId { get; set; }

    public string FieldName { get; set; } = string.Empty;

    // Kept for compatibility; it mirrors FieldName and is not an identity.
public string Label { get; set; } = string.Empty;

    public long SampleSize { get; set; }

    public long ErrorCount { get; set; }

    public double? ErrorRate { get; set; }
    public long TotalFieldCount { get; set; }

    public long CorrectFieldCount { get; set; }

    public long ErrorFieldCount { get; set; }

    public long UnknownFieldCount { get; set; }

    public double? FieldAccuracyRate { get; set; }
}

public sealed class IndicatorStateItem
{
        public string StateLabel { get; set; } = string.Empty;

public int Code { get; set; }

    public long Value { get; set; }
}

public sealed class PerformanceItem
{
        public Guid DimensionId { get; set; }

public string Label { get; set; } = string.Empty;

    public long TotalNotes { get; set; }

    public long EligibleCount { get; set; }

    public long LateCount { get; set; }

    public long OnTimeCount { get; set; }

    public long SampleCount { get; set; }

    public long CorrectCount { get; set; }

    public long ErrorCount { get; set; }

    public long QualityEligibleCount { get; set; }

    public double? LateRate { get; set; }

    public double? AccuracyRate { get; set; }
}

public sealed class DuplicateTrendItem
{
        public string PeriodKey { get; set; } = string.Empty;

    public string PeriodLabel { get; set; } = string.Empty;

    // Kept for compatibility; it mirrors PeriodLabel and is not an identity.
public string Label { get; set; } = string.Empty;

    public long CompanyResend { get; set; }

    public long DuplicateNumber { get; set; }
}

public sealed class DataCompletenessItem
{
    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public long Value { get; set; }
}

public sealed class CompanyLookup
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class FactoryLookup
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class NoteTypeLookup
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;
}

public sealed class ProductionLineLookup
{
    public Guid Id { get; set; }

    public Guid FactoryId { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class DashboardLookups
{
    public List<CompanyLookup> Companies { get; set; } = new();

    public List<FactoryLookup> Factories { get; set; } = new();

    public List<ProductionLineLookup> ProductionLines { get; set; } = new();

    public List<NoteTypeLookup> NoteTypes { get; set; } = new();
}

public sealed class FilterQuery
{
    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    public string DateBasis { get; set; } = "WritingDate";

    public Guid? CompanyId { get; set; }

    public Guid? FactoryId { get; set; }

    public Guid? ProductionLineId { get; set; }

    public Guid? NoteTypeId { get; set; }

    public string? Status { get; set; }

    public DateTime? EntryCutoffDate { get; set; }
}

public sealed class ReviewQueueItem
{
    public Guid Id { get; set; }

    public string NoteNumber { get; set; } = string.Empty;

    public Guid? CompanyId { get; set; }

    public string? CompanyName { get; set; }

    public string? FactoryName { get; set; }

    public string? ProductionLineName { get; set; }

    public string? NoteTypeName { get; set; }

    public string Status { get; set; } = string.Empty;

    public string StatusLabel { get; set; } = string.Empty;

    public DateTime? WritingDate { get; set; }

    public DateTime? ArrivalAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int? DaysInQueue { get; set; }

    public bool IsLate { get; set; }
}

public sealed class ReviewQueueResult
{
    public List<ReviewQueueItem> Items { get; set; } = new();

    public long Total { get; set; }

    public int Limit { get; set; }

    public int Offset { get; set; }
}


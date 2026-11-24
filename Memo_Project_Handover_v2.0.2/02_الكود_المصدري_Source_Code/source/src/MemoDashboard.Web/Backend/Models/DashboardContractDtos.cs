namespace MemoDashboard.Backend.Models;

public sealed class DashboardViewsContract
{
    public DashboardContractContext Context { get; set; } = new();

    public ExecutiveDashboardContract ExecutiveSummary { get; set; } = new();

    public OperationsDashboardContract OperationsSummary { get; set; } = new();

    public CompanyDashboardContract? CompanySummary { get; set; }
}

public sealed class DashboardContractContext
{
    public DashboardContractPeriod Period { get; set; } = new();

    public DashboardContractScope Scope { get; set; } = new();
}

public sealed class DashboardContractPeriod
{
    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    public string DateBasis { get; set; } = "WritingDate";

    public DateTime? EntryCutoffDate { get; set; }
}

public sealed class DashboardContractScope
{
    public Guid? CompanyId { get; set; }

    public Guid? FactoryId { get; set; }

    public Guid? ProductionLineId { get; set; }

    public Guid? NoteTypeId { get; set; }

    public string? Status { get; set; }
}

public sealed class DashboardDataAvailabilityContract
{
    public string State { get; set; } = "no-data";

    public bool HasRows { get; set; }

    public string? Explanation { get; set; }
}

public sealed class ExecutiveDashboardContract
{
    public DashboardDataAvailabilityContract Availability { get; set; } = new();

    public DashboardSummary Summary { get; set; } = new();

    public SlaStats Sla { get; set; } = new();

    public CompanyMetrics? Metrics { get; set; }

    public FieldQualitySummary? FieldQuality { get; set; }

    public List<CompanyTopItem> CompanyComparison { get; set; } = new();

    public List<PerformanceItem> TimelinessByCompany { get; set; } = new();

    public List<MonthlyActivityItem> MonthlyTrend { get; set; } = new();

    public List<StatusDistributionItem> StatusDistribution { get; set; } = new();

    public List<QualityByCompanyItem> QualityByCompany { get; set; } = new();

    public List<TopErrorFieldItem> HighImpactProblems { get; set; } = new();
}

public sealed class OperationsDashboardContract
{
    public DashboardDataAvailabilityContract Availability { get; set; } = new();

    public DashboardSummary Summary { get; set; } = new();

    public SlaStats Sla { get; set; } = new();

    public CompanyMetrics? Metrics { get; set; }

    public List<IncomingVsApprovedItem> IncomingVsApproved { get; set; } = new();

    public List<BacklogAgeItem> BacklogByAge { get; set; } = new();

    public List<StatusDistributionItem> WorkflowStatus { get; set; } = new();

    public List<PerformanceItem> PerformanceByFactory { get; set; } = new();

    public List<PerformanceItem> PerformanceByLine { get; set; } = new();

    public List<DuplicateBreakdownItem> RepetitionClassification { get; set; } = new();

    public List<DuplicateTrendItem> RepetitionTrend { get; set; } = new();

    public List<IndicatorStateItem> IndicatorStates { get; set; } = new();

    public List<DataCompletenessItem> DataCompleteness { get; set; } = new();

    public List<TopErrorFieldItem> TopErrorFields { get; set; } = new();

    public List<RecentNoteItem> RecentNotes { get; set; } = new();
}

public sealed class CompanyDashboardContract
{
    public DashboardDataAvailabilityContract Availability { get; set; } = new();

    public CompanyTopItem? Company { get; set; }

    public CompanyMetrics? Metrics { get; set; }

    public CompanyMetrics? PreviousMetrics { get; set; }

    public DashboardPeriod? PreviousPeriod { get; set; }

    public CompanyComparisonContract? Comparison { get; set; }

    public List<MonthlyActivityItem> MonthlyTrend { get; set; } = new();

    public List<StatusDistributionItem> StatusDistribution { get; set; } = new();

    public List<IncomingVsApprovedItem> IncomingVsApproved { get; set; } = new();

    public List<DuplicateBreakdownItem> RepetitionClassification { get; set; } = new();

    public List<DuplicateTrendItem> RepetitionTrend { get; set; } = new();

    public List<PerformanceItem> PerformanceByFactory { get; set; } = new();

    public List<PerformanceItem> PerformanceByLine { get; set; } = new();

    public List<QualityByCompanyItem> QualityByCompany { get; set; } = new();

    public List<TopErrorFieldItem> TopErrorFields { get; set; } = new();

    public List<RecentNoteItem> RecentNotes { get; set; } = new();
}
public sealed class CompanyComparisonContract
{
    public double? ItemQualityDeltaPoints { get; set; }

    public double? TimeComplianceDeltaPoints { get; set; }

    public int? TotalNotesDelta { get; set; }
}

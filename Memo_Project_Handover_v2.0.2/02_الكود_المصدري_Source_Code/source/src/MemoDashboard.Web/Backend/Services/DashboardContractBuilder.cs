using MemoDashboard.Backend.Models;

namespace MemoDashboard.Backend.Services;

public static class DashboardContractBuilder
{
    public static DashboardViewsContract Build(DashboardSnapshot snapshot)
    {
        var hasRows = snapshot.Summary.TotalNotes > 0;
        var availability = new DashboardDataAvailabilityContract
        {
            State = hasRows ? "available" : "no-data",
            HasRows = hasRows,
            Explanation = hasRows ? null : "لا توجد مذكرات ضمن النطاق المحدد؛ لا تُعرض الحالة كصفر افتراضي."
        };

        var context = new DashboardContractContext
        {
            Period = new DashboardContractPeriod
            {
                From = snapshot.EffectiveFilters.From,
                To = snapshot.EffectiveFilters.To,
                DateBasis = snapshot.EffectiveFilters.DateBasis,
                EntryCutoffDate = snapshot.EffectiveFilters.EntryCutoffDate
            },
            Scope = new DashboardContractScope
            {
                CompanyId = snapshot.EffectiveFilters.CompanyId,
                FactoryId = snapshot.EffectiveFilters.FactoryId,
                ProductionLineId = snapshot.EffectiveFilters.ProductionLineId,
                NoteTypeId = snapshot.EffectiveFilters.NoteTypeId,
                Status = snapshot.EffectiveFilters.Status
            }
        };

        var executive = new ExecutiveDashboardContract
        {
            Availability = CloneAvailability(availability),
            Summary = snapshot.Summary,
            Sla = snapshot.Sla,
            Metrics = snapshot.CompanyMetrics,
            FieldQuality = snapshot.FieldQuality,
            CompanyComparison = snapshot.Companies.ToList(),
            TimelinessByCompany = snapshot.TimelinessByCompany.ToList(),
            MonthlyTrend = snapshot.MonthlyActivity.ToList(),
            StatusDistribution = snapshot.StatusDistribution.ToList(),
            QualityByCompany = snapshot.QualityByCompany.ToList(),
            HighImpactProblems = snapshot.TopErrorFields.ToList()
        };

        var operations = new OperationsDashboardContract
        {
            Availability = CloneAvailability(availability),
            Summary = snapshot.Summary,
            Sla = snapshot.Sla,
            Metrics = snapshot.CompanyMetrics,
            IncomingVsApproved = snapshot.IncomingVsApproved.ToList(),
            BacklogByAge = snapshot.BacklogByAge.ToList(),
            WorkflowStatus = snapshot.StatusDistribution.ToList(),
            PerformanceByFactory = snapshot.PerformanceByFactory.ToList(),
            PerformanceByLine = snapshot.PerformanceByLine.ToList(),
            RepetitionClassification = snapshot.DuplicateBreakdown.ToList(),
            RepetitionTrend = snapshot.DuplicateTrend.ToList(),
            IndicatorStates = snapshot.IndicatorStateDistribution.ToList(),
            DataCompleteness = snapshot.DataCompleteness.ToList(),
            TopErrorFields = snapshot.TopErrorFields.ToList(),
            RecentNotes = snapshot.RecentNotes.ToList()
        };

        CompanyDashboardContract? company = null;
        if (snapshot.EffectiveFilters.CompanyId.HasValue)
        {
            var companyId = snapshot.EffectiveFilters.CompanyId.Value;
            company = new CompanyDashboardContract
            {
                Availability = CloneAvailability(availability),
                Company = snapshot.Companies.FirstOrDefault(item => item.Id == companyId),
                Metrics = snapshot.CompanyMetrics,
                PreviousMetrics = snapshot.PreviousCompanyMetrics,
                PreviousPeriod = snapshot.PreviousPeriod,
                Comparison = BuildComparison(snapshot.CompanyMetrics, snapshot.PreviousCompanyMetrics),
                MonthlyTrend = snapshot.MonthlyActivity.ToList(),
                StatusDistribution = snapshot.StatusDistribution.ToList(),
                IncomingVsApproved = snapshot.IncomingVsApproved.ToList(),
                RepetitionClassification = snapshot.DuplicateBreakdown.ToList(),
                RepetitionTrend = snapshot.DuplicateTrend.ToList(),
                PerformanceByFactory = snapshot.PerformanceByFactory.ToList(),
                PerformanceByLine = snapshot.PerformanceByLine.ToList(),
                QualityByCompany = snapshot.QualityByCompany.ToList(),
                TopErrorFields = snapshot.TopErrorFields.ToList(),
                RecentNotes = snapshot.RecentNotes.ToList()
            };
        }

        return new DashboardViewsContract
        {
            Context = context,
            ExecutiveSummary = executive,
            OperationsSummary = operations,
            CompanySummary = company
        };
    }

    private static CompanyComparisonContract? BuildComparison(CompanyMetrics? current, CompanyMetrics? previous)
    {
        if (current is null || previous is null || current.TotalNotes <= 0 || previous.TotalNotes <= 0)
        {
            return null;
        }

        return new CompanyComparisonContract
        {
            ItemQualityDeltaPoints = Delta(current.ItemQualityPercentage, previous.ItemQualityPercentage),
            TimeComplianceDeltaPoints = Delta(current.TimeCompliancePercentage, previous.TimeCompliancePercentage),
            TotalNotesDelta = current.TotalNotes - previous.TotalNotes
        };
    }

    private static double? Delta(decimal? current, decimal? previous)
    {
        return current.HasValue && previous.HasValue
            ? Math.Round((double)(current.Value - previous.Value), 2)
            : null;
    }
    private static DashboardDataAvailabilityContract CloneAvailability(DashboardDataAvailabilityContract source)
    {
        return new DashboardDataAvailabilityContract
        {
            State = source.State,
            HasRows = source.HasRows,
            Explanation = source.Explanation
        };
    }
}

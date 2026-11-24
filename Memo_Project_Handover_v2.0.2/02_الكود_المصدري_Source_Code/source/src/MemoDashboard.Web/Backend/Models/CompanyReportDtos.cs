namespace MemoDashboard.Backend.Models;

public sealed class CompanyReportQuery
{
    public Guid? CompanyId { get; set; }

    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    public string DateBasis { get; set; } = "WritingDate";

    public DateTime? EntryCutoffDate { get; set; }

    // V3 parity scope: any of these set rejects the request (desktop V3 throws too).
    public Guid? FactoryId { get; set; }

    public Guid? ProductionLineId { get; set; }

    public Guid? NoteTypeId { get; set; }

    public string? Status { get; set; }
}

public sealed class CompanyReportIssue
{
    public string Title { get; set; } = string.Empty;

    public string LetterNumber { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string HijriDate { get; set; } = string.Empty;

    public string GregorianDate { get; set; } = string.Empty;

    public string Recipient { get; set; } = string.Empty;

    public string IssuerName { get; set; } = string.Empty;

    public DateTime IssuedAt { get; set; } = DateTime.Now;

    public decimal? FormEfficiencyIndex { get; set; }
}

public sealed class CompanyReportIdentity
{
    public string RepublicNameArabic { get; set; } = "الجمهورية العربية السورية";

    public string RepublicNameEnglish { get; set; } = "Syrian Arab Republic";

    public string MinistryName { get; set; } = string.Empty;

    public string MinistryNameEnglish { get; set; } = string.Empty;

    public string AdministrativeLevelOne { get; set; } = string.Empty;

    public string AdministrativeLevelOneEnglish { get; set; } = string.Empty;

    public string AdministrativeLevelTwo { get; set; } = string.Empty;

    public string AdministrativeLevelTwoEnglish { get; set; } = string.Empty;

    public string AdministrativeLevelThree { get; set; } = string.Empty;

    public string AdministrativeLevelThreeEnglish { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public string CompanyNameEnglish { get; set; } = string.Empty;

    public string IssuingEntityName { get; set; } = string.Empty;

    public string DefaultCity { get; set; } = string.Empty;
}

public sealed class CompanyReportMonthRow
{
    public DateTime MonthStart { get; set; }

    public string PeriodLabel { get; set; } = string.Empty;

    public int TotalNotes { get; set; }

    public decimal? ItemQualityPercentage { get; set; }

    public decimal? CleanReviewedNotePercent { get; set; }

    public int CleanReviewedNotes { get; set; }
}

public sealed class CompanyReportFieldRow
{
    public string FieldLabel { get; set; } = string.Empty;

    public int CorrectItems { get; set; }

    public int ErrorItems { get; set; }

    public int UnreviewedItems { get; set; }

    public int NotApplicableItems { get; set; }

    public int CheckedItems { get; set; }

    public decimal? ErrorRate { get; set; }
}

public sealed class CompanyReportFactoryRow
{
    public Guid? FactoryId { get; set; }

    public string FactoryLabel { get; set; } = string.Empty;

    public string FactoryName
    {
        get => !string.IsNullOrWhiteSpace(_factoryName) ? _factoryName : FactoryLabel;
        set => _factoryName = value;
    }
    private string _factoryName = string.Empty;

    public int Total { get; set; }

    public int TotalNotes
    {
        get => _totalNotes > 0 ? _totalNotes : Total;
        set => _totalNotes = value;
    }
    private int _totalNotes;

    public int CleanNotes { get; set; }

    public int CleanReviewedNotes
    {
        get => _cleanReviewedNotes > 0 ? _cleanReviewedNotes : CleanNotes;
        set => _cleanReviewedNotes = value;
    }
    private int _cleanReviewedNotes;

    public decimal? CleanReviewedNotePercent
    {
        get => _cleanReviewedNotePercent ?? (TotalNotes > 0 ? Math.Round((decimal)CleanReviewedNotes * 100m / TotalNotes, 2) : 0m);
        set => _cleanReviewedNotePercent = value;
    }
    private decimal? _cleanReviewedNotePercent;

    public int ErrorNotes { get; set; }

    public int IncompleteNotes { get; set; }

    public int OnTime { get; set; }

    public int Late { get; set; }

    public decimal? QualityPercentage { get; set; }
}

public sealed class CompanyReportData
{
    public Guid CompanyId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public DateTime From { get; set; }

    public DateTime To { get; set; }

    public string DateBasis { get; set; } = "WritingDate";

    public DateTime? EntryCutoffDate { get; set; }

    public CompanyMetrics Metrics { get; set; } = new();

    public int FactoryCount { get; set; }

    public DateTime? LastArrivalAt { get; set; }

    public List<CompanyReportMonthRow> Monthly { get; set; } = new();

    public List<CompanyReportFieldRow> FieldErrors { get; set; } = new();

    public List<CompanyReportFactoryRow> Factories { get; set; } = new();

    public CompanyReportIdentity Identity { get; set; } = new();

    public CompanyReportIssue Issue { get; set; } = new();

    public string FilterSummary { get; set; } = string.Empty;

    public string ApplicationName { get; set; } = "برنامج إدارة المذكرات";
}

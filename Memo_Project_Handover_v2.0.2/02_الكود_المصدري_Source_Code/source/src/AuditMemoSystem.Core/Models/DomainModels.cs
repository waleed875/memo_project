using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Services;

namespace AuditMemoSystem.Core.Models;

public sealed class UserModel
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public Guid RoleId { get; set; }
    public string RoleCode { get; set; } = "";
    public string RoleNameAr { get; set; } = "";
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
}

public sealed class RoleModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class CompanyModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class FactoryModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ProductionLineModel
{
    public Guid Id { get; set; }
    public Guid FactoryId { get; set; }
    public string FactoryName { get; set; } = "";
    public Guid? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class NoteTypeModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public sealed class NoteFieldModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? Description { get; set; }
    public int DefaultSortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class NoteTypeFieldDefinitionModel
{
    public Guid Id { get; set; }
    public Guid NoteTypeId { get; set; }
    public Guid? FieldId { get; set; }
    public string FieldCode { get; set; } = "";
    public string FieldNameAr { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public sealed class NoteTypeFieldImpactPreviewModel
{
    public Guid NoteTypeId { get; set; }
    public Guid? FieldId { get; set; }
    public int AffectedNoteCount { get; set; }
    public int ExistingValueCount { get; set; }
    public int MissingOrUnreviewedCount { get; set; }
    public bool IsNewField { get; set; }
}

public sealed class NoteTypeFieldModel
{
    public Guid Id { get; set; }
    public Guid NoteTypeId { get; set; }
    public Guid FieldId { get; set; }
    public string FieldCode { get; set; } = "";
    public string FieldNameAr { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; }
}

public sealed class NoteFieldValueModel
{
    public Guid Id { get; set; }
    public Guid NoteId { get; set; }
    public Guid FieldId { get; set; }
    public string FieldName { get; set; } = "";
    public IndicatorValue FieldValue { get; set; } = IndicatorValue.Unreviewed;
    public string? InactiveReason { get; set; }
}

public sealed class NoteModel
{
    public Guid Id { get; set; }
    public string NoteNumber { get; set; } = "";
    public Guid NoteTypeId { get; set; }
    public string NoteTypeName { get; set; } = "";
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public Guid? FactoryId { get; set; }
    public string? FactoryName { get; set; }
    public Guid? ProductionLineId { get; set; }
    public string? ProductionLineName { get; set; }
    public DateOnly? WritingDate { get; set; }
    public DateOnly ArrivalAt { get; set; }
    public string? ImagePath { get; set; }
    public string? FilePath { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public byte[]? AttachmentData { get; set; }
    public string? Notes { get; set; }
    public NoteStatus Status { get; set; } = NoteStatus.DRAFT;
    public Guid? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByName { get; set; }
    public Guid? ReviewerId { get; set; }
    public string? ReviewerName { get; set; }
    public DateTime? ReviewStartedAt { get; set; }
    public string? ReturnReason { get; set; }
    public string? DuplicateReason { get; set; }
    public bool IsLoot { get; set; }
    public bool IsInvoice { get; set; }
    public List<Guid> RelatedNoteIds { get; set; } = [];
    public List<NoteFieldValueModel> FieldValues { get; set; } = [];
    public List<NoteReviewHistoryModel> ReviewHistory { get; set; } = [];
}

public sealed class NoteDuplicateModel
{
    public Guid NoteId { get; set; }
    public string NoteNumber { get; set; } = "";
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public Guid? FactoryId { get; set; }
    public string? FactoryName { get; set; }
    public Guid? ProductionLineId { get; set; }
    public string? ProductionLineName { get; set; }
    public Guid NoteTypeId { get; set; }
    public string NoteTypeName { get; set; } = "";
    public DateOnly? WritingDate { get; set; }
    public DateOnly ArrivalAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
}

public sealed class DuplicateAttachmentModel
{
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public byte[]? Data { get; set; }
}

public sealed class DuplicateResolutionRequest
{
    public DuplicateClassification Classification { get; set; }
    public Guid MatchedNoteId { get; set; }
}

public sealed class DuplicateResolutionResult
{
    public bool NoteSaved { get; set; }
    public Guid? NoteId { get; set; }
    public string Message { get; set; } = "";
}

public sealed class NoteLinkCandidateModel
{
    public Guid NoteId { get; set; }
    public string NoteNumber { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string NoteTypeName { get; set; } = "";
    public string? FactoryName { get; set; }
    public string? ProductionLineName { get; set; }
    public DateOnly? WritingDate { get; set; }
    public DateOnly ArrivalAt { get; set; }
    public NoteStatus Status { get; set; }
    public bool IsLinked { get; set; }
    public bool IsInvoice { get; set; }
}

public sealed class NoteReviewHistoryModel
{
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    public string? UserName { get; set; }
    public string? UserRoleName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ReviewQueueItemModel
{
    public Guid NoteId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string NoteNumber { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string NoteTypeName { get; set; } = "";
    public string? FactoryName { get; set; }
    public string? ProductionLineName { get; set; }
    public DateOnly ArrivalAt { get; set; }
    public NoteStatus Status { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ReviewerId { get; set; }
    public string? ReviewerName { get; set; }
    public DateTime? ReviewStartedAt { get; set; }
}

public sealed class NoteDetailsModel
{
    public Guid NoteId { get; set; }
    public string NoteNumber { get; set; } = "";
    public string NoteTypeName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public Guid? FactoryId { get; set; }
    public string? FactoryName { get; set; }
    public Guid? ProductionLineId { get; set; }
    public string? ProductionLineName { get; set; }
    public DateOnly? WritingDate { get; set; }
    public DateOnly ArrivalAt { get; set; }
    public NoteStatus Status { get; set; }
    public string? ImagePath { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewStartedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int TotalFields { get; set; }
    public int ActiveCount { get; set; }
    public int InactiveCount { get; set; }
    public int UnknownCount { get; set; }
    public decimal AccuracyPercentage { get; set; }
    public decimal ErrorPercentage { get; set; }
    public bool IsLoot { get; set; }
    public bool IsInvoice { get; set; }
    public int LinkCount { get; set; }
}

public sealed class NoteAccuracyModel
{
    public int TotalFields { get; set; }
    public int ActiveCount { get; set; }
    public int InactiveCount { get; set; }
    public int UnknownCount { get; set; }
    public decimal AccuracyPercentage { get; set; }
    public decimal ErrorPercentage { get; set; }
}

public sealed class NoteSearchFilterModel
{
    public string? NoteNumber { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? FactoryId { get; set; }
    public Guid? ProductionLineId { get; set; }
    public Guid? NoteTypeId { get; set; }
    public Guid? CreatedBy { get; set; }
    public NoteStatus? Status { get; set; }
    public bool? HasWritingDate { get; set; }
    public bool? UnlinkedOnly { get; set; }
    public bool? IsLoot { get; set; }
    public bool? IsInvoice { get; set; }
    public DateOnly? EntryCutoffDate { get; set; }
    public string? DateBasis { get; set; } = "ArrivalDate";
    public DateOnly From { get; set; } = DateOnly.MinValue;
    public DateOnly To { get; set; } = DateOnly.MaxValue;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}
public sealed class IncompleteIndicatorFilterModel
{
    public Guid? CompanyId { get; set; }
    public Guid? NoteTypeId { get; set; }
    public Guid? FieldId { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

public enum NoteSourceType
{
    Exit,
    Invoice,
    Loot,
    Other
}

public sealed class NoteSourceModel
{
    public Guid Id { get; set; }
    public Guid NoteId { get; set; }
    public NoteSourceType SourceType { get; set; }
    public string? OtherDescription { get; set; }
    public List<NoteSourceExitReferenceModel> ExitReferences { get; set; } = [];
    public List<NoteSourceInvoiceModel> Invoices { get; set; } = [];
    public string? LootDescription { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class NoteSourceExitReferenceModel
{
    public Guid ExitNoteId { get; set; }
    public int ReferenceOrder { get; set; }
    public string? NoteNumber { get; set; }
}

public sealed class NoteSourceInvoiceModel
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public DateOnly? InvoiceDate { get; set; }
    public string? Destination { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public byte[]? AttachmentData { get; set; }
}

public sealed class IncompleteIndicatorQueueItemModel
{
    public Guid NoteId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string NoteNumber { get; set; } = "";
    public string NoteTypeName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string? FactoryName { get; set; }
    public string? ProductionLineName { get; set; }
    public DateOnly ArrivalAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public string? ImagePath { get; set; }
    public string? AttachmentFileName { get; set; }
    public Guid FieldId { get; set; }
    public string FieldName { get; set; } = "";
    public int SortOrder { get; set; }
    public IndicatorValue CurrentValue { get; set; } = IndicatorValue.Unreviewed;
    public int IncompleteFieldCount { get; set; }
    public int QueueTotalCount { get; set; }
}

public sealed class ReportFilterModel
{
    public Guid? CompanyId { get; set; }
    public Guid? FactoryId { get; set; }
    public Guid? ProductionLineId { get; set; }
    public Guid? NoteTypeId { get; set; }
    public Guid? NoteId { get; set; }
    public Guid? CreatedBy { get; set; }
    public NoteStatus? Status { get; set; }
    public bool? HasWritingDate { get; set; }
    /// <summary>يعرض مذكرات الإدخال التي لا تملك رابطاً فعالاً مع مذكرة إخراج.</summary>
    public bool? UnlinkedOnly { get; set; }
    /// <summary>يحدد المذكرات التي وصل تاريخها الإداري حتى نهاية هذا اليوم؛ لا يعتمد على وقت الإدخال التقني.</summary>
    public DateOnly? EntryCutoffDate { get; set; }
    public ReportDateBasis DateBasis { get; set; } = ReportDateBasis.WritingDate;
    public DateOnly From { get; set; } = ReportPeriodResolver.OperationalToday().AddMonths(-1);
    public DateOnly To { get; set; } = ReportPeriodResolver.OperationalToday();
    public ReportPeriodMode PeriodMode { get; set; } = ReportPeriodMode.CustomRange;
    public string? PeriodLabel { get; set; }
}

/// <summary>
/// القيم المؤسسية المشتركة التي تظهر في الترويسة الرسمية لكل التقارير.
/// لا تتضمن بيانات كتاب التقرير المتغيرة؛ تلك تحفظ في <see cref="ReportIssueParametersModel"/>.
/// </summary>
public sealed class ReportIdentitySettingsModel
{
    public string RepublicNameArabic { get; set; } = "الجمهورية العربية السورية";
    public string RepublicNameEnglish { get; set; } = "Syrian Arab Republic";
    public string MinistryName { get; set; } = "";
    public string MinistryNameEnglish { get; set; } = "";
    public string AdministrativeLevelOne { get; set; } = "";
    public string AdministrativeLevelOneEnglish { get; set; } = "";
    public string AdministrativeLevelTwo { get; set; } = "";
    public string AdministrativeLevelTwoEnglish { get; set; } = "";
    public string AdministrativeLevelThree { get; set; } = "";
    public string AdministrativeLevelThreeEnglish { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string CompanyNameEnglish { get; set; } = "";
    public string IssuingEntityName { get; set; } = "";
    public string DefaultCity { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// بيانات الكتاب الخاصة بعملية إصدار تقرير واحدة. لا تعدل إعدادات هوية المؤسسة المحفوظة.
/// </summary>
public sealed class ReportIssueParametersModel
{
    public string Title { get; set; } = "";
    public string LetterNumber { get; set; } = "";
    public string City { get; set; } = "";
    public string HijriDate { get; set; } = "";
    public string GregorianDate { get; set; } = "";
    public string Recipient { get; set; } = "";
public string IssuerName { get; set; } = "";
    public DateTime IssuedAt { get; set; } = DateTime.Now;
    /// <summary>ملاحظة كفاءة يدوية اختيارية (0-100) للعرض المستقل؛ لا تدخل في حساب مؤشرات التقرير.</summary>
    public double? FormEfficiencyIndex { get; set; }
}

public sealed class ReportPresentationContext
{
    public ReportIdentitySettingsModel Identity { get; set; } = new();
    public ReportIssueParametersModel Issue { get; set; } = new();
    public string FilterSummary { get; set; } = "";
    public string ApplicationName { get; set; } = "برنامج إدارة المذكرات";
}

public sealed class DetailedDashboardSnapshotModel
{
    public DetailedDashboardSummaryModel Summary { get; set; } = new();
    public List<DashboardChartPointModel> NotesByCompany { get; set; } = [];
    public List<DashboardChartPointModel> MonthlyNotes { get; set; } = [];
    public List<DashboardChartPointModel> NotesByStatus { get; set; } = [];
    public List<DashboardChartPointModel> DuplicatesByType { get; set; } = [];
    public List<DetailedDashboardRecentNoteModel> RecentNotes { get; set; } = [];
}

public sealed class DetailedDashboardSummaryModel
{
    public int TotalNotes { get; set; }
    public int TodayNotes { get; set; }
    public int CurrentMonthNotes { get; set; }
    public int ActiveCompanies { get; set; }
    public int ApprovedNotes { get; set; }
    public int PendingReviewNotes { get; set; }
    public int ReturnedNotes { get; set; }
    public int CompanyResendNotes { get; set; }
    public int DuplicateNumberNotes { get; set; }
    public int LateNotes { get; set; }
    public int LateSameMonthNotes { get; set; }
    public int LateCrossMonthNotes { get; set; }
    public int OnTimeNotes { get; set; }
    public int InvalidDateOrderNotes { get; set; }
    public decimal AverageLateDays { get; set; }
    public int MaximumLateDays { get; set; }
    public DateTime? LatestArrivalAt { get; set; }
    public int TotalFieldCount { get; set; }
    public int CorrectFieldCount { get; set; }
    public int ErrorFieldCount { get; set; }
    public int UnknownFieldCount { get; set; }
    public decimal FieldAccuracyPercentage { get; set; }
}

public sealed class DashboardChartPointModel
{
    public string Label { get; set; } = "";
    public int Value { get; set; }
}

public sealed class DetailedDashboardRecentNoteModel
{
    public string NoteNumber { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string NoteTypeName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime ArrivalAt { get; set; }
}

public sealed class AuditLogModel
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string ActionType { get; set; } = "";
    public string? SchemaName { get; set; }
    public string? TableName { get; set; }
    public Guid? RecordId { get; set; }
    public string? Description { get; set; }
    public Guid? NoteId { get; set; }
    public string? NoteNumber { get; set; }
    public string? CompanyName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AuditLogFilterModel
{
    public DateOnly From { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddMonths(-1));
    public DateOnly To { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public Guid? UserId { get; set; }
    public string? ActionType { get; set; }
    public DuplicateClassification? DuplicateClassification { get; set; }
    public Guid? CompanyId { get; set; }
    public string? NoteNumber { get; set; }
    public string? DescriptionSearch { get; set; }
    public bool DuplicateAttemptsOnly { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}


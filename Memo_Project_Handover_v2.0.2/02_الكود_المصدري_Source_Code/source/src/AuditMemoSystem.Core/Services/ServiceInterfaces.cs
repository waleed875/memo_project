using System.Data;
using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;

namespace AuditMemoSystem.Core.Services;

public interface IAuthService
{
    UserModel? CurrentUser { get; }
    Task<UserModel?> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
    Task EnsureBootstrapAdminHashAsync(CancellationToken cancellationToken = default);
    Task<bool> IsBootstrapAdminPendingAsync(CancellationToken cancellationToken = default);
    Task SetBootstrapAdminPasswordAsync(string password, CancellationToken cancellationToken = default);
    Task ChangeOwnPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    void InvalidateSession();
    bool HasRole(params UserRole[] roles);
}

public interface IUserService
{
    Task<IReadOnlyList<UserModel>> GetUsersAsync(Guid requesterId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RoleModel>> GetActiveRolesAsync(Guid requesterId, CancellationToken cancellationToken = default);
    Task SaveUserAsync(UserModel user, string? newPassword, Guid requesterId, CancellationToken cancellationToken = default);
    Task ResetUserPasswordAsync(Guid userId, string newPassword, Guid requesterId, CancellationToken cancellationToken = default);
}

public interface ICompanyService
{
    Task<IReadOnlyList<CompanyModel>> GetCompaniesAsync(CancellationToken cancellationToken = default);
    Task<CompanyModel?> GetCompanyAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveCompanyAsync(CompanyModel company, Guid userId, CancellationToken cancellationToken = default);
    Task DeleteCompanyAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}

public interface IFactoryService
{
    Task<IReadOnlyList<FactoryModel>> GetFactoriesAsync(Guid? companyId = null, CancellationToken cancellationToken = default);
    Task SaveFactoryAsync(FactoryModel factory, Guid userId, CancellationToken cancellationToken = default);
    Task DeleteFactoryAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}

public interface IProductionLineService
{
    Task<IReadOnlyList<ProductionLineModel>> GetProductionLinesAsync(Guid? factoryId = null, CancellationToken cancellationToken = default);
    Task SaveProductionLineAsync(ProductionLineModel line, Guid userId, CancellationToken cancellationToken = default);
    Task ToggleProductionLineAsync(Guid id, bool isActive, Guid userId, CancellationToken cancellationToken = default);
Task DeleteProductionLineAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}

public interface INoteTypeService
{
    Task<IReadOnlyList<NoteTypeModel>> GetNoteTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NoteTypeFieldModel>> GetFieldsForTypeAsync(Guid noteTypeId, CancellationToken cancellationToken = default);
    Task<NoteTypeFieldImpactPreviewModel> PreviewFieldDefinitionAsync(NoteTypeFieldDefinitionModel definition, Guid requesterId, CancellationToken cancellationToken = default);
    Task<NoteTypeFieldDefinitionModel> SaveFieldDefinitionAsync(NoteTypeFieldDefinitionModel definition, Guid requesterId, CancellationToken cancellationToken = default);
    Task DeactivateFieldDefinitionAsync(Guid definitionId, Guid requesterId, CancellationToken cancellationToken = default);
}

public interface INoteService
{
    Task<PagedResult<NoteDetailsModel>> SearchNotesAsync(NoteSearchFilterModel filter, Guid requesterId, CancellationToken cancellationToken = default);
    Task<PagedResult<IncompleteIndicatorQueueItemModel>> SearchIncompleteIndicatorsAsync(IncompleteIndicatorFilterModel filter, Guid requesterId, CancellationToken cancellationToken = default);
    Task CompleteUnreviewedFieldAsync(Guid noteId, Guid fieldId, IndicatorValue value, DateTime expectedUpdatedAt, Guid reviewerId, string? reason, CancellationToken cancellationToken = default);
    Task<NoteModel?> GetNoteAsync(Guid id, Guid requesterId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewQueueItemModel>> GetReviewQueueAsync(Guid reviewerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NoteDuplicateModel>> FindDuplicateNotesAsync(Guid? currentNoteId, string noteNumber, Guid noteTypeId, Guid companyId, DateOnly? writingDate, Guid requesterId, CancellationToken cancellationToken = default, bool isInvoice = false);
    Task<DuplicateAttachmentModel?> GetDuplicateAttachmentAsync(Guid noteId, string noteNumber, Guid noteTypeId, Guid companyId, DateOnly? writingDate, Guid requesterId, CancellationToken cancellationToken = default);
    Task<DuplicateResolutionResult> ResolveDuplicateAsync(NoteModel note, Guid userId, bool saveAsDraft, DuplicateResolutionRequest resolution, Guid? relatedNoteId = null, CancellationToken cancellationToken = default, bool markAllIndicatorsCorrect = false, bool synchronizeRelatedNote = false, Guid? operationId = null);
    Task<IReadOnlyList<NoteLinkCandidateModel>> SearchLinkCandidatesAsync(Guid? currentNoteId, Guid companyId, Guid noteTypeId, string? noteNumberSearch, Guid requesterId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NoteLinkCandidateModel>> GetLinkCandidatesAsync(Guid noteId, Guid requesterId, bool includeUnlinked = false, CancellationToken cancellationToken = default);
    Task LinkNotesAsync(Guid noteId, Guid relatedNoteId, Guid userId, CancellationToken cancellationToken = default);
    Task LinkNotesAsync(Guid noteId, IReadOnlyList<Guid> relatedNoteIds, Guid userId, CancellationToken cancellationToken = default);
    Task UnlinkNotesAsync(Guid noteId, Guid relatedNoteId, Guid userId, CancellationToken cancellationToken = default);
    Task<NoteSourceModel?> GetNoteSourceAsync(Guid noteId, Guid requesterId, CancellationToken cancellationToken = default);
    Task SaveNoteSourceAsync(NoteSourceModel source, Guid requesterId, CancellationToken cancellationToken = default);
    Task RemoveNoteSourceAsync(Guid noteId, Guid requesterId, CancellationToken cancellationToken = default);
    Task StartReviewAsync(Guid id, Guid reviewerId, DateTime? expectedUpdatedAt = null, CancellationToken cancellationToken = default);
    Task ReturnForCorrectionAsync(Guid id, Guid reviewerId, string reason, DateTime? expectedUpdatedAt = null, CancellationToken cancellationToken = default);
    Task ReleaseReviewAsync(Guid id, Guid adminUserId, string reason, CancellationToken cancellationToken = default);
    Task<Guid> SaveNoteAsync(NoteModel note, Guid userId, bool saveAsDraft = false, Guid? relatedNoteId = null, DuplicateResolutionRequest? duplicateResolution = null, CancellationToken cancellationToken = default, bool markAllIndicatorsCorrect = false, bool synchronizeRelatedNote = false, Guid? operationId = null, IReadOnlyList<Guid>? relatedNoteIds = null);
    Task DeleteNoteAsync(Guid id, Guid userId, DateTime expectedUpdatedAt, CancellationToken cancellationToken = default);
    Task ApproveNoteAsync(Guid id, Guid userId, DateTime? expectedUpdatedAt = null, CancellationToken cancellationToken = default);
}

public interface IReportService
{
    Task<DataTable> GetReportTableAsync(string reportName, ReportFilterModel filter, Guid requesterId, CancellationToken cancellationToken = default);
    Task<DataSet> GetMemoLogExportAsync(ReportFilterModel filter, Guid requesterId, CancellationToken cancellationToken = default);
    Task<DetailedDashboardSnapshotModel> GetDashboardSnapshotAsync(ReportFilterModel filter, Guid requesterId, CancellationToken cancellationToken = default);
    Task<DetailedDashboardSnapshotModel> GetDetailedDashboardSnapshotAsync(Guid requesterId, CancellationToken cancellationToken = default);
    Task<ReportIdentitySettingsModel> GetReportIdentitySettingsAsync(CancellationToken cancellationToken = default);
    Task SaveReportIdentitySettingsAsync(ReportIdentitySettingsModel settings, Guid requesterId, CancellationToken cancellationToken = default);
}

public interface ISystemSettingsService
{
    Task<ServerAddressSettingsModel> GetServerAddressSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveServerAddressSettingsAsync(ServerAddressSettingsModel settings, Guid requesterId, CancellationToken cancellationToken = default);
    Task<WebDashboardSettingsModel> GetWebDashboardSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveWebDashboardSettingsAsync(WebDashboardSettingsModel settings, Guid requesterId, CancellationToken cancellationToken = default);
    Task<UpdateServiceSettingsModel> GetUpdateServiceSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveUpdateServiceSettingsAsync(UpdateServiceSettingsModel settings, Guid requesterId, CancellationToken cancellationToken = default);
}
public interface IAuditService
{
    Task WriteAsync(Guid? userId, string actionType, string? schemaName, string? tableName, Guid? recordId, string? description, CancellationToken cancellationToken = default);
    Task<PagedResult<AuditLogModel>> SearchLogsAsync(AuditLogFilterModel filter, Guid requesterId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetActionTypesAsync(Guid requesterId, CancellationToken cancellationToken = default);
}

public interface IBackupService
{
    Task<string> BackupServerToFileAsync(ConnectionProfileModel serverProfile, string backupDirectory, Guid userId, CancellationToken cancellationToken = default);
    Task RestoreFileToLocalAsync(ConnectionProfileModel localProfile, string backupFile, Guid userId, CancellationToken cancellationToken = default);
    Task<string> CopyServerToLocalAsync(ConnectionProfileModel serverProfile, ConnectionProfileModel localProfile, string backupDirectory, Guid userId, CancellationToken cancellationToken = default);
}

public interface ISystemModeService
{
    AppMode CurrentMode { get; }
    Task<AppMode> LoadModeAsync(CancellationToken cancellationToken = default);
    Task SwitchModeAsync(AppMode mode, Guid requesterId, CancellationToken cancellationToken = default);
    void EnsureWritable();
}

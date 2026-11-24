using AuditMemoSystem.Core.Enums;
using AuditMemoSystem.Core.Models;

namespace AuditMemoSystem.Core.Notes;

/// <summary>
/// Builds a clean model for the next data-entry memo. Only catalog/date context
/// is eligible for retention; document content and workflow state never flow on.
/// </summary>
public static class NextNoteRetentionPolicy
{
    public static NoteModel PrepareNext(NoteModel source, bool retainSafeContext)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new NoteModel
        {
            CompanyId = retainSafeContext ? source.CompanyId : Guid.Empty,
            FactoryId = retainSafeContext ? source.FactoryId : null,
            ProductionLineId = retainSafeContext ? source.ProductionLineId : null,
            NoteTypeId = retainSafeContext ? source.NoteTypeId : Guid.Empty,
            WritingDate = retainSafeContext ? source.WritingDate : null,
            ArrivalAt = retainSafeContext ? source.ArrivalAt : default,
            FieldValues = retainSafeContext
                ? source.FieldValues.Select(value => new NoteFieldValueModel
                {
                    FieldId = value.FieldId,
                    FieldName = value.FieldName,
                    FieldValue = IndicatorValue.Unreviewed
                }).ToList()
                : []
        };
    }
}

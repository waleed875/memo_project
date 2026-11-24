namespace AuditMemoSystem.Core.Enums;

public enum UserRole
{
    ADMIN,
    DATA_ENTRY,
    REVIEWER,
    VIEWER
}

public enum NoteStatus
{
    DRAFT,
    COMPLETE,
    HAS_ERRORS,
    NEEDS_REVIEW,
    UNDER_REVIEW,
    RETURNED_FOR_CORRECTION,
    APPROVED
}

public enum IndicatorValue : short
{
    Unreviewed = 0,
    Correct = 1,
    Incorrect = 2,
    Missing = 3,
    Unclear = 4,
    Incomplete = 5,
    NotApplicable = 6
}

public enum AppMode
{
    SERVER,
    LOCAL_READONLY
}

public enum ConnectionProfileType
{
    SERVER,
    LOCAL
}

public enum DuplicateClassification
{
    COMPANY_DUPLICATE_NUMBER,
    COMPANY_RESEND,
    DATA_ENTRY_DUPLICATE_ATTEMPT
}

public enum NoteDelayClassification
{
    ON_TIME,
    LATE_SAME_MONTH,
    LATE_CROSS_MONTH,
    MISSING_WRITING_DATE,
    INVALID_DATES
}

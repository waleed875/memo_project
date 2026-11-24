namespace AuditMemoSystem.Core.Models;

public enum NoteDelayState
{
    CannotCalculate,
    WithinAllowedPeriod,
    Late,
    InvalidDateOrder
}

public sealed record NoteDelayAssessment(NoteDelayState State, int? Days)
{
    public bool CanCalculate => State is NoteDelayState.WithinAllowedPeriod or NoteDelayState.Late;
    public bool IsLate => State == NoteDelayState.Late;

    public string DisplayText => State switch
    {
        NoteDelayState.CannotCalculate => "غير قابل للاحتساب بسبب تاريخ مفقود",
        NoteDelayState.InvalidDateOrder => "غير قابل للاحتساب بسبب ترتيب التواريخ",
        NoteDelayState.Late => $"متأخرة ({Days} يوم)",
        _ => $"ضمن المهلة ({Days} يوم)"
    };
}
